using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class BarcodeServiceFactoryTests
{
    [Fact]
    public async Task FactoryBackedMutations_PersistBarcodesAndAuditsWithoutDatabaseLock()
    {
        await using var fixture = await FactoryFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var assigned = await fixture.Service.AssignAsync(new(
            fixture.ProductoSkuId,
            "FACTORY-EXT-001",
            EsPrincipal: true), timeout.Token);
        var generated = await fixture.Service.GenerateInternalAsync(new(
            fixture.ProductoSkuId), timeout.Token);
        await fixture.Service.SetPrincipalAsync(generated.ProductoSkuBarcodeId, timeout.Token);
        await fixture.Service.SetActiveAsync(generated.ProductoSkuBarcodeId, false, timeout.Token);

        await using var verification = fixture.Factory.CreateDbContext();
        var barcodes = await verification.ProductoSkuBarcodes
            .AsNoTracking()
            .OrderBy(item => item.ProductoSkuBarcodeId)
            .ToListAsync(timeout.Token);
        var audits = await verification.Bitacora
            .AsNoTracking()
            .Where(item => item.Entidad == "producto_sku_barcodes")
            .ToListAsync(timeout.Token);

        Assert.Equal(2, barcodes.Count);
        var storedAssigned = Assert.Single(barcodes, item =>
            item.ProductoSkuBarcodeId == assigned.ProductoSkuBarcodeId);
        Assert.Equal("FACTORY-EXT-001", storedAssigned.Codigo);
        Assert.True(storedAssigned.Activo);
        Assert.False(storedAssigned.EsPrincipal);

        var storedGenerated = Assert.Single(barcodes, item =>
            item.ProductoSkuBarcodeId == generated.ProductoSkuBarcodeId);
        Assert.Matches("^AI[0-9]{10}$", storedGenerated.Codigo);
        Assert.False(storedGenerated.Activo);
        Assert.False(storedGenerated.EsPrincipal);

        Assert.Single(audits, item => item.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_ASIGNADO);
        Assert.Single(audits, item => item.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_GENERADO);
        Assert.Single(audits, item => item.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_ESTADO_MODIFICADO);
        Assert.Equal(3, audits.Count(item =>
            item.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_PRINCIPAL_MODIFICADO));
        Assert.Equal(6, audits.Count);
        Assert.All(audits, item => Assert.Equal(fixture.UserId, item.UsuarioId));
    }

    [Fact]
    public async Task FactoryBackedAssign_WhenAuditFails_RollsBackBarcodeAndAllowsRetry()
    {
        await using var fixture = await FactoryFixture.CreateAsync();
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_factory_barcode_audit
                BEFORE INSERT ON bitacora
                WHEN NEW.tipo_accion = 'PRODUCTO_BARCODE_ASIGNADO'
                BEGIN
                    SELECT RAISE(ABORT, 'simulated barcode audit failure');
                END;
                """);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service.AssignAsync(new(
            fixture.ProductoSkuId,
            "FACTORY-ROLLBACK-001")));

        await using (var verification = fixture.Factory.CreateDbContext())
        {
            Assert.False(await verification.ProductoSkuBarcodes
                .AnyAsync(item => item.Codigo == "FACTORY-ROLLBACK-001"));
            Assert.False(await verification.Bitacora.AnyAsync(item =>
                item.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_ASIGNADO));
            await verification.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_factory_barcode_audit;");
        }

        var retry = await fixture.Service.AssignAsync(new(
            fixture.ProductoSkuId,
            "FACTORY-ROLLBACK-001"));

        await using var finalVerification = fixture.Factory.CreateDbContext();
        Assert.True(await finalVerification.ProductoSkuBarcodes.AnyAsync(item =>
            item.ProductoSkuBarcodeId == retry.ProductoSkuBarcodeId &&
            item.Codigo == "FACTORY-ROLLBACK-001"));
        Assert.Single(await finalVerification.Bitacora
            .Where(item => item.TipoAccion == TipoBitacora.PRODUCTO_BARCODE_ASIGNADO)
            .ToListAsync());
    }

    [Fact]
    public async Task GenerateMissingBarcodes_ConcurrentRequestsLeaveOneActivePrincipalAndControlledResult()
    {
        await using var fixture = await FactoryFixture.CreateAsync();
        var otherProcess = fixture.CreateAdditionalService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await Task.WhenAll(
            fixture.Service.GenerateMissingBarcodesAsync(
                new GenerateMissingBarcodesRequest([fixture.ProductoSkuId]), timeout.Token),
            otherProcess.GenerateMissingBarcodesAsync(
                new GenerateMissingBarcodesRequest([fixture.ProductoSkuId]), timeout.Token));

        await using var verification = fixture.Factory.CreateDbContext();
        var barcodes = await verification.ProductoSkuBarcodes
            .Where(item => item.ProductoSkuId == fixture.ProductoSkuId && item.Activo)
            .ToListAsync(timeout.Token);
        Assert.Single(barcodes);
        Assert.True(barcodes.Single().EsPrincipal);
        Assert.Equal(1, results.Sum(result => result.GeneratedCount));
        Assert.Equal(1, results.Sum(result => result.OmittedCount));
        Assert.Equal(0, results.Sum(result => result.ErrorCount));
    }

    private sealed class FactoryFixture : IAsyncDisposable
    {
        private readonly string _temporaryDirectory;

        private FactoryFixture(
            string temporaryDirectory,
            FileContextFactory factory,
            BarcodeService service,
            int userId,
            int productoSkuId)
        {
            _temporaryDirectory = temporaryDirectory;
            Factory = factory;
            Service = service;
            UserId = userId;
            ProductoSkuId = productoSkuId;
        }

        public FileContextFactory Factory { get; }

        public BarcodeService Service { get; }

        public int UserId { get; }

        public int ProductoSkuId { get; }

        public BarcodeService CreateAdditionalService() => new(
            Factory,
            new AllowProductsPermissionService(),
            new PrincipalInstallationContext(),
            new TestSession(new UsuarioSesion(UserId, "Administrador barcode factory", "admin-barcode-factory", RolUsuario.ADMIN)),
            new AuditService(Factory));

        public static async Task<FactoryFixture> CreateAsync()
        {
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "ArteInventariado.Tests",
                $"barcode-factory-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            var factory = new FileContextFactory(Path.Combine(temporaryDirectory, "barcode.db"));

            await using var context = factory.CreateDbContext();
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador barcode factory",
                NombreUsuario = $"admin-barcode-factory-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = $"Barcode factory {Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.AddRange(user, category);
            await context.SaveChangesAsync();

            var product = new Producto
            {
                CodigoInterno = $"PRD-FACTORY-{Guid.NewGuid():N}",
                Nombre = "Producto barcode factory",
                CategoriaId = category.CategoriaId,
                Modo = ModoProducto.SIMPLE,
                PrecioCompraCentavos = 500,
                PrecioVentaCentavos = 1000,
                StockActual = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            var sku = new ProductoSku
            {
                Producto = product,
                CodigoSku = $"SKU-FACTORY-{Guid.NewGuid():N}",
                Activo = true,
                EsPrincipal = true
            };
            product.Skus.Add(sku);
            context.Productos.Add(product);
            await context.SaveChangesAsync();

            var session = new TestSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
            var audit = new AuditService(factory);
            var service = new BarcodeService(
                factory,
                new AllowProductsPermissionService(),
                new PrincipalInstallationContext(),
                session,
                audit);

            return new FactoryFixture(
                temporaryDirectory,
                factory,
                service,
                user.UsuarioId,
                sku.ProductoSkuId);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var context = Factory.CreateDbContext();
                await context.Database.EnsureDeletedAsync();
            }
            finally
            {
                if (Directory.Exists(_temporaryDirectory))
                {
                    Directory.Delete(_temporaryDirectory, recursive: true);
                }
            }
        }
    }

    private sealed class FileContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Default Timeout=2;Pooling=False")
                .Options;

        public AppDbContext CreateDbContext() => new(_options);
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

    private sealed class PrincipalInstallationContext : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            InstallationMode.Principal,
            true,
            Guid.NewGuid(),
            1,
            "barcode-factory-test",
            null);

        public bool AllowsOperationalWrites => true;

        public void EnsureInitialized()
        {
        }

        public void EnsureOperationalWriteAllowed(string operationName)
        {
        }
    }

    private sealed class AllowProductsPermissionService : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(
            PermisoEmpleado permiso,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permiso == PermisoEmpleado.PRODUCTOS_GESTIONAR);

        public Task<bool> HasAnyPermissionAsync(
            IReadOnlyCollection<PermisoEmpleado> permisos,
            int? usuarioId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permisos.Contains(PermisoEmpleado.PRODUCTOS_GESTIONAR));

        public Task EnsurePermissionAsync(
            PermisoEmpleado permiso,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(PermisoEmpleado.PRODUCTOS_GESTIONAR, permiso);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeePermissionState>>([]);

        public Task UpdatePermissionsAsync(
            UpdateEmployeePermissionsRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestoreRecommendedTemplateAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplySafeOperationalTemplateAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureRecommendedPermissionsForEmployeeAsync(
            int usuarioId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
