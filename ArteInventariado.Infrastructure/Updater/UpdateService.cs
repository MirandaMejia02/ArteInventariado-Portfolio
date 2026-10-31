using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateService : IUpdateService
{
    private readonly IBackupMaintenanceService _backup;
    private readonly IInstallationContext _installation;
    private readonly ICurrentSession _session;
    private readonly IPendingRestoreProcessor _pendingRestore;
    private readonly UpdatePackageValidator _validator;
    private readonly UpdateStateStore _stateStore;
    private readonly UpdateBackupService _updateBackup;
    private readonly UpdateStationGuard _stationGuard;
    private readonly UpdateRuntimePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly UpdateOperationalPreflight _preflight;
    private readonly UpdateApplicationInfo _applicationInfo;
    private readonly UpdateEventLog _eventLog;
    private readonly MachineWideUpdateStagingService _machineWideStaging;

    public UpdateService(
        IBackupMaintenanceService backup,
        IInstallationContext installation,
        ICurrentSession session,
        IPendingRestoreProcessor pendingRestore,
        UpdatePackageValidator validator,
        UpdateStateStore stateStore,
        UpdateBackupService updateBackup,
        UpdateStationGuard stationGuard,
        UpdateRuntimePaths paths,
        TimeProvider timeProvider,
        UpdateOperationalPreflight preflight,
        UpdateApplicationInfo applicationInfo,
        UpdateEventLog eventLog)
    {
        _backup = backup;
        _installation = installation;
        _session = session;
        _pendingRestore = pendingRestore;
        _validator = validator;
        _stateStore = stateStore;
        _updateBackup = updateBackup;
        _stationGuard = stationGuard;
        _paths = paths;
        _timeProvider = timeProvider;
        _preflight = preflight;
        _applicationInfo = applicationInfo;
        _eventLog = eventLog;
        _machineWideStaging = new MachineWideUpdateStagingService(_validator);
    }

    public async Task<UpdateStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = _applicationInfo.InstalledVersion;
        UpdateVersionPointer? current = null;
        try
        {
            current = new UpdatePointerStore(_paths.CurrentPointerPath).Load();
            if (current is not null && UpdateVersion.TryParse(current.Version, out _))
            {
                installed = current.Version;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            // The assembly version remains the safe fallback when CURRENT is
            // unavailable.  A malformed pointer is still surfaced below if
            // it is the only state that can explain the problem.
        }

        UpdateStateDocument? document = null;
        try
        {
            document = _stateStore.Load();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return new UpdateStatus(
                UpdateState.Failed,
                installed,
                null,
                null,
                null,
                null,
                ex.Message,
                UpdatePreparationStage.None,
                null,
                null,
                null,
                false);
        }

        // A completed or failed transaction is historical and must not mask
        // a newer valid per-user staging handoff.  Active transactions remain
        // authoritative: they must be recovered before another package can
        // be promoted.
        if (_paths.PathProvider?.IsMachineWide == true &&
            (document is null || !IsBlockingTransaction(document, installed)))
        {
            var staged = await FindStagedCandidateAsync(installed, cancellationToken);
            if (staged is not null)
            {
                var manifest = staged.Validation.Manifest!;
                return new UpdateStatus(
                    UpdateState.Preparing,
                    installed,
                    manifest.Version,
                    manifest.ReleaseNotes,
                    manifest.SizeBytes,
                    _timeProvider.GetUtcNow().ToString("O"),
                    null,
                    UpdatePreparationStage.Staged,
                    installed,
                    staged.TransactionId,
                    staged.StagingDirectory,
                    false);
            }
        }

        if (document is not null && IsCurrentTransaction(document, installed, current))
        {
            var stage = UpdateStateSemantics.PreparationStage(document);
            var canInstall = false;
            string? readinessError = null;
            if (document.State == UpdateState.ReadyToInstall &&
                stage == UpdatePreparationStage.ReadyToInstall)
            {
                (canInstall, readinessError) = await IsReadyToInstallAsync(document, cancellationToken);
            }

            return new UpdateStatus(
                document.State,
                installed,
                document.TargetVersion,
                TryReadReleaseNotes(document.ManifestPath),
                TryReadPackageSize(document.PackagePath),
                document.UpdatedUtc.ToString("O"),
                readinessError ?? document.LastError,
                stage,
                document.SourceVersion,
                document.TransactionId,
                null,
                canInstall);
        }

        return new UpdateStatus(
            UpdateState.Idle,
            installed,
            null,
            null,
            null,
            null,
            null,
            UpdatePreparationStage.None,
            null,
            null,
            null,
            false);
    }

    public async Task<UpdatePackageValidationResult> ValidatePackageAsync(
        string manifestPath,
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var result = await _validator.ValidateAsync(
            manifestPath,
            packagePath,
            _applicationInfo.InstalledVersion,
            cancellationToken,
            _paths.PathProvider?.IsMachineWide == true
                ? UpdatePackageTargets.MachineWide
                : null);
        SafeLog(
            result.IsValid ? "PACKAGE_VALIDATED" : "PACKAGE_REJECTED",
            result.IsValid ? "Manifest, compatibilidad, tamano, SHA-256 y ZIP validados." : string.Join(" ", result.Errors),
            result.Manifest?.Version);
        return result;
    }

    public async Task<UpdatePrepareResult> PrepareAsync(
        string manifestPath,
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        EnsureAdministrator();
        if (_paths.PathProvider?.IsMachineWide == true)
        {
            return await PrepareMachineWideStagingAsync(manifestPath, packagePath, cancellationToken);
        }

        if (!_installation.Status.IsConfigured ||
            !_installation.Status.InstallationId.HasValue ||
            !_installation.Status.Mode.HasValue)
        {
            throw new InvalidOperationException("La estacion debe estar configurada antes de preparar una actualizacion.");
        }

        var pending = _pendingRestore.GetStatus();
        if (pending.Exists)
        {
            throw new InvalidOperationException("Existe una restauracion pendiente. Resuelvala antes de actualizar.");
        }

        var existing = _stateStore.Load();
        if (existing is not null && IsBlockingTransaction(existing, _applicationInfo.InstalledVersion))
        {
            throw new InvalidOperationException(
                $"Existe una actualizacion en estado {existing.State}. Complete o recupere ese proceso antes de preparar otra.");
        }

        EnsureUpdateDirectoryWritable();
        await _preflight.ValidateAsync(_paths, _installation.Status, cancellationToken);
        if (!File.Exists(_applicationInfo.ExecutablePath))
        {
            throw new FileNotFoundException("No se encontro el ejecutable actual para rollback.", _applicationInfo.ExecutablePath);
        }

        if (!File.Exists(_applicationInfo.UpdaterExecutablePath))
        {
            throw new FileNotFoundException("La instalacion publicada no contiene ArteInventariado.Updater.exe.", _applicationInfo.UpdaterExecutablePath);
        }

        SafeLog(
            "PREPARE_STARTED",
            "Preparación iniciada; se validará el paquete antes de crear PRE_UPDATE.",
            null);
        var validation = await ValidatePackageAsync(manifestPath, packagePath, cancellationToken);
        if (!validation.IsValid)
        {
            return new(false, UpdateState.Failed, string.Join(" ", validation.Errors), null, validation);
        }

        var manifest = validation.Manifest!;
        var source = _applicationInfo.InstalledVersion;
        var stationSnapshot = await _stationGuard.CaptureAsync(
            _installation.Status.ConfigurationPath,
            _paths.Root,
            manifest.Version,
            cancellationToken);
        var initial = NewState(
            UpdateState.Preparing,
            source,
            manifest.Version,
            packagePath,
            manifestPath,
            stationSnapshot.BackupPath,
            stationSnapshot.Sha256);
        _stateStore.Save(initial);
        SafeLog("PREPARE_STARTED", "Preflight operacional e identidad local validados.", manifest.Version);

        try
        {
            var backup = await _backup.CreatePreUpdateBackupAsync(
                manifest.Version,
                cancellationToken: cancellationToken);
            var backupManifest = await _updateBackup.ValidatePreUpdateAsync(backup.FilePath, cancellationToken);
            if (backupManifest.SourceInstallationId != initial.InstallationId ||
                backupManifest.SourceInstallationMode != initial.StationMode)
            {
                throw new InvalidDataException("El PRE_UPDATE creado no coincide con la identidad local.");
            }

            var backupCreated = initial with
            {
                State = UpdateState.BackupCreated,
                PreparationStage = UpdatePreparationStage.PreUpdateCreated,
                PreUpdateBackupPath = backup.FilePath,
                UpdatedUtc = Now()
            };
            _stateStore.Save(backupCreated);
            SafeLog("PRE_UPDATE_CREATED", $"PRE_UPDATE validado: {Path.GetFileName(backup.FilePath)}.", manifest.Version);
            var ready = backupCreated with
            {
                State = UpdateState.ReadyToInstall,
                PreparationStage = UpdatePreparationStage.ReadyToInstall,
                UpdatedUtc = Now()
            };
            _stateStore.Save(ready);
            SafeLog("READY_TO_INSTALL", "Paquete listo para el updater separado.", manifest.Version);
            return new(
                true,
                UpdateState.ReadyToInstall,
                "Paquete validado, identidad protegida y respaldo PRE_UPDATE verificado. Puede instalarlo.",
                backup.FilePath,
                validation,
                null,
                UpdatePreparationStage.ReadyToInstall,
                source,
                manifest.Version,
                initial.TransactionId);
        }
        catch (Exception ex)
        {
            var failed = initial with
            {
                State = UpdateState.Failed,
                LastError = ex.Message,
                UpdatedUtc = Now()
            };
            _stateStore.Save(failed);
            _stationGuard.DeleteSnapshot(failed);
            SafeLog("PREPARE_FAILED", ex.Message, manifest.Version);
            throw;
        }
    }

    private async Task<UpdatePrepareResult> PrepareMachineWideStagingAsync(
        string manifestPath,
        string packagePath,
        CancellationToken cancellationToken)
    {
        if (!_installation.Status.IsConfigured ||
            !_installation.Status.InstallationId.HasValue ||
            !_installation.Status.Mode.HasValue)
        {
            throw new InvalidOperationException("La estacion debe estar configurada antes de preparar una actualizacion.");
        }

        if (_pendingRestore.GetStatus().Exists)
        {
            throw new InvalidOperationException("Existe una restauracion pendiente. Resuelvala antes de actualizar.");
        }

        var existing = _stateStore.Load();
        if (existing is not null && IsBlockingTransaction(existing, _applicationInfo.InstalledVersion))
        {
            throw new InvalidOperationException(
                $"Existe una actualizacion en estado {existing.State}. Complete o recupere ese proceso antes de preparar otra.");
        }

        await _preflight.ValidateAsync(_paths, _installation.Status, cancellationToken);
        if (!File.Exists(_applicationInfo.ExecutablePath))
        {
            throw new FileNotFoundException("No se encontro el ejecutable actual para rollback.", _applicationInfo.ExecutablePath);
        }

        if (!File.Exists(_applicationInfo.UpdaterExecutablePath))
        {
            throw new FileNotFoundException("La instalacion publicada no contiene ArteInventariado.Updater.exe.", _applicationInfo.UpdaterExecutablePath);
        }

        SafeLog(
            "PREPARE_STARTED",
            "Preparación machine-wide iniciada; se validará el paquete antes de crear PRE_UPDATE.",
            null);
        var validation = await ValidatePackageAsync(manifestPath, packagePath, cancellationToken);
        if (!validation.IsValid)
        {
            return new(false, UpdateState.Failed, string.Join(" ", validation.Errors), null, validation);
        }

        var source = _applicationInfo.InstalledVersion;
        try
        {
            var staged = await _machineWideStaging.StageAsync(
                _paths,
                manifestPath,
                packagePath,
                source,
                cancellationToken);
            SafeLog(
                "MACHINE_WIDE_UPDATE_STAGED",
                $"Paquete validado y copiado a staging no privilegiado: {Path.GetFileName(staged.StagingDirectory)}. PRE_UPDATE aún no existe.",
                staged.Validation.Manifest?.Version);
            return new(
                true,
                UpdateState.Preparing,
                "Paquete validado y copiado a staging temporal. Falta autorizar la preparación protegida para crear PRE_UPDATE.",
                null,
                staged.Validation,
                staged.StagingDirectory,
                UpdatePreparationStage.Staged,
                source,
                staged.Validation.Manifest?.Version,
                staged.TransactionId);
        }
        catch (Exception ex)
        {
            SafeLog("PREPARE_FAILED", ex.Message, null);
            throw;
        }
    }

    public string GetUpdaterExecutablePath()
    {
        return _applicationInfo.UpdaterExecutablePath;
    }

    private UpdateStateDocument NewState(
        UpdateState state,
        string source,
        string target,
        string package,
        string manifest,
        string stationBackupPath,
        string stationHash) =>
        new(
            UpdateStateStore.CurrentSchemaVersion,
            state,
            source,
            target,
            Path.GetFullPath(package),
            Path.GetFullPath(manifest),
            null,
            null,
            null,
            null,
            Now(),
            Path.GetFullPath(_applicationInfo.ExecutablePath),
            _installation.Status.InstallationId,
            _installation.Status.Mode,
            stationHash,
            false,
            stationBackupPath,
            Guid.NewGuid(),
            UpdateStateSemantics.PreparationStage(state));

    private void EnsureUpdateDirectoryWritable()
    {
        Directory.CreateDirectory(_paths.Root);
        var probe = Path.Combine(_paths.Root, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "ok");
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }

    private void EnsureAdministrator()
    {
        if (!_session.EsAdmin)
        {
            throw new UnauthorizedAccessException("Solo un administrador puede preparar actualizaciones.");
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

    private static bool IsCurrentTransaction(
        UpdateStateDocument document,
        string installedVersion,
        UpdateVersionPointer? current)
    {
        if (UpdateStateSemantics.IsRollback(document) &&
            IsCurrentAtOrBeyondTarget(installedVersion, document) &&
            !string.Equals(installedVersion, document.SourceVersion, StringComparison.OrdinalIgnoreCase))
        {
            // A completed rollback is historical once CURRENT points at a
            // later valid installation.  It must not mask the new version.
            return false;
        }

        if (document.State == UpdateState.Success &&
            !string.Equals(installedVersion, document.TargetVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (document.State == UpdateState.Failed &&
            !UpdateStateSemantics.IsRollback(document) &&
            !string.Equals(installedVersion, document.SourceVersion, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(installedVersion, document.TargetVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (document.State is UpdateState.Preparing or UpdateState.BackupCreated or UpdateState.ReadyToInstall)
        {
            if (!string.Equals(installedVersion, document.SourceVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (document.State == UpdateState.Success && current is not null &&
            !string.Equals(
                Path.GetFullPath(document.CurrentVersionPath ?? string.Empty),
                Path.GetFullPath(current.ExecutablePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsBlockingTransaction(
        UpdateStateDocument document,
        string installedVersion)
    {
        if (document.State is UpdateState.Idle or UpdateState.Success or UpdateState.Failed)
        {
            return false;
        }

        return !(UpdateStateSemantics.IsRollback(document) &&
                 IsCurrentAtOrBeyondTarget(installedVersion, document) &&
                 !string.Equals(installedVersion, document.SourceVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCurrentAtOrBeyondTarget(
        string installedVersion,
        UpdateStateDocument document)
    {
        return UpdateVersion.TryParse(installedVersion, out var installed) &&
               UpdateVersion.TryParse(document.TargetVersion, out var target) &&
               installed.CompareTo(target) >= 0;
    }

    private async Task<(bool Valid, string? Error)> IsReadyToInstallAsync(
        UpdateStateDocument document,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.PreUpdateBackupPath) ||
            !File.Exists(document.PreUpdateBackupPath))
        {
            return (false, "No existe un PRE_UPDATE válido para instalar.");
        }

        try
        {
            var manifest = await _updateBackup.ValidatePreUpdateAsync(
                document.PreUpdateBackupPath,
                cancellationToken);
            if (manifest.SourceInstallationId != document.InstallationId ||
                manifest.SourceInstallationMode != document.StationMode)
            {
                return (false, "El PRE_UPDATE no coincide con la identidad de la estación.");
            }

            return (true, null);
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (false, $"El PRE_UPDATE no es válido: {ex.Message}");
        }
    }

    private async Task<MachineWideUpdateStagingResult?> FindStagedCandidateAsync(
        string installedVersion,
        CancellationToken cancellationToken)
    {
        if (_paths.PathProvider?.IsMachineWide != true ||
            !Directory.Exists(_paths.StagingRoot))
        {
            return null;
        }

        var directories = Directory.EnumerateDirectories(_paths.StagingRoot)
            .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path));
        foreach (var directory in directories)
        {
            try
            {
                MachineWideUpdateGuard.ValidateHandoffPath(_paths, directory);
                var manifest = Path.Combine(directory, MachineWideUpdateStagingService.ManifestFileName);
                var package = Directory.EnumerateFiles(directory, "*.zip", SearchOption.TopDirectoryOnly).SingleOrDefault();
                if (package is null || !File.Exists(manifest))
                {
                    continue;
                }

                var validation = await _validator.ValidateAsync(
                    manifest,
                    package,
                    installedVersion,
                    cancellationToken,
                    UpdatePackageTargets.MachineWide);
                if (!validation.IsValid || validation.Manifest is null)
                {
                    continue;
                }

                return new MachineWideUpdateStagingResult(
                    Path.GetFullPath(directory),
                    Path.GetFullPath(manifest),
                    Path.GetFullPath(package),
                    validation);
            }
            catch (Exception ex) when (
                ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            {
                // An untrusted or incomplete staging directory is never
                // promoted to a visible ReadyToInstall state.
            }
        }

        return null;
    }

    private static long? TryReadPackageSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryReadReleaseNotes(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(
                File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return manifest?.ReleaseNotes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
