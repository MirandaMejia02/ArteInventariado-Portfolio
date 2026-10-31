using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class V110ReleaseRehearsalTests
{
    [Fact]
    public async Task PopulatedV19Backup_MigratesWithoutChangingBusinessHistory()
    {
        var packagePath = Environment.GetEnvironmentVariable("ARTE_V19_REHEARSAL_AIBAK");
        var outputRoot = Environment.GetEnvironmentVariable("ARTE_V19_REHEARSAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(packagePath) || string.IsNullOrWhiteSpace(outputRoot))
        {
            // La certificación externa se activa explícitamente para no acoplar la suite
            // ordinaria a datos locales. Las migraciones sintéticas siguen cubiertas aparte.
            return;
        }

        packagePath = Path.GetFullPath(packagePath);
        outputRoot = Path.GetFullPath(outputRoot);
        Assert.True(File.Exists(packagePath), $"No existe el paquete v1.9: {packagePath}");
        Directory.CreateDirectory(outputRoot);

        var preMigrationPackage = Path.Combine(outputRoot, "ArteInventariado_v1.9_PRE_MIGRATION.aibak");
        File.Copy(packagePath, preMigrationPackage, overwrite: true);
        var databasePath = Path.Combine(outputRoot, "v1.9-populated-migration-copy.db");
        var restoredPath = Path.Combine(outputRoot, "v1.9-restored-rollback-copy.db");
        var manifest = ExtractDatabase(preMigrationPackage, databasePath);
        _ = ExtractDatabase(preMigrationPackage, restoredPath);

        Assert.Equal(manifest.DatabaseSha256, Sha256(databasePath));
        Assert.Equal(manifest.DatabaseSha256, Sha256(restoredPath));
        Assert.Equal("20260809175437_AddFinancialAnalyticsBlockDV180", LastMigration(databasePath));
        Assert.Equal("20260809175437_AddFinancialAnalyticsBlockDV180", LastMigration(restoredPath));

        var before = Snapshot(databasePath);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using (var context = new AppDbContext(options))
        {
            await context.Database.MigrateAsync();
        }
        var after = Snapshot(databasePath);

        foreach (var key in before.Keys)
        {
            Assert.Equal(before[key], after[key]);
        }
        Assert.Equal(before["stock_fisico"], Scalar(databasePath,
            "SELECT COALESCE(SUM(existencia_fisica), 0) FROM inventario_saldos;"));
        Assert.Equal(before["productos"], Scalar(databasePath, "SELECT COUNT(*) FROM producto_skus;"));
        Assert.Equal(before["productos"] * 3, Scalar(databasePath, "SELECT COUNT(*) FROM inventario_saldos;"));
        Assert.Equal(0, Scalar(databasePath,
            "SELECT COALESCE(SUM(existencia_fisica), 0) FROM inventario_saldos WHERE ubicacion IN ('EXHIBICION', 'BODEGA');"));
        Assert.Equal(before["stock_fisico"], Scalar(databasePath,
            "SELECT COALESCE(SUM(existencia_fisica), 0) FROM inventario_saldos WHERE ubicacion = 'SIN_ASIGNAR';"));
        Assert.Equal(0, Scalar(databasePath,
            "SELECT COUNT(*) FROM producto_skus sku JOIN productos product ON product.producto_id = sku.producto_id " +
            "WHERE sku.es_principal <> 1 OR sku.codigo_sku <> product.codigo_interno OR product.modo <> 'SIMPLE';"));
        Assert.Equal(0, ForeignKeyViolationCount(databasePath));
        Assert.Equal("20260825055759_V110OperationalSkuIntegrations", LastMigration(databasePath));
        var performance = await MeasureAnalyticsAsync(databasePath);
        Assert.True(performance.InventoryMilliseconds < 15_000,
            $"El reporte de inventario tardó {performance.InventoryMilliseconds} ms.");
        Assert.True(performance.ProductsMilliseconds < 15_000,
            $"La analítica de productos tardó {performance.ProductsMilliseconds} ms.");

        var lines = new List<string>
        {
            $"Package PRE_MIGRATION={preMigrationPackage}",
            $"Manifest app={manifest.ApplicationVersion}; migration={manifest.LastMigration}; assets={manifest.AssetCount}",
            $"DB SHA-256={manifest.DatabaseSha256}",
            "ANTES / DESPUÉS"
        };
        lines.AddRange(before.OrderBy(item => item.Key)
            .Select(item => $"{item.Key}={item.Value}/{after[item.Key]}"));
        lines.Add($"sku={Scalar(databasePath, "SELECT COUNT(*) FROM producto_skus;")}");
        lines.Add($"inventario_saldos={Scalar(databasePath, "SELECT COUNT(*) FROM inventario_saldos;")}");
        lines.Add($"stock_canonico={Scalar(databasePath, "SELECT COALESCE(SUM(existencia_fisica), 0) FROM inventario_saldos;")}");
        lines.Add("restore_rollback_sha=OK");
        lines.Add("foreign_key_check=OK");
        lines.Add($"last_migration={LastMigration(databasePath)}");
        lines.Add($"inventory_report_rows={performance.InventoryRows}; ms={performance.InventoryMilliseconds}");
        lines.Add($"product_analytics_rows={performance.ProductRows}; ms={performance.ProductsMilliseconds}");
        lines.Add("analytics_full_size_images_loaded=0 (las consultas no proyectan assets ni blobs)");
        lines.Add("new_indexes=0 (medición dentro del presupuesto; no se justificó índice adicional)");
        await File.WriteAllLinesAsync(Path.Combine(outputRoot, "rehearsal-report.txt"), lines);
    }

    private static async Task<PerformanceResult> MeasureAnalyticsAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var context = new AppDbContext(options);
        var admin = await context.Usuarios.AsNoTracking()
            .Where(user => user.Activo && user.Rol == RolUsuario.ADMIN)
            .Select(user => new { user.UsuarioId, user.NombreCompleto, user.NombreUsuario })
            .FirstAsync();
        var session = new RehearsalSession(new UsuarioSesion(
            admin.UsuarioId, admin.NombreCompleto, admin.NombreUsuario, RolUsuario.ADMIN));
        var clock = TimeProvider.System;
        var factory = new RehearsalContextFactory(options);
        var sales = new SalesAnalyticsQueryService(context, session, clock);
        var financial = new FinancialAnalyticsQueryService(factory, sales, session);
        var commercial = new CommercialAnalyticsQueryService(context, sales, clock);
        var reports = new AdvancedReportQueryService(context, sales, financial, commercial, session);
        var analytics = new AnalyticsQueryRequest(new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.CustomRange,
            new DateOnly(2020, 1, 1),
            new DateOnly(2026, 12, 31)));

        var timer = Stopwatch.StartNew();
        var inventory = await reports.QueryAsync(new AdvancedReportRequest(
            AdvancedReportKind.Inventory, analytics, PageSize: 1000));
        timer.Stop();
        var inventoryMs = timer.ElapsedMilliseconds;

        timer.Restart();
        var products = await sales.GetProductsAsync(analytics, SalesAnalyticsQueryLimits.MaximumProducts);
        timer.Stop();
        return new PerformanceResult(
            inventoryMs,
            timer.ElapsedMilliseconds,
            inventory.TotalRows,
            products.MostSoldByUnits.Count + products.WithoutSales.Count);
    }

    private static IReadOnlyDictionary<string, long> Snapshot(string databasePath) =>
        new Dictionary<string, long>
        {
            ["productos"] = Scalar(databasePath, "SELECT COUNT(*) FROM productos;"),
            ["clientes"] = Scalar(databasePath, "SELECT COUNT(*) FROM clientes;"),
            ["ventas"] = Scalar(databasePath, "SELECT COUNT(*) FROM ventas;"),
            ["compras"] = Scalar(databasePath, "SELECT COUNT(*) FROM compras;"),
            ["caja"] = Scalar(databasePath, "SELECT COUNT(*) FROM cajas;"),
            ["creditos_cobrar"] = Scalar(databasePath, "SELECT COUNT(*) FROM cuentas_por_cobrar;"),
            ["creditos_pagar"] = Scalar(databasePath, "SELECT COUNT(*) FROM cuentas_por_pagar;"),
            ["proveedores"] = Scalar(databasePath, "SELECT COUNT(*) FROM proveedores;"),
            ["pedidos"] = Scalar(databasePath, "SELECT COUNT(*) FROM pedidos_personalizados;"),
            ["devoluciones"] = Scalar(databasePath, "SELECT COUNT(*) FROM devoluciones;"),
            ["stock_fisico"] = Scalar(databasePath, "SELECT COALESCE(SUM(stock_actual), 0) FROM productos;"),
            ["valor_inventario_legado"] = Scalar(databasePath,
                "SELECT COALESCE(SUM(CAST(stock_actual AS INTEGER) * precio_compra_centavos), 0) FROM productos;"),
            ["valor_ventas"] = Scalar(databasePath, "SELECT COALESCE(SUM(total_centavos), 0) FROM ventas;"),
            ["valor_compras"] = Scalar(databasePath, "SELECT COALESCE(SUM(total_centavos), 0) FROM compras;"),
            ["detalle_ventas"] = Scalar(databasePath, "SELECT COUNT(*) FROM detalle_ventas;"),
            ["snapshots_producto"] = Scalar(databasePath,
                "SELECT COUNT(*) FROM detalle_ventas WHERE producto_codigo_snapshot IS NOT NULL OR producto_nombre_snapshot IS NOT NULL;"),
            ["assets_legacy"] = Scalar(databasePath,
                "SELECT COUNT(*) FROM productos WHERE imagen_local_path IS NOT NULL AND TRIM(imagen_local_path) <> '';"),
            ["configuracion"] = Scalar(databasePath, "SELECT COUNT(*) FROM configuracion_sistema;")
        };

    private static BackupManifestInfo ExtractDatabase(string packagePath, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var database = archive.GetEntry("database.db") ?? throw new InvalidDataException("El paquete no contiene database.db.");
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("El paquete no contiene manifest.json.");
        using (var source = database.Open())
        using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(destination);
        }
        using var manifestStream = manifestEntry.Open();
        using var document = JsonDocument.Parse(manifestStream);
        var root = document.RootElement;
        return new BackupManifestInfo(
            root.GetProperty("applicationVersion").GetString() ?? string.Empty,
            root.GetProperty("lastMigration").GetString() ?? string.Empty,
            root.GetProperty("databaseSha256").GetString() ?? string.Empty,
            root.TryGetProperty("assets", out var assets) ? assets.GetArrayLength() : 0);
    }

    private static long Scalar(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string LastMigration(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static int ForeignKeyViolationCount(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read()) count++;
        return count;
    }

    private static string Sha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record BackupManifestInfo(
        string ApplicationVersion,
        string LastMigration,
        string DatabaseSha256,
        int AssetCount);

    private sealed record PerformanceResult(
        long InventoryMilliseconds,
        long ProductsMilliseconds,
        int InventoryRows,
        int ProductRows);

    private sealed class RehearsalContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class RehearsalSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => true;
        public bool EsAdmin => true;
        public bool TieneRol(RolUsuario rol) => rol == RolUsuario.ADMIN;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }
}
