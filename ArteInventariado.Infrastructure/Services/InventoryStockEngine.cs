using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Unico punto de mutacion del inventario canonico. Producto.StockActual se conserva solo
/// como dato legado de migracion; ninguna operacion nueva lo escribe.
/// </summary>
internal static class InventoryStockEngine
{
    private static readonly UbicacionInventario[] AllLocations =
    [
        UbicacionInventario.EXHIBICION,
        UbicacionInventario.BODEGA,
        UbicacionInventario.SIN_ASIGNAR
    ];

    private static readonly UbicacionInventario[] SalePriority =
    [
        UbicacionInventario.EXHIBICION,
        UbicacionInventario.SIN_ASIGNAR,
        UbicacionInventario.BODEGA
    ];

    internal static IReadOnlyList<InventarioSaldo> CreateInitialBalances(
        ProductoSku sku,
        int initialStock,
        UbicacionInventario initialLocation = UbicacionInventario.SIN_ASIGNAR)
    {
        if (initialStock < 0)
        {
            throw new InvalidOperationException("El stock inicial no puede ser negativo.");
        }

        if (!Enum.IsDefined(initialLocation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialLocation),
                "La ubicacion inicial no es valida.");
        }

        return AllLocations.Select(location => new InventarioSaldo
        {
            ProductoSku = sku,
            Ubicacion = location,
            ExistenciaFisica = location == initialLocation ? initialStock : 0,
            Reservado = 0,
            Version = 1
        }).ToList();
    }

    internal static async Task<ProductoSku> GetOrCreatePrimarySkuAsync(
        AppDbContext context,
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await context.Productos
            .Include(item => item.Skus)
                .ThenInclude(sku => sku.SaldosInventario)
            .SingleOrDefaultAsync(item => item.ProductoId == productId, cancellationToken)
            ?? throw new InvalidOperationException("El producto no existe.");

        var primary = product.Skus.FirstOrDefault(sku => sku.EsPrincipal)
            ?? product.Skus.OrderBy(sku => sku.ProductoSkuId).FirstOrDefault();

        if (primary is null)
        {
            primary = new ProductoSku
            {
                Producto = product,
                CodigoSku = product.CodigoInterno,
                Activo = product.Estado == EstadoProducto.ACTIVO,
                EsPrincipal = true
            };
            context.ProductoSkus.Add(primary);
        }
        else if (!product.Skus.Any(sku => sku.EsPrincipal))
        {
            primary.EsPrincipal = true;
        }

        var hasCanonicalInventory = product.Skus.SelectMany(item => item.SaldosInventario).Any();
        EnsureBalanceRows(primary, hasCanonicalInventory ? 0 : product.StockActual);
        return primary;
    }

    internal static async Task<ProductoSku> GetSkuAsync(
        AppDbContext context,
        int skuId,
        CancellationToken cancellationToken)
    {
        var sku = await context.ProductoSkus
            .Include(item => item.Producto)
                .ThenInclude(product => product.Skus)
                    .ThenInclude(item => item.SaldosInventario)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == skuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");

        var hasCanonicalInventory = sku.Producto.Skus.SelectMany(item => item.SaldosInventario).Any();
        EnsureBalanceRows(sku, !hasCanonicalInventory && sku.EsPrincipal ? sku.Producto.StockActual : 0);
        return sku;
    }

    internal static void EnsureBalanceRows(ProductoSku sku, int legacyStock)
    {
        var hadAnyBalance = sku.SaldosInventario.Count > 0;
        foreach (var location in AllLocations)
        {
            if (sku.SaldosInventario.Any(balance => balance.Ubicacion == location))
            {
                continue;
            }

            sku.SaldosInventario.Add(new InventarioSaldo
            {
                ProductoSku = sku,
                Ubicacion = location,
                ExistenciaFisica = !hadAnyBalance && location == UbicacionInventario.SIN_ASIGNAR
                    ? legacyStock
                    : 0,
                Reservado = 0,
                Version = 1
            });
        }
    }

    internal static void Transfer(
        AppDbContext context,
        ProductoSku sku,
        Guid operationId,
        UbicacionInventario origin,
        UbicacionInventario destination,
        int quantity,
        int userId,
        string reason,
        string? note,
        DateTime now)
    {
        ValidateOperation(operationId, quantity, reason);
        if (origin == destination)
        {
            throw new InvalidOperationException("La ubicacion de origen y destino deben ser diferentes.");
        }
        if (!IsAllowedTransfer(origin, destination))
        {
            throw new InvalidOperationException("La transferencia solicitada no pertenece a las rutas operativas habilitadas.");
        }

        var originBalance = RequiredBalance(sku, origin);
        var destinationBalance = RequiredBalance(sku, destination);
        if (originBalance.Disponible < quantity)
        {
            throw new InvalidOperationException("La ubicacion de origen no tiene disponibilidad suficiente.");
        }

        originBalance.ExistenciaFisica -= quantity;
        destinationBalance.ExistenciaFisica += quantity;
        Touch(originBalance);
        Touch(destinationBalance);
        AddMovement(context, sku, operationId, TipoOperacionInventario.TRANSFERENCIA,
            origin, destination, quantity, userId, reason, note, now);
        RefreshProductProjection(sku.Producto);
    }

    internal static void AddPhysical(
        AppDbContext context,
        ProductoSku sku,
        UbicacionInventario destination,
        int quantity,
        int userId,
        TipoOperacionInventario type,
        string reason,
        DateTime now,
        Guid? operationId = null,
        string? note = null)
    {
        ValidateQuantityAndReason(quantity, reason);
        var balance = RequiredBalance(sku, destination);
        balance.ExistenciaFisica += quantity;
        Touch(balance);
        AddMovement(context, sku, operationId ?? Guid.NewGuid(), type,
            null, destination, quantity, userId, reason, note, now);
        RefreshProductProjection(sku.Producto);
    }

    internal static void RemovePhysical(
        AppDbContext context,
        ProductoSku sku,
        int quantity,
        int userId,
        TipoOperacionInventario type,
        string reason,
        DateTime now,
        UbicacionInventario? origin = null,
        Guid? operationId = null,
        string? note = null)
    {
        ValidateQuantityAndReason(quantity, reason);
        var remaining = quantity;
        var locations = origin.HasValue ? [origin.Value] : SalePriority;
        var available = locations.Sum(location => RequiredBalance(sku, location).Disponible);
        if (available < quantity)
        {
            throw new InvalidOperationException("No existe inventario disponible suficiente; el stock reservado no puede consumirse.");
        }

        foreach (var location in locations)
        {
            var balance = RequiredBalance(sku, location);
            var taken = Math.Min(balance.Disponible, remaining);
            if (taken == 0)
            {
                continue;
            }

            balance.ExistenciaFisica -= taken;
            Touch(balance);
            AddMovement(context, sku,
                remaining == quantity ? operationId ?? Guid.NewGuid() : Guid.NewGuid(),
                type, location, null, taken, userId, reason, note, now);
            remaining -= taken;
            if (remaining == 0)
            {
                break;
            }
        }

        RefreshProductProjection(sku.Producto);
    }

    internal static IReadOnlyList<(InventarioSaldo Balance, int Quantity)> Reserve(
        AppDbContext context,
        ProductoSku sku,
        int quantity,
        int userId,
        string reason,
        DateTime now)
    {
        ValidateQuantityAndReason(quantity, reason);
        var remaining = quantity;
        if (SalePriority.Sum(location => RequiredBalance(sku, location).Disponible) < quantity)
        {
            throw new InvalidOperationException("Stock disponible insuficiente para reservar el pedido.");
        }

        var allocations = new List<(InventarioSaldo, int)>();
        foreach (var location in SalePriority)
        {
            var balance = RequiredBalance(sku, location);
            var reserved = Math.Min(balance.Disponible, remaining);
            if (reserved == 0)
            {
                continue;
            }

            balance.Reservado += reserved;
            Touch(balance);
            allocations.Add((balance, reserved));
            AddMovement(context, sku, Guid.NewGuid(), TipoOperacionInventario.RESERVA,
                location, location, reserved, userId, reason, null, now);
            remaining -= reserved;
            if (remaining == 0)
            {
                break;
            }
        }

        return allocations;
    }

    internal static void ReleaseReservation(
        AppDbContext context,
        ProductoSku sku,
        UbicacionInventario location,
        int quantity,
        int userId,
        string reason,
        DateTime now)
    {
        var balance = RequiredBalance(sku, location);
        if (quantity <= 0 || balance.Reservado < quantity)
        {
            throw new InvalidOperationException("La reserva que se intenta liberar no es valida.");
        }

        balance.Reservado -= quantity;
        Touch(balance);
        AddMovement(context, sku, Guid.NewGuid(), TipoOperacionInventario.LIBERACION_RESERVA,
            location, location, quantity, userId, reason, null, now);
    }

    internal static void ConsumeReservation(
        AppDbContext context,
        ProductoSku sku,
        UbicacionInventario location,
        int quantity,
        int userId,
        string reason,
        DateTime now)
    {
        var balance = RequiredBalance(sku, location);
        if (quantity <= 0 || balance.Reservado < quantity || balance.ExistenciaFisica < quantity)
        {
            throw new InvalidOperationException("La reserva que se intenta consumir no es valida.");
        }

        balance.Reservado -= quantity;
        balance.ExistenciaFisica -= quantity;
        Touch(balance);
        AddMovement(context, sku, Guid.NewGuid(), TipoOperacionInventario.CONSUMO_RESERVA,
            location, null, quantity, userId, reason, null, now);
        RefreshProductProjection(sku.Producto);
    }

    internal static void SetAbsolutePhysical(
        AppDbContext context,
        ProductoSku sku,
        UbicacionInventario location,
        int newPhysical,
        int userId,
        string reason,
        DateTime now,
        Guid? operationId = null,
        string? note = null)
    {
        var balance = RequiredBalance(sku, location);
        if (newPhysical < balance.Reservado)
        {
            throw new InvalidOperationException("El nuevo stock no puede ser menor que la cantidad reservada.");
        }

        var difference = newPhysical - balance.ExistenciaFisica;
        if (difference == 0)
        {
            throw new InvalidOperationException("El stock nuevo es igual al stock actual.");
        }

        balance.ExistenciaFisica = newPhysical;
        Touch(balance);
        AddMovement(context, sku, operationId ?? Guid.NewGuid(), TipoOperacionInventario.AJUSTE,
            difference < 0 ? location : null,
            difference > 0 ? location : null,
            Math.Abs(difference), userId, reason, note, now);
        RefreshProductProjection(sku.Producto);
    }

    internal static void RefreshProductProjection(Producto product)
    {
        product.FechaModificacion = DateTime.UtcNow;
    }

    internal static int GetPhysicalTotal(Producto product) => product.Skus
        .SelectMany(sku => sku.SaldosInventario)
        .Sum(balance => balance.ExistenciaFisica);

    internal static int GetPhysicalTotal(ProductoSku sku) => sku.SaldosInventario
        .Sum(balance => balance.ExistenciaFisica);

    internal static int GetAvailableTotal(ProductoSku sku) => sku.SaldosInventario
        .Sum(balance => balance.Disponible);

    private static InventarioSaldo RequiredBalance(ProductoSku sku, UbicacionInventario location) =>
        sku.SaldosInventario.Single(balance => balance.Ubicacion == location);

    private static void AddMovement(
        AppDbContext context,
        ProductoSku sku,
        Guid operationId,
        TipoOperacionInventario type,
        UbicacionInventario? origin,
        UbicacionInventario? destination,
        int quantity,
        int userId,
        string reason,
        string? note,
        DateTime now)
    {
        if (note?.Trim().Length > 1000)
        {
            throw new ArgumentException("La nota no puede superar 1000 caracteres.", nameof(note));
        }

        context.MovimientosInventarioUbicacion.Add(new MovimientoInventarioUbicacion
        {
            OperacionId = operationId,
            ProductoSku = sku,
            TipoOperacion = type,
            UbicacionOrigen = origin,
            UbicacionDestino = destination,
            Cantidad = quantity,
            UsuarioId = userId,
            Motivo = reason.Trim(),
            Nota = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Fecha = now
        });
    }

    private static void ValidateOperation(Guid operationId, int quantity, string reason)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("La operacion debe tener un identificador idempotente.", nameof(operationId));
        }

        ValidateQuantityAndReason(quantity, reason);
    }

    private static void ValidateQuantityAndReason(int quantity, string reason)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "La cantidad debe ser mayor que cero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("El motivo es obligatorio.", nameof(reason));
        }
        if (reason.Trim().Length > 500)
        {
            throw new ArgumentException("El motivo no puede superar 500 caracteres.", nameof(reason));
        }
    }

    private static void Touch(InventarioSaldo balance) => balance.Version++;

    private static bool IsAllowedTransfer(UbicacionInventario origin, UbicacionInventario destination) =>
        (origin, destination) is
            (UbicacionInventario.BODEGA, UbicacionInventario.EXHIBICION) or
            (UbicacionInventario.EXHIBICION, UbicacionInventario.BODEGA) or
            (UbicacionInventario.SIN_ASIGNAR, UbicacionInventario.EXHIBICION) or
            (UbicacionInventario.SIN_ASIGNAR, UbicacionInventario.BODEGA);
}
