using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class CustomOrderServiceTests
{
    [Fact]
    public async Task CreateManual_WithCustomer_CopiesSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateAsync(f.CustomerId, null);
        Assert.Equal(f.CustomerId, order.ClienteId);
        Assert.Equal("Cliente pedidos", order.Destinatario);
    }

    [Fact]
    public async Task CreateManual_WithoutCustomer_UsesManualRecipient()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateAsync(null, "Destinatario manual");
        Assert.Null(order.ClienteId);
        Assert.Equal("Destinatario manual", order.Destinatario);
    }

    [Fact]
    public async Task CreateManual_WithoutRecipient_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.CreateAsync(null, " "));
    }

    [Fact]
    public async Task Create_WithDescriptiveLine_IsAllowed()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateAsync(lines: [new(null, "Trabajo especial", 1, 1000)]);
        Assert.Null(Assert.Single(order.Detalles).ProductoId);
    }

    [Fact]
    public async Task Create_WithExistingProduct_CopiesProductSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var line = Assert.Single((await f.CreateAsync()).Detalles);
        Assert.Equal(f.ProductId, line.ProductoId);
        Assert.Equal("Producto para pedido", line.Descripcion);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 100, 101)]
    public async Task Create_WithInvalidLine_IsRejected(int quantity, long price, long discount)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            f.CreateAsync(lines: [new(null, "Linea", quantity, price, discount)]));
    }

    [Fact]
    public async Task Create_WithExcessiveGeneralDiscount_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.CreateAsync(generalDiscount: 1001));
    }

    [Fact]
    public async Task Create_CalculatesTotalsInCents()
    {
        await using var f = await Fixture.CreateAsync(
            lines: [new(null, "Linea", 3, 500, 100)], generalDiscount: 50);
        var order = Assert.Single(await f.Query.SearchAsync(new()));
        Assert.Equal(1350, order.TotalEstimadoCentavos);
        var detail = await f.Query.GetByIdAsync(order.PedidoPersonalizadoId);
        Assert.Equal(1400, detail!.SubtotalCentavos);
        Assert.Equal(50, detail.DescuentoGeneralCentavos);
    }

    [Fact]
    public async Task Create_DoesNotAffectStockCashOrReceivables()
    {
        await using var f = await Fixture.CreateAsync();
        var stock = await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync();
        await f.CreateAsync();
        Assert.Equal(stock, await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync());
        Assert.Empty(await f.Context.MovimientosInventario.ToListAsync());
        Assert.Empty(await f.Context.Cajas.ToListAsync());
        Assert.Empty(await f.Context.CuentasPorCobrar.ToListAsync());
    }

    [Fact]
    public async Task UpdateDraft_ReplacesCommercialData()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateAsync();
        var updated = await f.Service.UpdateDraftAsync(f.Request(
            draft.PedidoPersonalizadoId,
            lines: [new(null, "Linea nueva", 2, 750)]));
        Assert.Equal(1500, updated.TotalEstimadoCentavos);
        Assert.Equal("Linea nueva", Assert.Single(updated.Detalles).Descripcion);
    }

    [Fact]
    public async Task ConfirmedOrder_CannotBeEdited()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateAsync();
        await f.Service.ConfirmAsync(draft.PedidoPersonalizadoId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.UpdateDraftAsync(f.Request(draft.PedidoPersonalizadoId)));
    }

    [Fact]
    public async Task StateFlow_RegistersOperationalDates()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.Service.ConfirmAsync((await f.CreateAsync()).PedidoPersonalizadoId);
        order = await f.ChangeAsync(order, EstadoPedidoPersonalizado.EN_PRODUCCION);
        Assert.NotNull(order.FechaInicio);
        order = await f.ChangeAsync(order, EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA);
        Assert.NotNull(order.FechaFinalizacion);
        order = await f.ChangeAsync(order, EstadoPedidoPersonalizado.ENTREGADO);
        Assert.NotNull(order.FechaEntregaReal);
    }

    [Fact]
    public async Task InvalidStateTransition_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.ChangeAsync(draft, EstadoPedidoPersonalizado.ENTREGADO));
    }

    [Fact]
    public async Task Cancel_RequiresReason()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.Service.ChangeStateAsync(new(draft.PedidoPersonalizadoId,
                EstadoPedidoPersonalizado.CANCELADO, DateTime.Now, " ")));
    }

    [Fact]
    public async Task Cancel_PreservesOrderAndHistory()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateAsync();
        var cancelled = await f.Service.ChangeStateAsync(new(
            draft.PedidoPersonalizadoId, EstadoPedidoPersonalizado.CANCELADO,
            DateTime.Now, "Cliente desistio"));
        Assert.Equal(EstadoPedidoPersonalizado.CANCELADO, cancelled.Estado);
        Assert.Equal(2, cancelled.Historial.Count);
        Assert.True(await f.Context.PedidosPersonalizados.AnyAsync());
    }

    [Fact]
    public async Task Search_DetectsOverdueOrder()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateAsync(requestDate: DateTime.Today.AddDays(-10), promiseDate: DateTime.Today.AddDays(-1));
        var rows = await f.Query.SearchAsync(new(SoloAtrasados: true));
        Assert.True(Assert.Single(rows).Atrasado);
    }

    [Fact]
    public async Task Duplicate_GeneratesNewCodeAndDraft()
    {
        await using var f = await Fixture.CreateAsync();
        var source = await f.CreateAsync();
        var copy = await f.Service.DuplicateAsync(source.PedidoPersonalizadoId);
        Assert.NotEqual(source.CodigoPedido, copy.CodigoPedido);
        Assert.Equal(EstadoPedidoPersonalizado.BORRADOR, copy.Estado);
    }

    [Fact]
    public async Task HistoricalSnapshots_SurviveCatalogChanges()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateAsync(f.CustomerId, null);
        (await f.Context.Clientes.FindAsync(f.CustomerId))!.NombreCompleto = "Cliente cambiado";
        (await f.Context.Productos.FindAsync(f.ProductId))!.Nombre = "Producto cambiado";
        await f.Context.SaveChangesAsync();
        var stored = await f.Query.GetByIdAsync(order.PedidoPersonalizadoId);
        Assert.Equal("Cliente pedidos", stored!.Destinatario);
        Assert.Equal("Producto para pedido", Assert.Single(stored.Detalles).Descripcion);
    }

    [Fact]
    public async Task ConvertAcceptedQuotation_CopiesSnapshotsAndMarksSourceConverted()
    {
        await using var f = await Fixture.CreateAsync();
        var quotation = await f.AcceptQuotationAsync();
        var order = await f.Service.ConvertQuotationAsync(quotation.CotizacionId, DateTime.Today.AddDays(20));
        Assert.Equal(quotation.CotizacionId, order.CotizacionId);
        Assert.Equal(EstadoPedidoPersonalizado.CONFIRMADO, order.Estado);
        Assert.Equal(quotation.TotalCentavos, order.TotalEstimadoCentavos);
        Assert.Equal(EstadoCotizacion.CONVERTIDA,
            await f.Context.Cotizaciones.Where(c => c.CotizacionId == quotation.CotizacionId).Select(c => c.Estado).SingleAsync());
    }

    [Fact]
    public async Task ConvertNonAcceptedQuotation_IsRejectedWithoutChangingSource()
    {
        await using var f = await Fixture.CreateAsync();
        var quotation = await f.CreateQuotationAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.ConvertQuotationAsync(quotation.CotizacionId, DateTime.Today.AddDays(10)));
        Assert.Equal(EstadoCotizacion.BORRADOR,
            await f.Context.Cotizaciones.Where(c => c.CotizacionId == quotation.CotizacionId).Select(c => c.Estado).SingleAsync());
    }

    [Fact]
    public async Task ConvertQuotationTwice_IsBlockedAndCreatesOneOrder()
    {
        await using var f = await Fixture.CreateAsync();
        var quotation = await f.AcceptQuotationAsync();
        await f.Service.ConvertQuotationAsync(quotation.CotizacionId, DateTime.Today.AddDays(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.ConvertQuotationAsync(quotation.CotizacionId, DateTime.Today.AddDays(10)));
        Assert.Equal(1, await f.Context.PedidosPersonalizados.CountAsync());
    }

    [Fact]
    public async Task Conversion_DoesNotAffectStockCashOrReceivables()
    {
        await using var f = await Fixture.CreateAsync();
        var quotation = await f.AcceptQuotationAsync();
        var stock = await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync();
        await f.Service.ConvertQuotationAsync(quotation.CotizacionId, DateTime.Today.AddDays(10));
        Assert.Equal(stock, await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync());
        Assert.Empty(await f.Context.Cajas.ToListAsync());
        Assert.Empty(await f.Context.CuentasPorCobrar.ToListAsync());
    }

    [Fact]
    public async Task EmployeeWithoutAccess_IsBlocked()
    {
        await using var f = await Fixture.CreateAsync();
        f.SignInEmployee();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Query.SearchAsync(new()));
    }

    [Fact]
    public async Task EmployeeWithAccessButWithoutCreate_IsBlockedFromCreate()
    {
        await using var f = await Fixture.CreateAsync();
        await f.GrantAsync(PermisoEmpleado.PEDIDOS_ACCEDER);
        f.SignInEmployee();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.CreateAsync());
    }

    [Fact]
    public async Task Admin_HasCompleteOrderAccess()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.True(await f.PermissionService.HasPermissionAsync(PermisoEmpleado.PEDIDO_CANCELAR));
        Assert.NotNull(await f.CreateAsync());
    }

    [Fact]
    public async Task History_IsHiddenWithoutIndividualPermission()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateAsync();
        await f.GrantAsync(PermisoEmpleado.PEDIDOS_ACCEDER);
        f.SignInEmployee();
        Assert.Empty((await f.Query.GetByIdAsync(order.PedidoPersonalizadoId))!.Historial);
        await f.GrantAsync(PermisoEmpleado.PEDIDO_HISTORIAL_VER);
        Assert.Single((await f.Query.GetByIdAsync(order.PedidoPersonalizadoId))!.Historial);
    }

    [Fact]
    public async Task StateChanges_AreAudited()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateAsync();
        await f.Service.ConfirmAsync(order.PedidoPersonalizadoId);
        Assert.Equal(1, await f.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.PEDIDO_CREADO));
        Assert.Equal(1, await f.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.PEDIDO_CONFIRMADO));
    }

    [Fact]
    public async Task Pdf_IsGeneratedWithoutInternalNotes()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"ArteInventariado-Orders-{Guid.NewGuid():N}");
        f.Context.ConfiguracionSistema.Add(new()
        {
            Clave = SystemConfigService.ReportsFolderKey,
            Valor = directory,
            FechaModificacion = DateTime.UtcNow
        });
        await f.Context.SaveChangesAsync();
        try
        {
            var document = new CustomOrderDocumentService(
                f.Context, f.Query, f.Session, f.Audit, f.PermissionService);
            var path = await document.GeneratePdfAsync(order.PedidoPersonalizadoId);
            Assert.True(File.Exists(path));
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path), 0, 4));
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain("NOTA INTERNA SECRETA", System.Text.Encoding.ASCII.GetString(bytes));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(
            SqliteConnection connection, AppDbContext context, TestSession session,
            AuditService audit, EmployeePermissionService permissions,
            CustomOrderQueryService query, CustomOrderService service,
            QuotationService quotations, int adminId, int employeeId, int customerId, int productId)
        {
            _connection = connection; Context = context; Session = session; Audit = audit;
            PermissionService = permissions; Query = query; Service = service;
            QuotationService = quotations; AdminId = adminId; EmployeeId = employeeId;
            CustomerId = customerId; ProductId = productId;
        }
        public AppDbContext Context { get; }
        public TestSession Session { get; }
        public AuditService Audit { get; }
        public EmployeePermissionService PermissionService { get; }
        public CustomOrderQueryService Query { get; }
        public CustomOrderService Service { get; }
        public QuotationService QuotationService { get; }
        public int AdminId { get; }
        public int EmployeeId { get; }
        public int CustomerId { get; }
        public int ProductId { get; }

        public static async Task<Fixture> CreateAsync(
            IReadOnlyList<CustomOrderLineRequest>? lines = null,
            long generalDiscount = 0)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var admin = User("Admin pedidos", RolUsuario.ADMIN);
            var employee = User("Empleado pedidos", RolUsuario.EMPLEADO);
            context.Usuarios.AddRange(admin, employee);
            await context.SaveChangesAsync();
            var category = new Categoria { Nombre = $"Pedidos-{Guid.NewGuid():N}", Activa = true, FechaCreacion = DateTime.UtcNow };
            context.Categorias.Add(category);
            var customer = new Cliente
            {
                CodigoCliente = "CLI-PED-1", NombreCompleto = "Cliente pedidos",
                NombreNormalizado = "CLIENTE PEDIDOS", TelefonoCelular = "7000-0000",
                CorreoElectronico = "cliente@example.com", Direccion = "Direccion cliente",
                Estado = EstadoCliente.ACTIVO, FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow, UsuarioCreacionId = admin.UsuarioId
            };
            context.Clientes.Add(customer);
            await context.SaveChangesAsync();
            var product = new Producto
            {
                CodigoInterno = "PED-PROD-1", Nombre = "Producto para pedido",
                CategoriaId = category.CategoriaId, PrecioCompraCentavos = 500,
                PrecioVentaCentavos = 1000, StockActual = 12, StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO, FechaCreacion = DateTime.UtcNow
            };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            var session = new TestSession(ToSession(admin));
            var audit = new AuditService(context);
            var permissions = new EmployeePermissionService(context, session, audit);
            await permissions.EnsureRecommendedPermissionsForEmployeeAsync(employee.UsuarioId);
            var orderQuery = new CustomOrderQueryService(context, permissions);
            var orderService = new CustomOrderService(context, session, permissions, audit, orderQuery);
            var quotationQuery = new QuotationQueryService(context, permissions);
            var quotationService = new QuotationService(context, session, permissions, audit, quotationQuery);
            var fixture = new Fixture(connection, context, session, audit, permissions,
                orderQuery, orderService, quotationService, admin.UsuarioId, employee.UsuarioId,
                customer.ClienteId, product.ProductoId);
            if (lines is not null)
                await fixture.Service.CreateDraftAsync(fixture.Request(lines: lines, generalDiscount: generalDiscount));
            return fixture;
        }

        public CustomOrderLineRequest Line(int? productId = null, string? description = null) =>
            new(productId ?? ProductId, description, 1, 1000, 0, "Acabado artesanal",
                "Rosa", "20 cm", "Madera", "Bendiciones", "Cuidar empaque");
        public CustomOrderSaveRequest Request(
            int? id = null, int? customerId = null, string? recipient = "Destinatario",
            IReadOnlyList<CustomOrderLineRequest>? lines = null, long generalDiscount = 0,
            DateTime? requestDate = null, DateTime? promiseDate = null) => new(
                id, customerId, recipient, "7000-0000", "pedido@example.com", "Direccion",
                requestDate ?? DateTime.Today, promiseDate ?? DateTime.Today.AddDays(15),
                PrioridadPedido.NORMAL, generalDiscount, "Descripcion", "Especificaciones",
                "NOTA INTERNA SECRETA", "Notas cliente", lines ?? [Line()]);
        public Task<CustomOrderDetail> CreateAsync(
            int? customerId = null, string? recipient = "Destinatario",
            IReadOnlyList<CustomOrderLineRequest>? lines = null, long generalDiscount = 0,
            DateTime? requestDate = null, DateTime? promiseDate = null) =>
            Service.CreateDraftAsync(Request(null, customerId, recipient, lines, generalDiscount, requestDate, promiseDate));
        public Task<CustomOrderDetail> ChangeAsync(CustomOrderDetail order, EstadoPedidoPersonalizado state) =>
            Service.ChangeStateAsync(new(order.PedidoPersonalizadoId, state, DateTime.Now, "Avance operativo"));
        public async Task<QuotationDetail> CreateQuotationAsync()
        {
            return await QuotationService.CreateDraftAsync(new(
                null, CustomerId, null, null, null, null, DateTime.Today, DateTime.Today.AddDays(10),
                100, "Condiciones", "Observacion",
                [new(ProductId, null, 2, 1000, 100, "Linea cotizada")]));
        }
        public async Task<QuotationDetail> AcceptQuotationAsync()
        {
            var draft = await CreateQuotationAsync();
            await QuotationService.EmitAsync(draft.CotizacionId);
            return await QuotationService.ChangeStateAsync(new(draft.CotizacionId, EstadoCotizacion.ACEPTADA));
        }
        public async Task GrantAsync(params PermisoEmpleado[] permissions)
        {
            foreach (var permission in permissions)
            {
                var stored = await Context.UsuarioPermisos.SingleAsync(
                    p => p.UsuarioId == EmployeeId && p.Permiso == permission);
                stored.Habilitado = true;
            }
            await Context.SaveChangesAsync();
        }
        public void SignInEmployee()
        {
            var employee = Context.Usuarios.AsNoTracking().Single(u => u.UsuarioId == EmployeeId);
            Session.IniciarSesion(ToSession(employee));
        }
        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
        private static Usuario User(string name, RolUsuario role) => new()
        {
            NombreCompleto = name,
            NombreUsuario = $"{name.Replace(" ", "-").ToLowerInvariant()}-{Guid.NewGuid():N}",
            PasswordHash = "hash", Rol = role, Activo = true, FechaCreacion = DateTime.UtcNow
        };
        private static UsuarioSesion ToSession(Usuario user) =>
            new(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol);
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
