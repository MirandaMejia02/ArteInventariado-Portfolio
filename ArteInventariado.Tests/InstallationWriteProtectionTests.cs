using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class InstallationWriteProtectionTests
{
    [Fact]
    public async Task UnconfiguredInstallation_ProtectedAdminCanChangePasswordBeforeStationSetup()
    {
        var currentPassword = TestCredentials.Password("current-admin");
        var replacementPassword = TestCredentials.Password("replacement-admin");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var installation = new MutableInstallationContext(InstallationMode.Principal);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new InstallationWriteGuardInterceptor(installation))
            .Options;
        await using var verification = new AppDbContext(options);
        await verification.Database.EnsureCreatedAsync();
        var admin = new Usuario
        {
            NombreCompleto = "Administrador",
            NombreUsuario = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(currentPassword),
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        verification.Usuarios.Add(admin);
        await verification.SaveChangesAsync();
        verification.ChangeTracker.Clear();
        installation.SetRequiresSetup();
        var session = new TestSession(new UsuarioSesion(admin.UsuarioId, admin.NombreCompleto, admin.NombreUsuario, admin.Rol));
        var account = new AccountService(
            new TestContextFactory(options),
            session,
            new CollectingAuditService(),
            installation);

        await account.ChangeCurrentPasswordAsync(new(
            currentPassword,
            replacementPassword,
            replacementPassword));

        var hash = await verification.Usuarios.AsNoTracking().Select(user => user.PasswordHash).SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify(replacementPassword, hash));
        Assert.False(BCrypt.Net.BCrypt.Verify(currentPassword, hash));
        Assert.DoesNotContain(replacementPassword, hash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConsultaMode_LoginAndLogoutRemainAvailableWithoutMutatingDatabase()
    {
        var loginPassword = TestCredentials.Password("consulta-login");
        var logDirectory = Path.Combine(Path.GetTempPath(), $"ArteInventariado.LoginConsulta.{Guid.NewGuid():N}");
        var logPath = Path.Combine(logDirectory, "station-audit.log");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var installation = new MutableInstallationContext(InstallationMode.Principal);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new InstallationWriteGuardInterceptor(installation))
            .Options;
        await using var seed = new AppDbContext(options);
        await seed.Database.EnsureCreatedAsync();
        seed.Usuarios.Add(new Usuario
        {
            NombreCompleto = "Administrador",
            NombreUsuario = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(loginPassword),
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        seed.ChangeTracker.Clear();
        installation.SetMode(InstallationMode.Consulta);

        var session = new EmptyTestSession();
        var audit = new CollectingAuditService();
        var auth = new AuthService(
            new TestContextFactory(options),
            audit,
            session,
            installation,
            new StationEventLog(logPath));

        try
        {
            var result = await auth.LoginAsync("admin", loginPassword);
            Assert.True(result.Exitoso);
            Assert.True(session.EstaAutenticado);
            Assert.Null(await seed.Usuarios.Select(user => user.UltimoLogin).SingleAsync());
            Assert.Empty(await seed.Bitacora.AsNoTracking().ToListAsync());

            await auth.LogoutAsync();

            Assert.False(session.EstaAutenticado);
            Assert.Empty(audit.Entries);
            var localLog = File.ReadAllText(logPath);
            Assert.Contains("LOGIN_READ_ONLY", localLog);
            Assert.Contains("LOGOUT_READ_ONLY", localLog);
            Assert.DoesNotContain(loginPassword, localLog);
        }
        finally
        {
            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrincipalMode_AllowsReadsAndExistingServiceWrites()
    {
        await using var fixture = await ProtectedServicesFixture.CreateAsync(InstallationMode.Principal);

        var existing = await fixture.ProductService.GetAllAsync();
        var created = await fixture.ProductService.CreateAsync(fixture.NewProductRequest("Producto Principal"));

        Assert.Single(existing);
        Assert.Equal("Producto Principal", created.Nombre);
        Assert.Equal(2, await fixture.Db.Productos.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task ConsultaMode_AllowsRealProductReadButBlocksProductAndInventoryServices()
    {
        await using var fixture = await ProtectedServicesFixture.CreateAsync(InstallationMode.Consulta);

        var products = await fixture.ProductService.GetAllAsync();
        Assert.Single(products);
        var skus = await fixture.Db.ProductoSkus.AsNoTracking().ToListAsync();
        Assert.Single(skus);
        Assert.Equal("PRD-PROTECTED-01", skus[0].CodigoSku);
        Assert.Equal(3, (await fixture.InventoryService.GetBalancesByProductAsync(fixture.ProductId)).Count);

        var sku = await fixture.Db.ProductoSkus.SingleAsync();
        sku.Activo = false;
        await AssertWriteDeniedAsync(() => fixture.Db.SaveChangesAsync());
        fixture.ResetTracking();

        await AssertWriteDeniedAsync(() =>
            fixture.ProductService.CreateAsync(
                fixture.NewProductRequest("Producto bloqueado") with
                {
                    UbicacionInicial = UbicacionInventario.BODEGA
                }));
        fixture.ResetTracking();

        await AssertWriteDeniedAsync(() => fixture.InventoryService.AdjustStockAsync(new(
            fixture.ProductId,
            25,
            "Intento en consulta")));
        fixture.ResetTracking();

        await AssertWriteDeniedAsync(() => fixture.InventoryService.TransferAsync(new(
            Guid.NewGuid(), skus[0].ProductoSkuId,
            UbicacionInventario.SIN_ASIGNAR,
            UbicacionInventario.EXHIBICION,
            1,
            "Intento de transferencia en consulta")));
        fixture.ResetTracking();

        Assert.Equal(10, await fixture.StoredStockAsync());
        Assert.Single(await fixture.ProductService.GetAllAsync());
    }

    [Fact]
    public async Task ConsultaMode_BlocksRealSalePurchaseAndCashServices()
    {
        await using var fixture = await ProtectedServicesFixture.CreateAsync(InstallationMode.Consulta);

        await AssertWriteDeniedAsync(() => fixture.SaleService.RegisterSaleAsync(new(
            [new SaleLineRequest(fixture.ProductId, 1)],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 500)])));
        fixture.ResetTracking();

        await AssertWriteDeniedAsync(() => fixture.PurchaseService.CreateAsync(new(
            fixture.SupplierId,
            DateTime.Today,
            [new PurchaseLineRequest(fixture.ProductId, 2, 100)])));
        fixture.ResetTracking();

        await AssertWriteDeniedAsync(() => fixture.CashService.RegisterWithdrawalAsync(new(
            100,
            "Intento en consulta")));
        fixture.ResetTracking();

        Assert.Equal(0, await fixture.Db.Ventas.AsNoTracking().CountAsync());
        Assert.Equal(0, await fixture.Db.Compras.AsNoTracking().CountAsync());
        Assert.Equal(0, await fixture.Db.RetirosCaja.AsNoTracking().CountAsync());
        Assert.Equal(10, await fixture.StoredStockAsync());
    }

    [Theory]
    [InlineData("cobro/abono")]
    [InlineData("devolucion/reembolso")]
    [InlineData("cliente/proveedor")]
    [InlineData("cotizacion/pedido")]
    public async Task ConsultaMode_CentralPersistenceGuardBlocksAnyTrackedOperationalWrite(string operation)
    {
        await using var fixture = await ProtectedServicesFixture.CreateAsync(InstallationMode.Consulta);
        var product = await fixture.Db.Productos.SingleAsync(item => item.ProductoId == fixture.ProductId);
        product.Descripcion = operation;

        var exception = await Assert.ThrowsAsync<InstallationWriteDeniedException>(
            () => fixture.Db.SaveChangesAsync());

        Assert.Equal("Persistencia operativa", exception.OperationName);
    }

    private static async Task AssertWriteDeniedAsync(Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<InstallationWriteDeniedException>(operation);
        Assert.Contains("modo Consulta", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProtectedServicesFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ProtectedServicesFixture(
            SqliteConnection connection,
            AppDbContext db,
            ProductService productService,
            InventoryService inventoryService,
            SaleService saleService,
            PurchaseService purchaseService,
            CashService cashService,
            int categoryId,
            int productId,
            int supplierId)
        {
            _connection = connection;
            Db = db;
            ProductService = productService;
            InventoryService = inventoryService;
            SaleService = saleService;
            PurchaseService = purchaseService;
            CashService = cashService;
            CategoryId = categoryId;
            ProductId = productId;
            SupplierId = supplierId;
        }

        public AppDbContext Db { get; }
        public ProductService ProductService { get; }
        public InventoryService InventoryService { get; }
        public SaleService SaleService { get; }
        public PurchaseService PurchaseService { get; }
        public CashService CashService { get; }
        public int CategoryId { get; }
        public int ProductId { get; }
        public int SupplierId { get; }

        public static async Task<ProtectedServicesFixture> CreateAsync(InstallationMode mode)
        {
            var installation = new MutableInstallationContext(InstallationMode.Principal);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new InstallationWriteGuardInterceptor(installation))
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var admin = new Usuario
            {
                NombreCompleto = "Administrador protegido",
                NombreUsuario = "admin",
                PasswordHash = "existing-hash-must-not-change",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = "Categoria protegida",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            db.AddRange(admin, category);
            await db.SaveChangesAsync();

            var product = new Producto
            {
                CodigoInterno = "PRD-PROTECTED-01",
                Nombre = "Producto protegido",
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
                        CodigoSku = "PRD-PROTECTED-01",
                        EsPrincipal = true,
                        SaldosInventario =
                        {
                            new InventarioSaldo { Ubicacion = UbicacionInventario.EXHIBICION, Version = 1 },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 },
                            new InventarioSaldo
                            {
                                Ubicacion = UbicacionInventario.SIN_ASIGNAR,
                                ExistenciaFisica = 10,
                                Version = 1
                            }
                        }
                    }
                }
            };
            var supplier = new Proveedor
            {
                CodigoProveedor = "PRV-PROTECTED-01",
                NombreRazonSocial = "Proveedor protegido",
                NombreNormalizado = "proveedor protegido",
                Estado = EstadoProveedor.ACTIVO,
                UsuarioCreacionId = admin.UsuarioId,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };
            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = admin.UsuarioId
            };
            db.AddRange(product, supplier, cash);
            await db.SaveChangesAsync();

            var session = new TestSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            var permissions = new AllowAllPermissionService();
            var audit = new AuditService(db);
            var productService = new ProductService(db, audit, session);
            var inventoryService = new InventoryService(db, audit, session, installation);
            var credit = new CreditService(db, session, permissions, audit);
            var sale = new SaleService(
                new TestContextFactory(options),
                session,
                audit,
                permissions,
                credit,
                installation);
            var purchaseQuery = new PurchaseQueryService(db, permissions);
            var purchase = new PurchaseService(db, session, permissions, audit, purchaseQuery);
            var cashService = new CashService(db, session, audit, new NullNotificationService(), permissions);

            installation.SetMode(mode);
            db.ChangeTracker.Clear();
            return new ProtectedServicesFixture(
                connection,
                db,
                productService,
                inventoryService,
                sale,
                purchase,
                cashService,
                category.CategoriaId,
                product.ProductoId,
                supplier.ProveedorId);
        }

        public CreateProductRequest NewProductRequest(string name) => new(
            null,
            name,
            null,
            CategoryId,
            null,
            100,
            500,
            0,
            1);

        public Task<int> StoredStockAsync() => Db.Productos
            .AsNoTracking()
            .Where(item => item.ProductoId == ProductId)
            .Select(item => item.StockActual)
            .SingleAsync();

        public void ResetTracking() => Db.ChangeTracker.Clear();

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class MutableInstallationContext(InstallationMode initialMode) : IInstallationContext
    {
        private InstallationMode _mode = initialMode;
        private bool _requiresSetup;

        public InstallationStatus Status => new(
            _requiresSetup ? StationConfigurationState.RequiresSetup : StationConfigurationState.Ready,
            _requiresSetup ? null : _mode,
            !_requiresSetup,
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            1,
            "test-station-settings.json",
            null);

        public bool AllowsOperationalWrites => !_requiresSetup && _mode == InstallationMode.Principal;
        public void EnsureInitialized()
        {
        }

        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!AllowsOperationalWrites && !InstallationWriteAuthorizationScope.IsAuthorized)
            {
                throw new InstallationWriteDeniedException(InstallationContext.WriteDeniedMessage, operationName);
            }
        }

        public void SetMode(InstallationMode mode)
        {
            _mode = mode;
            _requiresSetup = false;
        }

        public void SetRequiresSetup() => _requiresSetup = true;
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

    private sealed class EmptyTestSession : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; }
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class CollectingAuditService : IAuditService
    {
        public List<AuditEntryRequest> Entries { get; } = [];

        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            Entries.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class AllowAllPermissionService : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(PermisoEmpleado permiso, int? usuarioId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> HasAnyPermissionAsync(IReadOnlyCollection<PermisoEmpleado> permisos, int? usuarioId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task EnsurePermissionAsync(PermisoEmpleado permiso, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(int usuarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);

        public Task UpdatePermissionsAsync(UpdateEmployeePermissionsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreRecommendedTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ApplySafeOperationalTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureRecommendedPermissionsForEmployeeAsync(int usuarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
