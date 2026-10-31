using System.Security.Principal;

namespace ArteInventariado.Infrastructure.Updater;

public sealed record StableBootstrapResult(string LauncherPath, string CurrentExecutablePath, bool InstalledInitialVersion);

public sealed class StableDeploymentBootstrap
{
    public const string LauncherExecutableName = "ArteInventariado.Launcher.exe";
    public const string UpdaterExecutableName = "ArteInventariado.Updater.exe";

    private readonly UpdateRuntimePaths _paths;
    private readonly UpdateLaunchTargetResolver _resolver;
    private readonly UpdatePointerStore _pointerStore;
    private readonly StableEntryPointInstaller _entryPointInstaller;

    public StableDeploymentBootstrap(UpdateRuntimePaths paths, ShortcutLocations? shortcutLocations = null)
        : this(
            paths,
            new UpdateLaunchTargetResolver(paths),
            new UpdatePointerStore(paths.CurrentPointerPath),
            new StableEntryPointInstaller(
                paths,
                new WindowsShortcutManager(
                    paths.PathProvider?.IsMachineWide == true
                        ? shortcutLocations ?? ShortcutLocations.CreateMachineWideDefault()
                        : shortcutLocations,
                    paths.PathProvider?.IsMachineWide == true
                        ? paths.GetMachineWideShortcutArguments()
                        : null)))
    {
    }

    internal StableDeploymentBootstrap(
        UpdateRuntimePaths paths,
        UpdateLaunchTargetResolver resolver,
        UpdatePointerStore pointerStore,
        StableEntryPointInstaller entryPointInstaller)
    {
        _paths = paths;
        _resolver = resolver;
        _pointerStore = pointerStore;
        _entryPointInstaller = entryPointInstaller;
    }

    public StableBootstrapResult EnsureInstalled(string sourceDirectory, string? stableComponentsDirectory = null)
    {
        var source = Path.GetFullPath(sourceDirectory);
        var stableComponents = Path.GetFullPath(stableComponentsDirectory ?? sourceDirectory);
        var pointer = _pointerStore.Load();
        if (pointer is not null)
        {
            var current = _resolver.ResolveCurrent(pointer);
            var stableRunnerSource = Path.Combine(
                stableComponents,
                ArteInventariadoPathProvider.BackupRunnerFileName);
            if (!_entryPointInstaller.HasStableBinaries() &&
                (_paths.PathProvider?.IsMachineWide != true || File.Exists(stableRunnerSource)))
            {
                _entryPointInstaller.InstallStableBinaries(stableComponents);
            }
            _entryPointInstaller.EnsureShortcuts();
            return new StableBootstrapResult(_paths.StableLauncherPath, current, false);
        }

        var sourceExecutable = Path.Combine(source, UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!File.Exists(sourceExecutable))
        {
            throw new FileNotFoundException("La instalacion inicial no contiene ArteInventariado.WinForms.exe.", sourceExecutable);
        }

        var version = _resolver.ReadProductVersion(sourceExecutable);
        var targetDirectory = _paths.GetVersionDirectory(version);
        var targetExecutable = Path.Combine(targetDirectory, UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!Directory.Exists(targetDirectory))
        {
            CopyVersionDirectory(source, targetDirectory);
        }

        var validatedTarget = _resolver.ValidateManagedExecutable(version, targetExecutable);
        _entryPointInstaller.InstallStableBinaries(stableComponents);
        _pointerStore.Save(version, validatedTarget, null, DateTimeOffset.UtcNow);
        _entryPointInstaller.EnsureShortcuts();
        return new StableBootstrapResult(_paths.StableLauncherPath, validatedTarget, true);
    }

    public void EnsureStableEntryPoints(string stableComponentsDirectory)
    {
        var stableComponents = Path.GetFullPath(stableComponentsDirectory);
        if (!_entryPointInstaller.HasStableBinaries())
        {
            _entryPointInstaller.InstallStableBinaries(stableComponents);
        }

        _entryPointInstaller.EnsureShortcuts();
    }

    private void CopyVersionDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(_paths.VersionsDirectory);
        var staging = Path.Combine(_paths.VersionsDirectory, $".initial-{Guid.NewGuid():N}");
        try
        {
            CopyDirectorySafely(sourceDirectory, staging);
            Directory.Move(staging, targetDirectory);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void CopyDirectorySafely(string sourceDirectory, string destinationDirectory)
    {
        if ((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("La fuente de instalacion no puede ser un reparse point.");
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("La fuente de instalacion contiene un reparse point no permitido.");
            }

            var destination = Path.Combine(destinationDirectory, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyDirectorySafely(entry, destination);
            }
            else
            {
                File.Copy(entry, destination, overwrite: false);
            }
        }
    }
}

public sealed class StableEntryPointInstaller
{
    /// <summary>
    /// Native dependencies required by the protected runners when they are
    /// published as single-file apps without native self-extraction. Keeping
    /// them beside the stable entrypoints prevents the SYSTEM task from
    /// depending on an interactive user's TEMP/TMP directory.
    /// </summary>
    public static IReadOnlyList<string> RunnerRuntimeSupportFileNames { get; } =
    [
        "e_sqlite3.dll",
        "libSkiaSharp.dll"
    ];

    private readonly UpdateRuntimePaths _paths;
    private readonly WindowsShortcutManager _shortcutManager;

