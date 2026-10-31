using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class PersistenceStabilityTests
{
    [Fact]
    public async Task LegacyLongLivedContext_RolledBackAudit_ReproducesGeneratedKeyCollision()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var legacyContext = fixture.Factory.CreateDbContext();
        await using (var transaction = await legacyContext.Database.BeginTransactionAsync())
        {
            legacyContext.Bitacora.Add(NewAudit(fixture.AdminId, "legacy-audit-first"));
            await legacyContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        legacyContext.Bitacora.Add(NewAudit(fixture.AdminId, "legacy-audit-second"));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            legacyContext.SaveChangesAsync());

        var trackingException = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Bitacora", trackingException.Message);
        Assert.Contains("same key", trackingException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyLongLivedContext_RolledBackReservation_PoisonsUnrelatedCashSave()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = await fixture.Orders.CreateDraftAsync(fixture.OrderRequest(
            [new(fixture.InStockProductId, null, 1, 1000)]));
        await using var legacyContext = fixture.Factory.CreateDbContext();
        var line = await legacyContext.DetallePedidosPersonalizados
            .AsNoTracking()
            .SingleAsync(detail => detail.PedidoPersonalizadoId == draft.PedidoPersonalizadoId);
        var sku = await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
            legacyContext, fixture.InStockProductId, CancellationToken.None);
        await legacyContext.SaveChangesAsync();
        var skuId = sku.ProductoSkuId;
        var reservation = new ReservaInventarioPedido
        {
            PedidoPersonalizadoId = draft.PedidoPersonalizadoId,
            DetallePedidoPersonalizadoId = line.DetallePedidoPersonalizadoId,
            ProductoId = fixture.InStockProductId,
            ProductoSkuId = skuId,
            Ubicacion = UbicacionInventario.SIN_ASIGNAR,
            CantidadReservada = 1,
            Estado = EstadoReservaInventarioPedido.ACTIVA,
            FechaReserva = DateTime.UtcNow,
            UsuarioId = fixture.AdminId,
            Motivo = "Reserva de reproduccion."
        };
        await using (var transaction = await legacyContext.Database.BeginTransactionAsync())
        {
            legacyContext.ReservasInventarioPedido.Add(reservation);
            await legacyContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        reservation.Estado = EstadoReservaInventarioPedido.LIBERADA;
        reservation.CantidadLiberada = 1;
        legacyContext.Cajas.Add(new Caja
        {
            FechaOperacion = DateOnly.FromDateTime(DateTime.Today),
            FechaApertura = DateTime.UtcNow,
            Estado = EstadoCaja.ABIERTA,
            UsuarioAperturaId = fixture.AdminId,
            MontoInicialCentavos = 0,
            MontoCierreCentavos = 0
        });

        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            legacyContext.SaveChangesAsync());

        Assert.Contains(exception.Entries, entry => entry.Entity is ReservaInventarioPedido);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Empty(await verification.Cajas.ToListAsync());
        Assert.Empty(await verification.ReservasInventarioPedido
            .Where(item => item.PedidoPersonalizadoId == draft.PedidoPersonalizadoId)
            .ToListAsync());
    }

    [Fact]
    public async Task PersistenceGuard_RealMissingRow_ProducesFriendlyDiagnosticConflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Factory.CreateDbContext();
        var missingCash = new Caja
        {
            CajaId = 999999,
            FechaOperacion = DateOnly.FromDateTime(DateTime.Today),
            FechaApertura = DateTime.UtcNow,
            Estado = EstadoCaja.CERRADA,
            UsuarioAperturaId = fixture.AdminId,
            MontoInicialCentavos = 0,
            MontoCierreCentavos = 0
        };
        context.Attach(missingCash);
        missingCash.Observaciones = "Cambio concurrente simulado.";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PersistenceGuard.SaveChangesAsync(
                context,
                "cash-concurrency-test",
                "Mensaje amigable de caja.",
                CancellationToken.None));

        Assert.Equal("Mensaje amigable de caja.", exception.Message);
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
        var diagnostics = Assert.IsType<string>(exception.Data["EFConcurrencyDiagnostics"]);
        Assert.Contains("Entity=Caja", diagnostics);
        Assert.Contains("CajaId=999999", diagnostics);
        Assert.Contains("Database=<row-not-found>", diagnostics);
    }

    [Fact]
    public async Task AuditService_AfterRolledBackGeneratedId_WritesDistinctEntries()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var poisonedContext = fixture.Factory.CreateDbContext())
        await using (var transaction = await poisonedContext.Database.BeginTransactionAsync())
        {
            poisonedContext.Bitacora.Add(NewAudit(fixture.AdminId, "rollback-probe"));
            await poisonedContext.SaveChangesAsync();
            await transaction.RollbackAsync();
            Assert.Single(poisonedContext.ChangeTracker.Entries<Bitacora>());
        }

        for (var index = 0; index < 25; index++)
        {
            await fixture.Audit.RegistrarAsync(new(
                fixture.AdminId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "stability_test",
                index,
                $"Registro consecutivo {index}."));
        }

        await using var verification = fixture.Factory.CreateDbContext();
        var ids = await verification.Bitacora
            .Where(entry => entry.Entidad == "stability_test")
            .Select(entry => entry.BitacoraId)
            .ToListAsync();
        Assert.Equal(25, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task AuthService_TwentyLoginLogoutCycles_DoNotShareTrackedAuditEntries()
    {
        await using var fixture = await Fixture.CreateAsync();
        var auth = new AuthService(fixture.Factory, fixture.Audit, fixture.Session);

        for (var cycle = 0; cycle < 20; cycle++)
        {
            var result = await auth.LoginAsync(Fixture.AdminUserName, Fixture.AdminPassword);
            Assert.True(result.Exitoso);
            Assert.True(fixture.Session.EstaAutenticado);
            await auth.LogoutAsync();
            Assert.False(fixture.Session.EstaAutenticado);
        }

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal(20, await verification.Bitacora.CountAsync(entry => entry.TipoAccion == TipoBitacora.LOGIN));
        Assert.Equal(20, await verification.Bitacora.CountAsync(entry => entry.TipoAccion == TipoBitacora.LOGOUT));
        var ids = await verification.Bitacora.Select(entry => entry.BitacoraId).ToListAsync();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task AuthService_SuccessPersistsExactlyOneEmployeeLoginEvent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var auth = new AuthService(fixture.Factory, fixture.Audit, fixture.Session);
        var result = await auth.LoginAsync(Fixture.AdminUserName, Fixture.AdminPassword);
        Assert.True(result.Exitoso);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal(1, await verification.EmployeeLoginEvents.CountAsync(e => e.UserId == fixture.AdminId));
    }

    [Fact]
    public async Task AuthService_FailedLoginPersistsNoEmployeeLoginEvent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var auth = new AuthService(fixture.Factory, fixture.Audit, fixture.Session);
        var result = await auth.LoginAsync(Fixture.AdminUserName, "invalid-password");
        Assert.False(result.Exitoso);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Empty(await verification.EmployeeLoginEvents.Where(e => e.UserId == fixture.AdminId).ToListAsync());
    }

    [Fact]
    public async Task Logout_WhenAuditFails_StillClearsCurrentSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        var auth = new AuthService(fixture.Factory, new FailingAuditService(), fixture.Session);

        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.LogoutAsync());

        Assert.False(fixture.Session.EstaAutenticado);
        Assert.Null(fixture.Session.UsuarioActual);
    }

    [Fact]
    public async Task CashService_UsesCleanContext_AndBlocksSecondOpen()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var poisonedContext = fixture.Factory.CreateDbContext())
        await using (var transaction = await poisonedContext.Database.BeginTransactionAsync())
        {
            poisonedContext.Bitacora.Add(NewAudit(fixture.AdminId, "cash-rollback-probe"));
            await poisonedContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        var service = fixture.CreateCashService();
        var opened = await service.OpenCashAsync(new OpenCashRequest(2500));
        Assert.Equal(EstadoCaja.ABIERTA, opened.Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.OpenCashAsync(new OpenCashRequest(2500)));

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Single(await verification.Cajas.Where(cash => cash.Estado == EstadoCaja.ABIERTA).ToListAsync());
        Assert.Equal(1, await verification.Bitacora.CountAsync(entry => entry.TipoAccion == TipoBitacora.CAJA_ABIERTA));
    }

    [Fact]
    public async Task FailedConfirmation_RollsBackCompletely_ThenCancellationSucceeds()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = await fixture.Orders.CreateDraftAsync(fixture.OrderRequest(
        [
            new(fixture.InStockProductId, null, 1, 1000),
            new(fixture.OutOfStockProductId, null, 1, 1000)
        ]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Orders.ConfirmAsync(draft.PedidoPersonalizadoId));
        Assert.Contains("Stock disponible insuficiente", exception.Message);

        await using (var verification = fixture.Factory.CreateDbContext())
        {
            var stored = await verification.PedidosPersonalizados
                .AsNoTracking()
                .SingleAsync(order => order.PedidoPersonalizadoId == draft.PedidoPersonalizadoId);
            Assert.Equal(EstadoPedidoPersonalizado.BORRADOR, stored.Estado);
            Assert.Empty(await verification.ReservasInventarioPedido
                .Where(reservation => reservation.PedidoPersonalizadoId == draft.PedidoPersonalizadoId)
                .ToListAsync());
            Assert.Single(await verification.HistorialEstadoPedidos
                .Where(history => history.PedidoPersonalizadoId == draft.PedidoPersonalizadoId)
                .ToListAsync());
            Assert.Empty(await verification.Bitacora
                .Where(entry => entry.EntidadId == draft.PedidoPersonalizadoId &&
                    entry.TipoAccion == TipoBitacora.PEDIDO_CONFIRMADO)
                .ToListAsync());
        }

        var cancelled = await fixture.Orders.ChangeStateAsync(new(
            draft.PedidoPersonalizadoId,
            EstadoPedidoPersonalizado.CANCELADO,
            DateTime.Now,
            "Cancelacion posterior al fallo de reserva."));
        Assert.Equal(EstadoPedidoPersonalizado.CANCELADO, cancelled.Estado);

        await using var finalVerification = fixture.Factory.CreateDbContext();
        Assert.Equal(1, await finalVerification.Bitacora.CountAsync(entry =>
            entry.EntidadId == draft.PedidoPersonalizadoId &&
            entry.TipoAccion == TipoBitacora.PEDIDO_CANCELADO));
    }

    [Fact]
    public async Task CancelConfirmedOrder_ReleasesReservationAtomically_AndAuditsOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = await fixture.Orders.CreateDraftAsync(fixture.OrderRequest(
            [new(fixture.InStockProductId, null, 2, 1000)]));
        await fixture.Orders.ConfirmAsync(draft.PedidoPersonalizadoId);

        await using (var beforeCancellation = fixture.Factory.CreateDbContext())
        {
            var active = await beforeCancellation.ReservasInventarioPedido.SingleAsync(reservation =>
                reservation.PedidoPersonalizadoId == draft.PedidoPersonalizadoId);
            Assert.Equal(EstadoReservaInventarioPedido.ACTIVA, active.Estado);
        }

        await fixture.Orders.ChangeStateAsync(new(
            draft.PedidoPersonalizadoId,
            EstadoPedidoPersonalizado.CANCELADO,
            DateTime.Now,
            "Cliente cancelo el pedido."));

        await using var verification = fixture.Factory.CreateDbContext();
        var reservation = await verification.ReservasInventarioPedido.SingleAsync(item =>
            item.PedidoPersonalizadoId == draft.PedidoPersonalizadoId);
        Assert.Equal(EstadoReservaInventarioPedido.LIBERADA, reservation.Estado);
        Assert.Equal(reservation.CantidadReservada, reservation.CantidadLiberada);
        Assert.Equal(1, await verification.Bitacora.CountAsync(entry =>
            entry.EntidadId == draft.PedidoPersonalizadoId &&
            entry.TipoAccion == TipoBitacora.PEDIDO_CANCELADO));
        Assert.Equal(1, await verification.Bitacora.CountAsync(entry =>
            entry.EntidadId == draft.PedidoPersonalizadoId &&
            entry.TipoAccion == TipoBitacora.RESERVA_PEDIDO_LIBERADA));
    }

    private static Bitacora NewAudit(int userId, string entity) => new()
    {
        UsuarioId = userId,
        TipoAccion = TipoBitacora.CONFIGURACION_MODIFICADA,
        Entidad = entity,
        Descripcion = "Prueba de rollback.",
        FechaAccion = DateTime.UtcNow
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public const string AdminUserName = "admin-stability";
        public static readonly string AdminPassword = TestCredentials.Password("stability");

        private readonly SqliteConnection _connection;
        private readonly AppDbContext _permissionContext;
        private readonly AppDbContext _queryContext;

        private Fixture(
            SqliteConnection connection,
            SharedConnectionContextFactory factory,
            CurrentSession session,
            AuditService audit,
            EmployeePermissionService permissions,
            AppDbContext permissionContext,
            AppDbContext queryContext,
            CustomOrderService orders,
            int adminId,
            int inStockProductId,
            int outOfStockProductId)
        {
            _connection = connection;
            _permissionContext = permissionContext;
            _queryContext = queryContext;
            Factory = factory;
            Session = session;
            Audit = audit;
            Permissions = permissions;
            Orders = orders;
            AdminId = adminId;
            InStockProductId = inStockProductId;
            OutOfStockProductId = outOfStockProductId;
        }

        public SharedConnectionContextFactory Factory { get; }
        public CurrentSession Session { get; }
        public AuditService Audit { get; }
        public EmployeePermissionService Permissions { get; }
        public CustomOrderService Orders { get; }
        public int AdminId { get; }
        public int InStockProductId { get; }
        public int OutOfStockProductId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var factory = new SharedConnectionContextFactory(connection);
            int adminId;
            int inStockProductId;
            int outOfStockProductId;

            await using (var setup = factory.CreateDbContext())
            {
                await setup.Database.EnsureCreatedAsync();
                var admin = new Usuario
                {
                    NombreCompleto = "Administrador estabilidad",
                    NombreUsuario = AdminUserName,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(AdminPassword),
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                };
                setup.Usuarios.Add(admin);
                var category = new Categoria
                {
                    Nombre = $"Estabilidad-{Guid.NewGuid():N}",
                    Activa = true,
                    FechaCreacion = DateTime.UtcNow
                };
                setup.Categorias.Add(category);
                await setup.SaveChangesAsync();

                var inStock = Product("STABLE-1", "Producto con stock", category.CategoriaId, 10);
                var outOfStock = Product("STABLE-0", "Producto sin stock", category.CategoriaId, 0);
                setup.Productos.AddRange(inStock, outOfStock);
                await setup.SaveChangesAsync();
                adminId = admin.UsuarioId;
                inStockProductId = inStock.ProductoId;
                outOfStockProductId = outOfStock.ProductoId;
            }

            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                adminId,
                "Administrador estabilidad",
                AdminUserName,
                RolUsuario.ADMIN));
            var audit = new AuditService(factory);
            var permissionContext = factory.CreateDbContext();
            var permissions = new EmployeePermissionService(permissionContext, session, audit);
            var queryContext = factory.CreateDbContext();
            var query = new CustomOrderQueryService(queryContext, permissions);
            var orders = new CustomOrderService(factory, session, permissions, audit, query);

            return new Fixture(
                connection,
                factory,
                session,
                audit,
                permissions,
                permissionContext,
                queryContext,
                orders,
                adminId,
                inStockProductId,
                outOfStockProductId);
        }

        public CashService CreateCashService() => new(
            Factory,
            Session,
            Audit,
            new NullNotificationService(),
            Permissions);

        public CustomOrderSaveRequest OrderRequest(IReadOnlyList<CustomOrderLineRequest> lines) => new(
            null,
            null,
            "Cliente de estabilidad",
            null,
            null,
            null,
            DateTime.Today,
            DateTime.Today.AddDays(7),
            PrioridadPedido.NORMAL,
            0,
            "Pedido para prueba de estabilidad",
            null,
            null,
            null,
            lines);

        public async ValueTask DisposeAsync()
        {
            await _queryContext.DisposeAsync();
            await _permissionContext.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Producto Product(string code, string name, int categoryId, int stock) => new()
        {
            CodigoInterno = code,
            Nombre = name,
            CategoriaId = categoryId,
            PrecioCompraCentavos = 500,
            PrecioVentaCentavos = 1000,
            StockActual = stock,
            StockMinimo = 1,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        };
    }

    public sealed class SharedConnectionContextFactory(SqliteConnection connection)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
    }

    private sealed class FailingAuditService : IAuditService
    {
        public Task RegistrarAsync(
            AuditEntryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallo simulado de bitacora.");
    }
}
