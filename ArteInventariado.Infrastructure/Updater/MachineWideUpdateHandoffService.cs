using System.Diagnostics;
using System.IO.Compression;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Updater;

/// <summary>
/// Consumes a per-user machine-wide update staging directory from the
/// elevated updater. The canonical update-state document is not written
/// until the package has been copied into Recovery and PRE_UPDATE exists.
/// </summary>
public sealed class MachineWideUpdateHandoffService
{
    private readonly UpdatePackageValidator _validator;
    private readonly IMachineWidePreUpdateBackupCreator _preUpdateCreator;
    private readonly UpdateStationGuard _stationGuard;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _isAdministrator;
    private readonly Func<UpdateRuntimePaths, UpdateLaunchTargetResolver> _resolverFactory;

    public MachineWideUpdateHandoffService(
        UpdatePackageValidator? validator = null,
        IMachineWidePreUpdateBackupCreator? preUpdateCreator = null,
        UpdateStationGuard? stationGuard = null,
        TimeProvider? timeProvider = null,
        Func<bool>? isAdministrator = null,
        Func<UpdateRuntimePaths, UpdateLaunchTargetResolver>? resolverFactory = null)
    {
        _validator = validator ?? new UpdatePackageValidator();
        _preUpdateCreator = preUpdateCreator ?? new MachineWidePreUpdateBackupCreator();
        _stationGuard = stationGuard ?? new UpdateStationGuard();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _isAdministrator = isAdministrator ?? MachineWideElevation.IsAdministrator;
        _resolverFactory = resolverFactory ?? (paths => new UpdateLaunchTargetResolver(paths));
    }

