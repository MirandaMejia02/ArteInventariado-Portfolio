using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class MachineWideBackupTests
{
    [Fact]
    public void PolicyDefaultsToProtectedDestinationAndIgnoresOperationalPreference()
    {
        using var fixture = new Fixture();
        var policyStore = new MachineWideBackupPolicyStore();
        var policy = policyStore.EnsureInitialized(fixture.Paths);
        var station = new LocalStationConfigurationStore(fixture.Paths);
        station.EnsureInitialized();
        station.SaveBackupSettings(new BackupStationSettingsDocument(
            AutomaticEnabled: false,
            DestinationDirectory: Path.Combine(fixture.Root, "user-selected"),
            Frequency: nameof(BackupFrequency.Monthly),
            AutomaticRetentionCount: 1,
            LastAutomaticBackupUtc: null,
            LastAutomaticError: null));

        var loaded = policyStore.Load(fixture.Paths);
        Assert.Equal(fixture.Paths.ProtectedBackupsRoot, policy.DestinationDirectory);
        Assert.Equal(policy, loaded);
        Assert.True(loaded.Enabled);
        Assert.Equal(7, loaded.DailyGenerations);
        Assert.Equal(4, loaded.WeeklyGenerations);
        Assert.Equal(3, loaded.MonthlyGenerations);
        Assert.Equal(MachineWideBackupPolicyStore.LocalSystemSid, loaded.TaskIdentitySid);
        Assert.Equal(fixture.Paths.ProtectedBackupsRoot, policyStore.Load(fixture.Paths).DestinationDirectory);
    }

    [Fact]
    public void PolicyRejectsDestinationAndIdentityTampering()
    {
        using var fixture = new Fixture();
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths);
        Assert.Throws<InvalidDataException>(() => MachineWideBackupPolicyStore.Validate(
            fixture.Paths,
            policy with { DestinationDirectory = Path.Combine(fixture.Root, "outside") }));
        Assert.Throws<InvalidDataException>(() => MachineWideBackupPolicyStore.Validate(
            fixture.Paths,
            policy with { TaskIdentitySid = "S-1-5-21-unsafe" }));
        Assert.Throws<InvalidDataException>(() => MachineWideBackupPolicyStore.Validate(
            fixture.Paths,
            policy with { DailyGenerations = 0 }));
    }

    [Fact]
    public void RetentionKeepsSevenDaysFourWeeksThreeMonthsAndNeverDeletesNewest()
    {
        using var fixture = new Fixture();
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths);
        var candidates = Enumerable.Range(0, 400)
            .Select(daysAgo => new AutomaticBackupCandidate(
                Path.Combine(fixture.Paths.ProtectedBackupsRoot, $"{daysAgo:D3}.aibak"),
                Manifest(DateTimeOffset.UtcNow.AddDays(-daysAgo))))
            .ToArray();

        var plan = MachineWideBackupRetention.BuildPlan(candidates, policy);

        Assert.Equal(7, plan.DailyGenerationsKept);
        Assert.Equal(4, plan.WeeklyGenerationsKept);
        Assert.Equal(3, plan.MonthlyGenerationsKept);
        Assert.Contains(candidates[0].FilePath, plan.KeptFiles);
        Assert.NotEmpty(plan.DeletableFiles);
        Assert.DoesNotContain(candidates[0].FilePath, plan.DeletableFiles);
    }

    [Fact]
    public void TaskDefinitionUsesSystemStartWhenAvailableAndNoParallelRuns()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Paths.BackupRunnerPath, "runner");
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths);
        var xml = XDocument.Parse(MachineWideBackupTaskScheduler.BuildTaskXml(fixture.Paths, policy));
        var text = xml.ToString(SaveOptions.DisableFormatting);

        Assert.Contains(MachineWideBackupPolicyStore.LocalSystemSid, text, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceAccount", text, StringComparison.Ordinal);
        Assert.Contains("StartWhenAvailable", text, StringComparison.Ordinal);
        Assert.Contains("IgnoreNew", text, StringComparison.Ordinal);
        Assert.Contains("RestartOnFailure", text, StringComparison.Ordinal);
        Assert.Contains("--run", text, StringComparison.Ordinal);
        Assert.Contains(fixture.Paths.BackupRunnerPath, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationUsesSystemArgumentAndPersistsProcessResult()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Paths.BackupRunnerPath, "runner");
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths);
        ProcessStartInfo? captured = null;
        var scheduler = new MachineWideBackupTaskScheduler(
            startInfo =>
            {
                captured = startInfo;
                return new MachineWideTaskSchedulerProcessResult(0, "Created", string.Empty);
            },
            allowSyntheticRoots: true);

        scheduler.EnsureRegistered(fixture.Paths, policy);

        Assert.NotNull(captured);
        Assert.Contains("/RU", captured!.ArgumentList);
        Assert.Contains("SYSTEM", captured.ArgumentList);
        Assert.Contains(policy.TaskName, captured.ArgumentList);
        var log = File.ReadAllText(Path.Combine(fixture.Paths.UpdateLogsRoot, "machine-bootstrap.log"));
        Assert.Contains("TASK", log, StringComparison.Ordinal);
        Assert.Contains($"task={policy.TaskName}", log, StringComparison.Ordinal);
        Assert.Contains("exit=0", log, StringComparison.Ordinal);
        Assert.Contains("stdout=Created", log, StringComparison.Ordinal);
        Assert.Contains("stderr=sin detalle", log, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationFailureCapturesSanitizedStderrAndRemainsFailure()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Paths.BackupRunnerPath, "runner");
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths);
        var scheduler = new MachineWideBackupTaskScheduler(
            _ => new MachineWideTaskSchedulerProcessResult(
                1,
                string.Empty,
                "Error: (18,35):LogonType:ServiceAccount"),
            allowSyntheticRoots: true);

        var error = Assert.Throws<InvalidOperationException>(
            () => scheduler.EnsureRegistered(fixture.Paths, policy));

        Assert.Contains("schtasks 1", error.Message, StringComparison.Ordinal);
        var log = File.ReadAllText(Path.Combine(fixture.Paths.UpdateLogsRoot, "machine-bootstrap.log"));
        Assert.Contains("TASK", log, StringComparison.Ordinal);
        Assert.Contains("exit=1", log, StringComparison.Ordinal);
        Assert.Contains("stderr=Error: (18,35):LogonType:ServiceAccount", log, StringComparison.Ordinal);
        Assert.DoesNotContain("SUCCESS", log, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationCanBeRetriedIdempotentlyWithStableSystemContract()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Paths.BackupRunnerPath, "runner");
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths);
        var calls = 0;
        var scheduler = new MachineWideBackupTaskScheduler(
            startInfo =>
            {
                calls++;
                return new MachineWideTaskSchedulerProcessResult(0, "ok", string.Empty);
            },
            allowSyntheticRoots: true);

        scheduler.EnsureRegistered(fixture.Paths, policy);
        scheduler.EnsureRegistered(fixture.Paths, policy);

        Assert.Equal(2, calls);
    }

    [Fact]
    public void RegistrationSuccessWithoutPhysicalTaskVerificationRemainsFailure()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Paths.BackupRunnerPath, "runner");
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths);
        var scheduler = new MachineWideBackupTaskScheduler(
            _ => new MachineWideTaskSchedulerProcessResult(0, "Created", string.Empty),
            (_, _, _) => false,
            allowSyntheticRoots: true);

        var error = Assert.Throws<InvalidOperationException>(
            () => scheduler.EnsureRegistered(fixture.Paths, policy));

        Assert.Contains("verificación física", error.Message, StringComparison.OrdinalIgnoreCase);
        var log = File.ReadAllText(Path.Combine(fixture.Paths.UpdateLogsRoot, "machine-bootstrap.log"));
        Assert.Contains("TASK-VERIFY", log, StringComparison.Ordinal);
        Assert.Contains("verified=false", log, StringComparison.Ordinal);
        Assert.DoesNotContain("SUCCESS", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunnerRequiresProtectedExecutionAndSkipsConcurrentOrDisabledRuns()
    {
        using var fixture = new Fixture();
        var policyStore = new MachineWideBackupPolicyStore();
        var policy = policyStore.EnsureInitialized(fixture.Paths);
        var denied = new MachineWideBackupRunner(
            fixture.Paths,
            new BackupPackageEngine(),
            policyStore,
            TimeProvider.System,
            () => false,
            _ => new TestLock(acquired: true),
            allowSyntheticRoots: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.RunAsync());

        var concurrent = new MachineWideBackupRunner(
            fixture.Paths,
            new BackupPackageEngine(),
            policyStore,
            TimeProvider.System,
            () => true,
            _ => new TestLock(acquired: false),
            allowSyntheticRoots: true);
        var concurrentResult = await concurrent.RunAsync();
        Assert.Equal(MachineWideBackupRunOutcome.SkippedConcurrent, concurrentResult.Outcome);

        var disabledDocument = JsonSerializer.Serialize(
            policy with { Enabled = false },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(fixture.Paths.ProtectedBackupPolicyPath, disabledDocument);
        var disabled = new MachineWideBackupRunner(
            fixture.Paths,
            new BackupPackageEngine(),
            policyStore,
            TimeProvider.System,
            () => true,
            _ => new TestLock(acquired: true),
            allowSyntheticRoots: true);
        var disabledResult = await disabled.RunAsync();
        Assert.Equal(MachineWideBackupRunOutcome.SkippedDisabled, disabledResult.Outcome);
    }

    [Fact]
    public async Task RunnerCreatesProtectedBackupFromValidOlderSchemaPrefix()
    {
        using var fixture = new Fixture();
        var policyStore = new MachineWideBackupPolicyStore();
        policyStore.EnsureInitialized(fixture.Paths);
        var station = new LocalStationConfigurationStore(fixture.Paths);
        station.EnsureInitialized();
        station.SaveConfigured(InstallationMode.Principal);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={fixture.Paths.DatabasePath};Pooling=False")
            .Options;
        await using (var context = new AppDbContext(options))
        {
            await context.Database.MigrateAsync("20260825055759_V110OperationalSkuIntegrations");
        }

        var runner = new MachineWideBackupRunner(
            fixture.Paths,
            new BackupPackageEngine(),
            policyStore,
            TimeProvider.System,
            () => true,
            _ => new TestLock(acquired: true),
            allowSyntheticRoots: true);

        var result = await runner.RunAsync();

        Assert.Equal(MachineWideBackupRunOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.FilePath);
        Assert.True(File.Exists(result.FilePath));
        Assert.NotNull(result.Manifest);
        Assert.Equal(
            "20260825055759_V110OperationalSkuIntegrations",
            result.Manifest!.LastMigration);
        Assert.Equal(22, result.Manifest.AppliedMigrations.Count);
        Assert.False(string.IsNullOrWhiteSpace(result.Sha256));
    }

    [Fact]
    public async Task ManualRunnerCreatesCorrelatedManualPackageWithoutApplyingAutomaticRetention()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync();
        var operationId = Guid.NewGuid();

        var result = await runner.RunAsync(BackupKind.Manual, operationId);

        Assert.Equal(MachineWideBackupRunOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Manifest);
        Assert.Equal(BackupKind.Manual, result.Manifest!.Kind);
        Assert.Contains("_Manual_", Path.GetFileName(result.FilePath), StringComparison.Ordinal);
        Assert.Null(result.Retention);
        Assert.True(File.Exists(result.FilePath));

        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(operationId, status.LastOperationId);
        Assert.Equal(BackupKind.Manual, status.LastBackupKind);
        Assert.Equal(MachineWideBackupStatusKind.SuccessHealthy, status.LastOutcome);
        Assert.Equal(Path.GetFileName(result.FilePath), status.LastSuccessfulFileName);
    }

    [Fact]
    public async Task ManualRunnerRequiresNonEmptyCorrelationId()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(BackupKind.Manual));
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(BackupKind.Manual, Guid.Empty));
    }

    [Fact]
    public async Task HealthyCompletionPersistsHealthyStatusAndReturnsExitZero()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync();

        var exitCode = await RunAsProcessAsync(runner);

        Assert.Equal(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.SuccessHealthy, status.LastOutcome);
        Assert.NotNull(status.LastSuccessfulBackupUtc);
        Assert.Null(status.LastError);
        Assert.Single(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ProcessBoundaryFailureBeforeRunnerIsDiagnosedWithoutStartOrBackup()
    {
        using var fixture = new Fixture();
        var policyStore = new MachineWideBackupPolicyStore();
        policyStore.EnsureInitialized(fixture.Paths);
        policyStore.SaveStatus(
            fixture.Paths,
            new MachineWideBackupStatus(
                MachineWideBackupPolicyStore.CurrentSchemaVersion,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                "previous.aibak",
                10,
                "AA",
                null,
                7,
                4,
                3,
                MachineWideBackupStatusKind.SuccessHealthy,
                0));

        var standardError = new StringWriter();
        var exitCode = await MachineWideBackupProcess.RunAsync(
            () => Task.FromException<MachineWideBackupRunResult>(
                new InvalidDataException("Fallo sintetico antes de entrar al runner.")),
            TextWriter.Null,
            standardError,
            exception => MachineWideBackupDiagnostics.RecordEarlyFailure(
                fixture.Paths,
                "construccion-runner",
                exception));

        Assert.Equal(1, exitCode);
        Assert.Contains("InvalidDataException", standardError.ToString(), StringComparison.Ordinal);
        var status = policyStore.LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Contains("construccion-runner", status.LastError, StringComparison.Ordinal);
        Assert.Equal("previous.aibak", status.LastSuccessfulFileName);
        Assert.Empty(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));

        var audit = await File.ReadAllTextAsync(
            StationConfigurationPathProvider.GetAuditLogPath(fixture.Paths));
        Assert.DoesNotContain("BACKUP_RUNNER_STARTED", audit, StringComparison.Ordinal);
        Assert.Contains("BACKUP_RUNNER_PROCESS_FAILED_BEFORE_START", audit, StringComparison.Ordinal);

        var diagnostics = await File.ReadAllTextAsync(
            MachineWideBackupDiagnostics.GetLogPath(fixture.Paths));
        Assert.Contains("stage=construccion-runner", diagnostics, StringComparison.Ordinal);
        Assert.Contains("pid=", diagnostics, StringComparison.Ordinal);
        Assert.Contains("executable=", diagnostics, StringComparison.Ordinal);
        Assert.Contains("tokenElevated=", diagnostics, StringComparison.Ordinal);
        Assert.Contains("temp=", diagnostics, StringComparison.Ordinal);
        Assert.Contains("tmp=", diagnostics, StringComparison.Ordinal);
        Assert.Contains("bundleExtractBase=", diagnostics, StringComparison.Ordinal);
        Assert.Contains("InvalidDataException", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeNamedLockCanBeReleasedAfterAsyncBackupCompletion()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync(useNativeNamedLock: true);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            MachineWideBackupStatusKind.SuccessHealthy,
            new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths).LastOutcome);
    }

    [Fact]
    public async Task EarlyPolicyFailureIsLoggedAndPersistsFailureStatus()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync();
        await File.WriteAllTextAsync(fixture.Paths.ProtectedBackupPolicyPath, "{");

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Contains("carga-politica", status.LastError, StringComparison.Ordinal);
        Assert.Null(status.LastSuccessfulBackupUtc);
        Assert.Empty(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));

        var audit = await File.ReadAllTextAsync(
            StationConfigurationPathProvider.GetAuditLogPath(fixture.Paths));
        Assert.Contains("BACKUP_RUNNER_STARTED", audit, StringComparison.Ordinal);
        Assert.Contains("BACKUP_RUNNER_FAILED", audit, StringComparison.Ordinal);
        Assert.Contains("carga-politica", audit, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidDataException), audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LockAcquisitionFailureIsLoggedAndPersistsFailureStatus()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync(throwOnAcquire: true);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Contains("adquisicion-lock", status.LastError, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));

        var audit = await File.ReadAllTextAsync(
            StationConfigurationPathProvider.GetAuditLogPath(fixture.Paths));
        Assert.Contains("BACKUP_RUNNER_FAILED", audit, StringComparison.Ordinal);
        Assert.Contains("adquisicion-lock", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidDatabaseIsLoggedAndCannotLeaveHealthyStatus()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync();
        await File.WriteAllTextAsync(fixture.Paths.DatabasePath, "not a sqlite database");

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Contains("creacion-validacion-publicacion", status.LastError, StringComparison.Ordinal);
        Assert.Null(status.LastSuccessfulBackupUtc);
        Assert.Empty(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task MissingDatabaseIsLoggedAndCannotLeaveHealthyStatus()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync();
        File.Delete(fixture.Paths.DatabasePath);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Contains("creacion-validacion-publicacion", status.LastError, StringComparison.Ordinal);
        Assert.Null(status.LastSuccessfulBackupUtc);
    }

    [Fact]
    public async Task InvalidProtectedDestinationIsLoggedBeforeAnyBackupIsPublished()
    {
        using var fixture = new Fixture();
        var policyStore = new MachineWideBackupPolicyStore();
        var policy = policyStore.EnsureInitialized(fixture.Paths);
        var runner = await fixture.CreateReadyRunnerAsync();
        await File.WriteAllTextAsync(
            fixture.Paths.ProtectedBackupPolicyPath,
            JsonSerializer.Serialize(
                policy with { DestinationDirectory = Path.Combine(fixture.Root, "outside") },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = policyStore.LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Contains("carga-politica", status.LastError, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ExceptionDuringClockStillPersistsFailureDiagnostic()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync(timeProvider: new ThrowingTimeProvider());

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Contains("reloj", status.LastError, StringComparison.Ordinal);
        var audit = await File.ReadAllTextAsync(
            StationConfigurationPathProvider.GetAuditLogPath(fixture.Paths));
        Assert.Contains("BACKUP_RUNNER_STARTED", audit, StringComparison.Ordinal);
        Assert.Contains("BACKUP_RUNNER_FAILED", audit, StringComparison.Ordinal);
        Assert.Contains("reloj", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusPersistenceFailureReturnsNonZeroAndNeverReportsNewHealthyRun()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync();
        Directory.CreateDirectory(fixture.Paths.ProtectedBackupStatusPath);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        Assert.Single(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
        var audit = await File.ReadAllTextAsync(
            StationConfigurationPathProvider.GetAuditLogPath(fixture.Paths));
        Assert.Contains("BACKUP_STATUS_WRITE_FAILED", audit, StringComparison.Ordinal);
        Assert.Contains("BACKUP_RUNNER_FAILED", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("BACKUP_RUNNER_SUCCEEDED", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LockCleanupFailureCannotPersistHealthyStatus()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync(throwOnDispose: true);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Null(status.LastSuccessfulBackupUtc);
        Assert.Single(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task FailureBeforePublishingPersistsFailureAndReturnsNonZero()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync(
            MachineWideBackupRunnerCheckpoint.BeforePublish);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(
            MachineWideBackupStatusKind.Failure,
            new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths).LastOutcome);
        Assert.Empty(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task FailureImmediatelyAfterPublishingRemovesFileAndKeepsStatusAndExitCoherent()
    {
        using var fixture = new Fixture();
        var runner = await fixture.CreateReadyRunnerAsync(
            MachineWideBackupRunnerCheckpoint.AfterPublish);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(
            MachineWideBackupStatusKind.Failure,
            new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths).LastOutcome);
        Assert.Empty(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData((int)MachineWideBackupRunnerCheckpoint.BeforeRetention)]
    [InlineData((int)MachineWideBackupRunnerCheckpoint.BeforeSuccessStatus)]
    public async Task RetentionOrFinalizationFailureCannotPersistFalseSuccess(int checkpointValue)
    {
        using var fixture = new Fixture();
        var checkpoint = (MachineWideBackupRunnerCheckpoint)checkpointValue;
        var runner = await fixture.CreateReadyRunnerAsync(checkpoint);

        var exitCode = await RunAsProcessAsync(runner);

        Assert.NotEqual(0, exitCode);
        var status = new MachineWideBackupPolicyStore().LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.Failure, status.LastOutcome);
        Assert.Null(status.LastSuccessfulBackupUtc);
        Assert.Single(Directory.EnumerateFiles(
            fixture.Paths.ProtectedBackupsRoot,
            "*.aibak",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task PreUpdateBackupCreatorAcceptsValidOlderSchemaPrefix()
    {
        using var fixture = new Fixture();
        var station = new LocalStationConfigurationStore(fixture.Paths);
        station.EnsureInitialized();
        station.SaveConfigured(InstallationMode.Principal);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={fixture.Paths.DatabasePath};Pooling=False")
            .Options;
        await using (var context = new AppDbContext(options))
        {
            await context.Database.MigrateAsync("20260825055759_V110OperationalSkuIntegrations");
        }

        var paths = UpdateRuntimePaths.Create(fixture.Paths);
        var creator = new MachineWidePreUpdateBackupCreator(
            new BackupPackageEngine(),
            TimeProvider.System,
            () => true);

        var result = await creator.CreateAsync(paths, "1.10.0", "1.13.0");

        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(BackupKind.PreUpdate, result.Manifest.Kind);
        Assert.Equal(
            "20260825055759_V110OperationalSkuIntegrations",
            result.Manifest.LastMigration);
        Assert.Equal(22, result.Manifest.AppliedMigrations.Count);
    }

    [Fact]
    public async Task RunnerRejectsLegacyLayoutWithoutTouchingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-backup-legacy-{Guid.NewGuid():N}");
        var paths = ArteInventariadoPathProvider.CreateLegacy(root);
        try
        {
            var runner = new MachineWideBackupRunner(paths);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());
            Assert.False(File.Exists(paths.ProtectedBackupPolicyPath));
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
    public void SyntheticRunnerSmokeCreatesValidatedGenerationsAndPreservesCorruptCandidate()
    {
        Assert.Equal(0, MachineWideBackupSmokeHarness.Run());
    }

    private static Task<int> RunAsProcessAsync(MachineWideBackupRunner runner) =>
        RunAsProcessWithTimeoutAsync(runner);

    private static async Task<int> RunAsProcessWithTimeoutAsync(MachineWideBackupRunner runner)
    {
        var process = MachineWideBackupProcess.RunAsync(
            () => runner.RunAsync(),
            TextWriter.Null,
            TextWriter.Null);
        var completed = await Task.WhenAny(process, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(process, completed);
        return await process;
    }

    private static BackupManifest Manifest(DateTimeOffset createdUtc) => new(
        1,
        Guid.NewGuid(),
        createdUtc,
        createdUtc.ToLocalTime(),
        "1.12.0",
        "SQLite",
        [],
        null,
        "AA",
        1,
        Guid.NewGuid(),
        InstallationMode.Principal,
        BackupKind.Automatic,
        []);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-backup-policy-{Guid.NewGuid():N}");

        public ArteInventariadoPathProvider Paths { get; }

        public Fixture()
        {
            Paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(Root, "install"),
                Path.Combine(Root, "data"),
                Path.Combine(Root, "user"));
            Directory.CreateDirectory(Paths.MachineInstallRoot);
            Directory.CreateDirectory(Paths.MachineDataRoot);
            Directory.CreateDirectory(Paths.DataRoot);
            Directory.CreateDirectory(Paths.AssetsRoot);
            Directory.CreateDirectory(Paths.BusinessAssetsRoot);
            Directory.CreateDirectory(Paths.StationRoot);
            Directory.CreateDirectory(Paths.ProtectedBackupsRoot);
        }

        public async Task<MachineWideBackupRunner> CreateReadyRunnerAsync(
            MachineWideBackupRunnerCheckpoint? failureCheckpoint = null,
            bool useNativeNamedLock = false,
            bool throwOnDispose = false,
            bool throwOnAcquire = false,
            TimeProvider? timeProvider = null)
        {
            var policyStore = new MachineWideBackupPolicyStore();
            policyStore.EnsureInitialized(Paths);
            var station = new LocalStationConfigurationStore(Paths);
            station.EnsureInitialized();
            station.SaveConfigured(InstallationMode.Principal);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Paths.DatabasePath};Pooling=False")
                .Options;
            await using (var context = new AppDbContext(options))
            {
                await DatabaseInitializer.InicializarAsync(context);
            }

            return new MachineWideBackupRunner(
                Paths,
                new BackupPackageEngine(),
                policyStore,
                timeProvider ?? TimeProvider.System,
                () => true,
                name => useNativeNamedLock
                    ? new NamedMachineWideBackupLock(name)
                    : new TestLock(
                        acquired: true,
                        throwOnDispose: throwOnDispose,
                        throwOnAcquire: throwOnAcquire),
                allowSyntheticRoots: true,
                checkpoint =>
                {
                    if (checkpoint == failureCheckpoint)
                    {
                        throw new IOException($"Fallo sintético en {checkpoint}.");
                    }
                });
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TestLock(
        bool acquired,
        bool throwOnDispose = false,
        bool throwOnAcquire = false) : IMachineWideBackupLock
    {
        public bool TryAcquire()
        {
            if (throwOnAcquire)
            {
                throw new IOException("Fallo sintetico al adquirir el lock.");
            }

            return acquired;
        }

        public void Dispose()
        {
            if (throwOnDispose)
            {
                throw new IOException("Fallo sintetico durante el cleanup del lock.");
            }
        }
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            throw new InvalidOperationException("Fallo sintetico al obtener la hora.");
    }
}
