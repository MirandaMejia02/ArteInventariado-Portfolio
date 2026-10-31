using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Deterministic H2 smoke. It uses synthetic machine-wide roots and in-memory
/// cloud/restore delegates; it never contacts AWS and never touches a real
/// installation. The orchestration, FreshMachine boundary, destination
/// identity and post-recovery database are still exercised end-to-end.
/// </summary>
public static class DisasterRecoverySmokeHarness
{
    public static int Run(string? outputPath = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-h2-smoke-{Guid.NewGuid():N}");
        var log = new List<string>();
        try
        {
            var paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"),
                Path.Combine(root, "user"));
            Directory.CreateDirectory(paths.MachineInstallRoot);
            Directory.CreateDirectory(paths.MachineDataRoot);
            Directory.CreateDirectory(paths.UserDataRoot);

            var sourceInstallationId = Guid.NewGuid();
            var backupId = Guid.NewGuid();
            var before = new InstallationStateClassifier(
                paths,
                legacyPaths: ArteInventariadoPathProvider.CreateLegacy(Path.Combine(root, "legacy")),
                migrationsProvider: ReadMigrations).Classify();
            Require(before.State == InstallationLifecycleState.FreshMachine,
                $"El clasificador inicial no quedó en FreshMachine: {before.State}; station={before.StationFileExists}, db={before.DatabaseFileExists}, current={before.CurrentPointerValid}, update={before.UpdateStatePresent}, marker={before.BootstrapMarkerPresent}, legacy={before.LegacyInstallationDetected}, problem={before.Problem}.");

            var candidateDirectory = Path.Combine(paths.CloudRecoveryStagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(candidateDirectory);
            var candidatePath = Path.Combine(candidateDirectory, "package.aibak");
            var candidatePackage = CreateSourcePackage(root, candidatePath, sourceInstallationId, backupId);
            var candidate = candidatePackage.Candidate;
            var selection = new CloudRecoveryCatalogItem(
                sourceInstallationId,
                backupId,
                candidate.Key,
                candidate.VersionId,
                true,
                candidate.CreatedUtc,
                candidate.SizeBytes,
                BackupContentStatus.Healthy,
                0,
                candidate.PackageSha256,
                null,
                null,
                true);
            var configuration = new CloudBackupConfiguration(
                CloudBackupConfigurationStore.CurrentSchemaVersion,
                true,
                "portfolio-example-backups",
                "us-east-1",
                "v1",
                MultipartThresholdBytes: 5L * 1024 * 1024,
                MultipartPartSizeBytes: 5L * 1024 * 1024);
            var authentication = new CloudRecoveryAuthenticationRequest(
                "fake-access",
                "fake-secret",
                "arn:aws:iam::123456789012:role/fake",
                "arn:aws:iam::123456789012:mfa/fake",
                "123456",
                "us-east-1");
            var cloudRequest = new DisasterRecoveryCloudRequest(
                configuration,
                authentication,
                sourceInstallationId,
                OwnerConfirmed: true);

            var classifier = new InstallationStateClassifier(
                paths,
                legacyPaths: ArteInventariadoPathProvider.CreateLegacy(Path.Combine(root, "legacy")),
                migrationsProvider: ReadMigrations);
            var service = new DisasterRecoveryBootstrapService(
                paths,
                classifier,
                (request, _) => Task.FromResult(new CloudRecoveryCatalogResult(
                    [selection],
                    selection,
                    null,
                    [])),
                (request, selected, _) => Task.FromResult(candidate),
                TimeProvider.System,
                (restoreRequest, cancellationToken) => new ProtectedRestoreEngine(
                        paths,
                        new BackupPackageEngine(),
                        TimeProvider.System,
                        isElevated: () => true)
                    .ExecuteAsync(restoreRequest, cancellationToken, allowSyntheticRoots: true),
                isElevated: () => true,
                allowSyntheticRoots: true);

            var catalog = service.GetCatalogAsync(cloudRequest).GetAwaiter().GetResult();
            Require(ReferenceEquals(catalog.RecommendedHealthy, selection),
                "El catálogo no recomendó el backup Healthy.");
            var result = service.RecoverAsync(
                new DisasterRecoveryRequest(
                    cloudRequest,
                    selection,
                    InstallationMode.Principal,
                    ConfirmationAccepted: true)).GetAwaiter().GetResult();
            Require(result.Outcome == DisasterRecoveryOutcome.Succeeded,
                $"El recovery sintético terminó en {result.Outcome}: {result.Message}");
            Require(result.SourceInstallationId == sourceInstallationId, "El origen no se conservó.");
            Require(result.DestinationInstallationId.HasValue &&
                    result.DestinationInstallationId.Value != sourceInstallationId,
                "La identidad destino no fue nueva y distinta.");
            Require(!File.Exists(candidatePath), "El candidate temporal no fue limpiado.");

            var after = classifier.Classify();
            Require(after.State == InstallationLifecycleState.ExistingValidInstallation,
                $"La instalación recuperada no quedó coherente: {after.State}.");
            var stationAfter = new Station.LocalStationConfigurationStore(paths).Load().Status;
            Require(stationAfter.InstallationId == result.DestinationInstallationId &&
                    stationAfter.Mode == InstallationMode.Principal,
                "La identidad o el modo de estación no quedaron protegidos.");
            Require(File.Exists(paths.DatabasePath), "No existe la DB recuperada.");
            using (var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                       .UseSqlite($"Data Source={paths.DatabasePath};Mode=ReadOnly;Pooling=False")
                       .Options))
            {
                Require(verify.Usuarios.Any(user => user.NombreUsuario == "admin"),
                    "No reapareció el Administrador Principal original.");
            }

            log.Add("FreshMachine -> catálogo Healthy -> candidate -> H1 ProtectedRestoreEngine -> destino nuevo -> Principal original: OK");
            log.Add($"source={sourceInstallationId:D}; destination={result.DestinationInstallationId:D}; backup={backupId:D}");
            WriteLog(outputPath, log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add($"FAIL: {ex.GetType().Name}: {ex.Message}");
            WriteLog(outputPath, log);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static IReadOnlyList<string> ReadMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new AppDbContext(options);
        return context.Database.GetMigrations().ToArray();
    }

    private static SourcePackage CreateSourcePackage(
        string root,
        string packagePath,
        Guid sourceInstallationId,
        Guid backupId)
    {
        var sourceDirectory = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceDirectory);
        var sourceDatabase = Path.Combine(sourceDirectory, ArteInventariadoPathProvider.DatabaseFileName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = sourceDatabase,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString())
            .Options;
        using (var context = new AppDbContext(options))
        {
            context.Database.Migrate();
            context.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Administrador principal original",
                NombreUsuario = "admin",
                PasswordHash = "synthetic-hash",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            context.SaveChanges();
        }

        var engine = new BackupPackageEngine();
        var packageStage = Path.Combine(root, "source-package");
        Directory.CreateDirectory(packageStage);
        var snapshot = Path.Combine(packageStage, BackupPackageEngine.DatabaseEntryName);
        engine.CreateSnapshotAsync(sourceDatabase, snapshot, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var migrations = ReadMigrations();
        var created = DateTimeOffset.UtcNow;
        var databaseHash = BackupPackageEngine.ComputeSha256Async(snapshot, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var manifest = new BackupManifest(
            BackupPackageEngine.CurrentFormatVersion,
            backupId,
            created,
            created.ToLocalTime(),
            "1.12.0",
            "SQLite",
            migrations,
            migrations.LastOrDefault(),
            databaseHash,
            new FileInfo(snapshot).Length,
            sourceInstallationId,
            InstallationMode.Principal,
            BackupKind.Automatic,
            [],
            BackupContentStatus.Healthy,
            0);
        engine.CreatePackageAsync(
                packageStage,
                snapshot,
                manifest,
                packagePath,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var packageHash = BackupPackageEngine.ComputeSha256Async(packagePath, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return new SourcePackage(
            new CloudRecoveryCandidate(
                sourceInstallationId,
                backupId,
                packagePath,
                $"v1/{sourceInstallationId:D}/{backupId:D}.aibak",
                "version-1",
                created,
                BackupContentStatus.Healthy,
                0,
                new FileInfo(packagePath).Length,
                packageHash,
                null,
                null,
                []));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void WriteLog(string? outputPath, IReadOnlyList<string> entries)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        var path = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, entries);
    }

    private sealed record SourcePackage(CloudRecoveryCandidate Candidate);
}
