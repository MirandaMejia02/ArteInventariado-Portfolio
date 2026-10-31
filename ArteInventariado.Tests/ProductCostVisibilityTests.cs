using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class ProductCostVisibilityTests
{
    [Fact]
    public async Task LabelOnlyEmployee_DoesNotReceiveProductOrSkuCosts()
    {
        await using var fixture = await Fixture.CreateAsync(allowProductManagement: false);

        var list = Assert.Single(await fixture.Products.GetAllAsync());
        var search = Assert.Single(await fixture.Products.SearchByNameAsync("Producto"));
        var byCode = await fixture.Products.GetByCodigoInternoAsync("PRD-COSTO");
        var lowStock = Assert.Single(await fixture.Products.GetLowStockAsync());
        var detail = await fixture.Catalog.GetDetailAsync(fixture.ProductId);

        Assert.False(list.CostosVisibles);
        Assert.Equal(0, list.PrecioCompraCentavos);
        Assert.Equal(2_000, list.PrecioVentaCentavos);
        Assert.False(search.CostosVisibles);
        Assert.Equal(0, search.PrecioCompraCentavos);
        Assert.NotNull(byCode);
        Assert.False(byCode.CostosVisibles);
        Assert.Equal(0, byCode.PrecioCompraCentavos);
        Assert.False(lowStock.CostosVisibles);
        Assert.Equal(0, lowStock.PrecioCompraCentavos);

        Assert.NotNull(detail);
        Assert.False(detail.Product.CostosVisibles);
        Assert.Equal(0, detail.Product.PrecioCompraCentavos);
        var sku = Assert.Single(detail.Skus);
        Assert.False(sku.CostosVisibles);
        Assert.Null(sku.CostoOverrideCentavos);
        Assert.Equal(0, sku.CostoEfectivoCentavos);
        Assert.Equal(2_200, sku.PrecioVentaEfectivoCentavos);
    }

    [Fact]
    public async Task ProductManager_ReceivesProductAndSkuCosts()
    {
        await using var fixture = await Fixture.CreateAsync(allowProductManagement: true);

        var list = Assert.Single(await fixture.Products.GetAllAsync());
        var detail = await fixture.Catalog.GetDetailAsync(fixture.ProductId);

        Assert.True(list.CostosVisibles);
        Assert.Equal(1_200, list.PrecioCompraCentavos);
        Assert.NotNull(detail);
        Assert.True(detail.Product.CostosVisibles);
        var sku = Assert.Single(detail.Skus);
        Assert.True(sku.CostosVisibles);
        Assert.Equal(900, sku.CostoOverrideCentavos);
        Assert.Equal(900, sku.CostoEfectivoCentavos);
    }

    [Fact]
    public async Task ImageOnlyEmployee_DoesNotReceiveCosts()
    {
        await using var fixture = await Fixture.CreateAsync(
            allowProductManagement: false,
            allowImageManagement: true);

        var list = Assert.Single(await fixture.Products.GetAllAsync());
        var detail = await fixture.Catalog.GetDetailAsync(fixture.ProductId);

        Assert.False(list.CostosVisibles);
        Assert.Equal(0, list.PrecioCompraCentavos);
        Assert.NotNull(detail);
        Assert.False(detail.Product.CostosVisibles);
        Assert.Equal(0, detail.Product.PrecioCompraCentavos);
        Assert.False(Assert.Single(detail.Skus).CostosVisibles);
    }

    [Fact]
    public async Task ProductManagerWithoutCostPermission_CanCreateAtZeroButCannotSetManualCosts()
    {
        await using var fixture = await Fixture.CreateAsync(allowProductManagement: true);
        var request = new CreateProductRequest(
            CodigoInterno: null,
            Nombre: "Producto sin costo asignado",
            Descripcion: null,
            CategoriaId: fixture.CategoryId,
            ImagenLocalPath: null,
            PrecioCompraCentavos: 0,
            PrecioVentaCentavos: 2_000,
            StockActual: 0,
            StockMinimo: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Products.CreateAsync(request with { PrecioCompraCentavos = 500 }));

        var created = await fixture.Products.CreateAsync(request);

        Assert.Equal(0, created.PrecioCompraCentavos);
        Assert.Equal(2_000, created.PrecioVentaCentavos);

        var summary = Assert.Single(await fixture.Products.GetAllAsync(
            new ProductFilter(SearchText: "PRD-COSTO")));
        var changeCost = new UpdateProductRequest(
            summary.ProductoId,
            summary.CodigoInterno,
            summary.Nombre,
            summary.Descripcion,
            summary.CategoriaId,
            summary.ImagenLocalPath,
            1_500,
            summary.PrecioVentaCentavos,
            summary.StockMinimo,
            summary.Modo,
            summary.SubcategoriaId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Products.UpdateAsync(changeCost));

        var createdAtZero = await fixture.Products.GetByCodigoInternoAsync(created.CodigoInterno);
        Assert.NotNull(createdAtZero);
        Assert.Equal(0, createdAtZero.PrecioCompraCentavos);
    }

    [Fact]
    public async Task ProductManagerWithoutCostPermission_CannotChangeSkuCostButCanAddSkuWithoutCost()
    {
        await using var fixture = await Fixture.CreateAsync(allowProductManagement: true);
        var product = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == fixture.ProductId);
        product.Modo = ModoProducto.POR_VARIANTES;
        await fixture.Context.SaveChangesAsync();

        var detail = await fixture.Catalog.GetDetailAsync(fixture.ProductId);
        Assert.NotNull(detail);
        var existingSku = Assert.Single(detail.Skus);
        var unauthorizedUpdate = new UpdateProductSkuRequest(
            existingSku.ProductoSkuId,
            existingSku.NombreVariante,
            existingSku.PrecioVentaOverrideCentavos,
            existingSku.CostoOverrideCentavos + 100,
            existingSku.MinimoExhibicionOverride);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Catalog.UpdateSkuAsync(unauthorizedUpdate));

        var createdSku = await fixture.Catalog.CreateSkuAsync(new CreateProductSkuRequest(
            fixture.ProductId,
            "SKU-SIN-COSTO",
            "Variante sin costo",
            PrecioVentaOverrideCentavos: 2_500));
        Assert.Null(createdSku.CostoOverrideCentavos);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Catalog.CreateSkuAsync(
            new CreateProductSkuRequest(
                fixture.ProductId,
                "SKU-CON-COSTO",
                "Variante con costo",
                CostoOverrideCentavos: 500)));
    }

    [Fact]
    public async Task ProductManagerWithCostPermission_CanChangeProductAndSkuCosts()
    {
        await using var fixture = await Fixture.CreateAsync(
            allowProductManagement: true,
            allowPurchaseCostEdit: true);
        var product = await fixture.Products.GetByCodigoInternoAsync("PRD-COSTO");
        Assert.NotNull(product);
        await fixture.Products.UpdateAsync(new UpdateProductRequest(
            product.ProductoId,
            product.CodigoInterno,
            product.Nombre,
            product.Descripcion,
            product.CategoriaId,
            product.ImagenLocalPath,
            1_500,
            product.PrecioVentaCentavos,
            product.StockMinimo,
            product.Modo,
            product.SubcategoriaId));

        var detail = await fixture.Catalog.GetDetailAsync(fixture.ProductId);
        Assert.NotNull(detail);
        var sku = Assert.Single(detail.Skus);
        await fixture.Catalog.UpdateSkuAsync(new UpdateProductSkuRequest(
            sku.ProductoSkuId,
            sku.NombreVariante,
            sku.PrecioVentaOverrideCentavos,
            1_100,
            sku.MinimoExhibicionOverride));

        Assert.Equal(1_500, (await fixture.Products.GetByCodigoInternoAsync("PRD-COSTO"))!.PrecioCompraCentavos);
        var updatedDetail = await fixture.Catalog.GetDetailAsync(fixture.ProductId);
        Assert.Equal(1_100, Assert.Single(updatedDetail!.Skus).CostoOverrideCentavos);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly AppDbContext _context;

        private Fixture(
            SqliteConnection connection,
            AppDbContext context,
            ProductService products,
            ProductCatalogService catalog,
            int productId)
        {
            _connection = connection;
            _context = context;
            Products = products;
            Catalog = catalog;
            ProductId = productId;
        }

        public AppDbContext Context => _context;
        public ProductService Products { get; }
        public ProductCatalogService Catalog { get; }
        public int ProductId { get; }
        public int CategoryId { get; private init; }

        public static async Task<Fixture> CreateAsync(
            bool allowProductManagement,
            bool allowImageManagement = false,
            bool allowPurchaseCostEdit = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var employee = new Usuario
            {
                NombreCompleto = "Empleado de etiquetas",
                NombreUsuario = "labels-" + Guid.NewGuid().ToString("N"),
                PasswordHash = "hash",
                Rol = RolUsuario.EMPLEADO,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = "Categoría de prueba",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            var product = new Producto
            {
                CodigoInterno = "PRD-COSTO",
                Nombre = "Producto con costo sensible",
                Categoria = category,
                PrecioCompraCentavos = 1_200,
                PrecioVentaCentavos = 2_000,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            product.Skus.Add(new ProductoSku
            {
                CodigoSku = "SKU-COSTO",
                Activo = true,
                EsPrincipal = true,
                PrecioVentaOverrideCentavos = 2_200,
                CostoOverrideCentavos = 900,
                SaldosInventario =
                {
                    new InventarioSaldo { Ubicacion = UbicacionInventario.EXHIBICION, ExistenciaFisica = 0 },
                    new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, ExistenciaFisica = 0 },
                    new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, ExistenciaFisica = 0 }
                }
            });
            context.AddRange(employee, product);
            await context.SaveChangesAsync();

            var session = new TestSession(new UsuarioSesion(
                employee.UsuarioId,
                employee.NombreCompleto,
                employee.NombreUsuario,
                employee.Rol));
            var permissions = new TestPermissions(
                allowProductManagement,
                allowImageManagement,
                allowPurchaseCostEdit);
            var installation = new PrincipalInstallationContext();
            var factory = new SharedFactory(connection);
            var audit = new AuditService(context);
            var products = new ProductService(factory, audit, session, permissions, installation);
            var catalog = new ProductCatalogService(factory, products, permissions, installation, session, audit);
            return new Fixture(connection, context, products, catalog, product.ProductoId)
            {
                CategoryId = category.CategoriaId
            };
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SharedFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

        public ValueTask<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed class TestSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class TestPermissions(
        bool allowProductManagement,
        bool allowImageManagement,
        bool allowPurchaseCostEdit) : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(
            PermisoEmpleado permiso,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permiso == PermisoEmpleado.ETIQUETAS_GESTIONAR ||
                allowProductManagement && permiso == PermisoEmpleado.PRODUCTOS_GESTIONAR ||
                allowPurchaseCostEdit && permiso == PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR ||
                allowImageManagement && permiso == PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR);

        public Task<bool> HasAnyPermissionAsync(
            IReadOnlyCollection<PermisoEmpleado> permisos,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permisos.Any(permiso =>
                permiso == PermisoEmpleado.ETIQUETAS_GESTIONAR ||
                allowProductManagement && permiso == PermisoEmpleado.PRODUCTOS_GESTIONAR ||
                allowPurchaseCostEdit && permiso == PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR ||
                allowImageManagement && permiso == PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR));

        public Task EnsurePermissionAsync(PermisoEmpleado permiso, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(int usuarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);
        public Task UpdatePermissionsAsync(UpdateEmployeePermissionsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreRecommendedTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ApplySafeOperationalTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureRecommendedPermissionsForEmployeeAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PrincipalInstallationContext : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            InstallationMode.Principal,
            true,
            Guid.NewGuid(),
            5,
            "test",
            null);
        public bool AllowsOperationalWrites => true;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) { }
    }
}
