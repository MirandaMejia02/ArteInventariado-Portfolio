using System.Diagnostics;
using System.IO.Compression;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateInstaller
{
    private const string ApplicationExecutableName = "ArteInventariado.WinForms.exe";
    private readonly UpdatePackageValidator _validator;
    private readonly UpdateStateStore _stateStore;
    private readonly UpdateBackupService _backupService;
    private readonly UpdateRuntimePaths _paths;
    private readonly IUpdateApplicationVerifier _verifier;
    private readonly UpdatePointerStore _pointerStore;
    private readonly UpdateStationGuard _stationGuard;
    private readonly UpdateEventLog _eventLog;
    private readonly TimeProvider _timeProvider;
    private readonly IUpdateDiskSpaceProbe _diskSpace;
    private readonly UpdateLaunchTargetResolver _launchTargetResolver;
    private readonly IStableDeploymentFinalizer _stableDeploymentFinalizer;
    private readonly Func<bool> _isAdministrator;

    public UpdateInstaller(
        UpdatePackageValidator validator,
        UpdateStateStore stateStore,
        UpdateBackupService backupService,
        UpdateRuntimePaths? paths = null,
        IUpdateApplicationVerifier? verifier = null,
        UpdatePointerStore? pointerStore = null,
        UpdateStationGuard? stationGuard = null,
        UpdateEventLog? eventLog = null,
        TimeProvider? timeProvider = null,
        IUpdateDiskSpaceProbe? diskSpace = null,
        UpdateLaunchTargetResolver? launchTargetResolver = null,
        IStableDeploymentFinalizer? stableDeploymentFinalizer = null,
        Func<bool>? isAdministrator = null)
    {
        _validator = validator;
        _stateStore = stateStore;
        _backupService = backupService;
        _paths = paths ?? UpdateRuntimePaths.CreateDefault();
        _verifier = verifier ?? new ProcessUpdateApplicationVerifier();
        _pointerStore = pointerStore ?? new UpdatePointerStore(_paths.CurrentPointerPath);
        _stationGuard = stationGuard ?? new UpdateStationGuard();
        _eventLog = eventLog ?? new UpdateEventLog(_paths.LogDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _diskSpace = diskSpace ?? new DriveUpdateDiskSpaceProbe();
        _launchTargetResolver = launchTargetResolver ?? new UpdateLaunchTargetResolver(_paths);
        _stableDeploymentFinalizer = stableDeploymentFinalizer ?? NullStableDeploymentFinalizer.Instance;
        _isAdministrator = isAdministrator ?? MachineWideElevation.IsAdministrator;
    }

    public async Task<int> InstallFromStateAsync(CancellationToken cancellationToken = default)
    {
        // Validate the managed state path before reading a document supplied
        // by a non-elevated caller.  The document is validated again after it
        // is loaded, immediately before any update operation.
        EnsureMachineWideAuthorization(null);
        var state = _stateStore.Load()
            ?? throw new InvalidOperationException("No existe una actualizacion preparada.");
        EnsureMachineWideAuthorization(state);
        if (state.State != UpdateState.ReadyToInstall)
        {
            throw new InvalidOperationException($"La actualizacion no esta lista para instalar: {state.State}.");
        }

        var targetDirectory = _paths.GetVersionDirectory(state.TargetVersion);
        var targetExecutable = Path.Combine(targetDirectory, ApplicationExecutableName);
        var stagingDirectory = Path.Combine(_paths.VersionsDirectory, $".staging-{Guid.NewGuid():N}");
        string? previousExecutable = null;

        try
        {
            var validation = await ValidatePreparedStateAsync(state, cancellationToken);
            EnsureDiskSpace(validation.ExpandedSizeBytes ?? new FileInfo(state.PackagePath).Length);
            previousExecutable = ResolvePreviousExecutable(state);
            state = state with
            {
                State = UpdateState.Installing,
                PreviousVersionPath = previousExecutable,
                CurrentVersionPath = targetExecutable,
                LastError = null,
                DatabaseMayHaveChanged = false,
                UpdatedUtc = Now()
            };
            _stateStore.Save(state);
            SafeLog("INSTALL_STARTED", "Inicio de extraccion side-by-side.", state.TargetVersion);

            Directory.CreateDirectory(_paths.VersionsDirectory);
            if (Directory.Exists(targetDirectory))
            {
                throw new IOException("La version destino ya existe. Ejecute la recuperacion antes de reintentar.");
            }

            Directory.CreateDirectory(stagingDirectory);
            await RevalidatePreparedPackageImmediatelyBeforeExtractionAsync(
                state,
                validation,
                cancellationToken);
            await ExtractPackageAsync(state.PackagePath, stagingDirectory, cancellationToken);
            var stagedExecutable = Path.Combine(stagingDirectory, ApplicationExecutableName);
            EnsureExecutableVersion(stagedExecutable, state.TargetVersion);
            Directory.Move(stagingDirectory, targetDirectory);

            state = state with
            {
                State = UpdateState.Verifying,
                CurrentVersionPath = targetExecutable,
                DatabaseMayHaveChanged = true,
                UpdatedUtc = Now()
            };
            _stateStore.Save(state);
            SafeLog("VERIFY_STARTED", "La nueva version inicia su verificacion de arranque y esquema.", state.TargetVersion);

            if (!await _verifier.VerifyAsync(targetExecutable, _paths, cancellationToken))
            {
                throw new InvalidOperationException("La nueva version no supero la verificacion de arranque o migracion.");
            }

            await _stationGuard.EnsurePreservedAsync(state, _paths.StationSettingsPath, cancellationToken);
            await _stableDeploymentFinalizer.FinalizeAsync(targetDirectory, cancellationToken);
            _pointerStore.Save(state.TargetVersion, targetExecutable, previousExecutable, Now());
            var success = state with
            {
                State = UpdateState.Success,
                PreviousVersionPath = previousExecutable,
                CurrentVersionPath = targetExecutable,
                LastError = null,
                UpdatedUtc = Now()
            };
            _stateStore.Save(success);
            _stationGuard.DeleteSnapshot(success);
            SafeLog("INSTALL_SUCCESS", "Version verificada y puntero current actualizado.", state.TargetVersion);
            return 0;
        }
        catch (Exception ex)
        {
            var failed = state with
            {
                State = UpdateState.RollbackRequired,
                PreviousVersionPath = previousExecutable ?? state.PreviousVersionPath,
                CurrentVersionPath = targetExecutable,
                LastError = ex.Message,
                UpdatedUtc = Now()
            };
            _stateStore.Save(failed);
            SafeLog("INSTALL_FAILED", ex.Message, state.TargetVersion);
            return await RollbackAsync(failed, ex.Message, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        EnsureMachineWideAuthorization(null);
        var state = _stateStore.Load();
        EnsureMachineWideAuthorization(state);
        if (state is null || state.State is UpdateState.Idle or UpdateState.ReadyToInstall or UpdateState.Success or UpdateState.Failed)
        {
            return 0;
        }

        SafeLog("RECOVERY_STARTED", $"Recuperacion desde estado {state.State}.", state.TargetVersion);
        if (state.State is UpdateState.Preparing or UpdateState.BackupCreated)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(state.StationSettingsBackupPath))
                {
                    await _stationGuard.RestoreAsync(state, _paths.StationSettingsPath, cancellationToken);
                    _stationGuard.DeleteSnapshot(state);
                }

                _pointerStore.Restore(state.SourceApplicationPath, Now());

                _stateStore.Save(state with
                {
                    State = UpdateState.Failed,
                    LastError = "La preparacion fue interrumpida antes de quedar lista.",
                    UpdatedUtc = Now()
                });
                SafeLog("RECOVERY_SUCCESS", "Preparacion incompleta cerrada de forma segura.", state.TargetVersion);
                return 0;
            }
            catch (Exception ex)
            {
                _stateStore.Save(state with
                {
                    State = UpdateState.RollbackRequired,
                    LastError = ex.Message,
                    UpdatedUtc = Now()
                });
                SafeLog("RECOVERY_FAILED", ex.Message, state.TargetVersion);
                return 3;
            }
        }

        var result = await RollbackAsync(
            state with { State = UpdateState.RollbackRequired },
            state.LastError ?? $"La actualizacion quedo interrumpida en {state.State}.",
            cancellationToken);
        return result == 2 ? 0 : result;
    }

    public async Task<int> RollbackSuccessfulAsync(CancellationToken cancellationToken = default)
    {
        EnsureMachineWideAuthorization(null);
        var state = _stateStore.Load()
            ?? throw new InvalidOperationException("No existe una actualizacion instalada para rollback.");
        EnsureMachineWideAuthorization(state);
        if (state.State != UpdateState.Success)
        {
            throw new InvalidOperationException($"El rollback manual requiere SUCCESS y el estado es {state.State}.");
        }

        try
        {
            var pointer = _pointerStore.Load()
                ?? throw new InvalidDataException("No existe CURRENT para ejecutar rollback.");
            var current = _launchTargetResolver.ResolveCurrent(pointer);
            var previous = _launchTargetResolver.ResolvePrevious(pointer)
                ?? throw new InvalidDataException("CURRENT no conserva una version previous administrada.");
            if (!string.Equals(current, Path.GetFullPath(state.CurrentVersionPath ?? string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("El estado SUCCESS y el puntero CURRENT no coinciden.");
            }

            await _stationGuard.EnsurePreservedAsync(state, _paths.StationSettingsPath, cancellationToken);
            var rollingBack = state with
            {
                State = UpdateState.RollingBack,
                LastError = "Rollback manual solicitado.",
                UpdatedUtc = Now()
            };
            _stateStore.Save(rollingBack);
            SafeLog("ROLLBACK_STARTED", "Rollback manual desde SUCCESS.", state.TargetVersion);

            if (state.DatabaseMayHaveChanged)
            {
                if (string.IsNullOrWhiteSpace(state.PreUpdateBackupPath))
                {
                    throw new InvalidDataException("No existe PRE_UPDATE para rollback manual.");
                }

                var manifest = await _backupService.ValidatePreUpdateAsync(state.PreUpdateBackupPath, cancellationToken);
                if (manifest.SourceInstallationId != state.InstallationId ||
                    manifest.SourceInstallationMode != state.StationMode)
                {
                    throw new InvalidDataException("El PRE_UPDATE no coincide con la estacion actual.");
                }

                await _backupService.RestorePreUpdateAsync(
                    state.PreUpdateBackupPath,
                    _paths.DatabasePath,
                    _paths.BusinessAssetsDirectory,
                    _paths.ManagedAssetsDirectory,
                    cancellationToken);
            }

            await _stationGuard.EnsurePreservedAsync(state, _paths.StationSettingsPath, cancellationToken);
            _pointerStore.Restore(previous, Now());
            DeleteInstalledTarget(current);
            _stateStore.Save(rollingBack with
            {
                State = UpdateState.Failed,
                CurrentVersionPath = previous,
                PreviousVersionPath = null,
                LastError = "Rollback manual completado; la version anterior vuelve a ser CURRENT.",
                DatabaseMayHaveChanged = false,
                UpdatedUtc = Now()
            });
            SafeLog("ROLLBACK_SUCCESS", "Rollback manual completo y previous promovido a CURRENT.", state.SourceVersion);
            return 0;
        }
        catch (Exception ex)
        {
            _stateStore.Save(state with
            {
                State = UpdateState.RollbackRequired,
                LastError = $"Rollback manual incompleto: {ex.Message}",
                UpdatedUtc = Now()
            });
            SafeLog("ROLLBACK_FAILED", ex.Message, state.SourceVersion);
            return 3;
        }
    }

    private async Task<UpdatePackageValidationResult> ValidatePreparedStateAsync(
        UpdateStateDocument state,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(
            state.ManifestPath,
            state.PackagePath,
            state.SourceVersion,
            cancellationToken,
            _paths.PathProvider?.IsMachineWide == true
                ? UpdatePackageTargets.MachineWide
                : null);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        }

        if (!string.Equals(validation.Manifest!.Version, state.TargetVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La version del manifest ya no coincide con el estado preparado.");
        }

        if (string.IsNullOrWhiteSpace(state.PreUpdateBackupPath))
        {
            throw new InvalidDataException("La actualizacion no tiene un respaldo PRE_UPDATE asociado.");
        }

        var backupManifest = await _backupService.ValidatePreUpdateAsync(state.PreUpdateBackupPath, cancellationToken);
        if (backupManifest.SourceInstallationId != state.InstallationId ||
            backupManifest.SourceInstallationMode != state.StationMode)
        {
            throw new InvalidDataException("El PRE_UPDATE pertenece a otra identidad o modo de estacion.");
        }

        await _stationGuard.EnsurePreservedAsync(state, _paths.StationSettingsPath, cancellationToken);
        return validation;
    }

    private async Task RevalidatePreparedPackageImmediatelyBeforeExtractionAsync(
        UpdateStateDocument state,
        UpdatePackageValidationResult initialValidation,
        CancellationToken cancellationToken)
    {
        var revalidation = await _validator.ValidateAsync(
            state.ManifestPath,
            state.PackagePath,
            state.SourceVersion,
            cancellationToken,
            _paths.PathProvider?.IsMachineWide == true
                ? UpdatePackageTargets.MachineWide
                : null);
        if (!revalidation.IsValid || revalidation.Manifest is null)
        {
            throw new InvalidDataException(
                "El paquete o el manifest cambiaron despues de la validacion inicial.");
        }

        var initialManifest = initialValidation.Manifest
            ?? throw new InvalidDataException("La validacion inicial no contiene manifest.");
        var manifest = revalidation.Manifest;
        if (!string.Equals(revalidation.PackagePath, initialValidation.PackagePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.PackagePath, initialManifest.PackagePath, StringComparison.Ordinal) ||
            !string.Equals(manifest.Sha256, initialManifest.Sha256, StringComparison.OrdinalIgnoreCase) ||
            manifest.SizeBytes != initialManifest.SizeBytes ||
            !string.Equals(manifest.Version, initialManifest.Version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "El paquete o el manifest cambiaron despues de la validacion inicial.");
        }
    }

    private void EnsureMachineWideAuthorization(UpdateStateDocument? state)
    {
        if (_paths.PathProvider?.IsMachineWide != true)
        {
            return;
        }

        if (!_isAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El updater machine-wide debe ejecutarse elevado antes de modificar instalación o UpdateState.");
        }

        MachineWideUpdateGuard.ValidateManagedRoots(_paths);

        var canonicalStatePath = _paths.PathProvider?.UpdateStatePath;
        if (canonicalStatePath is not null &&
            !string.Equals(
                Path.GetFullPath(_stateStore.Path),
                Path.GetFullPath(canonicalStatePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "El updater machine-wide no puede operar sobre un estado fuera de su layout administrado.");
        }

        if (state is not null)
        {
            MachineWideUpdateGuard.ValidateStateDocument(state, _paths);
        }
    }

    private async Task<int> RollbackAsync(
        UpdateStateDocument state,
        string originalError,
        CancellationToken cancellationToken)
    {
        try
        {
            var rollingBack = state with
            {
                State = UpdateState.RollingBack,
                LastError = originalError,
                UpdatedUtc = Now()
            };
            _stateStore.Save(rollingBack);
            SafeLog("ROLLBACK_STARTED", originalError, state.TargetVersion);

            if (state.DatabaseMayHaveChanged)
            {
                if (string.IsNullOrWhiteSpace(state.PreUpdateBackupPath))
                {
                    throw new InvalidDataException("No existe PRE_UPDATE para recuperar datos y assets.");
                }

                var manifest = await _backupService.ValidatePreUpdateAsync(state.PreUpdateBackupPath, cancellationToken);
                if (manifest.SourceInstallationId != state.InstallationId ||
                    manifest.SourceInstallationMode != state.StationMode)
                {
                    throw new InvalidDataException("El PRE_UPDATE no coincide con la estacion que debe recuperarse.");
                }

                await _backupService.RestorePreUpdateAsync(
                    state.PreUpdateBackupPath,
                    _paths.DatabasePath,
                    _paths.BusinessAssetsDirectory,
                    _paths.ManagedAssetsDirectory,
                    cancellationToken);
            }

            await _stationGuard.RestoreAsync(state, _paths.StationSettingsPath, cancellationToken);
            var previous = state.PreviousVersionPath ?? state.SourceApplicationPath
                ?? throw new FileNotFoundException("No existe un ejecutable previous para rollback.");
            _pointerStore.Restore(
                _launchTargetResolver.ValidateManagedExecutable(previous),
                Now());
            DeleteInstalledTarget(state.CurrentVersionPath);
            CleanupStagingDirectories();

            var failed = rollingBack with
            {
                State = UpdateState.Failed,
                LastError = $"{originalError} Rollback completado.",
                DatabaseMayHaveChanged = false,
                UpdatedUtc = Now()
            };
            _stateStore.Save(failed);
            _stationGuard.DeleteSnapshot(failed);
            SafeLog("ROLLBACK_SUCCESS", "DB, assets, estacion y puntero anterior recuperados.", state.SourceVersion);
            return 2;
        }
        catch (Exception rollbackError)
        {
            _stateStore.Save(state with
            {
                State = UpdateState.RollbackRequired,
                LastError = $"{originalError} Rollback: {rollbackError.Message}",
                UpdatedUtc = Now()
            });
            SafeLog("ROLLBACK_FAILED", rollbackError.Message, state.SourceVersion);
            return 3;
        }
    }

    private string? ResolvePreviousExecutable(UpdateStateDocument state)
    {
        var pointer = _pointerStore.Load();
        if (pointer is not null)
        {
            return _launchTargetResolver.ResolveCurrent(pointer);
        }

        if (!string.IsNullOrWhiteSpace(state.SourceApplicationPath))
        {
            return _launchTargetResolver.ValidateManagedExecutable(state.SourceApplicationPath);
        }

        throw new FileNotFoundException("No se encontro el ejecutable de la version anterior para rollback.", state.SourceApplicationPath);
    }

    private void EnsureDiskSpace(long expandedSize)
    {
        Directory.CreateDirectory(_paths.Root);
        var required = checked(expandedSize * 2 + 256L * 1024 * 1024);
        if (_diskSpace.GetAvailableBytes(_paths.Root) < required)
        {
            throw new IOException($"Espacio insuficiente para instalar y conservar rollback. Requerido: {required} bytes.");
        }
    }

    private static void EnsureExecutableVersion(string executablePath, string targetVersion)
    {
        if (!File.Exists(executablePath))
        {
            throw new InvalidDataException($"El paquete no contiene {ApplicationExecutableName}.");
        }

        var productVersion = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
        if (!UpdateVersion.TryParse(productVersion, out var executableVersion) ||
            executableVersion.CompareTo(UpdateVersion.Parse(targetVersion)) != 0)
        {
            throw new InvalidDataException(
                $"ProductVersion '{productVersion ?? "desconocida"}' no coincide con '{targetVersion}'.");
        }
    }

    private static async Task ExtractPackageAsync(
        string packagePath,
        string destination,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UpdatePackageValidator.IsUnsafeEntry(entry.FullName))
            {
                throw new InvalidDataException("El paquete contiene una ruta insegura.");
            }

            var target = SafeCombine(destination, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await source.CopyToAsync(file, cancellationToken);
        }
    }

    private void DeleteInstalledTarget(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (directory is null)
        {
            return;
        }

        var versionsRoot = Path.GetFullPath(_paths.VersionsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La version fallida queda fuera del directorio administrado.");
        }

        TryDeleteDirectory(directory);
    }

    private void CleanupStagingDirectories()
    {
        if (!Directory.Exists(_paths.VersionsDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_paths.VersionsDirectory, ".staging-*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La extraccion queda fuera del directorio destino.");
        }

        return full;
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

    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    private void SafeLog(string eventName, string description, string? version)
    {
        try
        {
            _eventLog.Write(eventName, description, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
