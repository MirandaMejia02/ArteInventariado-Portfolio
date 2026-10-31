using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class DeploymentLauncherTests
{
    [Fact]
    public void Resolver_AcceptsManagedCurrentAndPrevious()
    {
        using var fixture = new ResolverFixture();
        var current = fixture.AddVersion("1.11.0-rc3");
        var previous = fixture.AddVersion("1.11.0-rc2");
        var pointer = new UpdateVersionPointer(1, "1.11.0-rc3", current, previous, DateTimeOffset.UtcNow);

        Assert.Equal(current, fixture.Resolver.ResolveCurrent(pointer));
        Assert.Equal(previous, fixture.Resolver.ResolvePrevious(pointer));
    }

    [Fact]
    public void Resolver_ReturnsNullWhenPointerIsAbsent()
    {
        using var fixture = new ResolverFixture();

        Assert.Null(fixture.Resolver.ResolveCurrentOrNull());
    }

    [Fact]
    public void Resolver_RejectsCorruptPointer()
    {
        using var fixture = new ResolverFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Paths.CurrentPointerPath)!);
        File.WriteAllText(fixture.Paths.CurrentPointerPath, "{ corrupt }");

        Assert.Throws<InvalidDataException>(() => fixture.Resolver.ResolveCurrentOrNull());
    }

    [Fact]
    public void Resolver_RejectsMissingCurrentExecutable()
    {
        using var fixture = new ResolverFixture();
        var path = fixture.ExpectedPath("1.11.0-rc3");
        var pointer = new UpdateVersionPointer(1, "1.11.0-rc3", path, null, DateTimeOffset.UtcNow);

        Assert.Throws<FileNotFoundException>(() => fixture.Resolver.ResolveCurrent(pointer));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\notepad.exe")]
    [InlineData("\\\\server\\share\\ArteInventariado.WinForms.exe")]
    public void Resolver_RejectsExternalAndUncPaths(string path)
    {
        using var fixture = new ResolverFixture();
        var pointer = new UpdateVersionPointer(1, "1.11.0-rc3", path, null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidDataException>(() => fixture.Resolver.ResolveCurrent(pointer));
    }

    [Fact]
    public void Resolver_RejectsTraversalAndWrongExecutableName()
    {
        using var fixture = new ResolverFixture();
        var traversal = Path.Combine(
            fixture.Paths.GetVersionDirectory("1.11.0-rc3"),
            "..",
            "outside",
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        var wrongName = Path.Combine(fixture.Paths.GetVersionDirectory("1.11.0-rc3"), "otro.exe");

        Assert.Throws<InvalidDataException>(() => fixture.Resolver.ResolveCurrent(
            new UpdateVersionPointer(1, "1.11.0-rc3", traversal, null, DateTimeOffset.UtcNow)));
        Assert.Throws<InvalidDataException>(() => fixture.Resolver.ResolveCurrent(
            new UpdateVersionPointer(1, "1.11.0-rc3", wrongName, null, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void Resolver_RejectsReparsePointInManagedPath()
    {
        using var fixture = new ResolverFixture();
        var current = fixture.AddVersion("1.11.0-rc3");
        fixture.Probe.ReparsePoints.Add(Path.GetDirectoryName(current)!);

        Assert.Throws<InvalidDataException>(() => fixture.Resolver.ResolveCurrent(
            new UpdateVersionPointer(1, "1.11.0-rc3", current, null, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void Resolver_RejectsProductVersionMismatchAndExternalPrevious()
    {
        using var fixture = new ResolverFixture();
        var current = fixture.AddVersion("1.11.0-rc3", productVersion: "1.11.0-rc2");
        var external = Path.Combine(fixture.Temp.Path, "legacy", UpdateLaunchTargetResolver.ApplicationExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        File.WriteAllText(external, "legacy");
        fixture.Probe.Versions[Path.GetFullPath(external)] = "1.11.0-rc2";

        Assert.Throws<InvalidDataException>(() => fixture.Resolver.ResolveCurrent(
            new UpdateVersionPointer(1, "1.11.0-rc3", current, null, DateTimeOffset.UtcNow)));

        var validCurrent = fixture.AddVersion("1.11.0-rc3", productVersion: "1.11.0-rc3", overwrite: true);
        fixture.Probe.VersionRequests.Clear();
        Assert.Throws<InvalidDataException>(() => fixture.Resolver.ResolvePrevious(
            new UpdateVersionPointer(1, "1.11.0-rc3", validCurrent, external, DateTimeOffset.UtcNow)));
        Assert.DoesNotContain(Path.GetFullPath(external), fixture.Probe.VersionRequests);
    }

    [Fact]
    public void ShortcutManager_CreatesCanonicalAndMigratesLegacyLinks()
    {
        using var temp = new TemporaryDirectory("arte-shortcut-tests");
        var desktop = Path.Combine(temp.Path, "Desktop");
        var programs = Path.Combine(temp.Path, "Programs");
        var launcher = Path.Combine(temp.Path, "ArteInventariado.Launcher.exe");
        File.WriteAllText(launcher, "launcher");
        var backend = new FakeShortcutBackend();
        var legacyA = Path.Combine(programs, "ArteInventariado.WinForms.lnk");
        var legacyB = Path.Combine(programs, "ArteInventariado", "ArteInventariado.lnk");
        backend.Existing.UnionWith([legacyA, legacyB]);
        var manager = new WindowsShortcutManager(new ShortcutLocations(desktop, programs), backend);

        manager.EnsureStableShortcuts(launcher);

        var expected = new[]
        {
            Path.Combine(desktop, "ArteInventariado.lnk"),
            Path.Combine(programs, "ArteInventariado.lnk"),
            legacyA,
            legacyB
        };
        Assert.Equal(expected.Order(), backend.Saved.Keys.Order());
        Assert.All(backend.Saved.Values, shortcut =>
        {
            Assert.Equal(Path.GetFullPath(launcher), shortcut.Target);
            Assert.Equal(Path.GetFullPath(launcher), shortcut.Icon);
            Assert.Equal("ArteInventariado", shortcut.Description);
        });
    }

    [Fact]
    public void Bootstrap_InstallsInitialVersionAndIsIdempotent()
    {
        using var temp = new TemporaryDirectory("arte-bootstrap-tests");
        var source = Path.Combine(temp.Path, "publish");
        Directory.CreateDirectory(source);
        var sourceApplication = Path.Combine(source, UpdateLaunchTargetResolver.ApplicationExecutableName);
        File.WriteAllText(sourceApplication, "application");
        File.WriteAllText(Path.Combine(source, StableDeploymentBootstrap.LauncherExecutableName), "launcher");
        File.WriteAllText(Path.Combine(source, StableDeploymentBootstrap.UpdaterExecutableName), "updater");
        var paths = UpdateRuntimePaths.Create(Path.Combine(temp.Path, "app-data"));
        var probe = new FakeExecutableProbe();
        probe.Versions[Path.GetFullPath(sourceApplication)] = "1.11.0-rc3";
        var targetApplication = Path.Combine(paths.GetVersionDirectory("1.11.0-rc3"), UpdateLaunchTargetResolver.ApplicationExecutableName);
        probe.Versions[Path.GetFullPath(targetApplication)] = "1.11.0-rc3";
        var resolver = new UpdateLaunchTargetResolver(paths, probe);
        var backend = new FakeShortcutBackend();
        var shortcuts = new WindowsShortcutManager(
            new ShortcutLocations(Path.Combine(temp.Path, "Desktop"), Path.Combine(temp.Path, "Programs")),
            backend);
        var entryPoint = new StableEntryPointInstaller(paths, shortcuts);
        var bootstrap = new StableDeploymentBootstrap(paths, resolver, new UpdatePointerStore(paths.CurrentPointerPath), entryPoint);

        var first = bootstrap.EnsureInstalled(source);
        var second = bootstrap.EnsureInstalled(source);

        Assert.True(first.InstalledInitialVersion);
        Assert.False(second.InstalledInitialVersion);
        Assert.True(File.Exists(targetApplication));
        Assert.True(File.Exists(paths.StableLauncherPath));
        Assert.True(File.Exists(paths.StableUpdaterPath));
        Assert.Equal(Path.GetFullPath(targetApplication), new UpdatePointerStore(paths.CurrentPointerPath).Load()!.ExecutablePath);
        Assert.Contains(Path.Combine(temp.Path, "Desktop", "ArteInventariado.lnk"), backend.Saved.Keys);
        Assert.Contains(Path.Combine(temp.Path, "Programs", "ArteInventariado.lnk"), backend.Saved.Keys);

        File.WriteAllText(paths.CurrentPointerPath, "{ corrupt }");
        bootstrap.EnsureStableEntryPoints(source);
        Assert.True(File.Exists(paths.StableLauncherPath));
    }

    [Fact]
    public void Bootstrap_MachineWideCurrentUsesVersionedExecutableWithoutRootWinForms()
    {
        using var fixture = new ResolverFixture(machineWide: true);
        var current = fixture.AddVersion("1.12.0-ib6");
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Paths.StableLauncherPath)!);
        File.WriteAllText(fixture.Paths.StableLauncherPath, "stable launcher");
        File.WriteAllText(fixture.Paths.StableUpdaterPath, "stable updater");
        new UpdatePointerStore(fixture.Paths.CurrentPointerPath)
            .Save("1.12.0-ib6", current, null, DateTimeOffset.UtcNow);

        var shortcutBackend = new FakeShortcutBackend();
        var shortcutManager = new WindowsShortcutManager(
            new ShortcutLocations(
                Path.Combine(fixture.Temp.Path, "Desktop"),
                Path.Combine(fixture.Temp.Path, "Programs")),
            shortcutBackend);
        var entryPointInstaller = new StableEntryPointInstaller(fixture.Paths, shortcutManager);
        var bootstrap = new StableDeploymentBootstrap(
            fixture.Paths,
            fixture.Resolver,
            new UpdatePointerStore(fixture.Paths.CurrentPointerPath),
            entryPointInstaller);
        var sourceWithoutApplication = Path.Combine(fixture.Temp.Path, "payload-without-root-winforms");
        Directory.CreateDirectory(sourceWithoutApplication);

        var result = bootstrap.EnsureInstalled(sourceWithoutApplication);

        Assert.False(result.InstalledInitialVersion);
        Assert.Equal(Path.GetFullPath(current), result.CurrentExecutablePath);
        Assert.False(File.Exists(Path.Combine(
            fixture.Paths.MachineInstallRoot,
            UpdateLaunchTargetResolver.ApplicationExecutableName)));
    }

    [Fact]
    public void BootstrapRepairsMissingStableMachineWideBackupRunner()
    {
        using var fixture = new ResolverFixture(machineWide: true);
        var current = fixture.AddVersion("1.14.0");
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Paths.StableLauncherPath)!);
        File.WriteAllText(fixture.Paths.StableLauncherPath, "stable launcher");
        File.WriteAllText(fixture.Paths.StableUpdaterPath, "stable updater");
        new UpdatePointerStore(fixture.Paths.CurrentPointerPath)
            .Save("1.14.0", current, null, DateTimeOffset.UtcNow);

        var source = Path.Combine(fixture.Temp.Path, "published-components");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, StableDeploymentBootstrap.LauncherExecutableName), "new launcher");
        File.WriteAllText(Path.Combine(source, StableDeploymentBootstrap.UpdaterExecutableName), "new updater");
        File.WriteAllText(
            Path.Combine(source, ArteInventariadoPathProvider.BackupRunnerFileName),
            "corrected backup runner");

        var shortcutBackend = new FakeShortcutBackend();
        var shortcutManager = new WindowsShortcutManager(
            new ShortcutLocations(
                Path.Combine(fixture.Temp.Path, "Desktop"),
                Path.Combine(fixture.Temp.Path, "Programs")),
            shortcutBackend);
        var entryPointInstaller = new StableEntryPointInstaller(fixture.Paths, shortcutManager);
        var bootstrap = new StableDeploymentBootstrap(
            fixture.Paths,
            fixture.Resolver,
            new UpdatePointerStore(fixture.Paths.CurrentPointerPath),
            entryPointInstaller);

        bootstrap.EnsureInstalled(source, source);

        Assert.Equal(
            "corrected backup runner",
            File.ReadAllText(fixture.Paths.PathProvider!.BackupRunnerPath));
    }

    [Fact]
    public void StableMachineWideRunnersCarryNativeRuntimeSupport()
    {
        using var fixture = new ResolverFixture(machineWide: true);
        var source = Path.Combine(fixture.Temp.Path, "published-components");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, StableDeploymentBootstrap.LauncherExecutableName), "launcher");
        File.WriteAllText(Path.Combine(source, StableDeploymentBootstrap.UpdaterExecutableName), "updater");
        File.WriteAllText(
            Path.Combine(source, ArteInventariadoPathProvider.BackupRunnerFileName),
            "backup runner");
        foreach (var supportFileName in StableEntryPointInstaller.RunnerRuntimeSupportFileNames)
        {
            File.WriteAllText(Path.Combine(source, supportFileName), supportFileName);
        }

        var shortcutManager = new WindowsShortcutManager(
            new ShortcutLocations(
                Path.Combine(fixture.Temp.Path, "Desktop"),
                Path.Combine(fixture.Temp.Path, "Programs")),
            new FakeShortcutBackend());
        var installer = new StableEntryPointInstaller(fixture.Paths, shortcutManager);

        installer.InstallStableBinaries(source);

        Assert.True(installer.HasStableBinaries());
        foreach (var supportFileName in StableEntryPointInstaller.RunnerRuntimeSupportFileNames)
        {
            Assert.Equal(
                supportFileName,
                File.ReadAllText(Path.Combine(fixture.Paths.PathProvider!.MachineInstallRoot, supportFileName)));
        }
    }

    [Fact]
    public void LauncherCoordinator_MachineWideCurrentResolvesVersionedExecutableWithoutRootWinForms()
    {
        using var fixture = new ResolverFixture(machineWide: true);
        var current = fixture.AddVersion("1.12.0-ib6");
        new UpdatePointerStore(fixture.Paths.CurrentPointerPath)
            .Save("1.12.0-ib6", current, null, DateTimeOffset.UtcNow);

        var coordinator = new StableLauncherCoordinator(
            fixture.Paths,
            fixture.Resolver,
            new UpdateStateStore(fixture.Paths.StatePath),
            new FakeRecoveryRunner(() => 0));

        var resolved = coordinator.ResolveCurrentWithRecovery(false);

        Assert.Equal(Path.GetFullPath(current), resolved);
        Assert.False(File.Exists(Path.Combine(
            fixture.Paths.MachineInstallRoot,
            UpdateLaunchTargetResolver.ApplicationExecutableName)));
    }

    [Fact]
    public void Coordinator_DelegatesOneRecoveryAndThenResolvesCurrent()
    {
        using var fixture = new ResolverFixture();
        var current = fixture.ExpectedPath("1.11.0-rc3");
        fixture.SaveState(UpdateState.Installing);
        var runner = new FakeRecoveryRunner(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllText(current, "current");
            fixture.Probe.Versions[Path.GetFullPath(current)] = "1.11.0-rc3";
            new UpdatePointerStore(fixture.Paths.CurrentPointerPath)
                .Save("1.11.0-rc3", current, null, DateTimeOffset.UtcNow);
            return 0;
        });
        var coordinator = new StableLauncherCoordinator(
            fixture.Paths,
            fixture.Resolver,
            new UpdateStateStore(fixture.Paths.StatePath),
            runner);

        Assert.Equal(Path.GetFullPath(current), coordinator.ResolveCurrentWithRecovery(false));
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public void Coordinator_PreventsRecoveryLoopAndDoesNotGuessPrevious()
    {
        using var fixture = new ResolverFixture();
        fixture.SaveState(UpdateState.RollbackRequired);
        var runner = new FakeRecoveryRunner(() => 0);
        var coordinator = new StableLauncherCoordinator(
            fixture.Paths,
            fixture.Resolver,
            new UpdateStateStore(fixture.Paths.StatePath),
            runner);

        var error = Assert.Throws<InvalidOperationException>(() => coordinator.ResolveCurrentWithRecovery(true));

        Assert.Contains("ya fue intentada", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.Calls);
    }

    private sealed class ResolverFixture : IDisposable
    {
        public TemporaryDirectory Temp { get; } = new("arte-launcher-resolver-tests");
        public UpdateRuntimePaths Paths { get; }
        public FakeExecutableProbe Probe { get; } = new();
        public UpdateLaunchTargetResolver Resolver { get; }

        public ResolverFixture(bool machineWide = false)
        {
            Paths = machineWide
                ? UpdateRuntimePaths.Create(ArteInventariadoPathProvider.CreateMachineWide(
                    Path.Combine(Temp.Path, "Program Files", "ArteInventariado"),
                    Path.Combine(Temp.Path, "ProgramData", "ArteInventariado"),
                    Path.Combine(Temp.Path, "UserData", "ArteInventariado")))
                : UpdateRuntimePaths.Create(Path.Combine(Temp.Path, "app-data"));
            Directory.CreateDirectory(Paths.ApplicationDataRoot);
            Resolver = new UpdateLaunchTargetResolver(Paths, Probe);
        }

        public string ExpectedPath(string version) =>
            Path.Combine(Paths.GetVersionDirectory(version), UpdateLaunchTargetResolver.ApplicationExecutableName);

        public string AddVersion(string version, string? productVersion = null, bool overwrite = false)
        {
            var path = ExpectedPath(version);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, version);
            Probe.Versions[Path.GetFullPath(path)] = productVersion ?? version;
            return Path.GetFullPath(path);
        }

        public void SaveState(UpdateState state)
        {
            var source = ExpectedPath("1.11.0-rc2");
            new UpdateStateStore(Paths.StatePath).Save(new UpdateStateDocument(
                UpdateStateStore.CurrentSchemaVersion,
                state,
                "1.11.0-rc2",
                "1.11.0-rc3",
                "package.zip",
                "manifest.json",
                Path.Combine(Temp.Path, "PRE_UPDATE.aibak"),
                source,
                ExpectedPath("1.11.0-rc3"),
                null,
                DateTimeOffset.UtcNow,
                source,
                Guid.NewGuid(),
                InstallationMode.Principal,
                new string('A', 64),
                false,
                Path.Combine(Temp.Path, "station-snapshot.json")));
        }

        public void Dispose() => Temp.Dispose();
    }

    private sealed class FakeExecutableProbe : IUpdateExecutableProbe
    {
        public Dictionary<string, string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> VersionRequests { get; } = [];

        public bool FileExists(string path) => File.Exists(path);

        public bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

        public FileAttributes GetAttributes(string path)
        {
            var attributes = File.GetAttributes(path);
            return ReparsePoints.Contains(Path.GetFullPath(path)) ? attributes | FileAttributes.ReparsePoint : attributes;
        }

        public string? GetProductVersion(string path)
        {
            var fullPath = Path.GetFullPath(path);
            VersionRequests.Add(fullPath);
            return Versions.GetValueOrDefault(fullPath);
        }
    }

    private sealed class FakeShortcutBackend : IWindowsShortcutBackend
    {
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (string Target, string WorkingDirectory, string Icon, string Description)> Saved { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public bool Exists(string path) => Existing.Contains(path);

        public string? GetTargetOrNull(string path) =>
            Saved.TryGetValue(path, out var saved)
                ? saved.Target
                : Existing.Contains(path) ? "legacy-target" : null;

        public string? GetArgumentsOrNull(string path) => string.Empty;

        public void Save(
            string shortcutPath,
            string targetPath,
            string workingDirectory,
            string iconPath,
            string description,
            string arguments) =>
            Saved[shortcutPath] = (targetPath, workingDirectory, iconPath, description);
    }

    private sealed class FakeRecoveryRunner(Func<int> action) : IUpdateRecoveryRunner
    {
        public int Calls { get; private set; }

        public int Recover()
        {
            Calls++;
            return action();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