    public async Task<UpdateStateDocument> PrepareAsync(
        UpdateRuntimePaths paths,
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        if (paths.PathProvider?.IsMachineWide != true)
        {
            throw new InvalidOperationException("El handoff requiere el layout machine-wide.");
        }

        if (paths.PathProvider?.IsMachineWide == true && !_isAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El updater machine-wide debe ejecutarse elevado. Autorice UAC desde ArteInventariado.");
        }
        SafeLog(paths, "HANDOFF_STARTED", "Handoff elevado iniciado; se validará staging antes de crear PRE_UPDATE.", null);
        var diagnosticStage = "handoff-inicio";
        string? diagnosticPath = stagingDirectory;
        diagnosticStage = "handoff-validacion-raices";
        diagnosticPath = paths.MachineInstallRoot;
        MachineWideUpdateGuard.ValidateManagedRoots(paths);
        diagnosticStage = "handoff-validacion-staging";
        diagnosticPath = stagingDirectory;
        MachineWideUpdateGuard.ValidateHandoffPath(paths, stagingDirectory);

        var staging = Path.GetFullPath(stagingDirectory);
        diagnosticPath = staging;
        var manifestPath = Path.Combine(staging, MachineWideUpdateStagingService.ManifestFileName);
        diagnosticStage = "handoff-lectura-staging";
        var packagePath = ResolveSinglePackage(staging);
        ValidateStagingContents(staging, manifestPath, packagePath);
        EnsureStagingFile(manifestPath, "manifest");
        EnsureStagingFile(packagePath, "paquete");
        MachineWideUpdateGuard.ValidateHandoffPath(paths, staging);

        diagnosticStage = "handoff-lectura-current";
        diagnosticPath = paths.CurrentPointerPath;
        var pointerStore = new UpdatePointerStore(paths.CurrentPointerPath);
        var pointer = pointerStore.Load()
            ?? throw new InvalidDataException("No existe CURRENT para preparar una actualización machine-wide.");
        var resolver = _resolverFactory(paths);
        var sourceExecutable = resolver.ResolveCurrent(pointer);
        var sourceVersion = pointer.Version;
        diagnosticStage = "handoff-validacion-paquete";
        diagnosticPath = packagePath;
        var validation = await _validator.ValidateAsync(
            manifestPath,
            packagePath,
            sourceVersion,
            cancellationToken,
            UpdatePackageTargets.MachineWide);
        if (!validation.IsValid || validation.Manifest is null)
        {
            throw new InvalidDataException(
                "El staging no superó la validación elevada: " +
                string.Join(" ", validation.Errors));
        }

        var targetVersion = validation.Manifest.Version;
        SafeLog(paths, "HANDOFF_VALIDATED", "Staging validado y listo para crear PRE_UPDATE.", targetVersion);
        var transactionId = Guid.TryParseExact(new DirectoryInfo(staging).Name, "N", out var parsedTransactionId)
            ? parsedTransactionId
            : throw new InvalidDataException("El staging no tiene un identificador de transaccion valido.");
        var stateStore = new UpdateStateStore(paths.StatePath);
        var existing = stateStore.Load();
        if (existing is not null &&
            IsBlockingTransaction(existing, sourceVersion))
        {
            throw new InvalidOperationException(
                $"Existe una actualización en estado {existing.State}. Recupérela antes de preparar otra.");
        }

        var preUpdateDirectory = Path.Combine(
            paths.RecoveryDirectory,
            $"handoff-{Guid.NewGuid():N}");
        string? protectedPackage = null;
        string? protectedManifest = null;
        string? stationSnapshot = null;
        string? preUpdatePackage = null;
        var committed = false;
        try
        {
            diagnosticStage = "handoff-creacion-recovery";
            diagnosticPath = preUpdateDirectory;
            Directory.CreateDirectory(preUpdateDirectory);
            EnsureNoReparsePoint(preUpdateDirectory);
            protectedPackage = Path.Combine(preUpdateDirectory, Path.GetFileName(packagePath));
            protectedManifest = Path.Combine(preUpdateDirectory, MachineWideUpdateStagingService.ManifestFileName);
            diagnosticStage = "handoff-copia-paquete";
            diagnosticPath = protectedPackage;
            await CopyAtomicallyAsync(packagePath, protectedPackage, cancellationToken);
            diagnosticStage = "handoff-copia-manifest";
            diagnosticPath = protectedManifest;
            await CopyAtomicallyAsync(manifestPath, protectedManifest, cancellationToken);

            diagnosticStage = "handoff-validacion-protegida";
            diagnosticPath = protectedPackage;
            var protectedValidation = await _validator.ValidateAsync(
                protectedManifest,
                protectedPackage,
                sourceVersion,
                cancellationToken,
                UpdatePackageTargets.MachineWide);
            if (!protectedValidation.IsValid || protectedValidation.Manifest is null ||
                !string.Equals(
                    protectedValidation.Manifest.Sha256,
                    validation.Manifest.Sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                protectedValidation.Manifest.SizeBytes != validation.Manifest.SizeBytes)
            {
                throw new InvalidDataException(
                    "El paquete promovido desde staging cambió antes de entrar al flujo protegido.");
            }
            diagnosticStage = "handoff-validacion-version";
            diagnosticPath = protectedPackage;
            await ValidateProductVersionAsync(
                protectedPackage,
                protectedValidation.Manifest.Version,
                preUpdateDirectory,
                cancellationToken);

            diagnosticStage = "handoff-lectura-estacion";
            diagnosticPath = paths.StationSettingsPath;
            var station = new Station.LocalStationConfigurationStore(paths.StationSettingsPath).Load().Status;
            if (!station.IsConfigured || !station.InstallationId.HasValue || !station.Mode.HasValue)
            {
                throw new InvalidOperationException("La estación no está configurada para preparar una actualización.");
            }

            diagnosticStage = "handoff-preflight-operativo";
            diagnosticPath = paths.DatabasePath;
            await new UpdateOperationalPreflight().ValidateAsync(
                paths,
                station,
                cancellationToken);
            diagnosticStage = "handoff-snapshot-estacion";
            diagnosticPath = paths.StationSettingsPath;
            var snapshot = await _stationGuard.CaptureAsync(
                paths.StationSettingsPath,
                paths.UpdateStateRoot,
                targetVersion,
                cancellationToken);
            stationSnapshot = snapshot.BackupPath;

            diagnosticStage = "handoff-pre-update";
            diagnosticPath = paths.DatabasePath;
            var backup = await _preUpdateCreator.CreateAsync(
                paths,
                sourceVersion,
                targetVersion,
                cancellationToken);
            preUpdatePackage = backup.FilePath;
            if (backup.Manifest.SourceInstallationId != station.InstallationId ||
                backup.Manifest.SourceInstallationMode != station.Mode)
            {
                throw new InvalidDataException("El PRE_UPDATE no coincide con la identidad local de la estación.");
            }
            SafeLog(paths, "PRE_UPDATE_CREATED", $"PRE_UPDATE validado: {Path.GetFileName(preUpdatePackage)}.", targetVersion);

            var ready = new UpdateStateDocument(
                UpdateStateStore.CurrentSchemaVersion,
                UpdateState.ReadyToInstall,
                sourceVersion,
                targetVersion,
                protectedPackage,
                protectedManifest,
                preUpdatePackage,
                null,
                null,
                null,
                _timeProvider.GetUtcNow(),
                sourceExecutable,
                station.InstallationId,
                station.Mode,
                snapshot.Sha256,
                false,
                stationSnapshot,
                transactionId,
                UpdatePreparationStage.ReadyToInstall);
            diagnosticStage = "handoff-validacion-estado";
            diagnosticPath = paths.StatePath;
            MachineWideUpdateGuard.ValidateStateDocument(ready, paths);
            diagnosticStage = "handoff-escritura-estado";
            diagnosticPath = paths.StatePath;
            stateStore.Save(ready);
            committed = true;
            SafeLog(paths, "READY_TO_INSTALL", "PRE_UPDATE creado y estado ReadyToInstall persistido.", targetVersion);
            // The protected state is already committed at this point. A
            // user-owned staging directory is disposable; cleanup failure
            // must not turn a valid elevated handoff into a failed update.
            try
            {
                MachineWideUpdateStagingService.DeleteStagingDirectory(paths, staging);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }
            return ready;
        }
        catch (Exception ex)
        {
            SafeLog(paths, "HANDOFF_FAILED", ex.Message, null);
            MachineWideUpdateDiagnostics.Write(
                paths,
                diagnosticStage,
                ex,
                executablePath: paths.StableUpdaterPath,
                tokenElevated: MachineWideElevation.TryGetTokenElevation(out var elevated)
                    ? elevated
                    : null,
                relatedPath: diagnosticPath);
            throw;
        }
        finally
        {
            if (!committed)
            {
                TryDelete(preUpdatePackage);
                TryDelete(stationSnapshot);
                TryDelete(protectedPackage);
                TryDelete(protectedManifest);
                TryDeleteDirectory(preUpdateDirectory);
            }
        }
    }

    private static string ResolveSinglePackage(string staging)
    {
        var packages = Directory
            .EnumerateFiles(staging, "*.zip", SearchOption.TopDirectoryOnly)
            .ToArray();
        if (packages.Length != 1)
        {
            throw new InvalidDataException(
                "El staging debe contener exactamente un paquete ZIP.");
        }

        return Path.GetFullPath(packages[0]);
    }

    private static void ValidateStagingContents(
        string staging,
        string manifestPath,
        string packagePath)
    {
        var expectedManifest = Path.GetFullPath(manifestPath);
        var expectedPackage = Path.GetFullPath(packagePath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(staging, "*", SearchOption.TopDirectoryOnly))
        {
            var full = Path.GetFullPath(entry);
            if (!string.Equals(full, expectedManifest, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full, expectedPackage, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "El staging de actualización contiene archivos o carpetas no permitidos.");
            }

            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "El staging de actualización contiene un reparse point no permitido.");
            }
        }
    }

    internal static async Task ValidateProductVersionAsync(
        string packagePath,
        string expectedVersion,
        string protectedDirectory,
        CancellationToken cancellationToken)
    {
        var probePath = Path.Combine(
            protectedDirectory,
            $".product-version-{Guid.NewGuid():N}.exe");
        try
        {
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var entry = archive.GetEntry(UpdateLaunchTargetResolver.ApplicationExecutableName)
                    ?? throw new InvalidDataException("El paquete no contiene el ejecutable principal.");
                await using var input = entry.Open();
                await using var output = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            var productVersion = FileVersionInfo.GetVersionInfo(probePath).ProductVersion;
            if (!UpdateVersion.TryParse(productVersion, out var parsed) ||
                !UpdateVersion.TryParse(expectedVersion, out var expected) ||
                parsed.CompareTo(expected) != 0)
            {
                throw new InvalidDataException(
                    $"ProductVersion '{productVersion ?? "desconocida"}' no coincide con '{expectedVersion}'.");
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException(
                "El ejecutable del paquete no declara un ProductVersion compatible.",
                ex);
        }
        finally
        {
            TryDelete(probePath);
        }
    }

    private static void EnsureStagingFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No existe el {description} del staging.", path);
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"El {description} del staging no puede ser un reparse point.");
        }
    }

    private static void EnsureNoReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"El recovery contiene un reparse point no permitido: '{path}'.");
        }
    }

    private static async Task CopyAtomicallyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
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

    private static void SafeLog(
        UpdateRuntimePaths paths,
        string eventName,
        string description,
        string? version)
    {
        try
        {
            new UpdateEventLog(paths.LogDirectory).Write(eventName, description, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsBlockingTransaction(
        UpdateStateDocument document,
        string currentVersion)
    {
        if (document.State is UpdateState.Idle or UpdateState.Success or UpdateState.Failed)
        {
            return false;
        }

        return !(UpdateStateSemantics.IsRollback(document) &&
                 IsCurrentAtOrBeyondTarget(currentVersion, document) &&
                 !string.Equals(currentVersion, document.SourceVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCurrentAtOrBeyondTarget(
        string currentVersion,
        UpdateStateDocument document)
    {
        return UpdateVersion.TryParse(currentVersion, out var current) &&
               UpdateVersion.TryParse(document.TargetVersion, out var target) &&
               current.CompareTo(target) >= 0;
    }
}
