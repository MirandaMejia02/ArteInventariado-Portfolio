using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Updater;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Station;
using System.Diagnostics;

namespace ArteInventariado.Infrastructure.Installation;

public sealed record MachineWideInstallationResult(
    MachineWideBootstrapResult Bootstrap,
    MachineWideAclResult Acl,
    bool ShortcutsEnsured,
    string? LauncherPath);

/// <summary>
/// Explicit, elevated orchestration for the machine-wide layout.  It is not
/// called by normal startup or by the updater; Block D remains responsible
/// for future elevated updates/rollback.
/// </summary>
public sealed class MachineWideInstallationService
{
    private readonly MachineWideBootstrapService _bootstrap;
    private readonly MachineWideAclService _acl;
    private readonly Func<bool> _isAdministrator;
    private readonly MachineWideBackupPolicyStore _backupPolicyStore;
    private readonly IMachineWideBackupTaskScheduler _backupTaskScheduler;
    private readonly IMachineWideCloudBackupTaskScheduler _cloudBackupTaskScheduler;
    private readonly Action<string>? _phaseObserver;
    private readonly Guid _runId;

    public MachineWideInstallationService(
        MachineWideBootstrapService bootstrap,
        MachineWideAclService acl)
        : this(
            bootstrap,
            acl,
            MachineWideElevation.IsAdministrator,
            new MachineWideBackupPolicyStore(),
            null,
            null,
            null,
            Guid.NewGuid())
    {
    }

    public MachineWideInstallationService(
        MachineWideBootstrapService bootstrap,
        MachineWideAclService acl,
        Guid runId)
        : this(
            bootstrap,
            acl,
            MachineWideElevation.IsAdministrator,
            new MachineWideBackupPolicyStore(),
            new MachineWideBackupTaskScheduler(runId),
            new MachineWideCloudBackupTaskScheduler(runId),
            null,
            runId)
    {
    }

    public MachineWideInstallationService(
        MachineWideBootstrapService bootstrap,
        MachineWideAclService acl,
        Action<string> phaseObserver)
        : this(
            bootstrap,
            acl,
            MachineWideElevation.IsAdministrator,
            new MachineWideBackupPolicyStore(),
            null,
            null,
            phaseObserver,
            Guid.NewGuid())
    {
    }

    public MachineWideInstallationService(
        MachineWideBootstrapService bootstrap,
        MachineWideAclService acl,
        Action<string> phaseObserver,
        Guid runId)
        : this(
            bootstrap,
            acl,
            MachineWideElevation.IsAdministrator,
            new MachineWideBackupPolicyStore(),
            new MachineWideBackupTaskScheduler(runId),
            new MachineWideCloudBackupTaskScheduler(runId),
            phaseObserver,
            runId)
    {
    }

    internal MachineWideInstallationService(
        MachineWideBootstrapService bootstrap,
        MachineWideAclService acl,
        Func<bool> isAdministrator,
        MachineWideBackupPolicyStore? backupPolicyStore = null,
        IMachineWideBackupTaskScheduler? backupTaskScheduler = null,
        IMachineWideCloudBackupTaskScheduler? cloudBackupTaskScheduler = null,
        Action<string>? phaseObserver = null,
        Guid? runId = null)
    {
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _acl = acl ?? throw new ArgumentNullException(nameof(acl));
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        _backupPolicyStore = backupPolicyStore ?? new MachineWideBackupPolicyStore();
        _runId = runId ?? Guid.NewGuid();
        _backupTaskScheduler = backupTaskScheduler ?? new MachineWideBackupTaskScheduler(_runId);
        _cloudBackupTaskScheduler = cloudBackupTaskScheduler ?? new MachineWideCloudBackupTaskScheduler(_runId);
        _phaseObserver = phaseObserver;
    }

