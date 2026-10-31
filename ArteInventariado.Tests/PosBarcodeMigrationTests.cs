using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ArteInventariado.Tests;

public sealed class PosBarcodeMigrationTests
{
    private const string PreviousMigration = "20260825020737_V110ProductAssets";
    private const string PosBarcodeMigration = "20260825043813_V110PosBarcodeScanner";
    private const string SubcategorySnapshotMigration = "20260912184247_AddSubcategorySnapshotsBlock1D2B";

    [Fact]
    public async Task ProductAssetsDatabase_MigratesLegacySaleWithoutInventingSkuHistoryAndCreatesBarcodeConstraints()
    {
        var root = CreateTemporaryRoot();
        var databasePath = Path.Combine(root, "arteinventariado-v110-product-assets.db");

        try
        {
            var seededSkuId = await CreateProductAssetsDatabaseWithLegacySaleAsync(databasePath);

            Assert.Equal(
                0,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM pragma_table_info('detalle_ventas') WHERE name = 'producto_sku_id';"));

            await using (var context = new AppDbContext(CreateOptions(databasePath)))
            {
                await context.GetService<IMigrator>().MigrateAsync(PosBarcodeMigration);
                await context.GetService<IMigrator>().MigrateAsync(SubcategorySnapshotMigration);
            }

            await using (var verification = new AppDbContext(CreateOptions(databasePath)))
            {
                var historicalLine = await verification.DetalleVentas
                    .AsNoTracking()
                    .Include(line => line.Venta)
                    .SingleAsync();

                Assert.Null(historicalLine.ProductoSkuId);
                Assert.Null(historicalLine.CodigoSkuSnapshot);
                Assert.Null(historicalLine.NombreVarianteSnapshot);
                Assert.Equal("PRD-PRE-BARCODE", historicalLine.ProductoCodigoSnapshot);
                Assert.Equal("Producto antes de barcode", historicalLine.ProductoNombreSnapshot);
                Assert.Equal("Categoria antes de barcode", historicalLine.CategoriaNombreSnapshot);
                Assert.Null(historicalLine.SubcategoriaIdSnapshot);
                Assert.Null(historicalLine.SubcategoriaNombreSnapshot);
                Assert.Equal(OrigenCostoHistorico.PRODUCTO_ACTUAL, historicalLine.OrigenCosto);
                Assert.Equal(275, historicalLine.CostoUnitarioCentavos);
                Assert.Equal("VTA-PRE-BARCODE-001", historicalLine.Venta.NumeroVenta);
            }

            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{PosBarcodeMigration}';"));
            Assert.Equal(
                3,
                await ReadScalarIntAsync(
                    databasePath,
                    """
                    SELECT COUNT(*)
                    FROM pragma_table_info('detalle_ventas')
                    WHERE name IN ('producto_sku_id', 'codigo_sku_snapshot', 'nombre_variante_snapshot');
                    """));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'producto_sku_barcodes';"));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    """
                    SELECT COUNT(*)
                    FROM pragma_index_list('producto_sku_barcodes')
                    WHERE name = 'IX_producto_sku_barcodes_codigo' AND "unique" = 1;
                    """));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    """
                    SELECT COUNT(*)
                    FROM pragma_foreign_key_list('producto_sku_barcodes')
                    WHERE "table" = 'producto_skus'
                      AND "from" = 'producto_sku_id'
                      AND "to" = 'producto_sku_id'
                      AND on_delete = 'CASCADE';
                    """));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    """
                    SELECT COUNT(*)
                    FROM pragma_foreign_key_list('detalle_ventas')
                    WHERE "table" = 'producto_skus'
                      AND "from" = 'producto_sku_id'
                      AND "to" = 'producto_sku_id'
                      AND on_delete = 'RESTRICT';
                    """));

            var barcodeTableSql = await ReadScalarStringAsync(
                databasePath,
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'producto_sku_barcodes';");
            Assert.Contains("CK_producto_sku_barcodes_codigo_valido", barcodeTableSql, StringComparison.Ordinal);
            Assert.Contains("CK_producto_sku_barcodes_principal_activo", barcodeTableSql, StringComparison.Ordinal);

            await ExecuteSqlAsync(
                databasePath,
                $"""
                INSERT INTO producto_sku_barcodes (producto_sku_id, codigo, activo, es_principal)
                VALUES ({seededSkuId}, '7501234567890', 1, 1);
                """);

            await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(
                databasePath,
                $"""
                INSERT INTO producto_sku_barcodes (producto_sku_id, codigo, activo, es_principal)
                VALUES ({seededSkuId}, '7501234567890', 1, 0);
                """));
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(
                databasePath,
                $"""
                INSERT INTO producto_sku_barcodes (producto_sku_id, codigo, activo, es_principal)
                VALUES ({seededSkuId}, ' CODIGO-CON-ESPACIOS ', 1, 0);
                """));
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(
                databasePath,
                """
                INSERT INTO producto_sku_barcodes (producto_sku_id, codigo, activo, es_principal)
                VALUES (999999, 'SKU-INEXISTENTE', 1, 0);
                """));
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(
                databasePath,
                "UPDATE detalle_ventas SET producto_sku_id = 999999;"));

            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM detalle_ventas WHERE producto_sku_id IS NULL;"));
            Assert.Equal(
                0,
                await ReadScalarIntAsync(databasePath, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<int> CreateProductAssetsDatabaseWithLegacySaleAsync(string databasePath)
    {
        await using var context = new AppDbContext(CreateOptions(databasePath));
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var user = new Usuario
        {
            NombreCompleto = "Administrador pre-barcode",
            NombreUsuario = $"admin-pre-barcode-{Guid.NewGuid():N}",
            PasswordHash = "hash-pre-barcode",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        var category = new Categoria
        {
            Nombre = "Categoria pre-barcode",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        context.AddRange(user, category);
        await context.SaveChangesAsync();

        var cash = new Caja
        {
            FechaOperacion = new DateOnly(2026, 8, 24),
            FechaApertura = new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc),
            MontoInicialCentavos = 2000,
            MontoCierreCentavos = 2550,
            Estado = EstadoCaja.CERRADA,
            UsuarioAperturaId = user.UsuarioId,
            UsuarioCierreId = user.UsuarioId,
            FechaCierre = new DateTime(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc)
        };
        var product = new Producto
        {
            CodigoInterno = "PRD-PRE-BARCODE",
            Modo = ModoProducto.SIMPLE,
            Nombre = "Producto pre-barcode",
            CategoriaId = category.CategoriaId,
            PrecioCompraCentavos = 275,
            PrecioVentaCentavos = 550,
            StockActual = 0,
            StockMinimo = 1,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        };
        var sku = new ProductoSku
        {
            Producto = product,
            CodigoSku = "PRD-PRE-BARCODE",
            NombreVariante = "Principal",
            Activo = true,
            EsPrincipal = true
        };
        context.Add(cash);
        await context.SaveChangesAsync();

        // This fixture intentionally stops before the subcategory migration.
        // Seed product/SKU rows with SQL so the current model does not request
        // productos.subcategoria_id before the migration under test is applied.
        product.ProductoId = 1;
        sku.ProductoSkuId = 1;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO productos (
                producto_id, codigo_interno, modo, nombre, descripcion, categoria_id,
                imagen_local_path, precio_compra_centavos, precio_venta_centavos,
                stock_actual, stock_minimo, estado, fecha_creacion, fecha_modificacion)
            VALUES (
                {product.ProductoId}, {product.CodigoInterno}, {product.Modo.ToString()},
                {product.Nombre}, NULL, {category.CategoriaId}, NULL,
                {product.PrecioCompraCentavos}, {product.PrecioVentaCentavos},
                {product.StockActual}, {product.StockMinimo}, {product.Estado.ToString()},
                {product.FechaCreacion}, NULL);
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO producto_skus (
                producto_sku_id, producto_id, codigo_sku, nombre_variante, activo,
                es_principal, minimo_exhibicion_override, precio_venta_override_centavos,
                costo_override_centavos)
            VALUES (
                {sku.ProductoSkuId}, {product.ProductoId}, {sku.CodigoSku},
                {sku.NombreVariante}, {sku.Activo}, {sku.EsPrincipal}, NULL, NULL, NULL);
            """);

        var sale = new Venta
        {
            NumeroVenta = "VTA-PRE-BARCODE-001",
            CajaId = cash.CajaId,
            UsuarioId = user.UsuarioId,
            UsuarioNombreSnapshot = user.NombreCompleto,
            ClienteNombreSnapshot = "Consumidor final pre-barcode",
            FechaVenta = new DateTime(2026, 8, 24, 15, 0, 0, DateTimeKind.Utc),
            SubtotalCentavos = 550,
            DescuentoCentavos = 0,
            TotalCentavos = 550,
            PagadoCentavos = 550,
            CambioCentavos = 0,
            Estado = EstadoVenta.COMPLETADA
        };
        context.Add(sale);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO detalle_ventas (
                venta_id, producto_id, cantidad, precio_unitario_centavos,
                costo_unitario_centavos, origen_costo, producto_codigo_snapshot,
                producto_nombre_snapshot, categoria_id_snapshot, categoria_nombre_snapshot,
                subtotal_centavos, descuento_centavos,
                descuento_general_proporcional_centavos, descuento_motivo, total_centavos)
            VALUES (
                {sale.VentaId}, {product.ProductoId}, {1}, {550},
                {275}, {"PRODUCTO_ACTUAL"}, {"PRD-PRE-BARCODE"},
                {"Producto antes de barcode"}, {category.CategoriaId}, {"Categoria antes de barcode"},
                {550}, {0}, {0}, NULL, {550});
            """);

        return sku.ProductoSkuId;
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(CreateConnectionString(databasePath))
            .Options;

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(CreateConnectionString(databasePath));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadScalarIntAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(CreateConnectionString(databasePath));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadScalarStringAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(CreateConnectionString(databasePath));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static string CreateConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ArteInventariado.PosBarcodeMigration.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
