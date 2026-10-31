using System.Diagnostics;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class MachineWideInstallationTests
{
    [Fact]
    public async Task InstallationRequiresAdministratorBeforeCreatingAnyRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-c2-install-{Guid.NewGuid():N}");
        var target = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(root, "install"),
            Path.Combine(root, "data"),
            Path.Combine(root, "user"));
        var service = new MachineWideInstallationService(
            new MachineWideBootstrapService(),
            new MachineWideAclService(),
            () => false);

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.InstallCleanAsync(target));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ElevationOnAlreadyElevatedProcessDoesNotRequireASecondPrompt()
    {
        if (!OperatingSystem.IsWindows() || !MachineWideElevation.IsAdministrator())
        {
            return;
        }

        var result = MachineWideElevation.EnsureElevated(
            Environment.ProcessPath ?? "missing.exe",
            []);

        Assert.Equal(MachineWideElevationOutcome.AlreadyElevated, result.Outcome);
    }

    [Fact]
    public async Task InstallClean_ContinuesFromPartialRootsWithSpacesAndPrereleasePayload()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado I-B1 partial {Guid.NewGuid():N}");
        var target = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(root, "Program Files target"),
            Path.Combine(root, "ProgramData target"),
            Path.Combine(root, "User data target"));
        var payload = Path.Combine(root, "install source with spaces");
        var sourceAssembly = typeof(MachineWideInstallationTests).Assembly.Location;

        try
        {
            Directory.CreateDirectory(payload);
            foreach (var name in new[]
                     {
                         UpdateLaunchTargetResolver.ApplicationExecutableName,
                         StableDeploymentBootstrap.LauncherExecutableName,
                         StableDeploymentBootstrap.UpdaterExecutableName,
                         ArteInventariadoPathProvider.StationHelperFileName
                     })
            {
                File.Copy(sourceAssembly, Path.Combine(payload, name));
            }

            var phases = new List<string>();
            var bootstrap = new MachineWideBootstrapService(phases.Add);
            await bootstrap.PrepareCleanInstallAsync(target, initializeStation: false);
            _ = new MachineWideBackupPolicyStore().EnsureInitialized(target);

            var result = await bootstrap.PrepareCleanInstallAsync(target, payload);

            var productVersion = UpdateVersion.Sanitize(
                FileVersionInfo.GetVersionInfo(sourceAssembly).ProductVersion!);
            Assert.Equal(MachineWideBootstrapOutcome.CleanInstallPrepared, result.Outcome);
            Assert.True(File.Exists(target.LauncherPath));
            Assert.True(File.Exists(target.UpdaterPath));
            Assert.True(File.Exists(target.StationSettingsPath));
            Assert.True(File.Exists(target.CurrentStatePath));
            Assert.True(File.Exists(
                Path.Combine(
                    target.GetVersionDirectory(productVersion),
                    UpdateLaunchTargetResolver.ApplicationExecutableName)));
            Assert.Equal(productVersion, new UpdatePointerStore(target.CurrentStatePath).Load()!.Version);
            Assert.Contains("payload-validating", phases);
            Assert.Contains("versions-promoted", phases);
            Assert.Contains("completed", phases);
        }
        finally
        {
            TryDeleteTree(root);
        }
    }

    private static void TryDeleteTree(string path)
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
}