    public StableEntryPointInstaller(UpdateRuntimePaths paths, WindowsShortcutManager shortcutManager)
    {
        _paths = paths;
        _shortcutManager = shortcutManager;
    }

    public void EnsureFromVersionDirectory(string versionDirectory)
    {
        InstallStableBinaries(versionDirectory);
        EnsureShortcuts();
    }

    public bool HasStableBinaries()
    {
        if (!File.Exists(_paths.StableLauncherPath) ||
            !File.Exists(_paths.StableUpdaterPath))
        {
            return false;
        }

        // BackupProtegido invokes the runner directly from the stable
        // machine-wide root. Launcher/Updater being present is therefore not
        // sufficient: a stale or missing runner would leave the task calling
        // an older binary and can fail before BACKUP_RUNNER_STARTED.
        if (_paths.PathProvider?.IsMachineWide != true)
        {
            return true;
        }

        if (!File.Exists(_paths.PathProvider.BackupRunnerPath))
        {
            return false;
        }

        return RunnerRuntimeSupportFileNames.All(name =>
            File.Exists(Path.Combine(_paths.PathProvider.MachineInstallRoot, name)));
    }

    public void InstallStableBinaries(string versionDirectory)
    {
        EnsureApplicationRootIsSafe();
        CopyAtomically(Path.Combine(versionDirectory, StableDeploymentBootstrap.LauncherExecutableName), _paths.StableLauncherPath);
        CopyAtomically(Path.Combine(versionDirectory, StableDeploymentBootstrap.UpdaterExecutableName), _paths.StableUpdaterPath);
        var helperSource = Path.Combine(versionDirectory, ArteInventariadoPathProvider.StationHelperFileName);
        if (File.Exists(helperSource))
        {
            CopyAtomically(helperSource, _paths.PathProvider?.StationHelperPath ?? Path.Combine(_paths.ApplicationDataRoot, ArteInventariadoPathProvider.StationHelperFileName));
        }

        // The protected machine-wide task invokes this stable runner directly
        // from Program Files. Keep it alongside Launcher/Updater whenever a
        // published version supplies one, without changing the legacy layout.
        if (_paths.PathProvider?.IsMachineWide == true)
        {
            var backupRunnerSource = Path.Combine(
                versionDirectory,
                ArteInventariadoPathProvider.BackupRunnerFileName);
            if (File.Exists(backupRunnerSource))
            {
                CopyAtomically(backupRunnerSource, _paths.PathProvider.BackupRunnerPath);
            }
            var cloudBackupRunnerSource = Path.Combine(
                versionDirectory,
                ArteInventariadoPathProvider.CloudBackupRunnerFileName);
            if (File.Exists(cloudBackupRunnerSource))
            {
                CopyAtomically(cloudBackupRunnerSource, _paths.PathProvider.CloudBackupRunnerPath);
            }

            foreach (var supportFileName in RunnerRuntimeSupportFileNames)
            {
                var supportSource = Path.Combine(versionDirectory, supportFileName);
                if (File.Exists(supportSource))
                {
                    CopyAtomically(
                        supportSource,
                        Path.Combine(_paths.PathProvider.MachineInstallRoot, supportFileName));
                }
            }
        }
    }

    public void EnsureShortcuts()
    {
        // A standard user must be able to launch a protected machine-wide
        // install even if a common shortcut needs repair.  Shortcut creation
        // remains an explicit elevated installer responsibility; the legacy
        // per-user layout keeps its historical behavior.
        if (_paths.PathProvider?.IsMachineWide == true && !IsAdministrator())
        {
            return;
        }

        _shortcutManager.EnsureStableShortcuts(_paths.StableLauncherPath);
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void EnsureApplicationRootIsSafe()
    {
        Directory.CreateDirectory(_paths.ApplicationDataRoot);
        if ((File.GetAttributes(_paths.ApplicationDataRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("El installation root no puede ser un reparse point.");
        }
    }

    private static void CopyAtomically(string source, string destination)
    {
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("La version no contiene un componente estable requerido.", source);
        }

        if (File.Exists(destination) && FilesEqual(source, destination))
        {
            return;
        }

        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var a = new FileInfo(left);
        var b = new FileInfo(right);
        if (a.Length != b.Length) return false;
        using var first = File.OpenRead(left);
        using var second = File.OpenRead(right);
        const int size = 81920;
        var x = new byte[size];
        var y = new byte[size];
        int read;
        while ((read = first.Read(x, 0, x.Length)) > 0)
        {
            if (second.Read(y, 0, read) != read || !x.AsSpan(0, read).SequenceEqual(y.AsSpan(0, read))) return false;
        }

        return second.ReadByte() == -1;
    }
}

public interface IStableDeploymentFinalizer
{
    Task FinalizeAsync(string versionDirectory, CancellationToken cancellationToken = default);
}

public sealed class StableDeploymentFinalizer(StableEntryPointInstaller installer) : IStableDeploymentFinalizer
{
    public Task FinalizeAsync(string versionDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        installer.EnsureFromVersionDirectory(versionDirectory);
        return Task.CompletedTask;
    }
}

internal sealed class NullStableDeploymentFinalizer : IStableDeploymentFinalizer
{
    public static NullStableDeploymentFinalizer Instance { get; } = new();

    public Task FinalizeAsync(string versionDirectory, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
