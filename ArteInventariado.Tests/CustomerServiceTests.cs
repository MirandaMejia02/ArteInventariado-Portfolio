using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class CustomerServiceTests
{
    [Fact]
    public async Task CreateAsync_AllowsCustomerWithOnlyName()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Maria Lopez"));

        Assert.Equal("CLI-000001", customer.CodigoCliente);
        Assert.Equal("Maria Lopez", customer.NombreCompleto);
        Assert.Equal(EstadoCliente.ACTIVO, customer.Estado);
        Assert.Null(customer.TelefonoCelular);
        Assert.Null(customer.DocumentoIdentidad);
    }

    [Fact]
    public async Task CreateAsync_AllowsCustomerWithAllData()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(
            null,
            "Ana Beatriz Morales",
            "7777-1111",
            "+503 7777-1111",
            "ANA@example.com",
            "San Salvador",
            "01234567-8",
            "Cliente frecuente"));

        Assert.Equal("Ana Beatriz Morales", customer.NombreCompleto);
        Assert.Equal("7777-1111", customer.TelefonoCelular);
        Assert.Equal("+503 7777-1111", customer.WhatsApp);
        Assert.Equal("ANA@example.com", customer.CorreoElectronico);
        Assert.Equal("San Salvador", customer.Direccion);
        Assert.Equal("01234567-8", customer.DocumentoIdentidad);
        Assert.Equal("Cliente frecuente", customer.Notas);
    }

    [Fact]
    public async Task CreateAsync_BlocksEmptyName()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "   ")));
    }

    [Fact]
    public async Task CreateAsync_BlocksDuplicateDocument()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(
            null,
            "Cliente Uno",
            DocumentoIdentidad: "01234567-8"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CustomerService.CreateAsync(new CustomerSaveRequest(
                null,
                "Cliente Dos",
                DocumentoIdentidad: "012345678")));

        Assert.Contains("mismo DUI", exception.Message);
        Assert.Equal(1, await fixture.Context.Clientes.CountAsync());
    }

    [Fact]
    public async Task FindDuplicatesAsync_WarnsOnDuplicatePhoneAndRequiresConfirmationToCreate()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(
            null,
            "Cliente Telefono Uno",
            TelefonoCelular: "7777-8888"));

        var request = new CustomerSaveRequest(
            null,
            "Cliente Telefono Dos",
            TelefonoCelular: "77778888");

        var duplicates = await fixture.CustomerService.FindDuplicatesAsync(request);
        Assert.True(duplicates.HasWarnings);
        Assert.Contains(duplicates.Candidates, c => c.Kind == CustomerDuplicateKind.Telefono && !c.Blocking);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CustomerService.CreateAsync(request));

        var created = await fixture.CustomerService.CreateAsync(request with { ConfirmDuplicateWarning = true });
        Assert.Equal("Cliente Telefono Dos", created.NombreCompleto);
    }

    [Fact]
    public async Task FindDuplicatesAsync_WarnsOnDuplicateEmail()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(
            null,
            "Cliente Correo Uno",
            CorreoElectronico: "persona@example.com"));

        var duplicates = await fixture.CustomerService.FindDuplicatesAsync(new CustomerSaveRequest(
            null,
            "Cliente Correo Dos",
            CorreoElectronico: " PERSONA@example.com "));

        Assert.True(duplicates.HasWarnings);
        Assert.Contains(duplicates.Candidates, c => c.Kind == CustomerDuplicateKind.Correo && !c.Blocking);
    }

    [Fact]
    public async Task FindDuplicatesAsync_WarnsOnSimilarName()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Jose Perez"));

        var duplicates = await fixture.CustomerService.FindDuplicatesAsync(new CustomerSaveRequest(null, "José Pérez"));

        Assert.True(duplicates.HasWarnings);
        Assert.Contains(duplicates.Candidates, c => c.Kind == CustomerDuplicateKind.NombreParecido && !c.Blocking);
    }

    [Fact]
    public async Task EmployeeWithoutClientesAccederCannotQueryCustomers()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInEmployee();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CustomerQueryService.SearchAsync(new CustomerFilter()));
    }

    [Fact]
    public async Task EmployeeWithoutClienteCrearCannotCreateCustomer()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.CLIENTES_ACCEDER);
        fixture.SignInEmployee();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Sin permiso crear")));
    }

    [Fact]
    public async Task EmployeeWithoutClienteEditarCannotEditCustomer()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente editable"));

        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.CLIENTES_ACCEDER);
        fixture.SignInEmployee();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CustomerService.UpdateAsync(new CustomerSaveRequest(customer.ClienteId, "Cliente editado")));
    }

    [Fact]
    public async Task EmployeeWithoutClienteHistorialVerCannotReadCustomerHistory()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente con historial"));
        await fixture.CreateSaleAsync(customer.ClienteId);

        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.CLIENTES_ACCEDER);
        fixture.SignInEmployee();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CustomerQueryService.GetPurchaseHistoryAsync(customer.ClienteId));
    }

    [Fact]
    public async Task EmployeeWithClienteHistorialVerCanOpenOnlyCustomerPurchaseDetail()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente detalle"));
        var otherCustomer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Otro cliente"));
        var sale = await fixture.CreateSaleAsync(customer.ClienteId);
        var otherSale = await fixture.CreateSaleAsync(otherCustomer.ClienteId);

        await fixture.SetEmployeePermissionsAsync(
            PermisoEmpleado.CLIENTES_ACCEDER,
            PermisoEmpleado.CLIENTE_HISTORIAL_VER);
        fixture.SignInEmployee();

        var detail = await fixture.CustomerQueryService.GetPurchaseDetailAsync(customer.ClienteId, sale.VentaId);
        var blocked = await fixture.CustomerQueryService.GetPurchaseDetailAsync(customer.ClienteId, otherSale.VentaId);

        Assert.NotNull(detail);
        Assert.Equal("Cliente detalle", detail!.Cliente);
        Assert.Null(blocked);
    }

    [Fact]
    public async Task AdminCanUseCompleteCustomerModule()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Admin Cliente"));
        var listed = await fixture.CustomerQueryService.SearchAsync(new CustomerFilter("Admin"));
        var edited = await fixture.CustomerService.UpdateAsync(new CustomerSaveRequest(customer.ClienteId, "Admin Cliente Editado"));
        await fixture.CustomerService.SetStateAsync(new CustomerStateChangeRequest(customer.ClienteId, EstadoCliente.INACTIVO, "Prueba admin"));
        var detail = await fixture.CustomerQueryService.GetByIdAsync(customer.ClienteId);

        Assert.Single(listed);
        Assert.Equal("Admin Cliente Editado", edited.NombreCompleto);
        Assert.Equal(EstadoCliente.INACTIVO, detail!.Estado);
    }

    [Fact]
    public async Task DeactivateCustomerPreservesPurchaseHistory()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente historial"));
        await fixture.CreateSaleAsync(customer.ClienteId);

        await fixture.CustomerService.SetStateAsync(new CustomerStateChangeRequest(customer.ClienteId, EstadoCliente.INACTIVO, "Cliente inactivo"));

        var history = await fixture.CustomerQueryService.GetPurchaseHistoryAsync(customer.ClienteId);
        Assert.Single(history);
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksInactiveCustomer()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente inactivo"));
        await fixture.CustomerService.SetStateAsync(new CustomerStateChangeRequest(customer.ClienteId, EstadoCliente.INACTIVO, "No usar"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateSaleAsync(customer.ClienteId));

        Assert.Contains("cliente inactivo", exception.Message);
    }

    [Fact]
    public async Task ReactivateCustomerAllowsNewSale()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente reactivar"));
        await fixture.CustomerService.SetStateAsync(new CustomerStateChangeRequest(customer.ClienteId, EstadoCliente.INACTIVO, "Pausa"));
        await fixture.CustomerService.SetStateAsync(new CustomerStateChangeRequest(customer.ClienteId, EstadoCliente.ACTIVO, "Reactivado"));

        var sale = await fixture.CreateSaleAsync(customer.ClienteId);

        Assert.Equal(customer.ClienteId, sale.ClienteId);
    }

    [Fact]
    public async Task RegisterSaleAsync_WithCustomerPersistsCustomerId()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente venta"));

        var sale = await fixture.CreateSaleAsync(customer.ClienteId);

        Assert.Equal(customer.ClienteId, sale.ClienteId);
        var stored = await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == sale.VentaId);
        Assert.Equal(customer.ClienteId, stored.ClienteId);
    }

    [Fact]
    public async Task RegisterSaleAsync_WithCustomerStoresNameSnapshot()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente Snapshot"));

        var sale = await fixture.CreateSaleAsync(customer.ClienteId);

        Assert.Equal("Cliente Snapshot", sale.ClienteNombreSnapshot);
        var stored = await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == sale.VentaId);
        Assert.Equal("Cliente Snapshot", stored.ClienteNombreSnapshot);
    }

    [Fact]
    public async Task EditingCustomerNameDoesNotChangeHistoricalSaleSnapshot()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente Original"));
        var sale = await fixture.CreateSaleAsync(customer.ClienteId);

        await fixture.CustomerService.UpdateAsync(new CustomerSaveRequest(customer.ClienteId, "Cliente Nuevo"));

        var detail = await fixture.SalesQueryService.GetSaleDetailAsync(sale.VentaId);
        Assert.Equal("Cliente Original", detail!.Cliente);
    }

    [Fact]
    public async Task RegisterSaleAsync_WithoutCustomerUsesFinalConsumerSnapshot()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        var sale = await fixture.CreateSaleAsync(clienteId: null);

        Assert.Null(sale.ClienteId);
        Assert.Equal("Consumidor final", sale.ClienteNombreSnapshot);
        var stored = await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == sale.VentaId);
        Assert.Null(stored.ClienteId);
        Assert.Equal("Consumidor final", stored.ClienteNombreSnapshot);
    }

    [Fact]
    public async Task CustomerAuditIsWrittenForCreateEditAndStateChange()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();

        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(null, "Cliente Audit"));
        await fixture.CustomerService.UpdateAsync(new CustomerSaveRequest(customer.ClienteId, "Cliente Audit Editado"));
        await fixture.CustomerService.SetStateAsync(new CustomerStateChangeRequest(customer.ClienteId, EstadoCliente.INACTIVO, "Auditoria"));

        Assert.Equal(1, await fixture.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.CLIENTE_CREADO));
        Assert.Equal(1, await fixture.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.CLIENTE_EDITADO));
        Assert.Equal(1, await fixture.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.CLIENTE_ESTADO_MODIFICADO));
    }

    [Fact]
    public async Task UpdateAsync_WithNoChangesDoesNotWriteAudit()
    {
        await using var fixture = await CustomerFixture.CreateAsync();
        fixture.SignInAdmin();
        var customer = await fixture.CustomerService.CreateAsync(new CustomerSaveRequest(
            null,
            "Cliente Sin Cambios",
            TelefonoCelular: "7000-0000"));

        var auditCount = await fixture.Context.Bitacora.CountAsync();
        await fixture.CustomerService.UpdateAsync(new CustomerSaveRequest(
            customer.ClienteId,
            "Cliente Sin Cambios",
            TelefonoCelular: "7000-0000"));

        Assert.Equal(auditCount, await fixture.Context.Bitacora.CountAsync());
    }

    private sealed class CustomerFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CustomerFixture(
            SqliteConnection connection,
            AppDbContext context,
            TestCurrentSession currentSession,
            AuditService auditService,
            EmployeePermissionService permissionService,
            CustomerService customerService,
            CustomerQueryService customerQueryService,
            SaleService saleService,
            SalesQueryService salesQueryService,
            int adminId,
            int employeeId,
            int productId)
        {
            _connection = connection;
            Context = context;
            CurrentSession = currentSession;
            AuditService = auditService;
            PermissionService = permissionService;
            CustomerService = customerService;
            CustomerQueryService = customerQueryService;
            SaleService = saleService;
            SalesQueryService = salesQueryService;
            AdminId = adminId;
            EmployeeId = employeeId;
            ProductId = productId;
        }

        public AppDbContext Context { get; }
        public TestCurrentSession CurrentSession { get; }
        public AuditService AuditService { get; }
        public EmployeePermissionService PermissionService { get; }
        public CustomerService CustomerService { get; }
        public CustomerQueryService CustomerQueryService { get; }
        public SaleService SaleService { get; }
        public SalesQueryService SalesQueryService { get; }
        public int AdminId { get; }
        public int EmployeeId { get; }
        public int ProductId { get; }

        public static async Task<CustomerFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = CreateUser("Admin clientes", RolUsuario.ADMIN);
            var employee = CreateUser("Empleado clientes", RolUsuario.EMPLEADO);
            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };

            context.Usuarios.AddRange(admin, employee);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var productCode = $"CLI-PROD-{Guid.NewGuid():N}";
            var product = new Producto
            {
                CodigoInterno = productCode,
                Nombre = "Producto cliente",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 500,
                PrecioVentaCentavos = 1000,
                StockActual = 25,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = productCode,
                        Activo = true,
                        EsPrincipal = true,
                        SaldosInventario =
                        {
                            new InventarioSaldo
                            {
                                Ubicacion = UbicacionInventario.EXHIBICION,
                                ExistenciaFisica = 25,
                                Version = 1
                            },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, Version = 1 }
                        }
                    }
                }
            };

            context.Productos.Add(product);
            context.Cajas.Add(new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = admin.UsuarioId
            });
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, session, audit);
            await permissionService.EnsureRecommendedPermissionsForEmployeeAsync(employee.UsuarioId);
            var customerService = new CustomerService(context, session, permissionService, audit);
            var customerQueryService = new CustomerQueryService(context, permissionService);
            var creditService = new CreditService(context, session, permissionService, audit);
            var saleService = new SaleService(context, session, audit, permissionService, creditService);
            var salesQueryService = new SalesQueryService(context, session, permissionService);

            return new CustomerFixture(
                connection,
                context,
                session,
                audit,
                permissionService,
                customerService,
                customerQueryService,
                saleService,
                salesQueryService,
                admin.UsuarioId,
                employee.UsuarioId,
                product.ProductoId);
        }

        public void SignInAdmin()
        {
            SignIn(AdminId);
        }

        public void SignInEmployee()
        {
            SignIn(EmployeeId);
        }

        public async Task SetEmployeePermissionsAsync(params PermisoEmpleado[] enabled)
        {
            SignInAdmin();
            var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
            foreach (var permission in enabled)
            {
                EnablePermissionWithDependencies(states, permission);
            }

            await PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                EmployeeId,
                EmployeePermissionCatalog.AllPermissions
                    .OrderBy(permission => permission)
                    .Select(permission => new EmployeePermissionUpdate(permission, states[permission]))
                    .ToList()));
        }

        public Task<SaleResult> CreateSaleAsync(int? clienteId)
        {
            SignInAdmin();
            return SaleService.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(ProductId, 1)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 1000)],
                ClienteId: clienteId));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private void SignIn(int userId)
        {
            var user = Context.Usuarios.AsNoTracking().Single(u => u.UsuarioId == userId);
            CurrentSession.IniciarSesion(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
        }

        private static void EnablePermissionWithDependencies(
            IDictionary<PermisoEmpleado, bool> states,
            PermisoEmpleado permission)
        {
            if (EmployeePermissionCatalog.Dependencies.TryGetValue(permission, out var dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    EnablePermissionWithDependencies(states, dependency);
                }
            }

            states[permission] = true;
        }

        private static Usuario CreateUser(string name, RolUsuario role)
        {
            return new Usuario
            {
                NombreCompleto = name,
                NombreUsuario = $"{name.Replace(" ", "-").ToLowerInvariant()}-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = role,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
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
