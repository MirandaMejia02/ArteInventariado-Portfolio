using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class SupplierServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesActiveSupplierWithGeneratedCode()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();

        var supplier = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Artesanias del Norte",
            NombreContacto: "Juana Perez",
            Telefono: "7777-1111",
            WhatsApp: "7777-1111",
            Correo: "proveedor@example.com",
            Direccion: "San Salvador",
            DocumentoIdentidad: "01234567-8",
            DocumentoFiscal: "0614-123456-001-1",
            Notas: "Entrega semanal"));

        Assert.Equal("PRV-000001", supplier.CodigoProveedor);
        Assert.Equal(TipoProveedor.PROVEEDOR, supplier.Tipo);
        Assert.Equal(EstadoProveedor.ACTIVO, supplier.Estado);
        Assert.Equal("Artesanias del Norte", supplier.NombreRazonSocial);
        Assert.Equal("Juana Perez", supplier.NombreContacto);
        Assert.NotEmpty(supplier.Historial);
    }

    [Fact]
    public async Task CreateAsync_GeneratesArtisanCodeForArtisan()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();

        var supplier = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.ARTESANO,
            "Taller de barro"));

        Assert.Equal("ART-000001", supplier.CodigoProveedor);
        Assert.Equal(TipoProveedor.ARTESANO, supplier.Tipo);
    }

    [Fact]
    public async Task SearchAsync_FiltersByTextTypeAndState()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Distribuidora Mayorista"));
        var artisan = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.ARTESANO,
            "Artesano de madera"));

        await fixture.SupplierService.SetStateAsync(new SupplierStateChangeRequest(
            artisan.ProveedorId,
            EstadoProveedor.INACTIVO,
            "No trabaja actualmente"));

        var rows = await fixture.SupplierQueryService.SearchAsync(new SupplierFilter(
            "madera",
            TipoProveedor.ARTESANO,
            EstadoProveedor.INACTIVO));

        var row = Assert.Single(rows);
        Assert.Equal("Artesano de madera", row.NombreRazonSocial);
        Assert.Equal(EstadoProveedor.INACTIVO, row.Estado);
    }

    [Fact]
    public async Task CreateAsync_BlocksDuplicateIdentityOrFiscalDocument()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor Uno",
            DocumentoIdentidad: "01234567-8"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
                null,
                TipoProveedor.ARTESANO,
                "Proveedor Dos",
                DocumentoIdentidad: "012345678")));

        Assert.Contains("mismo DUI", exception.Message);
        Assert.Equal(1, await fixture.Context.Proveedores.CountAsync());
    }

    [Fact]
    public async Task FindDuplicatesAsync_WarnsOnPhoneEmailAndSimilarName()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor Ejemplo",
            Telefono: "7777-2222",
            Correo: "contacto@example.com"));

        var request = new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor Ejemplo",
            Telefono: "77772222",
            Correo: " CONTACTO@example.com ");

        var duplicates = await fixture.SupplierService.FindDuplicatesAsync(request);

        Assert.True(duplicates.HasWarnings);
        Assert.Contains(duplicates.Candidates, c => c.Kind == SupplierDuplicateKind.Telefono && !c.Blocking);
        Assert.Contains(duplicates.Candidates, c => c.Kind == SupplierDuplicateKind.Correo && !c.Blocking);
        Assert.Contains(duplicates.Candidates, c => c.Kind == SupplierDuplicateKind.NombreParecido && !c.Blocking);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.SupplierService.CreateAsync(request));

        var created = await fixture.SupplierService.CreateAsync(request with { ConfirmDuplicateWarning = true });
        Assert.Equal("Proveedor Ejemplo", created.NombreRazonSocial);
    }

    [Fact]
    public async Task EmployeeWithoutAccessCannotQuerySuppliers()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        await fixture.SetEmployeePermissionsAsync();
        fixture.SignInEmployee();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SupplierQueryService.SearchAsync(new SupplierFilter()));
    }

    [Fact]
    public async Task EmployeeWithAccessButWithoutCreateCannotCreateSupplier()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.PROVEEDORES_ACCEDER);
        fixture.SignInEmployee();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
                null,
                TipoProveedor.PROVEEDOR,
                "Proveedor sin permiso crear")));
    }

    [Fact]
    public async Task SetStateAsync_RequiresAdminAndMandatoryReason()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();
        var supplier = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor estado"));

        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.PROVEEDORES_ACCEDER, PermisoEmpleado.PROVEEDOR_EDITAR);
        fixture.SignInEmployee();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SupplierService.SetStateAsync(new SupplierStateChangeRequest(
                supplier.ProveedorId,
                EstadoProveedor.INACTIVO,
                "Empleado intenta desactivar")));

        fixture.SignInAdmin();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.SupplierService.SetStateAsync(new SupplierStateChangeRequest(
                supplier.ProveedorId,
                EstadoProveedor.INACTIVO,
                "  ")));

        await fixture.SupplierService.SetStateAsync(new SupplierStateChangeRequest(
            supplier.ProveedorId,
            EstadoProveedor.INACTIVO,
            "Fuera de uso"));

        var stored = await fixture.Context.Proveedores.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoProveedor.INACTIVO, stored.Estado);
        Assert.Equal("Fuera de uso", stored.MotivoDesactivacion);
    }

    [Fact]
    public async Task GetByIdAsync_HidesHistoryWhenEmployeeLacksHistoryPermission()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();
        var supplier = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor historial"));

        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.PROVEEDORES_ACCEDER);
        fixture.SignInEmployee();

        var detail = await fixture.SupplierQueryService.GetByIdAsync(supplier.ProveedorId);

        Assert.NotNull(detail);
        Assert.Empty(detail!.Historial);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SupplierQueryService.GetHistoryAsync(supplier.ProveedorId));
    }

    [Fact]
    public async Task AssociateProductAsync_LinksProductAndReplacesMainSupplier()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();
        var first = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor principal uno"));
        var second = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor principal dos"));

        await fixture.SupplierService.AssociateProductAsync(new SupplierProductAssociationRequest(
            first.ProveedorId,
            fixture.ProductId,
            "COD-A",
            EsProveedorPrincipal: true,
            Notas: "Principal inicial"));

        var replacement = await fixture.SupplierService.AssociateProductAsync(new SupplierProductAssociationRequest(
            second.ProveedorId,
            fixture.ProductId,
            "COD-B",
            EsProveedorPrincipal: true));

        Assert.True(replacement.EsProveedorPrincipal);

        var associations = await fixture.Context.ProductoProveedores
            .AsNoTracking()
            .Where(pp => pp.ProductoId == fixture.ProductId)
            .OrderBy(pp => pp.ProveedorId)
            .ToListAsync();

        Assert.Equal(2, associations.Count);
        Assert.False(associations[0].EsProveedorPrincipal);
        Assert.True(associations[1].EsProveedorPrincipal);
    }

    [Fact]
    public async Task GetByIdAsync_ProjectsAssociatedProductSubcategory()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();

        var category = await fixture.Context.Categorias.SingleAsync();
        var subcategory = new Subcategoria
        {
            CategoriaId = category.CategoriaId,
            Nombre = "Jarrones",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        var product = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == fixture.ProductId);
        product.Subcategoria = subcategory;
        await fixture.Context.SaveChangesAsync();

        var supplier = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor con subcategoria"));
        await fixture.SupplierService.AssociateProductAsync(new SupplierProductAssociationRequest(
            supplier.ProveedorId,
            fixture.ProductId,
            "PROV-JAR-001",
            EsProveedorPrincipal: true));

        var detail = await fixture.SupplierQueryService.GetByIdAsync(supplier.ProveedorId);
        var association = Assert.Single(detail!.Productos);
        Assert.Equal(fixture.ProductId, association.ProductoId);
        Assert.Equal("Jarrones", association.Subcategoria);
    }

    [Fact]
    public async Task AssociateProductAsync_RequiresPermissionAndActiveSupplier()
    {
        await using var fixture = await SupplierFixture.CreateAsync();
        fixture.SignInAdmin();
        var supplier = await fixture.SupplierService.CreateAsync(new SupplierSaveRequest(
            null,
            TipoProveedor.PROVEEDOR,
            "Proveedor producto"));

        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.PROVEEDORES_ACCEDER);
        fixture.SignInEmployee();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SupplierService.AssociateProductAsync(new SupplierProductAssociationRequest(
                supplier.ProveedorId,
                fixture.ProductId)));

        fixture.SignInAdmin();
        await fixture.SupplierService.SetStateAsync(new SupplierStateChangeRequest(
            supplier.ProveedorId,
            EstadoProveedor.INACTIVO,
            "No disponible"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SupplierService.AssociateProductAsync(new SupplierProductAssociationRequest(
                supplier.ProveedorId,
                fixture.ProductId)));
    }

    private sealed class SupplierFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SupplierFixture(
            SqliteConnection connection,
            AppDbContext context,
            TestCurrentSession currentSession,
            EmployeePermissionService permissionService,
            SupplierService supplierService,
            SupplierQueryService supplierQueryService,
            int adminId,
            int employeeId,
            int productId)
        {
            _connection = connection;
            Context = context;
            CurrentSession = currentSession;
            PermissionService = permissionService;
            SupplierService = supplierService;
            SupplierQueryService = supplierQueryService;
            AdminId = adminId;
            EmployeeId = employeeId;
            ProductId = productId;
        }

        public AppDbContext Context { get; }
        public TestCurrentSession CurrentSession { get; }
        public EmployeePermissionService PermissionService { get; }
        public SupplierService SupplierService { get; }
        public SupplierQueryService SupplierQueryService { get; }
        public int AdminId { get; }
        public int EmployeeId { get; }
        public int ProductId { get; }

        public static async Task<SupplierFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = CreateUser("Admin proveedores", RolUsuario.ADMIN);
            var employee = CreateUser("Empleado proveedores", RolUsuario.EMPLEADO);
            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };

            context.Usuarios.AddRange(admin, employee);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var product = new Producto
            {
                CodigoInterno = $"PRV-PROD-{Guid.NewGuid():N}",
                Nombre = "Producto proveedor",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 700,
                PrecioVentaCentavos = 1400,
                StockActual = 12,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };

            context.Productos.Add(product);
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, session, audit);
            var supplierQueryService = new SupplierQueryService(context, permissionService);
            var supplierService = new SupplierService(context, session, permissionService, audit, supplierQueryService);

            return new SupplierFixture(
                connection,
                context,
                session,
                permissionService,
                supplierService,
                supplierQueryService,
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
