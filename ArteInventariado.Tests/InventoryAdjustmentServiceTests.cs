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

public sealed class InventoryAdjustmentServiceTests
{
    [Fact]
    public async Task T01_AdjustsExactLocationAndAuditsWithoutChangingOtherBusinessData()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.AdjustPhysicalCountAsync(fixture.Request(
            UbicacionInventario.EXHIBICION, counted: 3));

        Assert.Equal(2, result.ExistenciaAnterior);
        Assert.Equal(3, result.CantidadContada);
        Assert.Equal(1, result.Diferencia);
        Assert.Equal(3, await fixture.PhysicalAsync(UbicacionInventario.EXHIBICION));
        Assert.Equal(8, await fixture.PhysicalAsync(UbicacionInventario.BODEGA));
        Assert.Equal(5, await fixture.PhysicalAsync(UbicacionInventario.SIN_ASIGNAR));
        Assert.Equal(15, await fixture.ProductStockAsync());
        Assert.Equal(1, await fixture.Context.MovimientosInventarioUbicacion.CountAsync(
            item => item.OperacionId == result.OperacionId && item.TipoOperacion == TipoOperacionInventario.AJUSTE));
        var audit = Assert.Single(await fixture.Context.Bitacora.AsNoTracking()
            .Where(item => item.TipoAccion == TipoBitacora.INVENTARIO_AJUSTADO)
            .ToListAsync());
        Assert.Contains("EXHIBICION", audit.DatosDespues, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OperacionId", audit.DatosDespues, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Compras.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task T01B_CorrectsOvercountWithNegativeDifferenceWithoutDeletingSkuOrChangingOtherLocations()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.AdjustPhysicalCountAsync(fixture.Request(
            UbicacionInventario.EXHIBICION, counted: 1));

        Assert.Equal(2, result.ExistenciaAnterior);
        Assert.Equal(1, result.CantidadContada);
        Assert.Equal(-1, result.Diferencia);
        Assert.Equal(1, await fixture.PhysicalAsync(UbicacionInventario.EXHIBICION));
        Assert.Equal(8, await fixture.PhysicalAsync(UbicacionInventario.BODEGA));
        Assert.Equal(5, await fixture.PhysicalAsync(UbicacionInventario.SIN_ASIGNAR));
        Assert.Equal(1, await fixture.Context.ProductoSkus
            .AsNoTracking()
            .CountAsync(item => item.ProductoSkuId == fixture.SkuId));
        var movement = Assert.Single(await fixture.Context.MovimientosInventarioUbicacion
            .AsNoTracking()
            .Where(item => item.OperacionId == result.OperacionId && item.TipoOperacion == TipoOperacionInventario.AJUSTE)
            .ToListAsync());
        Assert.Equal(UbicacionInventario.EXHIBICION, movement.UbicacionOrigen);
        Assert.Null(movement.UbicacionDestino);
        Assert.Equal(1, movement.Cantidad);
        Assert.Equal(15, await fixture.ProductStockAsync());
        Assert.Empty(await fixture.Context.Compras.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task T02_AdjustmentBelowReservedIsRejectedWithoutPartialChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var balance = await fixture.Context.InventarioSaldos.SingleAsync(item =>
            item.ProductoSkuId == fixture.SkuId && item.Ubicacion == UbicacionInventario.BODEGA);
        balance.Reservado = 2;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AdjustPhysicalCountAsync(
            fixture.Request(UbicacionInventario.BODEGA, counted: 1)));

        Assert.Equal(8, await fixture.PhysicalAsync(UbicacionInventario.BODEGA));
        Assert.Equal(2, await fixture.ReservedAsync(UbicacionInventario.BODEGA));
        Assert.Empty(await fixture.Context.MovimientosInventarioUbicacion.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Context.Bitacora.AsNoTracking()
            .Where(item => item.TipoAccion == TipoBitacora.INVENTARIO_AJUSTADO)
            .ToListAsync());
    }

    [Fact]
    public async Task T03_StaleAdjustmentConflictsWithSaleAndDoesNotLoseSale()
    {
        await using var fixture = await ConcurrentFixture.CreateAsync();
        // El servicio de ajuste reutiliza la entidad ya rastreada. Se carga el
        // saldo obsoleto, se confirma la venta en otro contexto y solo entonces
        // se invoca el servicio; no se reintenta el ajuste con ese conteo.
        await fixture.AdjustmentContext.ProductoSkus
            .Include(item => item.Producto)
            .Include(item => item.SaldosInventario)
            .SingleAsync(item => item.ProductoSkuId == fixture.SkuId);

        await using (var saleContext = fixture.NewContext())
        {
            var saleSku = await saleContext.ProductoSkus
                .Include(item => item.Producto)
                .Include(item => item.SaldosInventario)
                .SingleAsync(item => item.ProductoSkuId == fixture.SkuId);

            InventoryStockEngine.RemovePhysical(
                saleContext,
                saleSku,
                1,
                fixture.AdminId,
                TipoOperacionInventario.SALIDA_VENTA,
                "Venta concurrente",
                DateTime.UtcNow,
                UbicacionInventario.EXHIBICION,
                fixture.SaleOperationId);
            await saleContext.SaveChangesAsync();
            fixture.SaleCommitted = true;
        }

        var exception = await Record.ExceptionAsync(() => fixture.Service.AdjustPhysicalCountAsync(
            fixture.Request(UbicacionInventario.EXHIBICION, counted: 3)));

        Assert.NotNull(exception);
        Assert.True(IsConcurrencyConflict(exception), exception.ToString());
        Assert.True(fixture.SaleCommitted);

        await using var verificationContext = fixture.NewContext();
        Assert.Equal(1, await verificationContext.InventarioSaldos
            .Where(item => item.ProductoSkuId == fixture.SkuId &&
                item.Ubicacion == UbicacionInventario.EXHIBICION)
            .Select(item => item.ExistenciaFisica)
            .SingleAsync());
        Assert.Equal(14, await verificationContext.InventarioSaldos
            .Where(item => item.ProductoSkuId == fixture.SkuId)
            .SumAsync(item => item.ExistenciaFisica));
        // Producto.StockActual es una proyección histórica y no se escribe en
        // operaciones nuevas; el saldo canónico anterior ya se verificó arriba.
        Assert.Equal(15, await verificationContext.Productos
            .Where(item => item.ProductoId == fixture.ProductId)
            .Select(item => item.StockActual)
            .SingleAsync());
        Assert.Equal(1, await verificationContext.MovimientosInventarioUbicacion.CountAsync(
            item => item.OperacionId == fixture.SaleOperationId &&
                item.TipoOperacion == TipoOperacionInventario.SALIDA_VENTA));
        Assert.Empty(await verificationContext.MovimientosInventarioUbicacion
            .Where(item => item.TipoOperacion == TipoOperacionInventario.AJUSTE)
            .ToListAsync());
        Assert.Empty(await verificationContext.MovimientosInventario
            .Where(item => item.TipoMovimiento == TipoMovimientoInventario.AJUSTE)
            .ToListAsync());
        Assert.Empty(await verificationContext.Bitacora
            .Where(item => item.TipoAccion == TipoBitacora.INVENTARIO_AJUSTADO)
            .ToListAsync());
    }

    [Fact]
    public async Task T04_EmployeeAndConsultaAreRejectedByTheService()
    {
        await using var employee = await Fixture.CreateAsync(RolUsuario.EMPLEADO);
        await Assert.ThrowsAsync<InvalidOperationException>(() => employee.Service.AdjustPhysicalCountAsync(
            employee.Request(UbicacionInventario.EXHIBICION, counted: 3)));
        Assert.Empty(await employee.Context.MovimientosInventarioUbicacion.AsNoTracking().ToListAsync());

        await using var consulta = await Fixture.CreateAsync(RolUsuario.ADMIN, InstallationMode.Consulta);
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() => consulta.Service.AdjustPhysicalCountAsync(
            consulta.Request(UbicacionInventario.EXHIBICION, counted: 3)));
        Assert.Empty(await consulta.Context.MovimientosInventarioUbicacion.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task T06_RepeatingOperationIsIdempotentAndDifferentPayloadIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.Request(UbicacionInventario.EXHIBICION, counted: 3);

        var first = await fixture.Service.AdjustPhysicalCountAsync(request);
        var second = await fixture.Service.AdjustPhysicalCountAsync(request);

        Assert.Equal(first.MovimientoInventarioUbicacionId, second.MovimientoInventarioUbicacionId);
        Assert.Equal(1, await fixture.Context.MovimientosInventarioUbicacion.CountAsync(
            item => item.OperacionId == request.OperacionId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AdjustPhysicalCountAsync(
            request with { CantidadContada = 4 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AdjustPhysicalCountAsync(
            request with { Ubicacion = UbicacionInventario.BODEGA }));
    }

    [Fact]
    public async Task ZeroDifferenceCreatesAuditButNoMovement()
    {
        await using var fixture = await Fixture.CreateAsync();

        var request = fixture.Request(UbicacionInventario.EXHIBICION, counted: 2);
        var result = await fixture.Service.AdjustPhysicalCountAsync(request);
        var replay = await fixture.Service.AdjustPhysicalCountAsync(request);

        Assert.Equal(0, result.Diferencia);
        Assert.Null(result.MovimientoInventarioUbicacionId);
        Assert.Null(replay.MovimientoInventarioUbicacionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AdjustPhysicalCountAsync(
            request with { CantidadContada = 3 }));
        Assert.Empty(await fixture.Context.MovimientosInventarioUbicacion.AsNoTracking().ToListAsync());
        Assert.Single(await fixture.Context.Bitacora.AsNoTracking()
            .Where(item => item.TipoAccion == TipoBitacora.INVENTARIO_AJUSTADO)
            .ToListAsync());
    }

    [Fact]
    public async Task InvalidReauthenticationIsRejectedWithoutWriting()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AdjustPhysicalCountAsync(
            fixture.Request(UbicacionInventario.EXHIBICION, counted: 3) with
            {
                ReauthenticationPassword = "incorrecta"
            }));

        Assert.Equal(2, await fixture.PhysicalAsync(UbicacionInventario.EXHIBICION));
        Assert.Empty(await fixture.Context.MovimientosInventarioUbicacion.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task VariantSkuIsAdjustedWithoutTouchingPrimarySku()
    {
        await using var fixture = await Fixture.CreateAsync();
        var variant = new ProductoSku
        {
            ProductoId = fixture.ProductId,
            CodigoSku = $"VAR-{Guid.NewGuid():N}"[..20],
            NombreVariante = "Azul",
            Activo = true,
            EsPrincipal = false
        };
        variant.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.EXHIBICION, Version = 1 });
        variant.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 });
        variant.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, Version = 1 });
        fixture.Context.ProductoSkus.Add(variant);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.AdjustPhysicalCountAsync(
            new AdjustInventoryCountRequest(Guid.NewGuid(), variant.ProductoSkuId,
                UbicacionInventario.BODEGA, 4, "Conteo de variante", Fixture.Password));

        Assert.Equal(4, result.CantidadContada);
        Assert.Equal(4, await fixture.Context.InventarioSaldos
            .Where(item => item.ProductoSkuId == variant.ProductoSkuId && item.Ubicacion == UbicacionInventario.BODEGA)
            .Select(item => item.ExistenciaFisica)
            .SingleAsync());
        Assert.Equal(8, await fixture.PhysicalAsync(UbicacionInventario.BODEGA));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string Password = "Admin123!";
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            AppDbContext context,
            InventoryService service,
            TestSession session,
            TestInstallationContext installation,
            int adminId,
            int productId,
            int skuId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Session = session;
            Installation = installation;
            AdminId = adminId;
            ProductId = productId;
            SkuId = skuId;
        }

        public AppDbContext Context { get; }
        public InventoryService Service { get; }
        public TestSession Session { get; }
        public TestInstallationContext Installation { get; }
        public int AdminId { get; }
        public int ProductId { get; }
        public int SkuId { get; }

        public static async Task<Fixture> CreateAsync(
            RolUsuario role = RolUsuario.ADMIN,
            InstallationMode mode = InstallationMode.Principal)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador de inventario",
                NombreUsuario = $"inventory-{Guid.NewGuid():N}",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                Rol = role,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria { Nombre = $"Categoria-{Guid.NewGuid():N}", Activa = true, FechaCreacion = DateTime.UtcNow };
            var sku = new ProductoSku { CodigoSku = $"SKU-{Guid.NewGuid():N}"[..20], Activo = true, EsPrincipal = true };
            sku.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.EXHIBICION, ExistenciaFisica = 2, Version = 1 });
            sku.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, ExistenciaFisica = 8, Version = 1 });
            sku.SaldosInventario.Add(new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, ExistenciaFisica = 5, Version = 1 });
            var product = new Producto
            {
                CodigoInterno = $"PRD-{Guid.NewGuid():N}"[..20],
                Nombre = "Producto de ajuste",
                Categoria = category,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 250,
                StockActual = 15,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus = { sku }
            };
            context.AddRange(user, product);
            await context.SaveChangesAsync();

            var session = new TestSession(new UsuarioSesion(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, role));
            var installation = new TestInstallationContext(mode);
            var service = new InventoryService(
                context,
                new AuditService(context),
                session,
                installation,
                new AdminReauthenticationService(context, session));
            return new Fixture(connection, context, service, session, installation,
                user.UsuarioId, product.ProductoId, sku.ProductoSkuId);
        }

        public AdjustInventoryCountRequest Request(UbicacionInventario location, int counted) =>
            new(Guid.NewGuid(), SkuId, location, counted, "Conteo fisico", Password);

        public AppDbContext NewContext() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        public Task<int> PhysicalAsync(UbicacionInventario location) => Context.InventarioSaldos
            .AsNoTracking()
            .Where(item => item.ProductoSkuId == SkuId && item.Ubicacion == location)
            .Select(item => item.ExistenciaFisica)
            .SingleAsync();

        public Task<int> ReservedAsync(UbicacionInventario location) => Context.InventarioSaldos
            .AsNoTracking()
            .Where(item => item.ProductoSkuId == SkuId && item.Ubicacion == location)
            .Select(item => item.Reservado)
            .SingleAsync();

        public Task<int> ProductStockAsync() => Context.Productos
            .AsNoTracking()
            .Where(item => item.ProductoId == ProductId)
            .Select(item => item.StockActual)
            .SingleAsync();

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ConcurrentFixture : IAsyncDisposable
    {
        private ConcurrentFixture(
            string databasePath,
            string connectionString,
            AppDbContext adjustmentContext,
            InventoryService service,
            int adminId,
            int productId,
            int skuId)
        {
            DatabasePath = databasePath;
            ConnectionString = connectionString;
            AdjustmentContext = adjustmentContext;
            Service = service;
            AdminId = adminId;
            ProductId = productId;
            SkuId = skuId;
        }

        public string DatabasePath { get; }
        public string ConnectionString { get; }
        public AppDbContext AdjustmentContext { get; }
        public InventoryService Service { get; }
        public int AdminId { get; }
        public int ProductId { get; }
        public int SkuId { get; }
        public Guid SaleOperationId { get; } = Guid.NewGuid();
        public bool SaleCommitted { get; set; }

        public static async Task<ConcurrentFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"arteinventariado-t03-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 30
            }.ToString();

            await using (var pragmaConnection = new SqliteConnection(connectionString))
            {
                await pragmaConnection.OpenAsync();
                await using var pragma = pragmaConnection.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
                await pragma.ExecuteNonQueryAsync();
            }

            var adjustmentContext = new AppDbContext(BuildOptions(connectionString));
            await adjustmentContext.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador de inventario",
                NombreUsuario = $"inventory-{Guid.NewGuid():N}",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Fixture.Password),
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            var sku = new ProductoSku
            {
                CodigoSku = $"SKU-{Guid.NewGuid():N}"[..20],
                Activo = true,
                EsPrincipal = true
            };
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.EXHIBICION,
                ExistenciaFisica = 2,
                Version = 1
            });
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.BODEGA,
                ExistenciaFisica = 8,
                Version = 1
            });
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.SIN_ASIGNAR,
                ExistenciaFisica = 5,
                Version = 1
            });
            var product = new Producto
            {
                CodigoInterno = $"PRD-{Guid.NewGuid():N}"[..20],
                Nombre = "Producto de ajuste",
                Categoria = category,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 250,
                StockActual = 15,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus = { sku }
            };
            adjustmentContext.AddRange(user, product);
            await adjustmentContext.SaveChangesAsync();
            adjustmentContext.ChangeTracker.Clear();

            var session = new TestSession(new UsuarioSesion(
                user.UsuarioId, user.NombreCompleto, user.NombreUsuario, RolUsuario.ADMIN));
            var installation = new TestInstallationContext(InstallationMode.Principal);
            var service = new InventoryService(
                adjustmentContext,
                new AuditService(adjustmentContext),
                session,
                installation,
                new AdminReauthenticationService(adjustmentContext, session));
            return new ConcurrentFixture(databasePath, connectionString, adjustmentContext, service,
                user.UsuarioId, product.ProductoId, sku.ProductoSkuId);
        }

        public AppDbContext NewContext() => new(BuildOptions(ConnectionString));

        public AdjustInventoryCountRequest Request(UbicacionInventario location, int counted) =>
            new(Guid.NewGuid(), SkuId, location, counted, "Conteo simultaneo", Fixture.Password);

        public async ValueTask DisposeAsync()
        {
            await AdjustmentContext.DisposeAsync();
            foreach (var path in new[] { DatabasePath, $"{DatabasePath}-wal", $"{DatabasePath}-shm" })
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    // El SO puede conservar el sidecar un instante; no afecta al fixture ya cerrado.
                }
            }
        }

        private static DbContextOptions<AppDbContext> BuildOptions(string connectionString) =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options;
    }

    private static bool IsConcurrencyConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            if (current is SqliteException sqlite &&
                sqlite.SqliteErrorCode is 5 or 6 or 517)
            {
                return true;
            }
        }

        return false;
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

    private sealed class TestInstallationContext(InstallationMode mode) : IInstallationContext
    {
        public InstallationMode Mode { get; private set; } = mode;
        public InstallationStatus Status => new(
            StationConfigurationState.Ready,
            Mode,
            true,
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            1,
            "test-station-settings.json",
            null);
        public bool AllowsOperationalWrites => Mode == InstallationMode.Principal;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!AllowsOperationalWrites)
            {
                throw new InstallationWriteDeniedException(
                    InstallationContext.WriteDeniedMessage,
                    operationName);
            }
        }
    }
}
