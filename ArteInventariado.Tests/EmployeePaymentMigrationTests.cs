using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class EmployeePaymentMigrationTests
{
    [Fact]
    public async Task V12Fixture_MigratesToB3WithoutLosingBusinessData()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"arteinventariado-b3-migration-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

            await using (var preB3 = new AppDbContext(options))
            {
                await preB3.Database.MigrateAsync(
                    "20260908020345_AddEmployeeLoginActivityB1");

                var principal = new Usuario
                {
                    UsuarioId = 701,
                    NombreCompleto = "Principal fixture",
                    NombreUsuario = "principal-b3-fixture",
                    PasswordHash = "fixture-hash",
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = Utc(2026, 1, 2)
                };
                var category = new Categoria
                {
                    CategoriaId = 702,
                    Nombre = "Categoría B3 fixture",
                    Activa = true,
                    FechaCreacion = Utc(2026, 1, 2)
                };
                var product = new Producto
                {
                    ProductoId = 703,
                    CodigoInterno = "B3-FIXTURE-PRODUCT",
                    Nombre = "Producto B3 fixture",
                    Categoria = category,
                    PrecioCompraCentavos = 100,
                    PrecioVentaCentavos = 250,
                    StockActual = 4,
                    StockMinimo = 1,
                    Estado = EstadoProducto.ACTIVO,
                    FechaCreacion = Utc(2026, 1, 2),
                    Modo = ModoProducto.SIMPLE
                };
                var sku = new ProductoSku
                {
                    ProductoSkuId = 704,
                    Producto = product,
                    CodigoSku = "B3-FIXTURE-SKU",
                    EsPrincipal = true,
                    Activo = true
                };
                var balance = new InventarioSaldo
                {
                    InventarioSaldoId = 705,
                    ProductoSku = sku,
                    Ubicacion = UbicacionInventario.BODEGA,
                    ExistenciaFisica = 4,
                    Reservado = 0,
                    Version = 1
                };
                var cashRegister = new Caja
                {
                    CajaId = 706,
                    FechaOperacion = new DateOnly(2026, 1, 2),
                    FechaApertura = Utc(2026, 1, 2),
                    MontoInicialCentavos = 0,
                    MontoCierreCentavos = 0,
                    Estado = EstadoCaja.ABIERTA,
                    UsuarioApertura = principal
                };
                var sale = new Venta
                {
                    VentaId = 707,
                    NumeroVenta = "B3-FIXTURE-SALE",
                    Caja = cashRegister,
                    Usuario = principal,
                    UsuarioNombreSnapshot = principal.NombreCompleto,
                    ClienteNombreSnapshot = "Consumidor final",
                    FechaVenta = Utc(2026, 1, 2),
                    SubtotalCentavos = 250,
                    DescuentoCentavos = 0,
                    TotalCentavos = 250,
                    PagadoCentavos = 250,
                    CambioCentavos = 0,
                    Estado = EstadoVenta.COMPLETADA
                };
                var configuration = new ConfiguracionSistema
                {
                    ConfiguracionSistemaId = 708,
                    Clave = "b3-fixture-config",
                    Valor = "preserved",
                    Descripcion = "Configuración sintética pre-B3",
                    FechaModificacion = Utc(2026, 1, 2)
                };

                preB3.AddRange(
                    principal,
                    category,
                    cashRegister,
                    sale,
                    configuration);
                await preB3.SaveChangesAsync();

                // This fixture intentionally stops at the pre-subcategory schema.
                // Seed product/SKU/inventory rows with SQL so the current model's
                // new nullable column is not referenced before migration 1A runs.
                await preB3.Database.ExecuteSqlInterpolatedAsync($"""
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
                await preB3.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO producto_skus (
                        producto_sku_id, producto_id, codigo_sku, nombre_variante,
                        activo, es_principal, minimo_exhibicion_override,
                        precio_venta_override_centavos, costo_override_centavos)
                    VALUES (
                        {sku.ProductoSkuId}, {product.ProductoId}, {sku.CodigoSku}, NULL,
                        {sku.Activo}, {sku.EsPrincipal}, NULL, NULL, NULL);
                    """);
                await preB3.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO inventario_saldos (
                        inventario_saldo_id, producto_sku_id, ubicacion,
                        existencia_fisica, reservado, version)
                    VALUES (
                        {balance.InventarioSaldoId}, {sku.ProductoSkuId}, {balance.Ubicacion.ToString()},
                        {balance.ExistenciaFisica}, {balance.Reservado}, {balance.Version});
                    """);
            }

            await using (var migrated = new AppDbContext(options))
            {
                await migrated.Database.MigrateAsync();

                Assert.Equal(1, await migrated.Usuarios.CountAsync(u => u.UsuarioId == 701));
                Assert.Equal(1, await migrated.Ventas.CountAsync(v => v.VentaId == 707));
                Assert.Equal(1, await migrated.Productos.CountAsync(p => p.ProductoId == 703));
                Assert.Equal(1, await migrated.InventarioSaldos.CountAsync(i => i.InventarioSaldoId == 705));
                Assert.Equal("preserved", await migrated.ConfiguracionSistema
                    .Where(c => c.ConfiguracionSistemaId == 708)
                    .Select(c => c.Valor)
                    .SingleAsync());
                Assert.Equal("B3-FIXTURE-SKU", await migrated.ProductoSkus
                    .Where(s => s.ProductoSkuId == 704)
                    .Select(s => s.CodigoSku)
                    .SingleAsync());

                var applied = await migrated.Database.GetAppliedMigrationsAsync();
                Assert.Contains("20260908020345_AddEmployeeLoginActivityB1", applied);
                Assert.Contains("20260908211647_AddEmployeePaymentsB3", applied);
                Assert.Equal(0, await migrated.EmployeePayments.CountAsync());
                Assert.Equal("ok", await ScalarAsync(migrated, "PRAGMA integrity_check;"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
            if (File.Exists(databasePath + "-wal"))
                File.Delete(databasePath + "-wal");
            if (File.Exists(databasePath + "-shm"))
                File.Delete(databasePath + "-shm");
        }
    }

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<string?> ScalarAsync(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }
}
