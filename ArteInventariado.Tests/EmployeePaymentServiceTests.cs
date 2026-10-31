using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class EmployeePaymentServiceTests
{
    [Fact]
    public async Task Principal_CanRegisterViewAndCancelPayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();

        var registered = await fixture.Service(1, "admin").RegisterPaymentAsync(
            ExternalPayment(
                3,
                12500,
                EmployeePaymentType.Weekly,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 7),
                5,
                "Semana de prueba"));

        Assert.Equal(12500, registered.AmountCentavos);
        Assert.Equal(1, registered.RegisteredByUserId);
        Assert.Equal(EmployeePaymentStatus.Active, registered.Status);

        var history = await fixture.Query(1, "admin").GetEmployeeHistoryAsync(3);
        Assert.Single(history);
        Assert.Equal("admin", history[0].RegisteredByUserName);
        Assert.Equal(OrigenFondos.FONDOS_EXTERNOS_DUENO, history[0].OrigenFondos);
        Assert.Equal("Dueño", history[0].ResponsableOrigen);
        Assert.Null(history[0].CajaId);

        var cancelled = await fixture.Service(1, "admin").CancelPaymentAsync(
            new CancelEmployeePaymentRequest(registered.PaymentId, "Monto digitado incorrectamente"));
        Assert.Equal(EmployeePaymentStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.CancelledByUserId);
        Assert.Equal("Monto digitado incorrectamente", cancelled.CancellationReason);

        var summary = await fixture.Query(1, "admin").GetSummaryAsync(3);
        Assert.Equal(0, summary.TotalPaidCentavos);
        Assert.Equal(0, summary.PaymentCount);
        Assert.Equal(1, summary.CancelledPaymentCount);
        Assert.Equal(1, summary.TotalRecordCount);
    }

    [Fact]
    public async Task DelegatedAdmin_RequiresExactPermissionPerAction()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(2, "delegado");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterPaymentAsync(
            ExternalPayment(3, 1000, EmployeePaymentType.Daily)));

        await fixture.GrantAsync(2, PermisoEmpleado.EMPLEADOS_PAGOS_REGISTRAR);
        var payment = await service.RegisterPaymentAsync(
            ExternalPayment(3, 1000, EmployeePaymentType.Daily));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelPaymentAsync(
            new CancelEmployeePaymentRequest(payment.PaymentId, "Sin permiso de anulacion")));

        await fixture.GrantAsync(2, PermisoEmpleado.EMPLEADOS_PAGOS_ANULAR);
        await service.CancelPaymentAsync(new CancelEmployeePaymentRequest(payment.PaymentId, "Correccion autorizada"));
    }

    [Fact]
    public async Task EmployeeCannotAdministerPayments()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(3, "empleado");

        Assert.False(await service.CanRegisterAsync());
        Assert.False(await service.CanCancelAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterPaymentAsync(
            ExternalPayment(3, 1000, EmployeePaymentType.Daily)));
    }

    [Fact]
    public async Task InvalidAmountPeriodAndDaysAreRejected()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(1, "admin");

        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterPaymentAsync(
            ExternalPayment(3, 0, EmployeePaymentType.Daily)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterPaymentAsync(
            ExternalPayment(
                3,
                100,
                EmployeePaymentType.Daily,
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 1, 1))));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterPaymentAsync(
            ExternalPayment(3, 100, EmployeePaymentType.Daily, daysConsidered: -1)));

        var optionalDays = await service.RegisterPaymentAsync(
            ExternalPayment(3, 100, EmployeePaymentType.Extraordinary));
        Assert.Null(optionalDays.DaysConsidered);
    }

    [Fact]
    public async Task CancellationRequiresReasonAndCannotBeRepeated()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(1, "admin");
        var payment = await service.RegisterPaymentAsync(
            ExternalPayment(3, 1000, EmployeePaymentType.Monthly));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CancelPaymentAsync(
            new CancelEmployeePaymentRequest(payment.PaymentId, "  ")));
        await service.CancelPaymentAsync(new CancelEmployeePaymentRequest(payment.PaymentId, "Duplicado"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelPaymentAsync(
            new CancelEmployeePaymentRequest(payment.PaymentId, "Segundo intento")));
    }

    [Fact]
    public async Task HistoryFiltersAndSummaryPreserveInactiveUsersAndManualDays()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(1, "admin");
        var first = await service.RegisterPaymentAsync(
            ExternalPayment(
                4,
                3000,
                EmployeePaymentType.Biweekly,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 15),
                0));
        await service.RegisterPaymentAsync(
            ExternalPayment(
                4,
                5000,
                EmployeePaymentType.Extraordinary,
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 2, 1),
                null));

        var biweekly = await fixture.Query(1, "admin").GetHistoryAsync(new EmployeePaymentFilter(
            4,
            PaymentType: EmployeePaymentType.Biweekly,
            Status: EmployeePaymentStatus.Active));
        Assert.Single(biweekly);
        Assert.Equal(first.PaymentId, biweekly[0].PaymentId);
        Assert.Equal(0, biweekly[0].DaysConsidered);
        Assert.Equal(4, biweekly[0].UserId);

        var summary = await fixture.Query(1, "admin").GetSummaryAsync(
            4,
            new EmployeePaymentFilter(4));
        Assert.Equal(8000, summary.TotalPaidCentavos);
        Assert.Equal(2, summary.PaymentCount);
        Assert.Equal(2, summary.TotalRecordCount);
    }

    [Fact]
    public async Task PaymentDaysAreNotDerivedFromLoginEvents()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        fixture.Db.EmployeeLoginEvents.AddRange(
            new EmployeeLoginEvent { UserId = 3, LoggedInAtUtc = Utc(2026, 3, 1) },
            new EmployeeLoginEvent { UserId = 3, LoggedInAtUtc = Utc(2026, 3, 2) });
        await fixture.Db.SaveChangesAsync();

        var payment = await fixture.Service(1, "admin").RegisterPaymentAsync(
            ExternalPayment(3, 9000, EmployeePaymentType.Daily));
        Assert.Null(payment.DaysConsidered);
    }

    [Fact]
    public async Task CashFundingRequiresOpenCashAndAvailableAmountThenReducesExpectedCash()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(1, "admin");
        var firstRequest = CashPayment(3, 2_000);

        var withoutCash = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterPaymentAsync(firstRequest));
        Assert.Contains("sin una caja abierta", withoutCash.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.EmployeePayments.ToListAsync());

        var cash = await fixture.AddOpenCashAsync(5_000);
        var registered = await service.RegisterPaymentAsync(firstRequest);
        var summary = await CashService.BuildSummaryAsync(fixture.Db, cash, CancellationToken.None);

        Assert.Equal(cash.CajaId, registered.CajaId);
        Assert.Equal(OrigenFondos.CAJA_FISICA, registered.OrigenFondos);
        Assert.Equal(2_000, summary.TotalPagosEmpleadosCentavos);
        Assert.Equal(3_000, summary.TotalEsperadoCentavos);

        var insufficient = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterPaymentAsync(CashPayment(3, 3_001)));
        Assert.Contains("insuficiente", insufficient.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3_000, (await CashService.BuildSummaryAsync(fixture.Db, cash, CancellationToken.None)).TotalEsperadoCentavos);
        Assert.Single(await fixture.Db.EmployeePayments.ToListAsync());
    }

    [Fact]
    public async Task PaymentOperationIsIdempotentAndRejectsChangedPayload()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(1, "admin");
        var request = ExternalPayment(3, 1_250, EmployeePaymentType.Weekly);

        var first = await service.RegisterPaymentAsync(request);
        var replay = await service.RegisterPaymentAsync(request);
        var changed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterPaymentAsync(request with { AmountCentavos = 1_500 }));

        Assert.Equal(first.PaymentId, replay.PaymentId);
        Assert.Contains("otro pago", changed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await fixture.Db.EmployeePayments.ToListAsync());
        Assert.Equal(OrigenFondos.FONDOS_EXTERNOS_DUENO, replay.OrigenFondos);
    }

    [Fact]
    public async Task CancellingCashPaymentDoesNotRestoreCashThatAlreadyLeftTheDrawer()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var cash = await fixture.AddOpenCashAsync(5_000);
        var service = fixture.Service(1, "admin");
        var payment = await service.RegisterPaymentAsync(CashPayment(3, 1_000));

        await service.CancelPaymentAsync(new CancelEmployeePaymentRequest(payment.PaymentId, "Registro duplicado"));
        var summary = await CashService.BuildSummaryAsync(fixture.Db, cash, CancellationToken.None);

        Assert.Equal(1_000, summary.TotalPagosEmpleadosCentavos);
        Assert.Equal(4_000, summary.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task ExternalPaymentCanBeRecordedWithoutCashAndFundingFieldsMustBeValid()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var service = fixture.Service(1, "admin");
        var external = await service.RegisterPaymentAsync(ExternalPayment(3, 750, EmployeePaymentType.Extraordinary));

        Assert.Null(external.CajaId);
        var summary = await CashService.BuildSummaryAsync(
            fixture.Db,
            await fixture.AddOpenCashAsync(4_000),
            CancellationToken.None);
        Assert.Equal(0, summary.TotalPagosEmpleadosCentavos);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterPaymentAsync(
            new RegisterEmployeePaymentRequest(3, 100, EmployeePaymentType.Daily,
                MetodoPago: MetodoPago.TRANSFERENCIA,
                OrigenFondos: OrigenFondos.CAJA_FISICA,
                OperacionId: Guid.NewGuid())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterPaymentAsync(
            new RegisterEmployeePaymentRequest(3, 100, EmployeePaymentType.Daily,
                MetodoPago: MetodoPago.TRANSFERENCIA,
                OrigenFondos: OrigenFondos.FONDOS_EXTERNOS_DUENO,
                ResponsableOrigen: "Dueño",
                OperacionId: Guid.NewGuid())));
    }

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static RegisterEmployeePaymentRequest ExternalPayment(
        int userId,
        long amount,
        EmployeePaymentType type,
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null,
        int? daysConsidered = null,
        string? note = null) => new(
            userId,
            amount,
            type,
            periodStart,
            periodEnd,
            daysConsidered,
            note,
            MetodoPago.EFECTIVO,
            OrigenFondos.FONDOS_EXTERNOS_DUENO,
            "Dueño",
            OperacionId: Guid.NewGuid());

    private static RegisterEmployeePaymentRequest CashPayment(int userId, long amount) => new(
        userId,
        amount,
        EmployeePaymentType.Extraordinary,
        MetodoPago: MetodoPago.EFECTIVO,
        OrigenFondos: OrigenFondos.CAJA_FISICA,
        OperacionId: Guid.NewGuid());

    private sealed class PaymentFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly AppDbContext _db;

        private PaymentFixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            _db = db;
        }

        public AppDbContext Db => _db;

        public async Task<Caja> AddOpenCashAsync(long openingAmount)
        {
            var now = DateTime.UtcNow;
            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = now,
                MontoInicialCentavos = openingAmount,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = 1
            };
            _db.Cajas.Add(cash);
            await _db.SaveChangesAsync();
            return cash;
        }

        public static async Task<PaymentFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Usuarios.AddRange(
                User(1, "admin", "Principal", RolUsuario.ADMIN),
                User(2, "delegado", "Delegado", RolUsuario.ADMIN),
                User(3, "empleado", "Empleado", RolUsuario.EMPLEADO),
                User(4, "inactivo", "Inactivo", RolUsuario.EMPLEADO, false));
            db.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = "negocio.zona_horaria",
                Valor = "UTC",
                FechaModificacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return new PaymentFixture(connection, db);
        }

        public EmployeePaymentService Service(int userId, string userName)
        {
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                userId,
                userName,
                userName,
                userId == 3 ? RolUsuario.EMPLEADO : RolUsuario.ADMIN));
            return new EmployeePaymentService(
                _db,
                session,
                new AuditService(_db),
                TimeProvider.System);
        }

        public IEmployeePaymentQueryService Query(int userId, string userName)
        {
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                userId,
                userName,
                userName,
                userId == 3 ? RolUsuario.EMPLEADO : RolUsuario.ADMIN));
            return new EmployeePaymentQueryService(_db, session);
        }

        public async Task GrantAsync(int userId, PermisoEmpleado permission)
        {
            _db.UsuarioPermisos.Add(new UsuarioPermiso
            {
                UsuarioId = userId,
                Permiso = permission,
                Habilitado = true,
                FechaActualizacion = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Usuario User(int id, string userName, string fullName, RolUsuario role, bool active = true) => new()
        {
            UsuarioId = id,
            NombreUsuario = userName,
            NombreCompleto = fullName,
            PasswordHash = "test-hash",
            Rol = role,
            Activo = active,
            FechaCreacion = Utc(2026, 1, 1)
        };
    }
}
