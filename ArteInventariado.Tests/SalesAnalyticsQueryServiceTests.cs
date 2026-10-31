using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class SalesAnalyticsQueryServiceTests
{
    [Fact]
    public async Task SalesReport_CalculatesGrossDiscountReturnsNetAndCoverage()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        var report = await fixture.Service.GetSalesAsync(CustomYear(2026));

        Assert.Equal(6000, report.Summary.GrossSales.Value);
        Assert.Equal(100, report.Summary.Discounts.Value);
        Assert.Equal(950, report.Summary.Returns.Value);
        Assert.Equal(4950, report.Summary.NetSales.Value);
        Assert.Equal(3, report.Summary.Transactions.Value);
        Assert.Equal(1650, report.Summary.AverageTicket.Value);
        Assert.Equal(5, report.Summary.NetUnits.Value);
        Assert.Equal(3, report.Coverage.ProductSnapshots.TotalRecords);
        Assert.Equal(2, report.Coverage.ProductSnapshots.CompleteRecords);
        Assert.Equal(66.67m, report.Coverage.ProductSnapshots.Percentage);
        Assert.NotNull(report.Coverage.ReliableSnapshotsFromUtc);
        Assert.Equal(12, report.Trend.Count);
    }

    [Theory]
    [InlineData(AnalyticsPeriodKind.PreviousQuarter, 1)]
    [InlineData(AnalyticsPeriodKind.PreviousSemester, 2)]
    [InlineData(AnalyticsPeriodKind.PreviousYear, 2)]
    public async Task ProductAnalytics_SupportsQuarterSemesterAndYear(
        AnalyticsPeriodKind periodKind,
        int minimumExpectedProducts)
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        var report = await fixture.Service.GetProductsAsync(
            new AnalyticsQueryRequest(new AnalyticsPeriodRequest(periodKind)));

        Assert.True(report.MostSoldByUnits.Count >= minimumExpectedProducts);
        Assert.All(report.MostSoldByUnits, row => Assert.True(row.CurrentStock >= 0));
    }

    [Theory]
    [InlineData(SalesAnalyticsQueryLimits.MinimumProducts)]
    [InlineData(SalesAnalyticsQueryLimits.MaximumProducts)]
    public async Task ProductAnalytics_AcceptsPublishedLimits(int limit)
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        var report = await fixture.Service.GetProductsAsync(
            CustomYear(2026),
            limit);

        Assert.NotEmpty(report.MostSoldByUnits);
    }

    [Fact]
    public async Task ProductAnalytics_RejectsLimitAbovePublishedMaximum()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Service.GetProductsAsync(
                CustomYear(2026),
                SalesAnalyticsQueryLimits.MaximumProducts + 1));
    }

    [Theory]
    [InlineData(AnalyticsPeriodKind.PreviousQuarter)]
    [InlineData(AnalyticsPeriodKind.PreviousSemester)]
    [InlineData(AnalyticsPeriodKind.PreviousYear)]
    public async Task CategoryAnalytics_SupportsQuarterSemesterAndYear(AnalyticsPeriodKind periodKind)
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        var report = await fixture.Service.GetCategoriesAsync(
            new AnalyticsQueryRequest(new AnalyticsPeriodRequest(periodKind)));

        Assert.NotEmpty(report.Categories);
        Assert.All(report.Categories, row => Assert.False(string.IsNullOrWhiteSpace(row.CategoryName)));
    }

    [Fact]
    public async Task CategoryAnalytics_SeparatesLegacyRowsWithoutHistoricalCategory()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        var report = await fixture.Service.GetCategoriesAsync(CustomYear(2026));

        var legacy = Assert.Single(report.Categories, row => row.CategoryName == "Sin categoria historica");
        Assert.Null(legacy.CategoryIdSnapshot);
        Assert.Equal(1000, legacy.NetSalesCentavos);
        Assert.Equal(HistoricalDataQuality.Unavailable, legacy.SnapshotQuality);
    }

    [Fact]
    public async Task CategoryAnalytics_SeparatesSubcategorySnapshotsAndSupportsDependentFilters()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        var report = await fixture.Service.GetCategoriesAsync(CustomYear(2026));
        var subcategories = report.Subcategories!;

        var assigned = Assert.Single(subcategories, row => row.SubcategoryName == "Madera fina");
        Assert.Equal("Madera", assigned.CategoryName);
        Assert.Equal(1, assigned.NetUnits);
        Assert.Equal(HistoricalDataQuality.Exact, assigned.SnapshotQuality);
        Assert.Contains(subcategories, row => row.SubcategoryName == "Sin subcategoría");
        Assert.Contains(subcategories, row => row.SubcategoryName == "No disponible (histórico)");

        var filtered = await fixture.Service.GetCategoriesAsync(new AnalyticsQueryRequest(
            new AnalyticsPeriodRequest(AnalyticsPeriodKind.CustomRange, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            SubcategoryId: assigned.SubcategoryIdSnapshot));
        Assert.All(filtered.Subcategories!, row => Assert.Equal(assigned.SubcategoryIdSnapshot, row.SubcategoryIdSnapshot));
        Assert.Equal(950, filtered.Subcategories!.Single().NetSalesCentavos);
    }

    [Fact]
    public async Task CategoryAnalytics_DistinguishesExplicitUnassignedFromHistoricalUnavailable()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var period = new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.CustomRange,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));

        var explicitlyUnassigned = await fixture.Service.GetCategoriesAsync(new AnalyticsQueryRequest(
            period,
            SubcategoryFilter: AnalyticsSubcategoryFilter.ExplicitlyUnassigned));
        Assert.NotEmpty(explicitlyUnassigned.Subcategories!);
        Assert.All(explicitlyUnassigned.Subcategories!, row =>
            Assert.Equal(AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel, row.SubcategoryName));

        var historicalUnavailable = await fixture.Service.GetCategoriesAsync(new AnalyticsQueryRequest(
            period,
            SubcategoryFilter: AnalyticsSubcategoryFilter.HistoricalUnavailable));
        Assert.NotEmpty(historicalUnavailable.Subcategories!);
        Assert.All(historicalUnavailable.Subcategories!, row =>
            Assert.Equal(AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel, row.SubcategoryName));
    }

    [Fact]
    public async Task MultipleYears_ProducesOneBucketPerYearAndEquivalentComparison()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var request = new AnalyticsQueryRequest(new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.MultipleYears,
            StartYear: 2026,
            EndYear: 2027));

        var report = await fixture.Service.GetSalesAsync(request);

        Assert.Equal(AnalyticsGrouping.Year, report.Period.Grouping);
        Assert.Equal(["2026", "2027"], report.Trend.Select(point => point.Label).ToArray());
        Assert.Equal(2, report.ComparedPeriod.LocalEndExclusive.Year - report.ComparedPeriod.LocalStart.Year);
    }

    [Fact]
    public async Task EmptyPeriod_ReturnsZerosAndNoSyntheticHistoricalCoverage()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var request = new AnalyticsQueryRequest(new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.CustomRange,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31)));

        var report = await fixture.Service.GetSalesAsync(request);

        Assert.Equal(0, report.Summary.NetSales.Value);
        Assert.Equal(MetricComparisonState.NoChange, report.Summary.NetSales.Comparison.State);
        Assert.Equal(0, report.Coverage.ProductSnapshots.TotalRecords);
        Assert.Equal(HistoricalDataQuality.Unavailable, report.Coverage.ProductSnapshots.Quality);
    }

    [Fact]
    public async Task AnalyticsQueries_AreReadOnly()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var before = await fixture.CountRowsAsync();

        await fixture.Service.GetSalesAsync(CustomYear(2026));
        await fixture.Service.GetProductsAsync(CustomYear(2026));
        await fixture.Service.GetCategoriesAsync(CustomYear(2026));

        Assert.Equal(before, await fixture.CountRowsAsync());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ProductTrend_UsesHistoricalLineValuesAndFillsEmptyMonths()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();

        var report = await fixture.Service.GetProductTrendAsync(fixture.ProductAId, CustomYear(2026));

        Assert.Equal(12, report.Trend.Count);
        Assert.Equal(1900, report.Trend.Single(point => point.Label.StartsWith("feb", StringComparison.OrdinalIgnoreCase)).NetSalesCentavos);
        Assert.Equal(-950, report.Trend.Single(point => point.Label.StartsWith("mar", StringComparison.OrdinalIgnoreCase)).NetSalesCentavos);
    }

    private static AnalyticsQueryRequest CustomYear(int year) => new(new AnalyticsPeriodRequest(
        AnalyticsPeriodKind.CustomRange,
        new DateOnly(year, 1, 1),
        new DateOnly(year, 12, 31)));

    private sealed class AnalyticsFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private AnalyticsFixture(
            SqliteConnection connection,
            AppDbContext context,
            SalesAnalyticsQueryService service,
            int productAId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            ProductAId = productAId;
        }

        public AppDbContext Context { get; }
        public SalesAnalyticsQueryService Service { get; }
        public int ProductAId { get; }

        public static async Task<AnalyticsFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = new Usuario
            {
                NombreCompleto = "Admin analitica",
                NombreUsuario = "admin-analytics",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var categoryA = new Categoria { Nombre = "Madera", Activa = true, FechaCreacion = DateTime.UtcNow };
            var categoryB = new Categoria { Nombre = "Textil", Activa = true, FechaCreacion = DateTime.UtcNow };
            context.AddRange(admin, categoryA, categoryB);
            await context.SaveChangesAsync();

            var subcategoryA = new Subcategoria
            {
                CategoriaId = categoryA.CategoriaId,
                Nombre = "Madera fina",
                Activa = true,
                FechaCreacion = DateTime.UtcNow,
                FechaModificacion = DateTime.UtcNow
            };
            context.Subcategorias.Add(subcategoryA);
            var productA = Product(categoryA.CategoriaId, "A-001", "Producto A", 40);
            var productB = Product(categoryB.CategoriaId, "B-001", "Producto B", 30);
            var noSales = Product(categoryA.CategoriaId, "N-001", "Sin ventas", 12);
            context.AddRange(productA, productB, noSales);
            var cash = new Caja
            {
                FechaOperacion = new DateOnly(2026, 1, 1),
                FechaApertura = Utc(2026, 1, 1),
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.CERRADA,
                UsuarioAperturaId = admin.UsuarioId,
                UsuarioCierreId = admin.UsuarioId,
                FechaCierre = Utc(2026, 1, 1, 18)
            };
            context.Cajas.Add(cash);
            context.ConfiguracionSistema.AddRange(
                new ConfiguracionSistema
                {
                    Clave = DashboardQueryService.BusinessTimeZoneKey,
                    Valor = DashboardQueryService.DefaultBusinessTimeZoneId,
                    FechaModificacion = DateTime.UtcNow
                },
                new ConfiguracionSistema
                {
                    Clave = "analytics.snapshots_ventas_desde_utc",
                    Valor = "2026-02-01T00:00:00.000Z",
                    FechaModificacion = DateTime.UtcNow
                });
            await context.SaveChangesAsync();
            productA.SubcategoriaId = subcategoryA.SubcategoriaId;
            await context.SaveChangesAsync();

            var sale1 = Sale(cash.CajaId, admin.UsuarioId, "VTA-2026-1", Utc(2026, 2, 15), 2000, 100, 1900);
            var sale2 = Sale(cash.CajaId, admin.UsuarioId, "VTA-2026-2", Utc(2026, 8, 1), 3000, 0, 3000);
            var legacy = Sale(cash.CajaId, admin.UsuarioId, "VTA-2026-OLD", Utc(2026, 4, 1), 1000, 0, 1000);
            var sale2027A = Sale(cash.CajaId, admin.UsuarioId, "VTA-2027-1", Utc(2027, 2, 1), 1000, 0, 1000);
            var sale2027B = Sale(cash.CajaId, admin.UsuarioId, "VTA-2027-2", Utc(2027, 5, 1), 1000, 0, 1000);
            context.AddRange(sale1, sale2, legacy, sale2027A, sale2027B);
            await context.SaveChangesAsync();

            var line1 = SnapshotLine(sale1.VentaId, productA, categoryA, 2, 2000, 100, 1900, subcategoryA);
            var line2 = SnapshotLine(sale2.VentaId, productB, categoryB, 3, 3000, 0, 3000, subcategory: null, subcategoryName: "Sin subcategoría");
            var legacyLine = new DetalleVenta
            {
                VentaId = legacy.VentaId,
                ProductoId = productA.ProductoId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 1000,
                SubtotalCentavos = 1000,
                DescuentoCentavos = 0,
                DescuentoGeneralProporcionalCentavos = null,
                TotalCentavos = 1000,
                CostoUnitarioCentavos = null,
                OrigenCosto = OrigenCostoHistorico.DESCONOCIDO
            };
            var line2027A = SnapshotLine(sale2027A.VentaId, productA, categoryA, 1, 1000, 0, 1000, subcategoryA);
            var line2027B = SnapshotLine(sale2027B.VentaId, productB, categoryB, 1, 1000, 0, 1000, subcategory: null, subcategoryName: "Sin subcategoría");
            context.AddRange(line1, line2, legacyLine, line2027A, line2027B);
            await context.SaveChangesAsync();

            var returned = new Devolucion
            {
                NumeroDevolucion = "DEV-2026-1",
                VentaId = sale1.VentaId,
                UsuarioId = admin.UsuarioId,
                FechaDevolucion = Utc(2026, 3, 1),
                TipoCorreccion = TipoCorreccionVenta.DEVOLUCION,
                TotalDevueltoCentavos = 950,
                Motivo = "Prueba analitica"
            };
            context.Devoluciones.Add(returned);
            await context.SaveChangesAsync();
            context.DetalleDevoluciones.Add(new DetalleDevolucion
            {
                DevolucionId = returned.DevolucionId,
                DetalleVentaId = line1.DetalleVentaId,
                ProductoId = productA.ProductoId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 1000,
                DescuentoProporcionalCentavos = 50,
                SubtotalDevueltoCentavos = 950,
                CondicionProducto = CondicionProductoDevuelto.BUENO,
                ReintegraStock = false
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var session = new AnalyticsSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            var service = new SalesAnalyticsQueryService(
                context,
                session,
                new FixedTimeProvider(new DateTimeOffset(2027, 8, 15, 18, 0, 0, TimeSpan.Zero)));
            return new AnalyticsFixture(connection, context, service, productA.ProductoId);
        }

        public async Task<(int Sales, int Lines, int Returns, int Changes, int Audit)> CountRowsAsync() => (
            await Context.Ventas.CountAsync(),
            await Context.DetalleVentas.CountAsync(),
            await Context.Devoluciones.CountAsync(),
            await Context.DetallesCambioVenta.CountAsync(),
            await Context.Bitacora.CountAsync());

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Producto Product(int categoryId, string code, string name, int stock) => new()
        {
            CategoriaId = categoryId,
            CodigoInterno = code,
            Nombre = name,
            PrecioCompraCentavos = 500,
            PrecioVentaCentavos = 1000,
            StockActual = stock,
            StockMinimo = 1,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        };

        private static Venta Sale(
            int cashId,
            int userId,
            string number,
            DateTime date,
            long subtotal,
            long discount,
            long total) => new()
        {
            NumeroVenta = number,
            CajaId = cashId,
            UsuarioId = userId,
            UsuarioNombreSnapshot = "Admin analitica",
            ClienteNombreSnapshot = "Consumidor final",
            FechaVenta = date,
            SubtotalCentavos = subtotal,
            DescuentoCentavos = discount,
            TotalCentavos = total,
            PagadoCentavos = total,
            CambioCentavos = 0,
            Estado = EstadoVenta.COMPLETADA
        };

        private static DetalleVenta SnapshotLine(
            int saleId,
            Producto product,
            Categoria category,
            int quantity,
            long subtotal,
            long discount,
            long total,
            Subcategoria? subcategory = null,
            string? subcategoryName = null) => new()
        {
            VentaId = saleId,
            ProductoId = product.ProductoId,
            Cantidad = quantity,
            PrecioUnitarioCentavos = subtotal / quantity,
            CostoUnitarioCentavos = product.PrecioCompraCentavos,
            OrigenCosto = OrigenCostoHistorico.PRODUCTO_ACTUAL,
            ProductoCodigoSnapshot = product.CodigoInterno,
            ProductoNombreSnapshot = product.Nombre,
            CategoriaIdSnapshot = category.CategoriaId,
            CategoriaNombreSnapshot = category.Nombre,
            SubcategoriaIdSnapshot = subcategory?.SubcategoriaId,
            SubcategoriaNombreSnapshot = subcategoryName ?? subcategory?.Nombre,
            SubtotalCentavos = subtotal,
            DescuentoCentavos = discount,
            DescuentoMotivo = discount > 0 ? "Promocion historica" : null,
            DescuentoGeneralProporcionalCentavos = 0,
            TotalCentavos = total
        };

        private static DateTime Utc(int year, int month, int day, int hour = 12) =>
            new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AnalyticsSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }
}