    public async Task<MachineWideInstallationResult> InstallCleanAsync(
        ArteInventariadoPathProvider target,
        string? publishedPayloadDirectory = null,
        ShortcutLocations? shortcutLocations = null,
        CancellationToken cancellationToken = default)
    {
        Phase("installation-started");
        EnsureAdministrator();
        Phase("administrator-validated");
        EnsureTarget(target);
        Phase("target-validated");

        // Materialize the empty tree and protect it before copying the
        // payload.  A standard user never observes an unprotected promoted
        // installation, and a failed copy has no CURRENT pointer/marker.
        _acl.EnsureRuntimeDirectories(target);
        Phase("runtime-directories-ready");
        var policy = _backupPolicyStore.EnsureInitialized(target);
        Phase("backup-policy-ready");
        _ = await _bootstrap.PrepareCleanInstallAsync(
            target,
            cancellationToken: cancellationToken,
            initializeStation: false);
        Phase("bootstrap-roots-prepared");
        _acl.Apply(target);
        Phase("initial-acl-applied");

        var bootstrap = string.IsNullOrWhiteSpace(publishedPayloadDirectory)
            ? await _bootstrap.PrepareCleanInstallAsync(target, cancellationToken: cancellationToken)
            : await _bootstrap.PrepareCleanInstallAsync(
                target,
                publishedPayloadDirectory,
                cancellationToken);
        Phase("payload-bootstrap-complete");
        var acl = _acl.Apply(target);
        Phase("final-acl-applied");
        var shortcutState = EnsureShortcuts(target, shortcutLocations, out var launcherPath);
        var shortcuts = shortcutState.ReadyAfter;
        Phase("shortcuts-ready");
        var payloadRoot = string.IsNullOrWhiteSpace(publishedPayloadDirectory)
            ? null
            : Path.GetFullPath(publishedPayloadDirectory);
        var backupTask = EnsureBackupTask(target, policy, payloadRoot);
        if (backupTask.Required)
        {
            Phase("backup-task-ready");
        }

        var cloudTask = EnsureCloudBackupTask(target, payloadRoot);
        if (cloudTask.Required)
        {
            Phase("cloud-task-ready");
        }

        ValidateInstallationPostconditions(
            target,
            publishedPayloadDirectory,
            acl,
            shortcuts,
            backupTask,
            cloudTask);
        ValidateFinalTaskPostconditions(target, policy, backupTask, cloudTask);
        bootstrap = NormalizeAlreadyReadyOutcome(bootstrap, shortcutState, backupTask, cloudTask);
        Phase("installation-complete");
        return new MachineWideInstallationResult(bootstrap, acl, shortcuts, launcherPath);
    }

    public async Task<MachineWideInstallationResult> MigrateLegacyAsync(
        ArteInventariadoPathProvider legacy,
        ArteInventariadoPathProvider target,
        bool migrateValidBackups = true,
        IReadOnlyList<string>? currentMigrations = null,
        ShortcutLocations? shortcutLocations = null,
        CancellationToken cancellationToken = default,
        string? publishedPayloadDirectory = null)
    {
        Phase("migration-started");
        EnsureAdministrator();
        Phase("administrator-validated");
        ArgumentNullException.ThrowIfNull(legacy);
        EnsureTarget(target);
        Phase("target-validated");

        _acl.EnsureRuntimeDirectories(target);
        Phase("runtime-directories-ready");
        var policy = _backupPolicyStore.EnsureInitialized(target);
        Phase("backup-policy-ready");
        // Migration writes the verified identity and operational settings
        // from the legacy source. Do not create a default station core before
        // that atomic promotion, otherwise the target could be initialized
        // with a different identity/mode and block the migration.
        _ = await _bootstrap.PrepareCleanInstallAsync(
            target,
            cancellationToken: cancellationToken,
            initializeStation: false);
        Phase("bootstrap-roots-prepared");
        _acl.Apply(target);
        Phase("initial-acl-applied");

        var bootstrap = await _bootstrap.MigrateLegacyAsync(
            legacy,
            target,
            migrateValidBackups,
            currentMigrations,
            cancellationToken,
            publishedPayloadDirectory);
        Phase("legacy-migration-complete");
        var acl = _acl.Apply(target);
        Phase("final-acl-applied");
        var shortcutState = EnsureShortcuts(target, shortcutLocations, out var launcherPath);
        var shortcuts = shortcutState.ReadyAfter;
        Phase("shortcuts-ready");
        var payloadRoot = string.IsNullOrWhiteSpace(publishedPayloadDirectory)
            ? null
            : Path.GetFullPath(publishedPayloadDirectory);
        var backupTask = EnsureBackupTask(target, policy, payloadRoot);
        if (backupTask.Required)
        {
            Phase("backup-task-ready");
        }

        var cloudTask = EnsureCloudBackupTask(target, payloadRoot);
        if (cloudTask.Required)
        {
            Phase("cloud-task-ready");
        }

        ValidateInstallationPostconditions(
            target,
            publishedPayloadDirectory,
            acl,
            shortcuts,
            backupTask,
            cloudTask);
        ValidateFinalTaskPostconditions(target, policy, backupTask, cloudTask);
        bootstrap = NormalizeAlreadyReadyOutcome(bootstrap, shortcutState, backupTask, cloudTask);
        Phase("migration-complete");
        return new MachineWideInstallationResult(bootstrap, acl, shortcuts, launcherPath);
    }

