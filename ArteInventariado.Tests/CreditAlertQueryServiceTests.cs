using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class CreditAlertQueryServiceTests
{
    [Fact]
    public async Task GetAlertsAsync_ClassifiesActiveCreditRisks()
    {
        await using var fixture = await AlertFixture.CreateAsync();

        await fixture.AddAccountAsync(dueInDays: 0);
        await fixture.AddAccountAsync(dueInDays: 2);
        await fixture.AddAccountAsync(dueInDays: -2);
        await fixture.AddAccountAsync(dueInDays: -7);
        await fixture.AddAccountAsync(dueInDays: 20, createdDaysAgo: 8);
        await fixture.AddAccountAsync(dueInDays: 20, saldoCentavos: 800, customerLimitCentavos: 1000);
        await fixture.AddAccountAsync(dueInDays: 20, saldoCentavos: 1000, customerLimitCentavos: 1000);

        var alerts = await fixture.Service.GetAlertsAsync();

        Assert.Contains(alerts, a => a.Type == CreditAlertType.DueToday);
        Assert.Contains(alerts, a => a.Type == CreditAlertType.UpcomingDue);
        Assert.Contains(alerts, a => a.Type == CreditAlertType.Overdue);
        Assert.Contains(alerts, a => a.Type == CreditAlertType.Moroso);
        Assert.Contains(alerts, a => a.Type == CreditAlertType.NoRecentPayment);
        Assert.Contains(alerts, a => a.Type == CreditAlertType.NearLimit);
        Assert.Contains(alerts, a => a.Type == CreditAlertType.LimitReached);
    }

    [Fact]
    public async Task GetSummaryAsync_HidesBadgeWhenNoActiveAlerts()
    {
        await using var fixture = await AlertFixture.CreateAsync();
        await fixture.AddAccountAsync(dueInDays: 20, saldoCentavos: 1000, customerLimitCentavos: 10000);

        var summary = await fixture.Service.GetSummaryAsync();

        Assert.Equal(0, summary.TotalActive);
    }

    [Fact]
    public async Task GetAlertsAsync_DoesNotAlertPaidOrCancelledAccounts()
    {
        await using var fixture = await AlertFixture.CreateAsync();
        await fixture.AddAccountAsync(dueInDays: -20, saldoCentavos: 0, estado: EstadoCuentaPorCobrar.PAGADA);
        await fixture.AddAccountAsync(dueInDays: -20, saldoCentavos: 1000, estado: EstadoCuentaPorCobrar.ANULADA);

        var alerts = await fixture.Service.GetAlertsAsync();

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task GetAlertsAsync_BlocksEmployeeWithoutPermission()
    {
        await using var fixture = await AlertFixture.CreateAsync(grantAlertPermission: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetAlertsAsync());
    }

    [Fact]
    public async Task UpdateSettingsAsync_PersistsConfigurationForAdmin()
    {
        await using var fixture = await AlertFixture.CreateAsync(role: RolUsuario.ADMIN, grantAlertPermission: false);

        await fixture.Service.UpdateSettingsAsync(new UpdateCreditAlertSettingsRequest(
            5,
            10,
            75,
            EnableDueToday: true,
            EnableUpcomingDue: false,
            EnableOverdue: true,
            EnableMoroso: true,
            EnableNoRecentPayment: false,
            EnableNearLimit: true,
            EnableLimitReached: true));

        var settings = await fixture.Service.GetSettingsAsync();

        Assert.Equal(5, settings.UpcomingDueDays);
        Assert.Equal(10, settings.NoPaymentDays);
        Assert.Equal(75, settings.NearLimitPercent);
        Assert.False(settings.EnableUpcomingDue);
        Assert.False(settings.EnableNoRecentPayment);
    }

    private sealed class AlertFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private int _accountSequence;

        private AlertFixture(
            SqliteConnection connection,
            AppDbContext context,
            CreditAlertQueryService service,
            TestCurrentSession session,
            int userId,
            int cashId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Session = session;
            UserId = userId;
            CashId = cashId;
        }

        public AppDbContext Context { get; }
        public CreditAlertQueryService Service { get; }
        public TestCurrentSession Session { get; }
        public int UserId { get; }
        public int CashId { get; }

        public static async Task<AlertFixture> CreateAsync(
            RolUsuario role = RolUsuario.EMPLEADO,
            bool grantAlertPermission = true)
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
                NombreCompleto = "Usuario alertas",
                NombreUsuario = $"alertas-{Guid.NewGuid():N}"[..20],
                PasswordHash = "hash",
                Rol = role,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(user);
            await context.SaveChangesAsync();

            if (role == RolUsuario.EMPLEADO && grantAlertPermission)
            {
                context.UsuarioPermisos.Add(new UsuarioPermiso
                {
                    UsuarioId = user.UsuarioId,
                    Permiso = PermisoEmpleado.ALERTAS_CREDITO_VER,
                    Habilitado = true,
                    FechaActualizacion = DateTime.UtcNow
                });
            }

            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = user.UsuarioId
            };
            context.Cajas.Add(cash);
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, session, audit);
            var service = new CreditAlertQueryService(context, session, permissionService, audit);
            return new AlertFixture(connection, context, service, session, user.UsuarioId, cash.CajaId);
        }

        public async Task<int> AddAccountAsync(
            int dueInDays,
            long saldoCentavos = 1000,
            long? customerLimitCentavos = null,
            int createdDaysAgo = 0,
            EstadoCuentaPorCobrar estado = EstadoCuentaPorCobrar.ABIERTA,
            int? lastPaymentDaysAgo = null)
        {
            _accountSequence++;
            var customer = new Cliente
            {
                CodigoCliente = $"CLI-{_accountSequence:0000}",
                NombreCompleto = $"Cliente alerta {_accountSequence}",
                NombreNormalizado = $"cliente alerta {_accountSequence}",
                Estado = EstadoCliente.ACTIVO,
                CreditoHabilitado = true,
                CreditoLimiteCentavos = customerLimitCentavos,
                UsuarioCreacionId = UserId,
                FechaCreacion = DateTime.UtcNow.AddDays(-createdDaysAgo),
                FechaActualizacion = DateTime.UtcNow
            };
            Context.Clientes.Add(customer);
            await Context.SaveChangesAsync();

            var venta = new Venta
            {
                NumeroVenta = $"VTA-{_accountSequence:0000}",
                CajaId = CashId,
                UsuarioId = UserId,
                ClienteId = customer.ClienteId,
                ClienteNombreSnapshot = customer.NombreCompleto,
                FechaVenta = DateTime.UtcNow.AddDays(-createdDaysAgo),
                SubtotalCentavos = saldoCentavos,
                DescuentoCentavos = 0,
                TotalCentavos = saldoCentavos,
                PagadoCentavos = 0,
                CambioCentavos = 0,
                Estado = EstadoVenta.COMPLETADA
            };
            Context.Ventas.Add(venta);
            await Context.SaveChangesAsync();

            var totalOriginal = estado == EstadoCuentaPorCobrar.PAGADA ? Math.Max(1000, saldoCentavos) : saldoCentavos;
            var account = new CuentaPorCobrar
            {
                CodigoCuenta = $"CXC-{_accountSequence:0000}",
                VentaId = venta.VentaId,
                ClienteId = customer.ClienteId,
                CajaId = CashId,
                UsuarioRegistroId = UserId,
                CapitalFinanciadoCentavos = totalOriginal,
                InteresPorcentaje = 0,
                InteresCalculadoCentavos = 0,
                TotalOriginalCentavos = totalOriginal,
                TotalPagadoCentavos = totalOriginal - saldoCentavos,
                SaldoActualCentavos = saldoCentavos,
                FechaCreacion = DateTime.UtcNow.AddDays(-createdDaysAgo),
                FechaVencimiento = DateTime.Now.Date.AddDays(dueInDays).ToUniversalTime(),
                Estado = estado,
                FechaPago = estado == EstadoCuentaPorCobrar.PAGADA ? DateTime.UtcNow : null,
                FechaAnulacion = estado == EstadoCuentaPorCobrar.ANULADA ? DateTime.UtcNow : null,
                FechaActualizacion = DateTime.UtcNow
            };
            Context.CuentasPorCobrar.Add(account);
            await Context.SaveChangesAsync();

            if (lastPaymentDaysAgo.HasValue)
            {
                Context.AbonosCuentaPorCobrar.Add(new AbonoCuentaPorCobrar
                {
                    CodigoAbono = $"ABN-{_accountSequence:0000}",
                    OperacionId = Guid.NewGuid(),
                    CuentaPorCobrarId = account.CuentaPorCobrarId,
                    UsuarioId = UserId,
                    CajaId = CashId,
                    MetodoPago = MetodoPago.EFECTIVO,
                    MontoCentavos = 100,
                    AplicadoInteresCentavos = 0,
                    AplicadoCapitalCentavos = 100,
                    SaldoAnteriorCentavos = saldoCentavos + 100,
                    SaldoNuevoCentavos = saldoCentavos,
                    InteresPendienteAnteriorCentavos = 0,
                    InteresPendienteNuevoCentavos = 0,
                    CapitalPendienteAnteriorCentavos = saldoCentavos + 100,
                    CapitalPendienteNuevoCentavos = saldoCentavos,
                    FechaAbono = DateTime.UtcNow.AddDays(-lastPaymentDaysAgo.Value)
                });
                await Context.SaveChangesAsync();
            }

            Context.ChangeTracker.Clear();
            return account.CuentaPorCobrarId;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

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
}
