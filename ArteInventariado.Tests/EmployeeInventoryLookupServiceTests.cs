using System.Data.Common;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArteInventariado.Tests;

public sealed class EmployeeInventoryLookupServiceTests
{
    [Theory]
    [InlineData("PRD-ACTIVO")]
    [InlineData("Producto activo")]
    [InlineData("SKU-ACTIVO")]
    [InlineData("Variante activa")]
    [InlineData("Artesanias")]
    public async Task SearchAsync_PreservesExistingTextSearchFields(string searchText)
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(searchText);

        Assert.Contains(result, item => item.CodigoSku == "SKU-ACTIVO");
    }

    [Theory]
    [InlineData("AI-ACTIVO-001")]
    [InlineData("FAB-ACTIVO-001")]
    [InlineData("SEC-ACTIVO-001")]
    public async Task SearchAsync_ResolvesAnyActiveBarcode(string barcode)
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(barcode);

        var item = Assert.Single(result);
        Assert.Equal("SKU-ACTIVO", item.CodigoSku);
    }

    [Fact]
    public async Task SearchAsync_TrimsExternalBarcodeWhitespace()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("  AI-ACTIVO-001  ");

        Assert.Single(result, item => item.CodigoSku == "SKU-ACTIVO");
    }

    [Fact]
    public async Task SearchAsync_PreservesBinaryBarcodeCaseSemantics()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("ai-activo-001");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_ExcludesInactiveBarcodeSkuAndProduct()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        Assert.Empty(await fixture.Service.SearchAsync("AI-INACTIVO-001"));
        Assert.Empty(await fixture.Service.SearchAsync("SKU-INACTIVO-001"));
        Assert.Empty(await fixture.Service.SearchAsync("PROD-INACTIVO-001"));
    }

    [Fact]
    public async Task SearchAsync_UnknownBarcodeFallsBackToExistingTextSearch()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("PRD-ACTIVO");

        Assert.Single(result, item => item.CodigoSku == "SKU-ACTIVO");
    }

    [Fact]
    public async Task SearchAsync_ProjectsCategoryAndNullSubcategoryExplicitly()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("PRD-ACTIVO");
        var item = Assert.Single(result, candidate => candidate.CodigoSku == "SKU-ACTIVO");

        Assert.Equal(fixture.CategoryId, item.CategoriaId);
        Assert.Equal("Artesanias", item.CategoriaNombre);
        Assert.Null(item.SubcategoriaId);
        Assert.Null(item.SubcategoriaNombre);
    }

    [Fact]
    public async Task SearchAsync_ProjectsEffectiveSkuSalePrice()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("PRD-ACTIVO");
        var item = Assert.Single(result, candidate => candidate.CodigoSku == "SKU-ACTIVO");

        Assert.Equal(1500, item.PrecioVentaEfectivoCentavos);
    }

    [Fact]
    public async Task SearchAsync_ProjectsCreationDateStockMinimumAndImagePresenceForFilters()
    {
        await using var fixture = await LookupFixture.CreateAsync();
        var expectedCreatedAt = await fixture.Context.Productos
            .Where(product => product.CodigoInterno == "PRD-ACTIVO")
            .Select(product => product.FechaCreacion)
            .SingleAsync();

        var item = Assert.Single(await fixture.Service.SearchAsync("SKU-ACTIVO"));

        Assert.Equal(expectedCreatedAt, item.FechaCreacion);
        Assert.Equal(1, item.StockMinimo);
        Assert.False(item.TieneImagen);
    }

    [Fact]
    public async Task SearchAsync_FindsProductsBySubcategoryWithoutAdditionalRowQueries()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var subcategory = new Subcategoria
        {
            CategoriaId = fixture.CategoryId,
            Nombre = "Cestería",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        var product = await fixture.Context.Productos
            .SingleAsync(item => item.CodigoInterno == "PRD-ACTIVO");
        product.Subcategoria = subcategory;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        fixture.Sql.Commands.Clear();

        var result = await fixture.Service.SearchAsync("Cestería");

        var item = Assert.Single(result, candidate => candidate.CodigoSku == "SKU-ACTIVO");
        Assert.Equal(subcategory.SubcategoriaId, item.SubcategoriaId);
        Assert.Equal("Cestería", item.SubcategoriaNombre);
        Assert.InRange(fixture.Sql.Commands.Count, 1, 2);
    }

    [Fact]
    public async Task SearchAsync_DoesNotUseLikeAgainstBarcodeValues()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("AI-ACTIVO");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_PreservesTextualStateSemantics()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("SKU-INACTIVO");

        Assert.Single(result, item => item.CodigoSku == "SKU-INACTIVO");
    }

    [Fact]
    public async Task SearchAsync_ReturnsSafeModelAndLeavesContextUntracked()
    {
        await using var fixture = await LookupFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("AI-ACTIVO-001");

        Assert.Single(result);
        var sensitiveNames = typeof(EmployeeInventoryLookupItem)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name =>
                name.Contains("costo", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("margen", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("utilidad", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(sensitiveNames);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SearchAsync_ProjectionDoesNotSelectPurchaseCostColumns()
    {
        await using var fixture = await LookupFixture.CreateAsync();
        fixture.Sql.Commands.Clear();

        _ = await fixture.Service.SearchAsync("AI-ACTIVO-001");

        var sql = string.Join(Environment.NewLine, fixture.Sql.Commands);
        Assert.DoesNotContain("precio_compra_centavos", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("costo_override_centavos", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BarcodeOptionRemainsDisabledByDefaultForPosCatalogConsumers()
    {
        Assert.False(new PosSkuCatalogFilter().IncluirBarcodeExacto);
    }

    private sealed class LookupFixture : IAsyncDisposable
    {
        private LookupFixture(
            SqliteConnection connection,
            AppDbContext context,
            EmployeeInventoryLookupService service,
            int categoryId,
            SqlCaptureInterceptor sql)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Sql = sql;
            CategoryId = categoryId;
        }

        private readonly SqliteConnection _connection;

        public AppDbContext Context { get; }
        public EmployeeInventoryLookupService Service { get; }
        public SqlCaptureInterceptor Sql { get; }
        public int CategoryId { get; }

        public static async Task<LookupFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new SqlCaptureInterceptor();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(sql)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var category = new Categoria
            {
                Nombre = "Artesanias",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var activeProduct = new Producto
            {
                CodigoInterno = "PRD-ACTIVO",
                Nombre = "Producto activo",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 98765,
                PrecioVentaCentavos = 1500,
                StockActual = 12,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            var activeSku = new ProductoSku
            {
                CodigoSku = "SKU-ACTIVO",
                NombreVariante = "Variante activa",
                Activo = true,
                EsPrincipal = true,
                CostoOverrideCentavos = 87654,
                SaldosInventario =
                {
                    Balance(UbicacionInventario.EXHIBICION, 7, 2),
                    Balance(UbicacionInventario.BODEGA, 5, 1),
                    Balance(UbicacionInventario.SIN_ASIGNAR, 2, 0)
                },
                Barcodes =
                {
                    new ProductoSkuBarcode { Codigo = "AI-ACTIVO-001", Activo = true, EsPrincipal = true },
                    new ProductoSkuBarcode { Codigo = "FAB-ACTIVO-001", Activo = true },
                    new ProductoSkuBarcode { Codigo = "SEC-ACTIVO-001", Activo = true },
                    new ProductoSkuBarcode { Codigo = "AI-INACTIVO-001", Activo = false }
                }
            };
            var inactiveSku = new ProductoSku
            {
                CodigoSku = "SKU-INACTIVO",
                NombreVariante = "Variante archivada",
                Activo = false,
                EsPrincipal = false,
                Barcodes =
                {
                    new ProductoSkuBarcode { Codigo = "SKU-INACTIVO-001", Activo = true, EsPrincipal = true }
                }
            };
            activeProduct.Skus.Add(activeSku);
            activeProduct.Skus.Add(inactiveSku);

            var inactiveProduct = new Producto
            {
                CodigoInterno = "PRD-ARCHIVADO",
                Nombre = "Producto archivado",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 33333,
                PrecioVentaCentavos = 900,
                StockActual = 0,
                StockMinimo = 1,
                Estado = EstadoProducto.INACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = "SKU-PRODUCTO-INACTIVO",
                        Activo = true,
                        EsPrincipal = true,
                        Barcodes =
                        {
                            new ProductoSkuBarcode
                            {
                                Codigo = "PROD-INACTIVO-001",
                                Activo = true,
                                EsPrincipal = true
                            }
                        }
                    }
                }
            };

            context.Productos.AddRange(activeProduct, inactiveProduct);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();

            return new LookupFixture(
                connection,
                context,
                new EmployeeInventoryLookupService(context),
                category.CategoriaId,
                sql);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static InventarioSaldo Balance(
            UbicacionInventario location,
            int physical,
            int reserved) => new()
        {
            Ubicacion = location,
            ExistenciaFisica = physical,
            Reservado = reserved,
            Version = 1
        };
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