    private TaskReadiness EnsureBackupTask(
        ArteInventariadoPathProvider target,
        MachineWideBackupPolicy policy,
        string? payloadRoot)
    {
        // A directory-only preparation (without a published payload) remains
        // useful for C1/C2 callers and has no runner to register yet. A real
        // machine-wide installation always publishes the runner and then
        // receives the protected task definition here.
        var required = File.Exists(target.BackupRunnerPath) ||
                       (payloadRoot is not null && File.Exists(Path.Combine(
                           payloadRoot,
                           ArteInventariadoPathProvider.BackupRunnerFileName)));
        if (!required)
        {
            return TaskReadiness.NotRequired;
        }

        var readyBefore = _backupTaskScheduler.IsRegisteredCorrectly(target, policy);
        _backupTaskScheduler.EnsureRegistered(target, policy);
        var readyAfter = _backupTaskScheduler.IsRegisteredCorrectly(target, policy);
        if (!readyAfter)
        {
            throw new InvalidOperationException(
                "La tarea protegida de respaldos no existe o no coincide con su contrato después del registro.");
        }

        return new TaskReadiness(true, readyBefore, readyAfter);
    }

    private TaskReadiness EnsureCloudBackupTask(
        ArteInventariadoPathProvider target,
        string? payloadRoot)
    {
        var required = File.Exists(target.CloudBackupRunnerPath) ||
                       (payloadRoot is not null && File.Exists(Path.Combine(
                           payloadRoot,
                           ArteInventariadoPathProvider.CloudBackupRunnerFileName)));
        if (!required)
        {
            return TaskReadiness.NotRequired;
        }

        var readyBefore = _cloudBackupTaskScheduler.IsRegisteredCorrectly(target);
        _cloudBackupTaskScheduler.EnsureRegistered(target);
        var readyAfter = _cloudBackupTaskScheduler.IsRegisteredCorrectly(target);
        if (!readyAfter)
        {
            throw new InvalidOperationException(
                "La tarea cloud protegida no existe o no coincide con su contrato después del registro.");
        }

        return new TaskReadiness(true, readyBefore, readyAfter);
    }

    private static MachineWideBootstrapResult NormalizeAlreadyReadyOutcome(
        MachineWideBootstrapResult bootstrap,
        ShortcutReadiness shortcuts,
        TaskReadiness backupTask,
        TaskReadiness cloudTask)
    {
        var tasksWereReady =
            (!backupTask.Required || backupTask.WasReadyBefore) &&
            (!cloudTask.Required || cloudTask.WasReadyBefore);
        var allPostconditionsWereReady =
            (!shortcuts.Required || shortcuts.WasReadyBefore) &&
            tasksWereReady;
        if (bootstrap.Outcome != MachineWideBootstrapOutcome.AlreadyReady || allPostconditionsWereReady)
        {
            return bootstrap;
        }

        var repairedOutcome = bootstrap.LegacyDetected
            ? MachineWideBootstrapOutcome.Migrated
            : MachineWideBootstrapOutcome.CleanInstallPrepared;
        return bootstrap with { Outcome = repairedOutcome };
    }

    private static void ValidateInstallationPostconditions(
        ArteInventariadoPathProvider target,
        string? publishedPayloadDirectory,
        MachineWideAclResult acl,
        bool shortcuts,
        TaskReadiness backupTask,
        TaskReadiness cloudTask)
    {
        var pointer = new UpdatePointerStore(target.CurrentStatePath).Load()
            ?? throw new InvalidDataException("La instalación no tiene current.json válido.");
        var currentVersion = UpdateVersion.Sanitize(pointer.Version);
        var resolver = new UpdateLaunchTargetResolver(UpdateRuntimePaths.Create(target));
        _ = resolver.ResolveCurrent(pointer);

        var versionDirectory = target.GetVersionDirectory(currentVersion);
        if (!Directory.Exists(versionDirectory))
        {
            throw new InvalidDataException("La versión current no está promovida bajo Versions.");
        }

        EnsureStableVersion(target.LauncherPath, currentVersion, required: true);
        EnsureStableVersion(target.UpdaterPath, currentVersion, required: true);
        foreach (var component in new[]
                 {
                     ArteInventariadoPathProvider.StationHelperFileName,
                     ArteInventariadoPathProvider.BackupRunnerFileName,
                     ArteInventariadoPathProvider.CloudBackupRunnerFileName
                 })
        {
            var stablePath = Path.Combine(target.MachineInstallRoot, component);
            var required = File.Exists(stablePath) ||
                           (!string.IsNullOrWhiteSpace(publishedPayloadDirectory) &&
                            File.Exists(Path.Combine(Path.GetFullPath(publishedPayloadDirectory), component)));
            EnsureStableVersion(stablePath, currentVersion, required);
        }

        var station = new LocalStationConfigurationStore(target).Load().Status;
        if (station.State is StationConfigurationState.Corrupt or
            StationConfigurationState.Invalid or
            StationConfigurationState.Unsupported ||
            station.InstallationId is null ||
            station.InstallationId == Guid.Empty)
        {
            throw new InvalidDataException("La configuración protegida de Station no es válida.");
        }

        if (acl.AppliedScopes is null || acl.AppliedScopes.Count == 0)
        {
            throw new InvalidDataException("No se aplicaron las ACL finales de la instalación.");
        }

        if (File.Exists(target.LauncherPath) && !shortcuts)
        {
            throw new InvalidDataException("No se pudieron asegurar los accesos directos machine-wide.");
        }

        if ((backupTask.Required && !backupTask.ReadyAfter) ||
            (cloudTask.Required && !cloudTask.ReadyAfter))
        {
            throw new InvalidDataException("Falta una tarea protegida obligatoria.");
        }
    }

