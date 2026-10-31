using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SaleCancellationService : ISaleCancellationService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public SaleCancellationService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    internal SaleCancellationService(AppDbContext context, ICurrentSession currentSession, IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la anulacion de venta.");

    public async Task<SaleCancellationResult> CancelSaleAsync(
        RegisterSaleCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        ValidateRequestShape(request);
        EnsureAdmin();
        var usuarioId = GetCurrentUserId();

        if (request.OperacionId == Guid.Empty)
            throw new ArgumentException("El identificador de operacion no puede estar vacio.", nameof(request));
        if (request.OperacionId.HasValue)
        {
            var completed = await Context.Devoluciones.AsNoTracking()
                .Include(item => item.Reembolsos)
                .SingleOrDefaultAsync(item => item.OperacionId == request.OperacionId, cancellationToken);
            if (completed is not null)
            {
                if (completed.VentaId != request.VentaId ||
                    completed.TipoCorreccion != TipoCorreccionVenta.ANULACION ||
                    completed.EsErrorRegistroSinOperacionReal != request.EsErrorRegistroSinOperacionReal)
                    throw new InvalidOperationException("El identificador de operacion ya fue usado con datos distintos.");
                var completedRefund = completed.Reembolsos.SingleOrDefault();
                return new SaleCancellationResult(completed.DevolucionId, completed.NumeroDevolucion,
                    completed.TotalDevueltoCentavos, completedRefund?.ReembolsoId,
                    completedRefund?.MontoCentavos ?? 0);
            }
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var sale = await Context.Ventas
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .FirstOrDefaultAsync(v => v.VentaId == request.VentaId, cancellationToken)
            ?? throw new InvalidOperationException("La venta original no existe.");

        if (sale.Estado == EstadoVenta.ANULADA)
        {
            throw new InvalidOperationException("La venta ya fue anulada.");
        }

        var hasCancellation = await Context.Devoluciones
            .AnyAsync(d => d.VentaId == sale.VentaId && d.TipoCorreccion == TipoCorreccionVenta.ANULACION, cancellationToken);
        if (hasCancellation)
        {
            throw new InvalidOperationException("La venta ya tiene una operacion de anulacion registrada.");
        }

        var availableByDetail = await BuildAvailableQuantitiesAsync(sale.Detalles, cancellationToken);
        ValidateCancellationCoversAllRemainder(request, availableByDetail);
        if (request.EsErrorRegistroSinOperacionReal && request.Lines.Any(line =>
                line.CondicionProducto != CondicionProductoDevuelto.BUENO || !line.ReintegraStock))
        {
            throw new InvalidOperationException("Si la venta nunca ocurrio, todos los productos deben volver a Exhibicion como vendibles.");
        }

        CuentaPorCobrar? receivableToCancel = null;
        if (request.EsErrorRegistroSinOperacionReal)
        {
            if (request.Reembolso is not null)
                throw new InvalidOperationException("Un error sin transaccion real no puede registrar un reembolso.");

            var hasOrderPayments = await Context.PagosVenta
                .AnyAsync(payment => payment.VentaId == sale.VentaId &&
                    payment.Origen == OrigenPagoVenta.PEDIDO, cancellationToken);
            if (hasOrderPayments)
                throw new InvalidOperationException("Esta venta está vinculada a cobros reales de un pedido y no puede anularse como una operación inexistente.");

            var hasPriorCorrection = await Context.Devoluciones
                .AnyAsync(item => item.VentaId == sale.VentaId, cancellationToken);
            if (hasPriorCorrection)
                throw new InvalidOperationException("No se puede marcar como error sin transaccion real una venta que ya tiene devoluciones o correcciones.");

            receivableToCancel = await Context.CuentasPorCobrar
                .SingleOrDefaultAsync(item => item.VentaId == sale.VentaId, cancellationToken);
            if (receivableToCancel is not null)
            {
                var hasReceivablePayments = await Context.AbonosCuentaPorCobrar
                    .AnyAsync(item => item.CuentaPorCobrarId == receivableToCancel.CuentaPorCobrarId, cancellationToken);
                if (receivableToCancel.Estado != EstadoCuentaPorCobrar.ABIERTA || hasReceivablePayments)
                    throw new InvalidOperationException("La venta tiene pagos o movimientos de crédito reales; no se puede anular como un error sin transacción real.");
            }
        }

        var now = DateTime.UtcNow;
        var cash = request.Reembolso?.MetodoReembolso == MetodoReembolso.EFECTIVO
            ? await GetOpenCashAsync(cancellationToken)
                ?? throw new InvalidOperationException("El reembolso en efectivo requiere caja abierta.")
            : null;

        var numeroCorreccion = await GenerateNumeroCorreccionAsync(cancellationToken);
        var correction = new Devolucion
        {
            OperacionId = request.OperacionId,
            NumeroDevolucion = numeroCorreccion,
            VentaId = sale.VentaId,
            UsuarioId = usuarioId,
            CajaId = cash?.CajaId,
            FechaDevolucion = now,
            TipoCorreccion = TipoCorreccionVenta.ANULACION,
            EsErrorRegistroSinOperacionReal = request.EsErrorRegistroSinOperacionReal,
            Motivo = request.Motivo.Trim(),
            Observaciones = NormalizeOptional(request.Observaciones)
        };
        Context.Devoluciones.Add(correction);

        long totalAnulado = 0;
        foreach (var requestLine in request.Lines)
        {
            var saleLine = sale.Detalles.First(d => d.DetalleVentaId == requestLine.DetalleVentaId);
            ValidateLineCondition(requestLine);

            var returnedValue = CalculateReturnedAmount(saleLine.TotalCentavos, saleLine.Cantidad, requestLine.Cantidad);
            var returnedDiscount = CalculateReturnedAmount(saleLine.DescuentoCentavos, saleLine.Cantidad, requestLine.Cantidad);
            totalAnulado += returnedValue;

            var detail = new DetalleDevolucion
            {
                Devolucion = correction,
                DetalleVentaId = saleLine.DetalleVentaId,
                ProductoId = saleLine.ProductoId,
                ProductoSkuId = saleLine.ProductoSkuId,
                CodigoSkuSnapshot = saleLine.CodigoSkuSnapshot,
                NombreVarianteSnapshot = saleLine.NombreVarianteSnapshot,
                Cantidad = requestLine.Cantidad,
                PrecioUnitarioCentavos = saleLine.PrecioUnitarioCentavos,
                DescuentoProporcionalCentavos = returnedDiscount,
                SubtotalDevueltoCentavos = returnedValue,
                CondicionProducto = requestLine.CondicionProducto,
                ReintegraStock = requestLine.ReintegraStock
            };

            if (requestLine.ReintegraStock)
            {
                var product = saleLine.Producto;
                var sku = saleLine.ProductoSkuId.HasValue
                    ? await InventoryStockEngine.GetSkuAsync(Context, saleLine.ProductoSkuId.Value, cancellationToken)
                    : await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                        Context, product.ProductoId, cancellationToken);
                var stockAnterior = InventoryStockEngine.GetPhysicalTotal(sku.Producto);
                var destination = request.EsErrorRegistroSinOperacionReal
                    ? UbicacionInventario.EXHIBICION
                    : UbicacionInventario.BODEGA;
                InventoryStockEngine.AddPhysical(Context, sku, destination,
                    requestLine.Cantidad, usuarioId, TipoOperacionInventario.CANCELACION,
                    $"Anulacion {numeroCorreccion}: {request.Motivo.Trim()}", now,
                    request.OperacionId.HasValue
                        ? OperationalOperationIds.Derive(request.OperacionId.Value, "sale-cancellation", saleLine.DetalleVentaId)
                        : null);
                detail.UbicacionReintegro = destination;

                var movement = new MovimientoInventario
                {
                    ProductoId = product.ProductoId,
                    VentaId = sale.VentaId,
                    UsuarioId = usuarioId,
                    TipoMovimiento = TipoMovimientoInventario.ENTRADA,
                    Cantidad = requestLine.Cantidad,
                    StockAnterior = stockAnterior,
                    StockNuevo = stockAnterior + requestLine.Cantidad,
                    Motivo = $"Anulacion {numeroCorreccion}: {request.Motivo.Trim()}",
                    FechaMovimiento = now
                };
                Context.MovimientosInventario.Add(movement);
                detail.MovimientoInventario = movement;
            }

            Context.DetalleDevoluciones.Add(detail);

            if (RequiresWaste(requestLine.CondicionProducto))
            {
                Context.Mermas.Add(new Merma
                {
                    Devolucion = correction,
                    DetalleDevolucion = detail,
                    VentaId = sale.VentaId,
                    ProductoId = saleLine.ProductoId,
                    ProductoSkuId = saleLine.ProductoSkuId,
                    UsuarioId = usuarioId,
                    CondicionProducto = requestLine.CondicionProducto,
                    Cantidad = requestLine.Cantidad,
                    Motivo = request.Motivo.Trim(),
                    FechaMerma = now
                });
            }
        }

        correction.TotalDevueltoCentavos = totalAnulado;

        Reembolso? refund = null;
        if (request.Reembolso is not null)
        {
            ValidateRefund(request.Reembolso, totalAnulado);
            await PaymentMethodAvailability.EnsureAvailableAsync(
                Context,
                request.Reembolso.MetodoReembolso,
                cancellationToken);
            refund = new Reembolso
            {
                Devolucion = correction,
                UsuarioId = usuarioId,
                CajaId = request.Reembolso.MetodoReembolso == MetodoReembolso.EFECTIVO ? cash!.CajaId : null,
                MetodoReembolso = request.Reembolso.MetodoReembolso,
                MontoCentavos = request.Reembolso.MontoCentavos,
                Motivo = request.Reembolso.Motivo.Trim(),
                Referencia = NormalizeOptional(request.Reembolso.Referencia),
                FechaReembolso = now
            };
            Context.Reembolsos.Add(refund);
        }

        sale.Estado = EstadoVenta.ANULADA;
        if (receivableToCancel is not null)
        {
            receivableToCancel.Estado = EstadoCuentaPorCobrar.ANULADA;
            receivableToCancel.FechaAnulacion = now;
            receivableToCancel.SaldoActualCentavos = 0;
            receivableToCancel.FechaActualizacion = now;
        }

        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.ANULACION_REGISTRADA,
                "devoluciones",
                correction.DevolucionId,
                $"Anulacion {numeroCorreccion} registrada para venta {sale.NumeroVenta} por {FormatCentavos(totalAnulado)}. Motivo: {correction.Motivo}" +
                (request.EsErrorRegistroSinOperacionReal
                    ? " Confirmado como error de registro sin transaccion real; los pagos originales no se consideran ingresos ni movimientos de caja."
                    : string.Empty)),
            cancellationToken);

        if (receivableToCancel is not null)
        {
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    usuarioId,
                    TipoBitacora.ANULACION_REGISTRADA,
                    "cuentas_por_cobrar",
                    receivableToCancel.CuentaPorCobrarId,
                    $"Cuenta {receivableToCancel.CodigoCuenta} anulada junto con la venta {sale.NumeroVenta} por error de registro sin abonos reales. Motivo: {correction.Motivo}"),
                cancellationToken);
        }

        if (refund is not null)
        {
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    usuarioId,
                    TipoBitacora.REEMBOLSO_REGISTRADO,
                    "reembolsos",
                    refund.ReembolsoId,
                    $"Reembolso {refund.MetodoReembolso} por {FormatCentavos(refund.MontoCentavos)} asociado a anulacion {numeroCorreccion}."),
                cancellationToken);
        }

        if (request.Lines.Any(l => RequiresWaste(l.CondicionProducto)))
        {
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    usuarioId,
                    TipoBitacora.MERMA_REGISTRADA,
                    "mermas",
                    null,
                    $"Mermas registradas en anulacion {numeroCorreccion}."),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new SaleCancellationResult(
            correction.DevolucionId,
            correction.NumeroDevolucion,
            correction.TotalDevueltoCentavos,
            refund?.ReembolsoId,
            refund?.MontoCentavos ?? 0);
    }

    private static void ValidateRequestShape(RegisterSaleCancellationRequest request)
    {
        if (request.VentaId <= 0)
        {
            throw new ArgumentException("La venta es obligatoria.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo de la anulacion es obligatorio.", nameof(request));
        }

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("La anulacion debe incluir todas las cantidades remanentes.", nameof(request));
        }
    }

    private async Task<Dictionary<int, int>> BuildAvailableQuantitiesAsync(
        IEnumerable<DetalleVenta> saleLines,
        CancellationToken cancellationToken)
    {
        var lines = saleLines.ToList();
        var detailIds = lines.Select(d => d.DetalleVentaId).ToList();
        var processed = await Context.DetalleDevoluciones
            .Where(d => detailIds.Contains(d.DetalleVentaId))
            .GroupBy(d => d.DetalleVentaId)
            .Select(g => new { DetalleVentaId = g.Key, Cantidad = g.Sum(x => x.Cantidad) })
            .ToDictionaryAsync(x => x.DetalleVentaId, x => x.Cantidad, cancellationToken);

        return lines
            .Select(d => new
            {
                d.DetalleVentaId,
                Available = Math.Max(0, d.Cantidad - processed.GetValueOrDefault(d.DetalleVentaId))
            })
            .Where(x => x.Available > 0)
            .ToDictionary(x => x.DetalleVentaId, x => x.Available);
    }

    private static void ValidateCancellationCoversAllRemainder(
        RegisterSaleCancellationRequest request,
        IReadOnlyDictionary<int, int> availableByDetail)
    {
        if (availableByDetail.Count == 0)
        {
            throw new InvalidOperationException("La venta no tiene cantidades disponibles para anular.");
        }

        var requested = request.Lines.ToDictionary(l => l.DetalleVentaId);
        if (requested.Count != request.Lines.Count || requested.Count != availableByDetail.Count)
        {
            throw new InvalidOperationException("La anulacion debe procesar todo el remanente disponible, no una parte.");
        }

        foreach (var (detailId, available) in availableByDetail)
        {
            if (!requested.TryGetValue(detailId, out var line) || line.Cantidad != available)
            {
                throw new InvalidOperationException("La anulacion debe procesar todo el remanente disponible, no una parte.");
            }
        }
    }

    private static void ValidateLineCondition(RegisterSaleCancellationLineRequest line)
    {
        if (line.CondicionProducto is not CondicionProductoDevuelto.BUENO
            and not CondicionProductoDevuelto.DANADO
            and not CondicionProductoDevuelto.NO_APTO
            and not CondicionProductoDevuelto.FALTANTE)
        {
            throw new ArgumentException("La condicion del producto no es valida.", nameof(line));
        }

        if (line.Cantidad <= 0)
        {
            throw new ArgumentException("La cantidad a anular debe ser mayor que cero.", nameof(line));
        }

        if (line.CondicionProducto != CondicionProductoDevuelto.BUENO && line.ReintegraStock)
        {
            throw new InvalidOperationException("Solo un producto en buen estado puede reintegrarse al stock vendible.");
        }
    }

    private void ValidateRefund(RegisterRefundRequest refund, long totalAnulado)
    {
        if (refund.MontoCentavos <= 0)
        {
            throw new ArgumentException("El monto del reembolso debe ser mayor que cero.", nameof(refund));
        }

        if (refund.MontoCentavos > totalAnulado)
        {
            throw new InvalidOperationException("El reembolso no puede ser mayor que el total anulado.");
        }

        if (refund.MetodoReembolso is not MetodoReembolso.EFECTIVO and not MetodoReembolso.TRANSFERENCIA and not MetodoReembolso.BITCOIN)
        {
            throw new ArgumentException("El metodo de reembolso no es valido.", nameof(refund));
        }

        if (string.IsNullOrWhiteSpace(refund.Motivo))
        {
            throw new ArgumentException("El motivo del reembolso es obligatorio.", nameof(refund));
        }

        if ((refund.MetodoReembolso is MetodoReembolso.TRANSFERENCIA or MetodoReembolso.BITCOIN)
            && string.IsNullOrWhiteSpace(refund.Referencia))
        {
            throw new ArgumentException("La referencia es obligatoria para reembolsos por transferencia o Bitcoin.", nameof(refund));
        }
    }

    private async Task<string> GenerateNumeroCorreccionAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var number = $"ANU-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            var exists = await Context.Devoluciones.AnyAsync(d => d.NumeroDevolucion == number, cancellationToken);
            if (!exists)
            {
                return number;
            }

            await Task.Delay(5, cancellationToken);
        }

        throw new InvalidOperationException("No se pudo generar numero unico de anulacion.");
    }

    private Task<Caja?> GetOpenCashAsync(CancellationToken cancellationToken) =>
        CashRegisterStateQueries.GetCurrentOpenCashAsync(Context, cancellationToken);

    private void EnsureAdmin()
    {
        if (!_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("La anulacion de ventas requiere ADMIN.");
        }
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para anular ventas.");
    }

    private static long CalculateReturnedAmount(long originalAmount, int originalQuantity, int returnedQuantity)
    {
        return decimal.ToInt64(decimal.Round(
            originalAmount * returnedQuantity / (decimal)originalQuantity,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static bool RequiresWaste(CondicionProductoDevuelto condition)
    {
        return condition is CondicionProductoDevuelto.DANADO or CondicionProductoDevuelto.NO_APTO;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FormatCentavos(long centavos)
    {
        return (centavos / 100m).ToString("0.00");
    }
}
