using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class FinancialAnalyticsQueryServiceTests
{
    [Fact]
    public async Task Finance_UsesCoveredSalesAndHistoricalCostsWithoutExtrapolation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(13_800, report.Finance.NetSales.Value);
        Assert.Equal(8_800, report.Finance.CoveredNetSales.Value);
        Assert.Equal(2_700, report.Finance.CostOfGoodsSold.Value);
        Assert.Equal(6_100, report.Finance.GrossProfit.Value);
        Assert.Equal(69.32m, report.Finance.GrossMargin.Value);
        Assert.Equal(8, report.Finance.CostCoverage.CompleteRecords);
        Assert.Equal(9, report.Finance.CostCoverage.TotalRecords);
        Assert.Equal(88.89m, report.Finance.CostCoverage.Percentage);
        Assert.Equal(HistoricalDataQuality.Partial, report.Finance.CostCoverage.Quality);
    }

    [Fact]
    public async Task Finance_KnownZeroCostIsCoveredAndGeneralDiscountIsPreserved()
    {
        await using var fixture = await Fixture.CreateAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(8_800, report.Finance.Trend.Single(point => point.Label.StartsWith("ene")).CoveredNetSalesCentavos);
        Assert.True(report.Finance.CostCoverage.CompleteRecords > 0);
        Assert.DoesNotContain(double.NaN.ToString(), report.Finance.GrossMargin.Value?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Finance_ReturnsReverseCostOnlyWhenStockReturnsAndChangesUseIndependentCost()
    {
        await using var fixture = await Fixture.CreateAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        // Base COGS 3,300; stock returns reverse 1,800; replacement adds 1,200.
        // The damaged return does not restore inventory and therefore does not reverse its 500 cost.
        Assert.Equal(2_700, report.Finance.CostOfGoodsSold.Value);
        Assert.Equal(6_100, report.Finance.GrossProfit.Value);
    }

    [Fact]
    public async Task Expenses_KeepLegacyCategoryAndWithdrawalsSeparate()
    {
        await using var fixture = await Fixture.CreateAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(1_000, report.Finance.OperatingExpenses.Value);
        var legacy = Assert.Single(report.Finance.Expenses, expense => expense.Description == "Gasto historico");
        Assert.Equal("Sin categoría", legacy.Category);
        Assert.Equal("Método no especificado", legacy.PaymentMethod);
        Assert.Equal(900, report.Cash.WithdrawalsCentavos);
        Assert.NotEqual(report.Finance.OperatingExpenses.Value + report.Cash.WithdrawalsCentavos,
            report.Finance.OperatingExpenses.Value);
    }

    [Fact]
    public async Task Purchases_SeparateCommittedReceivedPaidAndCurrentInventory()
    {
        await using var fixture = await Fixture.CreateAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(11_000, report.Purchases.CommittedPurchases.Value);
        Assert.Equal(5_500, report.Purchases.ReceivedMerchandise.Value);
        Assert.Equal(4_000, report.Purchases.SupplierPayments.Value);
        Assert.Equal(17_200, report.Purchases.CurrentInventoryValueCentavos);
        Assert.Equal(50m, report.Purchases.ReceptionSnapshotCoverage.Percentage);
        Assert.NotEqual(report.Purchases.ReceivedMerchandise.Value, report.Purchases.SupplierPayments.Value);
    }

    [Fact]
    public async Task Cash_ReconcilesPhysicalMovementsWithoutTreatingTransferExpenseAsCash()
    {
        await using var fixture = await Fixture.CreateAsync();

        var cash = (await fixture.Service.GetAsync(Year2026())).Cash;

        Assert.Equal(9_000, cash.CashSalesCentavos);
        Assert.Equal(500, cash.CashCustomerPaymentsCentavos);
        Assert.Equal(4_000, cash.SupplierCashPaymentsCentavos);
        Assert.Equal(300, cash.ExpensesCentavos);
        Assert.Equal(900, cash.WithdrawalsCentavos);
        Assert.Equal(11_600, cash.ExpectedCashCentavos);
        Assert.Equal(0, cash.ClosingDifferenceCentavos);
    }

    [Fact]
    public async Task Cash_TracksCashContributionsSeparatelyWithoutChangingCommercialProfit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashBox = await fixture.Context.Cajas.SingleAsync();
        var adminId = await fixture.Context.Usuarios
            .Where(user => user.NombreUsuario == "admin-fin")
            .Select(user => user.UsuarioId)
            .SingleAsync();
        cashBox.MontoCierreCentavos += 3_500;
        fixture.Context.AportesCaja.Add(new AporteCaja
        {
            CajaId = cashBox.CajaId,
            UsuarioId = adminId,
            MontoCentavos = 3_500,
            Motivo = "Fondo para cambio",
            FechaAporte = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc)
        });
        await fixture.Context.SaveChangesAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(3_500, report.Cash.CashContributionsCentavos);
        Assert.Equal(15_100, report.Cash.ExpectedCashCentavos);
        Assert.Equal(5_100, report.Cash.NetMovementCentavos);
        Assert.Equal(0, report.Cash.ClosingDifferenceCentavos);
        Assert.Equal(13_800, report.Finance.NetSales.Value);
        Assert.Equal(6_100, report.Finance.GrossProfit.Value);
    }

    [Fact]
    public async Task ExternalExpense_RemainsBusinessExpenseButDoesNotReducePhysicalCash()
    {
        await using var fixture = await Fixture.CreateAsync();
        var adminId = await fixture.Context.Usuarios
            .Where(user => user.NombreUsuario == "admin-fin")
            .Select(user => user.UsuarioId)
            .SingleAsync();
        fixture.Context.Gastos.Add(new Gasto
        {
            CajaId = null,
            UsuarioId = adminId,
            Concepto = "Insumos pagados por el dueño",
            MontoCentavos = 2_500,
            FechaGasto = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            MetodoPago = MetodoPago.EFECTIVO,
            OrigenFondos = OrigenFondos.FONDOS_EXTERNOS_DUENO,
            ResponsableOrigen = "Dueño",
            OperacionId = Guid.NewGuid()
        });
        await fixture.Context.SaveChangesAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(3_500, report.Finance.OperatingExpenses.Value);
        Assert.Equal(300, report.Cash.ExpensesCentavos);
        Assert.Equal(2_500, report.Cash.ExpensesOutsideCashCentavos);
        Assert.Equal(11_600, report.Cash.ExpectedCashCentavos);
        var expense = Assert.Single(report.Finance.Expenses, row => row.Description == "Insumos pagados por el dueño");
        Assert.Equal("Fondos externos del dueño", expense.FundingOrigin);
        Assert.Equal("Dueño", expense.FundingResponsible);
        Assert.False(expense.AffectsCash);
    }

    [Fact]
    public async Task ReceivablesAndPayables_ExposeBalancesPaymentsOverdueAndAging()
    {
        await using var fixture = await Fixture.CreateAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(2_500, report.Receivables.OutstandingBalanceCentavos);
        Assert.Equal(4_000, report.Receivables.CreatedCentavos);
        Assert.Equal(1_500, report.Receivables.PaymentsCentavos);
        Assert.Equal(1, report.Receivables.PaidAccounts);
        Assert.Equal(1, report.Receivables.OverdueAccounts);
        Assert.Equal(2_500, report.Receivables.Aging.Sum(bucket => bucket.BalanceCentavos));

        Assert.Equal(7_000, report.Payables.OutstandingBalanceCentavos);
        Assert.Equal(11_000, report.Payables.CreatedCentavos);
        Assert.Equal(4_000, report.Payables.PaymentsCentavos);
        Assert.Equal(1, report.Payables.PaidAccounts);
        Assert.Equal(1, report.Payables.OverdueAccounts);
    }

    [Fact]
    public async Task EmptyPeriod_HasNoMarginAndNeverProducesInfinity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new AnalyticsQueryRequest(new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.CustomRange,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31)));

        var report = await fixture.Service.GetAsync(request);

        Assert.Null(report.Finance.GrossMargin.Value);
        Assert.Equal(0, report.Finance.GrossProfit.Value);
        Assert.Equal(HistoricalDataQuality.Unavailable, report.Finance.CostCoverage.Quality);
    }

    [Fact]
    public async Task Query_IsReadOnly_Cancellable_AndAdminOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM bitacora").SingleAsync();

        await fixture.Service.GetAsync(Year2026());
        Assert.Equal(before, await fixture.Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM bitacora").SingleAsync());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.GetAsync(Year2026(), cts.Token));

        fixture.Session.IniciarSesion(fixture.EmployeeSession);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetAsync(Year2026()));
    }

    [Fact]
    public void ReceptionDiscountAllocation_PreservesEveryCentAcrossPartialReceipts()
    {
        var first = PurchaseReceptionService.AllocateDiscountToUnitRange(5, 3, 0, 1);
        var second = PurchaseReceptionService.AllocateDiscountToUnitRange(5, 3, 1, 1);
        var third = PurchaseReceptionService.AllocateDiscountToUnitRange(5, 3, 2, 1);

        Assert.Equal(5, first + second + third);
        Assert.Equal([2L, 2L, 1L], [first, second, third]);
    }

    [Theory]
    [InlineData(AnalyticsPeriodKind.CurrentQuarter, AnalyticsGrouping.Month)]
    [InlineData(AnalyticsPeriodKind.CurrentSemester, AnalyticsGrouping.Month)]
    [InlineData(AnalyticsPeriodKind.CurrentYear, AnalyticsGrouping.Month)]
    public async Task FinancialPeriods_ReuseCanonicalQuarterSemesterAndYearGrouping(
        AnalyticsPeriodKind kind,
        AnalyticsGrouping expectedGrouping)
    {
        await using var fixture = await Fixture.CreateAsync();

        var report = await fixture.Service.GetAsync(new AnalyticsQueryRequest(new AnalyticsPeriodRequest(kind)));

        Assert.Equal(expectedGrouping, report.Period.Grouping);
        Assert.Equal(report.Period.Grouping, report.Finance.Trend.Count == 0
            ? report.Period.Grouping
            : expectedGrouping);
    }

    private static AnalyticsQueryRequest Year2026() => new(new AnalyticsPeriodRequest(
        AnalyticsPeriodKind.CustomRange,
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 12, 31)));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            AppDbContext context,
            FinancialAnalyticsQueryService service,
            MutableSession session,
            UsuarioSesion employeeSession)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Session = session;
            EmployeeSession = employeeSession;
        }

        public AppDbContext Context { get; }
        public FinancialAnalyticsQueryService Service { get; }
        public MutableSession Session { get; }
        public UsuarioSesion EmployeeSession { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = User("admin-fin", RolUsuario.ADMIN);
            var employee = User("employee-fin", RolUsuario.EMPLEADO);
            context.AddRange(admin, employee);
            await context.SaveChangesAsync();
            var session = new MutableSession(ToSession(admin));

            var category = new Categoria { Nombre = "Arte", Activa = true, FechaCreacion = Utc(2025, 1, 1) };
            var expenseCategory = new CategoriaGasto { Nombre = "Transporte", Activa = true, FechaCreacion = Utc(2026, 1, 1) };
            var client = new Cliente
            {
                CodigoCliente = "CLI-001",
                NombreCompleto = "Cliente financiero",
                NombreNormalizado = "CLIENTE FINANCIERO",
                Estado = EstadoCliente.ACTIVO,
                UsuarioCreacionId = admin.UsuarioId,
                FechaCreacion = Utc(2025, 1, 1),
                FechaActualizacion = Utc(2025, 1, 1)
            };
            var supplier = new Proveedor
            {
                CodigoProveedor = "PRV-001",
                NombreRazonSocial = "Proveedor financiero",
                NombreNormalizado = "PROVEEDOR FINANCIERO",
                Estado = EstadoProveedor.ACTIVO,
                UsuarioCreacionId = admin.UsuarioId,
                FechaCreacion = Utc(2025, 1, 1),
                FechaActualizacion = Utc(2025, 1, 1)
            };
            context.AddRange(category, expenseCategory, client, supplier);
            await context.SaveChangesAsync();

            var productA = Product(category.CategoriaId, "A", "Producto A", 10, 1_000);
            var productZero = Product(category.CategoriaId, "B", "Producto costo cero", 5, 0);
            var productOriginal = Product(category.CategoriaId, "C", "Producto original", 3, 800);
            var productReplacement = Product(category.CategoriaId, "D", "Producto sustituto", 4, 1_200);
            context.AddRange(productA, productZero, productOriginal, productReplacement);
            var cash = new Caja
            {
                FechaOperacion = new DateOnly(2026, 1, 1),
                FechaApertura = Utc(2026, 1, 1, 8),
                FechaCierre = Utc(2026, 12, 31, 18),
                MontoInicialCentavos = 10_000,
                MontoCierreCentavos = 11_600,
                Estado = EstadoCaja.CERRADA,
                UsuarioAperturaId = admin.UsuarioId,
                UsuarioCierreId = admin.UsuarioId
            };
            context.Cajas.Add(cash);
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
                    FechaModificacion = Utc(2026, 1, 1)
                },
                new ConfiguracionSistema
                {
                    Clave = FinancialAnalyticsQueryService.ReceptionSnapshotStartKey,
                    Valor = "2026-01-01T00:00:00.000Z",
                    FechaModificacion = Utc(2026, 1, 1)
                });
            await context.SaveChangesAsync();

            var sale = Sale(cash.CajaId, admin.UsuarioId, "VTA-KNOWN", Utc(2026, 1, 10), 10_000, 1_000, 9_000);
            var unknownSale = Sale(cash.CajaId, admin.UsuarioId, "VTA-UNKNOWN", Utc(2026, 1, 11), 5_000, 0, 5_000);
            var changedSale = Sale(cash.CajaId, admin.UsuarioId, "VTA-CHANGE", Utc(2026, 1, 9), 2_000, 0, 2_000);
            var damagedSale = Sale(cash.CajaId, admin.UsuarioId, "VTA-DAMAGE", Utc(2026, 1, 8), 1_000, 0, 1_000);
            var creditSaleA = Sale(cash.CajaId, admin.UsuarioId, "VTA-CREDIT-A", Utc(2025, 12, 1), 3_000, 0, 3_000);
            var creditSaleB = Sale(cash.CajaId, admin.UsuarioId, "VTA-CREDIT-B", Utc(2025, 12, 2), 1_000, 0, 1_000);
            context.AddRange(sale, unknownSale, changedSale, damagedSale, creditSaleA, creditSaleB);
            await context.SaveChangesAsync();

            var lineA = Line(sale.VentaId, productA, 2, 6_000, 600, 1_000, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            var lineZero = Line(sale.VentaId, productZero, 1, 4_000, 400, 0, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            var unknownLine = Line(unknownSale.VentaId, productA, 1, 5_000, 0, null, OrigenCostoHistorico.DESCONOCIDO);
            var originalLine = Line(changedSale.VentaId, productOriginal, 1, 2_000, 0, 800, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            var damagedLine = Line(damagedSale.VentaId, productA, 1, 1_000, 0, 500, OrigenCostoHistorico.PRODUCTO_ACTUAL);
            context.AddRange(lineA, lineZero, unknownLine, originalLine, damagedLine);
            await context.SaveChangesAsync();

            var normalReturn = Return(sale.VentaId, admin.UsuarioId, "DEV-NORMAL", Utc(2026, 1, 12), 2_700, TipoCorreccionVenta.DEVOLUCION);
            var change = Return(changedSale.VentaId, admin.UsuarioId, "DEV-CHANGE", Utc(2026, 1, 13), 2_000, TipoCorreccionVenta.CAMBIO);
            var damagedReturn = Return(damagedSale.VentaId, admin.UsuarioId, "DEV-DAMAGE", Utc(2026, 1, 14), 1_000, TipoCorreccionVenta.DEVOLUCION);
            context.AddRange(normalReturn, change, damagedReturn);
            await context.SaveChangesAsync();
            context.DetalleDevoluciones.AddRange(
                ReturnLine(normalReturn.DevolucionId, lineA, productA, 1, 2_700, true),
                ReturnLine(change.DevolucionId, originalLine, productOriginal, 1, 2_000, true),
                ReturnLine(damagedReturn.DevolucionId, damagedLine, productA, 1, 1_000, false));
            var changeMovement = Movement(productReplacement, admin.UsuarioId, -1, 4, 3, Utc(2026, 1, 13));
            context.MovimientosInventario.Add(changeMovement);
            await context.SaveChangesAsync();
            context.DetallesCambioVenta.Add(new DetalleCambioVenta
            {
                DevolucionId = change.DevolucionId,
                DetalleVentaOrigenId = originalLine.DetalleVentaId,
                ProductoNuevoId = productReplacement.ProductoId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 2_500,
                CostoUnitarioCentavos = 1_200,
                OrigenCosto = OrigenCostoHistorico.PRODUCTO_ACTUAL,
                ProductoCodigoSnapshot = productReplacement.CodigoInterno,
                ProductoNombreSnapshot = productReplacement.Nombre,
                CategoriaIdSnapshot = category.CategoriaId,
                CategoriaNombreSnapshot = category.Nombre,
                SubtotalCentavos = 2_500,
                DescuentoCentavos = 0,
                TotalCentavos = 2_500,
                MovimientoInventarioId = changeMovement.MovimientoInventarioId
            });

            context.PagosVenta.Add(new PagoVenta
            {
                VentaId = sale.VentaId,
                MetodoPago = MetodoPago.EFECTIVO,
                MontoCentavos = 9_000,
                FechaPago = Utc(2026, 1, 10),
                Origen = OrigenPagoVenta.VENTA,
                AfectaCaja = true
            });
            context.Reembolsos.Add(new Reembolso
            {
                DevolucionId = normalReturn.DevolucionId,
                UsuarioId = admin.UsuarioId,
                CajaId = cash.CajaId,
                MetodoReembolso = MetodoReembolso.EFECTIVO,
                MontoCentavos = 2_700,
                Motivo = "Devolucion",
                FechaReembolso = Utc(2026, 1, 12)
            });
            context.Gastos.AddRange(
                new Gasto
                {
                    CajaId = cash.CajaId,
                    UsuarioId = admin.UsuarioId,
                    Concepto = "Gasto historico",
                    MontoCentavos = 300,
                    FechaGasto = Utc(2026, 1, 15)
                },
                new Gasto
                {
                    CajaId = cash.CajaId,
                    UsuarioId = admin.UsuarioId,
                    CategoriaGastoId = expenseCategory.CategoriaGastoId,
                    Concepto = "Envio",
                    MontoCentavos = 700,
                    MetodoPago = MetodoPago.TRANSFERENCIA,
                    Referencia = "TRX-1",
                    FechaGasto = Utc(2026, 1, 16)
                });
            context.RetirosCaja.Add(new RetiroCaja
            {
                CajaId = cash.CajaId,
                UsuarioId = admin.UsuarioId,
                MontoCentavos = 900,
                Motivo = "Retiro no gasto",
                FechaRetiro = Utc(2026, 1, 17)
            });
            await context.SaveChangesAsync();

            var purchase = Purchase(supplier.ProveedorId, admin.UsuarioId, "CMP-1", 10_000, Utc(2026, 1, 2));
            var paidPurchase = Purchase(supplier.ProveedorId, admin.UsuarioId, "CMP-2", 1_000, Utc(2026, 1, 3));
            context.AddRange(purchase, paidPurchase);
            await context.SaveChangesAsync();
            var purchaseLine = new DetalleCompra
            {
                CompraId = purchase.CompraId,
                ProductoId = productA.ProductoId,
                CodigoProductoSnapshot = productA.CodigoInterno,
                NombreProductoSnapshot = productA.Nombre,
                CantidadComprada = 3,
                CantidadRecibida = 3,
                CostoUnitarioCentavos = 3_000,
                DescuentoLineaCentavos = 500,
                SubtotalCentavos = 8_500
            };
            context.DetalleCompras.Add(purchaseLine);
            await context.SaveChangesAsync();
            var reception = new RecepcionCompra
            {
                CodigoRecepcion = "REC-1",
                OperacionId = Guid.NewGuid(),
                CompraId = purchase.CompraId,
                UsuarioRecepcionId = admin.UsuarioId,
                FechaRecepcion = Utc(2026, 1, 4)
            };
            context.RecepcionesCompra.Add(reception);
            await context.SaveChangesAsync();
            var exactMovement = Movement(productA, admin.UsuarioId, 2, 8, 10, Utc(2026, 1, 4));
            var legacyMovement = Movement(productA, admin.UsuarioId, 1, 9, 10, Utc(2026, 1, 5));
            context.MovimientosInventario.AddRange(exactMovement, legacyMovement);
            await context.SaveChangesAsync();
            context.RecepcionCompraDetalles.AddRange(
                new RecepcionCompraDetalle
                {
                    RecepcionCompraId = reception.RecepcionCompraId,
                    DetalleCompraId = purchaseLine.DetalleCompraId,
                    ProductoId = productA.ProductoId,
                    CantidadRecibida = 2,
                    StockAnterior = 8,
                    StockNuevo = 10,
                    CostoUnitarioCentavos = 3_000,
                    CostoNetoUnitarioCentavos = 2_750,
                    DescuentoAsignadoCentavos = 500,
                    CostoPromedioAnteriorCentavos = 1_000,
                    CostoPromedioNuevoCentavos = 1_350,
                    MovimientoInventarioId = exactMovement.MovimientoInventarioId
                },
                new RecepcionCompraDetalle
                {
                    RecepcionCompraId = reception.RecepcionCompraId,
                    DetalleCompraId = purchaseLine.DetalleCompraId,
                    ProductoId = productA.ProductoId,
                    CantidadRecibida = 1,
                    StockAnterior = 9,
                    StockNuevo = 10,
                    CostoUnitarioCentavos = 2_000,
                    CostoPromedioAnteriorCentavos = 1_000,
                    CostoPromedioNuevoCentavos = 1_100,
                    MovimientoInventarioId = legacyMovement.MovimientoInventarioId
                });

            var payable = Payable(purchase, supplier, admin, "CPP-1", 10_000, 3_000, 7_000, Utc(2026, 1, 2), null);
            var paidPayable = Payable(paidPurchase, supplier, admin, "CPP-2", 1_000, 1_000, 0, Utc(2026, 1, 3), Utc(2026, 1, 3));
            context.AddRange(payable, paidPayable);
            await context.SaveChangesAsync();
            context.PagosCompra.AddRange(
                PurchasePayment(purchase, admin, cash, "PGC-1", 2_000, Utc(2026, 1, 6)),
                PurchasePayment(paidPurchase, admin, cash, "PGC-2", 1_000, Utc(2026, 1, 3)));
            context.AbonosCuentaPorPagar.Add(new AbonoCuentaPorPagar
            {
                CodigoAbono = "ABP-1",
                OperacionId = Guid.NewGuid(),
                CuentaPorPagarId = payable.CuentaPorPagarId,
                UsuarioId = admin.UsuarioId,
                CajaId = cash.CajaId,
                MetodoPago = MetodoPago.EFECTIVO,
                MontoCentavos = 1_000,
                SaldoAnteriorCentavos = 8_000,
                SaldoNuevoCentavos = 7_000,
                FechaAbono = Utc(2026, 1, 7)
            });

            var receivable = Receivable(creditSaleA, client, cash, admin, "CXC-1", 3_000, 500, 2_500, Utc(2026, 1, 1), null);
            var paidReceivable = Receivable(creditSaleB, client, cash, admin, "CXC-2", 1_000, 1_000, 0, Utc(2026, 1, 2), Utc(2026, 1, 5));
            context.AddRange(receivable, paidReceivable);
            await context.SaveChangesAsync();
            context.AbonosCuentaPorCobrar.AddRange(
                ReceivablePayment(receivable, admin, cash, "ABC-1", 500, 3_000, 2_500, Utc(2026, 1, 20), MetodoPago.EFECTIVO),
                ReceivablePayment(paidReceivable, admin, cash, "ABC-2", 1_000, 1_000, 0, Utc(2026, 1, 5), MetodoPago.TRANSFERENCIA));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var factory = new TestDbContextFactory(options);
            var sales = new SalesAnalyticsQueryService(
                context,
                session,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.Zero)));
            var service = new FinancialAnalyticsQueryService(factory, sales, session);
            return new Fixture(connection, context, service, session, ToSession(employee));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Usuario User(string name, RolUsuario role) => new()
        {
            NombreCompleto = name,
            NombreUsuario = name,
            PasswordHash = "hash",
            Rol = role,
            Activo = true,
            FechaCreacion = Utc(2025, 1, 1)
        };

        private static UsuarioSesion ToSession(Usuario user) =>
            new(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol);

        private static Producto Product(int categoryId, string code, string name, int stock, long cost) => new()
        {
            CategoriaId = categoryId,
            CodigoInterno = code,
            Nombre = name,
            StockActual = stock,
            StockMinimo = 0,
            PrecioCompraCentavos = cost,
            PrecioVentaCentavos = 5_000,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = Utc(2025, 1, 1)
        };

        private static Venta Sale(int cashId, int userId, string code, DateTime date, long gross, long discount, long total) => new()
        {
            NumeroVenta = code,
            CajaId = cashId,
            UsuarioId = userId,
            UsuarioNombreSnapshot = "admin-fin",
            ClienteNombreSnapshot = "Consumidor final",
            FechaVenta = date,
            SubtotalCentavos = gross,
            DescuentoCentavos = discount,
            TotalCentavos = total,
            PagadoCentavos = total,
            Estado = EstadoVenta.COMPLETADA
        };

        private static DetalleVenta Line(
            int saleId,
            Producto product,
            int quantity,
            long totalBeforeGeneral,
            long allocatedGeneral,
            long? cost,
            OrigenCostoHistorico origin) => new()
        {
            VentaId = saleId,
            ProductoId = product.ProductoId,
            Cantidad = quantity,
            PrecioUnitarioCentavos = totalBeforeGeneral / quantity,
            SubtotalCentavos = totalBeforeGeneral,
            DescuentoCentavos = 0,
            DescuentoGeneralProporcionalCentavos = allocatedGeneral,
            TotalCentavos = totalBeforeGeneral,
            CostoUnitarioCentavos = cost,
            OrigenCosto = origin,
            ProductoCodigoSnapshot = product.CodigoInterno,
            ProductoNombreSnapshot = product.Nombre,
            CategoriaIdSnapshot = product.CategoriaId,
            CategoriaNombreSnapshot = "Arte"
        };

        private static Devolucion Return(int saleId, int userId, string code, DateTime date, long amount, TipoCorreccionVenta type) => new()
        {
            NumeroDevolucion = code,
            VentaId = saleId,
            UsuarioId = userId,
            FechaDevolucion = date,
            TipoCorreccion = type,
            TotalDevueltoCentavos = amount,
            Motivo = "Prueba financiera"
        };

        private static DetalleDevolucion ReturnLine(
            int returnId,
            DetalleVenta saleLine,
            Producto product,
            int quantity,
            long amount,
            bool restocks) => new()
        {
            DevolucionId = returnId,
            DetalleVentaId = saleLine.DetalleVentaId,
            ProductoId = product.ProductoId,
            Cantidad = quantity,
            PrecioUnitarioCentavos = amount,
            DescuentoProporcionalCentavos = 0,
            SubtotalDevueltoCentavos = amount,
            CondicionProducto = CondicionProductoDevuelto.BUENO,
            ReintegraStock = restocks
        };

        private static MovimientoInventario Movement(
            Producto product,
            int userId,
            int quantity,
            int oldStock,
            int newStock,
            DateTime date) => new()
        {
            ProductoId = product.ProductoId,
            UsuarioId = userId,
            TipoMovimiento = quantity >= 0 ? TipoMovimientoInventario.ENTRADA_COMPRA : TipoMovimientoInventario.SALIDA,
            Cantidad = Math.Abs(quantity),
            StockAnterior = oldStock,
            StockNuevo = newStock,
            Motivo = "Prueba financiera",
            FechaMovimiento = date
        };

        private static Compra Purchase(int supplierId, int userId, string code, long total, DateTime date) => new()
        {
            CodigoCompra = code,
            ProveedorId = supplierId,
            UsuarioRegistroId = userId,
            FechaCompra = date,
            FechaRegistro = date,
            EstadoRecepcion = EstadoRecepcionCompra.RECIBIDA,
            SubtotalCentavos = total,
            DescuentoCentavos = 0,
            TotalCentavos = total,
            EstadoFinanciero = EstadoFinancieroCompra.PARCIAL,
            TotalPagadoCentavos = 0,
            SaldoPendienteCentavos = total,
            FechaActualizacion = date
        };

        private static CuentaPorPagar Payable(
            Compra purchase,
            Proveedor supplier,
            Usuario user,
            string code,
            long total,
            long paid,
            long balance,
            DateTime created,
            DateTime? paidDate) => new()
        {
            CodigoCuenta = code,
            CompraId = purchase.CompraId,
            ProveedorId = supplier.ProveedorId,
            UsuarioCreadorId = user.UsuarioId,
            TotalOriginalCentavos = total,
            TotalPagadoCentavos = paid,
            SaldoPendienteCentavos = balance,
            FechaCreacion = created,
            FechaVencimiento = Utc(2026, 1, 15),
            Estado = balance == 0 ? EstadoCuentaPorPagar.PAGADA : EstadoCuentaPorPagar.PARCIAL,
            FechaPago = paidDate,
            FechaActualizacion = created
        };

        private static PagoCompra PurchasePayment(
            Compra purchase,
            Usuario user,
            Caja cash,
            string code,
            long amount,
            DateTime date) => new()
        {
            CodigoPago = code,
            OperacionId = Guid.NewGuid(),
            CompraId = purchase.CompraId,
            UsuarioId = user.UsuarioId,
            CajaId = cash.CajaId,
            MetodoPago = MetodoPago.EFECTIVO,
            MontoCentavos = amount,
            FechaPago = date,
            FechaCreacion = date,
            EsPagoInicial = true
        };

        private static CuentaPorCobrar Receivable(
            Venta sale,
            Cliente client,
            Caja cash,
            Usuario user,
            string code,
            long total,
            long paid,
            long balance,
            DateTime created,
            DateTime? paidDate) => new()
        {
            CodigoCuenta = code,
            VentaId = sale.VentaId,
            ClienteId = client.ClienteId,
            CajaId = cash.CajaId,
            UsuarioRegistroId = user.UsuarioId,
            CapitalFinanciadoCentavos = total,
            InteresPorcentaje = 0,
            InteresCalculadoCentavos = 0,
            TotalOriginalCentavos = total,
            TotalPagadoCentavos = paid,
            SaldoActualCentavos = balance,
            FechaCreacion = created,
            FechaVencimiento = Utc(2026, 1, 15),
            Estado = balance == 0 ? EstadoCuentaPorCobrar.PAGADA : EstadoCuentaPorCobrar.ABIERTA,
            FechaPago = paidDate,
            FechaActualizacion = created
        };

        private static AbonoCuentaPorCobrar ReceivablePayment(
            CuentaPorCobrar account,
            Usuario user,
            Caja cash,
            string code,
            long amount,
            long previous,
            long next,
            DateTime date,
            MetodoPago method) => new()
        {
            CodigoAbono = code,
            OperacionId = Guid.NewGuid(),
            CuentaPorCobrarId = account.CuentaPorCobrarId,
            UsuarioId = user.UsuarioId,
            CajaId = cash.CajaId,
            MetodoPago = method,
            Referencia = method == MetodoPago.TRANSFERENCIA ? "TRX-CXC" : null,
            MontoCentavos = amount,
            AplicadoInteresCentavos = 0,
            AplicadoCapitalCentavos = amount,
            SaldoAnteriorCentavos = previous,
            SaldoNuevoCentavos = next,
            InteresPendienteAnteriorCentavos = 0,
            InteresPendienteNuevoCentavos = 0,
            CapitalPendienteAnteriorCentavos = previous,
            CapitalPendienteNuevoCentavos = next,
            FechaAbono = date
        };

        private static DateTime Utc(int year, int month, int day, int hour = 12) =>
            new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private sealed class MutableSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
