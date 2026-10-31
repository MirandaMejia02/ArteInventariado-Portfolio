using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Synthetic end-to-end smoke for H1. It creates isolated machine-wide-like
/// roots below the OS temp directory and exercises A -> candidate B ->
/// PRE_RESTORE(A) -> B -> simulated failure -> rollback(A). It is never used
/// by normal application startup unless explicitly requested by an operator.
/// </summary>
public static class ProtectedRestoreSmokeHarness
{
    public static int Run(string? logPath = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.ProtectedRestoreSmoke.{Guid.NewGuid():N}");
        logPath = string.IsNullOrWhiteSpace(logPath)
            ? Path.Combine(Path.GetTempPath(), "arteinventariado-protected-restore-smoke.log")
            : Path.GetFullPath(logPath);
        var lines = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };
        try
        {
            RunAsync(root, lines).GetAwaiter().GetResult();
            lines.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            lines.Add("RESULT=FAILED");
            lines.Add($"ERROR={ex.GetType().Name}");
            return 1;
        }
        finally
        {
            lines.Add($"END={DateTimeOffset.UtcNow:O}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllLines(logPath, lines);
            }
            catch
            {
                // A smoke log is diagnostic only; never mask the smoke code.
            }

            SqliteConnection.ClearAllPools();
            TryDelete(root);
        }
    }

    private static async Task RunAsync(string root, ICollection<string> lines)
    {
        var paths = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(root, "install"),
            Path.Combine(root, "data"),
            Path.Combine(root, "user"));
        foreach (var directory in new[]
        {
            paths.MachineInstallRoot,
            paths.DataRoot,
            paths.ProductBusinessAssetsRoot,
            paths.AssetsRoot,
            paths.ProtectedBackupsRoot,
            paths.RecoveryRoot
        })
        {
            Directory.CreateDirectory(directory);
        }

        var station = new LocalStationConfigurationStore(paths);
        station.EnsureInitialized();
        station.SaveConfigured(InstallationMode.Principal);
        var installationId = station.Load().Status.InstallationId
            ?? throw new InvalidDataException("El smoke no obtuvo installationId.");

        var factory = new SmokeDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await DatabaseInitializer.InicializarAsync(context);
            var category = await context.Categorias.FirstAsync();
            context.Productos.Add(new Producto
            {
                CodigoInterno = "H1-SMOKE-001",
                Nombre = "A",
                CategoriaId = category.CategoriaId,
                ImagenLocalPath = Path.Combine(paths.ProductBusinessAssetsRoot, "smoke.png"),
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 250,
                StockActual = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var image = Path.Combine(paths.ProductBusinessAssetsRoot, "smoke.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3, 4]);
        var engine = new BackupPackageEngine();
        var migrations = await ReadMigrationsAsync(factory);
        var candidate = await CreatePackageAsync(paths, engine, migrations, installationId, "B");

        await SetNameAsync(factory, "A");
        var restore = CreateEngine(paths, engine);
        var restored = await restore.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                candidate.Manifest.BackupId, candidate.Manifest.SourceInstallationId,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Require(restored.Outcome == ProtectedRestoreOutcome.Succeeded, "No se pudo restaurar B.");
        Require(await ReadNameAsync(factory) == "B", "El smoke no observo B tras restore.");
        Require(station.Load().Status.InstallationId == installationId, "El restore cambio la identidad.");

        await SetNameAsync(factory, "A");
        var failing = new ProtectedRestoreEngine(
            paths,
            engine,
            TimeProvider.System,
            () => true,
            () => new SmokeRestoreLock(),
            phase =>
            {
                if (phase == "validate-production") throw new InvalidDataException("smoke failure");
            });
        var rolled = await failing.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Require(rolled.Outcome == ProtectedRestoreOutcome.Failed && rolled.RolledBack, "El rollback A no se completo.");
        Require(await ReadNameAsync(factory) == "A", "El smoke no recupero A.");
        Require(station.Load().Status.InstallationId == installationId, "El rollback cambio la identidad.");
        lines.Add("sequence=A->B->PRE_RESTORE(A)->B->failure->rollback(A)");
        lines.Add("stationIdentity=preserved");
    }

    private static ProtectedRestoreEngine CreateEngine(
        ArteInventariadoPathProvider paths,
        BackupPackageEngine engine) =>
        new(paths, engine, TimeProvider.System, () => true, () => new SmokeRestoreLock());

    private static async Task<(string Path, BackupManifest Manifest)> CreatePackageAsync(
        ArteInventariadoPathProvider paths,
        BackupPackageEngine engine,
        IReadOnlyList<string> migrations,
        Guid installationId,
        string name)
    {
        await using (var context = new SmokeDbContextFactory(paths.DatabasePath).CreateDbContext())
        {
            var product = await context.Productos.SingleAsync();
            product.Nombre = name;
            await context.SaveChangesAsync();
        }

        var build = Path.Combine(paths.RecoveryRoot, $"smoke-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(build);
        var snapshot = Path.Combine(build, BackupPackageEngine.DatabaseEntryName);
        await engine.CreateSnapshotAsync(paths.DatabasePath, snapshot, CancellationToken.None);
        var products = await engine.ReadProductAssetsAsync(snapshot, CancellationToken.None);
        var managed = await engine.ReadManagedAssetSourcesAsync(snapshot, CancellationToken.None);
        var preparation = await new MachineWideBackupAssetTrustBoundary(
            paths.ProductBusinessAssetsRoot,
            paths.AssetsRoot).PrepareAsync(products, managed, build, CancellationToken.None);
        var applied = await engine.ReadAppliedMigrationsAsync(snapshot, CancellationToken.None);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var manifest = new BackupManifest(
            BackupPackageEngine.CurrentFormatVersion,
            id,
            now,
            now.ToLocalTime(),
            "1.12.0-smoke",
            "SQLite",
            applied,
            applied.LastOrDefault(),
            await BackupPackageEngine.ComputeSha256Async(snapshot, CancellationToken.None),
            new FileInfo(snapshot).Length,
            installationId,
            InstallationMode.Principal,
            BackupKind.Manual,
            preparation.Assets,
            preparation.ContentStatus,
            preparation.OmittedAssetCount);
        var package = Path.Combine(paths.ProtectedBackupsRoot, $"smoke-{id:N}.aibak");
        await engine.CreatePackageAsync(build, snapshot, manifest, package, CancellationToken.None);
        Directory.Delete(build, recursive: true);
        return (package, manifest);
    }

    private static async Task<IReadOnlyList<string>> ReadMigrationsAsync(SmokeDbContextFactory factory)
    {
        await using var context = factory.CreateDbContext();
        return context.Database.GetMigrations().ToList();
    }

    private static async Task SetNameAsync(SmokeDbContextFactory factory, string name)
    {
        await using var context = factory.CreateDbContext();
        var product = await context.Productos.SingleAsync();
        product.Nombre = name;
        await context.SaveChangesAsync();
    }

    private static async Task<string> ReadNameAsync(SmokeDbContextFactory factory)
    {
        await using var context = factory.CreateDbContext();
        return await context.Productos.Select(product => product.Nombre).SingleAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class SmokeRestoreLock : IProtectedRestoreLock
    {
        public bool TryAcquire() => true;
        public void Dispose() { }
    }

    private sealed class SmokeDbContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString())
            .Options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
