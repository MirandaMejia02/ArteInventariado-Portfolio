using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Infrastructure.Installation;

public sealed record MachineWideSetupUpdateResult(
    string SourceVersion,
    string TargetVersion,
    string PreUpdateBackupPath,
    int InstallerResult,
    UpdateState FinalState,
    string CurrentVersionPath);

/// <summary>
/// Applies the update bundled with a recovery Setup directly through the
/// existing protected updater pipeline. It never performs a clean install or
/// changes the business database except for the normal, verified migration
/// performed by the target application.
/// </summary>
public sealed class MachineWideSetupUpdateService
{
    private readonly Func<bool> _isAdministrator;

    public MachineWideSetupUpdateService()
        : this(MachineWideElevation.IsAdministrator)
    {
    }

    internal MachineWideSetupUpdateService(Func<bool> isAdministrator)
    {
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
    }

    public Task<MachineWideSetupUpdateResult> ApplyAsync(
        ArteInventariadoPathProvider paths,
        string manifestPath,
        string packagePath,
        string setupVersion,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(paths, manifestPath, packagePath, setupVersion, allowSyntheticRoots: false, cancellationToken);

    internal Task<MachineWideSetupUpdateResult> ApplySyntheticAsync(
        ArteInventariadoPathProvider paths,
        string manifestPath,
        string packagePath,
        string setupVersion,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(paths, manifestPath, packagePath, setupVersion, allowSyntheticRoots: true, cancellationToken);

    private async Task<MachineWideSetupUpdateResult> ApplyCoreAsync(
        ArteInventariadoPathProvider provider,
        string manifestPath,
        string packagePath,
        string setupVersion,
        bool allowSyntheticRoots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("La actualización protegida de Setup solo puede ejecutarse en Windows.");
        }

        if (!_isAdministrator())
        {
            throw new UnauthorizedAccessException("Setup requiere autorización de administrador para actualizar la instalación protegida.");
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(provider, allowSyntheticRoots);
        var paths = UpdateRuntimePaths.Create(provider);
        MachineWideUpdateGuard.ValidateManagedRoots(paths);
        MachineWideInstallCleanup.EnsureNoApplicationProcesses(Environment.ProcessId);

        using var maintenanceLease = MachineWideMaintenanceLease.Acquire(provider, allowSyntheticRoots);
        if (allowSyntheticRoots)
        {
            MachineWideDatabaseResetJournalStore.EnsureUpdaterMayProceedSynthetic(provider);
        }
        else
        {
            MachineWideDatabaseResetJournalStore.EnsureUpdaterMayProceed(provider);
        }

        var expectedTarget = UpdateVersion.Sanitize(setupVersion);
        if (!string.Equals(expectedTarget, setupVersion.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La versión declarada por Setup no es canónica.");
        }

        var pointerStore = new UpdatePointerStore(paths.CurrentPointerPath);
        var pointer = pointerStore.Load()
            ?? throw new InvalidOperationException("No existe una instalación machine-wide activa. Este paquete no puede usarse como instalación limpia.");
        var resolver = new UpdateLaunchTargetResolver(paths);
        var currentExecutable = resolver.ResolveCurrent(pointer);
        var sourceVersion = pointer.Version;
        var targetDirectory = paths.GetVersionDirectory(expectedTarget);
        if (Directory.Exists(targetDirectory))
        {
            throw new InvalidOperationException(
                $"Ya existe la carpeta de versión destino '{expectedTarget}'. No se borrará; primero hay que auditar o recuperar esa instalación parcial.");
        }

        var stateStore = new UpdateStateStore(paths.StatePath);
        var existingState = stateStore.Load();
        if (existingState is not null && existingState.State is not
            (UpdateState.Idle or UpdateState.Success or UpdateState.Failed))
        {
            throw new InvalidOperationException(
                $"Hay una transacción anterior en estado {existingState.State}. Setup no la sobrescribirá; requiere recuperación antes de iniciar otra.");
        }

        var validator = new UpdatePackageValidator();
        var validation = await validator.ValidateAsync(
            manifestPath,
            packagePath,
            sourceVersion,
            cancellationToken,
            UpdatePackageTargets.MachineWide);
        if (!validation.IsValid || validation.Manifest is null)
        {
            throw new InvalidDataException(
                "El paquete no pasó las validaciones previas y no se creó staging: " +
                string.Join(" ", validation.Errors));
        }

        var manifestVersion = UpdateVersion.Sanitize(validation.Manifest.Version);
        if (!string.Equals(manifestVersion, expectedTarget, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"La versión del paquete ({manifestVersion}) no coincide con la versión de este Setup ({expectedTarget}). No se creó staging.");
        }

        await MachineWideUpdateHandoffService.ValidateProductVersionAsync(
            packagePath,
            expectedTarget,
            paths.RecoveryDirectory,
            cancellationToken);

        var station = new LocalStationConfigurationStore(paths.StationSettingsPath).Load().Status;
        if (!station.IsConfigured || !station.InstallationId.HasValue || !station.Mode.HasValue)
        {
            throw new InvalidOperationException("La estación existente no está configurada; este Setup de recuperación no puede provisionarla ni cambiar su identidad.");
        }

        await new UpdateOperationalPreflight().ValidateAsync(paths, station, cancellationToken);
        SafeLog(paths, "SETUP_UPDATE_VALIDATED", "Setup validó canal, versión, ProductVersion, estación y preflight antes de crear staging.", expectedTarget);

        var staged = await new MachineWideUpdateStagingService(validator).StageAsync(
            paths,
            manifestPath,
            packagePath,
            sourceVersion,
            cancellationToken);

        UpdateStateDocument ready;
        try
        {
            ready = await new MachineWideUpdateHandoffService(
                validator: validator,
                isAdministrator: _isAdministrator,
                resolverFactory: runtimePaths => new UpdateLaunchTargetResolver(runtimePaths))
                .PrepareAsync(paths, staged.StagingDirectory, cancellationToken);
        }
        catch
        {
            // This exact GUID staging directory was created by this Setup run.
            // The handoff service intentionally preserves failed staging for
            // diagnostics, but the recovery Setup must not leave its own failed
            // attempt to masquerade as a fresh candidate in Update Center.
            try
            {
                MachineWideUpdateStagingService.DeleteStagingDirectory(paths, staged.StagingDirectory);
            }
            catch (Exception cleanupException) when (
                cleanupException is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SafeLog(paths, "SETUP_UPDATE_STAGING_CLEANUP_FAILED", cleanupException.Message, expectedTarget);
            }

            throw;
        }

        var installer = new UpdateInstaller(
            validator,
            stateStore,
            new UpdateBackupService(),
            paths,
            stableDeploymentFinalizer: new StableDeploymentFinalizer(
                new StableEntryPointInstaller(paths, new WindowsShortcutManager())),
            isAdministrator: _isAdministrator);
        var installerResult = await installer.InstallFromStateAsync(cancellationToken);
        var finalState = stateStore.Load()?.State
            ?? throw new InvalidDataException("El updater terminó sin dejar un estado verificable.");
        var finalPointer = pointerStore.Load();
        var finalExecutable = finalPointer?.ExecutablePath ?? currentExecutable;
        if (installerResult == 0 &&
            (finalState != UpdateState.Success ||
             !string.Equals(finalPointer?.Version, expectedTarget, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Setup recibió éxito, pero CURRENT no confirma la versión destino.");
        }

        return new MachineWideSetupUpdateResult(
            sourceVersion,
            expectedTarget,
            ready.PreUpdateBackupPath ?? throw new InvalidDataException("El handoff no dejó una ruta PRE_UPDATE."),
            installerResult,
            finalState,
            finalExecutable);
    }

    private static void SafeLog(UpdateRuntimePaths paths, string name, string description, string version)
    {
        try
        {
            new UpdateEventLog(paths.LogDirectory).Write(name, description, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
