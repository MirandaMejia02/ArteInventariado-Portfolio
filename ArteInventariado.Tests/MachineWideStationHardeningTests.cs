using System.Text.Json;
using System.Security.AccessControl;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;

#pragma warning disable CA1416 // ACL assertions are intentionally Windows-only.

namespace ArteInventariado.Tests;

public sealed class MachineWideStationHardeningTests
{
    [Fact]
    public void MachineWideStore_SplitsProtectedIdentityFromOperationalPreferences()
    {
        using var fixture = new StationFixture();
        var store = new LocalStationConfigurationStore(fixture.Paths);

        var pending = store.EnsureInitialized();
        var installationId = pending.Status.InstallationId!.Value;
        store.SaveConfigured(InstallationMode.Principal);
        store.SavePosSettings(new PosStationSettingsDocument(true));

        Assert.True(store.IsMachineWideSplit);
        Assert.Equal(fixture.Paths.StationSettingsPath, store.ConfigurationPath);
        Assert.Equal(fixture.Paths.StationOperationalSettingsPath, store.OperationalConfigurationPath);
        Assert.Equal(installationId, store.Load().Status.InstallationId);
        Assert.Equal(InstallationMode.Principal, store.Load().Status.Mode);
        Assert.True(store.Load().Document!.Pos!.QuickPosEnabled);

        using var core = JsonDocument.Parse(File.ReadAllText(fixture.Paths.StationSettingsPath));
        Assert.Equal(installationId, core.RootElement.GetProperty("installationId").GetGuid());
        Assert.Equal(
            InstallationMode.Principal.ToString(),
            core.RootElement.GetProperty("installationMode").GetString());
        Assert.False(core.RootElement.TryGetProperty("backup", out _));
        Assert.False(core.RootElement.TryGetProperty("reports", out _));
        Assert.False(core.RootElement.TryGetProperty("pos", out _));
        Assert.False(core.RootElement.TryGetProperty("peripherals", out _));

        using var operational = JsonDocument.Parse(
            File.ReadAllText(fixture.Paths.StationOperationalSettingsPath));
        Assert.True(operational.RootElement.GetProperty("pos").GetProperty("quickPosEnabled").GetBoolean());
        Assert.False(operational.RootElement.TryGetProperty("installationId", out _));
        Assert.False(operational.RootElement.TryGetProperty("installationMode", out _));
        Assert.Empty(Directory.EnumerateFiles(fixture.Paths.StationRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void SyntheticStationHardeningSmoke_IsGreen()
    {
        Assert.Equal(0, MachineWideStationHardeningSmokeHarness.Run());
    }

    [Fact]
    public void LegacyStore_RemainsCombinedForV11Compatibility()
    {
        using var fixture = new StationFixture(legacy: true);
        var store = new LocalStationConfigurationStore(fixture.Paths);
        store.EnsureInitialized();
        store.SaveConfigured(InstallationMode.Consulta);
        store.SavePosSettings(new PosStationSettingsDocument(true));

        Assert.False(store.IsMachineWideSplit);
        Assert.Equal(fixture.Paths.StationSettingsPath, store.OperationalConfigurationPath);
        using var settings = JsonDocument.Parse(File.ReadAllText(fixture.Paths.StationSettingsPath));
        Assert.True(settings.RootElement.TryGetProperty("installationId", out _));
        Assert.True(settings.RootElement.TryGetProperty("pos", out _));
        Assert.Equal(fixture.Paths.StationSettingsPath, fixture.Paths.StationOperationalSettingsPath);
    }

    [Fact]
    public void StationAclPlan_ProtectsCoreAndAllowsOnlyOperationalModify()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new StationFixture();
        var plan = new MachineWideAclService().CreatePlan(fixture.Paths);
        var core = Assert.Single(plan.Scopes, scope => scope.Name == "Station");
        var operational = Assert.Single(plan.Scopes, scope => scope.Name == "StationOperational");

        Assert.Equal(MachineWideAclProfile.StationReadExecute, core.Profile);
        Assert.Equal(MachineWideAclProfile.StationOperationalModify, operational.Profile);
        var coreUser = Assert.Single(core.DirectoryRules, rule => rule.IdentitySid == plan.UsersSid);
        var operationalUser = Assert.Single(operational.DirectoryRules, rule => rule.IdentitySid == plan.UsersSid);
        Assert.True(coreUser.Rights.HasFlag(FileSystemRights.ReadAndExecute));
        Assert.False(coreUser.Rights.HasFlag(FileSystemRights.Modify));
        Assert.False(coreUser.Rights.HasFlag(FileSystemRights.Delete));
        Assert.True(operationalUser.Rights.HasFlag(FileSystemRights.Modify));
        Assert.All(
            core.DirectoryRules.Concat(core.FileRules).Concat(operational.DirectoryRules).Concat(operational.FileRules),
            rule => Assert.Equal(AccessControlType.Allow, rule.AccessType));
    }

    [Fact]
    public void ProtectedHelper_RejectsUntrustedRootsAndTraversalShapes()
    {
        using var fixture = new StationFixture();
        var unc = ArteInventariadoPathProvider.CreateMachineWide(
            @"\\server\share\ArteInventariado",
            Path.Combine(fixture.Root, "data-unc"),
            Path.Combine(fixture.Root, "user-unc"));

        Assert.Throws<InvalidDataException>(() =>
            MachineWideStationConfigurationHelper.ValidateManagedRoots(unc, allowSyntheticRoots: true));

        var overlapping = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(fixture.Root, "install"),
            Path.Combine(fixture.Root, "install", "data"),
            Path.Combine(fixture.Root, "user"));
        Assert.Throws<InvalidDataException>(() =>
            MachineWideStationConfigurationHelper.ValidateManagedRoots(overlapping, allowSyntheticRoots: true));
    }

    [Fact]
    public void ProtectedHelper_RejectsStationReparsePointWhenWindowsAllowsCreatingOne()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new StationFixture();
        var target = Path.Combine(fixture.Root, "station-target");
        var link = Path.Combine(fixture.Paths.MachineDataRoot, "Station");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(fixture.Paths.MachineDataRoot);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return;
        }

