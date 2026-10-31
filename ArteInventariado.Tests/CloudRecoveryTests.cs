using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class CloudRecoveryTests
{
    private static CloudRecoveryAuthenticationRequest Authentication => new(
        "AKIA_BOOTSTRAP_TEST",
        "bootstrap-secret-never-persisted",
        "arn:aws:iam::123456789012:role/ArteInventariadoRecovery",
        "arn:aws:iam::123456789012:mfa/owner",
        "123456",
        "us-east-1");

    [Fact]
    public void RecoveryIamIsReadOnlyAndInstallationScoped()
    {
        var installationId = Guid.NewGuid();
        var contract = S3RecoveryPolicyBuilder.Build(
            "portfolio-example-backups",
            "v1",
            installationId);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "s3:ListBucket",
                "s3:ListBucketVersions",
                "s3:GetObject",
                "s3:GetObjectVersion"
            },
            contract.AllowedActions);
        Assert.DoesNotContain("s3:PutObject", contract.AllowedActions);
        Assert.DoesNotContain("s3:DeleteObject", contract.AllowedActions);
        Assert.Contains("s3:PutObject", contract.ForbiddenActions);
        Assert.Contains("s3:DeleteObject", contract.ForbiddenActions);
        Assert.Contains("s3:BypassGovernanceRetention", contract.ForbiddenActions);

        var document = S3RecoveryPolicyBuilder.CreatePolicyDocument(
            "portfolio-example-backups",
            "v1",
            installationId);
        Assert.Contains(contract.InstallationPrefix, document, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:PutObject", document, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:DeleteObject", document, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:BypassGovernanceRetention", document, StringComparison.Ordinal);
        Assert.DoesNotContain("arn:aws:s3:::portfolio-example-backups/v1/*\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogIsRestrictedToSelectedInstallationAndPrefersHealthy()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var healthy = await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow.AddMinutes(-20));
        var degraded = await fixture.CreatePackageAsync(BackupContentStatus.DegradedAssets, DateTimeOffset.UtcNow.AddMinutes(-2));
        fixture.Store.Add(healthy);
        fixture.Store.Add(degraded);
        fixture.Store.Add(await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow), Guid.NewGuid());

        var service = fixture.CreateService();
        var result = await service.GetCatalogAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(fixture.InstallationId, item.InstallationId));
        Assert.Equal(healthy.Manifest.BackupId, result.RecommendedHealthy!.BackupId);
        Assert.Null(result.RecommendedFallback);
        Assert.Equal(1, fixture.Authenticator.CallCount);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("otra instalación", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CatalogUsesDegradedFallbackOnlyWhenNoHealthyExists()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var degraded = await fixture.CreatePackageAsync(BackupContentStatus.DegradedAssets, DateTimeOffset.UtcNow);
        fixture.Store.Add(degraded);

        var result = await fixture.CreateService().GetCatalogAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false));

        Assert.Null(result.RecommendedHealthy);
        Assert.Equal(degraded.Manifest.BackupId, result.RecommendedFallback!.BackupId);
    }

    [Fact]
    public async Task DifferentInstallationRequiresExplicitOwnerConfirmation()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.CreateService().GetCatalogAsync(
            Authentication,
            new CloudRecoveryScope(Guid.NewGuid(), OwnerConfirmed: false)));
        Assert.Equal(0, fixture.Authenticator.CallCount);

        // The owner-confirmed branch is allowed to enumerate only the
        // explicitly selected installation prefix; no bucket-root listing is
        // ever requested by the service.
        var result = await fixture.CreateService().GetCatalogAsync(
            Authentication,
            new CloudRecoveryScope(Guid.NewGuid(), OwnerConfirmed: true));
        Assert.Empty(result.Items);
        Assert.Equal("v1/", fixture.Store.LastListedPrefix![..3]);
    }

    [Fact]
    public async Task ValidPackageIsDownloadedToProtectedStagingAndReturnedAsCandidate()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow);
        var remote = fixture.Store.Add(package);

        var candidate = await fixture.CreateService().DownloadAndValidateAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false),
            remote);

        Assert.Equal(package.Manifest.BackupId, candidate.BackupId);
        Assert.Equal(package.PackageSha256, candidate.PackageSha256);
        Assert.True(File.Exists(candidate.PackagePath));
        Assert.StartsWith(
            Path.GetFullPath(fixture.Paths.CloudRecoveryStagingRoot) + Path.DirectorySeparatorChar,
            Path.GetFullPath(candidate.PackagePath),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(fixture.Paths.CloudBackupCredentialsRoot));
        Assert.Equal(1, fixture.Store.DownloadCount);

        await fixture.CreateService().CleanupCandidateAsync(candidate);
        Assert.False(File.Exists(candidate.PackagePath));
    }

    [Fact]
    public async Task ChangedChecksumIsRejectedAndProtectedStagingIsCleaned()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow);
        var remote = fixture.Store.Add(package);
        var tampered = remote with { PackageSha256 = new string('A', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CreateService().DownloadAndValidateAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false),
            tampered));
        Assert.Empty(ExistingDirectories(fixture.Paths.CloudRecoveryStagingRoot));
    }

    [Fact]
    public async Task ChangedRemoteVersionIsRejectedBeforeDownload()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow);
        var remote = fixture.Store.Add(package);
        fixture.Store.ChangeVersion(remote.Key, remote.VersionId!, "version-2");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CreateService().DownloadAndValidateAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false),
            remote));
        Assert.Equal(0, fixture.Store.DownloadCount);
    }

    [Fact]
    public async Task CorruptZipIsRejectedWithoutProducingCandidate()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow);
        var bytes = Encoding.UTF8.GetBytes("this is not a zip");
        var invalid = fixture.Store.AddRaw(
            fixture.InstallationId,
            package.Manifest.BackupId,
            bytes,
            package.Manifest.CreatedUtc);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CreateService().DownloadAndValidateAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false),
            invalid));
        Assert.Empty(ExistingDirectories(fixture.Paths.CloudRecoveryStagingRoot));
    }

    [Fact]
    public async Task InterruptedDownloadLeavesProductionAndLocalBackupsUntouched()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow);
        var remote = fixture.Store.Add(package);
        fixture.Store.ThrowDuringDownload = true;

        await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().DownloadAndValidateAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false),
            remote));

        Assert.True(File.Exists(package.PackagePath));
        Assert.Empty(ExistingDirectories(fixture.Paths.CloudRecoveryStagingRoot));
    }

    [Fact]
    public async Task ZipSlipPackageIsRejectedByExistingPackageValidator()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync(BackupContentStatus.Healthy, DateTimeOffset.UtcNow);
        await using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("../outside.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("must never extract");
        }

        var invalid = fixture.Store.AddRaw(
            fixture.InstallationId,
            package.Manifest.BackupId,
            memory.ToArray(),
            package.Manifest.CreatedUtc);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CreateService().DownloadAndValidateAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false),
            invalid));
        Assert.Empty(ExistingDirectories(fixture.Paths.CloudRecoveryStagingRoot));
    }

    [Fact]
    public async Task NotElevatedCannotEnumerateOrCreateStaging()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        fixture.IsElevated = false;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.CreateService().GetCatalogAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false)));
        Assert.False(Directory.Exists(fixture.Paths.CloudRecoveryStagingRoot));
    }

    [Fact]
    public void StsAuthenticatorRejectsInvalidMfaWithoutPersistingSecrets()
    {
        var invalid = Authentication with { MfaTokenCode = "1234" };
        Assert.Throws<InvalidDataException>(() => AwsStsCloudRecoveryAuthenticator.Validate(invalid));
        var serialized = JsonSerializer.Serialize(Authentication);
        Assert.DoesNotContain(Authentication.BootstrapAccessKeyId, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(Authentication.BootstrapSecretAccessKey, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(Authentication.MfaTokenCode, serialized, StringComparison.Ordinal);
        Assert.Contains("secrets redacted", Authentication.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UacCancellationDoesNotCreateRecoveryState()
    {
        var coordinator = new CloudRecoveryUacCoordinator((_, _, _) =>
            new MachineWideElevationResult(MachineWideElevationOutcome.Cancelled));

        var result = coordinator.EnsureElevated("recovery.exe", []);

        Assert.Equal(MachineWideElevationOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task ServiceAuthorizationRunsBeforeCloudAuthentication()
    {
        await using var fixture = await RecoveryFixture.CreateAsync();
        var service = fixture.CreateService(new DenyRecoveryAuthorization());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetCatalogAsync(
            Authentication,
            new CloudRecoveryScope(fixture.InstallationId, OwnerConfirmed: false)));
        Assert.Equal(0, fixture.Authenticator.CallCount);
    }

    [Fact]
    public async Task RecoveryAuthorizationAcceptsOnlyProtectedAdmin()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(addUsers: true);
        var session = new CurrentSession();
        var authorization = new CloudRecoveryAuthorizationService(
            fixture.ContextFactory,
            session);

        session.IniciarSesion(new UsuarioSesion(1, "Administrador", "admin", RolUsuario.ADMIN));
        await authorization.EnsureAuthorizedAsync();

        session.IniciarSesion(new UsuarioSesion(2, "Delegado", "delegated-admin", RolUsuario.ADMIN));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authorization.EnsureAuthorizedAsync());

        session.IniciarSesion(new UsuarioSesion(3, "Empleado", "empleado", RolUsuario.EMPLEADO));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authorization.EnsureAuthorizedAsync());
    }

    [Fact]
    public void UploaderContractRemainsWriteOnlyAndSeparate()
    {
        var upload = S3UploadPolicyBuilder.Build("portfolio-example-backups", "v1");
        Assert.Contains("s3:PutObject", upload.AllowedActions);
        Assert.DoesNotContain("s3:GetObject", upload.AllowedActions);
        Assert.DoesNotContain("s3:GetObjectVersion", upload.AllowedActions);
        Assert.DoesNotContain("s3:DeleteObject", upload.AllowedActions);
        Assert.Contains("s3:GetObject", upload.ForbiddenActions);
        Assert.Contains("s3:DeleteObject", upload.ForbiddenActions);
    }

    private static IReadOnlyList<string> ExistingDirectories(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).ToArray()
            : [];

    private sealed class RecoveryFixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly IReadOnlyList<string> _migrations;
        private readonly BackupPackageEngine _engine;

        private RecoveryFixture(
            string root,
            ArteInventariadoPathProvider paths,
            Guid installationId,
            IReadOnlyList<string> migrations,
            BackupPackageEngine engine,
            TestDbContextFactory contextFactory)
        {
            _root = root;
            Paths = paths;
            InstallationId = installationId;
            _migrations = migrations;
            _engine = engine;
            ContextFactory = contextFactory;
            Store = new FakeRecoveryObjectStore();
            Authenticator = new FakeAuthenticator();
            Configuration = new CloudBackupConfiguration(
                CloudBackupConfigurationStore.CurrentSchemaVersion,
                true,
                "portfolio-example-backups",
                "us-east-1",
                "v1",
                MultipartThresholdBytes: 5L * 1024 * 1024,
                MultipartPartSizeBytes: 5L * 1024 * 1024);
            new CloudBackupConfigurationStore(paths).Save(Configuration);
        }

        public ArteInventariadoPathProvider Paths { get; }
        public Guid InstallationId { get; }
        public CloudBackupConfiguration Configuration { get; }
        public FakeRecoveryObjectStore Store { get; }
        public FakeAuthenticator Authenticator { get; }
        public TestDbContextFactory ContextFactory { get; }
        public bool IsElevated { get; set; } = true;

        public static async Task<RecoveryFixture> CreateAsync(bool addUsers = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-recovery-{Guid.NewGuid():N}");
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
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString())
                .Options;
            await using (var context = new AppDbContext(options))
            {
                await context.Database.MigrateAsync();
                if (addUsers)
                {
                    context.Usuarios.AddRange(
                        new Usuario { UsuarioId = 1, NombreCompleto = "Administrador", NombreUsuario = "admin", PasswordHash = "hash", Rol = RolUsuario.ADMIN, Activo = true },
                        new Usuario { UsuarioId = 2, NombreCompleto = "Delegado", NombreUsuario = "delegated-admin", PasswordHash = "hash", Rol = RolUsuario.ADMIN, Activo = true },
                        new Usuario { UsuarioId = 3, NombreCompleto = "Empleado", NombreUsuario = "empleado", PasswordHash = "hash", Rol = RolUsuario.EMPLEADO, Activo = true });
                    await context.SaveChangesAsync();
                }
            }

            await using var migrationsContext = new AppDbContext(options);
            var migrations = migrationsContext.Database.GetMigrations().ToList();
            return new RecoveryFixture(
                root,
                paths,
                document.InstallationId,
                migrations,
                new BackupPackageEngine(),
                new TestDbContextFactory(options));
        }

        public CloudRecoveryService CreateService(ICloudRecoveryAuthorizationService? authorization = null)
        {
            return new CloudRecoveryService(
                Paths,
                new CloudBackupConfigurationStore(Paths),
                _engine,
                authorization ?? new AllowRecoveryAuthorization(),
                Authenticator,
                (_, _) => Store,
                TimeProvider.System,
                () => IsElevated,
                () => InstallationId,
                _ => Task.FromResult(_migrations));
        }

        public async Task<CloudPackage> CreatePackageAsync(
            BackupContentStatus contentStatus,
            DateTimeOffset createdUtc)
        {
            var stage = Path.Combine(_root, "package-stage", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var snapshot = Path.Combine(stage, BackupPackageEngine.DatabaseEntryName);
            await _engine.CreateSnapshotAsync(Paths.DatabasePath, snapshot, CancellationToken.None);
            var databaseSha = await BackupPackageEngine.ComputeSha256Async(snapshot, CancellationToken.None);
            IReadOnlyList<BackupAssetManifest> assets = contentStatus == BackupContentStatus.DegradedAssets
                ? [new BackupAssetManifest(1, string.Empty, string.Empty, 0, string.Empty, true, IssueKind: BackupAssetIssueKind.Missing)]
                : [];
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                Guid.NewGuid(),
                createdUtc,
                createdUtc.ToLocalTime(),
                "1.12.0",
                "SQLite",
                _migrations,
                _migrations.LastOrDefault(),
                databaseSha,
                new FileInfo(snapshot).Length,
                InstallationId,
                InstallationMode.Principal,
                BackupKind.Automatic,
                assets,
                contentStatus,
                contentStatus == BackupContentStatus.DegradedAssets ? 1 : 0);
            var packagePath = Path.Combine(Paths.ProtectedBackupsRoot, $"{manifest.BackupId:N}.aibak");
            await _engine.CreatePackageAsync(stage, snapshot, manifest, packagePath, CancellationToken.None);
            var packageSha = await BackupPackageEngine.ComputeSha256Async(packagePath, CancellationToken.None);
            return new CloudPackage(packagePath, manifest, packageSha, File.ReadAllBytes(packagePath));
        }

        public async ValueTask DisposeAsync()
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

            await Task.CompletedTask;
        }
    }

    private sealed record CloudPackage(
        string PackagePath,
        BackupManifest Manifest,
        string PackageSha256,
        byte[] Bytes);

    private sealed class AllowRecoveryAuthorization : ICloudRecoveryAuthorizationService
    {
        public Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DenyRecoveryAuthorization : ICloudRecoveryAuthorizationService
    {
        public Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("simulated denied recovery");
    }

    private sealed class FakeAuthenticator : ICloudRecoveryAuthenticator
    {
        public int CallCount { get; private set; }

        public Task<CloudRecoveryTemporaryCredentials> AuthenticateAsync(
            CloudRecoveryAuthenticationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new CloudRecoveryTemporaryCredentials(
                "ASIA_TEMPORARY",
                "temporary-secret",
                "temporary-session-token",
                DateTimeOffset.UtcNow.AddMinutes(15)));
        }
    }

    private sealed class FakeRecoveryObjectStore : ICloudRecoveryObjectStore
    {
        private readonly Dictionary<(string Key, string VersionId), RemotePackage> _packages = new();

        public string? LastListedPrefix { get; private set; }
        public int DownloadCount { get; private set; }
        public bool ThrowDuringDownload { get; set; }

        public CloudRecoveryCatalogItem Add(CloudPackage package, Guid? installationId = null)
        {
            var sourceInstallationId = installationId ?? package.Manifest.SourceInstallationId;
            var key = CloudBackupObjectKey.Create("v1", sourceInstallationId, package.Manifest.BackupId);
            var version = $"version-{_packages.Count + 1}";
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["arte-backup-id"] = package.Manifest.BackupId.ToString("D"),
                ["arte-source-installation-id"] = sourceInstallationId.ToString("D"),
                ["arte-content-status"] = package.Manifest.ContentStatus.ToString(),
                ["arte-omitted-assets"] = (package.Manifest.OmittedAssetCount ?? 0).ToString(),
                ["arte-package-sha256"] = package.PackageSha256,
                ["arte-database-sha256"] = package.Manifest.DatabaseSha256,
                ["arte-created-utc"] = package.Manifest.CreatedUtc.ToUniversalTime().ToString("O")
            };
            _packages[(key, version)] = new RemotePackage(
                package.Bytes,
                new CloudRemoteObjectMetadata(
                    "portfolio-example-backups",
                    key,
                    version,
                    package.Bytes.LongLength,
                    package.Manifest.CreatedUtc,
                    "etag",
                    package.PackageSha256,
                    "FULL_OBJECT",
                    metadata));
            return ToCatalogItem(key, version, package.Manifest, package.PackageSha256, sourceInstallationId);
        }

        public CloudRecoveryCatalogItem AddRaw(
            Guid installationId,
            Guid backupId,
            byte[] bytes,
            DateTimeOffset createdUtc)
        {
            var key = CloudBackupObjectKey.Create("v1", installationId, backupId);
            var version = $"version-{_packages.Count + 1}";
            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["arte-backup-id"] = backupId.ToString("D"),
                ["arte-source-installation-id"] = installationId.ToString("D"),
                ["arte-content-status"] = BackupContentStatus.Healthy.ToString(),
                ["arte-package-sha256"] = sha,
                ["arte-created-utc"] = createdUtc.ToUniversalTime().ToString("O")
            };
            _packages[(key, version)] = new RemotePackage(
                bytes,
                new CloudRemoteObjectMetadata(
                    "portfolio-example-backups",
                    key,
                    version,
                    bytes.LongLength,
                    createdUtc,
                    "etag",
                    sha,
                    "FULL_OBJECT",
                    metadata));
            return new CloudRecoveryCatalogItem(
                installationId,
                backupId,
                key,
                version,
                true,
                createdUtc,
                bytes.LongLength,
                BackupContentStatus.Healthy,
                0,
                sha,
                sha,
                "FULL_OBJECT",
                true);
        }

        public void ChangeVersion(string key, string oldVersion, string newVersion)
        {
            var value = _packages[(key, oldVersion)];
            _packages[(key, oldVersion)] = value with
            {
                Metadata = value.Metadata with { VersionId = newVersion }
            };
        }

        public Task<IReadOnlyList<CloudRemoteObjectInfo>> ListVersionsAsync(
            string bucketName,
            string prefix,
            CancellationToken cancellationToken = default)
        {
            LastListedPrefix = prefix;
            var values = _packages
                .Where(entry => entry.Key.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(entry => new CloudRemoteObjectInfo(
                    entry.Key.Key,
                    entry.Key.VersionId,
                    IsLatest: true,
                    IsDeleteMarker: false,
                    entry.Value.Bytes.LongLength,
                    entry.Value.Metadata.LastModifiedUtc,
                    entry.Value.Metadata.ETag))
                .ToArray();
            return Task.FromResult<IReadOnlyList<CloudRemoteObjectInfo>>(values);
        }

        public Task<CloudRemoteObjectMetadata> GetMetadataAsync(
            string bucketName,
            string key,
            string? versionId,
            CancellationToken cancellationToken = default)
        {
            var version = versionId ?? _packages.Keys.Single(item => item.Key == key).VersionId;
            return Task.FromResult(_packages[(key, version)].Metadata);
        }

        public async Task DownloadAsync(
            string bucketName,
            string key,
            string? versionId,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            if (ThrowDuringDownload)
            {
                await destination.WriteAsync(new byte[] { 0x41, 0x42, 0x43 }, cancellationToken);
                throw new IOException("simulated interrupted recovery download");
            }

            var version = versionId ?? _packages.Keys.Single(item => item.Key == key).VersionId;
            await destination.WriteAsync(_packages[(key, version)].Bytes, cancellationToken);
        }

        public void Dispose()
        {
        }

        private CloudRecoveryCatalogItem ToCatalogItem(
            string key,
            string version,
            BackupManifest manifest,
            string packageSha,
            Guid installationId) => new(
                installationId,
                manifest.BackupId,
                key,
                version,
                true,
                manifest.CreatedUtc,
                _packages[(key, version)].Bytes.LongLength,
                manifest.ContentStatus,
                manifest.OmittedAssetCount,
                packageSha,
                packageSha,
                "FULL_OBJECT",
                true);

        private sealed record RemotePackage(byte[] Bytes, CloudRemoteObjectMetadata Metadata);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }
}
