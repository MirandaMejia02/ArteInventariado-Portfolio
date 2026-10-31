using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class ArteInventariadoPathProviderTests
{
    [Fact]
    public void MachineWideLayout_ResolvesTheApprovedRootsAndDerivedPaths()
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);

        Assert.Equal(ArteInventariadoPathLayout.MachineWide, paths.Layout);
        Assert.Equal(Path.Combine(fixture.InstallRoot, "ArteInventariado.Launcher.exe"), paths.LauncherPath);
        Assert.Equal(Path.Combine(fixture.InstallRoot, "ArteInventariado.Updater.exe"), paths.UpdaterPath);
        Assert.Equal(Path.Combine(fixture.InstallRoot, "Versions"), paths.VersionsRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "UpdateState", "current.json"), paths.CurrentStatePath);
        Assert.Equal(Path.Combine(fixture.DataRoot, "UpdateState", "update-state.json"), paths.UpdateStatePath);
        Assert.Equal(Path.Combine(fixture.DataRoot, "UpdateState", "StationSnapshots"), paths.StationSnapshotsRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "UpdateState", "Recovery"), paths.RecoveryRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "Data"), paths.DataRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "Data", "arteinventariado_v1.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(fixture.DataRoot, "Assets"), paths.AssetsRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "BusinessAssets"), paths.BusinessAssetsRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "BusinessAssets", "Products"), paths.ProductBusinessAssetsRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "Station"), paths.StationRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "Station", "station-settings.json"), paths.StationSettingsPath);
        Assert.Equal(Path.Combine(fixture.DataRoot, "Logs"), paths.LogsRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "Maintenance"), paths.MaintenanceRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "ProtectedBackups"), paths.ProtectedBackupsRoot);
        Assert.Equal(Path.Combine(fixture.UserRoot, "Cache"), paths.CacheRoot);
        Assert.Equal(Path.Combine(fixture.UserRoot, "Cache", "Thumbnails"), paths.ThumbnailRoot);
    }

    [Fact]
    public void MachineWideLayout_KeepsMutableDataOutsideInstallRoot()
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);

        var installPrefix = fixture.InstallRoot + Path.DirectorySeparatorChar;
        Assert.StartsWith(installPrefix, paths.LauncherPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(installPrefix, paths.VersionsRoot, StringComparison.OrdinalIgnoreCase);
        Assert.False(paths.DatabasePath.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase));
        Assert.False(paths.CurrentStatePath.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase));
        Assert.False(paths.UpdateStatePath.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase));
        Assert.False(paths.ProtectedBackupsRoot.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase));
        Assert.False(paths.CacheRoot.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MachineWideLayout_IsPureResolutionAndDoesNotCreateDirectories()
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);

        _ = paths.DatabasePath;
        _ = paths.GetVersionDirectory("1.12.0-rc1");

        Assert.False(Directory.Exists(fixture.InstallRoot));
        Assert.False(Directory.Exists(fixture.DataRoot));
        Assert.False(Directory.Exists(fixture.UserRoot));
    }

    [Fact]
    public void MachineWideDefault_UsesTheWindowsX64KnownFolderRoots()
    {
        var paths = ArteInventariadoPathProvider.CreateMachineWideDefault();

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ArteInventariado"),
            paths.MachineInstallRoot);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ArteInventariado"),
            paths.MachineDataRoot);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArteInventariado"),
            paths.UserDataRoot);
        Assert.EndsWith("\\Versions", paths.VersionsRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("\\\\server\\share\\outside")]
    [InlineData("not-a-semver")]
    public void GetVersionDirectory_RejectsTraversalAndInvalidVersions(string version)
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);

        Assert.ThrowsAny<Exception>(() => paths.GetVersionDirectory(version));
    }

    [Fact]
    public void GetVersionDirectory_UsesCanonicalSemverSegment()
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);

        var path = paths.GetVersionDirectory("v1.12.0-rc1+build.5");

        Assert.Equal(Path.Combine(fixture.InstallRoot, "Versions", "1.12.0-rc1"), path);
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("..\\outside.json")]
    [InlineData("C:\\outside.json")]
    public void GetRecoveryPath_RejectsTraversal(string fileName)
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);

        Assert.ThrowsAny<Exception>(() => paths.GetRecoveryPath(fileName));
    }

    [Fact]
    public void LegacyLayout_PreservesTheV111PerUserShape()
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateLegacy(fixture.LegacyRoot);

        Assert.Equal(ArteInventariadoPathLayout.LegacyPerUser, paths.Layout);
        Assert.Equal(fixture.LegacyRoot, paths.MachineInstallRoot);
        Assert.Equal(fixture.LegacyRoot, paths.MachineDataRoot);
        Assert.Equal(fixture.LegacyRoot, paths.UserDataRoot);
        Assert.Equal(Path.Combine(fixture.LegacyRoot, "Updates", "Versions"), paths.VersionsRoot);
        Assert.Equal(Path.Combine(fixture.LegacyRoot, "Updates", "current.json"), paths.CurrentStatePath);
        Assert.Equal(Path.Combine(fixture.LegacyRoot, "Updates", "update-state.json"), paths.UpdateStatePath);
        Assert.Equal(Path.Combine(fixture.LegacyRoot, "Data", "arteinventariado_v1.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(fixture.LegacyRoot, "Backups"), paths.ProtectedBackupsRoot);
        Assert.Equal(Path.Combine(fixture.LegacyRoot, "Cache", "Thumbnails"), paths.ThumbnailRoot);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData(null, false)]
    public void VisualDevMode_IsAnExactOptIn(string? value, bool expected)
    {
        Assert.Equal(expected, ArteInventariadoPathProvider.IsVisualDevValue(value));
    }

    [Fact]
    public void VisualDevDataShape_IsLegacyCompatibleAndSeparateFromMachineWide()
    {
        using var fixture = new PathFixture();
        var paths = ArteInventariadoPathProvider.CreateLegacy(
            Path.Combine(fixture.LegacyRoot, "VisualDev"));

        Assert.False(paths.IsMachineWide);
        Assert.Equal(
            Path.Combine(fixture.LegacyRoot, "VisualDev", "Data", "arteinventariado_v1.db"),
            paths.DatabasePath);
        Assert.Equal(
            Path.Combine(fixture.LegacyRoot, "VisualDev", "Station", "station-settings.json"),
            paths.StationSettingsPath);
        Assert.DoesNotContain("Program Files", paths.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateRuntimePaths_MachineWideFactoryUsesTheCentralProvider()
    {
        using var fixture = new PathFixture();
        var provider = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);
        var paths = UpdateRuntimePaths.Create(provider);

        Assert.Same(provider, paths.PathProvider);
        Assert.Equal(provider.MachineInstallRoot, paths.ApplicationDataRoot);
        Assert.Equal(provider.UpdateStateRoot, paths.Root);
        Assert.Equal(provider.VersionsRoot, paths.VersionsDirectory);
        Assert.Equal(provider.CurrentStatePath, paths.CurrentPointerPath);
        Assert.Equal(provider.UpdateStatePath, paths.StatePath);
        Assert.Equal(provider.DatabasePath, paths.DatabasePath);
        Assert.Equal(provider.StationSettingsPath, paths.StationSettingsPath);
        Assert.Equal(provider.ProductBusinessAssetsRoot, paths.BusinessAssetsDirectory);
        Assert.Equal(provider.AssetsRoot, paths.ManagedAssetsDirectory);
        Assert.Equal(provider.GetVersionDirectory("1.12.0-rc1"), paths.GetVersionDirectory("1.12.0-rc1"));
    }

    [Fact]
    public void UpdateRuntimePaths_CreateFromArgumentsSupportsMachineWideRoots()
    {
        using var fixture = new PathFixture();
        var paths = UpdateRuntimePaths.CreateFromArguments(
        [
            "--machine-install-root", fixture.InstallRoot,
            "--machine-data-root", fixture.DataRoot,
            "--user-data-root", fixture.UserRoot
        ]);

        Assert.True(paths.PathProvider?.IsMachineWide);
        Assert.Equal(fixture.InstallRoot, paths.ApplicationDataRoot);
        Assert.Equal(Path.Combine(fixture.DataRoot, "UpdateState"), paths.Root);
    }

    [Theory]
    [InlineData(ArteInventariadoPathProvider.LauncherFileName)]
    [InlineData(ArteInventariadoPathProvider.UpdaterFileName)]
    public void UpdateRuntimePaths_DirectStableEntryPointInfersMachineWideLayout(string executableName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var machine = ArteInventariadoPathProvider.CreateMachineWideDefault();
        var processPath = Path.Combine(machine.MachineInstallRoot, executableName);

        var paths = UpdateRuntimePaths.CreateFromArguments([], processPath);

        Assert.True(paths.PathProvider?.IsMachineWide);
        Assert.Equal(machine.MachineInstallRoot, paths.MachineInstallRoot);
        Assert.Equal(machine.MachineDataRoot, paths.MachineDataRoot);
        Assert.Equal(machine.UserDataRoot, paths.UserDataRoot);
    }

    [Fact]
    public void UpdateRuntimePaths_DirectVersionedApplicationKeepsLegacyDefault()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var machine = ArteInventariadoPathProvider.CreateMachineWideDefault();
        var processPath = Path.Combine(
            machine.VersionsRoot,
            "1.12.0-ib6",
            UpdateLaunchTargetResolver.ApplicationExecutableName);

        var paths = UpdateRuntimePaths.CreateFromArguments([], processPath);

        Assert.False(paths.PathProvider?.IsMachineWide == true);
    }

    [Theory]
    [InlineData(ArteInventariadoPathProvider.LauncherFileName)]
    [InlineData(ArteInventariadoPathProvider.UpdaterFileName)]
    public void UpdateRuntimePaths_DirectStableEntryPointRejectsExplicitLegacyRoot(string executableName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var machine = ArteInventariadoPathProvider.CreateMachineWideDefault();
        var legacyRoot = Path.Combine(Path.GetTempPath(), $"ArteInventariado-legacy-{Guid.NewGuid():N}");

        Assert.Throws<ArgumentException>(() => UpdateRuntimePaths.CreateFromArguments(
            ["--app-data-root", legacyRoot],
            Path.Combine(machine.MachineInstallRoot, executableName)));
    }

    [Fact]
    public void UpdateRuntimePaths_AddLayoutArgumentsPreservesTheSelectedLayout()
    {
        using var fixture = new PathFixture();
        var provider = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);
        var paths = UpdateRuntimePaths.Create(provider);
        var startInfo = new System.Diagnostics.ProcessStartInfo("ArteInventariado.Updater.exe");

        paths.AddLayoutArguments(startInfo);

        Assert.Equal(
            [
                "--machine-install-root", fixture.InstallRoot,
                "--machine-data-root", fixture.DataRoot,
                "--user-data-root", fixture.UserRoot
            ],
            startInfo.ArgumentList);
    }

    [Fact]
    public void UpdateRuntimePaths_GetLayoutArgumentsIsExplicitAndStable()
    {
        using var fixture = new PathFixture();
        var provider = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(fixture.InstallRoot, "with spaces"),
            Path.Combine(fixture.DataRoot, "with spaces"),
            Path.Combine(fixture.UserRoot, "with spaces"));
        var paths = UpdateRuntimePaths.Create(provider);

        var arguments = paths.GetLayoutArguments();

        Assert.Contains("--machine-install-root", arguments, StringComparison.Ordinal);
        Assert.Contains("--machine-data-root", arguments, StringComparison.Ordinal);
        Assert.Contains("--user-data-root", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("--app-data-root", arguments, StringComparison.Ordinal);
        Assert.Contains('"', arguments);
    }

    [Fact]
    public void UpdateRuntimePaths_MachineWideShortcutArgumentsUseTheClickingUsersLocalAppData()
    {
        using var fixture = new PathFixture();
        var provider = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            Path.Combine(fixture.UserRoot, "installer-user"));
        var paths = UpdateRuntimePaths.Create(provider);

        var arguments = paths.GetMachineWideShortcutArguments();

        Assert.Contains("--machine-install-root", arguments, StringComparison.Ordinal);
        Assert.Contains("--machine-data-root", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("--user-data-root", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateRuntimePaths_MachineWideArgumentsMayDerivePerUserRoot()
    {
        using var fixture = new PathFixture();
        var paths = UpdateRuntimePaths.CreateFromArguments(
        [
            "--machine-install-root", fixture.InstallRoot,
            "--machine-data-root", fixture.DataRoot
        ]);

        Assert.True(paths.PathProvider?.IsMachineWide);
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ArteInventariadoPathProvider.ApplicationDirectoryName),
            paths.UserDataRoot);
    }

    [Fact]
    public void DatabasePathProvider_UsesTheCentralMachineDataRoot()
    {
        using var fixture = new PathFixture();
        var provider = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.InstallRoot,
            fixture.DataRoot,
            fixture.UserRoot);

        var databasePath = DatabasePathProvider.GetDatabasePath(provider);

        Assert.Equal(provider.DatabasePath, databasePath);
        Assert.True(Directory.Exists(provider.DataRoot));
    }

    private sealed class PathFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "ArteInventariado-PathTests", Guid.NewGuid().ToString("N"));

        public string InstallRoot => Path.Combine(_root, "Program Files", "ArteInventariado");
        public string DataRoot => Path.Combine(_root, "ProgramData", "ArteInventariado");
        public string UserRoot => Path.Combine(_root, "LocalAppData", "ArteInventariado");
        public string LegacyRoot => Path.Combine(_root, "Legacy", "ArteInventariado");

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
