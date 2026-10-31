using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Tests;

public sealed class ProtectedBackupServiceTests
{
    [Fact]
    public async Task ManualBackupUsesNarrowRunnerArgumentsAndRequiresCorrelatedHealthyStatus()
    {
        using var fixture = new Fixture();
        var launcher = new FakeLauncher((executable, arguments) =>
        {
            Assert.Equal(fixture.Paths.BackupRunnerPath, executable);
            Assert.Equal(new[] { "--manual", "--request-id" }, arguments.Take(2));
            var operationId = Guid.ParseExact(arguments[2], "D");
            var fileName = $"ArteInventariado_test_Manual_{operationId:N}.aibak";
            var path = Path.Combine(fixture.Paths.ProtectedBackupsRoot, fileName);
            var packageBytes = new byte[] { 0x41, 0x49 };
            File.WriteAllBytes(path, packageBytes);
            var packageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes));
            new MachineWideBackupPolicyStore().SaveStatus(
                fixture.Paths,
                new MachineWideBackupStatus(
                    MachineWideBackupPolicyStore.CurrentSchemaVersion,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    fileName,
                    packageBytes.Length,
                    packageHash,
                    null,
                    7,
                    4,
                    3,
                    MachineWideBackupStatusKind.SuccessHealthy,
                    0,
                    operationId,
                    BackupKind.Manual));
            return new ProtectedBackupProcessResult(UacCancelled: false, ExitCode: 0);
        });
        var service = fixture.CreateService(launcher);

        var result = await service.CreateManualBackupAsync();

        Assert.Equal(ProtectedBackupOutcome.Succeeded, result.Outcome);
        Assert.NotEqual(Guid.Empty, result.OperationId);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(2, result.SizeBytes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 0x41, 0x49 })), result.Sha256);
        Assert.Equal(1, launcher.Calls);
    }

    [Fact]
    public async Task ManualBackupRejectsFileThatDoesNotMatchCorrelatedHash()
    {
        using var fixture = new Fixture();
        var launcher = new FakeLauncher((_, arguments) =>
        {
            var operationId = Guid.ParseExact(arguments[2], "D");
            var fileName = $"ArteInventariado_test_Manual_{operationId:N}.aibak";
            var path = Path.Combine(fixture.Paths.ProtectedBackupsRoot, fileName);
            File.WriteAllBytes(path, [0x41, 0x49]);
            new MachineWideBackupPolicyStore().SaveStatus(
                fixture.Paths,
                new MachineWideBackupStatus(
                    MachineWideBackupPolicyStore.CurrentSchemaVersion,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    fileName,
                    2,
                    new string('A', 64),
                    null,
                    7,
                    4,
                    3,
                    MachineWideBackupStatusKind.SuccessHealthy,
                    0,
                    operationId,
                    BackupKind.Manual));
            return new ProtectedBackupProcessResult(UacCancelled: false, ExitCode: 0);
        });
        var service = fixture.CreateService(launcher);

        var result = await service.CreateManualBackupAsync();

        Assert.Equal(ProtectedBackupOutcome.Failed, result.Outcome);
        Assert.Contains("SHA-256", result.Message, StringComparison.Ordinal);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public async Task UacCancellationDoesNotReportOrCreateBackup()
    {
        using var fixture = new Fixture();
        var launcher = new FakeLauncher((_, _) =>
            new ProtectedBackupProcessResult(UacCancelled: true, ExitCode: null));
        var service = fixture.CreateService(launcher);

        var result = await service.CreateManualBackupAsync();

        Assert.Equal(ProtectedBackupOutcome.Cancelled, result.Outcome);
        Assert.Empty(Directory.EnumerateFiles(fixture.Paths.ProtectedBackupsRoot, "*.aibak"));
        Assert.Equal(1, launcher.Calls);
    }

    [Fact]
    public async Task ExitZeroWithoutMatchingRequestStatusIsNotReportedAsSuccess()
    {
        using var fixture = new Fixture();
        var launcher = new FakeLauncher((_, _) =>
            new ProtectedBackupProcessResult(UacCancelled: false, ExitCode: 0));
        var service = fixture.CreateService(launcher);

        var result = await service.CreateManualBackupAsync();

        Assert.Equal(ProtectedBackupOutcome.Failed, result.Outcome);
        Assert.Contains("no dejó un resultado correlacionado", result.Message, StringComparison.Ordinal);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public async Task AuthorizationFailureDoesNotLaunchElevatedRunner()
    {
        using var fixture = new Fixture();
        var launcher = new FakeLauncher((_, _) =>
            throw new InvalidOperationException("El runner no debe iniciar."));
        var service = fixture.CreateService(
            launcher,
            _ => Task.FromException(new UnauthorizedAccessException("permiso requerido")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateManualBackupAsync());

        Assert.Equal(0, launcher.Calls);
    }

    [Fact]
    public void ManualArgumentsContainOnlyRequestKindAndOpaqueCorrelationId()
    {
        var operationId = Guid.NewGuid();

        var arguments = ProtectedBackupService.BuildManualArguments(operationId);

        Assert.Equal(new[] { "--manual", "--request-id", operationId.ToString("D") }, arguments);
        Assert.DoesNotContain("--destination", arguments);
        Assert.DoesNotContain("--database", arguments);
        Assert.DoesNotContain("password", string.Join(" ", arguments), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeLauncher(
        Func<string, IReadOnlyList<string>, ProtectedBackupProcessResult> launch) : IProtectedBackupProcessLauncher
    {
        public int Calls { get; private set; }

        public Task<ProtectedBackupProcessResult> LaunchAndWaitAsync(
            string executablePath,
            ArteInventariado.Infrastructure.Updater.UpdateRuntimePaths runtimePaths,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(launch(executablePath, arguments));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"ArteInventariado-protected-backup-{Guid.NewGuid():N}");
        public ArteInventariadoPathProvider Paths { get; }

        public Fixture()
        {
            Paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(Root, "install"),
                Path.Combine(Root, "data"),
                Path.Combine(Root, "user"));
            Directory.CreateDirectory(Paths.MachineInstallRoot);
            Directory.CreateDirectory(Paths.MachineDataRoot);
            Directory.CreateDirectory(Paths.StationRoot);
            Directory.CreateDirectory(Paths.ProtectedBackupsRoot);
            Directory.CreateDirectory(Paths.UserDataRoot);
            File.WriteAllBytes(Paths.BackupRunnerPath, [0x4D, 0x5A]);
            new MachineWideBackupPolicyStore().EnsureInitialized(Paths);
        }

        public ProtectedBackupService CreateService(
            IProtectedBackupProcessLauncher launcher,
            Func<CancellationToken, Task>? authorize = null) =>
            new(
                Paths,
                authorize ?? (_ => Task.CompletedTask),
                launcher,
                allowSyntheticRoots: true);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
