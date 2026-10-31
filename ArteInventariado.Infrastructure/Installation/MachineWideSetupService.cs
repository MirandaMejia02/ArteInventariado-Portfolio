using System.Diagnostics;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Infrastructure.Installation;

public sealed record MachineWideSetupRequest(
    string PayloadDirectory,
    bool CleanInstall = false,
    bool ConfirmedCleanInstall = false,
    bool ProvisionPrincipal = true,
    bool StartLauncherAfterInstall = true,
    string? ExpectedVersion = null,
    ShortcutLocations? ShortcutLocations = null);

public sealed record MachineWideSetupResult(
    string Version,
    Guid InstallationId,
    bool CleanPerformed,
    bool DatabaseCreated,
    bool PrincipalProvisioned,
    bool StationConfiguredPrincipal,
    bool BackupTaskInstalled,
    bool CloudTaskInstalled,
    string LauncherPath,
    string DatabasePath);

public sealed record MachineWideUninstallResult(
    bool InstallationRemoved,
    bool DataRemoved,
    bool UserDataRemoved,
    IReadOnlyList<string> RemovedTasks);

public sealed class ExistingMachineWideInstallationException : InvalidOperationException
{
    public ExistingMachineWideInstallationException(string message)
        : base(message)
    {
    }
}

public sealed class CleanInstallConfirmationRequiredException : InvalidOperationException
{
    public CleanInstallConfirmationRequiredException()
        : base(MachineWideSetupService.CleanInstallConfirmationText + ". Escriba CONFIRMAR para continuar.")
    {
    }
}

/// <summary>
/// Official setup orchestration.  It reuses MachineWideInstallationService
/// for payload promotion, ACLs, shortcuts and SYSTEM tasks, then invokes the
/// existing database and principal-provisioning flows.  The normal updater is
/// deliberately not called from here.
/// </summary>
public sealed class MachineWideSetupService
{
    public const string CleanInstallConfirmationText =
        "Esta acción eliminará la instalación y DB local actuales";

    private readonly MachineWideInstallationService _installation;
    private MachineWideSetupDiagnostics? _activeDiagnostics;
    private string? _activeStage;

    public MachineWideSetupService()
    {
        _installation = new MachineWideInstallationService(
            new MachineWideBootstrapService(phaseObserver: ObservePhase),
            new MachineWideAclService(),
            ObservePhase);
    }

    public MachineWideSetupService(MachineWideInstallationService installation)
    {
        _installation = installation ?? throw new ArgumentNullException(nameof(installation));
    }

    private void ObservePhase(string phase)
    {
        _activeStage = phase;
        _activeDiagnostics?.Phase(phase);
    }

    public static bool IsCleanConfirmationValid(string? value) =>
        string.Equals(value?.Trim(), "CONFIRMAR", StringComparison.OrdinalIgnoreCase);

    public async Task<MachineWideSetupResult> InstallAsync(
        ArteInventariadoPathProvider target,
        MachineWideSetupRequest request,
        CancellationToken cancellationToken = default)
        => await InstallCoreAsync(target, request, allowSyntheticRoots: false, cancellationToken);

    // Test-only fixture entry point.  The production setup is deliberately
    // locked to the canonical machine-wide roots; this internal overload
    // lets the test suite exercise the complete orchestration in an isolated
    // temporary tree without touching Program Files or ProgramData.
    internal async Task<MachineWideSetupResult> InstallSyntheticAsync(
        ArteInventariadoPathProvider target,
        MachineWideSetupRequest request,
        CancellationToken cancellationToken = default)
        => await InstallCoreAsync(target, request, allowSyntheticRoots: true, cancellationToken);

    private async Task<MachineWideSetupResult> InstallCoreAsync(
        ArteInventariadoPathProvider target,
        MachineWideSetupRequest request,
        bool allowSyntheticRoots,
        CancellationToken cancellationToken)
    {
        var diagnostics = new MachineWideSetupDiagnostics();
        var stage = "entrada";
        var newInstallation = false;
        var cleanPerformed = false;
        var hadManagedState = false;
        string? payload = null;
        string? payloadVersion = null;
        _activeDiagnostics = diagnostics;
        _activeStage = stage;

        void SetStage(string value)
        {
            stage = value;
            _activeStage = value;
            diagnostics.Phase(value);
        }

        try
        {
            diagnostics.Started(
                target,
                Environment.ProcessPath,
                request?.PayloadDirectory,
                request?.CleanInstall == true);
            SetStage("validacion-argumentos");
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.PayloadDirectory))
            {
                throw new ArgumentException("Debe indicarse el directorio payload del instalador.", nameof(request));
            }