    private void ValidateFinalTaskPostconditions(
        ArteInventariadoPathProvider target,
        MachineWideBackupPolicy policy,
        TaskReadiness backupTask,
        TaskReadiness cloudTask)
    {
        // Task Scheduler is queried again at the last possible moment. This
        // closes the gap where a task could disappear after the per-task
        // registration check but before the installer reports success.
        if (backupTask.Required)
        {
            Phase("backup-task-final-query");
            if (!_backupTaskScheduler.IsRegisteredCorrectly(target, policy))
            {
                throw new InvalidDataException(
                    "La tarea protegida de respaldos no persiste hasta la postcondición final.");
            }
        }

        if (cloudTask.Required)
        {
            Phase("cloud-task-final-query");
            if (!_cloudBackupTaskScheduler.IsRegisteredCorrectly(target))
            {
                throw new InvalidDataException(
                    "La tarea cloud protegida no persiste hasta la postcondición final.");
            }
        }
    }

    private static void EnsureStableVersion(string path, string expectedVersion, bool required)
    {
        if (!required)
        {
            return;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Falta un componente estable obligatorio.", path);
        }

        var actual = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        if (!UpdateVersion.TryParse(actual, out var parsed) ||
            parsed.CompareTo(UpdateVersion.Parse(expectedVersion)) != 0)
        {
            throw new InvalidDataException(
                $"El componente estable '{Path.GetFileName(path)}' no corresponde a current.");
        }
    }

    private sealed record ShortcutReadiness(
        bool Required,
        bool WasReadyBefore,
        bool ReadyAfter)
    {
        public static ShortcutReadiness NotRequired => new(false, true, true);
    }

    private sealed record TaskReadiness(
        bool Required,
        bool WasReadyBefore,
        bool ReadyAfter)
    {
        public static TaskReadiness NotRequired => new(false, true, true);
    }

    private static void EnsureTarget(ArteInventariadoPathProvider target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsMachineWide)
        {
            throw new ArgumentException("La instalacion protegida requiere el layout machine-wide.", nameof(target));
        }
    }

    private void EnsureAdministrator()
    {
        if (!_isAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El bootstrap machine-wide debe ejecutarse elevado. Use MachineWideElevation antes de escribir.");
        }
    }

    private void Phase(string name) => _phaseObserver?.Invoke(name);

    private static ShortcutReadiness EnsureShortcuts(
        ArteInventariadoPathProvider target,
        ShortcutLocations? shortcutLocations,
        out string? launcherPath)
    {
        launcherPath = target.LauncherPath;
        if (!File.Exists(launcherPath))
        {
            return ShortcutReadiness.NotRequired;
        }

        var runtime = UpdateRuntimePaths.Create(target);
        var locations = shortcutLocations ?? ShortcutLocations.CreateMachineWideDefault();
        var manager = new WindowsShortcutManager(locations, runtime.GetMachineWideShortcutArguments());
        var wasReady = manager.AreStableShortcutsCorrect(launcherPath);
        manager.EnsureStableShortcuts(launcherPath);
        var ready = manager.AreStableShortcutsCorrect(launcherPath);
        if (!ready)
        {
            throw new InvalidDataException(
                "Los accesos directos machine-wide no coinciden con el launcher estable después de crearlos.");
        }

        return new ShortcutReadiness(true, wasReady, ready);
    }
}
