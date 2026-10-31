using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class PurchaseReceptionService : IPurchaseReceptionService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly IInstallationContext? _installationContext;

    [ActivatorUtilitiesConstructor]
    public PurchaseReceptionService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService,
        IInstallationContext? installationContext = null)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _installationContext = installationContext;
    }

    internal PurchaseReceptionService(
        AppDbContext context,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService,
        IInstallationContext? installationContext = null)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _installationContext = installationContext;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la recepcion de compra.");

    public async Task<PurchaseReceptionSummary> RegisterReceptionAsync(
        RegisterPurchaseReceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        _installationContext?.EnsureOperationalWriteAllowed("recepcion.compra");
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRA_RECEPCIONAR, cancellationToken);
        ValidateRequestShape(request);

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var existing = await Context.RecepcionesCompra
            .Include(r => r.Detalles)
            .FirstOrDefaultAsync(r => r.OperacionId == request.OperacionId, cancellationToken);
        if (existing is not null)
        {
            if (IsSameReceptionRequest(existing, request))
            {
                await transaction.CommitAsync(cancellationToken);
                return await BuildReceptionSummaryAsync(existing.RecepcionCompraId, cancellationToken);
            }

            throw new InvalidOperationException("El identificador de operacion ya fue usado con datos distintos.");
        }

        var usuarioId = GetCurrentUserId();
        var compra = await Context.Compras
            .Include(c => c.Proveedor)
            .Include(c => c.Detalles)
                .ThenInclude(d => d.Producto)
                    .ThenInclude(product => product.Skus)
                        .ThenInclude(sku => sku.SaldosInventario)
            .FirstOrDefaultAsync(c => c.CompraId == request.CompraId, cancellationToken)
            ?? throw new InvalidOperationException("La compra no existe.");

        if (compra.Proveedor.Estado != EstadoProveedor.ACTIVO)
        {
            throw new InvalidOperationException("No se puede recibir una compra de proveedor inactivo.");
        }

        if (compra.EstadoRecepcion == EstadoRecepcionCompra.RECIBIDA)
        {
            throw new InvalidOperationException("La compra ya fue recibida completamente.");
        }

        var requestLines = request.Detalles
            .Where(d => d.CantidadRecibida > 0)
            .ToList();
        if (requestLines.Count == 0)
        {
            throw new ArgumentException("Debe recibir al menos una cantidad.");
        }

        var lineMap = compra.Detalles.ToDictionary(d => d.DetalleCompraId);
        var duplicateLine = requestLines
            .GroupBy(d => d.DetalleCompraId)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateLine is not null)
        {
            throw new InvalidOperationException("No se puede repetir el mismo detalle dentro de una recepcion.");
        }

        foreach (var requestLine in requestLines)
        {
            if (!lineMap.TryGetValue(requestLine.DetalleCompraId, out var detail))
            {
                throw new InvalidOperationException("La linea a recibir no pertenece a la compra.");
            }

            var pending = detail.CantidadComprada - detail.CantidadRecibida;
            if (requestLine.CantidadRecibida > pending)
            {
                throw new InvalidOperationException($"No se puede recibir mas de lo pendiente para '{detail.NombreProductoSnapshot}'.");
            }

            ValidateReceptionLocation(requestLine.UbicacionDestino);
        }

        var orderedPurchaseLines = compra.Detalles.OrderBy(detail => detail.DetalleCompraId).ToList();
        var generalDiscountAllocations = ProportionalAmountAllocator.Allocate(
            compra.DescuentoCentavos,
            orderedPurchaseLines.Select(detail => detail.SubtotalCentavos).ToList());
        var totalDiscountsByLine = orderedPurchaseLines
            .Select((detail, index) => new
            {
                detail.DetalleCompraId,
                TotalDiscountCentavos = checked(
                    detail.DescuentoLineaCentavos + generalDiscountAllocations[index])
            })
            .ToDictionary(item => item.DetalleCompraId, item => item.TotalDiscountCentavos);

        var now = DateTime.UtcNow;
        var receptionDate = request.FechaRecepcion ?? now;
        var reception = new RecepcionCompra
        {
            CodigoRecepcion = $"TMP-{Guid.NewGuid():N}"[..36],
            OperacionId = request.OperacionId,
            CompraId = compra.CompraId,
            UsuarioRecepcionId = usuarioId,
            FechaRecepcion = receptionDate,
            Observaciones = NormalizeOptional(request.Observaciones)
        };
        Context.RecepcionesCompra.Add(reception);
        await Context.SaveChangesAsync(cancellationToken);
        reception.CodigoRecepcion = GenerateReceptionCode(reception.RecepcionCompraId);

        foreach (var requestLine in requestLines)
        {
            if (!lineMap.TryGetValue(requestLine.DetalleCompraId, out var detail))
            {
                throw new InvalidOperationException("La linea a recibir no pertenece a la compra.");
            }

            var pending = detail.CantidadComprada - detail.CantidadRecibida;
            if (requestLine.CantidadRecibida > pending)
            {
                throw new InvalidOperationException($"No se puede recibir mas de lo pendiente para '{detail.NombreProductoSnapshot}'.");
            }

            var product = detail.Producto;
            var receivedUnitsBefore = detail.CantidadRecibida;
            var assignedDiscount = AllocateDiscountToUnitRange(
                totalDiscountsByLine[detail.DetalleCompraId],
                detail.CantidadComprada,
                receivedUnitsBefore,
                requestLine.CantidadRecibida);
            var receivedGross = checked(detail.CostoUnitarioCentavos * requestLine.CantidadRecibida);
            var receivedNet = checked(receivedGross - assignedDiscount);
            var netUnitCost = (long)decimal.Round(
                receivedNet / (decimal)requestLine.CantidadRecibida,
                0,
                MidpointRounding.AwayFromZero);
            var sku = detail.ProductoSkuId.HasValue
                ? await InventoryStockEngine.GetSkuAsync(Context, detail.ProductoSkuId.Value, cancellationToken)
                : await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                    Context, product.ProductoId, cancellationToken);
            var stockAnterior = InventoryStockEngine.GetPhysicalTotal(sku.Producto);
            var stockNuevo = checked(stockAnterior + requestLine.CantidadRecibida);
            var skuStockAnterior = InventoryStockEngine.GetPhysicalTotal(sku);
            var costoAnterior = sku.ResolverCostoCentavos();
            var costoNuevo = CalculateWeightedAverageCost(
                skuStockAnterior,
                costoAnterior,
                requestLine.CantidadRecibida,
                detail.CostoUnitarioCentavos);

            InventoryStockEngine.AddPhysical(Context, sku, requestLine.UbicacionDestino,
                requestLine.CantidadRecibida, usuarioId, TipoOperacionInventario.ENTRADA_COMPRA,
                $"Recepcion de compra {compra.CodigoCompra}", receptionDate,
                OperationalOperationIds.Derive(request.OperacionId, "purchase-reception", detail.DetalleCompraId));
            if (product.Modo == ModoProducto.POR_VARIANTES)
                sku.CostoOverrideCentavos = costoNuevo;
            else
                product.PrecioCompraCentavos = costoNuevo;
            detail.CantidadRecibida += requestLine.CantidadRecibida;

            var movement = new MovimientoInventario
            {
                ProductoId = product.ProductoId,
                UsuarioId = usuarioId,
                TipoMovimiento = TipoMovimientoInventario.ENTRADA_COMPRA,
                Cantidad = requestLine.CantidadRecibida,
                StockAnterior = stockAnterior,
                StockNuevo = stockNuevo,
                Motivo = $"Recepcion de compra {compra.CodigoCompra}",
                FechaMovimiento = receptionDate
            };

            var receptionDetail = new RecepcionCompraDetalle
            {
                RecepcionCompraId = reception.RecepcionCompraId,
                DetalleCompraId = detail.DetalleCompraId,
                ProductoId = product.ProductoId,
                ProductoSku = sku,
                CodigoSkuSnapshot = detail.CodigoSkuSnapshot ?? sku.CodigoSku,
                NombreVarianteSnapshot = detail.NombreVarianteSnapshot ?? sku.NombreVariante,
                UbicacionDestino = requestLine.UbicacionDestino,
                CantidadRecibida = requestLine.CantidadRecibida,
                StockAnterior = stockAnterior,
                StockNuevo = stockNuevo,
                CostoUnitarioCentavos = detail.CostoUnitarioCentavos,
                CostoNetoUnitarioCentavos = netUnitCost,
                DescuentoAsignadoCentavos = assignedDiscount,
                CostoPromedioAnteriorCentavos = costoAnterior,
                CostoPromedioNuevoCentavos = costoNuevo,
                MovimientoInventario = movement
            };

            var costHistory = new HistorialCostoProducto
            {
                ProductoId = product.ProductoId,
                ProveedorId = compra.ProveedorId,
                CompraId = compra.CompraId,
                DetalleCompraId = detail.DetalleCompraId,
                RecepcionCompraId = reception.RecepcionCompraId,
                CostoAnteriorCentavos = costoAnterior,
                CostoRecibidoCentavos = detail.CostoUnitarioCentavos,
                CostoPromedioNuevoCentavos = costoNuevo,
                StockAnterior = stockAnterior,
                CantidadRecibida = requestLine.CantidadRecibida,
                StockNuevo = stockNuevo,
                Fecha = receptionDate,
                UsuarioId = usuarioId,
                Observaciones = $"Recepcion {reception.CodigoRecepcion} de compra {compra.CodigoCompra}"
            };

            Context.MovimientosInventario.Add(movement);
            Context.RecepcionCompraDetalles.Add(receptionDetail);
            Context.HistorialCostosProducto.Add(costHistory);
            await UpdateProductSupplierAsync(
                compra.ProveedorId,
                product.ProductoId,
                detail.CostoUnitarioCentavos,
                receptionDate,
                now,
                cancellationToken);
        }

        compra.EstadoRecepcion = CalculateReceptionState(compra.Detalles);
        compra.FechaActualizacion = now;
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.RECEPCION_COMPRA_REGISTRADA,
                "recepciones_compra",
                reception.RecepcionCompraId,
                $"Recepcion {reception.CodigoRecepcion} registrada para compra {compra.CodigoCompra}.",
                DatosDespues: BuildReceptionAuditSnapshot(reception, compra)),
            cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.COSTO_PROMEDIO_ACTUALIZADO,
                "compras",
                compra.CompraId,
                $"Costo promedio actualizado por recepcion de compra {compra.CodigoCompra}."),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await BuildReceptionSummaryAsync(reception.RecepcionCompraId, cancellationToken);
    }

    private async Task UpdateProductSupplierAsync(
        int proveedorId,
        int productoId,
        long ultimoCostoCentavos,
        DateTime fechaCompra,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var association = await Context.ProductoProveedores
            .FirstOrDefaultAsync(pp => pp.ProveedorId == proveedorId && pp.ProductoId == productoId, cancellationToken);
        var hasOtherMain = await Context.ProductoProveedores
            .AnyAsync(pp =>
                pp.ProductoId == productoId &&
                pp.Activo &&
                pp.EsProveedorPrincipal &&
                (association == null || pp.ProductoProveedorId != association.ProductoProveedorId),
                cancellationToken);

        if (association is null)
        {
            association = new ProductoProveedor
            {
                ProveedorId = proveedorId,
                ProductoId = productoId,
                FechaCreacion = now,
                EsProveedorPrincipal = !hasOtherMain,
                Activo = true
            };
            Context.ProductoProveedores.Add(association);
        }
        else
        {
            association.Activo = true;
            if (!hasOtherMain)
            {
                association.EsProveedorPrincipal = true;
            }
        }

        association.UltimoCostoCentavos = ultimoCostoCentavos;
        association.FechaUltimaCompra = fechaCompra;
        association.FechaActualizacion = now;
    }

    private async Task<PurchaseReceptionSummary> BuildReceptionSummaryAsync(
        int receptionId,
        CancellationToken cancellationToken)
    {
        var reception = await Context.RecepcionesCompra
            .AsNoTracking()
            .Include(r => r.Compra)
            .Include(r => r.UsuarioRecepcion)
            .Include(r => r.Detalles)
                .ThenInclude(d => d.Producto)
            .FirstAsync(r => r.RecepcionCompraId == receptionId, cancellationToken);

        return new PurchaseReceptionSummary(
            reception.RecepcionCompraId,
            reception.CodigoRecepcion,
            reception.OperacionId,
            reception.CompraId,
            reception.Compra.CodigoCompra,
            reception.FechaRecepcion,
            reception.UsuarioRecepcion.NombreUsuario,
            reception.Observaciones,
            reception.Detalles
                .OrderBy(d => d.Producto.Nombre)
                .Select(d => new PurchaseReceptionLineSummary(
                    d.RecepcionCompraDetalleId,
                    d.DetalleCompraId,
                    d.ProductoId,
                    d.Producto.CodigoInterno,
                    d.Producto.Nombre,
                    d.CantidadRecibida,
                    d.StockAnterior,
                    d.StockNuevo,
                    d.CostoUnitarioCentavos,
                    d.CostoPromedioAnteriorCentavos,
                    d.CostoPromedioNuevoCentavos,
                    d.MovimientoInventarioId,
                    d.CostoNetoUnitarioCentavos,
                    d.DescuentoAsignadoCentavos,
                    d.ProductoSkuId,
                    d.CodigoSkuSnapshot,
                    d.NombreVarianteSnapshot,
                    d.UbicacionDestino))
                .ToList());
    }

    private static bool IsSameReceptionRequest(
        RecepcionCompra existing,
        RegisterPurchaseReceptionRequest request)
    {
        if (existing.CompraId != request.CompraId)
        {
            return false;
        }

        var stored = existing.Detalles
            .OrderBy(d => d.DetalleCompraId)
            .Select(d => (d.DetalleCompraId, d.CantidadRecibida, d.UbicacionDestino))
            .ToList();
        var incoming = request.Detalles
            .Where(d => d.CantidadRecibida > 0)
            .OrderBy(d => d.DetalleCompraId)
            .Select(d => (d.DetalleCompraId, d.CantidadRecibida, (UbicacionInventario?)d.UbicacionDestino))
            .ToList();

        return stored.SequenceEqual(incoming);
    }

    private static void ValidateReceptionLocation(UbicacionInventario location)
    {
        if (location is not UbicacionInventario.EXHIBICION and
            not UbicacionInventario.BODEGA and
            not UbicacionInventario.SIN_ASIGNAR)
        {
            throw new ArgumentException("La ubicacion de recepcion no es valida.", nameof(location));
        }
    }

    private static EstadoRecepcionCompra CalculateReceptionState(IEnumerable<DetalleCompra> details)
    {
        var rows = details.ToList();
        if (rows.All(d => d.CantidadRecibida == 0))
        {
            return EstadoRecepcionCompra.PENDIENTE;
        }

        return rows.All(d => d.CantidadRecibida >= d.CantidadComprada)
            ? EstadoRecepcionCompra.RECIBIDA
            : EstadoRecepcionCompra.PARCIAL;
    }

    private static long CalculateWeightedAverageCost(
        int stockAnterior,
        long costoAnteriorCentavos,
        int cantidadRecibida,
        long costoRecibidoCentavos)
    {
        var stockNuevo = stockAnterior + cantidadRecibida;
        if (stockNuevo <= 0)
        {
            return costoRecibidoCentavos;
        }

        var weighted = ((decimal)stockAnterior * costoAnteriorCentavos) +
            ((decimal)cantidadRecibida * costoRecibidoCentavos);
        return (long)Math.Round(weighted / stockNuevo, 0, MidpointRounding.AwayFromZero);
    }

    internal static long AllocateDiscountToUnitRange(
        long totalDiscountCentavos,
        int totalUnits,
        int startUnit,
        int unitCount)
    {
        if (totalDiscountCentavos < 0 || totalUnits <= 0 || startUnit < 0 || unitCount <= 0 ||
            startUnit + unitCount > totalUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCount), "El rango de unidades para distribuir el descuento no es valido.");
        }

        var baseDiscount = totalDiscountCentavos / totalUnits;
        var remainderUnits = checked((int)(totalDiscountCentavos % totalUnits));
        var endUnit = startUnit + unitCount;
        var unitsWithExtraCent = Math.Max(0, Math.Min(endUnit, remainderUnits) - startUnit);
        return checked(baseDiscount * unitCount + unitsWithExtraCent);
    }

    private static void ValidateRequestShape(RegisterPurchaseReceptionRequest request)
    {
        if (request.CompraId <= 0)
        {
            throw new ArgumentException("La compra es obligatoria.");
        }

        if (request.OperacionId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de operacion es obligatorio.");
        }

        if (request.Detalles.Count == 0)
        {
            throw new ArgumentException("Debe indicar al menos una linea a recibir.");
        }

        if (request.Detalles.Any(d => d.CantidadRecibida <= 0))
        {
            throw new ArgumentException("Las cantidades recibidas deben ser mayores a cero.");
        }
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    }

    private static string GenerateReceptionCode(int id)
    {
        return $"REC-{id:000000}";
    }

    private static string BuildReceptionAuditSnapshot(RecepcionCompra reception, Compra purchase)
    {
        return string.Join("; ", [
            $"Recepcion={reception.CodigoRecepcion}",
            $"Compra={purchase.CodigoCompra}",
            $"Estado={purchase.EstadoRecepcion}",
            $"Lineas={reception.Detalles.Count}"
        ]);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
