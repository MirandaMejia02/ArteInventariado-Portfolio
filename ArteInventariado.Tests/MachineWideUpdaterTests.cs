using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class MachineWideUpdaterTests
{
    [Fact]
    public void MachineWideUpdaterStartInfo_UsesRunasAndExplicitLayout()
    {
        using var fixture = new Fixture();
        MachineWideUpdateGuard.ValidateUpdaterArguments(
            fixture.Arguments,
            fixture.Paths,
            allowSyntheticRoots: true);

        var startInfo = UpdateLauncher.CreateUpdaterStartInfo(
            fixture.Paths,
            fixture.Paths.StableUpdaterPath,
            ["--recover", "--state", fixture.Paths.StatePath]);

        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb, ignoreCase: true);
        Assert.Equal(fixture.Paths.MachineInstallRoot, fixture.Paths.PathProvider!.MachineInstallRoot);
        Assert.Contains("--machine-install-root", startInfo.ArgumentList);
        Assert.Contains(fixture.Paths.MachineDataRoot, startInfo.ArgumentList);
        Assert.DoesNotContain(startInfo.ArgumentList, argument => argument == "--app-data-root");
    }

    [Fact]
    public void StableMachineWideUpdater_InfersOnlyItsOwnMissingLayoutArguments()
    {
        using var fixture = new Fixture();
        var legacyArguments = new[]
        {
            "--recover",
            "--state",
            fixture.Paths.StatePath
        };

        var effectiveArguments = MachineWideUpdateGuard.EnsureStableUpdaterLayoutArguments(
            legacyArguments,
            fixture.Paths,
            fixture.Paths.StableUpdaterPath);

        Assert.Contains("--machine-install-root", effectiveArguments);
        Assert.Contains(fixture.Paths.MachineInstallRoot, effectiveArguments);
        Assert.Contains("--machine-data-root", effectiveArguments);
        Assert.Contains(fixture.Paths.MachineDataRoot, effectiveArguments);
        Assert.Contains("--user-data-root", effectiveArguments);
        Assert.Contains(fixture.Paths.UserDataRoot, effectiveArguments);
        MachineWideUpdateGuard.ValidateUpdaterArguments(
            effectiveArguments,
            fixture.Paths,
            allowSyntheticRoots: true);
    }

    [Fact]
    public void StableMachineWideUpdater_DoesNotInferRootsForAnotherExecutableOrRepairPartialRoots()
    {
        using var fixture = new Fixture();
        var otherExecutable = Path.Combine(fixture.Root, "unmanaged", ArteInventariadoPathProvider.UpdaterFileName);
        Assert.Throws<InvalidDataException>(() => MachineWideUpdateGuard.EnsureStableUpdaterLayoutArguments(
            ["--recover", "--state", fixture.Paths.StatePath],
            fixture.Paths,
            otherExecutable));

        var partialLayout = new[]
        {
            "--recover",
            "--state",
            fixture.Paths.StatePath,
            "--machine-install-root",
            fixture.Paths.MachineInstallRoot
        };
        var unchanged = MachineWideUpdateGuard.EnsureStableUpdaterLayoutArguments(
            partialLayout,
            fixture.Paths,
            fixture.Paths.StableUpdaterPath);

        Assert.Equal(partialLayout, unchanged);
        Assert.Throws<InvalidDataException>(() => MachineWideUpdateGuard.ValidateUpdaterArguments(
            unchanged,
            fixture.Paths,
            allowSyntheticRoots: true));
    }

    [Fact]
    public void MachineWideUpdaterStartInfo_AllowsStableLaunchContinuation()
    {
        using var fixture = new Fixture();
        var arguments = fixture.Arguments
            .Where(argument => !string.Equals(argument, "--no-relaunch", StringComparison.OrdinalIgnoreCase))
            .Append("--launch-stable")
            .ToArray();

        MachineWideUpdateGuard.ValidateUpdaterArguments(
            arguments,
            fixture.Paths,
            allowSyntheticRoots: true);

        var startInfo = UpdateLauncher.CreateUpdaterStartInfo(
            fixture.Paths,
            fixture.Paths.StableUpdaterPath,
            ["--launch-stable", "--state", fixture.Paths.StatePath]);

        Assert.Contains("--launch-stable", startInfo.ArgumentList);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb, ignoreCase: true);
    }

    [Fact]
    public void LegacyUpdaterStartInfo_RemainsNonElevated()
    {
        using var fixture = new LegacyFixture();

        var startInfo = UpdateLauncher.CreateUpdaterStartInfo(
            fixture.Paths,
            fixture.Paths.StableUpdaterPath,
            ["--recover", "--state", fixture.Paths.StatePath]);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(string.IsNullOrEmpty(startInfo.Verb));
        Assert.Contains("--app-data-root", startInfo.ArgumentList);
        Assert.DoesNotContain(startInfo.ArgumentList, argument => argument == "--machine-install-root");

        var fallback = Path.Combine(fixture.Root, "fallback", ArteInventariadoPathProvider.UpdaterFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
        File.WriteAllText(fallback, "legacy fallback updater");
        var fallbackStartInfo = UpdateLauncher.CreateUpdaterStartInfo(
            fixture.Paths,
            fallback,
            ["--recover", "--state", fixture.Paths.StatePath]);

        Assert.False(fallbackStartInfo.UseShellExecute);
        Assert.Equal(Path.GetFullPath(fallback), fallbackStartInfo.FileName);
    }

    [Fact]
    public void MachineWideArgumentBoundary_RejectsUnknownFlagsAndNonCanonicalState()
    {
        using var fixture = new Fixture();

        var unknown = fixture.Arguments.Append("--execute").ToArray();
        Assert.Throws<ArgumentException>(() => MachineWideUpdateGuard.ValidateUpdaterArguments(
            unknown,
            fixture.Paths,
            allowSyntheticRoots: true));

        var forgedState = fixture.Arguments.ToArray();
        forgedState[Array.IndexOf(forgedState, "--state") + 1] =
            Path.Combine(fixture.Root, "data", "UpdateState", "forged.json");
        Assert.Throws<InvalidDataException>(() => MachineWideUpdateGuard.ValidateUpdaterArguments(
            forgedState,
            fixture.Paths,
            allowSyntheticRoots: true));
    }

    [Fact]
    public void MachineWideArgumentBoundary_AllowsUserRootFromTheInteractiveProfile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ArteInventariadoPathProvider.ApplicationDirectoryName);
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ArteInventariadoPathProvider.ApplicationDirectoryName);
        var userRoot = Path.Combine(
            Path.GetPathRoot(dataRoot)!,
            "Users",
            "Empleado",
            "AppData",
            "Local",
            ArteInventariadoPathProvider.ApplicationDirectoryName);
        var paths = UpdateRuntimePaths.CreateMachineWide(installRoot, dataRoot, userRoot);
        var arguments = new[]
        {
            "--machine-install-root", installRoot,
            "--machine-data-root", dataRoot,
            "--user-data-root", userRoot,
            "--state", paths.StatePath,
            "--no-relaunch"
        };

        MachineWideUpdateGuard.ValidateUpdaterArguments(arguments, paths);
    }

    [Fact]
    public void MachineWideStateBoundary_RejectsExternalWriteTargets()
    {
        using var fixture = new Fixture();
        var state = new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.ReadyToInstall,
            "1.11.0",
            "1.12.0",
            Path.Combine(fixture.Root, "package.zip"),
            Path.Combine(fixture.Root, "manifest.json"),
            Path.Combine(fixture.Root, "pre-update.aibak"),
            Path.Combine(fixture.Root, "outside", UpdateLaunchTargetResolver.ApplicationExecutableName),
            null,
            null,
            DateTimeOffset.UtcNow,
            Path.Combine(fixture.Root, "outside", UpdateLaunchTargetResolver.ApplicationExecutableName),
            Guid.NewGuid(),
            InstallationMode.Principal,
            new string('A', 64),
            false,
            Path.Combine(fixture.Paths.StationSnapshotsDirectory, "station.json"));

        Assert.Throws<InvalidDataException>(() => MachineWideUpdateGuard.ValidateStateDocument(state, fixture.Paths));
    }

    [Fact]
    public void MachineWideArgumentBoundary_ValidatesExpectedApplicationExecutable()
    {
        using var fixture = new Fixture();
        var executable = Path.Combine(
            fixture.Paths.GetVersionDirectory("1.14.0"),
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "synthetic application");
        var arguments = fixture.Arguments
            .Append("--wait-executable")
            .Append(executable)
            .ToArray();

        MachineWideUpdateGuard.ValidateUpdaterArguments(
            arguments,
            fixture.Paths,
            allowSyntheticRoots: true);

        var outside = arguments.ToArray();
        outside[Array.IndexOf(outside, "--wait-executable") + 1] = Path.Combine(fixture.Root, "outside.exe");
        Assert.Throws<InvalidDataException>(() => MachineWideUpdateGuard.ValidateUpdaterArguments(
            outside,
            fixture.Paths,
            allowSyntheticRoots: true));
    }

    [Fact]
    public async Task Installer_RejectsNonElevatedMachineWideProcessBeforeMutation()
    {
        using var fixture = new Fixture();
        var stateStore = new UpdateStateStore(fixture.Paths.StatePath);
        stateStore.Save(new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.Idle,
            "1.11.0",
            "1.12.0",
            "package.zip",
            "manifest.json",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow));

        var installer = new UpdateInstaller(
            new UpdatePackageValidator(),
            stateStore,
            new UpdateBackupService(),
            fixture.Paths,
            isAdministrator: () => false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => installer.RecoverInterruptedAsync());
        Assert.Equal(UpdateState.Idle, stateStore.Load()!.State);
    }

    [Fact]
    public void SyntheticMachineWideBoundarySmoke_IsGreen()
    {
        Assert.Equal(0, MachineWideUpdaterSmokeHarness.Run());
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-machine-updater-tests-{Guid.NewGuid():N}");
        public ArteInventariadoPathProvider Provider { get; }
        public UpdateRuntimePaths Paths { get; }
        public string[] Arguments { get; }

        public Fixture()
        {
            Provider = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(Root, "install"),
                Path.Combine(Root, "data"),
                Path.Combine(Root, "user"));
            Paths = UpdateRuntimePaths.Create(Provider);
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.StableUpdaterPath)!);
            File.WriteAllText(Paths.StableUpdaterPath, "synthetic updater");
            Arguments =
            [
                "--machine-install-root", Provider.MachineInstallRoot,
                "--machine-data-root", Provider.MachineDataRoot,
                "--user-data-root", Provider.UserDataRoot,
                "--state", Paths.StatePath,
                "--wait-pid", "1234",
                "--no-relaunch"
            ];
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class LegacyFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-legacy-updater-tests-{Guid.NewGuid():N}");
        public UpdateRuntimePaths Paths { get; }

        public LegacyFixture()
        {
            Paths = UpdateRuntimePaths.Create(Root);
            Directory.CreateDirectory(Paths.ApplicationDataRoot);
            File.WriteAllText(Paths.StableUpdaterPath, "legacy updater");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
