using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class QrBarcodeMigrationTests
{
    private const string BeforeQrMigration = "20260908211647_AddEmployeePaymentsB3";
    private const string QrMigration = "20260908225403_AddSkuBarcodeSymbologyQr";

    [Fact]
    public async Task ExistingCode128Data_MigratesToQrModelWithoutLossAndDefaultsToCode128()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"arteinventariado-qr-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

            await using (var beforeQr = new AppDbContext(options))
            {
                await beforeQr.Database.MigrateAsync(BeforeQrMigration);
                var user = new Usuario
                {
                    UsuarioId = 9301, NombreCompleto = "Usuario QR", NombreUsuario = "qr-migration-user",
                    PasswordHash = "fixture-hash", Rol = RolUsuario.ADMIN, Activo = true, FechaCreacion = DateTime.UtcNow
                };
                var category = new Categoria { CategoriaId = 9302, Nombre = "Categoría QR", Activa = true, FechaCreacion = DateTime.UtcNow };
                var product = new Producto
                {
                    ProductoId = 9303, CodigoInterno = "QR-MIGRATION-PRODUCT", Nombre = "Producto QR",
                    Categoria = category, PrecioCompraCentavos = 100, PrecioVentaCentavos = 200,
                    StockActual = 1, StockMinimo = 0, Estado = EstadoProducto.ACTIVO,
                    FechaCreacion = DateTime.UtcNow, Modo = ModoProducto.SIMPLE
                };
                var sku = new ProductoSku { ProductoSkuId = 9304, Producto = product, CodigoSku = "QR-MIGRATION-SKU", Activo = true, EsPrincipal = true };
                var configuration = new ConfiguracionSistema { ConfiguracionSistemaId = 9306, Clave = "qr-migration-config", Valor = "preserved", FechaModificacion = DateTime.UtcNow };
                beforeQr.AddRange(user, category, configuration);
                await beforeQr.SaveChangesAsync();
                await beforeQr.Database.ExecuteSqlInterpolatedAsync($"""
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
                await beforeQr.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO producto_skus (
                        producto_sku_id, producto_id, codigo_sku, nombre_variante, activo,
                        es_principal, minimo_exhibicion_override, precio_venta_override_centavos,
                        costo_override_centavos)
                    VALUES (
                        {sku.ProductoSkuId}, {product.ProductoId}, {sku.CodigoSku},
                        {sku.NombreVariante}, {sku.Activo}, {sku.EsPrincipal}, NULL, NULL, NULL);
                """);
                await beforeQr.Database.ExecuteSqlRawAsync(
                    "INSERT INTO producto_sku_barcodes (producto_sku_barcode_id, producto_sku_id, codigo, activo, es_principal) VALUES (9305, 9304, 'LEGACY-CODE128-9305', 1, 1);");
            }

            await using (var migrated = new AppDbContext(options))
            {
                await migrated.Database.MigrateAsync();

                var barcode = await migrated.ProductoSkuBarcodes.SingleAsync(item => item.ProductoSkuBarcodeId == 9305);
                Assert.Equal("LEGACY-CODE128-9305", barcode.Codigo);
                Assert.Equal(BarcodeSymbology.Code128, barcode.Symbology);
                Assert.Equal(9304, barcode.ProductoSkuId);
                Assert.Equal("preserved", await migrated.ConfiguracionSistema.Where(item => item.ConfiguracionSistemaId == 9306).Select(item => item.Valor).SingleAsync());
                Assert.Contains(QrMigration, await migrated.Database.GetAppliedMigrationsAsync());
                Assert.Equal("ok", await ScalarAsync(migrated, "PRAGMA integrity_check;"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task<string?> ScalarAsync(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }
}
