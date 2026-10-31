using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateBackupService
{
    private readonly BackupPackageEngine _engine;

    public UpdateBackupService() : this(new BackupPackageEngine())
    {
    }

    internal UpdateBackupService(BackupPackageEngine engine)
    {
        _engine = engine;
    }

    public async Task<BackupManifest> ValidatePreUpdateAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var root = CreateStagingDirectory();
        try
        {
            return (await StageAndValidateAsync(packagePath, root, cancellationToken)).Manifest;
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    public async Task RestorePreUpdateAsync(
        string packagePath,
        string activeDatabasePath,
        string businessAssetsDirectory,
        string managedAssetsDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = CreateStagingDirectory();
        var assetReplacements = new List<FileReplacement>();
        string? databaseBackup = null;
        var databaseWasCreated = false;
        var databaseReplaced = false;

        try
        {
            var staged = await StageAndValidateAsync(packagePath, root, cancellationToken);
            await RewriteLegacyAssetPathsAsync(
                staged.DatabasePath,
                staged.Manifest,
                businessAssetsDirectory,
                cancellationToken);

            var replacementPlan = staged.Manifest.Assets
                .Where(item => !item.WasMissing)
                .GroupBy(item => item.PackagePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(asset => new
                {
                    Source = staged.AssetPaths[NormalizeEntryName(asset.PackagePath)],
                    Target = ResolveAssetTarget(asset, businessAssetsDirectory, managedAssetsDirectory)
                })
                .ToList();

            foreach (var item in replacementPlan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                assetReplacements.Add(await ReplaceFileAtomicallyAsync(item.Source, item.Target, cancellationToken));
            }

            var databaseTarget = Path.GetFullPath(activeDatabasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(databaseTarget)!);
            databaseBackup = databaseTarget + $".pre-update-{Guid.NewGuid():N}.bak";
            SqliteConnection.ClearAllPools();
            DeleteSidecars(databaseTarget);
            if (File.Exists(databaseTarget))
            {
                File.Replace(staged.DatabasePath, databaseTarget, databaseBackup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(staged.DatabasePath, databaseTarget);
                databaseWasCreated = true;
            }

            databaseReplaced = true;
            DeleteSidecars(databaseTarget);
            TryDeleteFile(databaseBackup);
            foreach (var replacement in assetReplacements)
            {
                TryDeleteFile(replacement.BackupPath);
            }
        }
        catch
        {
            if (databaseReplaced)
            {
                TryRestoreReplacement(
                    Path.GetFullPath(activeDatabasePath),
                    databaseBackup,
                    databaseWasCreated);
            }

            for (var index = assetReplacements.Count - 1; index >= 0; index--)
            {
                TryRestoreReplacement(
                    assetReplacements[index].TargetPath,
                    assetReplacements[index].BackupPath,
                    assetReplacements[index].WasCreated);
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private async Task<StagedPreUpdateBackup> StageAndValidateAsync(
        string packagePath,
        string root,
        CancellationToken cancellationToken)
    {
        var knownMigrations = GetKnownMigrations();
        var validation = await _engine.ValidatePackageAsync(
            Path.GetFullPath(packagePath),
            knownMigrations,
            root,
            cancellationToken);
        if (!validation.IsValid || validation.Manifest is null)
        {
            throw new InvalidDataException(
                $"El PRE_UPDATE no supero el motor de validacion existente. {string.Join(" ", validation.Errors)}");
        }

        if (validation.Manifest.Kind != BackupKind.PreUpdate ||
            validation.Manifest.SourceInstallationId == Guid.Empty)
        {
            throw new InvalidDataException("El respaldo no es un PRE_UPDATE compatible.");
        }

        var databasePath = SafeCombine(root, BackupPackageEngine.DatabaseEntryName);
        var assets = validation.Manifest.Assets
            .Where(asset => !asset.WasMissing)
            .GroupBy(asset => NormalizeEntryName(asset.PackagePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SafeCombine(root, group.Key),
                StringComparer.OrdinalIgnoreCase);
        return new StagedPreUpdateBackup(validation.Manifest, databasePath, assets);
    }

    private static IReadOnlyList<string> GetKnownMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new AppDbContext(options);
        return context.Database.GetMigrations().ToList();
    }

    private static async Task RewriteLegacyAssetPathsAsync(
        string databasePath,
        BackupManifest manifest,
        string businessAssetsDirectory,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var asset in manifest.Assets.Where(asset => asset.OwnerKind is null))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE productos SET imagen_local_path = $path WHERE producto_id = $id;";
            command.Parameters.AddWithValue(
                "$path",
                asset.WasMissing ? DBNull.Value : ResolveAssetTarget(asset, businessAssetsDirectory, string.Empty));
            command.Parameters.AddWithValue("$id", asset.ProductId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string ResolveAssetTarget(
        BackupAssetManifest asset,
        string businessAssetsDirectory,
        string managedAssetsDirectory)
    {
        if (asset.OwnerKind is null)
        {
            var extension = Path.GetExtension(asset.PackagePath).ToLowerInvariant();
            return SafeCombine(businessAssetsDirectory, $"{asset.Sha256.ToLowerInvariant()}{extension}");
        }

        return SafeCombine(
            managedAssetsDirectory,
            asset.ManagedRelativePath ?? throw new InvalidDataException("Falta la ruta administrada del asset."));
    }

    private static async Task<FileReplacement> ReplaceFileAtomicallyAsync(
        string source,
        string target,
        CancellationToken cancellationToken)
    {
        var fullTarget = Path.GetFullPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
        var temporary = fullTarget + $".{Guid.NewGuid():N}.restore";
        var existed = File.Exists(fullTarget);
        var backup = existed ? fullTarget + $".pre-update-{Guid.NewGuid():N}.bak" : null;
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            if (existed)
            {
                File.Replace(temporary, fullTarget, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, fullTarget);
            }

            return new FileReplacement(fullTarget, backup, !existed);
        }
        finally
        {
            DeleteIfExists(temporary);
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La ruta queda fuera de su directorio permitido.");
        }

        return full;
    }

    private static string NormalizeEntryName(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string CreateStagingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryRestoreReplacement(string target, string? backup, bool wasCreated)
    {
        try
        {
            if (wasCreated)
            {
                DeleteIfExists(target);
            }
            else if (!string.IsNullOrWhiteSpace(backup) && File.Exists(backup))
            {
                if (File.Exists(target))
                {
                    File.Replace(backup, target, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(backup, target);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteSidecars(string path)
    {
        DeleteIfExists(path + "-wal");
        DeleteIfExists(path + "-shm");
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            DeleteIfExists(path);
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

    private sealed record StagedPreUpdateBackup(
        BackupManifest Manifest,
        string DatabasePath,
        IReadOnlyDictionary<string, string> AssetPaths);

    private sealed record FileReplacement(string TargetPath, string? BackupPath, bool WasCreated);
}