            SetStage("validacion-raices");
            MachineWideInstallCleanup.EnsureSafeRoots(target, allowSyntheticRoots);
            payload = Path.GetFullPath(request.PayloadDirectory);
            SetStage("payload-validating");
            EnsurePayloadRoot(payload);
            payloadVersion = ReadPayloadVersion(payload);
            SetStage("payload-version-validated");
            if (!string.IsNullOrWhiteSpace(request.ExpectedVersion) &&
                !string.Equals(payloadVersion, UpdateVersion.Sanitize(request.ExpectedVersion), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"El payload declara {payloadVersion}, pero el instalador esperaba {request.ExpectedVersion}.");
            }

            SetStage("managed-state-inspection");
            hadManagedState = allowSyntheticRoots
                ? MachineWideInstallCleanup.HasAnyManagedStateSynthetic(target)
                : MachineWideInstallCleanup.HasAnyManagedState(target);
            var hasInstallRoot = Directory.Exists(target.MachineInstallRoot) ||
                                 File.Exists(target.MachineInstallRoot);
            var currentVersion = TryReadCurrentVersion(target);
            if (hadManagedState && !request.CleanInstall)
            {
                // A normal uninstall deliberately removes only the executable
                // root and leaves ProgramData/LocalAppData intact.  In that
                // state there is no current pointer by design, so allow the
                // same setup package to rebuild the binaries and reuse the
                // existing database/station identity.  Any still-present
                // installation with a missing or different pointer remains a
                // guarded state and must go through Update Center or an
                // explicit clean install.
                var isDataOnlyAfterUninstall = !hasInstallRoot && currentVersion is null;
                if (!isDataOnlyAfterUninstall &&
                    !string.Equals(currentVersion, payloadVersion, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ExistingMachineWideInstallationException(
                        $"La instalación actual ({currentVersion ?? "desconocida"}) no se reemplaza con setup. " +
                        "Use el Update Center para actualizar o seleccione explícitamente la instalación limpia.");
                }
            }

            SetStage("clean-confirmation");
            if (request.CleanInstall)
            {
                if (!request.ConfirmedCleanInstall)
                {
                    throw new CleanInstallConfirmationRequiredException();
                }

                if (hadManagedState)
                {
                    SetStage("clean-removal");
                    MachineWideInstallCleanup.RemoveAllManagedState(target, allowSyntheticRoots);
                    cleanPerformed = true;
                }
            }

            newInstallation = !hadManagedState || cleanPerformed;
            SetStage("machine-wide-install");
            var installation = await _installation.InstallCleanAsync(
                target,
                payload!,
                request.ShortcutLocations,
                cancellationToken);

            SetStage("database-initialization");
            var database = allowSyntheticRoots
                ? await MachineWideDatabaseInitializer.InitializeSyntheticAsync(
                    target,
                    cancellationToken)
                : await MachineWideDatabaseInitializer.InitializeAsync(
                    target,
                    cancellationToken);
            var principalProvisioned = database.PrincipalExists;
            var stationConfiguredPrincipal = database.StationMode == InstallationMode.Principal;

            if (request.ProvisionPrincipal && !principalProvisioned)
            {
                SetStage("principal-provisioning");
                await ProvisionPrincipalInteractivelyAsync(target, payloadVersion!, cancellationToken);
                principalProvisioned = await MachineWideDatabaseInitializer.HasProtectedPrincipalAsync(
                    target,
                    cancellationToken);
                if (!principalProvisioned)
                {
                    throw new InvalidOperationException(
                        "El flujo de Administrador Principal terminó sin crear la cuenta protegida.");
                }
            }

            if (principalProvisioned && !stationConfiguredPrincipal)
            {
                SetStage("station-principal");
                var station = new LocalStationConfigurationStore(target).Load().Status;
                var installationId = station.InstallationId
                    ?? throw new InvalidDataException("La instalación no obtuvo una installationId válida.");
                MachineWideStationConfigurationHelper.WriteMode(
                    target,
                    InstallationMode.Principal,
                    installationId,
                    expectedCurrentMode: null);
                stationConfiguredPrincipal = true;
            }

            if (!principalProvisioned && request.ProvisionPrincipal)
            {
                throw new InvalidOperationException(
                    "La instalación necesita completar la provisión del Administrador Principal antes de quedar lista.");
            }

            if (request.StartLauncherAfterInstall && principalProvisioned)
            {
                SetStage("launcher-start");
                StartLauncherUnelevated(target);
            }

            diagnostics.Succeeded();
            return new MachineWideSetupResult(
                payloadVersion!,
                new LocalStationConfigurationStore(target).Load().Status.InstallationId
                    ?? throw new InvalidDataException("La instalación no conserva installationId."),
                cleanPerformed,
                database.DatabaseCreated,
                principalProvisioned,
                stationConfiguredPrincipal,
                File.Exists(target.BackupRunnerPath),
                File.Exists(target.CloudBackupRunnerPath),
                installation.LauncherPath ?? target.LauncherPath,
                target.DatabasePath);
        }
        catch (Exception exception)
        {
            var failedStage = _activeStage ?? stage;
            diagnostics.Failed(failedStage, exception);
            // After an explicit clean, or on a brand-new machine, no partial
            // installation is left behind.  A repair of an existing same-
            // version installation keeps its data for the next retry.
            if (newInstallation)
            {
                try
                {
                    MachineWideInstallCleanup.RemoveAllManagedState(target, allowSyntheticRoots);
                }
                catch (Exception cleanupException)
                {
                    diagnostics.CleanupFailed(cleanupException);
                    // Preserve the original failure; the caller receives a
                    // nonzero result and must inspect the protected roots.
                }
            }

            if (exception is CleanInstallConfirmationRequiredException or
                ExistingMachineWideInstallationException)
            {
                throw;
            }

            throw new MachineWideSetupFailureException(
                failedStage,
                diagnostics.LogPath,
                exception);
        }
        finally
        {
            _activeDiagnostics = null;
            _activeStage = null;
        }
    }

    public MachineWideUninstallResult Uninstall(
        ArteInventariadoPathProvider target,
        bool purgeData,
        bool confirmedCleanInstall)
    {
        ArgumentNullException.ThrowIfNull(target);
        MachineWideInstallCleanup.EnsureSafeRoots(target);
        if (purgeData && !confirmedCleanInstall)
        {
            throw new CleanInstallConfirmationRequiredException();
        }

        var result = purgeData
            ? MachineWideInstallCleanup.RemoveAllManagedState(target)
            : MachineWideInstallCleanup.RemoveInstallationOnly(target);
        return new MachineWideUninstallResult(
            result.InstallRootRemoved,
            result.DataRootRemoved,
            result.UserRootRemoved,
            result.RemovedTasks);
    }

    private static async Task ProvisionPrincipalInteractivelyAsync(
        ArteInventariadoPathProvider target,
        string version,
        CancellationToken cancellationToken)
    {
        var executable = Path.Combine(
            target.GetVersionDirectory(version),
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "No se encontró el ejecutable para abrir la configuración del Administrador Principal.",
                executable);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("--provision-principal");
        UpdateRuntimePaths.Create(target).AddLayoutArguments(startInfo);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo abrir la configuración del Administrador Principal.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"La configuración del Administrador Principal terminó con código {process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }
    }

    private static void StartLauncherUnelevated(ArteInventariadoPathProvider target)
    {
        if (!File.Exists(target.LauncherPath))
        {
            throw new FileNotFoundException("No se encontró el launcher estable de ArteInventariado.", target.LauncherPath);
        }

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(target.LauncherPath)!,
            Arguments = $"\"{target.LauncherPath}\""
        };
        _ = Process.Start(startInfo);
    }

    private static string ReadPayloadVersion(string payload)
    {
        var executable = Path.Combine(payload, UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "El paquete del instalador no contiene ArteInventariado.WinForms.exe.",
                executable);
        }

        var productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion;
        if (!UpdateVersion.TryParse(productVersion, out var version))
        {
            throw new InvalidDataException("El ejecutable del paquete no declara una ProductVersion válida.");
        }

        return version.ToString();
    }

    private static string? TryReadCurrentVersion(ArteInventariadoPathProvider target)
    {
        try
        {
            return new UpdatePointerStore(target.CurrentStatePath).Load()?.Version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static void EnsurePayloadRoot(string payload)
    {
        if (!Path.IsPathFullyQualified(payload) ||
            payload.StartsWith("\\\\", StringComparison.Ordinal) ||
            payload.StartsWith("//", StringComparison.Ordinal) ||
            payload.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("El payload debe estar en una ruta local absoluta.");
        }

        if (!Directory.Exists(payload))
        {
            throw new DirectoryNotFoundException($"No existe el directorio payload: '{payload}'.");
        }

        var current = payload;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"El payload contiene un reparse point: '{current}'.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }
}
