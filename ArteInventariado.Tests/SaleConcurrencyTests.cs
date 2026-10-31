using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class SaleConcurrencyTests
{
    [Fact]
    public async Task RegisterSaleAsync_TwoIndependentContextsForLastUnit_ConfirmsExactlyOneWithoutDivergence()
    {
        var root = CreateTemporaryRoot();
        var databasePath = Path.Combine(root, "last-unit-concurrency.db");

        try
        {
            var seed = await SeedDatabaseAsync(databasePath);
            var firstFactory = new FileDbContextFactory(CreateOptions(databasePath));
            var secondFactory = new FileDbContextFactory(CreateOptions(databasePath));
            var firstService = CreateSaleService(firstFactory, seed.User);
            var secondService = CreateSaleService(secondFactory, seed.User);
            var startGate = new AsyncStartGate(participants: 2);
            var request = new RegisterSaleRequest(
                [new SaleLineRequest(seed.ProductId, 1, ProductoSkuId: seed.SkuId)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 550)]);

            var attempts = await Task.WhenAll(
                Task.Run(() => CaptureAsync(firstService, request, startGate)),
                Task.Run(() => CaptureAsync(secondService, request, startGate)));

            Assert.Equal(2, startGate.Arrivals);
            var successful = Assert.Single(attempts, attempt => attempt.Result is not null);
            var failed = Assert.Single(attempts, attempt => attempt.Exception is not null);

            await using (var verification = new AppDbContext(CreateOptions(databasePath)))
            {
                var sale = await verification.Ventas.AsNoTracking().SingleAsync();
                var detail = await verification.DetalleVentas.AsNoTracking().SingleAsync();
                var payment = await verification.PagosVenta.AsNoTracking().SingleAsync();
                var legacyMovement = await verification.MovimientosInventario.AsNoTracking().SingleAsync();
                var canonicalMovement = await verification.MovimientosInventarioUbicacion
                    .AsNoTracking()
                    .SingleAsync(movement => movement.TipoOperacion == TipoOperacionInventario.SALIDA_VENTA);
                var productStock = await verification.Productos
                    .AsNoTracking()
                    .Where(product => product.ProductoId == seed.ProductId)
                    .Select(product => product.StockActual)
                    .SingleAsync();
                var balances = await verification.InventarioSaldos
                    .AsNoTracking()
                    .Where(balance => balance.ProductoSkuId == seed.SkuId)
                    .ToListAsync();
                var exhibition = balances.Single(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION);

                Assert.Equal(3, balances.Count);
                Assert.Equal(1, await verification.MovimientosInventarioUbicacion.CountAsync());
                Assert.Equal(successful.Result!.VentaId, sale.VentaId);
                Assert.Equal(sale.VentaId, detail.VentaId);
                Assert.Equal(sale.VentaId, payment.VentaId);
                Assert.Equal(sale.VentaId, legacyMovement.VentaId);
                Assert.Equal(seed.ProductId, detail.ProductoId);
                Assert.Equal(seed.SkuId, detail.ProductoSkuId);
                Assert.Equal(1, detail.Cantidad);
                Assert.Equal(-1, legacyMovement.Cantidad);
                Assert.Equal(1, canonicalMovement.Cantidad);
                Assert.Equal(UbicacionInventario.EXHIBICION, canonicalMovement.UbicacionOrigen);
                Assert.Null(canonicalMovement.UbicacionDestino);
                Assert.Equal(1, productStock);
                Assert.Equal(0, exhibition.ExistenciaFisica);
                Assert.Equal(0, exhibition.Reservado);
                Assert.Equal(2, exhibition.Version);
                Assert.Equal(0, balances.Sum(balance => balance.ExistenciaFisica));
                Assert.Equal(
                    1,
                    await verification.Bitacora.CountAsync(entry => entry.TipoAccion == TipoBitacora.VENTA_REALIZADA));
            }

            Assert.Equal(0, await ReadScalarIntAsync(databasePath, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));

            var controlled = Assert.IsType<InvalidOperationException>(failed.Exception);
            Assert.True(
                controlled.Message.Contains("Stock insuficiente en Exhibición", StringComparison.OrdinalIgnoreCase) ||
                controlled.Message.Contains("inventario cambió", StringComparison.OrdinalIgnoreCase),
                $"La venta perdedora no produjo un error controlado de inventario: {DescribeAttempt(failed)}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static SaleService CreateSaleService(
        IDbContextFactory<AppDbContext> factory,
        UsuarioSesion user)
    {
        var session = new TestCurrentSession(user);
        var audit = new AuditService(factory);
        var permissions = new EmployeePermissionService(factory, session, audit);
        var credit = new CreditService(factory, session, permissions, audit);
        return new SaleService(factory, session, audit, permissions, credit);
    }

    private static async Task<SaleAttempt> CaptureAsync(
        SaleService service,
        RegisterSaleRequest request,
        AsyncStartGate startGate)
    {
        await startGate.SignalAndWaitAsync();

        try
        {
            return new SaleAttempt(await service.RegisterSaleAsync(request), null);
        }
        catch (Exception exception)
        {
            return new SaleAttempt(null, exception);
        }
    }

    private static string DescribeAttempt(SaleAttempt attempt) => attempt.Exception is null
        ? $"OK venta {attempt.Result!.VentaId}"
        : $"{attempt.Exception.GetType().Name}: {attempt.Exception.GetBaseException().Message}";

    private static async Task<SeededSaleData> SeedDatabaseAsync(string databasePath)
    {
        await using var context = new AppDbContext(CreateOptions(databasePath));
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");

        var user = new Usuario
        {
            NombreCompleto = "Administrador de concurrencia",
            NombreUsuario = $"admin-concurrency-{Guid.NewGuid():N}",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        var category = new Categoria
        {
            Nombre = $"Categoria-concurrency-{Guid.NewGuid():N}",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        context.AddRange(user, category);
        await context.SaveChangesAsync();

        var product = new Producto
        {
            CodigoInterno = $"LAST-UNIT-{Guid.NewGuid():N}",
            Nombre = "Ultima unidad",
            CategoriaId = category.CategoriaId,
            PrecioCompraCentavos = 275,
            PrecioVentaCentavos = 550,
            StockActual = 1,
            StockMinimo = 1,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        };
        var cash = new Caja
        {
            FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
            FechaApertura = DateTime.UtcNow,
            MontoInicialCentavos = 0,
            MontoCierreCentavos = 0,
            Estado = EstadoCaja.ABIERTA,
            UsuarioAperturaId = user.UsuarioId
        };
        context.AddRange(product, cash);
        await context.SaveChangesAsync();

        var sku = new ProductoSku
        {
            ProductoId = product.ProductoId,
            CodigoSku = product.CodigoInterno,
            NombreVariante = "Principal",
            Activo = true,
            EsPrincipal = true
        };
        context.ProductoSkus.Add(sku);
        await context.SaveChangesAsync();
        context.InventarioSaldos.AddRange(
            new InventarioSaldo
            {
                ProductoSkuId = sku.ProductoSkuId,
                Ubicacion = UbicacionInventario.EXHIBICION,
                ExistenciaFisica = 1,
                Reservado = 0,
                Version = 1
            },
            new InventarioSaldo
            {
                ProductoSkuId = sku.ProductoSkuId,
                Ubicacion = UbicacionInventario.BODEGA,
                ExistenciaFisica = 0,
                Reservado = 0,
                Version = 1
            },
            new InventarioSaldo
            {
                ProductoSkuId = sku.ProductoSkuId,
                Ubicacion = UbicacionInventario.SIN_ASIGNAR,
                ExistenciaFisica = 0,
                Reservado = 0,
                Version = 1
            });
        await context.SaveChangesAsync();

        return new SeededSaleData(
            product.ProductoId,
            sku.ProductoSkuId,
            new UsuarioSesion(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol));
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string databasePath)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());

        return builder.Options;
    }

    private static async Task<int> ReadScalarIntAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ArteInventariado.SaleConcurrency.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FileDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestCurrentSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;

        public bool EstaAutenticado => UsuarioActual is not null;

        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);

        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;

        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;

        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class AsyncStartGate(int participants)
    {
        private readonly TaskCompletionSource<bool> _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken = default)
        {
            var arrivals = Interlocked.Increment(ref _arrivals);
            if (arrivals == participants)
            {
                _allArrived.TrySetResult(true);
            }

            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed record SeededSaleData(int ProductId, int SkuId, UsuarioSesion User);

    private sealed record SaleAttempt(SaleResult? Result, Exception? Exception);
}
