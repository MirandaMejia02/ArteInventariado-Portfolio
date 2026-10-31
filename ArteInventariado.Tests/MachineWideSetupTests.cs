using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;

namespace ArteInventariado.Tests;

public sealed class MachineWideSetupTests
{
    private static string CurrentProductVersion =>
        System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(MachineWideSetupTests).Assembly.Location)
            .ProductVersion
        ?? throw new InvalidDataException("El assembly de pruebas no declara ProductVersion.");

    [Fact]
    public void ProtectedTaskQueryUsesValidSchtasksListSyntax()
    {
        var arguments = MachineWideInstallCleanup.BuildTaskQueryArguments(
            "\\ArteInventariado\\BackupProtegido");

        Assert.Equal(
            ["/Query", "/TN", "\\ArteInventariado\\BackupProtegido", "/FO", "LIST"],
            arguments);
        Assert.DoesNotContain("/NH", arguments, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CONFIRMAR", true)]
    [InlineData(" confirmar ", true)]
    [InlineData("confirmar", true)]
    [InlineData("CANCELAR", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CleanInstallConfirmationRequiresTheExplicitWord(
        string? value,
        bool expected)
    {
        Assert.Equal(expected, MachineWideSetupService.IsCleanConfirmationValid(value));
    }

    [Fact]
    public async Task SetupFailurePersistsTheExactStageWithoutLoggingSecrets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SetupFixture();
        var installation = new MachineWideInstallationService(
            new MachineWideBootstrapService(),
            new MachineWideAclService(allowSyntheticRoots: true),
            () => true,
            new MachineWideBackupPolicyStore(),
            new MachineWideBackupTaskScheduler(_ => 0, allowSyntheticRoots: true),
            new MachineWideCloudBackupTaskScheduler(_ => 0, allowSyntheticRoots: true),
            runId: Guid.NewGuid());
        var setup = new MachineWideSetupService(installation);
        var request = new MachineWideSetupRequest(
            Path.Combine(fixture.Paths.MachineInstallRoot, "missing-payload"),
            CleanInstall: true,
            ConfirmedCleanInstall: true,
            ProvisionPrincipal: false,
            StartLauncherAfterInstall: false,
            ExpectedVersion: CurrentProductVersion,
            ShortcutLocations: fixture.Shortcuts);

        var error = await Assert.ThrowsAsync<MachineWideSetupFailureException>(() =>
            setup.InstallSyntheticAsync(fixture.Paths, request));

        Assert.Equal("payload-validating", error.Stage);
        Assert.True(File.Exists(error.DiagnosticPath));
        var log = File.ReadAllText(error.DiagnosticPath);
        Assert.Contains("stage=payload-validating", log, StringComparison.Ordinal);
        Assert.Contains("| EXIT | 1 |", log, StringComparison.Ordinal);
        Assert.DoesNotContain("password=", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret=", log, StringComparison.OrdinalIgnoreCase);

        File.Delete(error.DiagnosticPath);
    }

    [Fact]
    public async Task FreshSyntheticMachineWideFixtureCreatesNewDatabaseAndIdentityWithoutPrincipal()
    {
        using var fixture = new SetupFixture();

        var result = await MachineWideDatabaseInitializer.InitializeSyntheticAsync(fixture.Paths);

        Assert.True(result.DatabaseCreated);
        Assert.False(result.PrincipalExists);
        Assert.NotEqual(Guid.Empty, result.InstallationId);
        Assert.Null(result.StationMode);
        Assert.Equal(StationConfigurationState.RequiresSetup, result.StationState);
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
        Assert.True(File.Exists(fixture.Paths.StationSettingsPath));

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Paths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        Assert.Equal("ok", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CompleteSyntheticSetupPromotesPayloadCreatesDatabaseAndCanBeRepairedAfterBinaryUninstall()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SetupFixture();
        var payload = fixture.CreateMinimalPayload();
        var runId = Guid.NewGuid();
        var backupScheduler = new MachineWideBackupTaskScheduler(
            _ => 0,
            allowSyntheticRoots: true);
        var cloudScheduler = new MachineWideCloudBackupTaskScheduler(
            _ => 0,
            allowSyntheticRoots: true);
        var installation = new MachineWideInstallationService(
            new MachineWideBootstrapService(),
            new MachineWideAclService(allowSyntheticRoots: true),
            () => true,
            new MachineWideBackupPolicyStore(),
            backupScheduler,
            cloudScheduler,
            runId: runId);
        var setup = new MachineWideSetupService(installation);
        var request = new MachineWideSetupRequest(
            payload,
            ProvisionPrincipal: false,
            StartLauncherAfterInstall: false,
            ExpectedVersion: CurrentProductVersion,
            ShortcutLocations: fixture.Shortcuts);

        var installed = await setup.InstallSyntheticAsync(fixture.Paths, request);

        Assert.Equal(CurrentProductVersion, installed.Version);
        Assert.True(installed.DatabaseCreated);
        Assert.False(installed.PrincipalProvisioned);
        Assert.True(installed.StationConfiguredPrincipal is false);
        Assert.True(installed.BackupTaskInstalled);
        Assert.True(installed.CloudTaskInstalled);
        Assert.True(File.Exists(fixture.Paths.CurrentStatePath));
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
        Assert.True(File.Exists(fixture.Paths.BackupRunnerPath));

        var uninstall = MachineWideInstallCleanup.RemoveInstallationOnly(
            fixture.Paths,
            allowSyntheticRoots: true);
        Assert.True(uninstall.InstallRootRemoved);
        Assert.True(File.Exists(fixture.Paths.DatabasePath));

        // The setup's repair/reinstall path is allowed when the binary root
        // is gone after a normal uninstall and the data roots remain.
        var repaired = await setup.InstallSyntheticAsync(fixture.Paths, request);
        Assert.False(repaired.DatabaseCreated);
        Assert.Equal(installed.InstallationId, repaired.InstallationId);
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task ExplicitSyntheticCleanInstallRequiresConfirmationAndRecreatesDatabaseAndIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SetupFixture();
        var payload = fixture.CreateMinimalPayload();
        var installation = new MachineWideInstallationService(
            new MachineWideBootstrapService(),
            new MachineWideAclService(allowSyntheticRoots: true),
            () => true,
            new MachineWideBackupPolicyStore(),
            new MachineWideBackupTaskScheduler(_ => 0, allowSyntheticRoots: true),
            new MachineWideCloudBackupTaskScheduler(_ => 0, allowSyntheticRoots: true),
            runId: Guid.NewGuid());
        var setup = new MachineWideSetupService(installation);
        var initialRequest = new MachineWideSetupRequest(
            payload,
            ProvisionPrincipal: false,
            StartLauncherAfterInstall: false,
            ExpectedVersion: CurrentProductVersion,
            ShortcutLocations: fixture.Shortcuts);

        var first = await setup.InstallSyntheticAsync(fixture.Paths, initialRequest);
        File.WriteAllText(Path.Combine(fixture.Paths.MachineDataRoot, "business-data.marker"), "old");
        var oldInstallationId = first.InstallationId;

        await Assert.ThrowsAsync<CleanInstallConfirmationRequiredException>(() =>
            setup.InstallSyntheticAsync(fixture.Paths, initialRequest with { CleanInstall = true }));

        var clean = await setup.InstallSyntheticAsync(
            fixture.Paths,
            initialRequest with
            {
                CleanInstall = true,
                ConfirmedCleanInstall = true
            });

        Assert.True(clean.CleanPerformed);
        Assert.True(clean.DatabaseCreated);
        Assert.NotEqual(oldInstallationId, clean.InstallationId);
        Assert.False(File.Exists(Path.Combine(fixture.Paths.MachineDataRoot, "business-data.marker")));
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
    }

    [Fact]
    public void CleanCleanupDeletesOnlyTheThreeManagedRootsAndPreservesOutsideSentinel()
    {
        using var fixture = new SetupFixture();
        Directory.CreateDirectory(fixture.Paths.MachineInstallRoot);
        Directory.CreateDirectory(fixture.Paths.MachineDataRoot);
        Directory.CreateDirectory(fixture.Paths.UserDataRoot);
        File.WriteAllText(Path.Combine(fixture.Paths.MachineInstallRoot, "marker.txt"), "installer");
        File.WriteAllText(fixture.OutsideSentinel, "preserve");

        var result = MachineWideInstallCleanup.RemoveAllManagedState(
            fixture.Paths,
            allowSyntheticRoots: true);

        Assert.True(result.InstallRootRemoved);
        Assert.True(result.DataRootRemoved);
        Assert.True(result.UserRootRemoved);
        Assert.False(Directory.Exists(fixture.Paths.MachineInstallRoot));
        Assert.False(Directory.Exists(fixture.Paths.MachineDataRoot));
        Assert.False(Directory.Exists(fixture.Paths.UserDataRoot));
        Assert.Equal("preserve", File.ReadAllText(fixture.OutsideSentinel));
    }

    [Fact]
    public void NormalUninstallRemovesInstallRootButPreservesMachineDataAndUserData()
    {
        using var fixture = new SetupFixture();
        Directory.CreateDirectory(fixture.Paths.MachineInstallRoot);
        Directory.CreateDirectory(fixture.Paths.MachineDataRoot);
        Directory.CreateDirectory(fixture.Paths.UserDataRoot);
        File.WriteAllText(Path.Combine(fixture.Paths.MachineInstallRoot, "binary.exe"), "binary");
        File.WriteAllText(Path.Combine(fixture.Paths.MachineDataRoot, "data.txt"), "data");
        File.WriteAllText(Path.Combine(fixture.Paths.UserDataRoot, "prefs.txt"), "prefs");

        var result = MachineWideInstallCleanup.RemoveInstallationOnly(
            fixture.Paths,
            allowSyntheticRoots: true);

        Assert.True(result.InstallRootRemoved);
        Assert.False(result.DataRootRemoved);
        Assert.False(result.UserRootRemoved);
        Assert.False(Directory.Exists(fixture.Paths.MachineInstallRoot));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.MachineDataRoot, "data.txt")));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.UserDataRoot, "prefs.txt")));
    }

    [Fact]
    public void CleanInstallConfirmationExceptionCarriesTheExplicitWarning()
    {
        var error = new CleanInstallConfirmationRequiredException();

        Assert.Contains(
            MachineWideSetupService.CleanInstallConfirmationText,
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("CONFIRMAR", error.Message, StringComparison.Ordinal);
    }

    private sealed class SetupFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-setup-fixture-{Guid.NewGuid():N}");

        public SetupFixture()
        {
            Paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(_root, "Program Files", "ArteInventariado"),
                Path.Combine(_root, "ProgramData", "ArteInventariado"),
                Path.Combine(_root, "LocalAppData", "ArteInventariado"));
            OutsideSentinel = Path.Combine(_root, "outside.txt");
        }

        public ArteInventariadoPathProvider Paths { get; }

        public string OutsideSentinel { get; }

        public ShortcutLocations Shortcuts => new(
            Path.Combine(_root, "Desktop"),
            Path.Combine(_root, "Programs"));

        public string CreateMinimalPayload()
        {
            var payload = Path.Combine(_root, "payload");
            Directory.CreateDirectory(payload);
            var source = typeof(MachineWideSetupTests).Assembly.Location;
            foreach (var fileName in new[]
                     {
                         UpdateLaunchTargetResolver.ApplicationExecutableName,
                         StableDeploymentBootstrap.LauncherExecutableName,
                         StableDeploymentBootstrap.UpdaterExecutableName,
                         ArteInventariadoPathProvider.StationHelperFileName,
                         ArteInventariadoPathProvider.BackupRunnerFileName,
                         ArteInventariadoPathProvider.CloudBackupRunnerFileName
                     })
            {
                File.Copy(source, Path.Combine(payload, fileName));
            }

            return payload;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
