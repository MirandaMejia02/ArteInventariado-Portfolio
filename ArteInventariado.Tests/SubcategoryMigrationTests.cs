using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ArteInventariado.Tests;

public sealed class SubcategoryMigrationTests
{
    private const string PreviousMigration = "20260908225403_AddSkuBarcodeSymbologyQr";
    private const string SubcategoryMigration = "20260912045938_AddSubcategoriesBlock1A";

    [Fact]
    public async Task V1134Database_MigratesWithoutChangingProductsCategoriesSkusOrInventory()
    {
        var root = CreateTemporaryRoot();
        var databasePath = Path.Combine(root, "arteinventariado-v1134-subcategories.db");

        try
        {
            await CreateV1134DatabaseAsync(databasePath);

            await using (var context = new AppDbContext(CreateOptions(databasePath)))
            {
                await context.GetService<IMigrator>().MigrateAsync();
            }

            await using var verification = new AppDbContext(CreateOptions(databasePath));
            var product = await verification.Productos
                .AsNoTracking()
                .Include(item => item.Categoria)
                .Include(item => item.Skus)
                    .ThenInclude(sku => sku.SaldosInventario)
                .SingleAsync();

            Assert.Equal("SKU-MIGRACION-001", product.CodigoInterno);
            Assert.Equal("Categoria migracion", product.Categoria.Nombre);
            Assert.Null(product.SubcategoriaId);
            Assert.Single(product.Skus);
            Assert.Equal("SKU-MIGRACION-001", product.Skus.Single().CodigoSku);
            Assert.Equal(7, product.Skus.Single().SaldosInventario.Single().ExistenciaFisica);
            Assert.Equal(1, await verification.Categorias.CountAsync());
            Assert.Equal(1, await verification.ProductoSkus.CountAsync());
            Assert.Equal(1, await verification.InventarioSaldos.CountAsync());
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{SubcategoryMigration}';"));
            Assert.Equal(
                0,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM productos WHERE subcategoria_id IS NOT NULL;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CleanDatabase_MigratesSubcategorySchemaAndLeavesNoPendingMigrations()
    {
        var root = CreateTemporaryRoot();
        var databasePath = Path.Combine(root, "arteinventariado-clean-subcategories.db");

        try
        {
            await using (var context = new AppDbContext(CreateOptions(databasePath)))
            {
                await context.Database.MigrateAsync();

                Assert.Empty(context.Database.GetPendingMigrations());
            }

            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'subcategorias';"));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM pragma_table_info('productos') WHERE name = 'subcategoria_id';"));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM pragma_index_list('subcategorias') WHERE name = 'IX_subcategorias_categoria_id_nombre' AND \"unique\" = 1;"));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM pragma_foreign_key_list('subcategorias') WHERE \"from\" = 'categoria_id' AND \"table\" = 'categorias' AND on_delete = 'RESTRICT';"));
            Assert.Equal(
                1,
                await ReadScalarIntAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM pragma_foreign_key_list('productos') WHERE \"from\" = 'subcategoria_id' AND \"table\" = 'subcategorias' AND on_delete = 'RESTRICT';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateV1134DatabaseAsync(string databasePath)
    {
        await using var context = new AppDbContext(CreateOptions(databasePath));
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var category = new Categoria
        {
            Nombre = "Categoria migracion",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO productos (
                codigo_interno, modo, nombre, descripcion, categoria_id,
                imagen_local_path, precio_compra_centavos, precio_venta_centavos,
                stock_actual, stock_minimo, estado, fecha_creacion, fecha_modificacion)
            VALUES (
                {"SKU-MIGRACION-001"}, {"SIMPLE"}, {"Producto migracion"}, NULL,
                {category.CategoriaId}, NULL, {100}, {200}, {7}, {1}, {"ACTIVO"},
                {DateTime.UtcNow}, NULL);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO producto_skus (
                producto_id, codigo_sku, nombre_variante, activo, es_principal,
                minimo_exhibicion_override, precio_venta_override_centavos,
                costo_override_centavos)
            VALUES (
                (SELECT producto_id FROM productos WHERE codigo_interno = 'SKU-MIGRACION-001'),
                {"SKU-MIGRACION-001"}, NULL, {true}, {true}, NULL, NULL, NULL);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventario_saldos (
                producto_sku_id, ubicacion, existencia_fisica, reservado, version)
            VALUES (
                (SELECT producto_sku_id FROM producto_skus WHERE codigo_sku = 'SKU-MIGRACION-001'),
                {"BODEGA"}, {7}, {0}, {1});
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

    private static async Task<int> ReadScalarIntAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado.SubcategoryMigration.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
