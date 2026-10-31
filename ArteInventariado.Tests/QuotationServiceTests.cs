using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class QuotationServiceTests
{
    [Fact]
    public async Task CreateDraft_WithRegisteredCustomer_StoresCustomerSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.CreateDraftAsync(clienteId: f.CustomerId, recipient: null);
        Assert.Equal(f.CustomerId, result.ClienteId);
        Assert.Equal("Cliente Original", result.Destinatario);
    }

    [Fact]
    public async Task CreateDraft_WithoutCustomer_StoresManualRecipient()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.CreateDraftAsync(recipient: "Turista extranjero");
        Assert.Null(result.ClienteId);
        Assert.Equal("Turista extranjero", result.Destinatario);
    }

    [Fact]
    public async Task CreateDraft_WithoutRecipient_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.CreateDraftAsync(recipient: " "));
    }

    [Fact]
    public async Task CreateDraft_WithExistingProduct_StoresProductSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.CreateDraftAsync();
        var line = Assert.Single(result.Detalles);
        Assert.Equal(f.ProductId, line.ProductoId);
        Assert.Equal("Producto cotizable", line.Descripcion);
    }

    [Fact]
    public async Task CreateDraft_WithDescriptiveLine_AllowsNullProduct()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.CreateDraftAsync(lines: [new QuotationLineRequest(null, "Trabajo artesanal especial", 2, 2500)]);
        Assert.Null(Assert.Single(result.Detalles).ProductoId);
    }

    [Fact]
    public async Task CreateDraft_WithInvalidQuantity_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.CreateDraftAsync(lines: [new QuotationLineRequest(null, "Linea", 0, 100)]));
    }

    [Fact]
    public async Task CreateDraft_WithNegativePrice_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.CreateDraftAsync(lines: [new QuotationLineRequest(null, "Linea", 1, -1)]));
    }

    [Fact]
    public async Task CreateDraft_WithExcessiveLineDiscount_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.CreateDraftAsync(lines: [new QuotationLineRequest(null, "Linea", 1, 100, 101)]));
    }

    [Fact]
    public async Task CreateDraft_WithExcessiveGeneralDiscount_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.CreateDraftAsync(generalDiscount: 1001));
    }

    [Fact]
    public async Task CreateDraft_CalculatesTotalsInCents()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.CreateDraftAsync(
            generalDiscount: 50,
            lines: [new QuotationLineRequest(null, "Linea", 3, 500, 100)]);
        Assert.Equal(1400, result.SubtotalCentavos);
        Assert.Equal(50, result.DescuentoGeneralCentavos);
        Assert.Equal(1350, result.TotalCentavos);
    }

    [Fact]
    public async Task CreateDraft_DoesNotAffectStockOrCash()
    {
        await using var f = await Fixture.CreateAsync();
        var stockBefore = await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync();
        var cashBefore = await f.Context.Cajas.CountAsync();
        await f.CreateDraftAsync();
        Assert.Equal(stockBefore, await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync());
        Assert.Equal(cashBefore, await f.Context.Cajas.CountAsync());
        Assert.Empty(await f.Context.MovimientosInventario.ToListAsync());
    }

    [Fact]
    public async Task Emit_ChangesDraftToIssued()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        var issued = await f.Service.EmitAsync(draft.CotizacionId);
        Assert.Equal(EstadoCotizacion.EMITIDA, issued.Estado);
        Assert.NotNull(issued.FechaEmision);
    }

    [Fact]
    public async Task UpdateDraft_ReplacesLinesAndTotals()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        var updated = await f.Service.UpdateDraftAsync(f.Request(
            draft.CotizacionId,
            [new QuotationLineRequest(null, "Nueva linea", 2, 750)]));
        Assert.Equal(1500, updated.TotalCentavos);
        Assert.Equal("Nueva linea", Assert.Single(updated.Detalles).Descripcion);
    }

    [Fact]
    public async Task UpdateIssuedQuotation_IsBlocked()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        await f.Service.EmitAsync(draft.CotizacionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.UpdateDraftAsync(f.Request(draft.CotizacionId)));
    }

    [Fact]
    public async Task IssuedQuotation_CanBeAccepted()
    {
        await using var f = await Fixture.CreateAsync();
        var issued = await f.Service.EmitAsync((await f.CreateDraftAsync()).CotizacionId);
        var accepted = await f.Service.ChangeStateAsync(new(issued.CotizacionId, EstadoCotizacion.ACEPTADA));
        Assert.Equal(EstadoCotizacion.ACEPTADA, accepted.Estado);
    }

    [Fact]
    public async Task IssuedQuotation_CanBeRejected()
    {
        await using var f = await Fixture.CreateAsync();
        var issued = await f.Service.EmitAsync((await f.CreateDraftAsync()).CotizacionId);
        var rejected = await f.Service.ChangeStateAsync(new(issued.CotizacionId, EstadoCotizacion.RECHAZADA, "No aceptada"));
        Assert.Equal(EstadoCotizacion.RECHAZADA, rejected.Estado);
    }

    [Fact]
    public async Task Cancel_RequiresReason()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.Service.ChangeStateAsync(new(draft.CotizacionId, EstadoCotizacion.CANCELADA, " ")));
    }

    [Fact]
    public async Task Cancel_PreservesQuotationAndHistory()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        var cancelled = await f.Service.ChangeStateAsync(new(draft.CotizacionId, EstadoCotizacion.CANCELADA, "Cliente desistio"));
        Assert.Equal(EstadoCotizacion.CANCELADA, cancelled.Estado);
        Assert.Equal(2, cancelled.Historial.Count);
        Assert.True(await f.Context.Cotizaciones.AnyAsync(c => c.CotizacionId == draft.CotizacionId));
    }

    [Fact]
    public async Task UpdateExpired_MarksPastIssuedQuotation()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.Service.CreateDraftAsync(f.Request(
            null,
            date: DateTime.Today.AddDays(-10),
            expiry: DateTime.Today.AddDays(-1)));
        await f.Service.EmitAsync(draft.CotizacionId);
        Assert.Equal(1, await f.Service.UpdateExpiredAsync());
        Assert.Equal(EstadoCotizacion.VENCIDA, (await f.Query.GetByIdAsync(draft.CotizacionId))!.Estado);
    }

    [Fact]
    public async Task Duplicate_CreatesNewCodeAndDraft()
    {
        await using var f = await Fixture.CreateAsync();
        var source = await f.CreateDraftAsync();
        var copy = await f.Service.DuplicateAsync(source.CotizacionId);
        Assert.NotEqual(source.CodigoCotizacion, copy.CodigoCotizacion);
        Assert.Equal(EstadoCotizacion.BORRADOR, copy.Estado);
        Assert.Equal(source.TotalCentavos, copy.TotalCentavos);
        Assert.NotEqual(Assert.Single(source.Detalles).DetalleCotizacionId, Assert.Single(copy.Detalles).DetalleCotizacionId);
        Assert.Single(copy.Historial);
        Assert.Equal(EstadoCotizacion.BORRADOR, copy.Historial[0].EstadoNuevo);
        Assert.Equal(1, await f.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.COTIZACION_DUPLICADA));
    }

    [Theory]
    [InlineData(EstadoCotizacion.EMITIDA)]
    [InlineData(EstadoCotizacion.RECHAZADA)]
    public async Task Duplicate_FromIssuedOrRejected_CreatesIndependentDraft(EstadoCotizacion sourceState)
    {
        await using var f = await Fixture.CreateAsync();
        var source = await f.CreateDraftAsync();
        source = await f.Service.EmitAsync(source.CotizacionId);
        if (sourceState == EstadoCotizacion.RECHAZADA)
            source = await f.Service.ChangeStateAsync(new(source.CotizacionId, EstadoCotizacion.RECHAZADA, "No aceptada"));

        var stockBefore = await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync();
        var copy = await f.Service.DuplicateAsync(source.CotizacionId);
        var original = await f.Query.GetByIdAsync(source.CotizacionId);

        Assert.Equal(sourceState, original!.Estado);
        Assert.Equal(source.Historial.Count, original.Historial.Count);
        Assert.Equal(EstadoCotizacion.BORRADOR, copy.Estado);
        Assert.Null(copy.FechaEmision);
        Assert.Null(copy.FechaCierre);
        Assert.Single(copy.Historial);
        Assert.Equal(stockBefore, await f.Context.Productos.Where(p => p.ProductoId == f.ProductId).Select(p => p.StockActual).SingleAsync());
    }

    [Fact]
    public async Task Duplicate_Twice_GeneratesDifferentCodesAndIds()
    {
        await using var f = await Fixture.CreateAsync();
        var source = await f.CreateDraftAsync();
        var first = await f.Service.DuplicateAsync(source.CotizacionId);
        var second = await f.Service.DuplicateAsync(source.CotizacionId);

        Assert.Equal(3, new[] { source.CodigoCotizacion, first.CodigoCotizacion, second.CodigoCotizacion }.Distinct().Count());
        Assert.Equal(3, new[] { source.CotizacionId, first.CotizacionId, second.CotizacionId }.Distinct().Count());
        Assert.Equal(3, (await f.Context.DetalleCotizaciones.Select(d => d.DetalleCotizacionId).ToListAsync()).Distinct().Count());
    }

    [Fact]
    public async Task Duplicate_UsesHistoricalSnapshotsWhenProductIsNowInactive()
    {
        await using var f = await Fixture.CreateAsync();
        var source = await f.CreateDraftAsync();
        var product = await f.Context.Productos.FindAsync(f.ProductId);
        product!.Nombre = "Nombre modificado despues de cotizar";
        product.CodigoInterno = "COD-MODIFICADO";
        product.Estado = EstadoProducto.INACTIVO;
        await f.Context.SaveChangesAsync();

        var copy = await f.Service.DuplicateAsync(source.CotizacionId);
        var sourceLine = Assert.Single(source.Detalles);
        var copyLine = Assert.Single(copy.Detalles);

        Assert.Equal(sourceLine.Codigo, copyLine.Codigo);
        Assert.Equal(sourceLine.Descripcion, copyLine.Descripcion);
        Assert.Equal(sourceLine.PrecioUnitarioCentavos, copyLine.PrecioUnitarioCentavos);
    }

    [Fact]
    public async Task Duplicate_WhenAuditFails_RollsBackHeaderDetailsAndHistory()
    {
        await using var f = await Fixture.CreateAsync();
        var source = await f.CreateDraftAsync();
        var service = new QuotationService(
            f.Context,
            f.Session,
            f.PermissionService,
            new ThrowingAuditService(),
            f.Query);
        var quotationCount = await f.Context.Cotizaciones.CountAsync();
        var detailCount = await f.Context.DetalleCotizaciones.CountAsync();
        var historyCount = await f.Context.HistorialEstadoCotizaciones.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DuplicateAsync(source.CotizacionId));

        Assert.Equal(quotationCount, await f.Context.Cotizaciones.CountAsync());
        Assert.Equal(detailCount, await f.Context.DetalleCotizaciones.CountAsync());
        Assert.Equal(historyCount, await f.Context.HistorialEstadoCotizaciones.CountAsync());
        Assert.DoesNotContain(f.Context.ChangeTracker.Entries(), entry => entry.State == EntityState.Added);
    }

    [Fact]
    public async Task HistoricalSnapshots_DoNotChangeAfterCatalogEdits()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync(clienteId: f.CustomerId, recipient: null);
        var customer = await f.Context.Clientes.FindAsync(f.CustomerId);
        var product = await f.Context.Productos.FindAsync(f.ProductId);
        customer!.NombreCompleto = "Cliente Modificado";
        product!.Nombre = "Producto Modificado";
        await f.Context.SaveChangesAsync();
        var stored = await f.Query.GetByIdAsync(draft.CotizacionId);
        Assert.Equal("Cliente Original", stored!.Destinatario);
        Assert.Equal("Producto cotizable", Assert.Single(stored.Detalles).Descripcion);
    }

    [Fact]
    public async Task EmployeeWithoutAccess_CannotQuery()
    {
        await using var f = await Fixture.CreateAsync();
        f.SignInEmployee();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Query.SearchAsync(new()));
    }

    [Fact]
    public async Task EmployeeWithAccessButWithoutCreate_CannotCreate()
    {
        await using var f = await Fixture.CreateAsync();
        await f.GrantAsync(PermisoEmpleado.COTIZACIONES_ACCEDER);
        f.SignInEmployee();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.CreateDraftAsync());
    }

    [Fact]
    public async Task EmployeeWithIndividualPermissions_CanCreateAndEmit()
    {
        await using var f = await Fixture.CreateAsync();
        await f.GrantAsync(
            PermisoEmpleado.COTIZACIONES_ACCEDER,
            PermisoEmpleado.COTIZACION_CREAR,
            PermisoEmpleado.COTIZACION_EMITIR);
        f.SignInEmployee();
        var issued = await f.Service.EmitAsync((await f.CreateDraftAsync()).CotizacionId);
        Assert.Equal(EstadoCotizacion.EMITIDA, issued.Estado);
    }

    [Fact]
    public async Task EmployeeHistory_IsHiddenUntilHistoryPermissionIsGranted()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        await f.GrantAsync(PermisoEmpleado.COTIZACIONES_ACCEDER);
        f.SignInEmployee();

        Assert.Empty((await f.Query.GetByIdAsync(draft.CotizacionId))!.Historial);

        await f.GrantAsync(PermisoEmpleado.COTIZACION_HISTORIAL_VER);
        Assert.Single((await f.Query.GetByIdAsync(draft.CotizacionId))!.Historial);
    }

    [Fact]
    public async Task Admin_HasCompleteQuotationAccess()
    {
        await using var f = await Fixture.CreateAsync();
        var detail = await f.CreateDraftAsync();
        Assert.Single(await f.Query.SearchAsync(new()));
        Assert.True(await f.PermissionService.HasPermissionAsync(PermisoEmpleado.COTIZACION_CANCELAR));
        Assert.NotNull(await f.Query.GetByIdAsync(detail.CotizacionId));
    }

    [Fact]
    public async Task StateChanges_WriteHistoryAndAudit()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        await f.Service.EmitAsync(draft.CotizacionId);
        Assert.Equal(2, await f.Context.HistorialEstadoCotizaciones.CountAsync());
        Assert.Equal(1, await f.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.COTIZACION_CREADA));
        Assert.Equal(1, await f.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.COTIZACION_EMITIDA));
    }

    [Fact]
    public async Task Search_FindsByCodeRecipientAndProduct()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync(recipient: "Maria Cotiza");
        Assert.Single(await f.Query.SearchAsync(new(Search: draft.CodigoCotizacion)));
        Assert.Single(await f.Query.SearchAsync(new(Search: "Maria")));
        Assert.Single(await f.Query.SearchAsync(new(Search: "Producto cotizable")));
    }

    [Fact]
    public async Task Pdf_IsGeneratedInConfiguredReportsFolder()
    {
        await using var f = await Fixture.CreateAsync();
        var draft = await f.CreateDraftAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"ArteInventariado-Quotation-{Guid.NewGuid():N}");
        f.Context.ConfiguracionSistema.Add(new ConfiguracionSistema
        {
            Clave = SystemConfigService.ReportsFolderKey,
            Valor = directory,
            FechaModificacion = DateTime.UtcNow
        });
        f.Context.ConfiguracionSistema.Add(new ConfiguracionSistema
        {
            Clave = SystemConfigService.BusinessAddressKey,
            Valor = "Ciudad de ejemplo",
            FechaModificacion = DateTime.UtcNow
        });
        await f.Context.SaveChangesAsync();
        try
        {
            var document = new QuotationDocumentService(f.Context, f.Query, f.Session, f.Audit, f.PermissionService);
            var path = await document.GeneratePdfAsync(draft.CotizacionId);
            Assert.True(File.Exists(path));
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path), 0, 4));
            Assert.Contains("Direccion negocio: Ciudad de ejemplo", System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path)));
            Assert.Equal(1, await f.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.COTIZACION_PDF_GENERADO));
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
            SqliteConnection connection,
            AppDbContext context,
            TestSession session,
            AuditService audit,
            EmployeePermissionService permissionService,
            QuotationQueryService query,
            QuotationService service,
            int adminId,
            int employeeId,
            int customerId,
            int productId)
        {
            _connection = connection;
            Context = context;
            Session = session;
            Audit = audit;
            PermissionService = permissionService;
            Query = query;
            Service = service;
            AdminId = adminId;
            EmployeeId = employeeId;
            CustomerId = customerId;
            ProductId = productId;
        }

        public AppDbContext Context { get; }
        public TestSession Session { get; }
        public AuditService Audit { get; }
        public EmployeePermissionService PermissionService { get; }
        public QuotationQueryService Query { get; }
        public QuotationService Service { get; }
        public int AdminId { get; }
        public int EmployeeId { get; }
        public int CustomerId { get; }
        public int ProductId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var admin = User("Admin cotizaciones", RolUsuario.ADMIN);
            var employee = User("Empleado cotizaciones", RolUsuario.EMPLEADO);
            context.Usuarios.AddRange(admin, employee);
            await context.SaveChangesAsync();
            var category = new Categoria { Nombre = $"Categoria-{Guid.NewGuid():N}", Activa = true, FechaCreacion = DateTime.UtcNow };
            context.Categorias.Add(category);
            var customer = new Cliente
            {
                CodigoCliente = "CLI-000001",
                NombreCompleto = "Cliente Original",
                NombreNormalizado = "CLIENTE ORIGINAL",
                Estado = EstadoCliente.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow,
                UsuarioCreacionId = admin.UsuarioId
            };
            context.Clientes.Add(customer);
            await context.SaveChangesAsync();
            var product = new Producto
            {
                CodigoInterno = "COT-PROD-1",
                Nombre = "Producto cotizable",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 500,
                PrecioVentaCentavos = 1000,
                StockActual = 9,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            var session = new TestSession(ToSession(admin));
            var audit = new AuditService(context);
            var permissions = new EmployeePermissionService(context, session, audit);
            await permissions.EnsureRecommendedPermissionsForEmployeeAsync(employee.UsuarioId);
            var query = new QuotationQueryService(context, permissions);
            var service = new QuotationService(context, session, permissions, audit, query);
            return new Fixture(connection, context, session, audit, permissions, query, service, admin.UsuarioId, employee.UsuarioId, customer.ClienteId, product.ProductoId);
        }

        public QuotationSaveRequest Request(
            int? id = null,
            IReadOnlyList<QuotationLineRequest>? lines = null,
            int? clienteId = null,
            string? recipient = "Destinatario",
            long generalDiscount = 0,
            DateTime? date = null,
            DateTime? expiry = null) => new(
                id,
                clienteId,
                recipient,
                "7000-0000",
                "cliente@example.com",
                "Direccion",
                date ?? DateTime.Today,
                expiry ?? DateTime.Today.AddDays(15),
                generalDiscount,
                "Valida por 15 dias.",
                "Prueba",
                lines ?? [new QuotationLineRequest(ProductId, null, 1, 1000)]);

        public Task<QuotationDetail> CreateDraftAsync(
            int? clienteId = null,
            string? recipient = "Destinatario",
            long generalDiscount = 0,
            IReadOnlyList<QuotationLineRequest>? lines = null) =>
            Service.CreateDraftAsync(Request(null, lines, clienteId, recipient, generalDiscount));

        public async Task GrantAsync(params PermisoEmpleado[] permissions)
        {
            foreach (var permission in permissions)
            {
                var stored = await Context.UsuarioPermisos.SingleAsync(p => p.UsuarioId == EmployeeId && p.Permiso == permission);
                stored.Habilitado = true;
            }
            await Context.SaveChangesAsync();
        }
        public void SignInEmployee() => SignIn(EmployeeId);
        private void SignIn(int id)
        {
            var user = Context.Usuarios.AsNoTracking().Single(u => u.UsuarioId == id);
            Session.IniciarSesion(ToSession(user));
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
            PasswordHash = "hash",
            Rol = role,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        private static UsuarioSesion ToSession(Usuario u) => new(u.UsuarioId, u.NombreCompleto, u.NombreUsuario, u.Rol);
    }

    public sealed class TestSession : ICurrentSession
    {
        public TestSession(UsuarioSesion current) => UsuarioActual = current;
        public UsuarioSesion? UsuarioActual { get; private set; }
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallo de bitacora simulado.");
    }
}
