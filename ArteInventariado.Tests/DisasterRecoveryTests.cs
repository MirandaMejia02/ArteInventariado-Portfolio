using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.CloudBackup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class DisasterRecoveryTests
{
    [Fact]
    public void EmptyMachineIsFresh()
    {
        using var fixture = Fixture.Create();
        Assert.Equal(InstallationLifecycleState.FreshMachine, fixture.Classify().State);
    }

    [Fact]
    public void StationWithoutDatabaseIsDamaged()
    {
        using var fixture = Fixture.Create();
        new LocalStationConfigurationStore(fixture.Paths).EnsureInitialized();
        var assessment = fixture.Classify();
        Assert.Equal(InstallationLifecycleState.ExistingDamagedInstallation, assessment.State);
    }

    [Fact]
    public async Task ValidDatabaseWithoutPrincipalRequiresPrincipalProvisioning()
    {
        using var fixture = Fixture.Create();
        new LocalStationConfigurationStore(fixture.Paths).EnsureInitialized();
        await fixture.MigrateAsync();

        var assessment = fixture.Classify();

        Assert.Equal(InstallationLifecycleState.PrincipalProvisioningRequired, assessment.State);
        Assert.True(assessment.DatabaseValid);
        Assert.False(assessment.Station.IsConfigured);
    }

    [Fact]
    public async Task ValidDatabaseWithExistingPrincipalAndPendingStationIsSetupPending()
    {
        using var fixture = Fixture.Create();
        await fixture.PreparePendingStationWithPrincipalAsync();

        var assessment = fixture.Classify();

        Assert.Equal(InstallationLifecycleState.SetupPending, assessment.State);
        Assert.True(assessment.DatabaseValid);
        Assert.True(assessment.DatabaseFileExists);
        Assert.True(assessment.StationFileExists);
        Assert.False(assessment.Station.IsConfigured);
        Assert.False(assessment.Station.IsFirstRunCompleted);
    }

    [Fact]
    public void InstalledBinariesWithoutPointerAreDamaged()
    {
        using var fixture = Fixture.Create();
        File.WriteAllBytes(fixture.Paths.LauncherPath, [1]);
        Assert.Equal(
            InstallationLifecycleState.ExistingDamagedInstallation,
            fixture.Classify().State);
    }

    [Fact]
    public async Task ConfiguredStationAndValidDatabaseIsExistingValid()
    {
        using var fixture = Fixture.Create();
        var station = new LocalStationConfigurationStore(fixture.Paths);
        station.EnsureInitialized();
        station.SaveConfigured(InstallationMode.Principal);
        await fixture.MigrateAsync();
        Assert.Equal(InstallationLifecycleState.ExistingValidInstallation, fixture.Classify().State);
    }

    [Fact]
    public async Task ValidOlderDatabaseRemainsExistingValidUntilInitializerMigratesIt()
    {
        using var fixture = Fixture.Create();
        var station = new LocalStationConfigurationStore(fixture.Paths);
        station.EnsureInitialized();
        station.SaveConfigured(InstallationMode.Principal);
        Directory.CreateDirectory(fixture.Paths.DataRoot);

        var migrationOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={fixture.Paths.DatabasePath};Pooling=False")
            .Options;
        await using (var context = new AppDbContext(migrationOptions))
        {
            await context.Database.MigrateAsync("20260825055759_V110OperationalSkuIntegrations");
        }

        var assessment = fixture.Classify();

        Assert.Equal(InstallationLifecycleState.ExistingValidInstallation, assessment.State);
        Assert.True(assessment.DatabaseValid);
    }

    [Fact]
    public async Task RecognizedLegacyIsMigrationCandidate()
    {
        using var fixture = Fixture.Create();
        var legacy = ArteInventariadoPathProvider.CreateLegacy(Path.Combine(fixture.Root, "legacy"));
        Directory.CreateDirectory(legacy.StationRoot);
        var station = new LocalStationConfigurationStore(legacy);
        station.EnsureInitialized();
        station.SaveConfigured(InstallationMode.Principal);
        Directory.CreateDirectory(legacy.DataRoot);
        await MigrateAsync(legacy.DatabasePath);
        Assert.Equal(
            InstallationLifecycleState.MigrationCandidate,
            new InstallationStateClassifier(fixture.Paths, legacy, ReadMigrations).Classify().State);
    }

    [Fact]
    public void H2JournalIsRecoveryPending()
    {
        using var fixture = Fixture.Create();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        new DisasterRecoveryJournalStore(fixture.Paths.RecoveryRoot).Save(
            new DisasterRecoveryJournalDocument(
                DisasterRecoveryJournalStore.CurrentSchemaVersion,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DisasterRecoveryPhase.CandidateSelected,
                source,
                destination,
                InstallationMode.Principal,
                Guid.NewGuid(),
                null,
                null,
                null,
                false,
                false));
        Assert.Equal(InstallationLifecycleState.RecoveryPending, fixture.Classify().State);
    }

    [Fact]
    public async Task CatalogIsAvailableOnlyOnFreshMachine()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.Service.GetCatalogAsync(fixture.CloudRequest);
        Assert.Single(result.Items);
        Assert.Equal(fixture.Selection.BackupId, result.RecommendedHealthy!.BackupId);
    }

    [Fact]
    public async Task OwnerMustConfirmRemoteInstallationScope()
    {
        using var fixture = Fixture.Create();
        var request = fixture.CloudRequest with { OwnerConfirmed = false };
        var result = await fixture.Service.RecoverAsync(fixture.Request with { Cloud = request });
        Assert.Equal(DisasterRecoveryOutcome.Failed, result.Outcome);
        Assert.Equal(InstallationLifecycleState.FreshMachine, fixture.Classify().State);
    }

    [Fact]
    public async Task ExistingInstallationCannotUseDisasterBypass()
    {
        using var fixture = Fixture.Create();
        var station = new LocalStationConfigurationStore(fixture.Paths);
        station.EnsureInitialized();
        station.SaveConfigured(InstallationMode.Principal);
        await fixture.MigrateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.GetCatalogAsync(fixture.CloudRequest));
    }

    [Fact]
    public async Task RecoveryCreatesSchemaOnlyDatabaseAfterCandidate()
    {
        using var fixture = Fixture.Create();
        var observedBeforeCandidate = false;
        fixture.Download = (_, _, _) =>
        {
            observedBeforeCandidate = !File.Exists(fixture.Paths.DatabasePath);
            return Task.FromResult(fixture.Candidate);
        };
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.True(observedBeforeCandidate);
        Assert.Equal(DisasterRecoveryOutcome.Succeeded, result.Outcome);
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task RecoveryKeepsSourceAndDestinationIdentitiesDistinct()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(fixture.SourceInstallationId, result.SourceInstallationId);
        Assert.NotEqual(result.SourceInstallationId, result.DestinationInstallationId);
        Assert.Equal(result.DestinationInstallationId, new LocalStationConfigurationStore(fixture.Paths).Load().Status.InstallationId);
    }

    [Fact]
    public async Task InvalidDestinationModeIsRejectedBeforeDatabaseCreation()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.Service.RecoverAsync(
            fixture.Request with { DestinationMode = (InstallationMode)999 });
        Assert.Equal(DisasterRecoveryOutcome.Failed, result.Outcome);
        Assert.False(File.Exists(fixture.Paths.DatabasePath));
        Assert.Equal(InstallationLifecycleState.FreshMachine, fixture.Classify().State);
    }

    [Fact]
    public async Task RecoveryUsesExplicitStationMode()
    {
        using var fixture = Fixture.Create();
        fixture.Request = fixture.Request with
        {
            DestinationMode = InstallationMode.Consulta
        };
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Succeeded, result.Outcome);
        Assert.Equal(InstallationMode.Consulta, new LocalStationConfigurationStore(fixture.Paths).Load().Status.Mode);
    }

    [Fact]
    public async Task CandidateFailureReturnsToFreshState()
    {
        using var fixture = Fixture.Create();
        fixture.Download = (_, _, _) => throw new InvalidDataException("candidate corrupt");
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Failed, result.Outcome);
        Assert.Equal(InstallationLifecycleState.FreshMachine, fixture.Classify().State);
        Assert.False(File.Exists(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task CandidateIdentityMismatchIsRejectedBeforeH1()
    {
        using var fixture = Fixture.Create();
        var restoreCalls = 0;
        fixture.Download = (_, _, _) => Task.FromResult(fixture.Candidate with
        {
            InstallationId = Guid.NewGuid()
        });
        fixture.Restore = (_, _) =>
        {
            restoreCalls++;
            return Task.FromResult(new ProtectedRestoreHelperResult(
                Guid.NewGuid(), ProtectedRestoreOutcome.Succeeded, "unexpected", false, null, []));
        };

        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Failed, result.Outcome);
        Assert.Equal(0, restoreCalls);
        Assert.Equal(InstallationLifecycleState.FreshMachine, fixture.Classify().State);
    }

    [Fact]
    public async Task UacCancellationPreservesFreshState()
    {
        using var fixture = Fixture.Create(isElevated: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.GetCatalogAsync(fixture.CloudRequest));
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Rejected, result.Outcome);
        Assert.Equal(InstallationLifecycleState.FreshMachine, fixture.Classify().State);
        Assert.False(File.Exists(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task CloudAuthenticationFailurePreservesFreshState()
    {
        using var fixture = Fixture.Create();
        fixture.Download = (_, _, _) => throw new UnauthorizedAccessException("MFA rechazada");
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Rejected, result.Outcome);
        Assert.Equal(InstallationLifecycleState.FreshMachine, fixture.Classify().State);
        Assert.False(File.Exists(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task RecoveryRequiredLeavesJournalForH1()
    {
        using var fixture = Fixture.Create();
        fixture.Restore = (_, _) => Task.FromResult(new ProtectedRestoreHelperResult(
            Guid.NewGuid(),
            ProtectedRestoreOutcome.RecoveryRequired,
            "pending",
            false,
            null,
            []));
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.RecoveryPending, result.Outcome);
        Assert.True(DisasterRecoveryJournalStore.HasPending(fixture.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task PendingRecoveryBlocksAConcurrentSecondAttempt()
    {
        using var fixture = Fixture.Create();
        fixture.Restore = (_, _) => Task.FromResult(new ProtectedRestoreHelperResult(
            Guid.NewGuid(), ProtectedRestoreOutcome.RecoveryRequired, "pending", false, null, []));
        var first = await fixture.Service.RecoverAsync(fixture.Request);
        var second = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.RecoveryPending, first.Outcome);
        Assert.Equal(DisasterRecoveryOutcome.Rejected, second.Outcome);
    }

    [Fact]
    public async Task OriginalPrincipalCanReappearWithoutTemporaryPrincipal()
    {
        using var fixture = Fixture.Create();
        fixture.Restore = async (_, _) =>
        {
            await using var context = fixture.OpenContext();
            context.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Principal original",
                NombreUsuario = "admin",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            await context.SaveChangesAsync();
            return new ProtectedRestoreHelperResult(Guid.NewGuid(), ProtectedRestoreOutcome.Succeeded, "ok", false, null, []);
        };
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Succeeded, result.Outcome);
        await using var verify = fixture.OpenContext(readOnly: true);
        Assert.Single(await verify.Usuarios.Where(user => user.NombreUsuario == "admin").ToListAsync());
    }

    [Fact]
    public async Task DelegatedAdminDoesNotReplaceOriginalPrincipal()
    {
        using var fixture = Fixture.Create();
        fixture.Restore = async (_, _) =>
        {
            await using var context = fixture.OpenContext();
            context.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Administrador delegado",
                NombreUsuario = "delegado",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            await context.SaveChangesAsync();
            return new ProtectedRestoreHelperResult(Guid.NewGuid(), ProtectedRestoreOutcome.Succeeded, "ok", false, null, []);
        };

        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.RecoveryPending, result.Outcome);
        Assert.True(DisasterRecoveryJournalStore.HasPending(fixture.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task DegradedCatalogIsFallbackAndRecoveryRemainsExplicit()
    {
        using var fixture = Fixture.Create();
        var degraded = fixture.Selection with
        {
            BackupId = Guid.NewGuid(),
            ContentStatus = BackupContentStatus.DegradedAssets,
            OmittedAssetCount = 2
        };
        fixture.Catalog = new CloudRecoveryCatalogResult([degraded], null, degraded, ["assets omitidos"]);
        var catalog = await fixture.Service.GetCatalogAsync(fixture.CloudRequest);
        Assert.Null(catalog.RecommendedHealthy);
        Assert.Equal(degraded.BackupId, catalog.RecommendedFallback!.BackupId);
    }

    [Fact]
    public async Task SuccessfulRecoveryLeavesNoH2PendingJournal()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Succeeded, result.Outcome);
        Assert.False(DisasterRecoveryJournalStore.HasPending(fixture.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task RecoveryCancellationLeavesExplicitPendingStateForReconciliation()
    {
        using var fixture = Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await fixture.Service.RecoverAsync(fixture.Request, cancellation.Token);
        Assert.Equal(DisasterRecoveryOutcome.Cancelled, result.Outcome);
        Assert.Equal(InstallationLifecycleState.RecoveryPending, fixture.Classify().State);
    }

    [Fact]
    public async Task RecoveryOriginMetadataContainsNoCloudSecrets()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.Service.RecoverAsync(fixture.Request);
        Assert.Equal(DisasterRecoveryOutcome.Succeeded, result.Outcome);
        var originPath = Path.Combine(fixture.Paths.MaintenanceRoot, "recovery-origin.json");
        var origin = await File.ReadAllTextAsync(originPath);
        Assert.DoesNotContain(fixture.Authentication.BootstrapAccessKeyId, origin, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Authentication.BootstrapSecretAccessKey, origin, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Authentication.MfaTokenCode, origin, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ReadMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var context = new AppDbContext(options);
        return context.Database.GetMigrations().ToArray();
    }

    private static async Task MigrateAsync(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, ArteInventariadoPathProvider paths, bool isElevated)
        {
            Root = root;
            Paths = paths;
            IsElevated = isElevated;
            SourceInstallationId = Guid.NewGuid();
            BackupId = Guid.NewGuid();
            Configuration = new CloudBackupConfiguration(
                CloudBackupConfigurationStore.CurrentSchemaVersion,
                true,
                "portfolio-example-backups",
                "us-east-1",
                "v1",
                5L * 1024 * 1024,
                5L * 1024 * 1024);
            Authentication = new CloudRecoveryAuthenticationRequest(
                "access", "secret", "arn:aws:iam::123456789012:role/recovery",
                "arn:aws:iam::123456789012:mfa/owner", "123456", "us-east-1");
            var candidateDirectory = Path.Combine(paths.CloudRecoveryStagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(candidateDirectory);
            var candidatePath = Path.Combine(candidateDirectory, "package.aibak");
            File.WriteAllBytes(candidatePath, [1, 2, 3]);
            Candidate = new CloudRecoveryCandidate(
                SourceInstallationId,
                BackupId,
                candidatePath,
                $"v1/{SourceInstallationId:D}/{BackupId:D}.aibak",
                "version-1",
                DateTimeOffset.UtcNow,
                BackupContentStatus.Healthy,
                0,
                3,
                new string('A', 64),
                null,
                null,
                []);
            Selection = new CloudRecoveryCatalogItem(
                SourceInstallationId,
                BackupId,
                Candidate.Key,
                Candidate.VersionId,
                true,
                Candidate.CreatedUtc,
                Candidate.SizeBytes,
                Candidate.ContentStatus,
                Candidate.OmittedAssetCount,
                Candidate.PackageSha256,
                null,
                null,
                true);
            CloudRequest = new DisasterRecoveryCloudRequest(Configuration, Authentication, SourceInstallationId, true);
            Request = new DisasterRecoveryRequest(CloudRequest, Selection, InstallationMode.Principal, true);
            Catalog = new CloudRecoveryCatalogResult([Selection], Selection, null, []);
            Download = (_, _, _) => Task.FromResult(Candidate);
            Restore = async (_, _) =>
            {
                await using var context = OpenContext();
                context.Usuarios.Add(new Usuario
                {
                    NombreCompleto = "Principal original",
                    NombreUsuario = "admin",
                    PasswordHash = "hash",
                    Rol = RolUsuario.ADMIN,
                    Activo = true
                });
                await context.SaveChangesAsync();
                return new ProtectedRestoreHelperResult(
                    Guid.NewGuid(), ProtectedRestoreOutcome.Succeeded, "ok", false, null, []);
            };
        }

        public string Root { get; }
        public ArteInventariadoPathProvider Paths { get; }
        public Guid SourceInstallationId { get; }
        public Guid BackupId { get; }
        public CloudBackupConfiguration Configuration { get; }
        public CloudRecoveryAuthenticationRequest Authentication { get; }
        public DisasterRecoveryCloudRequest CloudRequest { get; }
        public DisasterRecoveryRequest Request { get; set; }
        public CloudRecoveryCatalogItem Selection { get; }
        public CloudRecoveryCandidate Candidate { get; }
        public CloudRecoveryCatalogResult Catalog { get; set; }
        public Func<DisasterRecoveryCloudRequest, CloudRecoveryCatalogItem, CancellationToken, Task<CloudRecoveryCandidate>> Download { get; set; }
        public Func<ProtectedRestoreRequest, CancellationToken, Task<ProtectedRestoreHelperResult>> Restore { get; set; }
        public bool IsElevated { get; }

        public static Fixture Create(bool isElevated = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-h2-test-{Guid.NewGuid():N}");
            var paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"),
                Path.Combine(root, "user"));
            Directory.CreateDirectory(paths.MachineInstallRoot);
            Directory.CreateDirectory(paths.MachineDataRoot);
            Directory.CreateDirectory(paths.UserDataRoot);
            return new Fixture(root, paths, isElevated);
        }

        public InstallationStateAssessment Classify() =>
            new InstallationStateClassifier(Paths, ArteInventariadoPathProvider.CreateLegacy(Path.Combine(Root, "legacy")), ReadMigrations).Classify();

        public DisasterRecoveryBootstrapService Service =>
            new(
                Paths,
                new InstallationStateClassifier(Paths, ArteInventariadoPathProvider.CreateLegacy(Path.Combine(Root, "legacy")), ReadMigrations),
                (_, _) => Task.FromResult(Catalog),
                (request, selection, cancellationToken) => Download(request, selection, cancellationToken),
                TimeProvider.System,
                (request, cancellationToken) => Restore(request, cancellationToken),
                () => IsElevated,
                allowSyntheticRoots: true);

        public async Task MigrateAsync() => await DisasterRecoveryTests.MigrateAsync(Paths.DatabasePath);

        public async Task PreparePendingStationWithPrincipalAsync()
        {
            new LocalStationConfigurationStore(Paths).EnsureInitialized();
            await MigrateAsync();
            await using var context = OpenContext();
            context.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Administrador principal",
                NombreUsuario = ProtectedAdministrator.UserName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestCredentials.Password("pending-setup")),
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        public AppDbContext OpenContext(bool readOnly = false)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Paths.DatabasePath,
                    Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString())
                .Options;
            return new AppDbContext(options);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
