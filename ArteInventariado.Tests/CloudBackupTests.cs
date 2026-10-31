using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

#pragma warning disable CA1416

namespace ArteInventariado.Tests;

public sealed class CloudBackupTests
{
    [Fact]
    public async Task DisabledByDefaultDoesNotReadCredentialOrTouchPackages()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        var secret = new FakeSecretStore(null);
        var store = new FakeObjectStore();
        var runner = fixture.CreateRunner(secret, store);

        var result = await runner.RunAsync();

        Assert.Equal(CloudBackupRunOutcome.SkippedDisabled, result.Outcome);
        Assert.Equal(0, secret.LoadCount);
        Assert.Equal(0, store.PutCount);
    }

    [Fact]
    public async Task HealthyPackageUploadsWithDeterministicKeyAndPutResponseConfirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore();

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        var entry = Assert.Single(result.States, state => state.Status == CloudBackupUploadStatus.Uploaded);
        Assert.Equal(CloudBackupRunOutcome.Succeeded, result.Outcome);
        Assert.Equal($"v1/{fixture.InstallationId:D}/{entry.BackupId:D}.aibak", entry.Key);
        Assert.Equal(package, fixture.LastPackagePath);
        Assert.Equal(1, store.PutCount);
        Assert.True(store.LastPutPreventOverwrite);
        Assert.NotNull(entry.VersionId);
        Assert.True(File.Exists(package));
    }

    [Fact]
    public async Task DegradedAssetsPackageUploadsAndPreservesContentStatus()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync(degradedAssets: true);
        fixture.EnableCloud();
        var store = new FakeObjectStore();

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        var entry = Assert.Single(result.States);
        Assert.Equal(CloudBackupUploadStatus.Uploaded, entry.Status);
        Assert.Equal(BackupContentStatus.DegradedAssets, entry.ContentStatus);
        Assert.Equal(BackupContentStatus.DegradedAssets, store.LastMetadataContentStatus);
    }

    [Fact]
    public async Task InvalidPackageIsNeverUploaded()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.EnableCloud();
        var invalid = Path.Combine(fixture.Paths.ProtectedBackupsRoot, "invalid.aibak");
        await File.WriteAllTextAsync(invalid, "not a zip");
        var store = new FakeObjectStore();

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupRunOutcome.Failed, result.Outcome);
        Assert.Equal(0, store.PutCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("paquete cloud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreexistingRemoteObjectIsNotOverwrittenByUploadOnlyContract()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore { PreexistingObject = true };

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupRunOutcome.Failed, result.Outcome);
        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.Equal(0, store.PutCount);
    }

    [Fact]
    public async Task PreviouslyUploadedStateSkipsNetworkWithoutRemoteRead()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore();

        var runner = fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store);
        var first = await runner.RunAsync();
        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.Uploaded, Assert.Single(first.States).Status);
        Assert.Equal(CloudBackupRunOutcome.Succeeded, result.Outcome);
        Assert.Equal(CloudBackupUploadStatus.Uploaded, Assert.Single(result.States).Status);
        Assert.Equal(1, store.PutCount);
        Assert.Equal(0, store.CompleteCount);
    }

    [Fact]
    public async Task MissingPutChecksumOrVersionRemainsUploadUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore { ReturnMissingPutChecksum = true };

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.Equal(1, store.PutCount);
    }

    [Fact]
    public async Task MissingPutVersionRemainsUploadUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore { ReturnMissingPutVersion = true };

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.Equal(1, store.PutCount);
    }

    [Fact]
    public async Task UploadStateNeverPersistsCloudSecret()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore { ThrowOnPut = true };

        await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        var stateText = File.ReadAllText(fixture.Paths.CloudBackupUploadStatePath);
        Assert.DoesNotContain(Fixture.Credential.AccessKeyId, stateText, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.Credential.SecretAccessKey, stateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipartUploadsPartsWithSha256AndConfirmsSize()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync(largeAsset: true);
        fixture.EnableCloud();
        var store = new FakeObjectStore();

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupRunOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, store.PutCount);
        Assert.Equal(1, store.CompleteCount);
        Assert.True(store.LastCompletePreventOverwrite);
        Assert.True(store.UploadedParts.Count >= 2);
        Assert.All(store.UploadedParts, part => Assert.False(string.IsNullOrWhiteSpace(part.Sha256)));
    }

    [Fact]
    public async Task MultipartResumesOnlyPartsConfirmedByProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync(largeAsset: true);
        fixture.EnableCloud();
        var store = new FakeObjectStore { FailAfterParts = 1 };
        var first = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();
        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(first.States).Status);
        var before = store.UploadedParts.Count;

        store.FailAfterParts = null;
        var second = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.Uploaded, Assert.Single(second.States).Status);
        Assert.True(store.UploadedParts.Count > before);
        Assert.Equal(1, store.CompleteCount);
    }

    [Fact]
    public async Task AmbiguousUploadIsMarkedUnknownAndLocalPackageRemains()
    {
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore { ThrowAfterPut = true };

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.True(File.Exists(package));
    }

    [Fact]
    public async Task UploadUnknownDoesNotBecomeUploadedWithoutNewEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore { ReturnMissingPutChecksum = true, ThrowOnPut = false };

        var first = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();
        store.ThrowOnPut = true;
        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(first.States).Status);
        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.True(store.PutCount >= 1);
    }

    [Fact]
    public async Task AmbiguousMultipartCompletionRemainsUploadUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync(largeAsset: true);
        fixture.EnableCloud();
        var store = new FakeObjectStore { ThrowAfterComplete = true };

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.True(store.CompleteCount >= 1);
    }

    [Fact]
    public async Task MissingMultipartCompletionChecksumOrVersionRemainsUploadUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync(largeAsset: true);
        fixture.EnableCloud();
        var store = new FakeObjectStore { ReturnMissingCompleteChecksum = true };

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.Equal(1, store.CompleteCount);
    }

    [Fact]
    public async Task MissingMultipartCompletionVersionRemainsUploadUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync(largeAsset: true);
        fixture.EnableCloud();
        var store = new FakeObjectStore { ReturnMissingCompleteVersion = true };

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupUploadStatus.UploadUnknown, Assert.Single(result.States).Status);
        Assert.Equal(1, store.CompleteCount);
    }

    [Fact]
    public async Task ReparseCandidateIsSkippedWithoutReadingOutsideRoot()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.EnableCloud();
        var outside = Path.Combine(fixture.RootPath, "outside.aibak");
        await File.WriteAllTextAsync(outside, "outside");
        var link = Path.Combine(fixture.Paths.ProtectedBackupsRoot, "redirected.aibak");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var store = new FakeObjectStore();
        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store).RunAsync();

        Assert.Equal(CloudBackupRunOutcome.NoCandidates, result.Outcome);
        Assert.Equal(0, store.PutCount);
    }

    [Fact]
    public async Task ConcurrentExecutionIsSkippedBeforeNetworkAccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreatePackageAsync();
        fixture.EnableCloud();
        var store = new FakeObjectStore();

        var result = await fixture.CreateRunner(new FakeSecretStore(Fixture.Credential), store, lockAcquired: false).RunAsync();

        Assert.Equal(CloudBackupRunOutcome.SkippedConcurrent, result.Outcome);
        Assert.Equal(0, store.PutCount);
    }

    [Fact]
    public void UploadTransportContractContainsNoDeleteOrRecoveryOperations()
    {
        var methodNames = typeof(ICloudBackupObjectStore).GetMethods().Select(method => method.Name).ToArray();
        Assert.DoesNotContain(methodNames, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Restore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("GetObject", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Read", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StateStoreRejectsMalformedChecksums()
    {
        var state = new CloudBackupUploadStateDocument(
            CloudBackupUploadStateStore.CurrentSchemaVersion,
            [new CloudBackupUploadState(
                Guid.NewGuid(),
                "v1/install/backup.aibak",
                "not-a-sha",
                1,
                BackupContentStatus.Healthy,
                CloudBackupUploadStatus.Pending,
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                [],
                null)]);

        Assert.Throws<InvalidDataException>(() => CloudBackupUploadStateStore.Validate(state));
    }

    [Fact]
    public async Task LegacyPerUserLayoutIsRejectedWithoutCreatingCloudState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-cloud-legacy-{Guid.NewGuid():N}");
        var paths = ArteInventariadoPathProvider.CreateLegacy(root);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new CloudBackupRunner(paths).RunAsync());
            Assert.False(File.Exists(paths.CloudBackupConfigurationPath));
            Assert.False(File.Exists(paths.CloudBackupUploadStatePath));
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
    public void ConfigurationAndObjectKeysRejectTraversalAndUntrustedDestinations()
    {
        Assert.Throws<InvalidDataException>(() => CloudBackupConfigurationStore.ValidatePrefix("v1/../escape"));
        Assert.Throws<InvalidDataException>(() => CloudBackupConfigurationStore.ValidatePrefix("v1\\escape"));
        Assert.Throws<InvalidDataException>(() => CloudBackupConfigurationStore.Validate(
            new CloudBackupConfiguration(1, true, "Bad_Bucket", "us-east-1", "v1")));
        Assert.Throws<InvalidDataException>(() => CloudBackupConfigurationStore.Validate(
            new CloudBackupConfiguration(1, true, "portfolio-example-backups", "not-a-region", "v1")));
        Assert.Throws<InvalidDataException>(() => CloudBackupObjectKey.Create("v1/../x", Guid.NewGuid(), Guid.NewGuid()));
        Assert.False(CloudBackupObjectKey.IsWithinPrefix("v2/installation/backup.aibak", "v1"));
    }

    [Fact]
    public async Task ProvisioningPreflightValidatesTargetAndRedactsCredential()
    {
        using var fixture = Fixture.CreateEmpty();
        CloudBackupProvisioningIdentity? observedIdentityRequest = null;
        var service = new CloudBackupProvisioningService(
            fixture.Paths,
            (configuration, credential, cancellationToken) =>
            {
                Assert.Equal(fixture.Configuration, configuration);
                Assert.Equal(Fixture.Credential, credential);
                Assert.False(cancellationToken.IsCancellationRequested);
                observedIdentityRequest = new CloudBackupProvisioningIdentity(
                    "123456789012",
                    "arn:aws:iam::123456789012:user/PortfolioDemoUpload",
                    "AIDA_TEST");
                return Task.FromResult(observedIdentityRequest);
            },
            allowSyntheticRoots: true);

        var check = await service.ValidateAsync(
            fixture.Configuration,
            Fixture.Credential,
            new CloudBackupProvisioningAuthorizationGrant(
                1,
                fixture.InstallationId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1),
                new string('a', 64)));

        Assert.True(check.TargetValidated);
        Assert.False(check.WritePermissionVerified);
        Assert.Same(observedIdentityRequest, check.Identity);
        Assert.Contains("no se prueba", check.WritePermissionNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Fixture.Credential.SecretAccessKey, Fixture.Credential.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.Credential.SecretAccessKey, check.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Paths.CloudBackupConfigurationPath));
        Assert.False(File.Exists(fixture.Paths.CloudBackupCredentialPath));
    }

    [Fact]
    public void UploadPolicyIsNarrowAndExcludesDeleteAndReadObject()
    {
        var policy = S3UploadPolicyBuilder.Build("portfolio-example-backups", "v1");

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "s3:PutObject",
                "s3:AbortMultipartUpload",
                "s3:ListMultipartUploadParts"
            },
            policy.AllowedActions);
        Assert.DoesNotContain("s3:GetObject", policy.AllowedActions);
        Assert.DoesNotContain("s3:GetObjectVersion", policy.AllowedActions);
        Assert.DoesNotContain("s3:GetObjectAttributes", policy.AllowedActions);
        Assert.Contains("s3:GetObject", policy.ForbiddenActions);
        Assert.Contains("s3:GetObjectVersion", policy.ForbiddenActions);
        Assert.Contains("s3:GetObjectAttributes", policy.ForbiddenActions);
        Assert.Contains("s3:ListBucket", policy.ForbiddenActions);
        Assert.Contains("s3:ListBucketMultipartUploads", policy.ForbiddenActions);
        Assert.Contains("s3:DeleteObject", policy.ForbiddenActions);
        Assert.Contains("s3:RestoreObject", policy.ForbiddenActions);
        Assert.Contains("s3:PutObjectAcl", policy.ForbiddenActions);
        Assert.Contains("s3:PutObjectLegalHold", policy.ForbiddenActions);
        Assert.Contains("s3:BypassGovernanceRetention", policy.ForbiddenActions);
        var document = S3UploadPolicyBuilder.CreatePolicyDocument("portfolio-example-backups", "v1");
        Assert.Contains("s3:PutObject", document, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:GetObject\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:GetObjectVersion", document, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:GetObjectAttributes", document, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:ListBucket", document, StringComparison.Ordinal);
    }

    [Fact]
    public void DpapiCredentialIsMachineProtectedAndNotStoredAsPlaintext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = Fixture.CreateEmpty();
        var store = new DpapiCloudBackupSecretStore(fixture.Paths, () => true, null);
        store.Save(Fixture.Credential);
        var encrypted = File.ReadAllBytes(fixture.Paths.CloudBackupCredentialPath);

        Assert.NotEmpty(encrypted);
        Assert.DoesNotContain(
            Fixture.Credential.SecretAccessKey,
            Encoding.UTF8.GetString(encrypted),
            StringComparison.Ordinal);
        Assert.Equal(Fixture.Credential, store.LoadOptional());
    }

    [Fact]
    public void StandardUserCannotProvisionCredential()
    {
        using var fixture = Fixture.CreateEmpty();
        var provisioner = new CloudBackupProvisioner(() => false);
        Assert.Throws<UnauthorizedAccessException>(() => provisioner.Provision(
            fixture.Paths,
            fixture.Configuration,
            Fixture.Credential));
        Assert.False(File.Exists(fixture.Paths.CloudBackupCredentialPath));
        Assert.False(File.Exists(fixture.Paths.CloudBackupConfigurationPath));
    }

    [Fact]
    public void CloudTaskRunsAsSystemAndStartsWhenNetworkAppears()
    {
        using var fixture = Fixture.CreateEmpty();
        Directory.CreateDirectory(fixture.Paths.MachineInstallRoot);
        File.WriteAllText(fixture.Paths.CloudBackupRunnerPath, "runner");
        var xml = XDocument.Parse(MachineWideCloudBackupTaskScheduler.BuildTaskXml(fixture.Paths));
        var text = xml.ToString(SaveOptions.DisableFormatting);

        Assert.Contains(MachineWideBackupPolicyStore.LocalSystemSid, text, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceAccount", text, StringComparison.Ordinal);
        Assert.Contains("StartWhenAvailable", text, StringComparison.Ordinal);
        Assert.Contains("IgnoreNew", text, StringComparison.Ordinal);
        Assert.Contains("--run", text, StringComparison.Ordinal);
        Assert.Contains(fixture.Paths.CloudBackupRunnerPath, text, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudTaskRegistrationUsesSystemArgumentAndCapturesResult()
    {
        using var fixture = Fixture.CreateEmpty();
        Directory.CreateDirectory(fixture.Paths.MachineInstallRoot);
        File.WriteAllText(fixture.Paths.CloudBackupRunnerPath, "runner");
        System.Diagnostics.ProcessStartInfo? captured = null;
        var scheduler = new MachineWideCloudBackupTaskScheduler(
            startInfo =>
            {
                captured = startInfo;
                return new MachineWideTaskSchedulerProcessResult(0, "Created", string.Empty);
            },
            allowSyntheticRoots: true);

        scheduler.EnsureRegistered(fixture.Paths);

        Assert.NotNull(captured);
        Assert.Contains("/RU", captured!.ArgumentList);
        Assert.Contains("SYSTEM", captured.ArgumentList);
        var log = File.ReadAllText(Path.Combine(fixture.Paths.UpdateLogsRoot, "machine-bootstrap.log"));
        Assert.Contains("TASK", log, StringComparison.Ordinal);
        Assert.Contains($"task={MachineWideCloudBackupTaskScheduler.DefaultTaskName}", log, StringComparison.Ordinal);
        Assert.Contains("exit=0", log, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudRegistrationSuccessWithoutPhysicalTaskVerificationRemainsFailure()
    {
        using var fixture = Fixture.CreateEmpty();
        Directory.CreateDirectory(fixture.Paths.MachineInstallRoot);
        File.WriteAllText(fixture.Paths.CloudBackupRunnerPath, "runner");
        var scheduler = new MachineWideCloudBackupTaskScheduler(
            _ => new MachineWideTaskSchedulerProcessResult(0, "Created", string.Empty),
            (_, _, _) => false,
            allowSyntheticRoots: true);

        var error = Assert.Throws<InvalidOperationException>(
            () => scheduler.EnsureRegistered(fixture.Paths));

        Assert.Contains("verificación física", error.Message, StringComparison.OrdinalIgnoreCase);
        var log = File.ReadAllText(Path.Combine(fixture.Paths.UpdateLogsRoot, "machine-bootstrap.log"));
        Assert.Contains("TASK-VERIFY", log, StringComparison.Ordinal);
        Assert.Contains("verified=false", log, StringComparison.Ordinal);
        Assert.DoesNotContain("SUCCESS", log, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudBackupAclScopeAllowsUsersReadExecuteOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = Fixture.CreateEmpty();
        var plan = new MachineWideAclService().CreatePlan(fixture.Paths);
        var scope = Assert.Single(plan.Scopes, value => value.Name == "CloudBackup");
        Assert.Equal(MachineWideAclProfile.CloudBackupReadExecute, scope.Profile);
        Assert.All(scope.DirectoryRules.Concat(scope.FileRules).Where(rule => rule.IdentitySid == plan.UsersSid), rule =>
            Assert.Equal(System.Security.AccessControl.FileSystemRights.ReadAndExecute, rule.Rights));
    }

    [Fact]
    public void CloudBackupCredentialScopeExcludesUsers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = Fixture.CreateEmpty();
        var plan = new MachineWideAclService().CreatePlan(fixture.Paths);
        var scope = Assert.Single(plan.Scopes, value => value.Name == "CloudBackupCredentials");
        Assert.Equal(MachineWideAclProfile.CloudBackupCredentialProtected, scope.Profile);
        Assert.DoesNotContain(scope.DirectoryRules.Concat(scope.FileRules), rule =>
            rule.IdentitySid == plan.UsersSid);
    }

    private sealed class Fixture : IAsyncDisposable, IDisposable
    {
        public static readonly CloudBackupCredential Credential = new(
            "AKIA_TEST_UPLOAD_ONLY",
            "test-secret-never-logged");

        private readonly string _root;
        private readonly IReadOnlyList<string> _migrations;
        private readonly BackupPackageEngine _engine;

        private Fixture(
            string root,
            ArteInventariadoPathProvider paths,
            Guid installationId,
            IReadOnlyList<string> migrations,
            BackupPackageEngine engine)
        {
            _root = root;
            Paths = paths;
            InstallationId = installationId;
            _migrations = migrations;
            _engine = engine;
            Configuration = new CloudBackupConfiguration(
                CloudBackupConfigurationStore.CurrentSchemaVersion,
                true,
                "portfolio-example-backups",
                "us-east-1",
                "v1",
                MultipartThresholdBytes: 5L * 1024 * 1024,
                MultipartPartSizeBytes: 5L * 1024 * 1024);
        }

        public ArteInventariadoPathProvider Paths { get; }
        public Guid InstallationId { get; }
        public CloudBackupConfiguration Configuration { get; }
        public string? LastPackagePath { get; private set; }
        public string RootPath => _root;

        public static Fixture CreateEmpty()
        {
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-cloud-{Guid.NewGuid():N}");
            var paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"),
                Path.Combine(root, "user"));
            foreach (var directory in new[]
                     {
                         paths.MachineInstallRoot,
                         paths.MachineDataRoot,
                         paths.DataRoot,
                         paths.AssetsRoot,
                         paths.BusinessAssetsRoot,
                         paths.StationRoot,
                         paths.ProtectedBackupsRoot,
                         paths.CloudBackupRoot
                     })
            {
                Directory.CreateDirectory(directory);
            }

            var station = new LocalStationConfigurationStore(paths);
            station.EnsureInitialized();
            var document = station.SaveConfigured(InstallationMode.Principal);
            return new Fixture(root, paths, document.InstallationId, [], new BackupPackageEngine());
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = CreateEmpty();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = fixture.Paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString())
                .Options;
            await using (var context = new AppDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            await using var migrationContext = new AppDbContext(options);
            var migrations = migrationContext.Database.GetMigrations().ToList();
            return new Fixture(fixture._root, fixture.Paths, fixture.InstallationId, migrations, fixture._engine);
        }

        public void EnableCloud() => new CloudBackupConfigurationStore(Paths).Save(Configuration);

        public async Task<string> CreatePackageAsync(bool degradedAssets = false, bool largeAsset = false)
        {
            var stage = Path.Combine(_root, "package-stage", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var snapshot = Path.Combine(stage, BackupPackageEngine.DatabaseEntryName);
            await _engine.CreateSnapshotAsync(Paths.DatabasePath, snapshot, CancellationToken.None);
            var sha = await BackupPackageEngine.ComputeSha256Async(snapshot, CancellationToken.None);
            var assets = new List<BackupAssetManifest>();
            if (degradedAssets)
            {
                assets.Add(new BackupAssetManifest(
                    1,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    true,
                    IssueKind: BackupAssetIssueKind.Missing));
            }
            else if (largeAsset)
            {
                var assetPath = Path.Combine(stage, "business-assets", "products", "large.bin");
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
                await using (var stream = new FileStream(assetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1024 * 1024];
                    for (var i = 0; i < 6; i++)
                    {
                        RandomNumberGenerator.Fill(buffer);
                        await stream.WriteAsync(buffer);
                    }
                }

                var assetSha = await BackupPackageEngine.ComputeSha256Async(assetPath, CancellationToken.None);
                assets.Add(new BackupAssetManifest(1, "business-assets/products/large.bin", "large.bin", new FileInfo(assetPath).Length, assetSha, false));
            }

            var now = DateTimeOffset.UtcNow;
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                Guid.NewGuid(),
                now,
                now.ToLocalTime(),
                "1.12.0",
                "SQLite",
                _migrations,
                _migrations.LastOrDefault(),
                sha,
                new FileInfo(snapshot).Length,
                InstallationId,
                InstallationMode.Principal,
                BackupKind.Automatic,
                assets,
                degradedAssets ? BackupContentStatus.DegradedAssets : BackupContentStatus.Healthy,
                degradedAssets ? 1 : 0);
            var package = Path.Combine(Paths.ProtectedBackupsRoot, $"{manifest.BackupId:N}.aibak");
            await _engine.CreatePackageAsync(stage, snapshot, manifest, package, CancellationToken.None);
            LastPackagePath = package;
            return package;
        }

        public CloudBackupRunner CreateRunner(FakeSecretStore secret, FakeObjectStore store, bool lockAcquired = true)
        {
            return new CloudBackupRunner(
                Paths,
                _engine,
                new CloudBackupConfigurationStore(Paths),
                new CloudBackupUploadStateStore(Paths),
                secret,
                (_, _) => store,
                TimeProvider.System,
                () => true,
                _ => new TestCloudLock(lockAcquired),
                _ => Task.FromResult(_migrations),
                () => InstallationId,
                allowSyntheticRoots: true);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        private sealed record FixtureState;
    }

    private sealed class FakeSecretStore(CloudBackupCredential? credential) : ICloudBackupSecretStore
    {
        public int LoadCount { get; private set; }

        public CloudBackupCredential? LoadOptional()
        {
            LoadCount++;
            return credential;
        }

        public void Save(CloudBackupCredential value) => throw new NotSupportedException();
    }

    private sealed class TestCloudLock(bool acquired) : ICloudBackupLock
    {
        public bool TryAcquire() => acquired;
        public void Dispose() { }
    }

    private sealed class FakeObjectStore : ICloudBackupObjectStore
    {
        private readonly HashSet<string> _objects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CloudMultipartPartReceipt>> _parts = new(StringComparer.Ordinal);
        private int _uploadSequence;

        public bool ThrowOnPut { get; set; }
        public bool ThrowAfterPut { get; set; }
        public bool ThrowAfterComplete { get; set; }
        public bool ReturnMissingPutChecksum { get; set; }
        public bool ReturnMissingPutVersion { get; set; }
        public bool ReturnMissingCompleteChecksum { get; set; }
        public bool ReturnMissingCompleteVersion { get; set; }
        public bool PreexistingObject { get; set; }
        public int? FailAfterParts { get; set; }
        public int PutCount { get; private set; }
        public int CompleteCount { get; private set; }
        public List<CloudMultipartPartReceipt> UploadedParts { get; } = [];
        public BackupContentStatus? LastMetadataContentStatus { get; private set; }
        public bool LastPutPreventOverwrite { get; private set; }
        public bool LastCompletePreventOverwrite { get; private set; }

        public Task<CloudObjectUploadReceipt> PutObjectAsync(CloudObjectUploadRequest request, CancellationToken cancellationToken = default)
        {
            LastPutPreventOverwrite = request.PreventOverwrite;
            if (request.PreventOverwrite && (PreexistingObject || _objects.Contains(request.Key)))
            {
                throw new CloudBackupWriteConflictException("simulated conditional PUT conflict");
            }

            PutCount++;
            if (ThrowOnPut)
            {
                throw new IOException("simulated network interruption");
            }

            LastMetadataContentStatus = request.Metadata.TryGetValue("arte-content-status", out var content)
                ? Enum.Parse<BackupContentStatus>(content)
                : null;
            _objects.Add(request.Key);
            if (ThrowAfterPut)
            {
                ThrowAfterPut = false;
                throw new IOException("simulated network interruption after commit");
            }

            return Task.FromResult(new CloudObjectUploadReceipt(
                request.BucketName,
                request.Key,
                request.SizeBytes,
                ReturnMissingPutChecksum ? null : request.Sha256,
                ReturnMissingPutVersion ? null : "v1",
                "etag",
                "FULL_OBJECT"));
        }

        public Task<CloudMultipartUploadSession> InitiateMultipartUploadAsync(CloudMultipartUploadRequest request, CancellationToken cancellationToken = default)
        {
            var uploadId = request.UploadId ?? $"upload-{++_uploadSequence}";
            _parts.TryAdd(uploadId, []);
            return Task.FromResult(new CloudMultipartUploadSession(
                request.BucketName,
                request.Key,
                uploadId,
                request.PreventOverwrite,
                request.SizeBytes,
                request.Sha256));
        }

        public Task<IReadOnlyList<CloudMultipartPartReceipt>> ListMultipartPartsAsync(CloudMultipartUploadSession session, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CloudMultipartPartReceipt>>(_parts.TryGetValue(session.UploadId, out var parts) ? parts.ToArray() : []);
        }

        public Task<CloudMultipartPartReceipt> UploadPartAsync(CloudMultipartPartRequest request, CancellationToken cancellationToken = default)
        {
            if (FailAfterParts.HasValue && UploadedParts.Count >= FailAfterParts.Value)
            {
                throw new IOException("simulated multipart interruption");
            }

            var receipt = new CloudMultipartPartReceipt(request.PartNumber, $"etag-{request.PartNumber}", request.Sha256, request.PartSizeBytes);
            _parts.GetValueOrDefault(request.UploadId)?.RemoveAll(part => part.PartNumber == request.PartNumber);
            _parts.GetValueOrDefault(request.UploadId)?.Add(receipt);
            UploadedParts.Add(receipt);
            return Task.FromResult(receipt);
        }

        public Task<CloudObjectUploadReceipt> CompleteMultipartUploadAsync(CloudMultipartUploadSession session, IReadOnlyList<CloudBackupUploadedPart> parts, CancellationToken cancellationToken = default)
        {
            CompleteCount++;
            LastCompletePreventOverwrite = session.PreventOverwrite;
            if (session.PreventOverwrite && (PreexistingObject || _objects.Contains(session.Key)))
            {
                throw new CloudBackupWriteConflictException("simulated conditional multipart conflict");
            }

            var size = parts.Sum(part => part.SizeBytes);
            var sha = ComputeCompositeSha256(parts);
            _objects.Add(session.Key);
            if (ThrowAfterComplete)
            {
                ThrowAfterComplete = false;
                throw new IOException("simulated multipart interruption after commit");
            }

            return Task.FromResult(new CloudObjectUploadReceipt(
                session.BucketName,
                session.Key,
                size,
                ReturnMissingCompleteChecksum ? null : sha,
                ReturnMissingCompleteVersion ? null : "v-multipart",
                "etag-multipart",
                "COMPOSITE"));
        }

        public Task AbortMultipartUploadAsync(CloudMultipartUploadSession session, CancellationToken cancellationToken = default)
        {
            _parts.Remove(session.UploadId);
            return Task.CompletedTask;
        }

        public void Dispose() { }

        private static string ComputeCompositeSha256(IReadOnlyList<CloudBackupUploadedPart> parts)
        {
            var concatenated = parts
                .OrderBy(part => part.PartNumber)
                .SelectMany(part => Convert.FromHexString(part.ChecksumSha256!))
                .ToArray();
            return Convert.ToHexString(SHA256.HashData(concatenated));
        }

    }
}

#pragma warning restore CA1416
