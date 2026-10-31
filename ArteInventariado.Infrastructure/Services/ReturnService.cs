using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class ReturnService : IReturnService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public ReturnService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
        _permissionService = permissionService;
    }

    internal ReturnService(AppDbContext context, ICurrentSession currentSession, IAuditService auditService, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la devolucion.");

    public async Task<ReturnResult> RegisterReturnAsync(
        RegisterReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        ValidateRequest(request);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.DEVOLUCION_REGISTRAR, cancellationToken);

        if (request.OperacionId == Guid.Empty)
            throw new ArgumentException("El identificador de operacion no puede estar vacio.", nameof(request));
        if (request.OperacionId.HasValue)
        {
            var completed = await Context.Devoluciones.AsNoTracking()
                .Include(item => item.Reembolsos)
                .SingleOrDefaultAsync(item => item.OperacionId == request.OperacionId, cancellationToken);
            if (completed is not null)
            {
                if (completed.VentaId != request.VentaId || completed.TipoCorreccion != TipoCorreccionVenta.DEVOLUCION)
                    throw new InvalidOperationException("El identificador de operacion ya fue usado con datos distintos.");
                var completedRefund = completed.Reembolsos.SingleOrDefault();
                return new ReturnResult(completed.DevolucionId, completed.NumeroDevolucion,
                    completed.TotalDevueltoCentavos, completedRefund?.ReembolsoId,
                    completedRefund?.MontoCentavos ?? 0);
            }
        }

        var usuarioId = GetCurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var sale = await Context.Ventas
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .FirstOrDefaultAsync(v => v.VentaId == request.VentaId, cancellationToken)
            ?? throw new InvalidOperationException("La venta original no existe.");

        if (sale.Estado != EstadoVenta.COMPLETADA)
        {
            throw new InvalidOperationException("Solo se pueden registrar devoluciones de ventas completadas.");
        }

        var requestedByDetail = request.Lines.ToDictionary(l => l.DetalleVentaId);
        var saleLines = sale.Detalles
            .Where(d => requestedByDetail.ContainsKey(d.DetalleVentaId))
            .ToDictionary(d => d.DetalleVentaId);

        if (saleLines.Count != request.Lines.Count)
        {
            throw new InvalidOperationException("Una o mas lineas seleccionadas no pertenecen a la venta original.");
        }

        var returnedByDetail = await Context.DetalleDevoluciones
            .Where(d => requestedByDetail.Keys.Contains(d.DetalleVentaId))
            .GroupBy(d => d.DetalleVentaId)
            .Select(g => new { DetalleVentaId = g.Key, Cantidad = g.Sum(x => x.Cantidad) })
            .ToDictionaryAsync(x => x.DetalleVentaId, x => x.Cantidad, cancellationToken);

        var now = DateTime.UtcNow;
        var cajaAbierta = await GetOpenCashAsync(cancellationToken);
        var refundCajaId = request.Reembolso?.MetodoReembolso == MetodoReembolso.EFECTIVO
            ? cajaAbierta?.CajaId ?? throw new InvalidOperationException("El reembolso en efectivo requiere caja abierta.")
            : (int?)null;

        var devolucion = new Devolucion
        {
            OperacionId = request.OperacionId,
            NumeroDevolucion = await GenerateNumeroDevolucionAsync(cancellationToken),
            VentaId = sale.VentaId,
            UsuarioId = usuarioId,
            CajaId = refundCajaId,
            FechaDevolucion = now,
            TipoCorreccion = TipoCorreccionVenta.DEVOLUCION,
            Motivo = request.Motivo.Trim(),
            Observaciones = NormalizeOptional(request.Observaciones)
        };

        Context.Devoluciones.Add(devolucion);

        long totalDevuelto = 0;
        foreach (var requestedLine in request.Lines)
        {
            var saleLine = saleLines[requestedLine.DetalleVentaId];
            var alreadyReturned = returnedByDetail.GetValueOrDefault(saleLine.DetalleVentaId);
            var available = saleLine.Cantidad - alreadyReturned;

            if (requestedLine.Cantidad > available)
            {
                throw new InvalidOperationException(
                    $"No se puede devolver mas cantidad de '{saleLine.ProductoNombreSnapshot ?? saleLine.Producto.Nombre}' que la disponible ({available}).");
            }

            if (requestedLine.CondicionProducto != CondicionProductoDevuelto.BUENO && requestedLine.ReintegraStock)
            {
                throw new InvalidOperationException("Solo un producto en buen estado puede reintegrarse al stock vendible.");
            }

            if (requestedLine.CondicionProducto != CondicionProductoDevuelto.BUENO && !_currentSession.EsAdmin)
            {
                throw new InvalidOperationException("Registrar producto danado, no apto o faltante requiere ADMIN.");
            }

            var generalDiscount = saleLine.DescuentoGeneralProporcionalCentavos ?? 0;
            var subtotalDevuelto = CalculateReturnedAmount(
                saleLine.TotalCentavos - generalDiscount,
                saleLine.Cantidad,
                requestedLine.Cantidad);
            var descuentoProporcional = CalculateReturnedAmount(
                saleLine.DescuentoCentavos + generalDiscount,
                saleLine.Cantidad,
                requestedLine.Cantidad);

            var detail = new DetalleDevolucion
            {
                Devolucion = devolucion,
                DetalleVentaId = saleLine.DetalleVentaId,
                ProductoId = saleLine.ProductoId,
                ProductoSkuId = saleLine.ProductoSkuId,
                CodigoSkuSnapshot = saleLine.CodigoSkuSnapshot,
                NombreVarianteSnapshot = saleLine.NombreVarianteSnapshot,
                Cantidad = requestedLine.Cantidad,
                PrecioUnitarioCentavos = saleLine.PrecioUnitarioCentavos,
                DescuentoProporcionalCentavos = descuentoProporcional,
                SubtotalDevueltoCentavos = subtotalDevuelto,
                CondicionProducto = requestedLine.CondicionProducto,
                ReintegraStock = requestedLine.ReintegraStock
            };

            if (requestedLine.ReintegraStock)
            {
                var producto = saleLine.Producto;
                var sku = saleLine.ProductoSkuId.HasValue
                    ? await InventoryStockEngine.GetSkuAsync(Context, saleLine.ProductoSkuId.Value, cancellationToken)
                    : await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                        Context, producto.ProductoId, cancellationToken);
                var stockAnterior = InventoryStockEngine.GetPhysicalTotal(sku.Producto);
                var stockNuevo = stockAnterior + requestedLine.Cantidad;
                InventoryStockEngine.AddPhysical(Context, sku, UbicacionInventario.BODEGA,
                    requestedLine.Cantidad, usuarioId, TipoOperacionInventario.DEVOLUCION,
                    $"Devolucion {devolucion.NumeroDevolucion}: {request.Motivo.Trim()}", now,
                    request.OperacionId.HasValue
                        ? OperationalOperationIds.Derive(request.OperacionId.Value, "return", saleLine.DetalleVentaId)
                        : null);
                detail.UbicacionReintegro = UbicacionInventario.BODEGA;

                var movement = new MovimientoInventario
                {
                    ProductoId = producto.ProductoId,
                    VentaId = sale.VentaId,
                    UsuarioId = usuarioId,
                    TipoMovimiento = TipoMovimientoInventario.ENTRADA,
                    Cantidad = requestedLine.Cantidad,
                    StockAnterior = stockAnterior,
                    StockNuevo = stockNuevo,
                    Motivo = $"Devolucion {devolucion.NumeroDevolucion}: {request.Motivo.Trim()}",
                    FechaMovimiento = now
                };

                Context.MovimientosInventario.Add(movement);
                detail.MovimientoInventario = movement;
            }

            Context.DetalleDevoluciones.Add(detail);

            if (RequiresWaste(requestedLine.CondicionProducto))
            {
                Context.Mermas.Add(new Merma
                {
                    Devolucion = devolucion,
                    DetalleDevolucion = detail,
                    VentaId = sale.VentaId,
                    ProductoId = saleLine.ProductoId,
                    ProductoSkuId = saleLine.ProductoSkuId,
                    UsuarioId = usuarioId,
                    CondicionProducto = requestedLine.CondicionProducto,
                    Cantidad = requestedLine.Cantidad,
                    Motivo = request.Motivo.Trim(),
                    FechaMerma = now
                });
            }

            totalDevuelto += subtotalDevuelto;
        }

        devolucion.TotalDevueltoCentavos = totalDevuelto;

        Reembolso? reembolso = null;
        if (request.Reembolso is not null)
        {
            if (!_currentSession.EsAdmin)
            {
                throw new InvalidOperationException("Registrar reembolso requiere ADMIN.");
            }

            ValidateRefund(request.Reembolso, totalDevuelto);
            await PaymentMethodAvailability.EnsureAvailableAsync(
                Context,
                request.Reembolso.MetodoReembolso,
                cancellationToken);
            reembolso = new Reembolso
            {
                Devolucion = devolucion,
                UsuarioId = usuarioId,
                CajaId = refundCajaId,
                MetodoReembolso = request.Reembolso.MetodoReembolso,
                MontoCentavos = request.Reembolso.MontoCentavos,
                Motivo = request.Reembolso.Motivo.Trim(),
                Referencia = NormalizeOptional(request.Reembolso.Referencia),
                FechaReembolso = now
            };
            Context.Reembolsos.Add(reembolso);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateReturnNumberError(ex))
        {
            throw new InvalidOperationException("No se pudo generar un numero unico de devolucion. Intente guardar nuevamente.", ex);
        }

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.DEVOLUCION_REGISTRADA,
                "devoluciones",
                devolucion.DevolucionId,
                $"Devolucion {devolucion.NumeroDevolucion} registrada para venta {sale.NumeroVenta} por {FormatCentavos(totalDevuelto)}. Motivo: {devolucion.Motivo}"),
            cancellationToken);

        if (reembolso is not null)
        {
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    usuarioId,
                    TipoBitacora.REEMBOLSO_REGISTRADO,
                    "reembolsos",
                    reembolso.ReembolsoId,
                    $"Reembolso {reembolso.MetodoReembolso} registrado por {FormatCentavos(reembolso.MontoCentavos)} asociado a {devolucion.NumeroDevolucion}."),
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
                    $"Merma registrada en devolucion {devolucion.NumeroDevolucion}."),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new ReturnResult(
            devolucion.DevolucionId,
            devolucion.NumeroDevolucion,
            devolucion.TotalDevueltoCentavos,
            reembolso?.ReembolsoId,
            reembolso?.MontoCentavos ?? 0);
    }

    private static void ValidateRequest(RegisterReturnRequest request)
    {
        if (request.VentaId <= 0)
        {
            throw new ArgumentException("La venta original es obligatoria.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo de la devolucion es obligatorio.", nameof(request));
        }

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("Debe seleccionar al menos una linea para devolver.", nameof(request));
        }

        foreach (var line in request.Lines)
        {
            if (line.DetalleVentaId <= 0)
            {
                throw new ArgumentException("La linea de venta no es valida.", nameof(request));
            }

            if (line.Cantidad <= 0)
            {
                throw new ArgumentException("La cantidad a devolver debe ser mayor que cero.", nameof(request));
            }

            if (line.CondicionProducto is not CondicionProductoDevuelto.BUENO
                and not CondicionProductoDevuelto.DANADO
                and not CondicionProductoDevuelto.NO_APTO
                and not CondicionProductoDevuelto.FALTANTE)
            {
                throw new ArgumentException("La condicion del producto debe ser BUENO, DANADO, NO_APTO o FALTANTE.", nameof(request));
            }
        }
    }

    private static void ValidateRefund(RegisterRefundRequest refund, long totalDevuelto)
    {
        if (refund.MontoCentavos <= 0)
        {
            throw new ArgumentException("El monto del reembolso debe ser mayor que cero.", nameof(refund));
        }

        if (refund.MontoCentavos > totalDevuelto)
        {
            throw new InvalidOperationException("El reembolso no puede ser mayor que el total devuelto.");
        }

        if (string.IsNullOrWhiteSpace(refund.Motivo))
        {
            throw new ArgumentException("El motivo del reembolso es obligatorio.", nameof(refund));
        }

        if (refund.MetodoReembolso is not MetodoReembolso.EFECTIVO and not MetodoReembolso.TRANSFERENCIA and not MetodoReembolso.BITCOIN)
        {
            throw new ArgumentException("El metodo de reembolso no es valido.", nameof(refund));
        }

        if ((refund.MetodoReembolso is MetodoReembolso.TRANSFERENCIA or MetodoReembolso.BITCOIN)
            && string.IsNullOrWhiteSpace(refund.Referencia))
        {
            throw new ArgumentException("La referencia es obligatoria para reembolsos por transferencia o Bitcoin.", nameof(refund));
        }
    }

    private async Task<string> GenerateNumeroDevolucionAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var number = $"DEV-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            var exists = await Context.Devoluciones
                .AnyAsync(d => d.NumeroDevolucion == number, cancellationToken);
            if (!exists)
            {
                return number;
            }

            await Task.Delay(5, cancellationToken);
        }

        throw new InvalidOperationException("No se pudo generar numero de devolucion.");
    }

    private static bool IsDuplicateReturnNumberError(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE constraint failed: devoluciones.numero_devolucion", StringComparison.OrdinalIgnoreCase);
    }

    private Task<Caja?> GetOpenCashAsync(CancellationToken cancellationToken) =>
        CashRegisterStateQueries.GetCurrentOpenCashAsync(Context, cancellationToken);

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para registrar devoluciones.");
    }

    private static long CalculateReturnedAmount(long originalAmount, int originalQuantity, int returnedQuantity)
    {
        return decimal.ToInt64(decimal.Round(
            originalAmount * returnedQuantity / (decimal)originalQuantity,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FormatCentavos(long centavos)
    {
        return (centavos / 100m).ToString("0.00");
    }

    private static bool RequiresWaste(CondicionProductoDevuelto condition)
    {
        return condition is CondicionProductoDevuelto.DANADO or CondicionProductoDevuelto.NO_APTO;
    }
}
