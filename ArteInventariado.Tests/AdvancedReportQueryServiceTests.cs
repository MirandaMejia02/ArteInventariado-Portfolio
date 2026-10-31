using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class AdvancedReportQueryServiceTests
{
    [Fact]
    public async Task Catalog_AllRegisteredReportsCanQueryWithoutData()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);

        foreach (var definition in AdvancedReportCatalog.All)
        {
            var result = await fixture.Service.QueryAsync(Request(definition.Kind));

            Assert.Equal(definition.Kind, result.Definition.Kind);
            Assert.NotNull(result.Columns);
            Assert.False(string.IsNullOrWhiteSpace(result.EmptyMessage));
        }

        Assert.Equal(27, AdvancedReportCatalog.All.Count);
    }

    [Fact]
    public async Task SalesAndGrossProfitReportsReuseApprovedAnalyticsTotals()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);

        var analyticsSales = await fixture.Sales.GetSalesAsync(Request(AdvancedReportKind.Sales).Analytics);
        var analyticsFinance = await fixture.Financial.GetAsync(Request(AdvancedReportKind.GrossProfit).Analytics);
        var salesReport = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Sales));
        var profitReport = await fixture.Service.QueryAsync(Request(AdvancedReportKind.GrossProfit));

        Assert.Equal(
            analyticsSales.Summary.NetSales.Value / 100m,
            salesReport.Summary.Single(item => item.Label == "Ventas netas").Value);
        Assert.Equal(
            analyticsFinance.Finance.GrossProfit.Value / 100m,
            profitReport.Summary.Single(item => item.Label == "Ganancia bruta").Value);
        Assert.Equal(
            analyticsFinance.Finance.CostCoverage.Percentage,
            profitReport.Summary.Single(item => item.Label == "Cobertura histórica").Value);
    }

    [Fact]
    public async Task CashReport_ListsCashContributionsAsTheirOwnCashInput()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);
        var cashBox = await fixture.Context.Cajas.SingleAsync();
        fixture.Context.AportesCaja.Add(new AporteCaja
        {
            CajaId = cashBox.CajaId,
            UsuarioId = fixture.Session.UsuarioActual!.UsuarioId,
            MontoCentavos = 2_750,
            Motivo = "Fondo para cambio",
            FechaAporte = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        await fixture.Context.SaveChangesAsync();

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Cash));

        var row = Assert.Single(report.Rows);
        Assert.Equal(27.5m, row["cashContributions"]);
        Assert.Contains(report.Columns, column =>
            column.Key == "cashContributions" && column.Title == "Aportes para caja");
        Assert.Equal(27.5m, row["expected"]);
    }

    [Fact]
    public async Task InventoryReport_CombinesSearchCategoryAndStateFilters()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);
        var request = Request(AdvancedReportKind.Inventory) with
        {
            Search = "PRD-REPORT",
            CategoryId = fixture.CategoryId,
            State = EstadoProducto.ACTIVO.ToString()
        };

        var report = await fixture.Service.QueryAsync(request);

        var row = Assert.Single(report.Rows);
        Assert.Equal("PRD-REPORT-001", row["code"]);
        Assert.Contains("Categoría", report.AppliedFilters.Single(item => item.StartsWith("Categoría")));
        Assert.Contains("Estado", report.AppliedFilters.Single(item => item.StartsWith("Estado")));
    }

    [Fact]
    public async Task InventoryReport_UsesCanonicalBalancesAndSeparatesOperationalStates()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);
        var product = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == fixture.ProductId);
        product.StockActual = 999;
        var sku = await fixture.Context.ProductoSkus.SingleAsync(item => item.ProductoId == fixture.ProductId);
        fixture.Context.MovimientosInventarioUbicacion.Add(new MovimientoInventarioUbicacion
        {
            OperacionId = Guid.NewGuid(),
            ProductoSkuId = sku.ProductoSkuId,
            TipoOperacion = TipoOperacionInventario.MERMA,
            UbicacionOrigen = UbicacionInventario.BODEGA,
            Cantidad = 2,
            UsuarioId = fixture.Session.UsuarioActual!.UsuarioId,
            Motivo = "Merma de prueba analítica",
            Fecha = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Inventory));

        var row = Assert.Single(report.Rows);
        Assert.Equal(8, row["physical"]);
        Assert.Equal(3, row["exhibition"]);
        Assert.Equal(2, row["warehouse"]);
        Assert.Equal(3, row["unassigned"]);
        Assert.Equal(1, row["reserved"]);
        Assert.Equal(7, row["available"]);
        Assert.Equal("Requiere reposición", row["situation"]);
        Assert.Equal("Pendiente de clasificar", row["classification"]);
        Assert.Equal(2, row["waste"]);
        Assert.Equal(8m, row["wasteValue"]);
        Assert.Equal(8, report.Summary.Single(item => item.Label == "Stock físico total").Value);
        Assert.Equal(1, report.Summary.Single(item => item.Label == "Requiere reposición").Value);
        Assert.Contains("inventario_saldos", report.CoverageNote);
    }

    [Fact]
    public async Task InventoryValue_UsesSkuEffectiveCostAndNeverLegacyStockProjection()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);
        var product = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == fixture.ProductId);
        product.StockActual = 999;
        var sku = await fixture.Context.ProductoSkus.SingleAsync(item => item.ProductoId == fixture.ProductId);
        sku.CostoOverrideCentavos = 550;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.CurrentInventoryValue));

        var row = Assert.Single(report.Rows);
        Assert.Equal(8, row["stock"]);
        Assert.Equal(5.50m, row["cost"]);
        Assert.Equal(44m, row["value"]);
        Assert.Equal(44m, report.Summary.Single(item => item.Label == "Inversión actual").Value);
    }

    [Fact]
    public async Task InventoryValue_ExposesPotentialMetricsAndGlobalMarginFromCanonicalBalances()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.CurrentInventoryValue));

        var row = Assert.Single(report.Rows);
        Assert.Equal(8, row["stock"]);
        Assert.Equal(1, row["reserved"]);
        Assert.Equal(7, row["available"]);
        Assert.Equal(32m, row["investment"]);
        Assert.Equal(80m, row["potential"]);
        Assert.Equal(70m, row["availableValue"]);
        Assert.Equal(48m, row["gain"]);
        Assert.Equal(60m, row["margin"]);
        Assert.Contains(report.Summary, item => item.Label == "Venta disponible");
        Assert.Equal(60m, report.Summary.Single(item => item.Label == "Margen bruto potencial").Value);
    }

    [Fact]
    public async Task InventoryValue_DoesNotMixLegacyStockForSkuLessProductWhenCanonicalBalancesExist()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);
        fixture.Context.Productos.Add(new Producto
        {
            CodigoInterno = "PRD-NO-SKU-001",
            Nombre = "Producto sin SKU",
            CategoriaId = fixture.CategoryId,
            PrecioCompraCentavos = 300,
            PrecioVentaCentavos = 500,
            StockActual = 999,
            StockMinimo = 0,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.CurrentInventoryValue));

        var row = Assert.Single(report.Rows, item => Equals(item["code"], "PRD-NO-SKU-001"));
        Assert.Equal(0, row["stock"]);
        Assert.Equal(0m, row["investment"]);
        Assert.Equal("Sin SKU", row["sku"]);
        Assert.DoesNotContain(report.Rows, item => Equals(item["stock"], 999));
    }

    [Fact]
    public async Task InventoryValue_SubcategoryUnassignedFilterKeepsCurrentRowsAndEmptyFilterIsSafe()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);

        var request = Request(AdvancedReportKind.CurrentInventoryValue) with
        {
            SubcategoryFilter = AnalyticsSubcategoryFilter.ExplicitlyUnassigned
        };
        var report = await fixture.Service.QueryAsync(request);

        Assert.Single(report.Rows);
        Assert.Equal("Sin subcategoría", report.Rows[0]["subcategory"]);

        var noRows = await fixture.Service.QueryAsync(request with { Search = "no-existe" });
        Assert.Empty(noRows.Rows);
        Assert.Contains("no-existe", noRows.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InventoryMovements_PrefersCanonicalRowsAndKeepsUnmatchedLegacyHistory()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);
        var sku = await fixture.Context.ProductoSkus.SingleAsync(item => item.ProductoId == fixture.ProductId);
        var userId = fixture.Session.UsuarioActual!.UsuarioId;
        var mirroredAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        fixture.Context.MovimientosInventarioUbicacion.Add(new MovimientoInventarioUbicacion
        {
            OperacionId = Guid.NewGuid(),
            ProductoSkuId = sku.ProductoSkuId,
            TipoOperacion = TipoOperacionInventario.TRANSFERENCIA,
            UbicacionOrigen = UbicacionInventario.BODEGA,
            UbicacionDestino = UbicacionInventario.EXHIBICION,
            Cantidad = 1,
            UsuarioId = userId,
            Motivo = "Movimiento canónico",
            Fecha = mirroredAt
        });
        fixture.Context.MovimientosInventario.AddRange(
            new MovimientoInventario
            {
                ProductoId = fixture.ProductId,
                UsuarioId = userId,
                TipoMovimiento = TipoMovimientoInventario.AJUSTE,
                Cantidad = 1,
                StockAnterior = 7,
                StockNuevo = 8,
                Motivo = "Movimiento canónico",
                FechaMovimiento = mirroredAt
            },
            new MovimientoInventario
            {
                ProductoId = fixture.ProductId,
                UsuarioId = userId,
                TipoMovimiento = TipoMovimientoInventario.ENTRADA,
                Cantidad = 3,
                StockAnterior = 4,
                StockNuevo = 7,
                Motivo = "Historia v1.9",
                FechaMovimiento = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc)
            });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.InventoryMovements));

        Assert.Equal(2, report.TotalRows);
        Assert.Contains(report.Rows, row => Equals(row["coverage"], "SKU exacto"));
        Assert.Contains(report.Rows, row => Equals(row["coverage"], "Histórico sin SKU"));
        Assert.Single(report.Rows, row => Equals(row["reason"], "Movimiento canónico"));
    }

    [Theory]
    [InlineData("PRD-REPORT-001")]
    [InlineData("report-00")]
    [InlineData("Producto reportable")]
    [InlineData("ducto repo")]
    [InlineData("Categoría reportes")]
    [InlineData("prd-report-001")]
    public async Task ProductsReport_SearchesDeclaredFieldsCaseInsensitively(string search)
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Products) with
        {
            Search = search
        });

        var row = Assert.Single(report.Rows);
        Assert.Equal("PRD-REPORT-001", row["code"]);
        Assert.Equal("Categoría reportes", row["category"]);
    }

    [Fact]
    public async Task ProductsReport_NoSearchMatchDoesNotFallBackToAllRows()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Products) with
        {
            Search = "producto-que-no-existe"
        });

        Assert.Empty(report.Rows);
        Assert.Equal(0, report.TotalRows);
        Assert.Equal("No se encontraron resultados para 'producto-que-no-existe'.", report.EmptyMessage);
    }

    [Fact]
    public async Task ProductsReport_CombinesSearchProductCategoryAndPeriod()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);

        var report = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Products) with
        {
            Search = "reportable",
            ProductId = fixture.ProductId,
            CategoryId = fixture.CategoryId
        });

        Assert.Single(report.Rows);
        Assert.Equal(1, report.TotalRows);
        Assert.Contains(report.AppliedFilters, item => item.StartsWith("Búsqueda:"));
        Assert.Contains(report.AppliedFilters, item => item.StartsWith("Producto:"));
        Assert.Contains(report.AppliedFilters, item => item.StartsWith("Categoría:"));
    }

    [Fact]
    public async Task CatalogReports_ExposeAndFilterCurrentSubcategoryClassification()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);
        var product = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == fixture.ProductId);
        var subcategory = new Subcategoria
        {
            CategoriaId = product.CategoriaId,
            Nombre = "Subcategoría catálogo",
            Activa = true,
            FechaCreacion = DateTime.UtcNow,
            FechaModificacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        await fixture.Context.SaveChangesAsync();
        product.SubcategoriaId = subcategory.SubcategoriaId;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var products = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Products) with
        {
            SubcategoryId = subcategory.SubcategoriaId
        });
        var inventory = await fixture.Service.QueryAsync(Request(AdvancedReportKind.Inventory) with
        {
            SubcategoryId = subcategory.SubcategoriaId
        });

        Assert.Equal("Subcategoría catálogo", Assert.Single(products.Rows)["subcategory"]);
        Assert.Equal("Subcategoría catálogo", Assert.Single(inventory.Rows)["subcategory"]);
        var options = await fixture.Service.GetOptionsAsync(
            AdvancedReportKind.Products,
            AdvancedReportFilterKind.Subcategory,
            "catálogo");
        Assert.Equal(subcategory.SubcategoriaId, Assert.Single(options).Id);
        Assert.Equal(fixture.CategoryId, options[0].ParentId);

        var specialOptions = await fixture.Service.GetOptionsAsync(
            AdvancedReportKind.Categories,
            AdvancedReportFilterKind.Subcategory);
        Assert.Contains(
            specialOptions,
            option => option.Id == AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedOptionId &&
                option.Name == AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel);
        Assert.Contains(
            specialOptions,
            option => option.Id == AnalyticsSubcategoryFilterOptions.HistoricalUnavailableOptionId &&
                option.Name == AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel);
    }

    [Fact]
    public async Task Catalog_EverySearchableReportDeclaresFieldsAndHonorsZeroResults()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);
        var searchable = AdvancedReportCatalog.All
            .Where(item => item.Filters.Contains(AdvancedReportFilterKind.Search))
            .ToList();

        Assert.NotEmpty(searchable);
        Assert.All(searchable, definition => Assert.NotEmpty(definition.SearchableFields));
        Assert.DoesNotContain(
            AdvancedReportFilterKind.Search,
            AdvancedReportCatalog.Get(AdvancedReportKind.GrossProfit).Filters);

        foreach (var definition in searchable)
        {
            var report = await fixture.Service.QueryAsync(Request(definition.Kind) with
            {
                Search = "__sin_coincidencias_8f9e__"
            });
            Assert.Equal(0, report.TotalRows);
            Assert.Empty(report.Rows);
            Assert.StartsWith("No se encontraron resultados", report.EmptyMessage);
        }
    }

    [Fact]
    public async Task FilterOptions_ReturnFriendlyBoundedEntitySelectors()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: true);

        var products = await fixture.Service.GetOptionsAsync(
            AdvancedReportKind.Products,
            AdvancedReportFilterKind.Product,
            "report");
        var categories = await fixture.Service.GetOptionsAsync(
            AdvancedReportKind.Products,
            AdvancedReportFilterKind.Category,
            "categoría");

        Assert.Equal(fixture.ProductId, Assert.Single(products).Id);
        Assert.Contains("PRD-REPORT-001", products[0].DisplayText);
        Assert.Equal(fixture.CategoryId, Assert.Single(categories).Id);
        Assert.Empty(await fixture.Service.GetOptionsAsync(
            AdvancedReportKind.Sales,
            AdvancedReportFilterKind.Product));
    }

    [Theory]
    [InlineData(AnalyticsPeriodKind.CurrentQuarter)]
    [InlineData(AnalyticsPeriodKind.CurrentSemester)]
    [InlineData(AnalyticsPeriodKind.CurrentYear)]
    [InlineData(AnalyticsPeriodKind.MultipleYears)]
    public async Task ReportsReuseQuarterSemesterYearAndMultipleYearPeriods(AnalyticsPeriodKind kind)
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);
        var period = kind == AnalyticsPeriodKind.MultipleYears
            ? new AnalyticsPeriodRequest(kind, StartYear: 2025, EndYear: 2026)
            : new AnalyticsPeriodRequest(kind);

        var report = await fixture.Service.QueryAsync(new AdvancedReportRequest(
            AdvancedReportKind.Sales,
            new AnalyticsQueryRequest(period)));

        Assert.Equal(kind, report.Period.Kind);
    }

    [Fact]
    public async Task QueryRequiresActiveAdminAndHonorsCancellation()
    {
        await using var fixture = await ReportFixture.CreateAsync(seedSale: false);
        fixture.Session.SetRole(RolUsuario.EMPLEADO);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Service.QueryAsync(Request(AdvancedReportKind.Customers)));

        fixture.Session.SetRole(RolUsuario.ADMIN);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.QueryAsync(Request(AdvancedReportKind.Customers), cancellation.Token));
    }

    private static AdvancedReportRequest Request(AdvancedReportKind kind) => new(
        kind,
        new AnalyticsQueryRequest(new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.CustomRange,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 8, 12))));

    private sealed class ReportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ReportFixture(
            SqliteConnection connection,
            AppDbContext context,
            AdvancedReportQueryService service,
            SalesAnalyticsQueryService sales,
            FinancialAnalyticsQueryService financial,
            MutableSession session,
            int categoryId,
            int productId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Sales = sales;
            Financial = financial;
            Session = session;
            CategoryId = categoryId;
            ProductId = productId;
        }

        public AppDbContext Context { get; }
        public AdvancedReportQueryService Service { get; }
        public SalesAnalyticsQueryService Sales { get; }
        public FinancialAnalyticsQueryService Financial { get; }
        public MutableSession Session { get; }
        public int CategoryId { get; }
        public int ProductId { get; }

        public static async Task<ReportFixture> CreateAsync(bool seedSale)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = new Usuario
            {
                NombreCompleto = "Admin reportes",
                NombreUsuario = "admin-reports",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = Utc(2025, 1, 1)
            };
            var category = new Categoria
            {
                Nombre = "Categoría reportes",
                Activa = true,
                FechaCreacion = Utc(2025, 1, 1)
            };
            context.AddRange(admin, category);
            context.ConfiguracionSistema.AddRange(
                new ConfiguracionSistema
                {
                    Clave = DashboardQueryService.BusinessTimeZoneKey,
                    Valor = DashboardQueryService.DefaultBusinessTimeZoneId,
                    FechaModificacion = Utc(2025, 1, 1)
                },
                new ConfiguracionSistema
                {
                    Clave = "analytics.snapshots_ventas_desde_utc",
                    Valor = "2026-01-01T00:00:00.000Z",
                    FechaModificacion = Utc(2025, 1, 1)
                });
            await context.SaveChangesAsync();

            var product = new Producto
            {
                CodigoInterno = "PRD-REPORT-001",
                Nombre = "Producto reportable",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 400,
                PrecioVentaCentavos = 1_000,
                StockActual = 8,
                StockMinimo = 2,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = Utc(2025, 1, 1)
            };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            var primarySku = new ProductoSku
            {
                ProductoId = product.ProductoId,
                CodigoSku = product.CodigoInterno,
                Activo = true,
                EsPrincipal = true
            };
            primarySku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.EXHIBICION,
                ExistenciaFisica = 3,
                Reservado = 1,
                Version = 1
            });
            primarySku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.BODEGA,
                ExistenciaFisica = 2,
                Version = 1
            });
            primarySku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.SIN_ASIGNAR,
                ExistenciaFisica = 3,
                Version = 1
            });
            context.ProductoSkus.Add(primarySku);
            await context.SaveChangesAsync();

            if (seedSale)
            {
                var cash = new Caja
                {
                    FechaOperacion = new DateOnly(2026, 4, 1),
                    FechaApertura = Utc(2026, 4, 1, 8),
                    FechaCierre = Utc(2026, 4, 1, 18),
                    Estado = EstadoCaja.CERRADA,
                    UsuarioAperturaId = admin.UsuarioId,
                    UsuarioCierreId = admin.UsuarioId
                };
                context.Cajas.Add(cash);
                await context.SaveChangesAsync();
                var sale = new Venta
                {
                    NumeroVenta = "VTA-REPORT-001",
                    CajaId = cash.CajaId,
                    UsuarioId = admin.UsuarioId,
                    UsuarioNombreSnapshot = admin.NombreCompleto,
                    ClienteNombreSnapshot = "Consumidor final",
                    FechaVenta = Utc(2026, 4, 1),
                    SubtotalCentavos = 2_000,
                    TotalCentavos = 2_000,
                    PagadoCentavos = 2_000,
                    Estado = EstadoVenta.COMPLETADA
                };
                context.Ventas.Add(sale);
                await context.SaveChangesAsync();
                context.DetalleVentas.Add(new DetalleVenta
                {
                    VentaId = sale.VentaId,
                    ProductoId = product.ProductoId,
                    Cantidad = 2,
                    PrecioUnitarioCentavos = 1_000,
                    CostoUnitarioCentavos = 400,
                    OrigenCosto = OrigenCostoHistorico.PRODUCTO_ACTUAL,
                    ProductoCodigoSnapshot = product.CodigoInterno,
                    ProductoNombreSnapshot = product.Nombre,
                    CategoriaIdSnapshot = category.CategoriaId,
                    CategoriaNombreSnapshot = category.Nombre,
                    SubtotalCentavos = 2_000,
                    DescuentoCentavos = 0,
                    DescuentoGeneralProporcionalCentavos = 0,
                    TotalCentavos = 2_000
                });
                await context.SaveChangesAsync();
            }
            context.ChangeTracker.Clear();

            var session = new MutableSession(new UsuarioSesion(
                admin.UsuarioId, admin.NombreCompleto, admin.NombreUsuario, RolUsuario.ADMIN));
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero));
            var factory = new TestContextFactory(options);
            var sales = new SalesAnalyticsQueryService(context, session, clock);
            var financial = new FinancialAnalyticsQueryService(factory, sales, session);
            var commercial = new CommercialAnalyticsQueryService(context, sales, clock);
            var permissions = new AllowAllPermissionService();
            var profitability = new ProductProfitabilityQueryService(context, session, permissions, clock);
            var service = new AdvancedReportQueryService(
                context,
                sales,
                financial,
                commercial,
                session,
                profitability);
            return new ReportFixture(
                connection,
                context,
                service,
                sales,
                financial,
                session,
                category.CategoriaId,
                product.ProductoId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static DateTime Utc(int year, int month, int day, int hour = 12) =>
            new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    }

    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AllowAllPermissionService : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(
            PermisoEmpleado permiso,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> HasAnyPermissionAsync(
            IReadOnlyCollection<PermisoEmpleado> permisos,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task EnsurePermissionAsync(
            PermisoEmpleado permiso,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);

        public Task UpdatePermissionsAsync(
            UpdateEmployeePermissionsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestoreRecommendedTemplateAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplySafeOperationalTemplateAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureRecommendedPermissionsForEmployeeAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MutableSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
        public void SetRole(RolUsuario role) => UsuarioActual = UsuarioActual! with { Rol = role };
    }
}
