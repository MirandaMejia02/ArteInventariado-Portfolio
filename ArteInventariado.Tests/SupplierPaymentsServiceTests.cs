using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class SupplierPaymentsServiceTests
{
    private const string ExternalOwner = "Propietario - fondos propios";

    [Fact]
    public async Task InitialCashPayment_CanPayPurchaseCompletely()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.InitialAsync(1000, MetodoPago.EFECTIVO);
        Assert.Equal(EstadoFinancieroCompra.PAGADA, result.EstadoFinanciero);
        Assert.Equal(EstadoCuentaPorPagar.PAGADA, result.EstadoCuenta);
        Assert.Equal(0, result.SaldoPendienteCentavos);
    }

    [Fact]
    public async Task InitialPayment_WithFreshContext_ReturnsCurrentUserWithoutLoadedNavigation()
    {
        await using var f = await Fixture.CreateAsync();
        f.Context.ChangeTracker.Clear();

        var result = await f.InitialAsync(1, MetodoPago.EFECTIVO);

        var payment = Assert.Single(result.Pagos);
        Assert.Equal("tester", payment.Usuario);
        Assert.Equal(999, result.SaldoPendienteCentavos);
    }

    [Fact]
    public async Task InitialTransferPayment_CreatesPartialState()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.InitialAsync(400, MetodoPago.TRANSFERENCIA, "TRX-1");
        Assert.Equal(EstadoFinancieroCompra.PARCIAL, result.EstadoFinanciero);
        Assert.Equal(600, result.SaldoPendienteCentavos);
    }

    [Fact]
    public async Task InitialExternalPayment_ReducesDebtWithoutCash()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 0);
        var result = await f.InitialAsync(250, MetodoPago.TRANSFERENCIA, "TRX-EXT", origin: OrigenFondos.FONDOS_EXTERNOS_DUENO, responsible: ExternalOwner);
        Assert.Equal(750, result.SaldoPendienteCentavos);
        Assert.Equal(0, (await f.CashService.GetTodaySummaryAsync()).TotalPagosProveedoresCentavos);
        var payment = await f.Context.PagosCompra.AsNoTracking().SingleAsync();
        Assert.Null(payment.CajaId);
        Assert.Equal(OrigenFondos.FONDOS_EXTERNOS_DUENO, payment.OrigenFondos);
    }

    [Fact]
    public async Task InitialMixedPayment_UsesOriginPerLine()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 400);
        var result = await f.InitialService.RegisterInitialPaymentsAsync(new RegisterInitialPurchasePaymentsRequest(
            f.PurchaseId,
            DateTime.Today.AddDays(30),
            [
                new PurchasePaymentLineRequest(Guid.NewGuid(), MetodoPago.EFECTIVO, 200, OrigenFondos: OrigenFondos.CAJA_FISICA),
                new PurchasePaymentLineRequest(Guid.NewGuid(), MetodoPago.TRANSFERENCIA, 300, "TRX-EXT-1", OrigenFondos.FONDOS_EXTERNOS_DUENO, ExternalOwner)
            ]));

        Assert.Equal(500, result.SaldoPendienteCentavos);
        Assert.Equal(200, (await f.CashService.GetTodaySummaryAsync()).TotalPagosProveedoresCentavos);
        var payments = await f.Context.PagosCompra.AsNoTracking().OrderBy(p => p.PagoCompraId).ToListAsync();
        Assert.Equal(2, payments.Count);
        Assert.NotNull(payments[0].CajaId);
        Assert.Null(payments[1].CajaId);
    }

    [Fact]
    public async Task InitialWithoutPayment_LeavesPurchasePending()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.PendingAsync();
        Assert.Equal(EstadoFinancieroCompra.PENDIENTE, result.EstadoFinanciero);
        Assert.Equal(1000, result.SaldoPendienteCentavos);
    }

    [Fact]
    public async Task Purchase_HasSinglePayableAccount()
    {
        await using var f = await Fixture.CreateAsync();
        await f.PendingAsync();
        Assert.Equal(1, await f.Context.CuentasPorPagar.CountAsync(c => c.CompraId == f.PurchaseId));
    }

    [Fact]
    public async Task InitialPayment_RejectsOverpayment()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.InitialAsync(1001, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task SupplierPayment_RejectsOverpayment()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentAsync(1001, MetodoPago.EFECTIVO));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SupplierPayment_RejectsNonPositiveAmount(long amount)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.PaymentAsync(amount, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task InitialCashPayment_RequiresOpenCash()
    {
        await using var f = await Fixture.CreateAsync(cashOpen: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.InitialAsync(100, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task SupplierCashPayment_RequiresOpenCash()
    {
        await using var f = await Fixture.CreateAsync(cashOpen: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentAsync(100, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task CashPayments_ReduceExpectedCashExactlyOnce()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 5000);
        await f.InitialAsync(200, MetodoPago.EFECTIVO);
        await f.PaymentAsync(300, MetodoPago.EFECTIVO);
        var summary = await f.CashService.GetTodaySummaryAsync();
        Assert.Equal(500, summary.TotalPagosProveedoresCentavos);
        Assert.Equal(4500, summary.TotalEsperadoCentavos);
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA)]
    [InlineData(MetodoPago.BITCOIN)]
    public async Task NonCashInitialPayment_RequiresReference(MetodoPago method)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.InitialAsync(100, method));
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA)]
    [InlineData(MetodoPago.BITCOIN)]
    public async Task NonCashSupplierPayment_RequiresReference(MetodoPago method)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentAsync(100, method));
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA)]
    [InlineData(MetodoPago.BITCOIN)]
    public async Task NonCashPayment_DoesNotReduceExpectedCash(MetodoPago method)
    {
        await using var f = await Fixture.CreateAsync(initialCash: 5000);
        await f.PaymentAsync(100, method, "REF-1");
        var summary = await f.CashService.GetTodaySummaryAsync();
        Assert.Equal(0, summary.TotalPagosProveedoresCentavos);
        Assert.Equal(5000, summary.TotalEsperadoCentavos);
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA, "TRX-CAJA")]
    [InlineData(MetodoPago.BITCOIN, "BTC-CAJA")]
    public async Task InitialNonCashPayment_RejectsPhysicalCashFunding(MetodoPago method, string reference)
    {
        await using var f = await Fixture.CreateAsync(initialCash: 5000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.InitialAsync(
            100,
            method,
            reference,
            origin: OrigenFondos.CAJA_FISICA));

        Assert.Empty(await f.Context.PagosCompra.AsNoTracking().ToListAsync());
        Assert.Equal(5000, (await f.CashService.GetTodaySummaryAsync()).TotalEsperadoCentavos);
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA, "TRX-CAJA")]
    [InlineData(MetodoPago.BITCOIN, "BTC-CAJA")]
    public async Task SupplierNonCashPayment_RejectsPhysicalCashFunding(MetodoPago method, string reference)
    {
        await using var f = await Fixture.CreateAsync(initialCash: 5000);
        await f.PendingAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentAsync(
            100,
            method,
            reference,
            origin: OrigenFondos.CAJA_FISICA));

        Assert.Empty(await f.Context.AbonosCuentaPorPagar.AsNoTracking().ToListAsync());
        Assert.Equal(5000, (await f.CashService.GetTodaySummaryAsync()).TotalEsperadoCentavos);
    }

    [Fact]
    public async Task PartialSupplierPayment_UpdatesBalances()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.PaymentAsync(300, MetodoPago.TRANSFERENCIA, "TRX-2");
        Assert.Equal(1000, result.SaldoAnteriorCentavos);
        Assert.Equal(700, result.SaldoNuevoCentavos);
        var purchase = await f.Context.Compras.AsNoTracking().SingleAsync(c => c.CompraId == f.PurchaseId);
        Assert.Equal(EstadoFinancieroCompra.PARCIAL, purchase.EstadoFinanciero);
    }

    [Fact]
    public async Task FinalSupplierPayment_ClosesAccount()
    {
        await using var f = await Fixture.CreateAsync();
        await f.PaymentAsync(400, MetodoPago.TRANSFERENCIA, "TRX-A");
        await f.PaymentAsync(600, MetodoPago.BITCOIN, "BTC-B");
        var account = await f.Context.CuentasPorPagar.AsNoTracking().SingleAsync(c => c.CuentaPorPagarId == f.AccountId);
        Assert.Equal(EstadoCuentaPorPagar.PAGADA, account.Estado);
        Assert.NotNull(account.FechaPago);
    }

    [Fact]
    public async Task Query_ClassifiesOverdueAccount()
    {
        await using var f = await Fixture.CreateAsync(overdue: true);
        var rows = await f.Query.SearchAsync(new AccountsPayableFilter());
        Assert.Equal(EstadoCuentaPorPagar.VENCIDA, Assert.Single(rows).Estado);
    }

    [Fact]
    public async Task InitialPayment_ReplayDoesNotDuplicate()
    {
        await using var f = await Fixture.CreateAsync();
        var op = Guid.NewGuid();
        var first = await f.InitialAsync(300, MetodoPago.EFECTIVO, operationId: op);
        var second = await f.InitialAsync(300, MetodoPago.EFECTIVO, operationId: op);
        Assert.False(first.EsReintentoIdempotente);
        Assert.True(second.EsReintentoIdempotente);
        Assert.Equal(1, await f.Context.PagosCompra.CountAsync());
    }

    [Fact]
    public async Task InitialBatch_RejectsSameBatchIdWithDifferentLinePayload()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 500);
        var batchId = Guid.NewGuid();
        await f.InitialService.RegisterInitialPaymentsAsync(new RegisterInitialPurchasePaymentsRequest(
            f.PurchaseId, DateTime.Today.AddDays(30),
            [new PurchasePaymentLineRequest(Guid.NewGuid(), MetodoPago.EFECTIVO, 200, OrigenFondos: OrigenFondos.CAJA_FISICA)],
            OperacionId: batchId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.InitialService.RegisterInitialPaymentsAsync(new RegisterInitialPurchasePaymentsRequest(
            f.PurchaseId, DateTime.Today.AddDays(30),
            [new PurchasePaymentLineRequest(Guid.NewGuid(), MetodoPago.EFECTIVO, 201, OrigenFondos: OrigenFondos.CAJA_FISICA)],
            OperacionId: batchId)));
        Assert.Single(await f.Context.PagosCompra.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SupplierPayment_ReplayDoesNotDuplicate()
    {
        await using var f = await Fixture.CreateAsync();
        var op = Guid.NewGuid();
        var first = await f.PaymentAsync(300, MetodoPago.EFECTIVO, operationId: op);
        var second = await f.PaymentAsync(300, MetodoPago.EFECTIVO, operationId: op);
        Assert.False(first.EsReintentoIdempotente);
        Assert.True(second.EsReintentoIdempotente);
        Assert.Equal(1, await f.Context.AbonosCuentaPorPagar.CountAsync());
    }

    [Fact]
    public async Task SupplierPayment_RejectsConflictingOperationId()
    {
        await using var f = await Fixture.CreateAsync();
        var op = Guid.NewGuid();
        await f.PaymentAsync(300, MetodoPago.EFECTIVO, operationId: op);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentAsync(301, MetodoPago.EFECTIVO, operationId: op));
    }

    [Fact]
    public async Task EmployeeWithoutPermission_IsBlocked()
    {
        await using var f = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO, grantPermissions: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentAsync(100, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task EmployeeWithPermission_CanPay()
    {
        await using var f = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO, grantPermissions: true);
        var result = await f.PaymentAsync(100, MetodoPago.EFECTIVO);
        Assert.Equal(900, result.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task Admin_CanPayWithoutStoredPermission()
    {
        await using var f = await Fixture.CreateAsync(role: RolUsuario.ADMIN, grantPermissions: false);
        var result = await f.PaymentAsync(100, MetodoPago.EFECTIVO);
        Assert.Equal(900, result.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task InitialAndSupplierPayments_RegisterAuditEntries()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InitialAsync(100, MetodoPago.EFECTIVO);
        await f.PaymentAsync(100, MetodoPago.EFECTIVO);
        Assert.Contains(await f.Context.Bitacora.AsNoTracking().ToListAsync(), b => b.TipoAccion == TipoBitacora.PAGO_COMPRA_REGISTRADO);
        Assert.Contains(await f.Context.Bitacora.AsNoTracking().ToListAsync(), b => b.TipoAccion == TipoBitacora.ABONO_PROVEEDOR_REGISTRADO);
    }

    [Fact]
    public async Task Query_ReturnsInitialPaymentsAndAbonos()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InitialAsync(100, MetodoPago.TRANSFERENCIA, "TRX-I");
        await f.PaymentAsync(200, MetodoPago.BITCOIN, "BTC-A");
        var detail = await f.Query.GetDetailAsync(f.AccountId);
        Assert.Single(detail!.PagosIniciales);
        Assert.Single(detail.Abonos);
    }

    [Fact]
    public async Task PaymentHistory_CombinesInitialPaymentAndInstallmentWithoutDuplicates()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InitialAsync(100, MetodoPago.TRANSFERENCIA, "TRX-I");
        await f.PaymentAsync(200, MetodoPago.BITCOIN, "BTC-A");

        var history = await f.Query.GetPaymentHistoryAsync(new SupplierPaymentHistoryFilter(
            CuentaPorPagarId: f.AccountId));

        Assert.Equal(2, history.Count);
        Assert.Equal(2, history.Select(item => (item.EsPagoInicial, item.MovimientoId)).Distinct().Count());
        var initial = Assert.Single(history, item => item.EsPagoInicial);
        Assert.Equal("PAGO INICIAL", initial.Tipo);
        Assert.Equal(1000, initial.SaldoAnteriorCentavos);
        Assert.Equal(900, initial.SaldoNuevoCentavos);
        Assert.Equal("TRX-I", initial.Referencia);
        var installment = Assert.Single(history, item => !item.EsPagoInicial);
        Assert.Equal("ABONO", installment.Tipo);
        Assert.Equal(900, installment.SaldoAnteriorCentavos);
        Assert.Equal(700, installment.SaldoNuevoCentavos);
        Assert.Equal("BTC-A", installment.Referencia);
    }

    [Fact]
    public async Task Query_HidesTechnicalBackfillObservationFromUserFacingSummary()
    {
        await using var f = await Fixture.CreateAsync();
        var account = await f.Context.CuentasPorPagar.SingleAsync(item => item.CuentaPorPagarId == f.AccountId);
        account.Observaciones = "Backfill v1.6.0 Bloque C: compra previa sin pagos; vencimiento tecnico a 30 dias.";
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();

        var detail = await f.Query.GetDetailAsync(f.AccountId);

        Assert.Equal(
            "Cuenta generada automáticamente para una compra registrada previamente sin pagos.",
            detail!.Summary.Observaciones);
    }

    [Fact]
    public async Task ExternalOwnerPayment_ReducesDebtWithoutReducingCash()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 0);
        var result = await f.PaymentAsync(300, MetodoPago.EFECTIVO, origin: OrigenFondos.FONDOS_EXTERNOS_DUENO, responsible: ExternalOwner);

        Assert.Equal(700, result.SaldoNuevoCentavos);
        Assert.Equal(0, (await f.CashService.GetTodaySummaryAsync()).TotalPagosProveedoresCentavos);
        var payment = await f.Context.AbonosCuentaPorPagar.AsNoTracking().SingleAsync();
        Assert.Equal(OrigenFondos.FONDOS_EXTERNOS_DUENO, payment.OrigenFondos);
        Assert.Equal(ExternalOwner, payment.ResponsableOrigen);
        Assert.Null(payment.CajaId);
    }

    [Fact]
    public async Task MixedSupplierBatch_ReducesCashOnlyForCashLine()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 500);
        var operationId = Guid.NewGuid();
        var result = await f.PaymentBatchAsync(operationId,
        [
            new SupplierPaymentLineRequest(200, MetodoPago.EFECTIVO, OrigenFondos: OrigenFondos.CAJA_FISICA),
            new SupplierPaymentLineRequest(300, MetodoPago.TRANSFERENCIA, "TRX-MIX", OrigenFondos.FONDOS_EXTERNOS_DUENO, ExternalOwner)
        ]);

        Assert.Equal(500, result.SaldoNuevoCentavos);
        Assert.Equal(200, (await f.CashService.GetTodaySummaryAsync()).TotalPagosProveedoresCentavos);
        Assert.Equal(2, await f.Context.AbonosCuentaPorPagar.CountAsync());
        Assert.Equal(500, await f.Context.AbonosCuentaPorPagar.SumAsync(x => x.MontoCentavos));
    }

    [Fact]
    public async Task ExternalPayment_IsAllowedWhenCashIsClosed()
    {
        await using var f = await Fixture.CreateAsync(cashOpen: false);
        var result = await f.PaymentAsync(100, MetodoPago.TRANSFERENCIA, "EXT-CLOSED", origin: OrigenFondos.FONDOS_EXTERNOS_DUENO, responsible: ExternalOwner);
        Assert.Equal(900, result.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task InsufficientCash_RejectsAtomicallyWithoutPartialPayment()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentBatchAsync(Guid.NewGuid(),
        [
            new SupplierPaymentLineRequest(101, MetodoPago.EFECTIVO, OrigenFondos: OrigenFondos.CAJA_FISICA),
            new SupplierPaymentLineRequest(100, MetodoPago.TRANSFERENCIA, "EXT-100", OrigenFondos.FONDOS_EXTERNOS_DUENO, ExternalOwner)
        ]));
        Assert.Empty(await f.Context.AbonosCuentaPorPagar.AsNoTracking().ToListAsync());
        var account = await f.Context.CuentasPorPagar.AsNoTracking().SingleAsync();
        Assert.Equal(1000, account.SaldoPendienteCentavos);
    }

    [Fact]
    public async Task SupplierBatch_ReplayRequiresSamePayload()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 500);
        var operationId = Guid.NewGuid();
        var first = await f.PaymentBatchAsync(operationId,
        [new SupplierPaymentLineRequest(200, MetodoPago.EFECTIVO, OrigenFondos: OrigenFondos.CAJA_FISICA)]);
        var replay = await f.PaymentBatchAsync(operationId,
        [new SupplierPaymentLineRequest(200, MetodoPago.EFECTIVO, OrigenFondos: OrigenFondos.CAJA_FISICA)]);
        Assert.False(first.EsReintentoIdempotente);
        Assert.True(replay.EsReintentoIdempotente);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PaymentBatchAsync(operationId,
            [new SupplierPaymentLineRequest(201, MetodoPago.EFECTIVO, OrigenFondos: OrigenFondos.CAJA_FISICA)]));
        Assert.Single(await f.Context.AbonosCuentaPorPagar.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HistoricalPaymentWithoutOriginRemainsUnspecified()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Context.AbonosCuentaPorPagar.AddAsync(new AbonoCuentaPorPagar
        {
            CodigoAbono = "ABP-HIST-1",
            OperacionId = Guid.NewGuid(),
            CuentaPorPagarId = f.AccountId,
            UsuarioId = 1,
            MetodoPago = MetodoPago.EFECTIVO,
            MontoCentavos = 10,
            FechaAbono = DateTime.UtcNow,
            SaldoAnteriorCentavos = 1000,
            SaldoNuevoCentavos = 990
        });
        await f.Context.SaveChangesAsync();
        var detail = await f.Query.GetDetailAsync(f.AccountId);
        Assert.Null(Assert.Single(detail!.Abonos).OrigenFondos);
    }

    [Fact]
    public async Task ConcurrentCashPayments_OnlyOneCanConsumeTheAvailableBalance()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 100);
        await using var sibling = await f.CreateSiblingAsync();
        var first = Task.Run(() => f.PaymentAsync(100, MetodoPago.EFECTIVO));
        var second = Task.Run(() => sibling.PaymentAsync(100, MetodoPago.EFECTIVO));
        var results = await Task.WhenAll(Capture(first), Capture(second));

        Assert.Equal(1, results.Count(item => item.Success));
        Assert.Equal(1, results.Count(item => item.Error is not null));
        Assert.Single(await f.Context.AbonosCuentaPorPagar.AsNoTracking().ToListAsync());
        Assert.Equal(100, (await f.CashService.GetTodaySummaryAsync()).TotalPagosProveedoresCentavos);
    }

    [Fact]
    public async Task AuditFailure_RollsBackSupplierBatchCompletely()
    {
        await using var f = await Fixture.CreateAsync(initialCash: 500);
        var failing = f.CreatePaymentService(new ThrowingAuditService());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RegisterPaymentsAsync(new RegisterSupplierPaymentBatchRequest(
            Guid.NewGuid(),
            f.AccountId,
            [new SupplierPaymentLineRequest(200, MetodoPago.EFECTIVO, OrigenFondos: OrigenFondos.CAJA_FISICA)])));

        Assert.Empty(await f.Context.AbonosCuentaPorPagar.AsNoTracking().ToListAsync());
        var account = await f.Context.CuentasPorPagar.AsNoTracking().SingleAsync();
        Assert.Equal(1000, account.SaldoPendienteCentavos);
        Assert.Equal(0, (await f.CashService.GetTodaySummaryAsync()).TotalPagosProveedoresCentavos);
    }

    [Fact]
    public async Task ConsultaStation_IsRejectedBySupplierPaymentServiceEvenForAdmin()
    {
        await using var f = await Fixture.CreateAsync();
        var service = f.CreatePaymentService(new AuditService(f.Context), new NoOperationalWritesInstallationContext());
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() => service.RegisterPaymentAsync(
            new RegisterSupplierPaymentRequest(Guid.NewGuid(), f.AccountId, 100, MetodoPago.EFECTIVO)));
        Assert.Empty(await f.Context.AbonosCuentaPorPagar.AsNoTracking().ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _connectionString;
        private readonly CurrentSession _session;
        public AppDbContext Context { get; }
        public int PurchaseId { get; private set; }
        public int AccountId { get; private set; }
        public PurchasePaymentService InitialService { get; }
        public AccountsPayableService PaymentService { get; }
        public AccountsPayableQueryService Query { get; }
        public CashService CashService { get; }

        private Fixture(SqliteConnection connection, AppDbContext context, CurrentSession session, string connectionString,
            PurchasePaymentService initialService, AccountsPayableService paymentService,
            AccountsPayableQueryService query, CashService cashService)
        {
            _connection = connection; _connectionString = connectionString; Context = context; _session = session;
            InitialService = initialService; PaymentService = paymentService; Query = query; CashService = cashService;
        }

        public static async Task<Fixture> CreateAsync(bool cashOpen = true, long initialCash = 5000, bool overdue = false,
            RolUsuario role = RolUsuario.ADMIN, bool grantPermissions = true)
        {
            var databaseName = $"supplier-payments-{Guid.NewGuid():N}";
            var connectionString = $"Data Source=file:{databaseName};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var user = new Usuario { NombreCompleto = "Usuario prueba", NombreUsuario = "tester", PasswordHash = "hash", Rol = role, Activo = true };
            context.Usuarios.Add(user);
            await context.SaveChangesAsync();
            if (role == RolUsuario.EMPLEADO && grantPermissions)
            {
                foreach (var permission in new[] { PermisoEmpleado.PAGO_COMPRA_REGISTRAR, PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR, PermisoEmpleado.CUENTAS_PAGAR_ACCEDER, PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER, PermisoEmpleado.FINANZAS_PROVEEDOR_VER })
                    context.UsuarioPermisos.Add(new UsuarioPermiso { UsuarioId = user.UsuarioId, Permiso = permission, Habilitado = true });
            }
            var supplier = new Proveedor { CodigoProveedor = "PRV-000001", Tipo = TipoProveedor.PROVEEDOR, NombreRazonSocial = "Proveedor prueba", NombreNormalizado = "proveedor prueba", Estado = EstadoProveedor.ACTIVO, UsuarioCreacionId = user.UsuarioId };
            context.Proveedores.Add(supplier);
            await context.SaveChangesAsync();
            var purchase = new Compra
            {
                CodigoCompra = "CMP-000001", ProveedorId = supplier.ProveedorId, UsuarioRegistroId = user.UsuarioId,
                FechaCompra = DateTime.Today, FechaRegistro = DateTime.UtcNow, EstadoRecepcion = EstadoRecepcionCompra.PENDIENTE,
                SubtotalCentavos = 1000, DescuentoCentavos = 0, TotalCentavos = 1000,
                EstadoFinanciero = EstadoFinancieroCompra.PENDIENTE, TotalPagadoCentavos = 0, SaldoPendienteCentavos = 1000
            };
            context.Compras.Add(purchase);
            await context.SaveChangesAsync();
            var account = new CuentaPorPagar
            {
                CodigoCuenta = "CPP-000001", CompraId = purchase.CompraId, ProveedorId = supplier.ProveedorId,
                UsuarioCreadorId = user.UsuarioId, TotalOriginalCentavos = 1000, TotalPagadoCentavos = 0,
                SaldoPendienteCentavos = 1000, FechaCreacion = DateTime.UtcNow,
                FechaVencimiento = overdue ? DateTime.Today.AddDays(-1) : DateTime.Today.AddDays(30),
                Estado = overdue ? EstadoCuentaPorPagar.VENCIDA : EstadoCuentaPorPagar.ABIERTA,
                FechaActualizacion = DateTime.UtcNow
            };
            context.CuentasPorPagar.Add(account);
            if (cashOpen)
                context.Cajas.Add(new Caja { FechaOperacion = DateOnly.FromDateTime(DateTime.Now), FechaApertura = DateTime.UtcNow, MontoInicialCentavos = initialCash, Estado = EstadoCaja.ABIERTA, UsuarioAperturaId = user.UsuarioId });
            await context.SaveChangesAsync();

            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, role));
            var audit = new AuditService(context);
            var permissions = new EmployeePermissionService(context, session, audit);
            var initialService = new PurchasePaymentService(context, session, permissions, audit);
            var paymentService = new AccountsPayableService(context, session, permissions, audit);
            var query = new AccountsPayableQueryService(context, permissions);
            var cash = new CashService(context, session, audit, new NullNotificationService(), permissions);
            var fixture = new Fixture(connection, context, session, connectionString, initialService, paymentService, query, cash)
            { PurchaseId = purchase.CompraId, AccountId = account.CuentaPorPagarId };
            return fixture;
        }

        public async Task<Sibling> CreateSiblingAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            var session = new CurrentSession();
            var user = _session.UsuarioActual ?? throw new InvalidOperationException("Falta usuario de prueba.");
            session.IniciarSesion(user);
            var audit = new AuditService(context);
            var permissions = new EmployeePermissionService(context, session, audit);
            return new Sibling(connection, context, new AccountsPayableService(context, session, permissions, audit), this);
        }

        public AccountsPayableService CreatePaymentService(IAuditService audit, IInstallationContext? installationContext = null)
        {
            var permissions = new EmployeePermissionService(Context, _session, audit);
            return new AccountsPayableService(Context, _session, permissions, audit, installationContext);
        }

        public Task<PurchaseFinancialResult> InitialAsync(long amount, MetodoPago method, string? reference = null, Guid? operationId = null,
            OrigenFondos? origin = null, string? responsible = null)
        {
            var effectiveOrigin = origin ?? (method == MetodoPago.EFECTIVO ? OrigenFondos.CAJA_FISICA : OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA);
            var effectiveResponsible = responsible ?? (effectiveOrigin == OrigenFondos.CAJA_FISICA ? null : "Fixture");
            IReadOnlyList<PurchasePaymentLineRequest> lines = amount > 0
                ? [new PurchasePaymentLineRequest(operationId ?? Guid.NewGuid(), method, amount, reference, effectiveOrigin, effectiveResponsible)]
                : [];
            return InitialService.RegisterInitialPaymentsAsync(new RegisterInitialPurchasePaymentsRequest(PurchaseId, DateTime.Today.AddDays(30), lines));
        }

        public Task<PurchaseFinancialResult> PendingAsync() => InitialService.RegisterInitialPaymentsAsync(
            new RegisterInitialPurchasePaymentsRequest(PurchaseId, DateTime.Today.AddDays(30), []));

        public Task<SupplierPaymentResult> PaymentAsync(long amount, MetodoPago method, string? reference = null, Guid? operationId = null,
            OrigenFondos? origin = null, string? responsible = null)
        {
            var effectiveOrigin = origin ?? (method == MetodoPago.EFECTIVO ? OrigenFondos.CAJA_FISICA : OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA);
            var effectiveResponsible = responsible ?? (effectiveOrigin == OrigenFondos.CAJA_FISICA ? null : "Fixture");
            return PaymentService.RegisterPaymentAsync(new RegisterSupplierPaymentRequest(operationId ?? Guid.NewGuid(), AccountId, amount, method, reference, OrigenFondos: effectiveOrigin, ResponsableOrigen: effectiveResponsible));
        }

        public Task<SupplierPaymentBatchResult> PaymentBatchAsync(Guid operationId, IReadOnlyList<SupplierPaymentLineRequest> lines) =>
            PaymentService.RegisterPaymentsAsync(new RegisterSupplierPaymentBatchRequest(operationId, AccountId, lines));

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        public sealed class Sibling : IAsyncDisposable
        {
            private readonly SqliteConnection _connection;
            private readonly AppDbContext _context;
            private readonly Fixture _owner;
            public AccountsPayableService Service { get; }

            internal Sibling(SqliteConnection connection, AppDbContext context, AccountsPayableService service, Fixture owner)
            {
                _connection = connection; _context = context; Service = service; _owner = owner;
            }

            public Task<SupplierPaymentResult> PaymentAsync(long amount, MetodoPago method) =>
                Service.RegisterPaymentAsync(new RegisterSupplierPaymentRequest(Guid.NewGuid(), _owner.AccountId, amount, method));

            public async ValueTask DisposeAsync()
            {
                await _context.DisposeAsync();
                await _connection.DisposeAsync();
            }
        }
    }

    private static async Task<CapturedResult> Capture(Task<SupplierPaymentResult> operation)
    {
        try
        {
            await operation;
            return new CapturedResult(true, null);
        }
        catch (Exception ex)
        {
            return new CapturedResult(false, ex);
        }
    }

    private sealed record CapturedResult(bool Success, Exception? Error);

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Auditoria de prueba fallida.");
    }

    private sealed class NoOperationalWritesInstallationContext : IInstallationContext
    {
        public InstallationStatus Status => new(
            StationConfigurationState.Ready,
            InstallationMode.Consulta,
            true,
            Guid.NewGuid(),
            1,
            "fixture-station-settings.json",
            null);

        public bool AllowsOperationalWrites => false;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) =>
            throw new InstallationWriteDeniedException("La estación CONSULTA no puede realizar escrituras operativas.", operationName);
    }
}
