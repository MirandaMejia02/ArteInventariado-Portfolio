using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class PurchaseServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesPendingPurchaseWithoutChangingStock()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var stockBefore = await fixture.GetProductStockAsync(fixture.ProductId);

        var purchase = await fixture.CreatePurchaseAsync(quantity: 5, costCentavos: 250);

        Assert.Equal(EstadoRecepcionCompra.PENDIENTE, purchase.EstadoRecepcion);
        Assert.Equal(1250, purchase.TotalCentavos);
        Assert.Equal(stockBefore, await fixture.GetProductStockAsync(fixture.ProductId));
        Assert.Empty(await fixture.Context.MovimientosInventario.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_BlocksInactiveSupplierAndDuplicateProduct()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PurchaseService.CreateAsync(new CreatePurchaseRequest(
                fixture.InactiveSupplierId,
                DateTime.Today,
                [new PurchaseLineRequest(fixture.ProductId, 1, 100)])));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PurchaseService.CreateAsync(new CreatePurchaseRequest(
                fixture.SupplierId,
                DateTime.Today,
                [
                    new PurchaseLineRequest(fixture.ProductId, 1, 100),
                    new PurchaseLineRequest(fixture.ProductId, 1, 100)
                ])));
    }

    [Fact]
    public async Task RegisterReceptionAsync_PartialReceptionIncreasesStockAndKeepsPurchasePartial()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 10, costCentavos: 200);
        var detail = Assert.Single(purchase.Detalles);

        var reception = await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 4)],
            "Recepcion parcial"));

        var refreshed = await fixture.PurchaseQueryService.GetByIdAsync(purchase.CompraId);
        var movement = await fixture.Context.MovimientosInventario.AsNoTracking().SingleAsync();
        var history = await fixture.Context.HistorialCostosProducto.AsNoTracking().SingleAsync();

        Assert.Equal(14, await fixture.GetProductStockAsync(fixture.ProductId));
        Assert.Equal(129, await fixture.Context.Productos.Where(p => p.ProductoId == fixture.ProductId)
            .Select(p => p.PrecioCompraCentavos).SingleAsync());
        Assert.Equal(EstadoRecepcionCompra.PARCIAL, refreshed!.EstadoRecepcion);
        Assert.Equal(TipoMovimientoInventario.ENTRADA_COMPRA, movement.TipoMovimiento);
        Assert.Equal(10, movement.StockAnterior);
        Assert.Equal(14, movement.StockNuevo);
        Assert.Equal(reception.RecepcionCompraId, history.RecepcionCompraId);
    }

    [Fact]
    public async Task RegisterReceptionAsync_SecondReceptionCompletesPurchaseAndUpdatesProductSupplier()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 6, costCentavos: 300);
        var detail = Assert.Single(purchase.Detalles);

        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 2)]));

        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 4)]));

        var refreshed = await fixture.PurchaseQueryService.GetByIdAsync(purchase.CompraId);
        var supplierLink = await fixture.Context.ProductoProveedores.AsNoTracking().SingleAsync();

        Assert.Equal(EstadoRecepcionCompra.RECIBIDA, refreshed!.EstadoRecepcion);
        Assert.Equal(16, await fixture.GetProductStockAsync(fixture.ProductId));
        Assert.Equal(300, supplierLink.UltimoCostoCentavos);
        Assert.True(supplierLink.Activo);
        Assert.True(supplierLink.EsProveedorPrincipal);
    }

    [Fact]
    public async Task RezeOutOfStock_CanBeFoundPurchasedAndReceivedIntoWarehouseStock()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var categoryId = await fixture.Context.Categorias.Select(category => category.CategoriaId).SingleAsync();
        var reze = new Producto
        {
            CodigoInterno = "PRD-000364",
            Nombre = "reze",
            CategoriaId = categoryId,
            PrecioCompraCentavos = 100,
            PrecioVentaCentavos = 1000,
            StockActual = 0,
            StockMinimo = 3,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Productos.Add(reze);
        await fixture.Context.SaveChangesAsync();
        var productService = new ProductService(
            fixture.Context,
            new AuditService(fixture.Context),
            fixture.Session);

        var matches = await productService.GetAllAsync(new ProductFilter(
            SearchText: "rez",
            Estado: EstadoProducto.ACTIVO));
        var found = Assert.Single(matches);
        Assert.Equal(reze.ProductoId, found.ProductoId);
        Assert.Equal(0, found.StockActual);

        var purchase = await fixture.PurchaseService.CreateAsync(new CreatePurchaseRequest(
            fixture.SupplierId,
            DateTime.Today,
            [new PurchaseLineRequest(reze.ProductoId, 9, 100)],
            Observaciones: "Prueba de reposición de producto agotado reze"));
        Assert.Equal(0, await fixture.GetProductStockAsync(reze.ProductoId));
        Assert.Equal(EstadoRecepcionCompra.PENDIENTE, purchase.EstadoRecepcion);

        var detail = Assert.Single(purchase.Detalles);
        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 9, UbicacionInventario.BODEGA)],
            "Recepción completa de prueba reze"));

        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(9, await fixture.GetProductStockAsync(reze.ProductoId));
        var skuId = await fixture.Context.ProductoSkus
            .Where(sku => sku.ProductoId == reze.ProductoId && sku.EsPrincipal)
            .Select(sku => sku.ProductoSkuId)
            .SingleAsync();
        var warehouse = await fixture.Context.InventarioSaldos
            .Where(balance => balance.ProductoSkuId == skuId &&
                balance.Ubicacion == UbicacionInventario.BODEGA)
            .Select(balance => balance.ExistenciaFisica)
            .SingleAsync();
        Assert.Equal(9, warehouse);
        Assert.True(await fixture.Context.ProductoProveedores.AnyAsync(link =>
            link.ProductoId == reze.ProductoId && link.ProveedorId == fixture.SupplierId && link.Activo));
    }

    [Fact]
    public async Task RegisterReceptionAsync_BlocksOverReceptionAndAlreadyCompletedPurchase()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 2, costCentavos: 150);
        var detail = Assert.Single(purchase.Detalles);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
                purchase.CompraId,
                Guid.NewGuid(),
                DateTime.UtcNow,
                [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 3)])));

        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 2)]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
                purchase.CompraId,
                Guid.NewGuid(),
                DateTime.UtcNow,
                [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 1)])));
    }

    [Fact]
    public async Task RegisterReceptionAsync_IsIdempotentByOperationId()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 5, costCentavos: 250);
        var detail = Assert.Single(purchase.Detalles);
        var operationId = Guid.NewGuid();
        var request = new RegisterPurchaseReceptionRequest(
            purchase.CompraId,
            operationId,
            DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 2)]);

        var first = await fixture.ReceptionService.RegisterReceptionAsync(request);
        var second = await fixture.ReceptionService.RegisterReceptionAsync(request);

        Assert.Equal(first.RecepcionCompraId, second.RecepcionCompraId);
        Assert.Equal(12, await fixture.GetProductStockAsync(fixture.ProductId));
        Assert.Equal(1, await fixture.Context.RecepcionesCompra.CountAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ReceptionService.RegisterReceptionAsync(request with
            {
                Detalles = [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 1)]
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ReceptionService.RegisterReceptionAsync(request with
            {
                Detalles = [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 2, UbicacionInventario.BODEGA)]
            }));
    }

    [Fact]
    public async Task RegisterReceptionAsync_PartialReceptionPersistsEachExplicitLocation()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 6, costCentavos: 200);
        var detail = Assert.Single(purchase.Detalles);

        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId, Guid.NewGuid(), DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 2, UbicacionInventario.EXHIBICION)]));
        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId, Guid.NewGuid(), DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 2, UbicacionInventario.BODEGA)]));
        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId, Guid.NewGuid(), DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 2, UbicacionInventario.SIN_ASIGNAR)]));

        var skuId = await fixture.Context.ProductoSkus
            .Where(item => item.ProductoId == fixture.ProductId)
            .Select(item => item.ProductoSkuId)
            .SingleAsync();
        var balances = await fixture.Context.InventarioSaldos.AsNoTracking()
            .Where(item => item.ProductoSkuId == skuId)
            .ToDictionaryAsync(item => item.Ubicacion, item => item.ExistenciaFisica);

        Assert.Equal(12, balances[UbicacionInventario.EXHIBICION]);
        Assert.Equal(2, balances[UbicacionInventario.BODEGA]);
        Assert.Equal(2, balances[UbicacionInventario.SIN_ASIGNAR]);
        Assert.Equal(
            [UbicacionInventario.EXHIBICION, UbicacionInventario.BODEGA, UbicacionInventario.SIN_ASIGNAR],
            await fixture.Context.RecepcionCompraDetalles.AsNoTracking()
                .OrderBy(item => item.RecepcionCompraDetalleId)
                .Select(item => item.UbicacionDestino!.Value)
                .ToListAsync());
    }

    [Fact]
    public async Task RegisterReceptionAsync_UsesSinAsignarWhenLocationIsOmitted()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 1, costCentavos: 200);
        var detail = Assert.Single(purchase.Detalles);

        var result = await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId, Guid.NewGuid(), DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 1)]));

        Assert.Equal(UbicacionInventario.SIN_ASIGNAR, Assert.Single(result.Detalles).UbicacionDestino);
        Assert.Equal(1, await fixture.Context.InventarioSaldos
            .Where(item => item.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
            .Select(item => item.ExistenciaFisica)
            .SingleAsync());
        Assert.Equal(0, await fixture.Context.InventarioSaldos
            .Where(item => item.Ubicacion == UbicacionInventario.BODEGA)
            .Select(item => item.ExistenciaFisica)
            .SingleAsync());
    }

    [Fact]
    public async Task RegisterReceptionAsync_RejectsInvalidLocationWithoutWriting()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 1, costCentavos: 200);
        var detail = Assert.Single(purchase.Detalles);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.ReceptionService.RegisterReceptionAsync(
            new RegisterPurchaseReceptionRequest(
                purchase.CompraId,
                Guid.NewGuid(),
                DateTime.UtcNow,
                [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 1, (UbicacionInventario)999)])));

        Assert.Empty(await fixture.Context.RecepcionesCompra.AsNoTracking().ToListAsync());
        Assert.Equal(10, await fixture.GetProductStockAsync(fixture.ProductId));
    }

    [Fact]
    public async Task HistoricalReceptionWithNullLocationRemainsUnknown()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var purchase = await fixture.CreatePurchaseAsync(quantity: 1, costCentavos: 200);
        var detail = Assert.Single(purchase.Detalles);
        var reception = await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            purchase.CompraId, Guid.NewGuid(), DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 1, UbicacionInventario.SIN_ASIGNAR)]));
        var row = await fixture.Context.RecepcionCompraDetalles
            .SingleAsync(item => item.RecepcionCompraDetalleId == reception.Detalles[0].RecepcionCompraDetalleId);
        row.UbicacionDestino = null;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Assert.Null(await fixture.Context.RecepcionCompraDetalles.AsNoTracking()
            .Where(item => item.RecepcionCompraDetalleId == row.RecepcionCompraDetalleId)
            .Select(item => item.UbicacionDestino)
            .SingleAsync());
    }

    [Fact]
    public async Task RegisterReceptionAsync_ReceivesExactVariantIntoWarehouse()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        var categoryId = await fixture.Context.Categorias.Select(category => category.CategoriaId).SingleAsync();
        var product = new Producto
        {
            CodigoInterno = "VAR-PURCHASE",
            Nombre = "Producto con variantes",
            Modo = ModoProducto.POR_VARIANTES,
            CategoriaId = categoryId,
            PrecioCompraCentavos = 300,
            PrecioVentaCentavos = 800,
            StockMinimo = 1,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        };
        var primary = CreateSku("VAR-PURCHASE-ROSA", "Rosa", isPrimary: true);
        var selected = CreateSku("VAR-PURCHASE-AZUL", "Azul", isPrimary: false);
        product.Skus.Add(primary);
        product.Skus.Add(selected);
        fixture.Context.Productos.Add(product);
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PurchaseService.CreateAsync(
            new CreatePurchaseRequest(fixture.SupplierId, DateTime.Today,
                [new PurchaseLineRequest(product.ProductoId, 3, 350)])));

        var purchase = await fixture.PurchaseService.CreateAsync(new CreatePurchaseRequest(
            fixture.SupplierId,
            DateTime.Today,
            [new PurchaseLineRequest(product.ProductoId, 3, 350, ProductoSkuId: selected.ProductoSkuId)]));
        var line = Assert.Single(purchase.Detalles);
        Assert.Equal(selected.ProductoSkuId, line.ProductoSkuId);
        Assert.Equal(selected.CodigoSku, line.CodigoSku);

        var reception = await fixture.ReceptionService.RegisterReceptionAsync(
            new RegisterPurchaseReceptionRequest(purchase.CompraId, Guid.NewGuid(), DateTime.UtcNow,
                [new PurchaseReceptionLineRequest(line.DetalleCompraId, 3, UbicacionInventario.BODEGA)]));

        var receptionLine = Assert.Single(reception.Detalles);
        Assert.Equal(selected.ProductoSkuId, receptionLine.ProductoSkuId);
        Assert.Equal(UbicacionInventario.BODEGA, receptionLine.UbicacionDestino);
        Assert.Equal(3, await fixture.Context.InventarioSaldos
            .Where(balance => balance.ProductoSkuId == selected.ProductoSkuId &&
                balance.Ubicacion == UbicacionInventario.BODEGA)
            .Select(balance => balance.ExistenciaFisica)
            .SingleAsync());
        Assert.Equal(0, await fixture.Context.InventarioSaldos
            .Where(balance => balance.ProductoSkuId == primary.ProductoSkuId)
            .SumAsync(balance => balance.ExistenciaFisica));
    }

    [Fact]
    public async Task EmployeePermissionsAreEnforcedForPurchases()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        await fixture.SetEmployeePermissionsAsync();
        fixture.SignInEmployee();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PurchaseQueryService.SearchAsync(new PurchaseFilter()));

        await fixture.SetEmployeePermissionsAsync(PermisoEmpleado.COMPRAS_ACCEDER);
        fixture.SignInEmployee();
        var rows = await fixture.PurchaseQueryService.SearchAsync(new PurchaseFilter());
        Assert.Empty(rows);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreatePurchaseAsync(quantity: 1, costCentavos: 100));
    }

    [Fact]
    public async Task QueryService_FiltersBySupplierProductStateAndDate()
    {
        await using var fixture = await PurchaseFixture.CreateAsync();
        fixture.SignInAdmin();
        await fixture.CreatePurchaseAsync(quantity: 1, costCentavos: 100, date: DateTime.Today.AddDays(-5));
        var received = await fixture.CreatePurchaseAsync(quantity: 1, costCentavos: 200, date: DateTime.Today);
        var detail = Assert.Single(received.Detalles);
        await fixture.ReceptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
            received.CompraId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new PurchaseReceptionLineRequest(detail.DetalleCompraId, 1)]));

        var rows = await fixture.PurchaseQueryService.SearchAsync(new PurchaseFilter(
            "Producto compra",
            EstadoRecepcionCompra.RECIBIDA,
            DateTime.Today.AddDays(-1),
            DateTime.Today,
            true));

        var row = Assert.Single(rows);
        Assert.Equal(received.CompraId, row.CompraId);
        Assert.Equal(EstadoRecepcionCompra.RECIBIDA, row.EstadoRecepcion);
    }

    private sealed class PurchaseFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PurchaseFixture(
            SqliteConnection connection,
            AppDbContext context,
            TestCurrentSession session,
            EmployeePermissionService permissionService,
            PurchaseService purchaseService,
            PurchaseQueryService purchaseQueryService,
            PurchaseReceptionService receptionService,
            int adminId,
            int employeeId,
            int supplierId,
            int inactiveSupplierId,
            int productId)
        {
            _connection = connection;
            Context = context;
            Session = session;
            PermissionService = permissionService;
            PurchaseService = purchaseService;
            PurchaseQueryService = purchaseQueryService;
            ReceptionService = receptionService;
            AdminId = adminId;
            EmployeeId = employeeId;
            SupplierId = supplierId;
            InactiveSupplierId = inactiveSupplierId;
            ProductId = productId;
        }

        public AppDbContext Context { get; }
        public TestCurrentSession Session { get; }
        public EmployeePermissionService PermissionService { get; }
        public PurchaseService PurchaseService { get; }
        public PurchaseQueryService PurchaseQueryService { get; }
        public PurchaseReceptionService ReceptionService { get; }
        public int AdminId { get; }
        public int EmployeeId { get; }
        public int SupplierId { get; }
        public int InactiveSupplierId { get; }
        public int ProductId { get; }

        public static async Task<PurchaseFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = CreateUser("Admin compras", RolUsuario.ADMIN);
            var employee = CreateUser("Empleado compras", RolUsuario.EMPLEADO);
            context.Usuarios.AddRange(admin, employee);
            await context.SaveChangesAsync();

            var category = new Categoria
            {
                Nombre = $"Categoria compra {Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var supplier = CreateSupplier("PRV-000001", "Proveedor compra", admin.UsuarioId, EstadoProveedor.ACTIVO);
            var inactiveSupplier = CreateSupplier("PRV-000002", "Proveedor inactivo", admin.UsuarioId, EstadoProveedor.INACTIVO);
            var productCode = $"CMP-PROD-{Guid.NewGuid():N}";
            var product = new Producto
            {
                CodigoInterno = productCode,
                Nombre = "Producto compra",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 500,
                StockActual = 10,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = productCode,
                        NombreVariante = "Principal",
                        Activo = true,
                        EsPrincipal = true,
                        SaldosInventario =
                        {
                            new InventarioSaldo
                            {
                                Ubicacion = UbicacionInventario.EXHIBICION,
                                ExistenciaFisica = 10,
                                Version = 1
                            },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, Version = 1 }
                        }
                    }
                }
            };

            context.Proveedores.AddRange(supplier, inactiveSupplier);
            context.Productos.Add(product);
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, session, audit);
            var queryService = new PurchaseQueryService(context, permissionService);
            var purchaseService = new PurchaseService(context, session, permissionService, audit, queryService);
            var receptionService = new PurchaseReceptionService(context, session, permissionService, audit);

            return new PurchaseFixture(
                connection,
                context,
                session,
                permissionService,
                purchaseService,
                queryService,
                receptionService,
                admin.UsuarioId,
                employee.UsuarioId,
                supplier.ProveedorId,
                inactiveSupplier.ProveedorId,
                product.ProductoId);
        }

        public Task<PurchaseDetail> CreatePurchaseAsync(
            int quantity,
            long costCentavos,
            DateTime? date = null)
        {
            return PurchaseService.CreateAsync(new CreatePurchaseRequest(
                SupplierId,
                date ?? DateTime.Today,
                [new PurchaseLineRequest(ProductId, quantity, costCentavos)]));
        }

        public async Task<int> GetProductStockAsync(int productId)
        {
            return await Context.InventarioSaldos
                .AsNoTracking()
                .Where(balance => balance.ProductoSku.ProductoId == productId)
                .SumAsync(balance => balance.ExistenciaFisica);
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
            Session.IniciarSesion(new UsuarioSesion(
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

        private static Proveedor CreateSupplier(
            string code,
            string name,
            int userId,
            EstadoProveedor state)
        {
            return new Proveedor
            {
                CodigoProveedor = code,
                Tipo = TipoProveedor.PROVEEDOR,
                NombreRazonSocial = name,
                NombreNormalizado = name.ToLowerInvariant(),
                Estado = state,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow,
                UsuarioCreacionId = userId,
                MotivoDesactivacion = state == EstadoProveedor.INACTIVO ? "No disponible" : null,
                FechaDesactivacion = state == EstadoProveedor.INACTIVO ? DateTime.UtcNow : null
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

    private static ProductoSku CreateSku(string code, string variant, bool isPrimary)
    {
        var sku = new ProductoSku
        {
            CodigoSku = code,
            NombreVariante = variant,
            Activo = true,
            EsPrincipal = isPrimary
        };
        sku.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.EXHIBICION, Version = 1 });
        sku.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 });
        sku.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, Version = 1 });
        return sku;
    }
}
