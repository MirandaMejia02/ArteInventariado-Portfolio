using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Updater;

/// <summary>
/// Creates the PRE_UPDATE package after the machine-wide updater has crossed
/// UAC. It deliberately writes only below UpdateState/Recovery and never
/// uses the commercial ProtectedBackups destination.
/// </summary>
public interface IMachineWidePreUpdateBackupCreator
{
    Task<BackupResult> CreateAsync(
        UpdateRuntimePaths paths,
        string sourceVersion,
        string targetVersion,
        CancellationToken cancellationToken = default);
}

public sealed class MachineWidePreUpdateBackupCreator : IMachineWidePreUpdateBackupCreator
{
    private readonly BackupPackageEngine _engine;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _isAdministrator;

    public MachineWidePreUpdateBackupCreator()
        : this(new BackupPackageEngine(), TimeProvider.System, null)
    {
    }

    public MachineWidePreUpdateBackupCreator(Func<bool>? isAdministrator)
        : this(new BackupPackageEngine(), TimeProvider.System, isAdministrator)
    {
    }

    internal MachineWidePreUpdateBackupCreator(
        BackupPackageEngine? engine = null,
        TimeProvider? timeProvider = null,
        Func<bool>? isAdministrator = null)
    {
        _engine = engine ?? new BackupPackageEngine();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _isAdministrator = isAdministrator ?? MachineWideElevation.IsAdministrator;
    }

    public async Task<BackupResult> CreateAsync(
        UpdateRuntimePaths paths,
        string sourceVersion,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);
        if (paths.PathProvider?.IsMachineWide != true)
        {
            throw new InvalidOperationException("PRE_UPDATE machine-wide requiere el layout protegido.");
        }

        if (paths.PathProvider?.IsMachineWide == true && !_isAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El updater machine-wide debe ejecutarse elevado. Autorice UAC desde ArteInventariado.");
        }
        MachineWideUpdateGuard.ValidateManagedRoots(paths);
        var station = new LocalStationConfigurationStore(paths.StationSettingsPath).Load().Status;
        if (!station.IsConfigured || !station.InstallationId.HasValue || !station.Mode.HasValue)
        {
            throw new InvalidOperationException("La estación no está configurada para crear PRE_UPDATE.");
        }

        Directory.CreateDirectory(paths.RecoveryDirectory);
        var backupId = Guid.NewGuid();
        var staging = Path.Combine(paths.RecoveryDirectory, $".pre-update-{backupId:N}");
        var finalPackagePath = (string?)null;
        var finalManifestPath = (string?)null;
        Directory.CreateDirectory(staging);
        try
        {
            var migrations = await GetCurrentMigrationsAsync(paths.DatabasePath, cancellationToken);
            var snapshot = Path.Combine(staging, BackupPackageEngine.DatabaseEntryName);
            await _engine.CreateSnapshotAsync(paths.DatabasePath, snapshot, cancellationToken);
            var errors = await _engine.ValidateDatabaseAsync(
                snapshot,
                migrations,
                // PRE_UPDATE must be possible before the staged installer
                // migrates its isolated copy of the database. The validator
                // still rejects future or incompatible migration histories.
                allowOlderSchema: true,
                cancellationToken);
            if (errors.Count > 0)
            {
                throw new InvalidDataException(
                    $"El snapshot no superó las validaciones. {string.Join(" ", errors)}");
            }

            var productAssets = await _engine.ReadProductAssetsAsync(snapshot, cancellationToken);
            var legacyAssets = await _engine.CopyAssetsToStagingAsync(productAssets, staging, cancellationToken);
            var managedSources = await _engine.ReadManagedAssetsAsync(
                snapshot,
                paths.ManagedAssetsDirectory,
                cancellationToken);
            var managedAssets = await _engine.CopyManagedAssetsToStagingAsync(
                managedSources,
                staging,
                cancellationToken);
            var assets = legacyAssets.Concat(managedAssets).ToList();
            var warnings = assets
                .Where(asset => asset.WasMissing)
                .Select(asset => $"La imagen del producto {asset.ProductId} ya no existía en su ruta original.")
                .ToList();
            var databaseHash = await BackupPackageEngine.ComputeSha256Async(snapshot, cancellationToken);
            var appliedMigrations = await _engine.ReadAppliedMigrationsAsync(snapshot, cancellationToken);
            var now = _timeProvider.GetUtcNow();
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                backupId,
                now,
                now.ToLocalTime(),
                sourceVersion,
                "SQLite",
                appliedMigrations,
                appliedMigrations.LastOrDefault(),
                databaseHash,
                new FileInfo(snapshot).Length,
                station.InstallationId.Value,
                station.Mode.Value,
                BackupKind.PreUpdate,
                assets);

            var name = $"PRE_UPDATE_{SafeVersionPart(sourceVersion)}_TO_{SafeVersionPart(targetVersion)}_{backupId:N}.aibak";
            var stagedPackagePath = Path.Combine(staging, name);
            await _engine.CreatePackageAsync(staging, snapshot, manifest, stagedPackagePath, cancellationToken);

            var validationDirectory = Path.Combine(staging, "validation");
            var validation = await _engine.ValidatePackageAsync(
                stagedPackagePath,
                migrations,
                validationDirectory,
                cancellationToken);
            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidDataException(
                    $"El PRE_UPDATE no superó la validación final. {string.Join(" ", validation.Errors)}");
            }

            finalPackagePath = Path.Combine(paths.RecoveryDirectory, name);
            finalManifestPath = Path.Combine(paths.RecoveryDirectory, $"{name}.manifest.json");
            if (File.Exists(finalPackagePath) || File.Exists(finalManifestPath))
            {
                throw new IOException("Ya existe un PRE_UPDATE con el mismo identificador.");
            }

            await CopyFileAsync(stagedPackagePath, finalPackagePath, cancellationToken);
            await CopyFileAsync(
                Path.Combine(staging, BackupPackageEngine.ManifestEntryName),
                finalManifestPath,
                cancellationToken);
            var finalValidation = await _engine.ValidatePackageAsync(
                finalPackagePath,
                migrations,
                Path.Combine(staging, "promoted-validation"),
                cancellationToken);
            if (!finalValidation.IsValid ||
                finalValidation.Manifest is null ||
                finalValidation.Manifest.BackupId != manifest.BackupId ||
                finalValidation.Manifest.Kind != BackupKind.PreUpdate ||
                finalValidation.Manifest.SourceInstallationId != manifest.SourceInstallationId)
            {
                throw new InvalidDataException(
                    $"El PRE_UPDATE promovido no superó la validación final. {string.Join(" ", finalValidation.Errors)}");
            }

            return new BackupResult(backupId, finalPackagePath, manifest, warnings);
        }
        catch
        {
            TryDelete(finalPackagePath);
            TryDelete(finalManifestPath);
            throw;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static async Task<IReadOnlyList<string>> GetCurrentMigrationsAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection.ToString())
            .Options;
        await using var context = new AppDbContext(options);
        cancellationToken.ThrowIfCancellationRequested();
        return context.Database.GetMigrations().ToList();
    }

    private static string SafeVersionPart(string version) =>
        string.IsNullOrWhiteSpace(version)
            ? "unknown"
            : new string(version.Select(character =>
                char.IsLetterOrDigit(character) || character is '.' or '-' ? character : '_').ToArray());

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
