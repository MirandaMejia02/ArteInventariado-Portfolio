using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SaleChangeService : ISaleChangeService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public SaleChangeService(
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

    internal SaleChangeService(AppDbContext context, ICurrentSession currentSession, IAuditService auditService, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para el cambio de venta.");

    public async Task<SaleChangeResult> RegisterChangeAsync(
        RegisterSaleChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        ValidateRequestShape(request);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CAMBIO_REGISTRAR, cancellationToken);
        var usuarioId = GetCurrentUserId();

        if (request.OperacionId == Guid.Empty)
            throw new ArgumentException("El identificador de operacion no puede estar vacio.", nameof(request));
        if (request.OperacionId.HasValue)
        {
            var completed = await Context.Devoluciones.AsNoTracking()
                .Include(item => item.DetallesCambio)
                .Include(item => item.PagosCorreccion)
                .Include(item => item.Reembolsos)
                .SingleOrDefaultAsync(item => item.OperacionId == request.OperacionId, cancellationToken);
            if (completed is not null)
            {
                if (completed.VentaId != request.VentaId || completed.TipoCorreccion != TipoCorreccionVenta.CAMBIO)
                    throw new InvalidOperationException("El identificador de operacion ya fue usado con datos distintos.");
                var completedNewValue = completed.DetallesCambio.Sum(item => item.TotalCentavos);
                return new SaleChangeResult(completed.DevolucionId, completed.NumeroDevolucion,
                    completed.TotalDevueltoCentavos, completedNewValue,
                    completedNewValue - completed.TotalDevueltoCentavos,
                    completed.PagosCorreccion.SingleOrDefault()?.PagoCorreccionVentaId,
                    completed.Reembolsos.SingleOrDefault()?.ReembolsoId);
            }
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var sale = await Context.Ventas
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .FirstOrDefaultAsync(v => v.VentaId == request.VentaId, cancellationToken)
            ?? throw new InvalidOperationException("La venta original no existe.");

        if (sale.Estado != EstadoVenta.COMPLETADA)
        {
            throw new InvalidOperationException("Solo se pueden registrar cambios de ventas completadas.");
        }

        var saleLine = sale.Detalles.FirstOrDefault(d => d.DetalleVentaId == request.DetalleVentaId)
            ?? throw new InvalidOperationException("La linea seleccionada no pertenece a la venta original.");

        var available = await GetAvailableQuantityAsync(saleLine.DetalleVentaId, saleLine.Cantidad, cancellationToken);
        if (request.Cantidad > available)
        {
            throw new InvalidOperationException(
                $"No se puede cambiar mas cantidad de '{saleLine.ProductoNombreSnapshot ?? saleLine.Producto.Nombre}' que la disponible ({available}).");
        }

        var newProduct = await Context.Productos
            .Include(p => p.Categoria)
            .Include(p => p.Subcategoria)
            .Include(p => p.Skus)
                .ThenInclude(sku => sku.SaldosInventario)
            .FirstOrDefaultAsync(p => p.ProductoId == request.ProductoNuevoId, cancellationToken)
            ?? throw new InvalidOperationException("El producto nuevo no existe.");

        if (newProduct.Estado != EstadoProducto.ACTIVO)
        {
            throw new InvalidOperationException("El producto nuevo no esta activo.");
        }

        var newSku = newProduct.Skus.Count == 0 && !request.ProductoNuevoSkuId.HasValue
            ? await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                Context, newProduct.ProductoId, cancellationToken)
            : ResolveNewSku(newProduct, request.ProductoNuevoSkuId);
        var newSkuAvailable = InventoryStockEngine.GetAvailableTotal(newSku);
        if (newSkuAvailable < request.Cantidad)
        {
            throw new InvalidOperationException(
                $"Stock insuficiente para '{newProduct.Nombre}' / {newSku.CodigoSku}. Disponible: {newSkuAvailable}.");
        }

        ValidateConditionAndPermissions(request);

        var originalGeneralDiscount = saleLine.DescuentoGeneralProporcionalCentavos ?? 0;
        var returnedValue = CalculateReturnedAmount(
            saleLine.TotalCentavos - originalGeneralDiscount,
            saleLine.Cantidad,
            request.Cantidad);
        var returnedDiscount = CalculateReturnedAmount(
            saleLine.DescuentoCentavos + originalGeneralDiscount,
            saleLine.Cantidad,
            request.Cantidad);
        var newSubtotal = newSku.ResolverPrecioVentaCentavos() * request.Cantidad;
        var newTotal = newSubtotal;
        var difference = newTotal - returnedValue;
        var now = DateTime.UtcNow;
        var newProductCostOrigin = (await HistoricalProductSnapshotResolver.ResolveCostOriginsAsync(
            Context,
            [newProduct],
            cancellationToken))[newProduct.ProductoId];

        ValidateFinancialDifference(request, difference);
        if (request.CobroAdicional is not null)
        {
            await PaymentMethodAvailability.EnsureAvailableAsync(
                Context,
                request.CobroAdicional.MetodoPago,
                cancellationToken);
        }

        if (request.Reembolso is not null)
        {
            await PaymentMethodAvailability.EnsureAvailableAsync(
                Context,
                request.Reembolso.MetodoReembolso,
                cancellationToken);
        }

        var cash = await GetOpenCashForOperationAsync(request, difference, cancellationToken);
        var numeroCorreccion = await GenerateNumeroCorreccionAsync("CAM", cancellationToken);

        var correction = new Devolucion
        {
            OperacionId = request.OperacionId,
            NumeroDevolucion = numeroCorreccion,
            VentaId = sale.VentaId,
            UsuarioId = usuarioId,
            CajaId = cash?.CajaId,
            FechaDevolucion = now,
            TipoCorreccion = TipoCorreccionVenta.CAMBIO,
            TotalDevueltoCentavos = returnedValue,
            Motivo = request.Motivo.Trim(),
            Observaciones = NormalizeOptional(request.Observaciones)
        };
        Context.Devoluciones.Add(correction);

        var returnDetail = new DetalleDevolucion
        {
            Devolucion = correction,
            DetalleVentaId = saleLine.DetalleVentaId,
            ProductoId = saleLine.ProductoId,
            ProductoSkuId = saleLine.ProductoSkuId,
            CodigoSkuSnapshot = saleLine.CodigoSkuSnapshot,
            NombreVarianteSnapshot = saleLine.NombreVarianteSnapshot,
            Cantidad = request.Cantidad,
            PrecioUnitarioCentavos = saleLine.PrecioUnitarioCentavos,
            DescuentoProporcionalCentavos = returnedDiscount,
            SubtotalDevueltoCentavos = returnedValue,
            CondicionProducto = request.CondicionProductoDevuelto,
            ReintegraStock = request.ReintegraStock
        };

        if (request.ReintegraStock)
        {
            var originalProduct = saleLine.Producto;
            var originalSku = saleLine.ProductoSkuId.HasValue
                ? await InventoryStockEngine.GetSkuAsync(Context, saleLine.ProductoSkuId.Value, cancellationToken)
                : await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                    Context, originalProduct.ProductoId, cancellationToken);
            var stockAnterior = InventoryStockEngine.GetPhysicalTotal(originalSku.Producto);
            InventoryStockEngine.AddPhysical(Context, originalSku, UbicacionInventario.BODEGA,
                request.Cantidad, usuarioId, TipoOperacionInventario.CAMBIO_ENTRADA,
                $"Cambio {numeroCorreccion}: {request.Motivo.Trim()}", now,
                request.OperacionId.HasValue
                    ? OperationalOperationIds.Derive(request.OperacionId.Value, "sale-change-return", saleLine.DetalleVentaId)
                    : null);
            returnDetail.UbicacionReintegro = UbicacionInventario.BODEGA;

            var returnMovement = new MovimientoInventario
            {
                ProductoId = originalProduct.ProductoId,
                VentaId = sale.VentaId,
                UsuarioId = usuarioId,
                TipoMovimiento = TipoMovimientoInventario.ENTRADA,
                Cantidad = request.Cantidad,
                StockAnterior = stockAnterior,
                StockNuevo = stockAnterior + request.Cantidad,
                Motivo = $"Cambio {numeroCorreccion}: {request.Motivo.Trim()}",
                FechaMovimiento = now
            };
            Context.MovimientosInventario.Add(returnMovement);
            returnDetail.MovimientoInventario = returnMovement;
        }

        Context.DetalleDevoluciones.Add(returnDetail);

        if (RequiresWaste(request.CondicionProductoDevuelto))
        {
            Context.Mermas.Add(new Merma
            {
                Devolucion = correction,
                DetalleDevolucion = returnDetail,
                VentaId = sale.VentaId,
                ProductoId = saleLine.ProductoId,
                ProductoSkuId = saleLine.ProductoSkuId,
                UsuarioId = usuarioId,
                CondicionProducto = request.CondicionProductoDevuelto,
                Cantidad = request.Cantidad,
                Motivo = request.Motivo.Trim(),
                FechaMerma = now
            });
        }

        var stockNuevoAnterior = InventoryStockEngine.GetPhysicalTotal(newSku.Producto);
        InventoryStockEngine.RemovePhysical(Context, newSku, request.Cantidad, usuarioId,
            TipoOperacionInventario.CAMBIO_SALIDA,
            $"Cambio {numeroCorreccion}: producto entregado", now,
            operationId: request.OperacionId.HasValue
                ? OperationalOperationIds.Derive(request.OperacionId.Value, "sale-change-output", newSku.ProductoSkuId)
                : null);
        var newProductMovement = new MovimientoInventario
        {
            ProductoId = newProduct.ProductoId,
            VentaId = sale.VentaId,
            UsuarioId = usuarioId,
            TipoMovimiento = TipoMovimientoInventario.SALIDA,
            Cantidad = -request.Cantidad,
            StockAnterior = stockNuevoAnterior,
            StockNuevo = stockNuevoAnterior - request.Cantidad,
            Motivo = $"Cambio {numeroCorreccion}: producto entregado",
            FechaMovimiento = now
        };
        Context.MovimientosInventario.Add(newProductMovement);

        var changeDetail = new DetalleCambioVenta
        {
            Devolucion = correction,
            DetalleVentaOrigenId = saleLine.DetalleVentaId,
            ProductoNuevoId = newProduct.ProductoId,
            ProductoSkuNuevo = newSku,
            Cantidad = request.Cantidad,
            PrecioUnitarioCentavos = newSku.ResolverPrecioVentaCentavos(),
            CostoUnitarioCentavos = newSku.ResolverCostoCentavos(),
            OrigenCosto = newSku.CostoOverrideCentavos.HasValue
                ? OrigenCostoHistorico.SKU_OVERRIDE
                : newProductCostOrigin,
            ProductoCodigoSnapshot = newProduct.CodigoInterno,
            ProductoNombreSnapshot = newProduct.Nombre,
            CodigoSkuSnapshot = newSku.CodigoSku,
            NombreVarianteSnapshot = newSku.NombreVariante,
            CategoriaIdSnapshot = newProduct.CategoriaId,
            CategoriaNombreSnapshot = newProduct.Categoria.Nombre,
            SubcategoriaIdSnapshot = newProduct.SubcategoriaId,
            SubcategoriaNombreSnapshot = newProduct.SubcategoriaId.HasValue
                ? newProduct.Subcategoria!.Nombre
                : "Sin subcategoría",
            SubtotalCentavos = newSubtotal,
            DescuentoCentavos = 0,
            TotalCentavos = newTotal,
            MovimientoInventario = newProductMovement
        };
        Context.DetallesCambioVenta.Add(changeDetail);

        PagoCorreccionVenta? correctionPayment = null;
        Reembolso? refund = null;
        if (difference > 0 && request.CobroAdicional is not null)
        {
            correctionPayment = new PagoCorreccionVenta
            {
                Devolucion = correction,
                VentaId = sale.VentaId,
                UsuarioId = usuarioId,
                CajaId = request.CobroAdicional.MetodoPago == MetodoPago.EFECTIVO ? cash!.CajaId : null,
                MetodoPago = request.CobroAdicional.MetodoPago,
                MontoCentavos = request.CobroAdicional.MontoCentavos,
                Motivo = request.CobroAdicional.Motivo.Trim(),
                Referencia = NormalizeOptional(request.CobroAdicional.Referencia),
                FechaPago = now
            };
            Context.PagosCorreccionVenta.Add(correctionPayment);
        }
        else if (difference < 0 && request.Reembolso is not null)
        {
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

        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CAMBIO_REGISTRADO,
                "devoluciones",
                correction.DevolucionId,
                $"Cambio {numeroCorreccion} registrado para venta {sale.NumeroVenta}. Diferencia {FormatCentavos(difference)}. Motivo: {correction.Motivo}"),
            cancellationToken);

        if (correctionPayment is not null)
        {
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    usuarioId,
                    TipoBitacora.COBRO_CORRECCION_REGISTRADO,
                    "pagos_correccion_venta",
                    correctionPayment.PagoCorreccionVentaId,
                    $"Cobro adicional {correctionPayment.MetodoPago} por {FormatCentavos(correctionPayment.MontoCentavos)} asociado a {numeroCorreccion}."),
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
                    $"Reembolso {refund.MetodoReembolso} por {FormatCentavos(refund.MontoCentavos)} asociado a {numeroCorreccion}."),
                cancellationToken);
        }

        if (RequiresWaste(request.CondicionProductoDevuelto))
        {
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    usuarioId,
                    TipoBitacora.MERMA_REGISTRADA,
                    "mermas",
                    null,
                    $"Merma registrada en cambio {numeroCorreccion} para producto {saleLine.ProductoNombreSnapshot ?? saleLine.Producto.Nombre}."),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new SaleChangeResult(
            correction.DevolucionId,
            correction.NumeroDevolucion,
            returnedValue,
            newTotal,
            difference,
            correctionPayment?.PagoCorreccionVentaId,
            refund?.ReembolsoId);
    }

    private void ValidateRequestShape(RegisterSaleChangeRequest request)
    {
        if (request.VentaId <= 0 || request.DetalleVentaId <= 0)
        {
            throw new ArgumentException("La venta y linea original son obligatorias.", nameof(request));
        }

        if (request.Cantidad <= 0)
        {
            throw new ArgumentException("La cantidad a cambiar debe ser mayor que cero.", nameof(request));
        }

        if (request.ProductoNuevoId <= 0)
        {
            throw new ArgumentException("El producto nuevo es obligatorio.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo del cambio es obligatorio.", nameof(request));
        }
    }

    private void ValidateConditionAndPermissions(RegisterSaleChangeRequest request)
    {
        if (request.CondicionProductoDevuelto is not CondicionProductoDevuelto.BUENO
            and not CondicionProductoDevuelto.DANADO
            and not CondicionProductoDevuelto.NO_APTO
            and not CondicionProductoDevuelto.FALTANTE)
        {
            throw new ArgumentException("La condicion del producto devuelto no es valida.", nameof(request));
        }

        if (request.CondicionProductoDevuelto != CondicionProductoDevuelto.BUENO && request.ReintegraStock)
        {
            throw new InvalidOperationException("Solo un producto en buen estado puede reintegrarse al stock vendible.");
        }

        if (request.ReintegraStock && request.CondicionProductoDevuelto != CondicionProductoDevuelto.BUENO)
        {
            throw new InvalidOperationException("La reintegracion requiere producto en buen estado.");
        }

        var requiresAdmin = request.FueraPolitica
            || request.CondicionProductoDevuelto != CondicionProductoDevuelto.BUENO;

        if (requiresAdmin && !_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("Esta operacion de cambio requiere ADMIN.");
        }
    }

    private void ValidateFinancialDifference(RegisterSaleChangeRequest request, long difference)
    {
        if (difference == 0)
        {
            if (request.CobroAdicional is not null || request.Reembolso is not null)
            {
                throw new InvalidOperationException("Un cambio sin diferencia no debe registrar cobro ni reembolso.");
            }

            return;
        }

        if (difference > 0)
        {
            if (request.Reembolso is not null)
            {
                throw new InvalidOperationException("Un cambio a producto mas caro no debe registrar reembolso.");
            }

            if (request.CobroAdicional is null)
            {
                throw new InvalidOperationException("Debe registrar el cobro completo de la diferencia.");
            }

            ValidateCorrectionPayment(request.CobroAdicional, difference);
            return;
        }

        if (!_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("La devolucion de dinero en un cambio requiere ADMIN.");
        }

        if (request.CobroAdicional is not null)
        {
            throw new InvalidOperationException("Un cambio a producto mas barato no debe registrar cobro adicional.");
        }

        if (request.Reembolso is null)
        {
            throw new InvalidOperationException("Debe registrar la devolucion completa de la diferencia.");
        }

        ValidateRefund(request.Reembolso, -difference);
    }

    private static void ValidateCorrectionPayment(RegisterCorrectionPaymentRequest payment, long expectedAmount)
    {
        if (payment.MontoCentavos != expectedAmount)
        {
            throw new InvalidOperationException("El cobro adicional debe coincidir exactamente con la diferencia.");
        }

        if (payment.MetodoPago is not MetodoPago.EFECTIVO and not MetodoPago.TRANSFERENCIA and not MetodoPago.BITCOIN)
        {
            throw new ArgumentException("El metodo de pago del cobro adicional no es valido.", nameof(payment));
        }

        if (string.IsNullOrWhiteSpace(payment.Motivo))
        {
            throw new ArgumentException("El motivo del cobro adicional es obligatorio.", nameof(payment));
        }

        if ((payment.MetodoPago is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN)
            && string.IsNullOrWhiteSpace(payment.Referencia))
        {
            throw new ArgumentException("La referencia es obligatoria para transferencia o Bitcoin.", nameof(payment));
        }
    }

    private void ValidateRefund(RegisterRefundRequest refund, long expectedAmount)
    {
        if (refund.MontoCentavos != expectedAmount)
        {
            throw new InvalidOperationException("El reembolso debe coincidir exactamente con la diferencia.");
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

        if (refund.MetodoReembolso == MetodoReembolso.BITCOIN && !_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("El reembolso por Bitcoin requiere ADMIN.");
        }
    }

    private async Task<Caja?> GetOpenCashForOperationAsync(
        RegisterSaleChangeRequest request,
        long difference,
        CancellationToken cancellationToken)
    {
        var requiresCash = (difference > 0 && request.CobroAdicional?.MetodoPago == MetodoPago.EFECTIVO)
            || (difference < 0 && request.Reembolso?.MetodoReembolso == MetodoReembolso.EFECTIVO);

        if (!requiresCash)
        {
            return null;
        }

        return await GetOpenCashAsync(cancellationToken)
            ?? throw new InvalidOperationException("La operacion en efectivo requiere caja abierta.");
    }

    private async Task<int> GetAvailableQuantityAsync(
        int detalleVentaId,
        int soldQuantity,
        CancellationToken cancellationToken)
    {
        var processed = await Context.DetalleDevoluciones
            .Where(d => d.DetalleVentaId == detalleVentaId)
            .SumAsync(d => (int?)d.Cantidad, cancellationToken) ?? 0;

        return Math.Max(0, soldQuantity - processed);
    }

    private async Task<string> GenerateNumeroCorreccionAsync(string prefix, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var number = $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            var exists = await Context.Devoluciones.AnyAsync(d => d.NumeroDevolucion == number, cancellationToken);
            if (!exists)
            {
                return number;
            }

            await Task.Delay(5, cancellationToken);
        }

        throw new InvalidOperationException("No se pudo generar numero unico de correccion.");
    }

    private Task<Caja?> GetOpenCashAsync(CancellationToken cancellationToken) =>
        CashRegisterStateQueries.GetCurrentOpenCashAsync(Context, cancellationToken);

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para registrar cambios.");
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

    private static ProductoSku ResolveNewSku(Producto product, int? requestedSkuId)
    {
        var active = product.Skus.Where(sku => sku.Activo).ToList();
        if (requestedSkuId.HasValue)
            return active.SingleOrDefault(sku => sku.ProductoSkuId == requestedSkuId.Value)
                ?? throw new InvalidOperationException("El SKU nuevo no pertenece al producto o esta inactivo.");
        if (product.Modo == ModoProducto.POR_VARIANTES && active.Count > 1)
            throw new InvalidOperationException($"Debe seleccionar el SKU exacto para '{product.Nombre}'.");
        return active.FirstOrDefault(sku => sku.EsPrincipal)
            ?? active.SingleOrDefault()
            ?? throw new InvalidOperationException($"El producto '{product.Nombre}' no tiene un SKU activo.");
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
