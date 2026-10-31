using System.Security.Cryptography;
using System.IO.Compression;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class MachineWideBusinessDatabaseResetTests
{
    [Theory]
    [InlineData("REINICIAR", false)]
    [InlineData("CANCELAR", false)]
    [InlineData(" reiniciar db ", true)]
    [InlineData("REINICIAR DB", true)]
    [InlineData(null, false)]
    public void ResetRequiresTheExactConfirmationPhrase(string? value, bool expected)
    {
        Assert.Equal(expected, MachineWideBusinessDatabaseResetService.IsConfirmationValid(value));
    }

    [Fact]
    public void MissingConfirmationDoesNotCreateBackupOrChangeDatabase()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var originalHash = HashFile(fixture.Paths.DatabasePath);
        var backupCalls = 0;
        var reset = fixture.CreateResetService(
            createBackup: (_, _, _) =>
            {
                backupCalls++;
                return fixture.CreateHealthyBackup();
            });

        Assert.Throws<MachineWideDatabaseResetConfirmationRequiredException>(() =>
            reset.ResetSynthetic(fixture.Paths, "nope"));

        Assert.Equal(0, backupCalls);
        Assert.Equal(originalHash, HashFile(fixture.Paths.DatabasePath));
        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
    }

    [Fact]
    public void ConsultationStationIsRejectedBeforeBackupOrAnyDatabaseMutation()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var store = new LocalStationConfigurationStore(fixture.Paths);
        store.SaveConfigured(InstallationMode.Consulta);
        var originalHash = HashFile(fixture.Paths.DatabasePath);
        var backupCalls = 0;
        var reset = fixture.CreateResetService(
            createBackup: (_, _, _) =>
            {
                backupCalls++;
                return fixture.CreateHealthyBackup();
            });

        Assert.Throws<InvalidOperationException>(() =>
            reset.ResetSynthetic(fixture.Paths, MachineWideBusinessDatabaseResetService.ConfirmationPhrase));

        Assert.Equal(0, backupCalls);
        Assert.Equal(originalHash, HashFile(fixture.Paths.DatabasePath));
    }

    [Fact]
    public void UnhealthyBackupLeavesTheExistingDatabaseAndStationUntouched()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var originalHash = HashFile(fixture.Paths.DatabasePath);
        var originalStation = File.ReadAllBytes(fixture.Paths.StationCorePath);
        var reset = fixture.CreateResetService(
            createBackup: (paths, _, _) =>
            {
                var journal = MachineWideDatabaseResetJournalStore.LoadSynthetic(paths);
                Assert.NotNull(journal);
                Assert.Equal(MachineWideDatabaseResetPhase.BackingUp, journal.Phase);

                Assert.Throws<InvalidOperationException>(() =>
                    MachineWideDatabaseResetJournalStore.EnsureUpdaterMayProceedSynthetic(paths));

                return new MachineWideBackupRunResult(
                    MachineWideBackupRunOutcome.SkippedConcurrent,
                    null,
                    null,
                    null,
                    null,
                    "Respaldo concurrente",
                    []);
            });

        Assert.Throws<InvalidOperationException>(() =>
            reset.ResetSynthetic(fixture.Paths, MachineWideBusinessDatabaseResetService.ConfirmationPhrase));

        Assert.Equal(originalHash, HashFile(fixture.Paths.DatabasePath));
        Assert.Equal(originalStation, File.ReadAllBytes(fixture.Paths.StationCorePath));
        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
    }

    [Fact]
    public void PendingUpdatePreventsDatabaseResetBeforeBackupOrMutation()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var originalHash = HashFile(fixture.Paths.DatabasePath);
        new UpdateStateStore(fixture.Paths.UpdateStatePath).Save(new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.Preparing,
            "1.14.2",
            "1.15.0",
            "package.zip",
            "manifest.json",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow));
        var backupCalls = 0;
        var reset = fixture.CreateResetService(
            createBackup: (_, _, _) =>
            {
                backupCalls++;
                return fixture.CreateHealthyBackup();
            });

        Assert.Throws<InvalidOperationException>(() => reset.ResetSynthetic(
            fixture.Paths,
            MachineWideBusinessDatabaseResetService.ConfirmationPhrase));

        Assert.Equal(0, backupCalls);
        Assert.Equal(originalHash, HashFile(fixture.Paths.DatabasePath));
        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
    }

    [Fact]
    public void PendingDatabaseResetRecoveryRestoresDatabaseEvenIfCrashHappensDuringBackup()
    {
        using var fixture = new ResetFixture();
        var installationId = fixture.InitializePrincipalWithOldEmployee();
        var originalHash = HashFile(fixture.Paths.DatabasePath);
        var operationId = Guid.NewGuid();
        MachineWideDatabaseResetJournalStore.EnsureStagingDirectory(
            fixture.Paths,
            operationId,
            allowSyntheticRoots: true);
        MachineWideDatabaseResetJournalStore.Save(
            fixture.Paths,
            new MachineWideDatabaseResetJournal(
                MachineWideDatabaseResetJournalStore.CurrentSchemaVersion,
                operationId,
                installationId,
                InstallationMode.Principal,
                MachineWideDatabaseResetPhase.BackingUp,
                DateTimeOffset.UtcNow,
                "pending.aibak",
                new string('0', 64)),
            allowSyntheticRoots: true);

        fixture.CreateResetService().RecoverPendingResetSynthetic(fixture.Paths);

        Assert.Equal(originalHash, HashFile(fixture.Paths.DatabasePath));
        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
    }

    [Fact]
    public void ActiveMachineWideMaintenanceLeasePreventsDatabaseReset()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var originalHash = HashFile(fixture.Paths.DatabasePath);
        var backupCalls = 0;
        var reset = fixture.CreateResetService(
            createBackup: (_, _, _) =>
            {
                backupCalls++;
                return fixture.CreateHealthyBackup();
            });

        using (MachineWideMaintenanceLease.Acquire(fixture.Paths, allowSyntheticRoots: true))
        {
            Assert.Throws<InvalidOperationException>(() => reset.ResetSynthetic(
                fixture.Paths,
                MachineWideBusinessDatabaseResetService.ConfirmationPhrase));
        }

        Assert.Equal(0, backupCalls);
        Assert.Equal(originalHash, HashFile(fixture.Paths.DatabasePath));
    }

    [Fact]
    public void SuccessfulResetCreatesEmptyBusinessDatabaseAndPreservesStationAssetsAndBackup()
    {
        using var fixture = new ResetFixture();
        var originalInstallationId = fixture.InitializePrincipalWithOldEmployee();
        var stationBytes = File.ReadAllBytes(fixture.Paths.StationCorePath);
        var assetPath = Path.Combine(fixture.Paths.ProductBusinessAssetsRoot, "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllText(assetPath, "product assets stay outside the DB");
        var operationalSettingsPath = fixture.Paths.StationOperationalSettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(operationalSettingsPath)!);
        File.WriteAllText(
            operationalSettingsPath,
            "{\"schemaVersion\":5,\"backup\":null,\"reports\":null,\"pos\":{\"quickPosEnabled\":true},\"peripherals\":null}");
        var operationalSettingsBytes = File.ReadAllBytes(operationalSettingsPath);
        var backupPath = Path.Combine(fixture.Paths.ProtectedBackupsRoot, "older-backup.aibak");
        Directory.CreateDirectory(fixture.Paths.ProtectedBackupsRoot);
        File.WriteAllText(backupPath, "preserve older backup");

        var reset = fixture.CreateResetService(
            createBackup: fixture.CreateActualBackup,
            provisionPrincipal: (paths, _) =>
            {
                using var context = fixture.CreateContext(paths.DatabasePath);
                context.Usuarios.Add(new Usuario
                {
                    NombreCompleto = "Administrador principal",
                    NombreUsuario = ProtectedAdministrator.UserName,
                    PasswordHash = "test-hash",
                    Rol = RolUsuario.ADMIN,
                    Activo = true
                });
                context.SaveChanges();
                return 0;
            });

        var result = reset.ResetSynthetic(
            fixture.Paths,
            MachineWideBusinessDatabaseResetService.ConfirmationPhrase);

        Assert.Equal(originalInstallationId, result.InstallationId);
        Assert.Equal(stationBytes, File.ReadAllBytes(fixture.Paths.StationCorePath));
        Assert.Equal(operationalSettingsBytes, File.ReadAllBytes(operationalSettingsPath));
        Assert.Equal("product assets stay outside the DB", File.ReadAllText(assetPath));
        Assert.Equal("preserve older backup", File.ReadAllText(backupPath));
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(result.BackupSha256, HashFile(result.BackupPath));
        using (var package = ZipFile.OpenRead(result.BackupPath))
        {
            Assert.Contains(package.Entries, entry => entry.FullName == "manifest.json");
            Assert.Contains(package.Entries, entry => entry.FullName == "database.db");
        }
        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));

        using var newContext = fixture.CreateContext(fixture.Paths.DatabasePath, readOnly: true);
        var users = newContext.Usuarios.AsNoTracking().ToList();
        Assert.Single(users);
        Assert.Equal(ProtectedAdministrator.UserName, users[0].NombreUsuario);
        Assert.Equal(RolUsuario.ADMIN, users[0].Rol);
        Assert.Empty(newContext.Ventas);
        Assert.Empty(newContext.Compras);
        Assert.Empty(newContext.Clientes);
        Assert.Empty(newContext.Proveedores);
        Assert.Empty(newContext.Productos);
        Assert.Empty(newContext.ProductoSkus);
        Assert.Empty(newContext.InventarioSaldos);
        Assert.Empty(newContext.Cajas);
        Assert.Empty(newContext.Cotizaciones);
        Assert.Empty(newContext.PedidosPersonalizados);
    }

    [Fact]
    public void BackupChangedDuringProvisioningKeepsPreviousDatabaseUntilRecoveryVerifiesBackup()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var originalDatabaseHash = HashFile(fixture.Paths.DatabasePath);
        MachineWideBackupRunResult? backup = null;
        var reset = fixture.CreateResetService(
            createBackup: (_, operationId, _) =>
            {
                var created = fixture.CreateHealthyBackup(operationId);
                backup = created;
                return created;
            },
            provisionPrincipal: (paths, _) =>
            {
                using var context = fixture.CreateContext(paths.DatabasePath);
                context.Usuarios.Add(new Usuario
                {
                    NombreCompleto = "Administrador principal",
                    NombreUsuario = ProtectedAdministrator.UserName,
                    PasswordHash = "test-hash",
                    Rol = RolUsuario.ADMIN,
                    Activo = true
                });
                context.SaveChanges();
                File.WriteAllText(backup!.FilePath!, "tampered during provisioning");
                return 0;
            });

        Assert.Throws<InvalidDataException>(() => reset.ResetSynthetic(
            fixture.Paths,
            MachineWideBusinessDatabaseResetService.ConfirmationPhrase));

        var journal = MachineWideDatabaseResetJournalStore.LoadSynthetic(fixture.Paths);
        Assert.NotNull(journal);
        Assert.Equal(MachineWideDatabaseResetPhase.Committed, journal.Phase);
        var stagedDatabase = MachineWideDatabaseResetJournalStore.StagedDatabasePath(
            fixture.Paths,
            journal.OperationId);
        Assert.Equal(originalDatabaseHash, HashFile(stagedDatabase));

        File.WriteAllText(backup!.FilePath!, "synthetic verified backup package");
        reset.RecoverPendingResetSynthetic(fixture.Paths);

        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
        Assert.False(File.Exists(stagedDatabase));
        using var activeDatabase = fixture.CreateContext(fixture.Paths.DatabasePath, readOnly: true);
        Assert.Contains(activeDatabase.Usuarios, user => user.NombreUsuario == ProtectedAdministrator.UserName);
        Assert.DoesNotContain(activeDatabase.Usuarios, user => user.NombreUsuario == "empleado-anterior");
    }

    [Fact]
    public void CancelledPrincipalProvisioningRollsBackToTheExactPreviousDatabase()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var originalDatabaseHash = HashFile(fixture.Paths.DatabasePath);
        var originalStation = File.ReadAllBytes(fixture.Paths.StationCorePath);
        var reset = fixture.CreateResetService(
            provisionPrincipal: (_, _) => 0); // The existing UI returns success on Cancel; Setup verifies the DB.

        Assert.Throws<InvalidOperationException>(() => reset.ResetSynthetic(
            fixture.Paths,
            MachineWideBusinessDatabaseResetService.ConfirmationPhrase));

        Assert.Equal(originalDatabaseHash, HashFile(fixture.Paths.DatabasePath));
        Assert.Equal(originalStation, File.ReadAllBytes(fixture.Paths.StationCorePath));
        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
        using var restored = fixture.CreateContext(fixture.Paths.DatabasePath, readOnly: true);
        Assert.Contains(restored.Usuarios, user => user.NombreUsuario == "empleado-anterior");
    }

    [Fact]
    public async Task PendingProvisioningJournalAllowsOnlyMatchingEmptyDbProvisioningAndRecoveryRestoresOldDb()
    {
        using var fixture = new ResetFixture();
        var installationId = fixture.InitializePrincipalWithOldEmployee();
        var operationId = Guid.NewGuid();
        var backup = fixture.CreateHealthyBackup();
        MachineWideDatabaseResetJournalStore.EnsureStagingDirectory(fixture.Paths, operationId, allowSyntheticRoots: true);
        var stagedDatabase = MachineWideDatabaseResetJournalStore.StagedDatabasePath(fixture.Paths, operationId);
        File.Move(fixture.Paths.DatabasePath, stagedDatabase);
        await MachineWideDatabaseInitializer.InitializeSyntheticAsync(fixture.Paths);
        MachineWideDatabaseResetJournalStore.Save(
            fixture.Paths,
            new MachineWideDatabaseResetJournal(
                MachineWideDatabaseResetJournalStore.CurrentSchemaVersion,
                operationId,
                installationId,
                InstallationMode.Principal,
                MachineWideDatabaseResetPhase.Provisioning,
                DateTimeOffset.UtcNow,
                Path.GetFileName(backup.FilePath!),
                backup.Sha256!),
            allowSyntheticRoots: true);

        Assert.True(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
        Assert.True(MachineWideDatabaseResetJournalStore.IsProvisioningRequestSynthetic(fixture.Paths, operationId));
        Assert.False(MachineWideDatabaseResetJournalStore.IsProvisioningRequestSynthetic(fixture.Paths, Guid.NewGuid()));

        var reset = fixture.CreateResetService();
        reset.RecoverPendingResetSynthetic(fixture.Paths);

        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
        using var restored = fixture.CreateContext(fixture.Paths.DatabasePath, readOnly: true);
        Assert.Contains(restored.Usuarios, user => user.NombreUsuario == "empleado-anterior");
    }

    [Fact]
    public async Task ConfiguredStationCannotReprovisionPrincipalWithoutResetAuthorization()
    {
        using var fixture = new ResetFixture();
        fixture.InitializePrincipalWithOldEmployee();
        var service = fixture.CreatePrincipalProvisioningService(null);

        Assert.False(await service.IsRequiredAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProvisionAsync(
            new PrincipalProvisioningRequest("test-password-123", "test-password-123")));
    }

    [Fact]
    public async Task ResetAuthorizationAllowsOnePrincipalProvisionOnlyWhileNewDatabaseIsEmpty()
    {
        using var fixture = new ResetFixture();
        var installationId = fixture.InitializePrincipalWithOldEmployee();
        var operationId = Guid.NewGuid();
        var backup = fixture.CreateHealthyBackup();
        MachineWideDatabaseResetJournalStore.EnsureStagingDirectory(fixture.Paths, operationId, allowSyntheticRoots: true);
        var stagedDatabase = MachineWideDatabaseResetJournalStore.StagedDatabasePath(fixture.Paths, operationId);
        File.Move(fixture.Paths.DatabasePath, stagedDatabase);
        await MachineWideDatabaseInitializer.InitializeSyntheticAsync(fixture.Paths);
        MachineWideDatabaseResetJournalStore.Save(
            fixture.Paths,
            new MachineWideDatabaseResetJournal(
                MachineWideDatabaseResetJournalStore.CurrentSchemaVersion,
                operationId,
                installationId,
                InstallationMode.Principal,
                MachineWideDatabaseResetPhase.Provisioning,
                DateTimeOffset.UtcNow,
                Path.GetFileName(backup.FilePath!),
                backup.Sha256!),
            allowSyntheticRoots: true);

        var service = fixture.CreatePrincipalProvisioningService(operationId);
        Assert.False(MachineWideDatabaseResetJournalStore.IsProvisioningRequestSynthetic(fixture.Paths, Guid.NewGuid()));
        Assert.True(await service.IsRequiredAsync());
        await service.ProvisionAsync(
            new PrincipalProvisioningRequest("test-password-123", "test-password-123"));

        Assert.False(await service.IsRequiredAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProvisionAsync(
            new PrincipalProvisioningRequest("test-password-456", "test-password-456")));
        using var context = fixture.CreateContext(fixture.Paths.DatabasePath, readOnly: true);
        Assert.Single(context.Usuarios);
        Assert.Contains(context.Usuarios, user => user.NombreUsuario == ProtectedAdministrator.UserName);
    }

    [Fact]
    public async Task CommittedRecoveryRetainsOldDatabaseUntilProtectedBackupHashIsVerified()
    {
        using var fixture = new ResetFixture();
        var installationId = fixture.InitializePrincipalWithOldEmployee();
        var operationId = Guid.NewGuid();
        var backup = fixture.CreateHealthyBackup();
        MachineWideDatabaseResetJournalStore.EnsureStagingDirectory(fixture.Paths, operationId, allowSyntheticRoots: true);
        var stagedDatabase = MachineWideDatabaseResetJournalStore.StagedDatabasePath(fixture.Paths, operationId);
        File.Move(fixture.Paths.DatabasePath, stagedDatabase);
        await MachineWideDatabaseInitializer.InitializeSyntheticAsync(fixture.Paths);
        using (var context = fixture.CreateContext(fixture.Paths.DatabasePath))
        {
            context.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Administrador principal",
                NombreUsuario = ProtectedAdministrator.UserName,
                PasswordHash = "test-hash",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            context.SaveChanges();
        }

        MachineWideDatabaseResetJournalStore.Save(
            fixture.Paths,
            new MachineWideDatabaseResetJournal(
                MachineWideDatabaseResetJournalStore.CurrentSchemaVersion,
                operationId,
                installationId,
                InstallationMode.Principal,
                MachineWideDatabaseResetPhase.Committed,
                DateTimeOffset.UtcNow,
                Path.GetFileName(backup.FilePath!),
                backup.Sha256!),
            allowSyntheticRoots: true);

        var reset = fixture.CreateResetService();
        File.WriteAllText(backup.FilePath!, "tampered backup");
        Assert.Throws<InvalidDataException>(() => reset.RecoverPendingResetSynthetic(fixture.Paths));
        Assert.True(File.Exists(stagedDatabase));
        Assert.True(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));

        File.WriteAllText(backup.FilePath!, "synthetic verified backup package");
        reset.RecoverPendingResetSynthetic(fixture.Paths);

        Assert.False(File.Exists(stagedDatabase));
        Assert.False(MachineWideDatabaseResetJournalStore.HasPendingSynthetic(fixture.Paths));
        using var newContext = fixture.CreateContext(fixture.Paths.DatabasePath, readOnly: true);
        Assert.Contains(newContext.Usuarios, user => user.NombreUsuario == ProtectedAdministrator.UserName);
        Assert.DoesNotContain(newContext.Usuarios, user => user.NombreUsuario == "empleado-anterior");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class ResetFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-db-reset-test-{Guid.NewGuid():N}");

        public ResetFixture()
        {
            Paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(_root, "Program Files", "ArteInventariado"),
                Path.Combine(_root, "ProgramData", "ArteInventariado"),
                Path.Combine(_root, "LocalAppData", "ArteInventariado"));
        }

        public ArteInventariadoPathProvider Paths { get; }

        public Guid InitializePrincipalWithOldEmployee()
        {
            MachineWideDatabaseInitializer.InitializeSyntheticAsync(Paths).GetAwaiter().GetResult();
            new LocalStationConfigurationStore(Paths).SaveConfigured(InstallationMode.Principal);
            using var context = CreateContext(Paths.DatabasePath);
            context.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Empleado anterior",
                NombreUsuario = "empleado-anterior",
                PasswordHash = "old-hash",
                Rol = RolUsuario.EMPLEADO,
                Activo = true
            });
            context.SaveChanges();
            return new LocalStationConfigurationStore(Paths).Load().Status.InstallationId!.Value;
        }

        public MachineWideBusinessDatabaseResetService CreateResetService(
            Func<ArteInventariadoPathProvider, Guid, CancellationToken, MachineWideBackupRunResult>? createBackup = null,
            Func<ArteInventariadoPathProvider, Guid, int>? provisionPrincipal = null)
        {
            return new MachineWideBusinessDatabaseResetService(
                createBackup ?? ((_, operationId, _) => CreateHealthyBackup(operationId)),
                provisionPrincipal ?? ((_, _) => 0),
                () => NoOpLease.Instance,
                () => true,
                allowSyntheticRoots: true);
        }

        public MachineWideBackupRunResult CreateHealthyBackup(Guid? requestedOperationId = null)
        {
            var station = new LocalStationConfigurationStore(Paths).Load().Status;
            var backupId = Guid.NewGuid();
            var backupPath = Path.Combine(
                Paths.ProtectedBackupsRoot,
                $"test-{backupId:N}.aibak");
            Directory.CreateDirectory(Paths.ProtectedBackupsRoot);
            File.WriteAllText(backupPath, "synthetic verified backup package");
            var sha = HashFile(backupPath);
            var manifest = new BackupManifest(
                1,
                backupId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.Now,
                "test",
                "SQLite",
                [],
                null,
                "synthetic-db-hash",
                1,
                station.InstallationId!.Value,
                station.Mode!.Value,
                BackupKind.Manual,
                [],
                BackupContentStatus.Healthy,
                0);
            return new MachineWideBackupRunResult(
                MachineWideBackupRunOutcome.Succeeded,
                backupPath,
                sha,
                manifest,
                null,
                "Respaldo sintético válido para prueba unitaria.",
                []);
        }

        public MachineWideBackupRunResult CreateActualBackup(
            ArteInventariadoPathProvider paths,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            var policyStore = new MachineWideBackupPolicyStore();
            policyStore.EnsureInitialized(paths);
            var runner = new MachineWideBackupRunner(
                paths,
                new BackupPackageEngine(),
                policyStore,
                TimeProvider.System,
                () => true,
                _ => new TestBackupLock(),
                allowSyntheticRoots: true);
            return runner.RunAsync(BackupKind.Manual, operationId, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }

        public AppDbContext CreateContext(string databasePath, bool readOnly = false)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
            return new AppDbContext(optionsBuilder.Options);
        }

        public PrincipalProvisioningService CreatePrincipalProvisioningService(Guid? resetOperationId)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                    ForeignKeys = true
                }.ToString())
                .Options;
            var factory = new TestDbContextFactory(options);
            return new PrincipalProvisioningService(
                factory,
                Paths,
                new InstallationContext(new LocalStationConfigurationStore(Paths)),
                new NoOpAuditService(),
                resetOperationId,
                () => true,
                allowSyntheticResetAuthorization: true);
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
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
            : IDbContextFactory<AppDbContext>
        {
            public AppDbContext CreateDbContext() => new(options);

            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(CreateDbContext());
        }

        private sealed class TestBackupLock : IMachineWideBackupLock
        {
            public bool TryAcquire() => true;

            public void Dispose()
            {
            }
        }
    }

    private sealed class NoOpLease : IDisposable
    {
        public static NoOpLease Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
