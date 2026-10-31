using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Synthetic end-to-end smoke for the dedicated BackupRunner. It uses three
/// temporary roots, never touches the real machine-wide layout, and exercises
/// snapshot, package validation, atomic promotion and retention candidates.
/// </summary>
public static class MachineWideBackupSmokeHarness
{
    private const string SmokeRootEnvironmentVariable = "ARTE_BACKUP_SMOKE_ROOT";
    private const string KeepSmokeRootEnvironmentVariable = "ARTE_BACKUP_SMOKE_KEEP_ROOT";

    public static int Run()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(SmokeRootEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? CreateDefaultSmokeRoot()
            : Path.GetFullPath(configuredRoot);
        try
        {
            RunAsync(root).GetAwaiter().GetResult();
            Console.WriteLine("BackupRunner smoke: Healthy + DegradedAssets OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BackupRunner smoke: {ex}");
            return 1;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(KeepSmokeRootEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                TryDelete(root);
            }
        }
    }

    private static string CreateDefaultSmokeRoot()
    {
        var name = $"ArteInventariado-backup-runner-smoke-{Guid.NewGuid():N}";
        try
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            Directory.CreateDirectory(temp);
            return Path.Combine(temp, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A SYSTEM task may inherit an unusable interactive TEMP.  The
            // smoke fixture can fall back to a disposable profile-local
            // directory (SYSTEM resolves this to its own profile); production
            // --run never uses this path.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArteInventariado",
                "BackupRunnerSmoke",
                name);
        }
    }

    private static async Task RunAsync(string root)
    {
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
            Path.Combine(paths.ProtectedBackupsRoot, ".staging")
        })
        {
            Directory.CreateDirectory(directory);
        }
        // The published-process fixture opts into the machine-local SQLite
        // temp directory explicitly.  In-process unit tests intentionally
        // leave the process-wide SQLite setting untouched so parallel tests
        // cannot race over one another's temporary roots.
        var publishedRunnerProcess = string.Equals(
            Path.GetFileName(Environment.ProcessPath),
            "ArteInventariado.BackupRunner.exe",
            StringComparison.OrdinalIgnoreCase);
        if (publishedRunnerProcess ||
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(SmokeRootEnvironmentVariable)))
        {
            MachineWideSqliteRuntime.Configure(paths);
        }

        var station = new LocalStationConfigurationStore(paths);
        var pending = station.EnsureInitialized();
        var installationId = pending.Status.InstallationId
            ?? throw new InvalidDataException("El smoke no obtuvo installationId.");
        station.SaveConfigured(InstallationMode.Principal);

        var databaseOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString())
            .Options;
        await using (var context = new AppDbContext(databaseOptions))
        {
            await DatabaseInitializer.InicializarAsync(context);
            var category = await context.Categorias.FirstAsync();
            context.Productos.Add(new Producto
            {
                CodigoInterno = "BACKUP-SMOKE-001",
                Nombre = "Asset de smoke",
                Categoria = category,
                ImagenLocalPath = Path.Combine(paths.ProductBusinessAssetsRoot, "healthy.txt"),
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 250,
                StockActual = 1,
                StockMinimo = 0,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }
        Directory.CreateDirectory(paths.ProductBusinessAssetsRoot);
        await File.WriteAllTextAsync(
            Path.Combine(paths.ProductBusinessAssetsRoot, "healthy.txt"),
            "healthy-asset");

        var policyStore = new MachineWideBackupPolicyStore();
        var policy = policyStore.EnsureInitialized(paths);
        var now = DateTimeOffset.UtcNow;
        // Use a unique native mutex so the published smoke exercises the same
        // owner-thread lifecycle as production without colliding with a real
        // scheduled run or another test process.
        var smokeLockName = $"{MachineWideBackupRunner.LockName}.Smoke.{Guid.NewGuid():N}";
        var runner = new MachineWideBackupRunner(
            paths,
            new BackupPackageEngine(),
            policyStore,
            new FixedTimeProvider(now),
            () => true,
            _ => new NamedMachineWideBackupLock(smokeLockName),
            allowSyntheticRoots: true);

        var first = await runner.RunAsync();
        Require(first.Outcome == MachineWideBackupRunOutcome.Succeeded, "la primera generación no terminó correctamente");
        Require(first.FilePath is not null && File.Exists(first.FilePath), "no se promovió el primer .aibak");
        Require(first.Manifest?.ContentStatus == BackupContentStatus.Healthy,
            "la generación healthy no quedó marcada como Healthy");

        var externalAsset = Path.Combine(root, "external-secret.txt");
        await File.WriteAllTextAsync(externalAsset, "outside-asset-must-not-be-read");
        await using (var context = new AppDbContext(databaseOptions))
        {
            var product = await context.Productos.SingleAsync(product => product.CodigoInterno == "BACKUP-SMOKE-001");
            product.ImagenLocalPath = externalAsset;
            await context.SaveChangesAsync();
        }

        var corrupt = Path.Combine(paths.ProtectedBackupsRoot, "corrupt.aibak");
        File.Copy(first.FilePath!, corrupt);
        await using (var stream = new FileStream(corrupt, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            stream.SetLength(Math.Max(1, stream.Length / 2));
        }

        var second = await runner.RunAsync();
        Require(second.Outcome == MachineWideBackupRunOutcome.Succeeded, "la segunda generación no terminó correctamente");
        Require(second.FilePath is not null && File.Exists(second.FilePath), "no se promovió el segundo .aibak");
        Require(second.Manifest?.ContentStatus == BackupContentStatus.DegradedAssets,
            "la generación con asset externo no quedó marcada como DegradedAssets");
        Require(second.Manifest?.OmittedAssetCount == 1,
            "la generación degradada no registró el asset omitido");
        Require(second.Warnings.Any(warning => warning.Contains(nameof(BackupAssetIssueKind.OutsideCanonicalRoot), StringComparison.Ordinal)),
            "la generación degradada no informó la categoría del asset externo");
        Require(File.Exists(corrupt), "la retención eliminó un paquete corrupto no candidato");
        Require(Directory.GetFiles(paths.ProtectedBackupsRoot, "*.aibak").Length >= 2,
            "no quedó al menos un respaldo válido y el candidato corrupto");
        Require(policy.DestinationDirectory == paths.ProtectedBackupsRoot,
            "la política no apunta al destino protegido");

        var status = policyStore.LoadStatus(paths);
        Require(status.LastSuccessfulBackupUtc.HasValue, "no se registró el último respaldo exitoso");
        Require(status.LastSuccessfulFileName == Path.GetFileName(second.FilePath),
            "el estado no identifica la última generación");
        Require(status.LastSuccessfulSizeBytes > 0 && !string.IsNullOrWhiteSpace(status.LastSuccessfulSha256),
            "el estado no registró tamaño/hash");
        Require(status.LastOutcome == MachineWideBackupStatusKind.SuccessDegradedAssets,
            "el estado no distingue el respaldo degradado");
        Require(status.LastOmittedAssetCount == 1,
            "el estado no registró la cantidad de assets omitidos");

        using var package = System.IO.Compression.ZipFile.OpenRead(second.FilePath!);
        Require(package.Entries.All(entry =>
                !entry.FullName.EndsWith("external-secret.txt", StringComparison.OrdinalIgnoreCase)),
            "el paquete incluyó el asset externo no autorizado");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

}
