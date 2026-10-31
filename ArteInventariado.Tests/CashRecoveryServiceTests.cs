using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed partial class CashRecoveryServiceTests
{
    [Theory]
    [InlineData("2026-01-15", false)]
    [InlineData("2026-01-14", true)]
    [InlineData("2025-12-31", true)]
    [InlineData("2024-01-15", true)]
    public async Task Detection_FindsOpenCashAcrossRelevantDates(string operationDate, bool stale)
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(DateOnly.Parse(operationDate), 10_000);

        var recovery = await fixture.Service.GetOpenCashRecoveryAsync();

        Assert.NotNull(recovery);
        Assert.Equal(cashId, recovery.Cash.CajaId);
        Assert.Equal(stale, recovery.IsFromPreviousDay);
        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
    }

    [Fact]
    public async Task Detection_UsesConfiguredLocalTimeZoneInsteadOfUtcDate()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC-06-test", TimeSpan.FromHours(-6), "UTC-06", "UTC-06");
        var provider = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 4, 0, 0, TimeSpan.Zero), zone);
        await using var fixture = await Fixture.CreateAsync(provider);
        await fixture.AddOpenCashAsync(new DateOnly(2025, 12, 31), 10_000);

        var recovery = await fixture.Service.GetOpenCashRecoveryAsync();

        Assert.NotNull(recovery);
        Assert.False(recovery.IsFromPreviousDay);
    }

    [Fact]
    public async Task Detection_SelectsNewestOpenCashAndReportsOlderSessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddOpenCashAsync(new DateOnly(2025, 12, 30), 5_000, openedHoursAgo: 48);
        var newest = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000, openedHoursAgo: 24);

        var recovery = await fixture.Service.GetOpenCashRecoveryAsync();

        Assert.Equal(newest, recovery!.Cash.CajaId);
        Assert.Equal(1, recovery.AdditionalOpenCashSessions);
    }

    [Fact]
    public async Task Continue_ReusesSameCashWithoutChangingOpeningAndAuditsOnceOnRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 12_300);
        var before = await fixture.ReadCashAsync(cashId);
        var operationId = Guid.NewGuid();

        var first = await fixture.Service.ContinueCashAsync(new(cashId, operationId));
        var retry = await fixture.Service.ContinueCashAsync(new(cashId, operationId));
        var after = await fixture.ReadCashAsync(cashId);

        Assert.Equal(cashId, first.CajaId);
        Assert.Equal(cashId, retry.CajaId);
        Assert.Equal(before.FechaApertura, after.FechaApertura);
        Assert.Equal(before.MontoInicialCentavos, after.MontoInicialCentavos);
        Assert.Equal(EstadoCaja.ABIERTA, after.Estado);
        Assert.Equal(1, await fixture.CountAuditsAsync(TipoBitacora.CAJA_RECUPERADA, cashId));
    }

    [Fact]
    public async Task OpenCash_WhenAnyHistoricalCashIsOpen_DoesNotCreateSecondCash()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddOpenCashAsync(new DateOnly(2025, 12, 31), 5_000);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.OpenCashAsync(new OpenCashRequest(8_000)));

        Assert.Contains("continuarla o regularizarla", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await fixture.CountOpenCashAsync());
    }

    [Theory]
    [InlineData(12_000, 2_000)]
    [InlineData(8_000, -2_000)]
    [InlineData(10_000, 0)]
    public async Task Regularize_StoresRealCountAndNormalDifferenceWithRecoveryAudit(long counted, long difference)
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);

        var result = await fixture.Service.RegularizeCashAsync(new(
            cashId,
            counted,
            "Apagón durante cierre de caja",
            Fixture.Password,
            Guid.NewGuid()));
        var stored = await fixture.ReadCashAsync(cashId);
        var audit = await fixture.ReadAuditAsync(TipoBitacora.CAJA_REGULARIZADA, cashId);

        Assert.Equal(EstadoCaja.CERRADA, stored.Estado);
        Assert.Equal(counted, stored.MontoCierreCentavos);
        Assert.Equal(difference, result.DiferenciaCentavos);
        Assert.Contains("[RECUPERACION] Apagón durante cierre de caja", stored.Observaciones);
        Assert.Contains($"DiferenciaCentavos={difference}", audit.DatosDespues);
        Assert.Contains("RECUPERACION", audit.Descripcion);
    }

    [Fact]
    public async Task RegularizeClosedDay_UpdatesOnlyLastCountAndAuditsPriorAndNewValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateOnly(2026, 1, 15);
        var firstId = await fixture.AddClosedCashAsync(date, 10_000, 12_000, 2);
        var lastId = await fixture.AddClosedCashAsync(date, 12_000, 11_500, 1);
        var operationId = Guid.NewGuid();
        var request = new RegularizeClosedCashDayRequest(
            date,
            10_200,
            "Conteo corregido al revisar el efectivo físico",
            Fixture.Password,
            operationId);

        await fixture.Service.RegularizeClosedCashDayAsync(request);
        await fixture.Service.RegularizeClosedCashDayAsync(request);

        var first = await fixture.ReadCashAsync(firstId);
        var latest = await fixture.ReadCashAsync(lastId);
        var audit = await fixture.ReadAuditAsync(TipoBitacora.CAJA_REGULARIZADA, lastId);
        var history = await fixture.Service.GetSessionHistoryAsync(date, date);
        var day = Assert.Single(CashDailySummaryCalculator.Aggregate(history));

        Assert.Equal(12_000, first.MontoCierreCentavos);
        Assert.Equal(10_200, latest.MontoCierreCentavos);
        Assert.Equal(EstadoCaja.CERRADA, latest.Estado);
        Assert.Contains("CierreCentavos=11500", audit.DatosAntes);
        Assert.Contains("ContadoCentavos=10200", audit.DatosDespues);
        Assert.Contains("DiferenciaCentavos=200", audit.DatosDespues);
        Assert.Contains("Operacion=", audit.Descripcion);
        Assert.Equal(1, await fixture.CountAuditsAsync(TipoBitacora.CAJA_REGULARIZADA, lastId));
        Assert.Equal(2, day.CantidadSesiones);
        Assert.Equal(10_000, day.EfectivoEsperadoAcumuladoCentavos);
        Assert.Equal(10_200, day.UltimoCierreContadoCentavos);
        Assert.Equal(200, day.DiferenciaFinalCentavos);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegularizeClosedCashDayAsync(request with { CountedCashCentavos = 9_000 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegularizeClosedCashDayAsync(request with { Reason = "Conteo corregido" }));
        Assert.Equal(10_200, (await fixture.ReadCashAsync(lastId)).MontoCierreCentavos);
    }

    [Fact]
    public async Task RegularizeClosedDay_RequiresAdminAndEverySessionClosed()
    {
        var date = new DateOnly(2026, 1, 15);
        await using (var employee = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO))
        {
            await employee.AddClosedCashAsync(date, 10_000, 10_000, 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => employee.Service.RegularizeClosedCashDayAsync(new(
                date, 10_000, "Corrección", Fixture.Password, Guid.NewGuid())));
            await using var context = employee.CreateContext();
            Assert.Empty(await context.Bitacora.Where(entry => entry.TipoAccion == TipoBitacora.CAJA_REGULARIZADA).ToListAsync());
        }

        await using (var fixture = await Fixture.CreateAsync())
        {
            await fixture.AddClosedCashAsync(date, 10_000, 10_000, 2);
            await fixture.AddOpenCashAsync(date, 10_000, openedHoursAgo: 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegularizeClosedCashDayAsync(new(
                date, 10_000, "Corrección", Fixture.Password, Guid.NewGuid())));
            Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.CAJA_REGULARIZADA, null));
        }
    }

    [Fact]
    public async Task RegularizeClosedDay_RequiresCurrentPasswordAndRejectsConsultaWrites()
    {
        var date = new DateOnly(2026, 1, 15);
        await using (var fixture = await Fixture.CreateAsync())
        {
            var cashId = await fixture.AddClosedCashAsync(date, 10_000, 10_500, 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegularizeClosedCashDayAsync(new(
                date, 10_000, "Conteo real", "contraseña incorrecta", Guid.NewGuid())));
            Assert.Equal(10_500, (await fixture.ReadCashAsync(cashId)).MontoCierreCentavos);
            Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.CAJA_REGULARIZADA, cashId));
        }

        await using (var consulta = await Fixture.CreateAsync(allowsWrites: false))
        {
            var cashId = await consulta.AddClosedCashAsync(date, 10_000, 10_500, 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => consulta.Service.RegularizeClosedCashDayAsync(new(
                date, 10_000, "Conteo real", Fixture.Password, Guid.NewGuid())));
            Assert.Equal(10_500, (await consulta.ReadCashAsync(cashId)).MontoCierreCentavos);
            Assert.Equal(0, await consulta.CountAuditsAsync(TipoBitacora.CAJA_REGULARIZADA, cashId));
        }
    }

    [Theory]
    [InlineData(0, "", true)]
    [InlineData(0, "Apagón", false)]
    [InlineData(-1, "Apagón", true)]
    public async Task Regularize_RequiresValidCountReasonAndPassword(long counted, string reason, bool useValidPassword)
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);
        var password = useValidPassword ? Fixture.Password : string.Empty;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Service.RegularizeCashAsync(new(
            cashId, counted, reason, password, Guid.NewGuid())));

        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
    }

    [Fact]
    public async Task Regularize_RejectsIncorrectCurrentPassword()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegularizeCashAsync(new(cashId, 10_000, "Apagón", "incorrecta", Guid.NewGuid())));

        Assert.Contains("contrasena actual", error.Message, StringComparison.OrdinalIgnoreCase);
        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
    }

    [Fact]
    public async Task UnauthorizedEmployee_CannotContinueOrRegularize()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ContinueCashAsync(new(cashId, Guid.NewGuid())));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegularizeCashAsync(new(cashId, 10_000, "Apagón", Fixture.Password, Guid.NewGuid())));

        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
    }

    [Fact]
    public async Task Consulta_CanDetectButRejectsRecoveryWrites()
    {
        await using var fixture = await Fixture.CreateAsync(allowsWrites: false);
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);

        Assert.NotNull(await fixture.Service.GetOpenCashRecoveryAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ContinueCashAsync(new(cashId, Guid.NewGuid())));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegularizeCashAsync(new(cashId, 10_000, "Apagón", Fixture.Password, Guid.NewGuid())));
        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
    }

    [Fact]
    public async Task Regularize_IsIdempotentForSameOperationAndRejectsDifferentSecondOperation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);
        var operationId = Guid.NewGuid();
        var request = new RegularizeCashRequest(cashId, 9_500, "Cierre recuperado", Fixture.Password, operationId);

        var first = await fixture.Service.RegularizeCashAsync(request);
        var retry = await fixture.Service.RegularizeCashAsync(request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegularizeCashAsync(
            request with { OperationId = Guid.NewGuid() }));

        Assert.Equal(first.DiferenciaCentavos, retry.DiferenciaCentavos);
        Assert.Equal(1, await fixture.CountAuditsAsync(TipoBitacora.CAJA_REGULARIZADA, cashId));
    }

    [Fact]
    public async Task Regularize_WhenAuditInsertFails_RollsBackCashClosure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);
        await fixture.ExecuteSqlAsync("""
            CREATE TRIGGER fail_cash_recovery_audit
            BEFORE INSERT ON bitacora
            WHEN NEW.tipo_accion = 'CAJA_REGULARIZADA'
            BEGIN
                SELECT RAISE(ABORT, 'simulated recovery audit failure');
            END;
            """);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => fixture.Service.RegularizeCashAsync(new(
            cashId, 9_500, "Cierre recuperado", Fixture.Password, Guid.NewGuid())));

        var stored = await fixture.ReadCashAsync(cashId);
        Assert.Equal(EstadoCaja.ABIERTA, stored.Estado);
        Assert.Null(stored.FechaCierre);
        Assert.Equal(0, stored.MontoCierreCentavos);
        Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.CAJA_REGULARIZADA, cashId));
    }

    [Fact]
    public async Task OrdinaryClose_RefusesToDisguiseStaleCashAsNormalClosure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CloseCashAsync(new CloseCashRequest(10_000)));

        Assert.Contains("regularizar", error.Message, StringComparison.OrdinalIgnoreCase);
        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
        Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.CAJA_CERRADA, cashId));
    }

    [Fact]
    public async Task OrdinaryClose_AdminIncidentReasonIsAuditOnlyAndDoesNotChangeExpectedCash()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);

        var summary = await fixture.Service.CloseCashAsync(new CloseCashRequest(
            8_000,
            MotivoImprevisto: "Se derramó efectivo durante un accidente en caja."));
        var stored = await fixture.ReadCashAsync(cashId);
        var audit = await fixture.ReadAuditAsync(TipoBitacora.CAJA_CERRADA, cashId);
        await using var context = fixture.CreateContext();

        Assert.Equal(8_000, stored.MontoCierreCentavos);
        Assert.Null(stored.Observaciones);
        Assert.Equal(10_000, summary.TotalEsperadoCentavos);
        Assert.Equal(-2_000, summary.DiferenciaCentavos);
        Assert.Contains("Incidente imprevisto reportado por ADMIN (solo auditoría)", audit.Descripcion);
        Assert.Contains("Se derramó efectivo durante un accidente en caja.", audit.Descripcion);
        Assert.Equal(0, await context.Ventas.CountAsync(sale => sale.CajaId == cashId));
        Assert.Equal(0, await context.PagosVenta.CountAsync(payment => payment.Venta.CajaId == cashId));
        Assert.Equal(0, await context.Gastos.CountAsync(expense => expense.CajaId == cashId));
        Assert.Equal(0, await context.RetirosCaja.CountAsync(withdrawal => withdrawal.CajaId == cashId));
        Assert.Equal(1, await fixture.CountAuditsAsync(TipoBitacora.CAJA_CERRADA, cashId));
    }

    [Fact]
    public async Task OrdinaryClose_EmployeeCannotSupplyAdminIncidentReason()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CloseCashAsync(new CloseCashRequest(
                8_000,
                MotivoImprevisto: "Incidente reportado por empleado.")));

        Assert.Contains("Solo un ADMIN", error.Message, StringComparison.OrdinalIgnoreCase);
        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
        Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.CAJA_CERRADA, cashId));
    }

    [Fact]
    public async Task OrdinaryClose_AdminIncidentReasonRequiresActualDiscrepancy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.CloseCashAsync(new CloseCashRequest(
                10_000,
                MotivoImprevisto: "No hubo diferencia.")));

        Assert.Contains("solo se registra cuando", error.Message, StringComparison.OrdinalIgnoreCase);
        await fixture.AssertCashStateAsync(cashId, EstadoCaja.ABIERTA);
        Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.CAJA_CERRADA, cashId));
    }

    [Fact]
    public async Task ContinuedStaleCash_AcceptsMovementsOnSameCashWithoutChangingSessionDate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 14), 10_000);
        await fixture.Service.ContinueCashAsync(new(cashId, Guid.NewGuid()));

        var withdrawal = await fixture.Service.RegisterWithdrawalAsync(new(500, "Cambio de efectivo"));
        var cash = await fixture.ReadCashAsync(cashId);

        Assert.Equal(cashId, withdrawal.CajaId);
        Assert.Equal(new DateOnly(2026, 1, 14), cash.FechaOperacion);
        Assert.Equal(EstadoCaja.ABIERTA, cash.Estado);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly string Password = TestCredentials.Password("cash");
        private readonly SqliteConnection _connection;
        private readonly SharedConnectionContextFactory _factory;
        private readonly AppDbContext _permissionContext;

        private Fixture(
            SqliteConnection connection,
            SharedConnectionContextFactory factory,
            AppDbContext permissionContext,
            CashService service,
            int userId,
            FixedTimeProvider timeProvider)
        {
            _connection = connection;
            _factory = factory;
            _permissionContext = permissionContext;
            Service = service;
            UserId = userId;
            TimeProvider = timeProvider;
        }

        public CashService Service { get; }
        public int UserId { get; }
        public FixedTimeProvider TimeProvider { get; }

        public static async Task<Fixture> CreateAsync(
            FixedTimeProvider? timeProvider = null,
            RolUsuario role = RolUsuario.ADMIN,
            bool allowsWrites = true)
        {
            timeProvider ??= new FixedTimeProvider(
                new DateTimeOffset(2026, 1, 15, 18, 0, 0, TimeSpan.Zero),
                TimeZoneInfo.Utc);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var factory = new SharedConnectionContextFactory(connection);
            int userId;
            await using (var setup = factory.CreateDbContext())
            {
                await setup.Database.EnsureCreatedAsync();
                var user = new Usuario
                {
                    NombreCompleto = "Usuario recuperación",
                    NombreUsuario = "recovery-user",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                    Rol = role,
                    Activo = true,
                    FechaCreacion = timeProvider.GetUtcNow().UtcDateTime
                };
                setup.Usuarios.Add(user);
                await setup.SaveChangesAsync();
                userId = user.UsuarioId;
            }

            var session = new TestSession(new UsuarioSesion(userId, "Usuario recuperación", "recovery-user", role));
            var audit = new AuditService(factory);
            var permissionContext = factory.CreateDbContext();
            var permissions = new EmployeePermissionService(permissionContext, session, audit);
            var installation = new TestInstallationContext(allowsWrites);
            var service = new CashService(
                factory,
                session,
                audit,
                new NullNotificationService(),
                permissions,
                installation,
                timeProvider);
            return new Fixture(connection, factory, permissionContext, service, userId, timeProvider);
        }

        public async Task<int> AddOpenCashAsync(DateOnly operationDate, long initial, int openedHoursAgo = 12)
        {
            await using var context = _factory.CreateDbContext();
            var cash = new Caja
            {
                FechaOperacion = operationDate,
                FechaApertura = TimeProvider.GetUtcNow().AddHours(-openedHoursAgo).UtcDateTime,
                MontoInicialCentavos = initial,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = UserId
            };
            context.Cajas.Add(cash);
            await context.SaveChangesAsync();
            return cash.CajaId;
        }

        public async Task<int> AddClosedCashAsync(
            DateOnly operationDate,
            long initial,
            long closing,
            int openedHoursAgo)
        {
            await using var context = _factory.CreateDbContext();
            var openedAt = TimeProvider.GetUtcNow().AddHours(-openedHoursAgo).UtcDateTime;
            var cash = new Caja
            {
                FechaOperacion = operationDate,
                FechaApertura = openedAt,
                FechaCierre = openedAt.AddMinutes(30),
                MontoInicialCentavos = initial,
                MontoCierreCentavos = closing,
                Estado = EstadoCaja.CERRADA,
                UsuarioAperturaId = UserId,
                UsuarioCierreId = UserId
            };
            context.Cajas.Add(cash);
            await context.SaveChangesAsync();
            return cash.CajaId;
        }

        public async Task<Caja> ReadCashAsync(int cashId)
        {
            await using var context = _factory.CreateDbContext();
            return await context.Cajas.AsNoTracking().SingleAsync(cash => cash.CajaId == cashId);
        }

        public async Task GrantPermissionAsync(PermisoEmpleado permission)
        {
            await using var context = _factory.CreateDbContext();
            context.UsuarioPermisos.Add(new UsuarioPermiso
            {
                UsuarioId = UserId,
                Permiso = permission,
                Habilitado = true,
                FechaActualizacion = TimeProvider.GetUtcNow().UtcDateTime
            });
            await context.SaveChangesAsync();
        }

        public AppDbContext CreateContext() => _factory.CreateDbContext();

        public async Task<Bitacora> ReadAuditAsync(TipoBitacora type, int cashId)
        {
            await using var context = _factory.CreateDbContext();
            return await context.Bitacora.AsNoTracking().SingleAsync(entry =>
                entry.TipoAccion == type && entry.EntidadId == cashId);
        }

        public async Task<int> CountAuditsAsync(TipoBitacora type, int? cashId)
        {
            await using var context = _factory.CreateDbContext();
            return await context.Bitacora.CountAsync(entry => entry.TipoAccion == type &&
                (!cashId.HasValue || entry.EntidadId == cashId));
        }

        public async Task<int> CountOpenCashAsync()
        {
            await using var context = _factory.CreateDbContext();
            return await context.Cajas.CountAsync(cash => cash.Estado == EstadoCaja.ABIERTA);
        }

        public async Task AssertCashStateAsync(int cashId, EstadoCaja state)
        {
            var cash = await ReadCashAsync(cashId);
            Assert.Equal(state, cash.Estado);
        }

        public async Task ExecuteSqlAsync(string sql)
        {
            await using var context = _factory.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync(sql);
        }

        public async ValueTask DisposeAsync()
        {
            await _permissionContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SharedConnectionContextFactory(SqliteConnection connection)
        : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        public AppDbContext CreateDbContext() => new(_options);
    }

    private sealed class TestSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class TestInstallationContext(bool allowsWrites) : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            allowsWrites ? InstallationMode.Principal : InstallationMode.Consulta,
            true,
            Guid.NewGuid(),
            1,
            "test",
            null);
        public bool AllowsOperationalWrites => allowsWrites;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!allowsWrites)
            {
                throw new InvalidOperationException("Modo Consulta no permite escrituras operativas.");
            }
        }
    }

    public sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo localTimeZone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => localTimeZone;
    }
}
