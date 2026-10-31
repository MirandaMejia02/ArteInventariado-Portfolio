using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class AccountPaymentServiceTests
{
    [Fact]
    public async Task RegisterPaymentAsync_PersistsPartialCashPayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var result = await fixture.RegisterAsync(accountId, 400, MetodoPago.EFECTIVO);

        Assert.Equal(400, result.MontoCentavos);
        Assert.Equal(0, result.AplicadoInteresCentavos);
        Assert.Equal(400, result.AplicadoCapitalCentavos);
        Assert.Equal(600, result.SaldoNuevoCentavos);
        await using var verification = fixture.CreateContext();
        var account = await verification.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.CuentaPorCobrarId == accountId);
        Assert.Equal(400, account.TotalPagadoCentavos);
        Assert.Equal(600, account.SaldoActualCentavos);
        Assert.Equal(EstadoCuentaPorCobrar.ABIERTA, account.Estado);
    }

    [Fact]
    public async Task RegisterPaymentAsync_PersistsCompleteCashPaymentAndMarksPaid()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var result = await fixture.RegisterAsync(accountId, 1000, MetodoPago.EFECTIVO);

        Assert.Equal(0, result.SaldoNuevoCentavos);
        await using var verification = fixture.CreateContext();
        var account = await verification.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.CuentaPorCobrarId == accountId);
        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, account.Estado);
        Assert.NotNull(account.FechaPago);
    }

    [Fact]
    public async Task RegisterPaymentAsync_BlocksCashWhenCashIsClosed()
    {
        await using var fixture = await PaymentFixture.CreateAsync(cashOpen: false);
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task RegisterPaymentAsync_AllowsTransferWithReference()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var result = await fixture.RegisterAsync(accountId, 250, MetodoPago.TRANSFERENCIA, "TRX-1");

        Assert.Null(result.CajaId);
        Assert.Equal("TRX-1", result.Referencia);
        Assert.Equal(750, result.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_BlocksTransferWithoutReference()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 250, MetodoPago.TRANSFERENCIA));
    }

    [Fact]
    public async Task RegisterPaymentAsync_AllowsBitcoinWithReference()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var result = await fixture.RegisterAsync(accountId, 250, MetodoPago.BITCOIN, "BTC-1");

        Assert.Null(result.CajaId);
        Assert.Equal("BTC-1", result.Referencia);
        Assert.Equal(750, result.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_BlocksBitcoinWithoutReference()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 250, MetodoPago.BITCOIN));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RegisterPaymentAsync_BlocksZeroOrNegativeAmount(long amount)
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.RegisterAsync(accountId, amount, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task RegisterPaymentAsync_BlocksOverpayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 1001, MetodoPago.EFECTIVO));
    }

    [Theory]
    [InlineData(EstadoCuentaPorCobrar.PAGADA)]
    [InlineData(EstadoCuentaPorCobrar.ANULADA)]
    public async Task RegisterPaymentAsync_BlocksClosedAccounts(EstadoCuentaPorCobrar estado)
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(
            detailCapitalCentavos: estado == EstadoCuentaPorCobrar.PAGADA ? [0] : [1000],
            estado: estado);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task RegisterPaymentAsync_BlocksMissingAccount()
    {
        await using var fixture = await PaymentFixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(9999, 100, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task RegisterPaymentAsync_BlocksEmployeeWithoutPermission()
    {
        await using var fixture = await PaymentFixture.CreateAsync(grantPaymentPermission: false);
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO));
    }

    [Fact]
    public async Task RegisterPaymentAsync_AllowsEmployeeWithPermission()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var result = await fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO);

        Assert.Equal(900, result.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_AllowsAdmin()
    {
        await using var fixture = await PaymentFixture.CreateAsync(role: RolUsuario.ADMIN, grantPaymentPermission: false);
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var result = await fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO);

        Assert.Equal(900, result.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_AppliesFullPaymentToInterest()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000], interestCentavos: 500);

        var result = await fixture.RegisterAsync(accountId, 500, MetodoPago.EFECTIVO);

        Assert.Equal(500, result.AplicadoInteresCentavos);
        Assert.Equal(0, result.AplicadoCapitalCentavos);
        Assert.Equal(0, result.InteresPendienteNuevoCentavos);
        Assert.Equal(1000, result.CapitalPendienteNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_AppliesPartialPaymentToInterest()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000], interestCentavos: 500);

        var result = await fixture.RegisterAsync(accountId, 200, MetodoPago.EFECTIVO);

        Assert.Equal(200, result.AplicadoInteresCentavos);
        Assert.Equal(0, result.AplicadoCapitalCentavos);
        Assert.Equal(300, result.InteresPendienteNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_AppliesPaymentToInterestAndCapital()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000], interestCentavos: 500);

        var result = await fixture.RegisterAsync(accountId, 700, MetodoPago.EFECTIVO);

        Assert.Equal(500, result.AplicadoInteresCentavos);
        Assert.Equal(200, result.AplicadoCapitalCentavos);
        Assert.Equal(800, result.CapitalPendienteNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_AppliesPaymentOnlyToCapitalWhenInterestIsPaid()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(
            detailCapitalCentavos: [1000],
            interestCentavos: 500,
            totalPaidCentavos: 500);

        var result = await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO);

        Assert.Equal(0, result.AplicadoInteresCentavos);
        Assert.Equal(300, result.AplicadoCapitalCentavos);
        Assert.Equal(700, result.CapitalPendienteNuevoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_DistributesCapitalProportionallyAcrossDetails()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000, 3000]);

        var result = await fixture.RegisterAsync(accountId, 800, MetodoPago.EFECTIVO);

        Assert.Equal(800, result.AplicadoCapitalCentavos);
        await using var verification = fixture.CreateContext();
        var remaining = await verification.CuentasPorCobrarDetalle
            .AsNoTracking()
            .Where(d => d.CuentaPorCobrarId == accountId)
            .OrderBy(d => d.CuentaPorCobrarDetalleId)
            .Select(d => d.CapitalPendienteCentavos)
            .ToListAsync();
        Assert.Equal([800, 2400], remaining);
    }

    [Fact]
    public async Task RegisterPaymentAsync_AssignsResidualCentToLargestPendingDetailDeterministically()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000, 1000, 1000]);

        await fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO);

        await using var verification = fixture.CreateContext();
        var remaining = await verification.CuentasPorCobrarDetalle
            .AsNoTracking()
            .Where(d => d.CuentaPorCobrarId == accountId)
            .OrderBy(d => d.CuentaPorCobrarDetalleId)
            .Select(d => d.CapitalPendienteCentavos)
            .ToListAsync();
        Assert.Equal([966, 967, 967], remaining);
    }

    [Fact]
    public async Task RegisterPaymentAsync_CashPaymentIncreasesExpectedCash()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000], initialCashPaymentCentavos: 500);

        await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO);

        var summary = await fixture.GetCashSummaryAsync();
        Assert.Equal(800, summary.TotalVentasCentavos);
        Assert.Equal(800, summary.TotalEsperadoCentavos);
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA)]
    [InlineData(MetodoPago.BITCOIN)]
    public async Task RegisterPaymentAsync_NonCashPaymentDoesNotIncreaseExpectedCash(MetodoPago method)
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000], initialCashPaymentCentavos: 500);

        await fixture.RegisterAsync(accountId, 300, method, "REF-1");

        var summary = await fixture.GetCashSummaryAsync();
        Assert.Equal(500, summary.TotalVentasCentavos);
        Assert.Equal(500, summary.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_ReusingSameOperationIdReturnsExistingPayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);
        var operationId = Guid.NewGuid();

        var first = await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO, operationId: operationId);
        var second = await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO, operationId: operationId);

        Assert.False(first.EsReintentoIdempotente);
        Assert.True(second.EsReintentoIdempotente);
        Assert.Equal(first.AbonoCuentaPorCobrarId, second.AbonoCuentaPorCobrarId);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.AbonosCuentaPorCobrar.CountAsync());
        Assert.Equal(700, await verification.CuentasPorCobrar
            .Where(c => c.CuentaPorCobrarId == accountId)
            .Select(c => c.SaldoActualCentavos)
            .SingleAsync());
    }

    [Fact]
    public async Task RegisterPaymentAsync_ReusingSameOperationIdWithDifferentDataFails()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);
        var operationId = Guid.NewGuid();

        await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO, operationId: operationId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 301, MetodoPago.EFECTIVO, operationId: operationId));
    }

    [Fact]
    public async Task RegisterPaymentAsync_ConcurrentOperationsDoNotOverpay()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var attempts = await Task.WhenAll(
            fixture.CaptureRegisterAsync(accountId, 800, Guid.NewGuid()),
            fixture.CaptureRegisterAsync(accountId, 800, Guid.NewGuid()));

        Assert.Equal(1, attempts.Count(a => a.Success));
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.AbonosCuentaPorCobrar.CountAsync());
        Assert.Equal(200, await verification.CuentasPorCobrar
            .Where(c => c.CuentaPorCobrarId == accountId)
            .Select(c => c.SaldoActualCentavos)
            .SingleAsync());
    }

    [Fact]
    public async Task RegisterPaymentAsync_RollsBackWhenAuditFails()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO, auditService: new ThrowingAuditService()));

        await using var verification = fixture.CreateContext();
        Assert.Equal(0, await verification.AbonosCuentaPorCobrar.CountAsync());
        var account = await verification.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.CuentaPorCobrarId == accountId);
        Assert.Equal(0, account.TotalPagadoCentavos);
        Assert.Equal(1000, account.SaldoActualCentavos);
    }

    [Fact]
    public async Task RegisterPaymentAsync_RegistersAuditEntry()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var result = await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO);

        await using var verification = fixture.CreateContext();
        var audit = await verification.Bitacora.AsNoTracking().SingleAsync(b => b.TipoAccion == TipoBitacora.ABONO_REGISTRADO);
        Assert.Equal("abonos_cuenta_por_cobrar", audit.Entidad);
        Assert.Equal(result.AbonoCuentaPorCobrarId, audit.EntidadId);
        Assert.Contains(result.CodigoAbono, audit.Descripcion);
    }

    [Fact]
    public async Task PreviewAsync_UsesSameInterestThenCapitalRules()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000], interestCentavos: 500);

        await using var context = fixture.CreateContext();
        var preview = await fixture.CreateQueryService(context).PreviewAsync(new AccountPaymentPreviewRequest(
            accountId,
            700,
            MetodoPago.EFECTIVO));

        Assert.Equal(500, preview.AplicadoInteresCentavos);
        Assert.Equal(200, preview.AplicadoCapitalCentavos);
        Assert.Equal(1000, preview.CapitalPendienteCentavos);
        Assert.Equal(800, preview.SaldoNuevoCentavos);
        Assert.NotNull(preview.CajaId);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsRegisteredPaymentsForAccount()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);
        var registered = await fixture.RegisterAsync(accountId, 250, MetodoPago.TRANSFERENCIA, "TRX-HIST");

        await using var context = fixture.CreateContext();
        var history = await fixture.CreateQueryService(context).GetHistoryAsync(new AccountPaymentHistoryFilter(
            CuentaPorCobrarId: accountId));

        var row = Assert.Single(history);
        Assert.Equal(registered.AbonoCuentaPorCobrarId, row.AbonoCuentaPorCobrarId);
        Assert.Equal("TRX-HIST", row.Referencia);
        Assert.Equal(MetodoPago.TRANSFERENCIA, row.MetodoPago);
        Assert.Equal(750, row.SaldoNuevoCentavos);
    }

    [Fact]
    public async Task GetHistoryAsync_CombinesTextMethodAndDateFilters()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [3000]);
        var cash = await fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO);
        var transfer = await fixture.RegisterAsync(accountId, 200, MetodoPago.TRANSFERENCIA, "TRX-FILTER");
        var bitcoin = await fixture.RegisterAsync(accountId, 300, MetodoPago.BITCOIN, "BTC-FILTER");
        var today = DateTime.UtcNow.Date;

        await using (var updateContext = fixture.CreateContext())
        {
            var payments = await updateContext.AbonosCuentaPorCobrar
                .Where(payment =>
                    payment.AbonoCuentaPorCobrarId == cash.AbonoCuentaPorCobrarId ||
                    payment.AbonoCuentaPorCobrarId == transfer.AbonoCuentaPorCobrarId ||
                    payment.AbonoCuentaPorCobrarId == bitcoin.AbonoCuentaPorCobrarId)
                .ToListAsync();

            payments.Single(payment => payment.AbonoCuentaPorCobrarId == cash.AbonoCuentaPorCobrarId).FechaAbono = today.AddDays(-3).AddHours(10);
            payments.Single(payment => payment.AbonoCuentaPorCobrarId == transfer.AbonoCuentaPorCobrarId).FechaAbono = today.AddDays(-1).AddHours(11);
            payments.Single(payment => payment.AbonoCuentaPorCobrarId == bitcoin.AbonoCuentaPorCobrarId).FechaAbono = today.AddHours(12);
            await updateContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var service = fixture.CreateQueryService(context);
        var all = await service.GetHistoryAsync(new AccountPaymentHistoryFilter(CuentaPorCobrarId: accountId));
        Assert.Equal(3, all.Count);

        var sample = all.Single(payment => payment.AbonoCuentaPorCobrarId == transfer.AbonoCuentaPorCobrarId);
        Assert.Single(await service.GetHistoryAsync(new AccountPaymentHistoryFilter(Search: transfer.CodigoAbono)));
        Assert.Equal(3, (await service.GetHistoryAsync(new AccountPaymentHistoryFilter(Search: sample.Cliente))).Count);
        Assert.Equal(3, (await service.GetHistoryAsync(new AccountPaymentHistoryFilter(Search: sample.CodigoCuenta))).Count);
        Assert.Equal(3, (await service.GetHistoryAsync(new AccountPaymentHistoryFilter(Search: sample.NumeroVenta))).Count);
        Assert.Single(await service.GetHistoryAsync(new AccountPaymentHistoryFilter(MetodoPago: MetodoPago.BITCOIN)));
        Assert.Equal(
            2,
            (await service.GetHistoryAsync(new AccountPaymentHistoryFilter(
                Desde: today.AddDays(-1),
                Hasta: today.AddDays(1).AddTicks(-1)))).Count);

        var combined = await service.GetHistoryAsync(new AccountPaymentHistoryFilter(
            Desde: today.AddDays(-2),
            Hasta: today.AddTicks(-1),
            MetodoPago: MetodoPago.TRANSFERENCIA,
            Search: sample.Cliente));

        var row = Assert.Single(combined);
        Assert.Equal(transfer.AbonoCuentaPorCobrarId, row.AbonoCuentaPorCobrarId);
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsReceiptSnapshot()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000], interestCentavos: 100);
        var registered = await fixture.RegisterAsync(accountId, 150, MetodoPago.BITCOIN, "BTC-DET");

        await using var context = fixture.CreateContext();
        var detail = await fixture.CreateQueryService(context).GetDetailAsync(registered.AbonoCuentaPorCobrarId);

        Assert.NotNull(detail);
        Assert.Equal(registered.CodigoAbono, detail.Summary.CodigoAbono);
        Assert.Equal(100, detail.Summary.AplicadoInteresCentavos);
        Assert.Equal(50, detail.Summary.AplicadoCapitalCentavos);
        Assert.Equal("BTC-DET", detail.Summary.Referencia);
        Assert.Equal(EstadoCuentaPorCobrar.ABIERTA, detail.EstadoCuenta);
    }

    [Fact]
    public async Task RegisterPaymentAsync_PersistsObservationsForReceiptDetail()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);

        var registered = await fixture.RegisterAsync(
            accountId,
            200,
            MetodoPago.EFECTIVO,
            observations: "Abono revisado en mostrador");

        await using var context = fixture.CreateContext();
        var detail = await fixture.CreateQueryService(context).GetDetailAsync(registered.AbonoCuentaPorCobrarId);

        Assert.NotNull(detail);
        Assert.Equal("Abono revisado en mostrador", detail.Summary.Observaciones);
    }

    [Fact]
    public async Task RegisterPaymentAsync_ReplayAfterUiFailureKeepsSinglePayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);
        var operationId = Guid.NewGuid();

        var first = await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO, operationId: operationId);
        var replay = await fixture.RegisterAsync(accountId, 300, MetodoPago.EFECTIVO, operationId: operationId);

        await using var context = fixture.CreateContext();
        Assert.Equal(first.AbonoCuentaPorCobrarId, replay.AbonoCuentaPorCobrarId);
        Assert.True(replay.EsReintentoIdempotente);
        Assert.Equal(1, await context.AbonosCuentaPorCobrar.CountAsync(a => a.CuentaPorCobrarId == accountId));
        var account = await context.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.CuentaPorCobrarId == accountId);
        Assert.Equal(300, account.TotalPagadoCentavos);
        Assert.Equal(700, account.SaldoActualCentavos);
    }

    [Fact]
    public async Task GetHistoryAsync_BlocksUserWithoutViewPermission()
    {
        await using var fixture = await PaymentFixture.CreateAsync(grantViewPermission: false);
        var accountId = await fixture.CreateAccountAsync(detailCapitalCentavos: [1000]);
        await fixture.RegisterAsync(accountId, 100, MetodoPago.EFECTIVO);

        await using var context = fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateQueryService(context).GetHistoryAsync(new AccountPaymentHistoryFilter(CuentaPorCobrarId: accountId)));
    }

    private sealed class PaymentFixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        private readonly DbContextOptions<AppDbContext> _options;

        private PaymentFixture(
            string dbPath,
            DbContextOptions<AppDbContext> options,
            TestCurrentSession session,
            int userId,
            int customerId,
            int cashId,
            int categoryId)
        {
            _dbPath = dbPath;
            _options = options;
            Session = session;
            UserId = userId;
            CustomerId = customerId;
            CashId = cashId;
            CategoryId = categoryId;
        }

        public TestCurrentSession Session { get; }

        public int UserId { get; }

        public int CustomerId { get; }

        public int CashId { get; }

        public int CategoryId { get; }

        public static async Task<PaymentFixture> CreateAsync(
            RolUsuario role = RolUsuario.EMPLEADO,
            bool grantPaymentPermission = true,
            bool grantViewPermission = true,
            bool cashOpen = true)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"arte-account-payment-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Usuario abono",
                NombreUsuario = $"usuario-{Guid.NewGuid():N}"[..20],
                PasswordHash = "hash",
                Rol = role,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}"[..30],
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(user);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            if (role == RolUsuario.EMPLEADO)
            {
                if (grantPaymentPermission)
                {
                    context.UsuarioPermisos.Add(new UsuarioPermiso
                    {
                        UsuarioId = user.UsuarioId,
                        Permiso = PermisoEmpleado.ABONO_REGISTRAR,
                        Habilitado = true,
                        FechaActualizacion = DateTime.UtcNow
                    });
                }

                if (grantViewPermission)
                {
                    context.UsuarioPermisos.Add(new UsuarioPermiso
                    {
                        UsuarioId = user.UsuarioId,
                        Permiso = PermisoEmpleado.CUENTAS_COBRAR_VER,
                        Habilitado = true,
                        FechaActualizacion = DateTime.UtcNow
                    });
                }
            }

            var customer = new Cliente
            {
                CodigoCliente = $"CLI-{Guid.NewGuid():N}"[..12],
                NombreCompleto = "Cliente abono",
                NombreNormalizado = "cliente abono",
                Estado = EstadoCliente.ACTIVO,
                CreditoHabilitado = true,
                UsuarioCreacionId = user.UsuarioId,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };
            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = cashOpen ? EstadoCaja.ABIERTA : EstadoCaja.CERRADA,
                UsuarioAperturaId = user.UsuarioId
            };
            context.Clientes.Add(customer);
            context.Cajas.Add(cash);
            await context.SaveChangesAsync();

            return new PaymentFixture(
                dbPath,
                options,
                new TestCurrentSession(new UsuarioSesion(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol)),
                user.UsuarioId,
                customer.ClienteId,
                cash.CajaId,
                category.CategoriaId);
        }

        public AppDbContext CreateContext() => new(_options);

        public async Task<int> CreateAccountAsync(
            IReadOnlyList<long> detailCapitalCentavos,
            long interestCentavos = 0,
            long totalPaidCentavos = 0,
            EstadoCuentaPorCobrar estado = EstadoCuentaPorCobrar.ABIERTA,
            long initialCashPaymentCentavos = 0)
        {
            await using var context = CreateContext();
            var totalCapital = detailCapitalCentavos.Sum();
            var totalOriginal = totalCapital + interestCentavos;
            var saldo = Math.Max(0, totalOriginal - totalPaidCentavos);
            if (estado == EstadoCuentaPorCobrar.PAGADA)
            {
                totalPaidCentavos = totalOriginal;
                saldo = 0;
            }

            var venta = new Venta
            {
                NumeroVenta = $"VTA-{Guid.NewGuid():N}"[..18],
                CajaId = CashId,
                UsuarioId = UserId,
                ClienteId = CustomerId,
                ClienteNombreSnapshot = "Cliente abono",
                FechaVenta = DateTime.UtcNow,
                SubtotalCentavos = totalOriginal + initialCashPaymentCentavos,
                DescuentoCentavos = 0,
                TotalCentavos = totalOriginal + initialCashPaymentCentavos,
                PagadoCentavos = initialCashPaymentCentavos,
                CambioCentavos = 0,
                Estado = EstadoVenta.COMPLETADA
            };
            context.Ventas.Add(venta);
            await context.SaveChangesAsync();

            if (initialCashPaymentCentavos > 0)
            {
                context.PagosVenta.Add(new PagoVenta
                {
                    VentaId = venta.VentaId,
                    MetodoPago = MetodoPago.EFECTIVO,
                    MontoCentavos = initialCashPaymentCentavos,
                    FechaPago = DateTime.UtcNow
                });
            }

            var account = new CuentaPorCobrar
            {
                CodigoCuenta = $"CXC-{Guid.NewGuid():N}"[..18],
                VentaId = venta.VentaId,
                ClienteId = CustomerId,
                CajaId = CashId,
                UsuarioRegistroId = UserId,
                CapitalFinanciadoCentavos = totalCapital,
                InteresPorcentaje = interestCentavos > 0 && totalCapital > 0 ? Math.Round(interestCentavos * 100m / totalCapital, 4) : 0,
                InteresCalculadoCentavos = interestCentavos,
                TotalOriginalCentavos = totalOriginal,
                TotalPagadoCentavos = totalPaidCentavos,
                SaldoActualCentavos = saldo,
                FechaCreacion = DateTime.UtcNow,
                FechaVencimiento = DateTime.UtcNow.AddDays(7),
                Estado = estado,
                FechaPago = estado == EstadoCuentaPorCobrar.PAGADA ? DateTime.UtcNow : null,
                FechaAnulacion = estado == EstadoCuentaPorCobrar.ANULADA ? DateTime.UtcNow : null,
                FechaActualizacion = DateTime.UtcNow
            };
            context.CuentasPorCobrar.Add(account);
            await context.SaveChangesAsync();

            for (var i = 0; i < detailCapitalCentavos.Count; i++)
            {
                var product = new Producto
                {
                    CodigoInterno = $"PRD-{Guid.NewGuid():N}"[..18],
                    Nombre = $"Producto {i + 1}",
                    CategoriaId = CategoryId,
                    PrecioCompraCentavos = 100,
                    PrecioVentaCentavos = Math.Max(1, detailCapitalCentavos[i]),
                    StockActual = 10,
                    StockMinimo = 1,
                    Estado = EstadoProducto.ACTIVO,
                    FechaCreacion = DateTime.UtcNow
                };
                context.Productos.Add(product);
                await context.SaveChangesAsync();

                var detail = new DetalleVenta
                {
                    VentaId = venta.VentaId,
                    ProductoId = product.ProductoId,
                    Cantidad = 1,
                    PrecioUnitarioCentavos = Math.Max(1, detailCapitalCentavos[i]),
                    SubtotalCentavos = Math.Max(0, detailCapitalCentavos[i]),
                    DescuentoCentavos = 0,
                    TotalCentavos = Math.Max(0, detailCapitalCentavos[i])
                };
                context.DetalleVentas.Add(detail);
                await context.SaveChangesAsync();

                context.CuentasPorCobrarDetalle.Add(new CuentaPorCobrarDetalle
                {
                    CuentaPorCobrarId = account.CuentaPorCobrarId,
                    DetalleVentaId = detail.DetalleVentaId,
                    ProductoId = product.ProductoId,
                    ProductoCodigoSnapshot = product.CodigoInterno,
                    ProductoNombreSnapshot = product.Nombre,
                    CantidadFinanciada = 1,
                    PrecioUnitarioCentavos = detail.PrecioUnitarioCentavos,
                    DescuentoLineaCentavos = 0,
                    DescuentoGeneralProporcionalCentavos = 0,
                    SubtotalFinanciadoCentavos = Math.Max(0, detailCapitalCentavos[i]),
                    CapitalPendienteCentavos = estado == EstadoCuentaPorCobrar.PAGADA ? 0 : detailCapitalCentavos[i]
                });
            }

            await context.SaveChangesAsync();
            return account.CuentaPorCobrarId;
        }

        public async Task<RegisterAccountPaymentResult> RegisterAsync(
            int accountId,
            long amount,
            MetodoPago method,
            string? reference = null,
            string? observations = null,
            Guid? operationId = null,
            IAuditService? auditService = null)
        {
            await using var context = CreateContext();
            var service = CreatePaymentService(context, auditService);
            return await service.RegisterPaymentAsync(new RegisterAccountPaymentRequest(
                operationId ?? Guid.NewGuid(),
                accountId,
                amount,
                method,
                reference,
                observations));
        }

        public async Task<AttemptResult> CaptureRegisterAsync(int accountId, long amount, Guid operationId)
        {
            try
            {
                await using var context = CreateContext();
                var service = CreatePaymentService(context);
                await service.RegisterPaymentAsync(new RegisterAccountPaymentRequest(
                    operationId,
                    accountId,
                    amount,
                    MetodoPago.EFECTIVO));
                return new AttemptResult(true);
            }
            catch (Exception ex)
            {
                return new AttemptResult(false, ex);
            }
        }

        public async Task<CashSummary> GetCashSummaryAsync()
        {
            await using var context = CreateContext();
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, Session, audit);
            var cashService = new CashService(context, Session, audit, new NullNotificationService(), permissionService);
            return await cashService.GetTodaySummaryAsync();
        }

        private AccountPaymentService CreatePaymentService(AppDbContext context, IAuditService? auditService = null)
        {
            var audit = auditService ?? new AuditService(context);
            var permissionService = new EmployeePermissionService(context, Session, audit);
            return new AccountPaymentService(context, Session, permissionService, audit);
        }

        public AccountPaymentQueryService CreateQueryService(AppDbContext context)
        {
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, Session, audit);
            return new AccountPaymentQueryService(context, permissionService);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var context = CreateContext();
                await context.Database.EnsureDeletedAsync();
            }
            catch
            {
                // Best-effort cleanup for temporary test databases.
            }

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
    }

    private sealed record AttemptResult(bool Success, Exception? Exception = null);

    private sealed class TestCurrentSession : ICurrentSession
    {
        public TestCurrentSession(UsuarioSesion usuario)
        {
            UsuarioActual = usuario;
        }

        public UsuarioSesion? UsuarioActual { get; private set; }
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Fallo simulado al guardar auditoria.");
        }
    }
}
