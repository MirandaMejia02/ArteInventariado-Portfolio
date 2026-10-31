using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class PosCatalogServiceTests
{
    [Fact]
    public async Task SearchAsync_ProjectsSingleSkuModesAndEachActiveVariant()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var items = await fixture.Service.SearchAsync();

        Assert.Equal(4, items.Count);
        Assert.Single(items, item => item.Modo == ModoProducto.SIMPLE);
        Assert.Single(items, item => item.Modo == ModoProducto.SURTIDO);
        Assert.Equal(2, items.Count(item => item.Modo == ModoProducto.POR_VARIANTES));
        Assert.DoesNotContain(items, item => item.CodigoSku == "VAR-ARCHIVO");
    }

    [Theory]
    [InlineData("REZE", "SKU-SIMPLE-001")]
    [InlineData("000042", "SKU-SIMPLE-001")]
    [InlineData("sku-simple", "SKU-SIMPLE-001")]
    [InlineData("rosa-xl", "VAR-ROSA-XL")]
    [InlineData("ROSA GRANDE", "VAR-ROSA-XL")]
    public async Task SearchAsync_IsCaseInsensitiveAcrossDeclaredManualFields(
        string search,
        string expectedSku)
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(SearchText: search));

        Assert.Contains(result, item => item.CodigoSku == expectedSku);
    }

    [Theory]
    [InlineData("AI1234567890")]
    [InlineData("FAB-PINK-01")]
    [InlineData("SECONDARY-PINK-01")]
    public async Task SearchAsync_WithBarcodeExactoResolvesAnyActiveBarcodeForItsSku(string barcode)
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: barcode,
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true));

        var item = Assert.Single(result);
        Assert.Equal(fixture.PinkSkuId, item.ProductoSkuId);
        Assert.Equal("VAR-ROSA-XL", item.CodigoSku);
    }

    [Fact]
    public async Task SearchAsync_WithBarcodeExactoIgnoresExternalSpaces()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "  AI1234567890  ",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true));

        Assert.Single(result, item => item.ProductoSkuId == fixture.PinkSkuId);
    }

    [Fact]
    public async Task SearchAsync_WithBarcodeExactoPreservesBinaryCaseSemantics()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "fab-pink-01",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true));

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_WithBarcodeExactoRejectsInactiveBarcodeSkuAndProduct()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        Assert.Empty(await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "INACTIVE-BLUE-01",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true)));

        Assert.Empty(await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "INACTIVE-SKU-01",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true)));

        var blueSku = await fixture.Context.ProductoSkus
            .Include(sku => sku.Producto)
            .SingleAsync(sku => sku.ProductoSkuId == fixture.BlueSkuId);
        blueSku.Producto.Estado = EstadoProducto.INACTIVO;
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "FAB-BLUE-01",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true)));
    }

    [Fact]
    public async Task SearchAsync_WithBarcodeExactoFallsBackToExistingTextSearch()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "REZE",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true));

        Assert.Single(result, item => item.CodigoSku == "SKU-SIMPLE-001");
    }

    [Fact]
    public async Task SearchAsync_WithBarcodeExactoAndUnknownBarcodeReturnsNoResults()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "BARCODE-NO-EXISTE",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true));

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_WithoutBarcodeOptionKeepsPosTextSearchAndDoesNotResolveBarcode()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var barcodeResult = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "AI1234567890",
            Estado: null,
            SkuActivo: null));
        var textResult = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "REZE",
            Estado: null,
            SkuActivo: null));

        Assert.Empty(barcodeResult);
        Assert.Single(textResult, item => item.CodigoSku == "SKU-SIMPLE-001");
    }

    [Fact]
    public async Task SearchAsync_BarcodeLookupRemainsReadOnly()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        _ = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "AI1234567890",
            Estado: null,
            SkuActivo: null,
            IncluirBarcodeExacto: true));

        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SearchAsync_CombinesCategoryAndTextAndNeverFallsBackOnZeroResults()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var categoryMatches = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "decoracion especial",
            CategoriaId: fixture.CategoryId));
        var wrongCategory = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "reze",
            CategoriaId: fixture.OtherCategoryId));
        var noMatches = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "codigo-que-no-existe"));

        Assert.Equal(4, categoryMatches.Count);
        Assert.Empty(wrongCategory);
        Assert.Empty(noMatches);
    }

    [Fact]
    public async Task SearchAsync_ProjectsSubcategoryAndSupportsAllConcreteAndWithoutSubcategoryFilters()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var subcategory = new Subcategoria
        {
            CategoriaId = fixture.CategoryId,
            Nombre = "Cerámica pintada",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        var simple = await fixture.Context.Productos
            .SingleAsync(product => product.CodigoInterno == "000042");
        simple.Subcategoria = subcategory;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var projected = Assert.Single(await fixture.Service.SearchAsync(
            new PosSkuCatalogFilter(SearchText: "Cerámica pintada")));
        Assert.Equal(subcategory.SubcategoriaId, projected.SubcategoriaId);
        Assert.Equal(subcategory.Nombre, projected.SubcategoriaNombre);

        var concrete = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            CategoriaId: fixture.CategoryId,
            SubcategoriaFilter: PosSubcategoryFilter.Concreta(subcategory.SubcategoriaId)));
        Assert.Contains(concrete, item => item.SubcategoriaId == subcategory.SubcategoriaId);

        var withoutSubcategory = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            CategoriaId: fixture.CategoryId,
            SubcategoriaFilter: PosSubcategoryFilter.SinSubcategoria));
        Assert.DoesNotContain(withoutSubcategory, item => item.SubcategoriaId == subcategory.SubcategoriaId);

        var all = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            CategoriaId: fixture.CategoryId,
            SubcategoriaFilter: PosSubcategoryFilter.Todas));
        Assert.Contains(all, item => item.SubcategoriaId == subcategory.SubcategoriaId);
    }

    [Fact]
    public async Task SearchAsync_KeepsActiveProductWithHistoricalInactiveSubcategoryVisible()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var historical = new Subcategoria
        {
            CategoriaId = fixture.CategoryId,
            Nombre = "Subcategoría histórica",
            Activa = false,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(historical);
        var simple = await fixture.Context.Productos
            .SingleAsync(product => product.CodigoInterno == "000042");
        simple.Subcategoria = historical;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var all = await fixture.Service.SearchAsync();
        Assert.Contains(all, item =>
            item.CodigoInterno == "000042" &&
            item.SubcategoriaId == historical.SubcategoriaId);

        var byHistoricalName = await fixture.Service.SearchAsync(
            new PosSkuCatalogFilter(SearchText: historical.Nombre));
        Assert.Contains(byHistoricalName, item => item.CodigoInterno == "000042");
    }

    [Fact]
    public async Task SearchAsync_RejectsIncompatibleCategoryAndSubcategoryCombination()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var subcategory = new Subcategoria
        {
            CategoriaId = fixture.CategoryId,
            Nombre = "Cerámica pintada",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        var simple = await fixture.Context.Productos
            .SingleAsync(product => product.CodigoInterno == "000042");
        simple.Subcategoria = subcategory;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            CategoriaId: fixture.OtherCategoryId,
            SubcategoriaFilter: PosSubcategoryFilter.Concreta(subcategory.SubcategoriaId)));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetBySkuIdAsync_ReturnsExactSkuWithEffectiveValuesLocationBalancesAndSkuImage()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var item = await fixture.Service.GetBySkuIdAsync(fixture.PinkSkuId);

        Assert.NotNull(item);
        Assert.Equal("VAR-ROSA-XL", item.CodigoSku);
        Assert.Equal("Rosa grande", item.VarianteNombre);
        Assert.Equal(650, item.PrecioVentaEfectivoCentavos);
        Assert.Equal(300, item.CostoEfectivoCentavos);
        Assert.Equal(7, item.StockFisicoExhibicion);
        Assert.Equal(2, item.StockReservadoExhibicion);
        Assert.Equal(5, item.StockDisponibleExhibicion);
        Assert.Equal(5, item.StockFisicoBodega);
        Assert.Equal(1, item.StockReservadoBodega);
        Assert.Equal(4, item.StockDisponibleBodega);
        Assert.Equal(3, item.StockFisicoSinAsignar);
        Assert.Equal(0, item.StockReservadoSinAsignar);
        Assert.Equal(3, item.StockDisponibleSinAsignar);
        Assert.Equal(15, item.StockFisicoTotal);
        Assert.Equal(3, item.StockReservadoTotal);
        Assert.Equal(12, item.StockDisponibleTotal);
        Assert.True(item.DisponibleParaVenta);
        Assert.Equal(ProductAssetOwnerKind.Sku, item.ImagenOwnerKind);
        Assert.NotNull(item.ImagenAssetId);
        Assert.Equal("Assets/Skus/rosa.png", item.ImagenRutaRelativa);
        Assert.Null(item.ImagenLocalPath);
    }

    [Fact]
    public async Task GetBySkuIdAsync_SkuWithoutOwnImageFallsBackToPrimaryProductImage()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var item = await fixture.Service.GetBySkuIdAsync(fixture.BlueSkuId);

        Assert.NotNull(item);
        Assert.Equal(ProductAssetOwnerKind.Product, item.ImagenOwnerKind);
        Assert.NotNull(item.ImagenAssetId);
        Assert.Equal("Assets/Products/bolso.png", item.ImagenRutaRelativa);
    }

    [Fact]
    public async Task GetBySkuIdAsync_WhenSkuAndProductHaveNoImageReturnsPlaceholderMetadata()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var item = await fixture.Service.GetBySkuIdAsync(fixture.AssortmentSkuId);

        Assert.NotNull(item);
        Assert.Null(item.ImagenOwnerKind);
        Assert.Null(item.ImagenAssetId);
        Assert.Null(item.ImagenRutaRelativa);
        Assert.Null(item.ImagenLocalPath);
    }

    [Fact]
    public async Task GetBySkuIdAsync_IsExactAndCanDescribeInactiveSkuWhileDefaultSearchHidesIt()
    {
        await using var fixture = await PosCatalogFixture.CreateAsync();

        var exact = await fixture.Service.GetBySkuIdAsync(fixture.InactiveSkuId);
        var defaultSearch = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "VAR-ARCHIVO"));
        var inactiveSearch = await fixture.Service.SearchAsync(new PosSkuCatalogFilter(
            SearchText: "var-archivo",
            SkuActivo: false));

        Assert.NotNull(exact);
        Assert.False(exact.SkuActivo);
        Assert.False(exact.DisponibleParaVenta);
        Assert.Empty(defaultSearch);
        Assert.Single(inactiveSearch);
    }

    private sealed class PosCatalogFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PosCatalogFixture(
            SqliteConnection connection,
            AppDbContext context,
            PosCatalogService service,
            int categoryId,
            int otherCategoryId,
            int pinkSkuId,
            int blueSkuId,
            int assortmentSkuId,
            int inactiveSkuId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            CategoryId = categoryId;
            OtherCategoryId = otherCategoryId;
            PinkSkuId = pinkSkuId;
            BlueSkuId = blueSkuId;
            AssortmentSkuId = assortmentSkuId;
            InactiveSkuId = inactiveSkuId;
        }

        public AppDbContext Context { get; }
        public PosCatalogService Service { get; }
        public int CategoryId { get; }
        public int OtherCategoryId { get; }
        public int PinkSkuId { get; }
        public int BlueSkuId { get; }
        public int AssortmentSkuId { get; }
        public int InactiveSkuId { get; }

        public static async Task<PosCatalogFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();

            var category = new Categoria
            {
                Nombre = "Decoracion especial",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            var otherCategory = new Categoria
            {
                Nombre = "Otra categoria",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Categorias.AddRange(category, otherCategory);
            await context.SaveChangesAsync();

            var simple = BuildProduct(
                category.CategoriaId,
                ModoProducto.SIMPLE,
                "000042",
                "Reze clasica",
                "SKU-SIMPLE-001",
                "Principal",
                exhibition: (4, 0));
            simple.ImagenLocalPath = "legacy-reze.jpg";

            var assortment = BuildProduct(
                category.CategoriaId,
                ModoProducto.SURTIDO,
                "SUR-001",
                "Canasta surtida",
                "SURTIDO-MIX",
                null,
                exhibition: (2, 0));

            var variant = new Producto
            {
                CodigoInterno = "VAR-001",
                Nombre = "Bolso de colores",
                CategoriaId = category.CategoriaId,
                Modo = ModoProducto.POR_VARIANTES,
                PrecioCompraCentavos = 200,
                PrecioVentaCentavos = 500,
                StockActual = 18,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Assets =
                {
                    BuildProductAsset("Assets/Products/bolso.png")
                }
            };

            var pink = new ProductoSku
            {
                CodigoSku = "VAR-ROSA-XL",
                NombreVariante = "Rosa grande",
                Activo = true,
                EsPrincipal = true,
                PrecioVentaOverrideCentavos = 650,
                CostoOverrideCentavos = 300,
                SaldosInventario =
                {
                    Balance(UbicacionInventario.EXHIBICION, 7, 2),
                    Balance(UbicacionInventario.BODEGA, 5, 1),
                    Balance(UbicacionInventario.SIN_ASIGNAR, 3, 0)
                },
                Assets =
                {
                    BuildSkuAsset("Assets/Skus/rosa.png")
                }
            };
            var blue = new ProductoSku
            {
                CodigoSku = "VAR-AZUL",
                NombreVariante = "Azul mediano",
                Activo = true,
                SaldosInventario =
                {
                    Balance(UbicacionInventario.EXHIBICION, 3, 0),
                    Balance(UbicacionInventario.BODEGA, 0, 0),
                    Balance(UbicacionInventario.SIN_ASIGNAR, 0, 0)
                }
            };
            var inactive = new ProductoSku
            {
                CodigoSku = "VAR-ARCHIVO",
                NombreVariante = "Diseno archivado",
                Activo = false,
                SaldosInventario =
                {
                    Balance(UbicacionInventario.EXHIBICION, 0, 0),
                    Balance(UbicacionInventario.BODEGA, 0, 0),
                    Balance(UbicacionInventario.SIN_ASIGNAR, 0, 0)
                }
            };
            variant.Skus.Add(pink);
            variant.Skus.Add(blue);
            variant.Skus.Add(inactive);

            pink.Barcodes.Add(new ProductoSkuBarcode
            {
                Codigo = "AI1234567890",
                Activo = true,
                EsPrincipal = true
            });
            pink.Barcodes.Add(new ProductoSkuBarcode
            {
                Codigo = "FAB-PINK-01",
                Activo = true,
                EsPrincipal = false
            });
            pink.Barcodes.Add(new ProductoSkuBarcode
            {
                Codigo = "SECONDARY-PINK-01",
                Activo = true,
                EsPrincipal = false
            });
            blue.Barcodes.Add(new ProductoSkuBarcode
            {
                Codigo = "FAB-BLUE-01",
                Activo = true,
                EsPrincipal = true
            });
            blue.Barcodes.Add(new ProductoSkuBarcode
            {
                Codigo = "INACTIVE-BLUE-01",
                Activo = false,
                EsPrincipal = false
            });
            inactive.Barcodes.Add(new ProductoSkuBarcode
            {
                Codigo = "INACTIVE-SKU-01",
                Activo = true,
                EsPrincipal = true
            });

            context.Productos.AddRange(simple, assortment, variant);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            return new PosCatalogFixture(
                connection,
                context,
                new PosCatalogService(context),
                category.CategoriaId,
                otherCategory.CategoriaId,
                pink.ProductoSkuId,
                blue.ProductoSkuId,
                assortment.Skus.Single().ProductoSkuId,
                inactive.ProductoSkuId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Producto BuildProduct(
            int categoryId,
            ModoProducto mode,
            string internalCode,
            string name,
            string skuCode,
            string? variant,
            (int Physical, int Reserved) exhibition)
        {
            return new Producto
            {
                CodigoInterno = internalCode,
                Nombre = name,
                CategoriaId = categoryId,
                Modo = mode,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 250,
                StockActual = exhibition.Physical,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = skuCode,
                        NombreVariante = variant,
                        Activo = true,
                        EsPrincipal = true,
                        SaldosInventario =
                        {
                            Balance(UbicacionInventario.EXHIBICION, exhibition.Physical, exhibition.Reserved),
                            Balance(UbicacionInventario.BODEGA, 0, 0),
                            Balance(UbicacionInventario.SIN_ASIGNAR, 0, 0)
                        }
                    }
                }
            };
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

        private static ProductoAsset BuildProductAsset(string relativePath) => new()
        {
            StorageKey = Guid.NewGuid(),
            RutaRelativa = relativePath,
            NombreOriginal = "product.png",
            MimeType = "image/png",
            Extension = ".png",
            SizeBytes = 128,
            Width = 32,
            Height = 32,
            Sha256 = new string('a', 64),
            Orden = 0,
            EsPrincipal = true,
            FechaCreacion = DateTime.UtcNow
        };

        private static ProductoSkuAsset BuildSkuAsset(string relativePath) => new()
        {
            StorageKey = Guid.NewGuid(),
            RutaRelativa = relativePath,
            NombreOriginal = "sku.png",
            MimeType = "image/png",
            Extension = ".png",
            SizeBytes = 128,
            Width = 32,
            Height = 32,
            Sha256 = new string('b', 64),
            Orden = 0,
            EsPrincipal = true,
            FechaCreacion = DateTime.UtcNow
        };
    }
}
