using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class CustomOrderFinanceServiceTests
{
    [Fact]
    public async Task CashDeposit_RequiresOpenCash()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PayAsync(order, MetodoPago.EFECTIVO, 300));
    }

    [Fact]
    public async Task CashDeposit_WithOpenCash_UpdatesBalanceAndCash()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OpenCashAsync();
        var order = await f.ConfirmedAsync();
        var payment = await f.PayAsync(order, MetodoPago.EFECTIVO, 300);
        Assert.True(payment.AfectaCaja);
        Assert.NotNull(payment.CajaId);
        Assert.Equal(700, payment.SaldoNuevoCentavos);
        Assert.Equal(EstadoFinancieroPedido.PARCIAL,
            await f.Context.PedidosPersonalizados.Where(p => p.PedidoPersonalizadoId == order.PedidoPersonalizadoId)
                .Select(p => p.EstadoFinanciero).SingleAsync());
        var cash = await f.CreateCashService().GetTodaySummaryAsync();
        Assert.Equal(300, cash.TotalVentasCentavos);
        Assert.Equal(1300, cash.TotalEsperadoCentavos);
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA)]
    [InlineData(MetodoPago.BITCOIN)]
    public async Task ElectronicPayment_RequiresReference(MetodoPago method)
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.PayAsync(order, method, 200, " "));
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA)]
    [InlineData(MetodoPago.BITCOIN)]
    public async Task ElectronicPayment_DoesNotAffectPhysicalCash(MetodoPago method)
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        var payment = await f.PayAsync(order, method, 200, "REF-1");
        Assert.False(payment.AfectaCaja);
        Assert.Null(payment.CajaId);
    }

    [Fact]
    public async Task Payment_CannotExceedBalance()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.PayAsync(order, MetodoPago.TRANSFERENCIA, 1001, "REF"));
    }

    [Fact]
    public async Task Payment_RejectsZero()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.PayAsync(order, MetodoPago.TRANSFERENCIA, 0, "REF"));
    }

    [Fact]
    public async Task SameOperationId_IsIdempotentAndConflictIsBlocked()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        var operation = Guid.NewGuid();
        var first = await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 200, "REF", operation);
        var retry = await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 200, "REF", operation);
        Assert.Equal(first.PagoPedidoPersonalizadoId, retry.PagoPedidoPersonalizadoId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.PayAsync(order, MetodoPago.TRANSFERENCIA, 201, "REF", operation));
        Assert.Equal(1, await f.Context.PagosPedidoPersonalizado.CountAsync());
    }

    [Fact]
    public async Task FullPayment_MarksOrderPaid()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 1000, "REF");
        var stored = await f.Context.PedidosPersonalizados.FindAsync(order.PedidoPersonalizadoId);
        Assert.Equal(EstadoFinancieroPedido.PAGADO, stored!.EstadoFinanciero);
        Assert.Equal(0, stored.SaldoPendienteCentavos);
    }

    [Fact]
    public async Task PartialAndFullRefund_UpdateFinancialState()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 1000, "PAY");
        await f.RefundAsync(order, MetodoPago.TRANSFERENCIA, 300, "REFUND-1");
        Assert.Equal(EstadoFinancieroPedido.REEMBOLSADO_PARCIAL,
            (await f.Context.PedidosPersonalizados.FindAsync(order.PedidoPersonalizadoId))!.EstadoFinanciero);
        await f.RefundAsync(order, MetodoPago.TRANSFERENCIA, 700, "REFUND-2");
        Assert.Equal(EstadoFinancieroPedido.REEMBOLSADO,
            (await f.Context.PedidosPersonalizados.FindAsync(order.PedidoPersonalizadoId))!.EstadoFinanciero);
    }

    [Fact]
    public async Task Refund_CannotExceedNetPaid()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 200, "PAY");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.RefundAsync(order, MetodoPago.TRANSFERENCIA, 201, "REF"));
    }

    [Fact]
    public async Task CashRefund_RequiresCashAndRepresentsOutflow()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 300, "PAY");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.RefundAsync(order, MetodoPago.EFECTIVO, 100));
        await f.OpenCashAsync();
        var refund = await f.RefundAsync(order, MetodoPago.EFECTIVO, 100);
        Assert.True(refund.AfectaCaja);
        Assert.NotNull(refund.CajaId);
        var cash = await f.CreateCashService().GetTodaySummaryAsync();
        Assert.Equal(100, cash.TotalReembolsosCentavos);
        Assert.Equal(900, cash.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task Cancel_DoesNotRefundAndReleasesReservation()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 200, "PAY");
        await f.OrderService.ChangeStateAsync(new(order.PedidoPersonalizadoId,
            EstadoPedidoPersonalizado.CANCELADO, DateTime.Now, "Cancelado"));
        Assert.Empty(await f.Context.ReembolsosPedidoPersonalizado.ToListAsync());
        Assert.Equal(EstadoReservaInventarioPedido.LIBERADA,
            await f.Context.ReservasInventarioPedido.Select(r => r.Estado).SingleAsync());
    }

    [Fact]
    public async Task Confirmation_ReservesWithoutReducingPhysicalStock()
    {
        await using var f = await Fixture.CreateAsync();
        var before = await f.Context.InventarioSaldos.Where(balance => balance.ProductoSku.ProductoId == f.ProductId)
            .SumAsync(balance => balance.ExistenciaFisica);
        await f.ConfirmedAsync();
        Assert.Equal(before, await f.Context.InventarioSaldos.Where(balance => balance.ProductoSku.ProductoId == f.ProductId)
            .SumAsync(balance => balance.ExistenciaFisica));
        Assert.Equal(1, await f.Context.ReservasInventarioPedido.CountAsync(r => r.Estado == EstadoReservaInventarioPedido.ACTIVA));
    }

    [Fact]
    public async Task PosSale_RespectsReservedStock()
    {
        await using var f = await Fixture.CreateAsync(stock: 1);
        await f.ConfirmedAsync();
        await f.OpenCashAsync();
        var sale = f.CreateSaleService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sale.RegisterSaleAsync(new(
            [new(f.ProductId, 1)], [new(MetodoPago.EFECTIVO, 1000)])));
    }

    [Fact]
    public async Task DescriptiveLine_MustBeMappedBeforeConversion()
    {
        await using var f = await Fixture.CreateAsync(descriptive: true);
        var ready = await f.ReadyAsync();
        await f.OpenCashAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Finance.ConvertToSaleAsync(new(ready.PedidoPersonalizadoId, Guid.NewGuid(),
                [new(MetodoPago.TRANSFERENCIA, 1000, "REF")])));
        var lineId = await f.Context.DetallePedidosPersonalizados.Select(d => d.DetallePedidoPersonalizadoId).SingleAsync();
        await f.Finance.MapProductAsync(new(lineId, f.ProductId));
        Assert.Equal(f.ProductId, await f.Context.DetallePedidosPersonalizados.Select(d => d.ProductoId).SingleAsync());
        Assert.Equal("Trabajo especial", await f.Context.DetallePedidosPersonalizados.Select(d => d.NombreDescripcionSnapshot).SingleAsync());
    }

    [Fact]
    public async Task Conversion_CreatesSaleConsumesReservationAndDecrementsStockOnce()
    {
        await using var f = await Fixture.CreateAsync(stock: 5);
        var product = await f.Context.Productos.SingleAsync(item => item.ProductoId == f.ProductId);
        var subcategory = new Subcategoria
        {
            CategoriaId = product.CategoriaId,
            Nombre = "Subcategoría pedido",
            Activa = true,
            FechaCreacion = DateTime.UtcNow,
            FechaModificacion = DateTime.UtcNow
        };
        f.Context.Subcategorias.Add(subcategory);
        await f.Context.SaveChangesAsync();
        product.SubcategoriaId = subcategory.SubcategoriaId;
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var ready = await f.ReadyAsync();
        await f.OpenCashAsync();
        var result = await f.Finance.ConvertToSaleAsync(new(
            ready.PedidoPersonalizadoId, Guid.NewGuid(), [new(MetodoPago.EFECTIVO, 1000)]));
        Assert.Equal(1, await f.Context.Ventas.CountAsync());
        Assert.Equal(4, await f.Context.InventarioSaldos.Where(balance => balance.ProductoSku.ProductoId == f.ProductId)
            .SumAsync(balance => balance.ExistenciaFisica));
        Assert.Equal(EstadoReservaInventarioPedido.CONSUMIDA,
            await f.Context.ReservasInventarioPedido.Select(r => r.Estado).SingleAsync());
        Assert.Equal(EstadoPedidoPersonalizado.ENTREGADO,
            await f.Context.PedidosPersonalizados.Where(p => p.PedidoPersonalizadoId == ready.PedidoPersonalizadoId).Select(p => p.Estado).SingleAsync());
        Assert.Equal(result.VentaId,
            await f.Context.PedidosPersonalizados.Where(p => p.PedidoPersonalizadoId == ready.PedidoPersonalizadoId).Select(p => p.VentaId).SingleAsync());
        var saleLine = await f.Context.DetalleVentas.AsNoTracking().SingleAsync();
        Assert.Equal(subcategory.SubcategoriaId, saleLine.SubcategoriaIdSnapshot);
        Assert.Equal("Subcategoría pedido", saleLine.SubcategoriaNombreSnapshot);
    }

    [Fact]
    public async Task Conversion_AppliesPriorPaymentsWithoutSecondCashImpact()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OpenCashAsync();
        var ready = await f.ReadyAsync();
        await f.PayAsync(ready, MetodoPago.EFECTIVO, 300);
        var result = await f.Finance.ConvertToSaleAsync(new(
            ready.PedidoPersonalizadoId, Guid.NewGuid(), [new(MetodoPago.EFECTIVO, 700)]));
        Assert.Equal(2, await f.Context.PagosPedidoPersonalizado.CountAsync());
        Assert.Equal(1000, await f.Context.PagosPedidoPersonalizado.SumAsync(p => p.MontoCentavos));
        Assert.Equal(1000, await f.Context.PagosVenta.SumAsync(p => p.MontoCentavos));
        Assert.All(await f.Context.PagosVenta.ToListAsync(), p => Assert.False(p.AfectaCaja));
        Assert.Equal(300, result.PagosPreviosAplicadosCentavos);
        Assert.Equal(700, result.PagoFinalCentavos);
        var cash = await f.CreateCashService().GetTodaySummaryAsync();
        Assert.Equal(1000, cash.TotalVentasCentavos);
        Assert.Equal(2000, cash.TotalEsperadoCentavos);
    }

    [Theory]
    [InlineData(MetodoPago.TRANSFERENCIA)]
    [InlineData(MetodoPago.BITCOIN)]
    public async Task Conversion_FinalElectronicPayment_DoesNotIncreasePhysicalCash(MetodoPago method)
    {
        await using var f = await Fixture.CreateAsync();
        await f.OpenCashAsync();
        var ready = await f.ReadyAsync();

        await f.Finance.ConvertToSaleAsync(new(
            ready.PedidoPersonalizadoId,
            Guid.NewGuid(),
            [new(method, 1000, "FINAL-REF")]));

        var cash = await f.CreateCashService().GetTodaySummaryAsync();
        Assert.Equal(0, cash.TotalVentasCentavos);
        Assert.Equal(1000, cash.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task Conversion_WithSameOperation_IsIdempotent()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OpenCashAsync();
        var ready = await f.ReadyAsync();
        var operation = Guid.NewGuid();
        var first = await f.Finance.ConvertToSaleAsync(new(ready.PedidoPersonalizadoId, operation,
            [new(MetodoPago.TRANSFERENCIA, 1000, "REF")]));
        var retry = await f.Finance.ConvertToSaleAsync(new(ready.PedidoPersonalizadoId, operation,
            [new(MetodoPago.TRANSFERENCIA, 1000, "REF")]));
        Assert.Equal(first.VentaId, retry.VentaId);
        Assert.Equal(1, await f.Context.Ventas.CountAsync());
    }

    [Fact]
    public async Task ConversionFailure_RollsBackSaleAndReservation()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OpenCashAsync();
        var ready = await f.ReadyAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Finance.ConvertToSaleAsync(new(ready.PedidoPersonalizadoId, Guid.NewGuid(), [])));
        Assert.Empty(await f.Context.Ventas.ToListAsync());
        Assert.Equal(EstadoReservaInventarioPedido.ACTIVA,
            await f.Context.ReservasInventarioPedido.Select(r => r.Estado).SingleAsync());
    }

    [Fact]
    public async Task CreditConversion_FinancesOnlyRemainingBalance()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OpenCashAsync();
        var ready = await f.ReadyAsync();
        await f.PayAsync(ready, MetodoPago.TRANSFERENCIA, 400, "PAY");
        var result = await f.Finance.ConvertToSaleAsync(new(
            ready.PedidoPersonalizadoId, Guid.NewGuid(), [], true, 7));
        Assert.Equal(600, result.CreditoCentavos);
        Assert.Equal(600, await f.Context.CuentasPorCobrar.Select(c => c.CapitalFinanciadoCentavos).SingleAsync());
    }

    [Fact]
    public async Task CreditConversion_RequiresRegisteredCustomer()
    {
        await using var f = await Fixture.CreateAsync(withCustomer: false);
        await f.OpenCashAsync();
        var ready = await f.ReadyAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Finance.ConvertToSaleAsync(new(ready.PedidoPersonalizadoId, Guid.NewGuid(), [], true, 7)));
    }

    [Fact]
    public async Task HistoryAndAudit_AreUnified()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 300, "PAY");
        await f.RefundAsync(order, MetodoPago.TRANSFERENCIA, 100, "REF");
        var history = await f.FinanceQuery.GetHistoryAsync(order.PedidoPersonalizadoId);
        Assert.Equal(2, history.Count);
        Assert.Contains(await f.Context.Bitacora.ToListAsync(), b => b.TipoAccion == TipoBitacora.PAGO_PEDIDO_REGISTRADO);
        Assert.Contains(await f.Context.Bitacora.ToListAsync(), b => b.TipoAccion == TipoBitacora.REEMBOLSO_PEDIDO_REGISTRADO);
    }

    [Fact]
    public async Task EmployeeWithoutPaymentPermission_IsBlocked()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.SignInEmployeeAsync(PermisoEmpleado.PEDIDOS_ACCEDER, PermisoEmpleado.PEDIDO_FINANZAS_VER);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.PayAsync(order, MetodoPago.TRANSFERENCIA, 100, "REF"));
    }

    [Fact]
    public async Task EmployeeWithPaymentPermission_IsAllowed()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.ConfirmedAsync();
        await f.SignInEmployeeAsync(
            PermisoEmpleado.PEDIDOS_ACCEDER,
            PermisoEmpleado.PEDIDO_FINANZAS_VER,
            PermisoEmpleado.PEDIDO_PAGO_REGISTRAR);
        Assert.Equal(100, (await f.PayAsync(order, MetodoPago.TRANSFERENCIA, 100, "REF")).MontoCentavos);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, AppDbContext context, TestSession session,
            AuditService audit, EmployeePermissionService permissions, CustomOrderService orderService,
            CustomOrderFinanceService finance, CustomOrderFinanceQueryService financeQuery,
            int customerId, int productId, int employeeId)
        {
            _connection = connection; Context = context; Session = session; Audit = audit;
            Permissions = permissions; OrderService = orderService; Finance = finance;
            FinanceQuery = financeQuery; CustomerId = customerId; ProductId = productId;
            EmployeeId = employeeId;
        }

        public AppDbContext Context { get; }
        public TestSession Session { get; }
        public AuditService Audit { get; }
        public EmployeePermissionService Permissions { get; }
        public CustomOrderService OrderService { get; }
        public CustomOrderFinanceService Finance { get; }
        public CustomOrderFinanceQueryService FinanceQuery { get; }
        public int CustomerId { get; }
        public int ProductId { get; }
        public int EmployeeId { get; }

        public static async Task<Fixture> CreateAsync(
            int stock = 10,
            bool descriptive = false,
            bool withCustomer = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var admin = new Usuario
            {
                NombreUsuario = "admin-finance", NombreCompleto = "Admin finance",
                PasswordHash = "x", Rol = RolUsuario.ADMIN, Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(admin);
            var employee = new Usuario
            {
                NombreUsuario = "employee-finance", NombreCompleto = "Employee finance",
                PasswordHash = "x", Rol = RolUsuario.EMPLEADO, Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(employee);
            await context.SaveChangesAsync();
            var category = new Categoria { Nombre = "Finance test", Activa = true, FechaCreacion = DateTime.UtcNow };
            var customer = new Cliente
            {
                CodigoCliente = "CLI-FIN-1", NombreCompleto = "Cliente finance",
                NombreNormalizado = "CLIENTE FINANCE", Estado = EstadoCliente.ACTIVO,
                CreditoHabilitado = true, CreditoLimiteCentavos = 100000,
                CreditoMaximoCuentasActivas = 5, CreditoPlazoMaximoDias = 30,
                FechaCreacion = DateTime.UtcNow, FechaActualizacion = DateTime.UtcNow,
                UsuarioCreacionId = admin.UsuarioId
            };
            context.Categorias.Add(category); context.Clientes.Add(customer);
            await context.SaveChangesAsync();
            var product = new Producto
            {
                CodigoInterno = "FIN-PROD-1", Nombre = "Producto finance",
                CategoriaId = category.CategoriaId, PrecioCompraCentavos = 500,
                PrecioVentaCentavos = 1000, StockActual = stock, StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO, FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = "FIN-PROD-1",
                        NombreVariante = "Principal",
                        Activo = true,
                        EsPrincipal = true,
                        SaldosInventario =
                        {
                            new InventarioSaldo
                            {
                                Ubicacion = UbicacionInventario.EXHIBICION,
                                ExistenciaFisica = stock,
                                Version = 1
                            },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, Version = 1 }
                        }
                    }
                }
            };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            var session = new TestSession(new UsuarioSesion(admin.UsuarioId, admin.NombreCompleto, admin.NombreUsuario, admin.Rol));
            var audit = new AuditService(context);
            var permissions = new EmployeePermissionService(context, session, audit);
            var query = new CustomOrderQueryService(context, permissions);
            var orderService = new CustomOrderService(context, session, permissions, audit, query);
            var credit = new CreditService(context, session, permissions, audit);
            var finance = new CustomOrderFinanceService(context, session, permissions, audit, credit);
            var financeQuery = new CustomOrderFinanceQueryService(context, permissions);
            var fixture = new Fixture(connection, context, session, audit, permissions,
                orderService, finance, financeQuery, customer.ClienteId, product.ProductoId,
                employee.UsuarioId);
            fixture.Descriptive = descriptive;
            fixture.WithCustomer = withCustomer;
            return fixture;
        }

        private bool Descriptive { get; set; }
        private bool WithCustomer { get; set; }

        public async Task SignInEmployeeAsync(params PermisoEmpleado[] permissions)
        {
            await Permissions.EnsureRecommendedPermissionsForEmployeeAsync(EmployeeId);
            foreach (var permission in permissions)
            {
                var row = await Context.UsuarioPermisos.SingleAsync(
                    p => p.UsuarioId == EmployeeId && p.Permiso == permission);
                row.Habilitado = true;
            }
            await Context.SaveChangesAsync();
            var employee = await Context.Usuarios.AsNoTracking().SingleAsync(u => u.UsuarioId == EmployeeId);
            Session.IniciarSesion(new UsuarioSesion(
                employee.UsuarioId, employee.NombreCompleto, employee.NombreUsuario, employee.Rol));
        }

        public async Task<CustomOrderDetail> ConfirmedAsync()
        {
            var line = Descriptive
                ? new CustomOrderLineRequest(null, "Trabajo especial", 1, 1000)
                : new CustomOrderLineRequest(ProductId, null, 1, 1000);
            var draft = await OrderService.CreateDraftAsync(new(
                null, WithCustomer ? CustomerId : null, WithCustomer ? null : "Destinatario manual",
                null, null, null, DateTime.Today, DateTime.Today.AddDays(7),
                PrioridadPedido.NORMAL, 0, null, null, null, null, [line]));
            return await OrderService.ConfirmAsync(draft.PedidoPersonalizadoId);
        }

        public async Task<CustomOrderDetail> ReadyAsync()
        {
            var order = await ConfirmedAsync();
            order = await OrderService.ChangeStateAsync(new(order.PedidoPersonalizadoId,
                EstadoPedidoPersonalizado.EN_PRODUCCION, DateTime.Now, "Inicio"));
            return await OrderService.ChangeStateAsync(new(order.PedidoPersonalizadoId,
                EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA, DateTime.Now, "Listo"));
        }

        public async Task OpenCashAsync()
        {
            Context.Cajas.Add(new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow, MontoInicialCentavos = 1000,
                Estado = EstadoCaja.ABIERTA, UsuarioAperturaId = Session.UsuarioActual!.UsuarioId
            });
            await Context.SaveChangesAsync();
        }

        public Task<CustomOrderPaymentResult> PayAsync(
            CustomOrderDetail order, MetodoPago method, long amount,
            string? reference = null, Guid? operation = null) =>
            Finance.RegisterPaymentAsync(new(order.PedidoPersonalizadoId, operation ?? Guid.NewGuid(),
                TipoPagoPedido.ANTICIPO, method, amount, reference));

        public Task<CustomOrderRefundResult> RefundAsync(
            CustomOrderDetail order, MetodoPago method, long amount, string? reference = null) =>
            Finance.RegisterRefundAsync(new(order.PedidoPersonalizadoId, Guid.NewGuid(),
                method, amount, "Motivo de prueba", Referencia: reference));

        public SaleService CreateSaleService() => new(
            Context, Session, Audit, Permissions,
            new CreditService(Context, Session, Permissions, Audit));

        public CashService CreateCashService() => new(
            Context, Session, Audit, new NullNotificationService(), Permissions);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestSession : ICurrentSession
    {
        public TestSession(UsuarioSesion user) => UsuarioActual = user;
        public UsuarioSesion? UsuarioActual { get; private set; }
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }
}
