using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class ProductProfitabilityQueryServiceTests
{
    [Fact]
    public async Task Query_UsesHistoricalPricesDiscountsReturnsChangesAndNegativeMargins()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();

        var report = await fixture.Query.QueryAsync(fixture.Request());

        var profit = fixture.Row(report, "PRD-PROFIT");
        Assert.Equal(2, profit.NetUnits);
        Assert.Equal(1, profit.ReturnedUnits);
        Assert.Equal(2_100, profit.NetSalesCentavos);
        Assert.Equal(900, profit.KnownCostCentavos);
        Assert.Equal(900, profit.TotalCostCentavos);
        Assert.Equal(450m, profit.AppliedUnitCostCentavos);
        Assert.Equal(600m, profit.UnitGainCentavos);
        Assert.Equal(1_200, profit.TotalGainCentavos);
        Assert.Equal(57.14m, profit.MarginPercentage);
        Assert.Equal(100m, profit.CostCoveragePercentage);
        Assert.Equal(ProductProfitabilityCoverageState.Complete, profit.CoverageState);

        var loss = fixture.Row(report, "PRD-LOSS");
        Assert.Equal(-300, loss.TotalGainCentavos);
        Assert.Equal(-300m, loss.UnitGainCentavos);
        Assert.Equal(-60m, loss.MarginPercentage);

        var changed = fixture.Row(report, "PRD-CHANGED");
        Assert.Equal(1, changed.NetUnits);
        Assert.Equal(700, changed.NetSalesCentavos);
        Assert.Equal(300, changed.TotalCostCentavos);
        Assert.Equal(ProductProfitabilityCoverageState.Complete, changed.CoverageState);
    }

    [Fact]
    public async Task Query_DoesNotInventProfitForPartialOrMissingHistoricalCost()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();

        var report = await fixture.Query.QueryAsync(fixture.Request());

        var partial = fixture.Row(report, "PRD-PARTIAL");
        Assert.Equal(3, partial.NetUnits);
        Assert.Equal(3_000, partial.NetSalesCentavos);
        Assert.Equal(600, partial.KnownCostCentavos);
        Assert.Equal(300m, partial.AppliedUnitCostCentavos);
        Assert.Equal(66.67m, partial.CostCoveragePercentage);
        Assert.Null(partial.TotalCostCentavos);
        Assert.Null(partial.TotalGainCentavos);
        Assert.Null(partial.UnitGainCentavos);
        Assert.Null(partial.MarginPercentage);
        Assert.Equal(ProductProfitabilityCoverageState.Partial, partial.CoverageState);

        var withoutCost = fixture.Row(report, "PRD-NOCOST");
        Assert.Equal(0m, withoutCost.CostCoveragePercentage);
        Assert.Null(withoutCost.KnownCostCentavos);
        Assert.Null(withoutCost.TotalCostCentavos);
        Assert.Null(withoutCost.TotalGainCentavos);
        Assert.Equal(ProductProfitabilityCoverageState.NoCoverage, withoutCost.CoverageState);
    }

    [Fact]
    public async Task Query_ExcludesCanceledSalesAndMarksFullyReturnedProductsWithoutSales()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();

        var report = await fixture.Query.QueryAsync(fixture.Request());

        var canceled = fixture.Row(report, "PRD-CANCELED");
        Assert.Equal(0, canceled.NetUnits);
        Assert.Equal(0, canceled.NetSalesCentavos);
        Assert.Equal(0, canceled.ReturnedUnits);
        Assert.Equal(ProductProfitabilityCoverageState.NoSales, canceled.CoverageState);
        Assert.Null(canceled.KnownCostCentavos);

        var returned = fixture.Row(report, "PRD-RETURNED");
        Assert.Equal(0, returned.NetUnits);
        Assert.Equal(1, returned.ReturnedUnits);
        Assert.Equal(0, returned.NetSalesCentavos);
        Assert.Null(returned.KnownCostCentavos);
        Assert.Equal(ProductProfitabilityCoverageState.NoSales, returned.CoverageState);
    }

    [Fact]
    public async Task Query_SupportsSearchCategoryTopAndProfitabilityOrdering()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();

        var bySku = await fixture.Query.QueryAsync(fixture.Request() with
        {
            Search = "SKU-PROFIT"
        });
        Assert.Equal("PRD-PROFIT", Assert.Single(bySku.Rows).ProductCode);

        var byCategory = await fixture.Query.QueryAsync(fixture.Request() with
        {
            CategoryId = fixture.SecondCategoryId,
            TopLimit = null
        });
        Assert.All(byCategory.Rows, row => Assert.Equal(fixture.SecondCategoryId, row.CategoryId));
        Assert.Equal(3, byCategory.TotalRows);

        var topBySales = await fixture.Query.QueryAsync(fixture.Request() with
        {
            Sort = ProductProfitabilitySort.NetSales,
            Descending = true,
            TopLimit = 1
        });
        var top = Assert.Single(topBySales.Rows);
        Assert.Equal("PRD-PARTIAL", top.ProductCode);

        var byGain = await fixture.Query.QueryAsync(fixture.Request() with
        {
            Sort = ProductProfitabilitySort.TotalGain,
            Descending = true,
            TopLimit = null
        });
        Assert.NotEmpty(byGain.Rows);
        var firstNonCalculable = byGain.Rows
            .Select((row, index) => (row, index))
            .FirstOrDefault(item => !item.row.TotalGainCentavos.HasValue)
            .index;
        Assert.True(firstNonCalculable >= 0);
        Assert.All(byGain.Rows.Take(firstNonCalculable), row =>
            Assert.Equal(ProductProfitabilityCoverageState.Complete, row.CoverageState));
    }

    [Fact]
    public async Task Query_DoesNotUseCurrentSubcategoryForHistoricalRowsWithoutSnapshot()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();
        var product = await fixture.Context.Productos
            .SingleAsync(item => item.ProductoId == fixture.ProductIds["PRD-PROFIT"]);
        var subcategory = new Subcategoria
        {
            CategoriaId = product.CategoriaId,
            Nombre = "Clasificación actual",
            Activa = true,
            FechaCreacion = DateTime.UtcNow,
            FechaModificacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        await fixture.Context.SaveChangesAsync();
        product.SubcategoriaId = subcategory.SubcategoriaId;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var report = await fixture.Query.QueryAsync(fixture.Request());
        var row = fixture.Row(report, "PRD-PROFIT");

        Assert.Null(row.SubcategoryId);
        Assert.Equal("No disponible (histórico)", row.SubcategoryName);
    }

    [Fact]
    public async Task Query_DistinguishesExplicitlyUnassignedAndHistoricalUnavailableFilters()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();

        var explicitRows = await fixture.Query.QueryAsync(fixture.Request() with
        {
            SubcategoryFilter = AnalyticsSubcategoryFilter.ExplicitlyUnassigned,
            TopLimit = null
        });
        Assert.NotEmpty(explicitRows.Rows);
        Assert.All(explicitRows.Rows, row =>
            Assert.Equal(AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel, row.SubcategoryName));

        var historicalRows = await fixture.Query.QueryAsync(fixture.Request() with
        {
            SubcategoryFilter = AnalyticsSubcategoryFilter.HistoricalUnavailable,
            TopLimit = null
        });
        Assert.NotEmpty(historicalRows.Rows);
        Assert.All(historicalRows.Rows, row =>
            Assert.Equal(AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel, row.SubcategoryName));
    }

    [Fact]
    public async Task Query_RequiresBothEmployeePermissionsAndAdminRemainsAllowed()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();

        var storedUser = await fixture.Context.Usuarios.SingleAsync(
            user => user.UsuarioId == fixture.User.UsuarioId);
        storedUser.Rol = RolUsuario.EMPLEADO;
        await fixture.Context.SaveChangesAsync();
        fixture.Session.SetRole(RolUsuario.EMPLEADO);
        fixture.Permissions.Enable(PermisoEmpleado.REPORTES_COMERCIALES_VER);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Query.QueryAsync(fixture.Request()));

        fixture.Permissions.Enable(PermisoEmpleado.COSTOS_VER);
        var employeeReport = await fixture.Query.QueryAsync(fixture.Request());
        Assert.NotEmpty(employeeReport.Rows);

        storedUser.Rol = RolUsuario.ADMIN;
        await fixture.Context.SaveChangesAsync();
        fixture.Session.SetRole(RolUsuario.ADMIN);
        fixture.Permissions.Disable(PermisoEmpleado.COSTOS_VER);
        fixture.Permissions.Disable(PermisoEmpleado.REPORTES_COMERCIALES_VER);
        var adminReport = await fixture.Query.QueryAsync(fixture.Request());
        Assert.NotEmpty(adminReport.Rows);
    }

    [Fact]
    public async Task AdvancedReportAndExcelExportPreserveCoverageStatesForAllAndSelectedRows()
    {
        await using var fixture = await ProfitabilityFixture.CreateAsync();
        var request = new AdvancedReportRequest(
            AdvancedReportKind.ProductProfitability,
            fixture.Request().Analytics,
            ProfitabilitySort: ProductProfitabilitySort.NetSales,
            ProfitabilityDescending: true,
            ProfitabilityTop: null);

        var report = await fixture.Reports.QueryAsync(request);
        var partial = Assert.Single(report.Rows, row => Equals(row["code"], "PRD-PARTIAL"));
        Assert.Equal("Parcial", partial["coverageState"]);
        Assert.Null(partial["totalGain"]);
        Assert.Contains(report.Columns, column => column.Title == "Cobertura costo");

        var allExport = await fixture.Exports.ExportExcelAsync(request);
        using (var workbook = new XLWorkbook(allExport.FilePath))
        {
            var values = workbook.Worksheet(1).CellsUsed().Select(cell => cell.GetString()).ToList();
            Assert.Contains("Parcial", values);
            Assert.Contains("No calculable", values);
        }

        var selectedExport = await fixture.Exports.ExportExcelAsync(request with
        {
            SelectedProductIds = [fixture.ProductIds["PRD-PROFIT"]]
        });
        Assert.Equal(1, selectedExport.ExportedRows);
        using (var workbook = new XLWorkbook(selectedExport.FilePath))
        {
            var values = workbook.Worksheet(1).CellsUsed().Select(cell => cell.GetString()).ToList();
            Assert.Contains("PRD-PROFIT", values);
            Assert.DoesNotContain("PRD-PARTIAL", values);
        }
    }

    private sealed class ProfitabilityFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ProfitabilityFixture(
            SqliteConnection connection,
            AppDbContext context,
            Usuario user,
            ProductProfitabilityQueryService query,
            AdvancedReportQueryService reports,
            AdvancedReportExportService exports,
            MutableSession session,
            PermissionSet permissions,
            IReadOnlyDictionary<string, int> productIds,
            int secondCategoryId,
            string outputDirectory)
        {
            _connection = connection;
            Context = context;
            User = user;
            Query = query;
            Reports = reports;
            Exports = exports;
            Session = session;
            Permissions = permissions;
            ProductIds = productIds;
            SecondCategoryId = secondCategoryId;
            OutputDirectory = outputDirectory;
        }

        public AppDbContext Context { get; }
        public Usuario User { get; }
        public ProductProfitabilityQueryService Query { get; }
        public AdvancedReportQueryService Reports { get; }
        public AdvancedReportExportService Exports { get; }
        public MutableSession Session { get; }
        public PermissionSet Permissions { get; }
        public IReadOnlyDictionary<string, int> ProductIds { get; }
        public int SecondCategoryId { get; }
        public string OutputDirectory { get; }

        public ProductProfitabilityRequest Request() => new(
            new AnalyticsQueryRequest(new AnalyticsPeriodRequest(
                AnalyticsPeriodKind.CustomRange,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31))));

        public static async Task<ProfitabilityFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Admin rentabilidad",
                NombreUsuario = "admin-profitability",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = Utc(2025, 1, 1)
            };
            var firstCategory = new Categoria
            {
                Nombre = "Artesanías",
                Activa = true,
                FechaCreacion = Utc(2025, 1, 1)
            };
            var secondCategory = new Categoria
            {
                Nombre = "Hogar",
                Activa = true,
                FechaCreacion = Utc(2025, 1, 1)
            };
            context.AddRange(user, firstCategory, secondCategory);
            context.ConfiguracionSistema.AddRange(
                new ConfiguracionSistema
                {
                    Clave = DashboardQueryService.BusinessTimeZoneKey,
                    Valor = DashboardQueryService.DefaultBusinessTimeZoneId,
                    FechaModificacion = Utc(2025, 1, 1)
                },
                new ConfiguracionSistema
                {
                    Clave = SystemConfigService.BusinessNameKey,
                    Valor = "Negocio de rentabilidad",
                    FechaModificacion = Utc(2025, 1, 1)
                });
            await context.SaveChangesAsync();

            var products = new[]
            {
                Product("PRD-PROFIT", "Producto rentable", firstCategory),
                Product("PRD-LOSS", "Producto con pérdida", firstCategory),
                Product("PRD-PARTIAL", "Producto costo parcial", secondCategory),
                Product("PRD-NOCOST", "Producto sin costo", secondCategory),
                Product("PRD-CANCELED", "Producto anulado", firstCategory),
                Product("PRD-RETURNED", "Producto devuelto", firstCategory),
                Product("PRD-CHANGED", "Producto de cambio", secondCategory),
                Product("PRD-NOSALES", "Producto sin ventas", firstCategory)
            };
            context.Productos.AddRange(products);
            await context.SaveChangesAsync();

            var skuCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRD-PROFIT"] = "SKU-PROFIT",
                ["PRD-LOSS"] = "SKU-LOSS",
                ["PRD-PARTIAL"] = "SKU-PARTIAL",
                ["PRD-NOCOST"] = "SKU-NOCOST",
                ["PRD-CANCELED"] = "SKU-CANCELED",
                ["PRD-RETURNED"] = "SKU-RETURNED",
                ["PRD-CHANGED"] = "SKU-CHANGED",
                ["PRD-NOSALES"] = "SKU-NOSALES"
            };
            context.ProductoSkus.AddRange(products.Select(product => new ProductoSku
            {
                ProductoId = product.ProductoId,
                CodigoSku = skuCodes[product.CodigoInterno],
                Activo = true,
                EsPrincipal = true
            }));
            await context.SaveChangesAsync();

            var cash = new Caja
            {
                FechaOperacion = new DateOnly(2026, 8, 1),
                FechaApertura = Utc(2026, 8, 1, 8),
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = user.UsuarioId
            };
            context.Cajas.Add(cash);
            await context.SaveChangesAsync();

            var byCode = products.ToDictionary(product => product.CodigoInterno);

            async Task<DetalleVenta> AddSale(
                string code,
                string number,
                DateTime date,
                int quantity,
                long unitPrice,
                long lineDiscount,
                long generalDiscount,
                long? cost,
                OrigenCostoHistorico costOrigin,
                EstadoVenta state = EstadoVenta.COMPLETADA)
            {
                var product = byCode[code];
                var lineBeforeGeneral = unitPrice * quantity - lineDiscount;
                var lineTotal = lineBeforeGeneral - generalDiscount;
                var sale = new Venta
                {
                    NumeroVenta = number,
                    CajaId = cash.CajaId,
                    UsuarioId = user.UsuarioId,
                    UsuarioNombreSnapshot = user.NombreCompleto,
                    ClienteNombreSnapshot = "Consumidor final",
                    FechaVenta = date,
                    SubtotalCentavos = unitPrice * quantity,
                    DescuentoCentavos = lineDiscount + generalDiscount,
                    TotalCentavos = lineTotal,
                    PagadoCentavos = lineTotal,
                    Estado = state
                };
                context.Ventas.Add(sale);
                await context.SaveChangesAsync();
                var detail = new DetalleVenta
                {
                    VentaId = sale.VentaId,
                    ProductoId = product.ProductoId,
                    ProductoSkuId = context.ProductoSkus.Single(item => item.ProductoId == product.ProductoId).ProductoSkuId,
                    Cantidad = quantity,
                    PrecioUnitarioCentavos = unitPrice,
                    CostoUnitarioCentavos = cost,
                    OrigenCosto = costOrigin,
                    ProductoCodigoSnapshot = product.CodigoInterno,
                    ProductoNombreSnapshot = product.Nombre,
                    CodigoSkuSnapshot = skuCodes[code],
                    CategoriaIdSnapshot = product.CategoriaId,
                    CategoriaNombreSnapshot = product.Categoria.Nombre,
                    SubtotalCentavos = unitPrice * quantity,
                    DescuentoCentavos = lineDiscount,
                    DescuentoGeneralProporcionalCentavos = generalDiscount,
                    DescuentoMotivo = lineDiscount > 0 ? "Promoción" : null,
                    TotalCentavos = lineBeforeGeneral
                };
                context.DetalleVentas.Add(detail);
                await context.SaveChangesAsync();
                return detail;
            }

            var profitFirst = await AddSale(
                "PRD-PROFIT", "VTA-PROFIT-001", Utc(2026, 8, 2), 2, 1_000, 100, 100,
                400, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            await AddSale(
                "PRD-PROFIT", "VTA-PROFIT-002", Utc(2026, 8, 3), 1, 1_200, 0, 0,
                500, OrigenCostoHistorico.SKU_OVERRIDE);
            await AddSale(
                "PRD-LOSS", "VTA-LOSS-001", Utc(2026, 8, 4), 1, 500, 0, 0,
                800, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            await AddSale(
                "PRD-PARTIAL", "VTA-PARTIAL-001", Utc(2026, 8, 5), 2, 1_000, 0, 0,
                300, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            await AddSale(
                "PRD-PARTIAL", "VTA-PARTIAL-002", Utc(2026, 8, 6), 1, 1_000, 0, 0,
                null, OrigenCostoHistorico.DESCONOCIDO);
            await AddSale(
                "PRD-NOCOST", "VTA-NOCOST-001", Utc(2026, 8, 7), 1, 900, 0, 0,
                null, OrigenCostoHistorico.DESCONOCIDO);
            await AddSale(
                "PRD-CANCELED", "VTA-CANCELED-001", Utc(2026, 8, 8), 2, 1_000, 0, 0,
                400, OrigenCostoHistorico.PRODUCTO_ACTUAL, EstadoVenta.ANULADA);
            var returnedSale = await AddSale(
                "PRD-RETURNED", "VTA-RETURNED-001", Utc(2026, 8, 9), 1, 700, 0, 0,
                500, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            var correction = new Devolucion
            {
                NumeroDevolucion = "DEV-PROFIT-001",
                VentaId = profitFirst.VentaId,
                UsuarioId = user.UsuarioId,
                FechaDevolucion = Utc(2026, 8, 11),
                TipoCorreccion = TipoCorreccionVenta.DEVOLUCION,
                TotalDevueltoCentavos = 900,
                Motivo = "Prueba de devolución"
            };
            context.Devoluciones.Add(correction);
            await context.SaveChangesAsync();
            context.DetalleDevoluciones.Add(new DetalleDevolucion
            {
                DevolucionId = correction.DevolucionId,
                DetalleVentaId = profitFirst.DetalleVentaId,
                ProductoId = profitFirst.ProductoId,
                ProductoSkuId = profitFirst.ProductoSkuId,
                CodigoSkuSnapshot = profitFirst.CodigoSkuSnapshot,
                Cantidad = 1,
                PrecioUnitarioCentavos = profitFirst.PrecioUnitarioCentavos,
                DescuentoProporcionalCentavos = 100,
                SubtotalDevueltoCentavos = 900,
                CondicionProducto = CondicionProductoDevuelto.BUENO,
                ReintegraStock = true,
                UbicacionReintegro = UbicacionInventario.BODEGA
            });
            await context.SaveChangesAsync();

            var changedProduct = byCode["PRD-CHANGED"];
            var changeMovement = new MovimientoInventario
            {
                ProductoId = changedProduct.ProductoId,
                UsuarioId = user.UsuarioId,
                TipoMovimiento = TipoMovimientoInventario.ENTRADA,
                Cantidad = 1,
                StockAnterior = 0,
                StockNuevo = 1,
                Motivo = "Cambio de prueba",
                FechaMovimiento = Utc(2026, 8, 11)
            };
            context.MovimientosInventario.Add(changeMovement);
            await context.SaveChangesAsync();
            context.DetallesCambioVenta.Add(new DetalleCambioVenta
            {
                DevolucionId = correction.DevolucionId,
                DetalleVentaOrigenId = profitFirst.DetalleVentaId,
                ProductoNuevoId = changedProduct.ProductoId,
                ProductoSkuNuevoId = context.ProductoSkus.Single(item => item.ProductoId == changedProduct.ProductoId).ProductoSkuId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 700,
                CostoUnitarioCentavos = 300,
                OrigenCosto = OrigenCostoHistorico.PRODUCTO_ACTUAL,
                ProductoCodigoSnapshot = changedProduct.CodigoInterno,
                ProductoNombreSnapshot = changedProduct.Nombre,
                CodigoSkuSnapshot = skuCodes[changedProduct.CodigoInterno],
                CategoriaIdSnapshot = changedProduct.CategoriaId,
                CategoriaNombreSnapshot = changedProduct.Categoria.Nombre,
                SubtotalCentavos = 700,
                DescuentoCentavos = 0,
                TotalCentavos = 700,
                MovimientoInventarioId = changeMovement.MovimientoInventarioId
            });
            await context.SaveChangesAsync();

            var returnedCorrection = new Devolucion
            {
                NumeroDevolucion = "DEV-RETURNED-001",
                VentaId = returnedSale.VentaId,
                UsuarioId = user.UsuarioId,
                FechaDevolucion = Utc(2026, 8, 12),
                TipoCorreccion = TipoCorreccionVenta.DEVOLUCION,
                TotalDevueltoCentavos = 700,
                Motivo = "Devolución total de prueba"
            };
            context.Devoluciones.Add(returnedCorrection);
            await context.SaveChangesAsync();
            context.DetalleDevoluciones.Add(new DetalleDevolucion
            {
                DevolucionId = returnedCorrection.DevolucionId,
                DetalleVentaId = returnedSale.DetalleVentaId,
                ProductoId = returnedSale.ProductoId,
                ProductoSkuId = returnedSale.ProductoSkuId,
                CodigoSkuSnapshot = returnedSale.CodigoSkuSnapshot,
                Cantidad = 1,
                PrecioUnitarioCentavos = returnedSale.PrecioUnitarioCentavos,
                SubtotalDevueltoCentavos = 700,
                CondicionProducto = CondicionProductoDevuelto.BUENO,
                ReintegraStock = true,
                UbicacionReintegro = UbicacionInventario.BODEGA
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var session = new MutableSession(new UsuarioSesion(
                user.UsuarioId, user.NombreCompleto, user.NombreUsuario, RolUsuario.ADMIN));
            var permissions = new PermissionSet();
            var clock = new FixedTimeProvider(new DateTimeOffset(
                2026, 8, 12, 18, 0, 0, TimeSpan.Zero));
            var factory = new TestContextFactory(options);
            var sales = new SalesAnalyticsQueryService(context, session, clock);
            var financial = new FinancialAnalyticsQueryService(factory, sales, session);
            var commercial = new CommercialAnalyticsQueryService(context, sales, clock);
            var query = new ProductProfitabilityQueryService(context, session, permissions, clock);
            var reports = new AdvancedReportQueryService(
                context, sales, financial, commercial, session, query);
            var output = Path.Combine(
                Path.GetTempPath(),
                "ArteInventariado-product-profitability-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            var exports = new AdvancedReportExportService(
                context,
                reports,
                session,
                new NoOpAuditService(),
                new TestReportPathProvider(output),
                clock,
                query);
            return new ProfitabilityFixture(
                connection,
                context,
                user,
                query,
                reports,
                exports,
                session,
                permissions,
                byCode.ToDictionary(item => item.Key, item => item.Value.ProductoId),
                secondCategory.CategoriaId,
                output);
        }

        public ProductProfitabilityRow Row(ProductProfitabilityReport report, string code) =>
            Assert.Single(report.Rows, row => row.ProductCode == code);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(OutputDirectory))
            {
                Directory.Delete(OutputDirectory, recursive: true);
            }
        }

        private static Producto Product(string code, string name, Categoria category) => new()
        {
            CodigoInterno = code,
            Nombre = name,
            Categoria = category,
            PrecioCompraCentavos = 100,
            PrecioVentaCentavos = 500,
            StockActual = 0,
            StockMinimo = 0,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = Utc(2025, 1, 1)
        };

        private static DateTime Utc(int year, int month, int day, int hour = 12) =>
            new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    }

    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class TestReportPathProvider(string root) : IReportPathProvider
    {
        public Task<string> GetRootDirectoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(root);

        public Task<string> GetDirectoryAsync(
            string category,
            CancellationToken cancellationToken = default)
        {
            var directory = Path.Combine(root, category);
            Directory.CreateDirectory(directory);
            return Task.FromResult(directory);
        }
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task RegistrarAsync(
            AuditEntryRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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

    private sealed class PermissionSet : IEmployeePermissionService
    {
        private readonly HashSet<PermisoEmpleado> _enabled = [];

        public void Enable(params PermisoEmpleado[] permissions) =>
            _enabled.UnionWith(permissions);

        public void Disable(params PermisoEmpleado[] permissions)
        {
            foreach (var permission in permissions)
            {
                _enabled.Remove(permission);
            }
        }

        public Task<bool> HasPermissionAsync(
            PermisoEmpleado permiso,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_enabled.Contains(permiso));

        public Task<bool> HasAnyPermissionAsync(
            IReadOnlyCollection<PermisoEmpleado> permisos,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permisos.Any(_enabled.Contains));

        public Task EnsurePermissionAsync(
            PermisoEmpleado permiso,
            CancellationToken cancellationToken = default) =>
            _enabled.Contains(permiso)
                ? Task.CompletedTask
                : Task.FromException(new UnauthorizedAccessException());

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
}