        Assert.Throws<InvalidDataException>(() =>
            MachineWideStationConfigurationHelper.ValidateManagedRoots(fixture.Paths, allowSyntheticRoots: true));
    }

    [Fact]
    public async Task UacCancellation_LeavesProtectedStationFilesUntouched()
    {
        using var fixture = new StationFixture();
        Directory.CreateDirectory(fixture.Paths.MachineInstallRoot);
        await File.WriteAllTextAsync(fixture.Paths.StationHelperPath, "helper");
        var store = new LocalStationConfigurationStore(fixture.Paths);
        store.EnsureInitialized();
        store.SaveConfigured(InstallationMode.Principal);
        var coreBefore = await File.ReadAllTextAsync(fixture.Paths.StationSettingsPath);
        var operationalBefore = File.Exists(fixture.Paths.StationOperationalSettingsPath)
            ? await File.ReadAllTextAsync(fixture.Paths.StationOperationalSettingsPath)
            : null;
        IReadOnlyList<string>? capturedArguments = null;
        var service = new MachineWideStationModeService(
            fixture.Paths,
            (_, arguments) =>
            {
                capturedArguments = arguments;
                return new MachineWideElevationResult(MachineWideElevationOutcome.Cancelled);
            },
            allowSyntheticRoots: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ChangeModeAsync(
            InstallationMode.Consulta,
            store.Load().Status.InstallationId,
            InstallationMode.Principal));

        Assert.NotNull(capturedArguments);
        Assert.Contains(MachineWideStationConfigurationHelper.WriteModeCommand, capturedArguments!);
        Assert.Contains("--expected-installation-id", capturedArguments!);
        Assert.Equal(coreBefore, await File.ReadAllTextAsync(fixture.Paths.StationSettingsPath));
        if (operationalBefore is null)
        {
            Assert.False(File.Exists(fixture.Paths.StationOperationalSettingsPath));
        }
        else
        {
            Assert.Equal(operationalBefore, await File.ReadAllTextAsync(fixture.Paths.StationOperationalSettingsPath));
        }
        Assert.Equal(InstallationMode.Principal, store.Load().Status.Mode);
    }

    private sealed class StationFixture : IDisposable
    {
        public StationFixture(bool legacy = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-station-e-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Paths = legacy
                ? ArteInventariadoPathProvider.CreateLegacy(Path.Combine(Root, "legacy"))
                : ArteInventariadoPathProvider.CreateMachineWide(
                    Path.Combine(Root, "Program Files", "ArteInventariado"),
                    Path.Combine(Root, "ProgramData", "ArteInventariado"),
                    Path.Combine(Root, "LocalAppData", "ArteInventariado"));
        }

        public string Root { get; }
        public ArteInventariadoPathProvider Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

#pragma warning restore CA1416
