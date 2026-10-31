using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class ProductSkuModelTests
{
    [Fact]
    public void CanonicalEnums_ExposeOnlyApprovedModesAndLocations()
    {
        Assert.Equal(
            ["SIMPLE", "SURTIDO", "POR_VARIANTES"],
            Enum.GetNames<ModoProducto>());
        Assert.Equal(
            ["EXHIBICION", "BODEGA", "SIN_ASIGNAR"],
            Enum.GetNames<UbicacionInventario>());
        Assert.DoesNotContain("PIEZA_UNICA", Enum.GetNames<ModoProducto>());
    }

    [Theory]
    [InlineData(ModoProducto.SIMPLE)]
    [InlineData(ModoProducto.SURTIDO)]
    public async Task SingleSkuModes_PersistOneOperationalSku(ModoProducto mode)
    {
        await using var fixture = await ProductSkuFixture.CreateAsync();
        var product = fixture.NewProduct(mode, $"SKU-{mode}", stock: 8);
        fixture.Context.Productos.Add(product);

        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var stored = await fixture.Context.Productos
            .AsNoTracking()
            .Include(item => item.Skus)
            .SingleAsync();
        Assert.Equal(mode, stored.Modo);
        Assert.Single(stored.Skus);
        Assert.Equal(8, stored.StockActual);
    }

    [Fact]
    public async Task VariantMode_PersistsMultipleIndividuallyIdentifiedSkus()
    {
        await using var fixture = await ProductSkuFixture.CreateAsync();
        var product = fixture.NewProduct(ModoProducto.POR_VARIANTES, "VAR-ROJO", stock: 15);
        product.Skus.Add(new ProductoSku
        {
            CodigoSku = "VAR-AZUL",
            NombreVariante = "Azul",
            Activo = false
        });
        fixture.Context.Productos.Add(product);

        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var stored = await fixture.Context.Productos
            .AsNoTracking()
            .Include(item => item.Skus)
            .SingleAsync();
        Assert.Equal(ModoProducto.POR_VARIANTES, stored.Modo);
        Assert.Equal(2, stored.Skus.Count);
        Assert.Contains(stored.Skus, sku => sku.CodigoSku == "VAR-AZUL" && !sku.Activo);
    }

    [Fact]
    public void NullableOverrides_InheritProductBaseValues()
    {
        var product = new Producto
        {
            PrecioCompraCentavos = 125,
            PrecioVentaCentavos = 275
        };
        var sku = new ProductoSku { Producto = product };

        Assert.Equal(125, sku.ResolverCostoCentavos());
        Assert.Equal(275, sku.ResolverPrecioVentaCentavos());

        sku.CostoOverrideCentavos = 150;
        sku.PrecioVentaOverrideCentavos = 325;
        Assert.Equal(150, sku.ResolverCostoCentavos());
        Assert.Equal(325, sku.ResolverPrecioVentaCentavos());
    }

    [Fact]
    public async Task SkuCode_IsTextualUniqueAndPreservesLeadingZeros()
    {
        await using var fixture = await ProductSkuFixture.CreateAsync();
        fixture.Context.Productos.Add(fixture.NewProduct(ModoProducto.SIMPLE, "000007", stock: 4));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Assert.Equal("000007", await fixture.Context.ProductoSkus.Select(sku => sku.CodigoSku).SingleAsync());

        fixture.Context.Productos.Add(fixture.NewProduct(ModoProducto.SIMPLE, "000007", stock: 1));
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task SkuModel_DoesNotCreateASecondWritableStockSource()
    {
        Assert.DoesNotContain(
            typeof(ProductoSku).GetProperties(),
            property => property.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase));

        await using var fixture = await ProductSkuFixture.CreateAsync();
        var product = fixture.NewProduct(ModoProducto.POR_VARIANTES, "STOCK-A", stock: 27);
        product.Skus.Add(new ProductoSku { CodigoSku = "STOCK-B" });
        fixture.Context.Productos.Add(product);
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(27, await fixture.Context.Productos.Select(item => item.StockActual).SingleAsync());
    }

    private sealed class ProductSkuFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ProductSkuFixture(SqliteConnection connection, AppDbContext context, int categoryId)
        {
            _connection = connection;
            Context = context;
            CategoryId = categoryId;
        }

        public AppDbContext Context { get; }
        public int CategoryId { get; }

        public static async Task<ProductSkuFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Categorias.Add(category);
            await context.SaveChangesAsync();
            return new ProductSkuFixture(connection, context, category.CategoriaId);
        }

        public Producto NewProduct(ModoProducto mode, string skuCode, int stock) => new()
        {
            CodigoInterno = $"LEG-{Guid.NewGuid():N}",
            Modo = mode,
            Nombre = $"Producto {skuCode}",
            CategoriaId = CategoryId,
            PrecioCompraCentavos = 100,
            PrecioVentaCentavos = 200,
            StockActual = stock,
            StockMinimo = 0,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow,
            Skus =
            {
                new ProductoSku { CodigoSku = skuCode }
            }
        };

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
