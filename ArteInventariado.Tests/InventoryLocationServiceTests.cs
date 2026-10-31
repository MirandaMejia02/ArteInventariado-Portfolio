using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class InventoryLocationServiceTests
{
    [Fact]
    public async Task CanonicalBalance_DefinesPhysicalReservedAndAvailableBySkuAndLocation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var balances = await fixture.Service.GetBalancesByProductAsync(fixture.ProductId);

        Assert.Equal(3, balances.Count);
        Assert.Equal(15, balances.Sum(item => item.ExistenciaFisica));
        Assert.Equal(15, balances.Sum(item => item.Disponible));
        Assert.All(balances, item => Assert.Equal(item.ExistenciaFisica - item.Reservado, item.Disponible));
    }

    [Fact]
    public async Task NewVariantSku_StartsAtZeroAndDoesNotDuplicateProductStock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var variant = new ProductoSku
        {
            ProductoId = fixture.ProductId,
            CodigoSku = $"VAR-{Guid.NewGuid():N}"[..20],
            NombreVariante = "Variante adicional",
            Activo = true,
            EsPrincipal = false
        };
        fixture.Context.ProductoSkus.Add(variant);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var loaded = await InventoryStockEngine.GetSkuAsync(
            fixture.Context, variant.ProductoSkuId, CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(3, loaded.SaldosInventario.Count);
        Assert.Equal(0, loaded.SaldosInventario.Sum(item => item.ExistenciaFisica));
        Assert.Equal(15, (await fixture.Service.ReconcileAsync()).Single().TotalCanonico);
    }

    [Fact]
    public async Task Transfer_ConservesTotal_AndIsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operationId = Guid.NewGuid();
        var request = new TransferInventoryRequest(operationId, fixture.SkuId,
            UbicacionInventario.BODEGA, UbicacionInventario.EXHIBICION, 3, "Reposicion de vitrina");

        var first = await fixture.Service.TransferAsync(request);
        var second = await fixture.Service.TransferAsync(request);

        Assert.Equal(15, first.Sum(item => item.ExistenciaFisica));
        Assert.Equal(15, second.Sum(item => item.ExistenciaFisica));
        Assert.Equal(5, second.Single(item => item.Ubicacion == UbicacionInventario.EXHIBICION).ExistenciaFisica);
        Assert.Equal(5, second.Single(item => item.Ubicacion == UbicacionInventario.BODEGA).ExistenciaFisica);
        Assert.Equal(1, await fixture.Context.MovimientosInventarioUbicacion.CountAsync(
            item => item.OperacionId == operationId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TransferAsync(
            request with { Cantidad = 2 }));
    }

    [Fact]
    public async Task RcScenario_TransfersAllWarehouseStockToExhibitionAndPreservesPhysicalTotal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetBalancesAsync(exhibition: 1, warehouse: 21, unassigned: 30, reserved: 0);

        var result = await fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA,
            UbicacionInventario.EXHIBICION, 21, "Reposición completa de exhibición"));

        Assert.Equal(22, result.Single(item => item.Ubicacion == UbicacionInventario.EXHIBICION).ExistenciaFisica);
        Assert.Equal(0, result.Single(item => item.Ubicacion == UbicacionInventario.BODEGA).ExistenciaFisica);
        Assert.Equal(30, result.Single(item => item.Ubicacion == UbicacionInventario.SIN_ASIGNAR).ExistenciaFisica);
        Assert.Equal(52, result.Sum(item => item.ExistenciaFisica));
    }

    [Fact]
    public async Task RcScenario_ClassifiesOnlyRequestedUnassignedStockAndPreservesPhysicalTotal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetBalancesAsync(exhibition: 1, warehouse: 21, unassigned: 30, reserved: 0);

        var result = await fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.SIN_ASIGNAR,
            UbicacionInventario.EXHIBICION, 7, "Clasificación manual"));

        Assert.Equal(8, result.Single(item => item.Ubicacion == UbicacionInventario.EXHIBICION).ExistenciaFisica);
        Assert.Equal(21, result.Single(item => item.Ubicacion == UbicacionInventario.BODEGA).ExistenciaFisica);
        Assert.Equal(23, result.Single(item => item.Ubicacion == UbicacionInventario.SIN_ASIGNAR).ExistenciaFisica);
        Assert.Equal(52, result.Sum(item => item.ExistenciaFisica));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Transfer_RejectsNonPositiveQuantity(int quantity)
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA,
            UbicacionInventario.EXHIBICION, quantity, "Cantidad inválida")));
    }

    [Fact]
    public async Task Transfer_RejectsSameLocationAndUnassignedWithoutStock()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA,
            UbicacionInventario.BODEGA, 1, "Mismo destino")));

        await fixture.SetBalancesAsync(exhibition: 1, warehouse: 21, unassigned: 0, reserved: 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.SIN_ASIGNAR,
            UbicacionInventario.EXHIBICION, 1, "Sin stock pendiente")));
    }

    [Fact]
    public async Task InsufficientTransfer_RollsBackWithoutChangingEitherLocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Service.GetBalancesByProductAsync(fixture.ProductId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.EXHIBICION,
            UbicacionInventario.BODEGA, 99, "Transferencia imposible")));

        fixture.Context.ChangeTracker.Clear();
        var after = await fixture.Service.GetBalancesByProductAsync(fixture.ProductId);
        Assert.Equal(before.Select(item => item.ExistenciaFisica), after.Select(item => item.ExistenciaFisica));
        Assert.Empty(await fixture.Context.MovimientosInventarioUbicacion.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA,
            UbicacionInventario.SIN_ASIGNAR, 1, "Ruta no habilitada")));
    }

    [Fact]
    public async Task Reservation_DoesNotMovePhysical_AndCannotExceedIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sku = await InventoryStockEngine.GetSkuAsync(fixture.Context, fixture.SkuId, CancellationToken.None);
        var totalBefore = sku.SaldosInventario.Sum(item => item.ExistenciaFisica);

        var allocations = InventoryStockEngine.Reserve(
            fixture.Context, sku, 4, fixture.AdminId, "Pedido confirmado", DateTime.UtcNow);
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(4, allocations.Sum(item => item.Quantity));
        Assert.Equal(totalBefore, sku.SaldosInventario.Sum(item => item.ExistenciaFisica));
        Assert.Equal(4, sku.SaldosInventario.Sum(item => item.Reservado));
        Assert.Equal(totalBefore - 4, sku.SaldosInventario.Sum(item => item.Disponible));
        Assert.Throws<InvalidOperationException>(() => InventoryStockEngine.Reserve(
            fixture.Context, sku, totalBefore, fixture.AdminId, "Exceso", DateTime.UtcNow));
    }

    [Fact]
    public async Task Replenishment_DistinguishesRequiredOutOfStockAndNoAvailability()
    {
        await using var fixture = await Fixture.CreateAsync();
        var required = Assert.Single(await fixture.Service.GetReplenishmentAsync());
        Assert.Equal(EstadoDisponibilidadInventario.REQUIERE_REPOSICION, required.Estado);

        await fixture.SetBalancesAsync(0, 0, 0, reserved: 0);
        var outOfStock = Assert.Single(await fixture.Service.GetReplenishmentAsync());
        Assert.Equal(EstadoDisponibilidadInventario.SIN_EXISTENCIAS, outOfStock.Estado);

        await fixture.SetBalancesAsync(2, 8, 5, reserved: 15);
        var unavailable = Assert.Single(await fixture.Service.GetReplenishmentAsync());
        Assert.Equal(EstadoDisponibilidadInventario.SIN_DISPONIBILIDAD, unavailable.Estado);
    }

    [Fact]
    public async Task PostponeAndDismiss_DoNotChangeStock_AndDismissRequiresReason()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = (await fixture.Service.GetBalancesByProductAsync(fixture.ProductId))
            .Sum(item => item.ExistenciaFisica);

        await fixture.Service.PostponeReplenishmentAsync(new(
            fixture.SkuId, DateTime.UtcNow.AddDays(2)));
        Assert.Equal(EstadoAlertaReposicion.POSPUESTA,
            Assert.Single(await fixture.Service.GetReplenishmentAsync()).Decision);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.DismissReplenishmentAsync(
            new(fixture.SkuId, " ")));
        await fixture.Service.DismissReplenishmentAsync(new(fixture.SkuId, "Sin espacio temporal"));

        Assert.Equal(before, (await fixture.Service.GetBalancesByProductAsync(fixture.ProductId))
            .Sum(item => item.ExistenciaFisica));
        var dismissed = Assert.Single(await fixture.Service.GetReplenishmentAsync());
        Assert.Equal(EstadoAlertaReposicion.DESCARTADA, dismissed.Decision);
        Assert.Equal("Sin espacio temporal", dismissed.MotivoDescarte);
    }

    [Fact]
    public async Task Waste_ReducesPhysical_ButCannotConsumeReservedUnits()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sku = await InventoryStockEngine.GetSkuAsync(fixture.Context, fixture.SkuId, CancellationToken.None);
        InventoryStockEngine.Reserve(fixture.Context, sku, 2, fixture.AdminId, "Pedido", DateTime.UtcNow);
        await fixture.Context.SaveChangesAsync();

        var afterWaste = await fixture.Service.RegisterWasteAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA, 2,
            "Producto quebrado", "Detectado en conteo"));
        Assert.Equal(13, afterWaste.Sum(item => item.ExistenciaFisica));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegisterWasteAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.EXHIBICION, 1,
            "Intento sobre reserva")));
    }

    [Fact]
    public async Task RcScenario_ValidWarehouseWasteReducesPhysicalAndBlankReasonIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetBalancesAsync(exhibition: 1, warehouse: 21, unassigned: 30, reserved: 0);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.RegisterWasteAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA, 3, " ")));

        var result = await fixture.Service.RegisterWasteAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA, 3, "Producto dañado"));

        Assert.Equal(18, result.Single(item => item.Ubicacion == UbicacionInventario.BODEGA).ExistenciaFisica);
        Assert.Equal(49, result.Sum(item => item.ExistenciaFisica));
    }

    [Fact]
    public async Task StagnantStock_IsOnlyFlaggedForReview()
    {
        await using var fixture = await Fixture.CreateAsync(productCreated: DateTime.UtcNow.AddDays(-120));

        var stagnant = Assert.Single(await fixture.Service.GetStagnantInventoryAsync(new(90)));

        Assert.True(stagnant.RequiereRevision);
        Assert.True(stagnant.DiasSinMovimiento >= 119);
        Assert.Equal(15, stagnant.ExistenciaFisica);
    }

    [Fact]
    public async Task DatabaseConstraint_RejectsReservedGreaterThanPhysical()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.ChangeTracker.Clear();
        var balance = await fixture.Context.InventarioSaldos
            .FirstAsync(item => item.ProductoSkuId == fixture.SkuId);
        balance.Reservado = balance.ExistenciaFisica + 1;

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task BalanceVersion_DetectsConcurrentWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.ChangeTracker.Clear();
        await using var first = fixture.NewContext();
        await using var second = fixture.NewContext();
        var firstBalance = await first.InventarioSaldos
            .SingleAsync(item => item.ProductoSkuId == fixture.SkuId && item.Ubicacion == UbicacionInventario.BODEGA);
        var secondBalance = await second.InventarioSaldos
            .SingleAsync(item => item.ProductoSkuId == fixture.SkuId && item.Ubicacion == UbicacionInventario.BODEGA);

        firstBalance.ExistenciaFisica++;
        firstBalance.Version++;
        await first.SaveChangesAsync();
        secondBalance.ExistenciaFisica++;
        secondBalance.Version++;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task EmployeeWithoutSensitivePermission_CanReadButCannotWrite()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);

        Assert.Equal(3, (await fixture.Service.GetBalancesByProductAsync(fixture.ProductId)).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TransferAsync(new(
            Guid.NewGuid(), fixture.SkuId, UbicacionInventario.BODEGA,
            UbicacionInventario.EXHIBICION, 1, "No autorizado")));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, AppDbContext context, InventoryService service,
            int adminId, int productId, int skuId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            AdminId = adminId;
            ProductId = productId;
            SkuId = skuId;
        }

        public AppDbContext Context { get; }
        public InventoryService Service { get; }
        public int AdminId { get; }
        public int ProductId { get; }
        public int SkuId { get; }

        public AppDbContext NewContext() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        public static async Task<Fixture> CreateAsync(
            RolUsuario role = RolUsuario.ADMIN,
            DateTime? productCreated = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var user = new Usuario
            {
                NombreCompleto = "Operador inventario",
                NombreUsuario = $"inventory-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = role,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var category = new Categoria
            {
                Nombre = $"Inventario-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.AddRange(user, category);
            await context.SaveChangesAsync();

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
                Modo = ModoProducto.SIMPLE,
                Nombre = "Producto canonico",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 250,
                StockActual = 15,
                StockMinimo = 3,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = productCreated ?? DateTime.UtcNow,
                Skus = { sku }
            };
            context.Productos.Add(product);
            await context.SaveChangesAsync();

            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(user.UsuarioId, user.NombreCompleto,
                user.NombreUsuario, user.Rol));
            var service = new InventoryService(context, new AuditService(context), session);
            return new Fixture(connection, context, service, user.UsuarioId,
                product.ProductoId, sku.ProductoSkuId);
        }

        public async Task SetBalancesAsync(int exhibition, int warehouse, int unassigned, int reserved)
        {
            var balances = await Context.InventarioSaldos
                .Where(item => item.ProductoSkuId == SkuId)
                .ToListAsync();
            foreach (var balance in balances)
            {
                balance.ExistenciaFisica = balance.Ubicacion switch
                {
                    UbicacionInventario.EXHIBICION => exhibition,
                    UbicacionInventario.BODEGA => warehouse,
                    _ => unassigned
                };
                balance.Reservado = 0;
                balance.Version++;
            }
            var remaining = reserved;
            foreach (var balance in balances.OrderByDescending(item => item.ExistenciaFisica))
            {
                var allocated = Math.Min(remaining, balance.ExistenciaFisica);
                balance.Reservado = allocated;
                remaining -= allocated;
            }
            Assert.Equal(0, remaining);
            var product = await Context.Productos.SingleAsync(item => item.ProductoId == ProductId);
            product.StockActual = exhibition + warehouse + unassigned;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
