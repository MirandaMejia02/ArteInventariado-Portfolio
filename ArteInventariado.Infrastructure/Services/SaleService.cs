using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SaleService : ISaleService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly CreditService _creditService;
    private readonly IInstallationContext? _installation;

    [ActivatorUtilitiesConstructor]
    public SaleService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        IEmployeePermissionService permissionService,
        CreditService creditService,
        IInstallationContext? installation = null)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
        _permissionService = permissionService;
        _creditService = creditService;
        _installation = installation;
    }

    // Conservado para fixtures unitarios que comparten una conexion SQLite en memoria.
    internal SaleService(
        AppDbContext context,
        ICurrentSession currentSession,
        IAuditService auditService,
        IEmployeePermissionService permissionService,
        CreditService creditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
        _permissionService = permissionService;
        _creditService = creditService;
    }

    public async Task<SaleResult> RegisterSaleAsync(
        RegisterSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequestShape(request);
        _installation?.EnsureOperationalWriteAllowed("Registrar venta");
        await EnsureSalePermissionsAsync(request, cancellationToken);

        try
        {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var usuarioId = GetCurrentUserId();
        var usuario = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.UsuarioId == usuarioId && u.Activo)
            .Select(u => new { u.UsuarioId, u.NombreCompleto })
            .SingleOrDefaultAsync(cancellationToken);
        if (usuario is null)
        {
            throw new InvalidOperationException("El usuario de la sesion no existe o esta inactivo.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var caja = await GetOpenCashAsync(context, cancellationToken);
        var isCreditSale = request.Credit is not null;
        var customer = await GetSaleCustomerAsync(context, request.ClienteId, cancellationToken);
        if (isCreditSale && (customer is null || request.Credit!.ClienteId != customer.ClienteId))
        {
            throw new InvalidOperationException("La venta a credito requiere un cliente real seleccionado.");
        }

        var payments = NormalizePayments(request.Payments, allowEmpty: isCreditSale);
        foreach (var payment in payments)
        {
            await PaymentMethodAvailability.EnsureAvailableAsync(context, payment.MetodoPago, cancellationToken);
        }
        var productIds = request.Lines.Select(l => l.ProductoId).Distinct().ToList();

        var productos = await context.Productos
            .Include(p => p.Categoria)
            .Include(p => p.Subcategoria)
            .Include(p => p.Skus)
                .ThenInclude(sku => sku.SaldosInventario)
            .Where(p => productIds.Contains(p.ProductoId))
            .ToDictionaryAsync(p => p.ProductoId, cancellationToken);
        var costOrigins = await HistoricalProductSnapshotResolver.ResolveCostOriginsAsync(
            context,
            productos.Values.ToList(),
            cancellationToken);

        var numeroVenta = await GenerateNumeroVentaAsync(context, cancellationToken);
        var fechaVenta = DateTime.UtcNow;
        var detalleDrafts = BuildLineDrafts(request.Lines, productos);
        var subtotal = detalleDrafts.Sum(d => d.SubtotalCentavos);
        var descuentoLineas = detalleDrafts.Sum(d => d.DescuentoCentavos);

        ValidateGeneralDiscount(request.DescuentoGeneralCentavos, request.DescuentoGeneralMotivo, subtotal - descuentoLineas);

        var descuentoTotal = descuentoLineas + request.DescuentoGeneralCentavos;
        var total = subtotal - descuentoTotal;
        var totalPagado = payments.Sum(p => p.MontoCentavos);
        var totalEfectivoRecibido = payments
            .Where(p => p.MetodoPago == MetodoPago.EFECTIVO)
            .Sum(p => p.MontoCentavos);
        var generalDiscountAllocations = ProportionalAmountAllocator.Allocate(
            request.DescuentoGeneralCentavos,
            detalleDrafts.Select(d => d.TotalCentavos).ToList());
        var creditPlan = request.Credit is null
            ? null
            : await BuildCreditPlanAsync(
                context,
                request.Credit,
                customer!,
                detalleDrafts,
                generalDiscountAllocations,
                total,
                totalPagado,
                cancellationToken);

        if (creditPlan is null && totalPagado < total)
        {
            throw new InvalidOperationException("El total pagado debe ser mayor o igual al total final de la venta.");
        }

        if (creditPlan is not null && totalPagado > total)
        {
            throw new InvalidOperationException("El pago inicial no puede superar el total final de la venta a credito.");
        }

        var cambio = creditPlan is null ? totalPagado - total : 0;
        if (cambio > 0 && totalEfectivoRecibido <= 0)
        {
            throw new InvalidOperationException("El vuelto solo puede generarse desde un pago en efectivo.");
        }

        if (cambio > totalEfectivoRecibido)
        {
            throw new InvalidOperationException("El vuelto no puede superar el monto recibido en efectivo.");
        }

        var effectivePayments = BuildStoredPayments(payments, cambio);

        var venta = new Venta
        {
            NumeroVenta = numeroVenta,
            CajaId = caja.CajaId,
            UsuarioId = usuarioId,
            UsuarioNombreSnapshot = usuario.NombreCompleto,
            ClienteId = customer?.ClienteId,
            ClienteNombreSnapshot = customer?.NombreCompleto ?? "Consumidor final",
            FechaVenta = fechaVenta,
            SubtotalCentavos = subtotal,
            DescuentoCentavos = descuentoTotal,
            DescuentoMotivo = NormalizeOptional(request.DescuentoGeneralMotivo),
            TotalCentavos = total,
            PagadoCentavos = totalPagado,
            CambioCentavos = cambio,
            Estado = EstadoVenta.COMPLETADA,
            Observaciones = NormalizeOptional(request.Observaciones)
        };

        context.Ventas.Add(venta);
        await context.SaveChangesAsync(cancellationToken);

        var storedDetails = new List<(SaleLineDraft Draft, DetalleVenta Detalle)>();
        foreach (var draft in detalleDrafts)
        {
            var detalle = new DetalleVenta
            {
                VentaId = venta.VentaId,
                ProductoId = draft.Producto.ProductoId,
                ProductoSkuId = draft.Sku.ProductoSkuId,
                Cantidad = draft.Cantidad,
                PrecioUnitarioCentavos = draft.PrecioUnitarioCentavos,
                CostoUnitarioCentavos = draft.Sku.ResolverCostoCentavos(),
                OrigenCosto = draft.Sku.CostoOverrideCentavos.HasValue
                    ? OrigenCostoHistorico.SKU_OVERRIDE
                    : costOrigins[draft.Producto.ProductoId],
                ProductoCodigoSnapshot = draft.Producto.CodigoInterno,
                ProductoNombreSnapshot = draft.Producto.Nombre,
                CodigoSkuSnapshot = draft.Sku.CodigoSku,
                NombreVarianteSnapshot = draft.Sku.NombreVariante,
                CategoriaIdSnapshot = draft.Producto.CategoriaId,
                CategoriaNombreSnapshot = draft.Producto.Categoria.Nombre,
                SubcategoriaIdSnapshot = draft.Producto.SubcategoriaId,
                SubcategoriaNombreSnapshot = draft.Producto.SubcategoriaId.HasValue
                    ? draft.Producto.Subcategoria!.Nombre
                    : "Sin subcategoría",
                SubtotalCentavos = draft.SubtotalCentavos,
                DescuentoCentavos = draft.DescuentoCentavos,
                DescuentoGeneralProporcionalCentavos = generalDiscountAllocations[draft.Index],
                DescuentoMotivo = NormalizeOptional(draft.DescuentoMotivo),
                TotalCentavos = draft.TotalCentavos
            };

            var stockAnterior = InventoryStockEngine.GetPhysicalTotal(draft.Producto);
            InventoryStockEngine.RemovePhysical(context, draft.Sku, draft.Cantidad, usuarioId,
                TipoOperacionInventario.SALIDA_VENTA, $"Venta {venta.NumeroVenta}", fechaVenta,
                origin: UbicacionInventario.EXHIBICION);
            var stockNuevo = InventoryStockEngine.GetPhysicalTotal(draft.Producto);

            context.DetalleVentas.Add(detalle);
            storedDetails.Add((draft, detalle));
            context.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = draft.Producto.ProductoId,
                VentaId = venta.VentaId,
                UsuarioId = usuarioId,
                TipoMovimiento = TipoMovimientoInventario.SALIDA,
                Cantidad = -draft.Cantidad,
                StockAnterior = stockAnterior,
                StockNuevo = stockNuevo,
                Motivo = $"Venta {venta.NumeroVenta}",
                FechaMovimiento = fechaVenta
            });
        }

        foreach (var payment in effectivePayments)
        {
            context.PagosVenta.Add(new PagoVenta
            {
                VentaId = venta.VentaId,
                MetodoPago = payment.MetodoPago,
                MontoCentavos = payment.MontoCentavos,
                Referencia = NormalizeOptional(payment.Referencia),
                FechaPago = fechaVenta
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        if (creditPlan is not null)
        {
            await PersistCreditAccountAsync(
                context,
                creditPlan,
                venta,
                customer!,
                caja.CajaId,
                usuarioId,
                storedDetails.ToDictionary(d => d.Draft.Index, d => d.Detalle.DetalleVentaId),
                cancellationToken);
        }

        await AddAuditAsync(
            context,
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.VENTA_REALIZADA,
                "ventas",
                venta.VentaId,
                $"Venta {venta.NumeroVenta} registrada por {FormatCentavos(total)}. Pago inmediato {FormatCentavos(totalPagado)}. Cambio {FormatCentavos(cambio)}. Metodos: {FormatPaymentMethods(effectivePayments)}{(creditPlan is null ? string.Empty : $". Credito: {FormatCentavos(creditPlan.TotalCuentaCentavos)}.")}"),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await GetSaleResultAsync(context, venta.VentaId, totalPagado, cambio, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            LogSalePersistenceException(ex);
            throw new InvalidOperationException(
                "El inventario cambió mientras se confirmaba la venta. Actualice el POS e inténtelo nuevamente.", ex);
        }
        catch (DbUpdateException ex) when (IsSqliteBusy(ex))
        {
            LogSalePersistenceException(ex);
            throw new InvalidOperationException(
                "El inventario cambió o está siendo actualizado por otra operación. Actualice el POS e inténtelo nuevamente.", ex);
        }
        catch (DbUpdateException ex)
        {
            LogSalePersistenceException(ex);
            throw;
        }
        catch (SqliteException ex) when (IsSqliteBusy(ex))
        {
            throw new InvalidOperationException(
                "El inventario cambió o está siendo actualizado por otra operación. Actualice el POS e inténtelo nuevamente.", ex);
        }
    }

    private async Task EnsureSalePermissionsAsync(
        RegisterSaleRequest request,
        CancellationToken cancellationToken)
    {
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.POS_ACCEDER, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.VENTA_REGISTRAR, cancellationToken);

        if (request.Lines.Any(l => l.DescuentoCentavos > 0))
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.DESCUENTO_LINEA_APLICAR, cancellationToken);
        }

        if (request.DescuentoGeneralCentavos > 0)
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.DESCUENTO_GENERAL_APLICAR, cancellationToken);
        }

        if (request.Payments.Any(p => p.MetodoPago == MetodoPago.TRANSFERENCIA && p.MontoCentavos > 0))
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PAGO_TRANSFERENCIA_USAR, cancellationToken);
        }

        if (request.Payments.Any(p => p.MetodoPago == MetodoPago.BITCOIN && p.MontoCentavos > 0))
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PAGO_BITCOIN_USAR, cancellationToken);
        }

        if (request.Credit is not null)
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER, cancellationToken);
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CREDITO_REGISTRAR, cancellationToken);
        }
    }

    private static void ValidateRequestShape(RegisterSaleRequest request)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("La venta debe tener al menos un producto.", nameof(request));
        }

        if (request.Payments.Count == 0 && request.Credit is null)
        {
            throw new ArgumentException("La venta debe tener al menos un pago.", nameof(request));
        }

        foreach (var line in request.Lines)
        {
            if (line.Cantidad <= 0)
            {
                throw new ArgumentException("La cantidad vendida debe ser mayor que cero.", nameof(request));
            }

            if (line.DescuentoCentavos < 0)
            {
                throw new ArgumentException("El descuento por linea no puede ser negativo.", nameof(request));
            }

            if (line.DescuentoCentavos > 0 && string.IsNullOrWhiteSpace(line.DescuentoMotivo))
            {
                throw new ArgumentException("Todo descuento por linea requiere motivo.", nameof(request));
            }
        }

        foreach (var payment in request.Payments)
        {
            if (payment.MetodoPago is not MetodoPago.EFECTIVO and not MetodoPago.TRANSFERENCIA and not MetodoPago.BITCOIN)
            {
                throw new ArgumentException("Metodo de pago no valido para V1.", nameof(request));
            }

            if (payment.MontoCentavos < 0)
            {
                throw new ArgumentException("El monto pagado no puede ser negativo.", nameof(request));
            }
        }

        if (request.Credit is not null)
        {
            if (request.Credit.ClienteId <= 0)
            {
                throw new ArgumentException("La venta a credito requiere cliente real.", nameof(request));
            }

            if (request.Credit.PlazoDias <= 0)
            {
                throw new ArgumentException("El plazo de credito debe ser mayor que cero.", nameof(request));
            }

            if (request.Credit.Lines.Count == 0)
            {
                throw new ArgumentException("Debe seleccionar al menos un producto para credito.", nameof(request));
            }

            foreach (var creditLine in request.Credit.Lines)
            {
                if (creditLine.ProductoId <= 0 || creditLine.CantidadFinanciada <= 0)
                {
                    throw new ArgumentException("Las cantidades financiadas deben ser mayores que cero.", nameof(request));
                }
            }
        }
    }

    private static IReadOnlyList<SalePaymentRequest> NormalizePayments(
        IReadOnlyList<SalePaymentRequest> payments,
        bool allowEmpty)
    {
        var normalized = payments
            .Where(p => p.MontoCentavos > 0)
            .ToList();

        if (normalized.Count == 0 && !allowEmpty)
        {
            throw new ArgumentException("Debe registrar al menos un pago mayor que cero.", nameof(payments));
        }

        return normalized;
    }

    private static IReadOnlyList<SalePaymentRequest> BuildStoredPayments(
        IReadOnlyList<SalePaymentRequest> payments,
        long cambioCentavos)
    {
        var storedPayments = new List<SalePaymentRequest>();
        var efectivoRecibido = payments
            .Where(p => p.MetodoPago == MetodoPago.EFECTIVO)
            .Sum(p => p.MontoCentavos);
        var efectivoNeto = efectivoRecibido - cambioCentavos;

        if (efectivoNeto > 0)
        {
            storedPayments.Add(new SalePaymentRequest(MetodoPago.EFECTIVO, efectivoNeto));
        }

        storedPayments.AddRange(payments
            .Where(p => p.MetodoPago is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN)
            .Select(p => new SalePaymentRequest(p.MetodoPago, p.MontoCentavos, p.Referencia)));

        return storedPayments;
    }

    private static List<SaleLineDraft> BuildLineDrafts(
        IReadOnlyList<SaleLineRequest> lines,
        IReadOnlyDictionary<int, Producto> productos)
    {
        var drafts = new List<SaleLineDraft>();

        var index = 0;
        foreach (var line in lines)
        {
            if (!productos.TryGetValue(line.ProductoId, out var producto))
            {
                throw new InvalidOperationException($"El producto con id {line.ProductoId} no existe.");
            }

            if (producto.Estado != EstadoProducto.ACTIVO)
            {
                throw new InvalidOperationException($"El producto '{producto.Nombre}' no esta activo.");
            }

            var sku = ResolveSaleSku(producto, line.ProductoSkuId);
            var exhibition = sku.SaldosInventario.SingleOrDefault(balance =>
                balance.Ubicacion == UbicacionInventario.EXHIBICION);
            var availableStock = exhibition?.Disponible ?? 0;
            if (availableStock < line.Cantidad)
            {
                var warehouse = sku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
                    .Sum(balance => balance.Disponible);
                throw new InvalidOperationException(
                    $"Stock insuficiente en Exhibición para '{producto.Nombre}' ({sku.CodigoSku}). " +
                    $"Disponible: {availableStock}. En Bodega: {warehouse}. Bodega no se consume automáticamente.");
            }

            var effectivePrice = sku.ResolverPrecioVentaCentavos();
            var subtotal = effectivePrice * line.Cantidad;
            if (line.DescuentoCentavos > subtotal)
            {
                throw new InvalidOperationException($"El descuento de '{producto.Nombre}' no puede superar el subtotal de la linea.");
            }

            drafts.Add(new SaleLineDraft(
                index,
                producto,
                sku,
                line.Cantidad,
                effectivePrice,
                subtotal,
                line.DescuentoCentavos,
                line.DescuentoMotivo,
                subtotal - line.DescuentoCentavos));
            index++;
        }

        foreach (var group in drafts.GroupBy(d => d.Sku.ProductoSkuId))
        {
            var draft = group.First();
            var cantidadTotal = group.Sum(d => d.Cantidad);
            var availableStock = draft.Sku.SaldosInventario
                .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                .Sum(balance => balance.Disponible);
            if (availableStock < cantidadTotal)
            {
                throw new InvalidOperationException(
                    $"Stock insuficiente en Exhibición para '{draft.Producto.Nombre}' ({draft.Sku.CodigoSku}). Disponible: {availableStock}.");
            }
        }

        return drafts;
    }

    private static ProductoSku ResolveSaleSku(Producto producto, int? requestedSkuId)
    {
        if (producto.Modo == ModoProducto.POR_VARIANTES && !requestedSkuId.HasValue)
        {
            throw new InvalidOperationException(
                $"Debe seleccionar el SKU o diseño exacto para '{producto.Nombre}'.");
        }

        var sku = requestedSkuId.HasValue
            ? producto.Skus.SingleOrDefault(item => item.ProductoSkuId == requestedSkuId.Value)
            : producto.Skus.SingleOrDefault(item => item.EsPrincipal)
                ?? producto.Skus.SingleOrDefault();

        if (sku is null)
        {
            throw new InvalidOperationException($"El producto '{producto.Nombre}' no tiene un SKU válido para venta.");
        }

        if (sku.ProductoId != producto.ProductoId)
        {
            throw new InvalidOperationException("El SKU seleccionado no pertenece al producto indicado.");
        }

        if (!sku.Activo)
        {
            throw new InvalidOperationException($"El SKU '{sku.CodigoSku}' está inactivo.");
        }

        if (producto.Modo is ModoProducto.SIMPLE or ModoProducto.SURTIDO && !sku.EsPrincipal)
        {
            throw new InvalidOperationException(
                $"El producto '{producto.Nombre}' solo puede venderse mediante su SKU principal.");
        }

        return sku;
    }

    private static void ValidateGeneralDiscount(long descuentoGeneral, string? motivo, long subtotalAfterLineDiscounts)
    {
        if (descuentoGeneral < 0)
        {
            throw new ArgumentException("El descuento general no puede ser negativo.", nameof(descuentoGeneral));
        }

        if (descuentoGeneral > 0 && string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("Todo descuento general requiere motivo.", nameof(motivo));
        }

        if (descuentoGeneral > subtotalAfterLineDiscounts)
        {
            throw new InvalidOperationException("El descuento general no puede superar el subtotal despues de descuentos por linea.");
        }
    }

    private async Task<CreditPlan> BuildCreditPlanAsync(
        AppDbContext context,
        CreditSaleRequest request,
        Cliente customer,
        IReadOnlyList<SaleLineDraft> drafts,
        IReadOnlyList<long> generalDiscountAllocations,
        long saleTotalCentavos,
        long immediatePaymentCentavos,
        CancellationToken cancellationToken)
    {
        var selectedBySku = new Dictionary<int, int>();
        foreach (var selectedLine in request.Lines)
        {
            var candidates = drafts
                .Where(d => d.Producto.ProductoId == selectedLine.ProductoId &&
                    (!selectedLine.ProductoSkuId.HasValue ||
                        d.Sku.ProductoSkuId == selectedLine.ProductoSkuId.Value))
                .ToList();
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("No se puede financiar un producto que no esta en la venta.");
            }

            var distinctSkuIds = candidates.Select(d => d.Sku.ProductoSkuId).Distinct().ToList();
            if (distinctSkuIds.Count != 1)
            {
                throw new InvalidOperationException(
                    "Debe indicar el SKU exacto de cada producto financiado cuando hay variantes.");
            }

            var skuId = distinctSkuIds[0];
            selectedBySku[skuId] = selectedBySku.GetValueOrDefault(skuId) + selectedLine.CantidadFinanciada;
        }

        foreach (var (skuId, selectedQuantity) in selectedBySku)
        {
            var soldQuantity = drafts.Where(d => d.Sku.ProductoSkuId == skuId).Sum(d => d.Cantidad);
            if (selectedQuantity > soldQuantity)
            {
                throw new InvalidOperationException("La cantidad financiada no puede superar la cantidad vendida.");
            }
        }

        var creditLines = new List<CreditLinePlan>();
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            if (!selectedBySku.TryGetValue(draft.Sku.ProductoSkuId, out var remaining) || remaining <= 0)
            {
                continue;
            }

            var financedQuantity = Math.Min(remaining, draft.Cantidad);
            selectedBySku[draft.Sku.ProductoSkuId] = remaining - financedQuantity;
            var lineTotalAfterGeneral = draft.TotalCentavos - generalDiscountAllocations[i];
            var financedSubtotal = financedQuantity == draft.Cantidad
                ? lineTotalAfterGeneral
                : lineTotalAfterGeneral * financedQuantity / draft.Cantidad;
            var lineDiscountPart = draft.DescuentoCentavos * financedQuantity / draft.Cantidad;
            var generalDiscountPart = generalDiscountAllocations[i] * financedQuantity / draft.Cantidad;

            creditLines.Add(new CreditLinePlan(
                draft.Index,
                draft.Producto.ProductoId,
                draft.Producto.CodigoInterno,
                draft.Producto.Nombre,
                financedQuantity,
                draft.PrecioUnitarioCentavos,
                lineDiscountPart,
                generalDiscountPart,
                financedSubtotal,
                0));
        }

        var selectedCreditSubtotal = creditLines.Sum(l => l.SubtotalFinanciadoCentavos);
        if (selectedCreditSubtotal <= 0)
        {
            throw new InvalidOperationException("El monto seleccionado para credito debe ser mayor que cero.");
        }

        var requiredCashPortion = saleTotalCentavos - selectedCreditSubtotal;
        if (immediatePaymentCentavos < requiredCashPortion)
        {
            var missing = requiredCashPortion - immediatePaymentCentavos;
            throw new InvalidOperationException($"El pago inmediato no cubre la parte de contado. Faltante: {FormatCentavos(missing)}.");
        }

        var initialCreditPayment = immediatePaymentCentavos - requiredCashPortion;
        var financedCapital = selectedCreditSubtotal - initialCreditPayment;
        if (financedCapital <= 0)
        {
            throw new InvalidOperationException("La cuenta por cobrar debe tener capital pendiente mayor que cero.");
        }

        var policy = await _creditService.BuildEffectivePolicyAsync(context, customer, cancellationToken);
        var interest = CalculateInterest(financedCapital, policy.InterestPercent);
        var accountTotal = financedCapital + interest;

        await _creditService.ValidateCreditEligibilityAsync(
            context,
            customer,
            policy,
            accountTotal,
            request.PlazoDias,
            cancellationToken);

        var capitalAllocations = ProportionalAmountAllocator.Allocate(
            financedCapital,
            creditLines.Select(l => l.SubtotalFinanciadoCentavos).ToList());
        creditLines = creditLines
            .Select((line, index) => line with { CapitalPendienteCentavos = capitalAllocations[index] })
            .ToList();

        return new CreditPlan(
            request.PlazoDias,
            NormalizeOptional(request.Observaciones),
            requiredCashPortion,
            initialCreditPayment,
            financedCapital,
            policy.InterestPercent,
            interest,
            accountTotal,
            DateTime.UtcNow.AddDays(request.PlazoDias),
            creditLines);
    }

    private async Task PersistCreditAccountAsync(
        AppDbContext context,
        CreditPlan plan,
        Venta venta,
        Cliente customer,
        int cajaId,
        int usuarioId,
        IReadOnlyDictionary<int, int> detalleVentaIdsByDraftIndex,
        CancellationToken cancellationToken)
    {
        var account = new CuentaPorCobrar
        {
            CodigoCuenta = await _creditService.GenerateAccountCodeAsync(context, cancellationToken),
            VentaId = venta.VentaId,
            ClienteId = customer.ClienteId,
            CajaId = cajaId,
            UsuarioRegistroId = usuarioId,
            CapitalFinanciadoCentavos = plan.CapitalFinanciadoCentavos,
            InteresPorcentaje = plan.InteresPorcentaje,
            InteresCalculadoCentavos = plan.InteresCalculadoCentavos,
            TotalOriginalCentavos = plan.TotalCuentaCentavos,
            TotalPagadoCentavos = 0,
            SaldoActualCentavos = plan.TotalCuentaCentavos,
            FechaCreacion = DateTime.UtcNow,
            FechaVencimiento = plan.FechaVencimiento,
            Estado = EstadoCuentaPorCobrar.ABIERTA,
            Observaciones = plan.Observaciones,
            FechaActualizacion = DateTime.UtcNow
        };

        context.CuentasPorCobrar.Add(account);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var line in plan.Lines)
        {
            if (!detalleVentaIdsByDraftIndex.TryGetValue(line.DraftIndex, out var detalleVentaId))
            {
                throw new InvalidOperationException("No se pudo vincular el detalle financiado con la venta.");
            }

            context.CuentasPorCobrarDetalle.Add(new CuentaPorCobrarDetalle
            {
                CuentaPorCobrarId = account.CuentaPorCobrarId,
                DetalleVentaId = detalleVentaId,
                ProductoId = line.ProductoId,
                ProductoCodigoSnapshot = line.ProductoCodigo,
                ProductoNombreSnapshot = line.ProductoNombre,
                CantidadFinanciada = line.CantidadFinanciada,
                PrecioUnitarioCentavos = line.PrecioUnitarioCentavos,
                DescuentoLineaCentavos = line.DescuentoLineaCentavos,
                DescuentoGeneralProporcionalCentavos = line.DescuentoGeneralProporcionalCentavos,
                SubtotalFinanciadoCentavos = line.SubtotalFinanciadoCentavos,
                CapitalPendienteCentavos = line.CapitalPendienteCentavos
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(
            context,
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CUENTA_COBRAR_CREADA,
                "cuentas_por_cobrar",
                account.CuentaPorCobrarId,
                $"Cuenta {account.CodigoCuenta} creada para venta {venta.NumeroVenta}. Cliente {customer.CodigoCliente}. Capital {FormatCentavos(account.CapitalFinanciadoCentavos)}. Interes {account.InteresPorcentaje:0.####}% = {FormatCentavos(account.InteresCalculadoCentavos)}. Plazo {plan.PlazoDias} dias. Vence {account.FechaVencimiento:yyyy-MM-dd}. Pago inicial credito {FormatCentavos(plan.AbonoInicialCreditoCentavos)}."),
            cancellationToken);
    }

    private static long CalculateInterest(long capitalCentavos, decimal interestPercent)
    {
        return (long)Math.Round(capitalCentavos * interestPercent / 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static async Task<Caja> GetOpenCashAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var caja = await CashRegisterStateQueries.GetCurrentOpenCashAsync(context, cancellationToken);

        return caja ?? throw new InvalidOperationException("No hay caja abierta para registrar la venta.");
    }

    private static async Task<Cliente?> GetSaleCustomerAsync(
        AppDbContext context,
        int? clienteId,
        CancellationToken cancellationToken)
    {
        if (clienteId is null)
        {
            return null;
        }

        var customer = await context.Clientes
            .FirstOrDefaultAsync(c => c.ClienteId == clienteId.Value, cancellationToken);

        if (customer is null)
        {
            throw new InvalidOperationException("El cliente seleccionado no existe.");
        }

        if (customer.Estado != EstadoCliente.ACTIVO)
        {
            throw new InvalidOperationException("No se puede asociar un cliente inactivo a una venta nueva.");
        }

        return customer;
    }

    private static async Task<string> GenerateNumeroVentaAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var prefix = $"VTA-{DateTime.Now:yyyyMMdd}-";
        var countToday = await context.Ventas
            .CountAsync(v => v.NumeroVenta.StartsWith(prefix), cancellationToken);

        var next = countToday + 1;

        while (true)
        {
            var numeroVenta = $"{prefix}{next:0000}";
            var exists = await context.Ventas
                .AnyAsync(v => v.NumeroVenta == numeroVenta, cancellationToken);

            if (!exists)
            {
                return numeroVenta;
            }

            next++;
        }
    }

    private async Task<SaleResult> GetSaleResultAsync(
        AppDbContext context,
        int ventaId,
        long totalPagado,
        long cambio,
        CancellationToken cancellationToken)
    {
        var venta = await context.Ventas
            .AsNoTracking()
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .Include(v => v.Pagos)
            .Include(v => v.CuentaPorCobrar)
            .ThenInclude(c => c!.Detalles)
            .FirstAsync(v => v.VentaId == ventaId, cancellationToken);

        var lines = venta.Detalles
            .OrderBy(d => d.DetalleVentaId)
            .Select(d => new SaleLineResult(
                d.DetalleVentaId,
                d.ProductoId,
                d.ProductoCodigoSnapshot ?? d.Producto.CodigoInterno,
                d.ProductoNombreSnapshot ?? d.Producto.Nombre,
                d.Cantidad,
                d.PrecioUnitarioCentavos,
                d.SubtotalCentavos,
                d.DescuentoCentavos,
                d.TotalCentavos,
                d.ProductoSkuId,
                d.CodigoSkuSnapshot,
                d.NombreVarianteSnapshot))
            .ToList();

        var payments = venta.Pagos
            .OrderBy(p => p.PagoVentaId)
            .Select(p => new SalePaymentResult(
                p.PagoVentaId,
                p.MetodoPago,
                p.MontoCentavos,
                p.Referencia))
            .ToList();

        SaleCreditResult? credit = null;
        if (venta.CuentaPorCobrar is not null)
        {
            var creditLines = venta.CuentaPorCobrar.Detalles
                .OrderBy(d => d.CuentaPorCobrarDetalleId)
                .Select(d => new SaleCreditLineResult(
                    d.CuentaPorCobrarDetalleId,
                    d.DetalleVentaId,
                    d.ProductoId,
                    d.ProductoCodigoSnapshot,
                    d.ProductoNombreSnapshot,
                    d.CantidadFinanciada,
                    d.SubtotalFinanciadoCentavos,
                    d.CapitalPendienteCentavos))
                .ToList();

            credit = new SaleCreditResult(
                venta.CuentaPorCobrar.CuentaPorCobrarId,
                venta.CuentaPorCobrar.CodigoCuenta,
                Math.Max(0, venta.TotalCentavos - venta.CuentaPorCobrar.Detalles.Sum(d => d.SubtotalFinanciadoCentavos)),
                Math.Max(0, totalPagado - Math.Max(0, venta.TotalCentavos - venta.CuentaPorCobrar.Detalles.Sum(d => d.SubtotalFinanciadoCentavos))),
                venta.CuentaPorCobrar.CapitalFinanciadoCentavos,
                venta.CuentaPorCobrar.InteresPorcentaje,
                venta.CuentaPorCobrar.InteresCalculadoCentavos,
                venta.CuentaPorCobrar.TotalOriginalCentavos,
                venta.CuentaPorCobrar.SaldoActualCentavos,
                venta.CuentaPorCobrar.FechaVencimiento,
                creditLines);
        }

        return new SaleResult(
            venta.VentaId,
            venta.NumeroVenta,
            venta.CajaId,
            venta.UsuarioId,
            venta.ClienteId,
            venta.ClienteNombreSnapshot,
            venta.FechaVenta,
            venta.SubtotalCentavos,
            venta.DescuentoCentavos,
            venta.TotalCentavos,
            totalPagado,
            cambio,
            lines,
            payments,
            credit);
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para registrar ventas.");
    }

    private async Task AddAuditAsync(
        AppDbContext context,
        AuditEntryRequest request,
        CancellationToken cancellationToken)
    {
        if (_contextFactory is null)
        {
            await _auditService.RegistrarAsync(request, cancellationToken);
            return;
        }

        context.Bitacora.Add(AuditEntryFactory.Create(request));
        await context.SaveChangesAsync(cancellationToken);
    }

    private ValueTask<AppDbContextLease> CreateContextAsync(CancellationToken cancellationToken) =>
        AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FormatCentavos(long centavos)
    {
        return (centavos / 100m).ToString("0.00");
    }

    private static string FormatPaymentMethods(IEnumerable<SalePaymentRequest> payments)
    {
        return string.Join(", ", payments
            .OrderBy(p => p.MetodoPago)
            .Select(p => p.MetodoPago.ToString())
            .Distinct());
    }

    private static void LogSalePersistenceException(DbUpdateException ex)
    {
        var baseException = ex.GetBaseException();
        var logMessage = string.Join(
            Environment.NewLine,
            $"[{DateTime.Now:O}] DbUpdateException while registering sale.",
            $"BaseException: {baseException.Message}",
            "Exception:",
            ex.ToString(),
            string.Empty);

        System.Diagnostics.Trace.TraceError(logMessage);

        try
        {
            var logDirectory = ArteInventariadoPathProvider.CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "ventas.log"), logMessage);
        }
        catch
        {
            System.Diagnostics.Trace.TraceError("Could not write sale persistence technical log.");
        }
    }

    private static bool IsSqliteBusy(Exception exception)
    {
        var sqlite = exception as SqliteException ?? exception.GetBaseException() as SqliteException;
        return sqlite?.SqliteErrorCode is 5 or 6;
    }

    private sealed record SaleLineDraft(
        int Index,
        Producto Producto,
        ProductoSku Sku,
        int Cantidad,
        long PrecioUnitarioCentavos,
        long SubtotalCentavos,
        long DescuentoCentavos,
        string? DescuentoMotivo,
        long TotalCentavos);

    private sealed record CreditPlan(
        int PlazoDias,
        string? Observaciones,
        long MontoContadoObligatorioCentavos,
        long AbonoInicialCreditoCentavos,
        long CapitalFinanciadoCentavos,
        decimal InteresPorcentaje,
        long InteresCalculadoCentavos,
        long TotalCuentaCentavos,
        DateTime FechaVencimiento,
        IReadOnlyList<CreditLinePlan> Lines);

    private sealed record CreditLinePlan(
        int DraftIndex,
        int ProductoId,
        string ProductoCodigo,
        string ProductoNombre,
        int CantidadFinanciada,
        long PrecioUnitarioCentavos,
        long DescuentoLineaCentavos,
        long DescuentoGeneralProporcionalCentavos,
        long SubtotalFinanciadoCentavos,
        long CapitalPendienteCentavos);
}
