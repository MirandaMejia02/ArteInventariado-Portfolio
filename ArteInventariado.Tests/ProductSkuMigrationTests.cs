using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ArteInventariado.Tests;

public sealed class ProductSkuMigrationTests
{
    private const string PreviousMigration = "20260809175437_AddFinancialAnalyticsBlockDV180";
    private const string ProductSkuMigration = "20260825004709_AddCanonicalProductSkuBlockAV110";

    [Fact]
    public async Task V19PopulatedDatabase_MigratesProductsOneToOneWithoutChangingOperationalHistory()
    {
        var root = CreateTemporaryRoot();
        var databasePath = Path.Combine(root, "arteinventariado-v19-populated.db");
        var backupPath = Path.Combine(root, "arteinventariado-v19-before-sku.db");

        try
        {
            await CreatePopulatedV19DatabaseAsync(databasePath);
            SqliteConnection.ClearAllPools();
            File.Copy(databasePath, backupPath);
            Assert.True(new FileInfo(backupPath).Length > 0);

            await using (var context = new AppDbContext(CreateOptions(databasePath)))
            {
                await context.Database.MigrateAsync();
            }

            await using var verification = new AppDbContext(CreateOptions(databasePath));
            var products = await verification.Productos
                .AsNoTracking()
                .Include(product => product.Skus)
                    .ThenInclude(sku => sku.SaldosInventario)
                .OrderBy(product => product.CodigoInterno)
                .ToListAsync();

            Assert.Equal(2, products.Count);
            Assert.All(products, product => Assert.Equal(ModoProducto.SIMPLE, product.Modo));
            Assert.All(products, product => Assert.Single(product.Skus));
            Assert.Equal(2, products.Sum(product => product.Skus.Count));
            Assert.All(products, product => Assert.Equal(product.CodigoInterno, product.Skus.Single().CodigoSku));
            Assert.Equal("000123", products[0].CodigoInterno);
            Assert.Equal(7, products.Single(product => product.CodigoInterno == "000123").StockActual);
            Assert.Equal(5, products.Single(product => product.CodigoInterno == "SKU-INACTIVO").StockActual);
            Assert.False(products.Single(product => product.CodigoInterno == "SKU-INACTIVO").Skus.Single().Activo);
            Assert.All(products, product => Assert.True(product.Skus.Single().EsPrincipal));
            Assert.All(products, product => Assert.Equal(3, product.Skus.Single().SaldosInventario.Count));
            Assert.All(products, product =>
            {
                var balances = product.Skus.Single().SaldosInventario;
                Assert.Equal(0, balances.Single(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION).ExistenciaFisica);
                Assert.Equal(0, balances.Single(balance => balance.Ubicacion == UbicacionInventario.BODEGA).ExistenciaFisica);
                Assert.Equal(product.StockActual, balances.Single(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR).ExistenciaFisica);
                Assert.Equal(product.StockActual, balances.Sum(balance => balance.ExistenciaFisica));
            });
            Assert.All(products.SelectMany(product => product.Skus), sku =>
            {
                Assert.Null(sku.PrecioVentaOverrideCentavos);
                Assert.Null(sku.CostoOverrideCentavos);
            });

            Assert.Equal("CLI-V19-001", await verification.Clientes.Select(item => item.CodigoCliente).SingleAsync());
            Assert.Equal("CMP-V19-001", await verification.Compras.Select(item => item.CodigoCompra).SingleAsync());
            Assert.Equal("VTA-V19-001", await verification.Ventas.Select(item => item.NumeroVenta).SingleAsync());
            Assert.Equal(1250, await verification.Cajas.Select(item => item.MontoInicialCentavos).SingleAsync());

            var historicalLine = await verification.DetalleVentas.AsNoTracking().SingleAsync();
            Assert.Equal("000123", historicalLine.ProductoCodigoSnapshot);
            Assert.Equal("Producto legado snapshot", historicalLine.ProductoNombreSnapshot);
            Assert.Equal("Categoria v1.9 snapshot", historicalLine.CategoriaNombreSnapshot);
            Assert.Equal(OrigenCostoHistorico.PRODUCTO_ACTUAL, historicalLine.OrigenCosto);
            Assert.Equal(250, historicalLine.CostoUnitarioCentavos);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedMigration_RollsBackModeAndLeavesOriginalProductDataIntact()
    {
        var root = CreateTemporaryRoot();
        var databasePath = Path.Combine(root, "arteinventariado-v19-rollback.db");

        try
        {
            await CreatePopulatedV19DatabaseAsync(databasePath);
            await ExecuteSqlAsync(databasePath, "CREATE TABLE producto_skus (sentinel INTEGER NOT NULL);");

            await using (var context = new AppDbContext(CreateOptions(databasePath)))
            {
                await Assert.ThrowsAnyAsync<Exception>(() => context.Database.MigrateAsync());
            }

            Assert.Equal(
                0,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM pragma_table_info('productos') WHERE name = 'modo';"));
            Assert.Equal(2, await ReadScalarIntAsync(databasePath, "SELECT COUNT(*) FROM productos;"));
            Assert.Equal(12, await ReadScalarIntAsync(databasePath, "SELECT SUM(stock_actual) FROM productos;"));
            Assert.Equal(
                0,
                await ReadScalarIntAsync(
                    databasePath,
                    $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{ProductSkuMigration}';"));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM pragma_table_info('producto_skus') WHERE name = 'sentinel';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreatePopulatedV19DatabaseAsync(string databasePath)
    {
        await using var context = new AppDbContext(CreateOptions(databasePath));
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var user = new Usuario
        {
            NombreCompleto = "Administrador v1.9",
            NombreUsuario = $"admin-v19-{Guid.NewGuid():N}",
            PasswordHash = "hash-v19",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        var category = new Categoria
        {
            Nombre = "Categoria v1.9",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        context.AddRange(user, category);
        await context.SaveChangesAsync();

        var customer = new Cliente
        {
            CodigoCliente = "CLI-V19-001",
            NombreCompleto = "Cliente v1.9",
            NombreNormalizado = "cliente v1.9",
            Estado = EstadoCliente.ACTIVO,
            UsuarioCreacionId = user.UsuarioId,
            FechaCreacion = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow
        };
        var supplier = new Proveedor
        {
            CodigoProveedor = "PRV-V19-001",
            Tipo = TipoProveedor.PROVEEDOR,
            NombreRazonSocial = "Proveedor v1.9",
            NombreNormalizado = "proveedor v1.9",
            Estado = EstadoProveedor.ACTIVO,
            UsuarioCreacionId = user.UsuarioId,
            FechaCreacion = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow
        };
        var cash = new Caja
        {
            FechaOperacion = new DateOnly(2026, 8, 20),
            FechaApertura = new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc),
            MontoInicialCentavos = 1250,
            MontoCierreCentavos = 1250,
            Estado = EstadoCaja.CERRADA,
            UsuarioAperturaId = user.UsuarioId,
            UsuarioCierreId = user.UsuarioId,
            FechaCierre = new DateTime(2026, 8, 20, 20, 0, 0, DateTimeKind.Utc)
        };
        context.AddRange(customer, supplier, cash);
        await context.SaveChangesAsync();

        var sale = new Venta
        {
            NumeroVenta = "VTA-V19-001",
            CajaId = cash.CajaId,
            UsuarioId = user.UsuarioId,
            UsuarioNombreSnapshot = user.NombreCompleto,
            ClienteId = customer.ClienteId,
            ClienteNombreSnapshot = customer.NombreCompleto,
            FechaVenta = new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc),
            SubtotalCentavos = 500,
            DescuentoCentavos = 0,
            TotalCentavos = 500,
            PagadoCentavos = 500,
            CambioCentavos = 0,
            Estado = EstadoVenta.COMPLETADA
        };
        var purchase = new Compra
        {
            CodigoCompra = "CMP-V19-001",
            ProveedorId = supplier.ProveedorId,
            UsuarioRegistroId = user.UsuarioId,
            FechaCompra = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
            FechaRegistro = new DateTime(2026, 8, 19, 12, 5, 0, DateTimeKind.Utc),
            EstadoRecepcion = EstadoRecepcionCompra.PENDIENTE,
            SubtotalCentavos = 300,
            DescuentoCentavos = 0,
            TotalCentavos = 300,
            EstadoFinanciero = EstadoFinancieroCompra.PENDIENTE,
            TotalPagadoCentavos = 0,
            SaldoPendienteCentavos = 300,
            FechaActualizacion = new DateTime(2026, 8, 19, 12, 5, 0, DateTimeKind.Utc)
        };
        context.AddRange(sale, purchase);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO productos (
                codigo_interno, nombre, descripcion, categoria_id, imagen_local_path,
                precio_compra_centavos, precio_venta_centavos, stock_actual, stock_minimo,
                estado, fecha_creacion, fecha_modificacion)
            VALUES (
                {"000123"}, {"Producto legado activo"}, NULL, {category.CategoriaId}, NULL,
                {250}, {500}, {7}, {2}, {"ACTIVO"}, {DateTime.UtcNow}, NULL);
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO productos (
                codigo_interno, nombre, descripcion, categoria_id, imagen_local_path,
                precio_compra_centavos, precio_venta_centavos, stock_actual, stock_minimo,
                estado, fecha_creacion, fecha_modificacion)
            VALUES (
                {"SKU-INACTIVO"}, {"Producto legado inactivo"}, NULL, {category.CategoriaId}, NULL,
                {100}, {200}, {5}, {1}, {"INACTIVO"}, {DateTime.UtcNow}, NULL);
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO detalle_ventas (
                venta_id, producto_id, cantidad, precio_unitario_centavos,
                costo_unitario_centavos, origen_costo, producto_codigo_snapshot,
                producto_nombre_snapshot, categoria_id_snapshot, categoria_nombre_snapshot,
                subtotal_centavos, descuento_centavos,
                descuento_general_proporcional_centavos, descuento_motivo, total_centavos)
            VALUES (
                {sale.VentaId},
                (SELECT producto_id FROM productos WHERE codigo_interno = '000123'),
                {1}, {500}, {250}, {"PRODUCTO_ACTUAL"}, {"000123"},
                {"Producto legado snapshot"}, {category.CategoriaId}, {"Categoria v1.9 snapshot"},
                {500}, {0}, {0}, NULL, {500});
            """);
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                Pooling = false
            }.ToString())
            .Options;

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(CreateOptionsConnectionString(databasePath));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadScalarIntAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(CreateOptionsConnectionString(databasePath));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string CreateOptionsConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ArteInventariado.ProductSkuMigration.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
