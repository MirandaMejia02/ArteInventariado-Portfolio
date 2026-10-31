using System.Security.Cryptography;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomOrderFinanceService : ICustomOrderFinanceService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _session;
    private readonly IEmployeePermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly CreditService _creditService;

    [ActivatorUtilitiesConstructor]
    public CustomOrderFinanceService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession session,
        IEmployeePermissionService permissions,
        IAuditService audit,
        CreditService creditService)
    {
        _contextFactory = contextFactory;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _creditService = creditService;
    }

    internal CustomOrderFinanceService(AppDbContext context, ICurrentSession session, IEmployeePermissionService permissions, IAuditService audit, CreditService creditService)
    {
        _legacyContext = context;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _creditService = creditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion financiera del pedido.");

    public async Task<CustomOrderPaymentResult> RegisterPaymentAsync(
        CustomOrderPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_PAGO_REGISTRAR, cancellationToken);
        ValidatePaymentInput(request);

        var existing = await Context.PagosPedidoPersonalizado
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OperacionId == request.OperacionId, cancellationToken);
        if (existing is not null)
        {
            if (existing.PedidoPersonalizadoId != request.PedidoPersonalizadoId ||
                existing.TipoPago != request.TipoPago ||
                existing.MetodoPago != request.MetodoPago ||
                existing.MontoCentavos != request.MontoCentavos)
            {
                throw new InvalidOperationException("El identificador de operacion ya fue usado con datos diferentes.");
            }

            return await GetPaymentResultAsync(existing.PagoPedidoPersonalizadoId, cancellationToken);
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await PaymentMethodAvailability.EnsureAvailableAsync(Context, request.MetodoPago, cancellationToken);
        var order = await Context.PedidosPersonalizados
            .FirstOrDefaultAsync(p => p.PedidoPersonalizadoId == request.PedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");
        EnsureCanReceivePayment(order);

        var currentNet = order.TotalPagadoCentavos - order.TotalReembolsadoCentavos;
        var balance = Math.Max(0, order.TotalEstimadoCentavos - currentNet);
        if (request.MontoCentavos > balance)
            throw new InvalidOperationException("El pago no puede superar el saldo pendiente del pedido.");

        var cash = await GetCashForMethodAsync(request.MetodoPago, cancellationToken);
        var now = DateTime.UtcNow;
        var payment = new PagoPedidoPersonalizado
        {
            CodigoPago = $"TMP-{Guid.NewGuid():N}"[..30],
            OperacionId = request.OperacionId,
            PedidoPersonalizadoId = order.PedidoPersonalizadoId,
            TipoPago = request.TipoPago,
            MetodoPago = request.MetodoPago,
            MontoCentavos = request.MontoCentavos,
            Referencia = Normalize(request.Referencia),
            Fecha = now,
            UsuarioId = CurrentUserId(),
            Observaciones = Normalize(request.Observaciones),
            AfectaCaja = request.MetodoPago == MetodoPago.EFECTIVO,
            CajaId = cash?.CajaId,
            SaldoAnteriorCentavos = balance,
            SaldoNuevoCentavos = balance - request.MontoCentavos,
            FechaCreacion = now
        };
        Context.PagosPedidoPersonalizado.Add(payment);
        order.TotalPagadoCentavos += payment.MontoCentavos;
        RecalculateFinancialState(order);
        await Context.SaveChangesAsync(cancellationToken);
        payment.CodigoPago = $"PAGPED-{payment.PagoPedidoPersonalizadoId:000000}";
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.PAGO_PEDIDO_REGISTRADO, "pagos_pedido_personalizado",
            payment.PagoPedidoPersonalizadoId,
            $"{payment.TipoPago} {payment.CodigoPago} por {Money(payment.MontoCentavos)} en pedido {order.CodigoPedido}. Metodo {payment.MetodoPago}.",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPaymentResultAsync(payment.PagoPedidoPersonalizadoId, cancellationToken);
    }

    public async Task<CustomOrderRefundResult> RegisterRefundAsync(
        CustomOrderRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_REEMBOLSO_REGISTRAR, cancellationToken);
        ValidateRefundInput(request);

        var existing = await Context.ReembolsosPedidoPersonalizado
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OperacionId == request.OperacionId, cancellationToken);
        if (existing is not null)
        {
            if (existing.PedidoPersonalizadoId != request.PedidoPersonalizadoId ||
                existing.MetodoPago != request.MetodoPago ||
                existing.MontoCentavos != request.MontoCentavos)
            {
                throw new InvalidOperationException("El identificador de operacion ya fue usado con datos diferentes.");
            }

            return await GetRefundResultAsync(existing.ReembolsoPedidoPersonalizadoId, cancellationToken);
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await PaymentMethodAvailability.EnsureAvailableAsync(Context, request.MetodoPago, cancellationToken);
        var order = await Context.PedidosPersonalizados
            .FirstOrDefaultAsync(p => p.PedidoPersonalizadoId == request.PedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");
        if (order.VentaId.HasValue)
            throw new InvalidOperationException("Los pagos de un pedido convertido deben corregirse desde la venta relacionada.");

        var available = order.TotalPagadoCentavos - order.TotalReembolsadoCentavos;
        if (request.MontoCentavos > available)
            throw new InvalidOperationException("El reembolso no puede superar el pago neto disponible.");

        if (request.PagoPedidoPersonalizadoId.HasValue)
        {
            var payment = await Context.PagosPedidoPersonalizado
                .FirstOrDefaultAsync(p =>
                    p.PagoPedidoPersonalizadoId == request.PagoPedidoPersonalizadoId &&
                    p.PedidoPersonalizadoId == order.PedidoPersonalizadoId, cancellationToken)
                ?? throw new InvalidOperationException("El pago original no pertenece al pedido.");
            var refunded = await Context.ReembolsosPedidoPersonalizado
                .Where(r => r.PagoPedidoPersonalizadoId == payment.PagoPedidoPersonalizadoId)
                .SumAsync(r => (long?)r.MontoCentavos, cancellationToken) ?? 0;
            if (request.MontoCentavos > payment.MontoCentavos - refunded)
                throw new InvalidOperationException("El reembolso supera el monto disponible del pago seleccionado.");
        }

        var cash = await GetCashForMethodAsync(request.MetodoPago, cancellationToken);
        var oldBalance = order.SaldoPendienteCentavos;
        var now = DateTime.UtcNow;
        var refund = new ReembolsoPedidoPersonalizado
        {
            CodigoReembolso = $"TMP-{Guid.NewGuid():N}"[..30],
            OperacionId = request.OperacionId,
            PedidoPersonalizadoId = order.PedidoPersonalizadoId,
            PagoPedidoPersonalizadoId = request.PagoPedidoPersonalizadoId,
            MetodoPago = request.MetodoPago,
            MontoCentavos = request.MontoCentavos,
            Referencia = Normalize(request.Referencia),
            Motivo = request.Motivo.Trim(),
            Fecha = now,
            UsuarioId = CurrentUserId(),
            AfectaCaja = request.MetodoPago == MetodoPago.EFECTIVO,
            CajaId = cash?.CajaId,
            SaldoAnteriorCentavos = oldBalance,
            SaldoNuevoCentavos = Math.Min(order.TotalEstimadoCentavos, oldBalance + request.MontoCentavos),
            FechaCreacion = now
        };
        Context.ReembolsosPedidoPersonalizado.Add(refund);
        order.TotalReembolsadoCentavos += refund.MontoCentavos;
        RecalculateFinancialState(order);
        await Context.SaveChangesAsync(cancellationToken);
        refund.CodigoReembolso = $"REMPED-{refund.ReembolsoPedidoPersonalizadoId:000000}";
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.REEMBOLSO_PEDIDO_REGISTRADO, "reembolsos_pedido_personalizado",
            refund.ReembolsoPedidoPersonalizadoId,
            $"Reembolso {refund.CodigoReembolso} por {Money(refund.MontoCentavos)} del pedido {order.CodigoPedido}. Motivo: {refund.Motivo}",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRefundResultAsync(refund.ReembolsoPedidoPersonalizadoId, cancellationToken);
    }

    public async Task MapProductAsync(
        CustomOrderProductMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CONVERTIR_VENTA, cancellationToken);
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var line = await Context.DetallePedidosPersonalizados
            .Include(d => d.PedidoPersonalizado)
            .FirstOrDefaultAsync(d => d.DetallePedidoPersonalizadoId == request.DetallePedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("La linea del pedido no existe.");
        if (line.PedidoPersonalizado.VentaId.HasValue || line.PedidoPersonalizado.Estado == EstadoPedidoPersonalizado.CANCELADO)
            throw new InvalidOperationException("No se puede vincular productos en un pedido cerrado.");
        if (line.ProductoId.HasValue)
            throw new InvalidOperationException("La linea ya tiene un producto vinculado.");

        var product = await Context.Productos
            .Include(p => p.Skus)
            .FirstOrDefaultAsync(p => p.ProductoId == request.ProductoId, cancellationToken)
            ?? throw new InvalidOperationException("El producto no existe.");
        if (product.Estado != EstadoProducto.ACTIVO)
            throw new InvalidOperationException("El producto seleccionado no esta activo.");

        var sku = product.Skus.Count == 0 && !request.ProductoSkuId.HasValue
            ? await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                Context, product.ProductoId, cancellationToken)
            : ResolveOrderSku(product, request.ProductoSkuId, allowHistoricalFallback: false);
        line.ProductoId = product.ProductoId;
        line.ProductoSku = sku;
        line.CodigoProductoSnapshot ??= product.CodigoInterno;
        line.CodigoSkuSnapshot = sku.CodigoSku;
        line.NombreVarianteSnapshot = sku.NombreVariante;
        if (line.PedidoPersonalizado.Estado != EstadoPedidoPersonalizado.BORRADOR)
            await CreateReservationForLineAsync(line.PedidoPersonalizado, line, product, CurrentUserId(), cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.LINEA_PEDIDO_VINCULADA, "detalle_pedidos_personalizados",
            line.DetallePedidoPersonalizadoId,
            $"Linea '{line.NombreDescripcionSnapshot}' vinculada al producto {product.CodigoInterno} sin alterar su snapshot.",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CustomOrderConversionPreview> GetConversionPreviewAsync(
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CONVERTIR_VENTA, cancellationToken);
        var order = await Context.PedidosPersonalizados
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Venta)
            .Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(product => product!.Categoria)
            .Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(product => product!.Subcategoria)
            .Include(p => p.Detalles).ThenInclude(d => d.ProductoSku).ThenInclude(sku => sku!.SaldosInventario)
            .Include(p => p.Reservas).ThenInclude(r => r.Producto)
            .Include(p => p.Reservas).ThenInclude(r => r.ProductoSku).ThenInclude(sku => sku.SaldosInventario)
            .FirstOrDefaultAsync(p => p.PedidoPersonalizadoId == pedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");

        var skuIds = order.Detalles.Where(d => d.ProductoSkuId.HasValue)
            .Select(d => d.ProductoSkuId!.Value).Distinct().ToList();
        var allReservations = await Context.ReservasInventarioPedido.AsNoTracking()
            .Where(r => skuIds.Contains(r.ProductoSkuId) && r.Estado == EstadoReservaInventarioPedido.ACTIVA)
            .GroupBy(r => r.ProductoSkuId)
            .Select(g => new { SkuId = g.Key, Quantity = g.Sum(r => r.CantidadReservada - r.CantidadConsumida - r.CantidadLiberada) })
            .ToDictionaryAsync(x => x.SkuId, x => x.Quantity, cancellationToken);

        var lines = order.Detalles.Select(line =>
        {
            var own = order.Reservas
                .Where(r => r.DetallePedidoPersonalizadoId == line.DetallePedidoPersonalizadoId &&
                    r.Estado == EstadoReservaInventarioPedido.ACTIVA)
                .Sum(r => r.CantidadReservada - r.CantidadConsumida - r.CantidadLiberada);
            var totalReserved = line.ProductoSkuId.HasValue
                ? allReservations.GetValueOrDefault(line.ProductoSkuId.Value)
                : 0;
            var others = Math.Max(0, totalReserved - own);
            var stock = line.ProductoSku?.SaldosInventario.Sum(balance => balance.ExistenciaFisica) ?? 0;
            return new CustomOrderConversionLine(
                line.DetallePedidoPersonalizadoId, line.ProductoId,
                line.Producto?.CodigoInterno, line.NombreDescripcionSnapshot,
                line.Cantidad, line.PrecioUnitarioCentavos, line.DescuentoLineaCentavos,
                line.SubtotalCentavos, stock, own, others, Math.Max(0, stock - others),
                line.ProductoSkuId, line.CodigoSkuSnapshot, line.NombreVarianteSnapshot);
        }).ToList();

        var finance = new CustomOrderFinanceSummary(
            order.PedidoPersonalizadoId, order.CodigoPedido, order.NombreDestinatarioSnapshot,
            order.EstadoFinanciero, order.TotalEstimadoCentavos, order.TotalPagadoCentavos,
            order.TotalReembolsadoCentavos, order.TotalPagadoCentavos - order.TotalReembolsadoCentavos,
            order.SaldoPendienteCentavos, order.VentaId, order.Venta?.NumeroVenta);
        var reservations = order.Reservas.Select(r => new CustomOrderReservationResult(
            r.ReservaInventarioPedidoId, r.DetallePedidoPersonalizadoId, r.ProductoId,
            r.Producto.CodigoInterno, r.Producto.Nombre, r.CantidadReservada,
            r.CantidadConsumida, r.CantidadLiberada, r.Estado, r.FechaReserva,
            r.ProductoSkuId, r.ProductoSku.CodigoSku, r.ProductoSku.NombreVariante, r.Ubicacion)).ToList();
        return new CustomOrderConversionPreview(
            finance, order.Estado, order.ClienteId,
            order.Cliente?.Estado == EstadoCliente.ACTIVO,
            order.Cliente is { Estado: EstadoCliente.ACTIVO, CreditoHabilitado: true },
            lines, reservations, lines.Count(l => !l.ProductoId.HasValue));
    }

    public async Task<CustomOrderConversionResult> ConvertToSaleAsync(
        CustomOrderConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CONVERTIR_VENTA, cancellationToken);
        if (request.OperacionId == Guid.Empty)
            throw new ArgumentException("El identificador de operacion es obligatorio.");

        var completed = await Context.PedidosPersonalizados.AsNoTracking()
            .Where(p => p.OperacionConversionId == request.OperacionId && p.VentaId.HasValue)
            .Select(p => new { p.PedidoPersonalizadoId, p.VentaId })
            .FirstOrDefaultAsync(cancellationToken);
        if (completed is not null)
            return await BuildConversionResultAsync(completed.PedidoPersonalizadoId, completed.VentaId!.Value, cancellationToken);

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var order = await Context.PedidosPersonalizados
            .Include(p => p.Cliente)
            .Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(product => product!.Skus)
                .ThenInclude(sku => sku.SaldosInventario)
            .Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(product => product!.Categoria)
            .Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(product => product!.Subcategoria)
            .Include(p => p.Detalles).ThenInclude(d => d.ProductoSku).ThenInclude(sku => sku!.SaldosInventario)
            .Include(p => p.Reservas).ThenInclude(r => r.ProductoSku).ThenInclude(sku => sku.SaldosInventario)
            .Include(p => p.Pagos).ThenInclude(p => p.Reembolsos)
            .FirstOrDefaultAsync(p => p.PedidoPersonalizadoId == request.PedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");
        EnsureCanConvert(order);

        var cash = await GetOpenCashAsync(cancellationToken)
            ?? throw new InvalidOperationException("Debe existir una caja abierta para convertir el pedido en venta.");
        var userId = CurrentUserId();
        var now = DateTime.UtcNow;
        var priorNet = order.TotalPagadoCentavos - order.TotalReembolsadoCentavos;
        var outstanding = Math.Max(0, order.TotalEstimadoCentavos - priorNet);
        var finalPayments = NormalizeFinalPayments(request.FinalPayments);
        var finalReceived = finalPayments.Sum(p => p.MontoCentavos);
        long creditCapital;
        long change;

        if (request.DejarSaldoCredito)
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.CREDITO_REGISTRAR, cancellationToken);
            if (order.Cliente is null)
                throw new InvalidOperationException("El saldo a credito requiere un cliente registrado.");
            if (finalReceived > outstanding)
                throw new InvalidOperationException("El pago final no puede superar el saldo cuando se deja credito.");
            creditCapital = outstanding - finalReceived;
            if (creditCapital <= 0)
                throw new InvalidOperationException("No existe saldo restante para crear credito.");
            change = 0;
        }
        else
        {
            if (finalReceived < outstanding)
                throw new InvalidOperationException("El pago final no cubre el saldo pendiente.");
            creditCapital = 0;
            change = finalReceived - outstanding;
            var cashReceived = finalPayments.Where(p => p.MetodoPago == MetodoPago.EFECTIVO).Sum(p => p.MontoCentavos);
            if (change > cashReceived)
                throw new InvalidOperationException("El vuelto solo puede salir del efectivo recibido.");
        }

        var activeReservations = order.Reservas
            .Where(r => r.Estado == EstadoReservaInventarioPedido.ACTIVA)
            .ToList();
        ValidateStockForConversion(order, activeReservations);
        var conversionProducts = order.Detalles
            .Select(line => line.Producto!)
            .DistinctBy(product => product.ProductoId)
            .ToList();
        var costOrigins = await HistoricalProductSnapshotResolver.ResolveCostOriginsAsync(
            Context,
            conversionProducts,
            cancellationToken);

        var sale = new Venta
        {
            NumeroVenta = await GenerateSaleNumberAsync(cancellationToken),
            CajaId = cash.CajaId,
            UsuarioId = userId,
            ClienteId = order.ClienteId,
            ClienteNombreSnapshot = order.NombreDestinatarioSnapshot,
            FechaVenta = now,
            SubtotalCentavos = order.SubtotalCentavos,
            DescuentoCentavos = order.Detalles.Sum(d => d.DescuentoLineaCentavos) + order.DescuentoGeneralCentavos,
            DescuentoMotivo = order.DescuentoGeneralCentavos > 0 ? $"Pedido {order.CodigoPedido}" : null,
            TotalCentavos = order.TotalEstimadoCentavos,
            PagadoCentavos = order.TotalEstimadoCentavos - creditCapital,
            CambioCentavos = change,
            Estado = EstadoVenta.COMPLETADA,
            Observaciones = $"Convertida desde pedido personalizado {order.CodigoPedido}."
        };
        Context.Ventas.Add(sale);
        await Context.SaveChangesAsync(cancellationToken);

        var details = new List<DetalleVenta>();
        foreach (var line in order.Detalles)
        {
            var product = line.Producto!;
            ProductoSku? soldSku = null;
            var stockBefore = InventoryStockEngine.GetPhysicalTotal(product);
            var lineReservations = order.Reservas
                .Where(reservation => reservation.DetallePedidoPersonalizadoId == line.DetallePedidoPersonalizadoId &&
                    reservation.Estado == EstadoReservaInventarioPedido.ACTIVA)
                .ToList();
            if (lineReservations.Count > 0)
            {
                foreach (var reservation in lineReservations)
                {
                    var quantity = reservation.CantidadReservada - reservation.CantidadLiberada - reservation.CantidadConsumida;
                    var sku = await InventoryStockEngine.GetSkuAsync(
                        Context, reservation.ProductoSkuId, cancellationToken);
                    soldSku ??= sku;
                    if (soldSku.ProductoSkuId != sku.ProductoSkuId)
                        throw new InvalidOperationException("Una línea de pedido no puede convertirse desde varios SKU.");
                    InventoryStockEngine.ConsumeReservation(Context, sku,
                        reservation.Ubicacion,
                        quantity, userId,
                        $"Venta {sale.NumeroVenta} desde pedido {order.CodigoPedido}", now);
                    reservation.ProductoSku = sku;
                    reservation.CantidadConsumida += quantity;
                    reservation.Estado = EstadoReservaInventarioPedido.CONSUMIDA;
                    reservation.FechaConsumo = now;
                }
            }
            else
            {
                var sku = line.ProductoSkuId.HasValue
                    ? await InventoryStockEngine.GetSkuAsync(Context, line.ProductoSkuId.Value, cancellationToken)
                    : await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                        Context, product.ProductoId, cancellationToken);
                soldSku = sku;
                InventoryStockEngine.RemovePhysical(Context, sku, line.Cantidad, userId,
                    TipoOperacionInventario.SALIDA_VENTA,
                    $"Venta {sale.NumeroVenta} desde pedido {order.CodigoPedido}", now,
                    origin: UbicacionInventario.EXHIBICION);
            }
            soldSku ??= await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                Context, product.ProductoId, cancellationToken);
            var detail = new DetalleVenta
            {
                VentaId = sale.VentaId,
                ProductoId = product.ProductoId,
                ProductoSku = soldSku,
                Cantidad = line.Cantidad,
                PrecioUnitarioCentavos = line.PrecioUnitarioCentavos,
                CostoUnitarioCentavos = soldSku.ResolverCostoCentavos(),
                OrigenCosto = soldSku.CostoOverrideCentavos.HasValue
                    ? OrigenCostoHistorico.SKU_OVERRIDE
                    : costOrigins[product.ProductoId],
                ProductoCodigoSnapshot = product.CodigoInterno,
                ProductoNombreSnapshot = product.Nombre,
                CodigoSkuSnapshot = soldSku.CodigoSku,
                NombreVarianteSnapshot = soldSku.NombreVariante,
                CategoriaIdSnapshot = product.CategoriaId,
                CategoriaNombreSnapshot = product.Categoria.Nombre,
                SubcategoriaIdSnapshot = product.SubcategoriaId,
                SubcategoriaNombreSnapshot = product.SubcategoriaId.HasValue
                    ? product.Subcategoria!.Nombre
                    : "Sin subcategoría",
                SubtotalCentavos = checked(line.PrecioUnitarioCentavos * line.Cantidad),
                DescuentoCentavos = line.DescuentoLineaCentavos,
                DescuentoMotivo = line.DescuentoLineaCentavos > 0 ? $"Pedido {order.CodigoPedido}" : null,
                TotalCentavos = line.SubtotalCentavos
            };
            details.Add(detail);
            Context.DetalleVentas.Add(detail);
            Context.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = product.ProductoId,
                VentaId = sale.VentaId,
                UsuarioId = userId,
                TipoMovimiento = TipoMovimientoInventario.SALIDA,
                Cantidad = -line.Cantidad,
                StockAnterior = stockBefore,
                StockNuevo = stockBefore - line.Cantidad,
                Motivo = $"Venta {sale.NumeroVenta} desde pedido {order.CodigoPedido}",
                FechaMovimiento = now
            });
        }
        await Context.SaveChangesAsync(cancellationToken);

        var effectiveFinalPayments = BuildEffectiveFinalPayments(finalPayments, change);
        var finalPaymentIndex = 0;
        foreach (var input in effectiveFinalPayments)
        {
            finalPaymentIndex++;
            var oldBalance = order.SaldoPendienteCentavos;
            var payment = new PagoPedidoPersonalizado
            {
                CodigoPago = $"TMP-{Guid.NewGuid():N}"[..30],
                OperacionId = DeterministicGuid(request.OperacionId, finalPaymentIndex),
                PedidoPersonalizadoId = order.PedidoPersonalizadoId,
                TipoPago = TipoPagoPedido.PAGO_FINAL,
                MetodoPago = input.MetodoPago,
                MontoCentavos = input.MontoCentavos,
                Referencia = Normalize(input.Referencia),
                Fecha = now,
                UsuarioId = userId,
                Observaciones = $"Pago final durante conversion a {sale.NumeroVenta}.",
                AfectaCaja = input.MetodoPago == MetodoPago.EFECTIVO,
                CajaId = input.MetodoPago == MetodoPago.EFECTIVO ? cash.CajaId : null,
                SaldoAnteriorCentavos = oldBalance,
                SaldoNuevoCentavos = Math.Max(0, oldBalance - input.MontoCentavos),
                VentaIdAplicada = sale.VentaId,
                FechaAplicacionVenta = now,
                FechaCreacion = now
            };
            Context.PagosPedidoPersonalizado.Add(payment);
            await Context.SaveChangesAsync(cancellationToken);
            payment.CodigoPago = $"PAGPED-{payment.PagoPedidoPersonalizadoId:000000}";
            order.TotalPagadoCentavos += payment.MontoCentavos;
            order.SaldoPendienteCentavos = payment.SaldoNuevoCentavos;
        }
        await Context.SaveChangesAsync(cancellationToken);

        var netToApply = order.TotalEstimadoCentavos - creditCapital;
        foreach (var payment in order.Pagos.OrderBy(p => p.Fecha).ThenBy(p => p.PagoPedidoPersonalizadoId))
        {
            if (netToApply <= 0) break;
            var refundedForPayment = payment.Reembolsos.Sum(r => r.MontoCentavos);
            var available = Math.Max(0, payment.MontoCentavos - refundedForPayment);
            var applied = Math.Min(available, netToApply);
            if (applied <= 0) continue;
            Context.PagosVenta.Add(new PagoVenta
            {
                VentaId = sale.VentaId,
                MetodoPago = payment.MetodoPago,
                MontoCentavos = applied,
                Referencia = payment.Referencia,
                FechaPago = now,
                Origen = OrigenPagoVenta.PEDIDO,
                PagoPedidoPersonalizadoId = payment.PagoPedidoPersonalizadoId,
                AfectaCaja = false
            });
            payment.VentaIdAplicada = sale.VentaId;
            payment.FechaAplicacionVenta = now;
            netToApply -= applied;
        }
        if (netToApply != 0)
            throw new InvalidOperationException("Los pagos netos del pedido no cuadran con la venta.");

        CuentaPorCobrar? account = null;
        if (creditCapital > 0)
        {
            account = await CreateCreditAccountAsync(
                order, sale, details, cash.CajaId, userId, creditCapital,
                request.PlazoCreditoDias, request.ObservacionesCredito, cancellationToken);
        }

        var previousState = order.Estado;
        order.Estado = EstadoPedidoPersonalizado.ENTREGADO;
        order.FechaEntregaReal = now;
        order.VentaId = sale.VentaId;
        order.OperacionConversionId = request.OperacionId;
        order.FechaConversion = now;
        order.UsuarioConversionId = userId;
        order.UsuarioUltimaActualizacionId = userId;
        order.FechaActualizacion = now;
        RecalculateFinancialState(order);
        order.HistorialEstados.Add(new HistorialEstadoPedido
        {
            PedidoPersonalizadoId = order.PedidoPersonalizadoId,
            EstadoAnterior = previousState,
            EstadoNuevo = EstadoPedidoPersonalizado.ENTREGADO,
            Motivo = $"Convertido en venta {sale.NumeroVenta}.",
            UsuarioId = userId,
            Fecha = now
        });
        await Context.SaveChangesAsync(cancellationToken);

        await AuditAsync(TipoBitacora.RESERVA_PEDIDO_CONSUMIDA, "reservas_inventario_pedido",
            order.PedidoPersonalizadoId, $"Reservas del pedido {order.CodigoPedido} consumidas por la venta {sale.NumeroVenta}.", cancellationToken);
        await AuditAsync(TipoBitacora.PAGOS_PEDIDO_APLICADOS_VENTA, "pagos_pedido_personalizado",
            order.PedidoPersonalizadoId, $"Pagos netos por {Money(sale.PagadoCentavos)} aplicados a {sale.NumeroVenta} sin afectar caja nuevamente.", cancellationToken);
        await AuditAsync(TipoBitacora.PEDIDO_CONVERTIDO_VENTA, "pedidos_personalizados",
            order.PedidoPersonalizadoId, $"Pedido {order.CodigoPedido} convertido en venta {sale.NumeroVenta}.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CustomOrderConversionResult(
            order.PedidoPersonalizadoId, sale.VentaId, sale.NumeroVenta,
            sale.TotalCentavos, priorNet, effectiveFinalPayments.Sum(p => p.MontoCentavos),
            creditCapital, change, account?.CuentaPorCobrarId);
    }

    internal async Task CreateReservationsAsync(
        PedidoPersonalizado order,
        int userId,
        CancellationToken cancellationToken)
    {
        var productLines = order.Detalles.Where(d => d.ProductoId.HasValue).ToList();
        foreach (var line in productLines)
            await CreateReservationForLineAsync(order, line, line.Producto!, userId, cancellationToken);
    }

    internal async Task ReleaseReservationsAsync(
        PedidoPersonalizado order,
        int userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        foreach (var reservation in order.Reservas.Where(r => r.Estado == EstadoReservaInventarioPedido.ACTIVA))
        {
            var quantity = reservation.CantidadReservada - reservation.CantidadConsumida;
            var sku = await InventoryStockEngine.GetSkuAsync(
                Context, reservation.ProductoSkuId, cancellationToken);
            InventoryStockEngine.ReleaseReservation(Context, sku, reservation.Ubicacion,
                quantity, userId, reason, now);
            reservation.CantidadLiberada = quantity;
            reservation.Estado = EstadoReservaInventarioPedido.LIBERADA;
            reservation.FechaLiberacion = now;
            reservation.Motivo = reason;
        }
        await Context.SaveChangesAsync(cancellationToken);
        if (order.Reservas.Any(r => r.Estado == EstadoReservaInventarioPedido.LIBERADA))
            await AuditAsync(TipoBitacora.RESERVA_PEDIDO_LIBERADA, "reservas_inventario_pedido",
                order.PedidoPersonalizadoId, $"Reservas del pedido {order.CodigoPedido} liberadas. Motivo: {reason}", cancellationToken);
    }

    private async Task CreateReservationForLineAsync(
        PedidoPersonalizado order,
        DetallePedidoPersonalizado line,
        Producto product,
        int userId,
        CancellationToken cancellationToken)
    {
        var exists = await Context.ReservasInventarioPedido.AnyAsync(
            r => r.DetallePedidoPersonalizadoId == line.DetallePedidoPersonalizadoId,
            cancellationToken);
        if (exists) return;

        var now = DateTime.UtcNow;
        var sku = line.ProductoSkuId.HasValue
            ? await InventoryStockEngine.GetSkuAsync(Context, line.ProductoSkuId.Value, cancellationToken)
            : await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                Context, product.ProductoId, cancellationToken);
        if (sku.ProductoId != product.ProductoId)
            throw new InvalidOperationException("El SKU de la linea no pertenece al producto del pedido.");
        line.ProductoSku = sku;
        line.CodigoSkuSnapshot ??= sku.CodigoSku;
        line.NombreVarianteSnapshot ??= sku.NombreVariante;
        var reason = $"Reserva automatica al confirmar {order.CodigoPedido}.";
        var allocations = InventoryStockEngine.Reserve(
            Context, sku, line.Cantidad, userId, reason, now);
        foreach (var allocation in allocations)
        {
            Context.ReservasInventarioPedido.Add(new ReservaInventarioPedido
            {
                PedidoPersonalizadoId = order.PedidoPersonalizadoId,
                DetallePedidoPersonalizadoId = line.DetallePedidoPersonalizadoId,
                ProductoId = product.ProductoId,
                ProductoSku = sku,
                Ubicacion = allocation.Balance.Ubicacion,
                CantidadReservada = allocation.Quantity,
                Estado = EstadoReservaInventarioPedido.ACTIVA,
                FechaReserva = now,
                UsuarioId = userId,
                Motivo = reason
            });
        }
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.RESERVA_PEDIDO_CREADA, "reservas_inventario_pedido",
            order.PedidoPersonalizadoId,
            $"Reservadas {line.Cantidad} unidad(es) de {product.CodigoInterno} para {order.CodigoPedido}.",
            cancellationToken);
    }

    private async Task<CuentaPorCobrar> CreateCreditAccountAsync(
        PedidoPersonalizado order,
        Venta sale,
        IReadOnlyList<DetalleVenta> details,
        int cajaId,
        int userId,
        long capital,
        int termDays,
        string? observations,
        CancellationToken cancellationToken)
    {
        if (order.Cliente is null)
            throw new InvalidOperationException("El credito requiere cliente.");
        var policy = await _creditService.BuildEffectivePolicyAsync(order.Cliente, cancellationToken);
        var interest = (long)Math.Round(capital * policy.InterestPercent / 100m, MidpointRounding.AwayFromZero);
        var totalAccount = capital + interest;
        await _creditService.ValidateCreditEligibilityAsync(
            order.Cliente, policy, totalAccount, termDays, cancellationToken);
        var account = new CuentaPorCobrar
        {
            CodigoCuenta = await _creditService.GenerateAccountCodeAsync(cancellationToken),
            VentaId = sale.VentaId,
            ClienteId = order.Cliente.ClienteId,
            CajaId = cajaId,
            UsuarioRegistroId = userId,
            CapitalFinanciadoCentavos = capital,
            InteresPorcentaje = policy.InterestPercent,
            InteresCalculadoCentavos = interest,
            TotalOriginalCentavos = totalAccount,
            SaldoActualCentavos = totalAccount,
            FechaCreacion = DateTime.UtcNow,
            FechaVencimiento = DateTime.UtcNow.AddDays(termDays),
            Estado = EstadoCuentaPorCobrar.ABIERTA,
            Observaciones = Normalize(observations) ?? $"Saldo del pedido {order.CodigoPedido}.",
            FechaActualizacion = DateTime.UtcNow
        };
        Context.CuentasPorCobrar.Add(account);
        await Context.SaveChangesAsync(cancellationToken);
        var allocations = Allocate(capital, details.Select(d => d.TotalCentavos).ToList());
        var orderLines = order.Detalles.ToList();
        for (var i = 0; i < details.Count; i++)
        {
            var detail = details[i];
            var product = orderLines[i].Producto!;
            Context.CuentasPorCobrarDetalle.Add(new CuentaPorCobrarDetalle
            {
                CuentaPorCobrarId = account.CuentaPorCobrarId,
                DetalleVentaId = detail.DetalleVentaId,
                ProductoId = detail.ProductoId,
                ProductoCodigoSnapshot = product.CodigoInterno,
                ProductoNombreSnapshot = orderLines[i].NombreDescripcionSnapshot,
                CantidadFinanciada = detail.Cantidad,
                PrecioUnitarioCentavos = detail.PrecioUnitarioCentavos,
                DescuentoLineaCentavos = detail.DescuentoCentavos,
                DescuentoGeneralProporcionalCentavos = 0,
                SubtotalFinanciadoCentavos = allocations[i],
                CapitalPendienteCentavos = allocations[i]
            });
        }
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.CREDITO_PEDIDO_CREADO, "cuentas_por_cobrar",
            account.CuentaPorCobrarId,
            $"Cuenta {account.CodigoCuenta} creada solo por saldo {Money(capital)} del pedido {order.CodigoPedido}.",
            cancellationToken);
        return account;
    }

    private static void EnsureCanReceivePayment(PedidoPersonalizado order)
    {
        if (order.Estado == EstadoPedidoPersonalizado.BORRADOR)
            throw new InvalidOperationException("No se permiten pagos en pedidos borrador.");
        if (order.Estado == EstadoPedidoPersonalizado.CANCELADO)
            throw new InvalidOperationException("No se permiten pagos en pedidos cancelados.");
        if (order.VentaId.HasValue)
            throw new InvalidOperationException("El pedido ya fue convertido en venta.");
    }

    private static void EnsureCanConvert(PedidoPersonalizado order)
    {
        if (order.Estado != EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA)
            throw new InvalidOperationException("Solo un pedido listo para entrega puede convertirse en venta.");
        if (order.VentaId.HasValue || order.OperacionConversionId.HasValue)
            throw new InvalidOperationException("El pedido ya fue convertido en venta.");
        if (order.Detalles.Count == 0)
            throw new InvalidOperationException("El pedido no tiene productos.");
        if (order.Detalles.Any(d => !d.ProductoId.HasValue))
            throw new InvalidOperationException("Todas las lineas descriptivas deben vincularse a un producto.");
        if (order.Detalles.Sum(d => d.SubtotalCentavos) - order.DescuentoGeneralCentavos != order.TotalEstimadoCentavos)
            throw new InvalidOperationException("El total del pedido no es consistente.");
    }

    private static void ValidateStockForConversion(
        PedidoPersonalizado order,
        IReadOnlyList<ReservaInventarioPedido> activeReservations)
    {
        foreach (var line in order.Detalles)
        {
            var product = line.Producto!;
            if (product.Estado != EstadoProducto.ACTIVO)
                throw new InvalidOperationException($"El producto '{product.Nombre}' no esta activo.");
            var lineReservations = activeReservations
                .Where(r => r.DetallePedidoPersonalizadoId == line.DetallePedidoPersonalizadoId)
                .ToList();
            var own = lineReservations.Sum(r => r.CantidadReservada - r.CantidadConsumida - r.CantidadLiberada);
            if (own > 0 && own != line.Cantidad)
                throw new InvalidOperationException($"La reserva de '{product.Nombre}' esta incompleta.");
            if (line.ProductoSkuId.HasValue && lineReservations.Any(r => r.ProductoSkuId != line.ProductoSkuId.Value))
                throw new InvalidOperationException($"La reserva de '{product.Nombre}' no corresponde al SKU seleccionado.");
            foreach (var reservation in lineReservations)
            {
                var balance = reservation.ProductoSku.SaldosInventario
                    .Single(item => item.Ubicacion == reservation.Ubicacion);
                var remaining = reservation.CantidadReservada - reservation.CantidadConsumida - reservation.CantidadLiberada;
                if (balance.Reservado < remaining || balance.ExistenciaFisica < remaining)
                    throw new InvalidOperationException($"La reserva de '{product.Nombre}' ya no es consistente con inventario.");
            }
        }
    }

    private static ProductoSku ResolveOrderSku(
        Producto product,
        int? requestedSkuId,
        bool allowHistoricalFallback)
    {
        var active = product.Skus.Where(sku => sku.Activo).ToList();
        if (requestedSkuId.HasValue)
            return active.SingleOrDefault(sku => sku.ProductoSkuId == requestedSkuId.Value)
                ?? throw new InvalidOperationException("El SKU seleccionado no pertenece al producto o esta inactivo.");
        if (!allowHistoricalFallback && product.Modo == ModoProducto.POR_VARIANTES && active.Count > 1)
            throw new InvalidOperationException($"Debe seleccionar la variante/SKU exacta para '{product.Nombre}'.");
        return active.FirstOrDefault(sku => sku.EsPrincipal)
            ?? active.SingleOrDefault()
            ?? throw new InvalidOperationException($"El producto '{product.Nombre}' no tiene un SKU activo.");
    }

    private static void ValidatePaymentInput(CustomOrderPaymentRequest request)
    {
        if (request.OperacionId == Guid.Empty) throw new ArgumentException("OperacionId es obligatorio.");
        if (request.MontoCentavos <= 0) throw new ArgumentException("El monto debe ser mayor que cero.");
        ValidateMethod(request.MetodoPago, request.Referencia);
    }

    private static void ValidateRefundInput(CustomOrderRefundRequest request)
    {
        if (request.OperacionId == Guid.Empty) throw new ArgumentException("OperacionId es obligatorio.");
        if (request.MontoCentavos <= 0) throw new ArgumentException("El monto debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(request.Motivo)) throw new ArgumentException("El motivo es obligatorio.");
        ValidateMethod(request.MetodoPago, request.Referencia);
    }

    private static IReadOnlyList<SalePaymentRequest> NormalizeFinalPayments(IReadOnlyList<SalePaymentRequest> payments)
    {
        var result = payments.Where(p => p.MontoCentavos > 0).ToList();
        foreach (var payment in result)
        {
            if (payment.MontoCentavos <= 0) throw new ArgumentException("Los pagos finales deben ser positivos.");
            ValidateMethod(payment.MetodoPago, payment.Referencia);
        }
        return result;
    }

    private static void ValidateMethod(MetodoPago method, string? reference)
    {
        if (method is not MetodoPago.EFECTIVO and not MetodoPago.TRANSFERENCIA and not MetodoPago.BITCOIN)
            throw new ArgumentException("Metodo de pago no permitido.");
        if (method is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN && string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException($"{method} requiere referencia.");
    }

    private static IReadOnlyList<SalePaymentRequest> BuildEffectiveFinalPayments(
        IReadOnlyList<SalePaymentRequest> payments,
        long change)
    {
        var result = new List<SalePaymentRequest>();
        var cash = payments.Where(p => p.MetodoPago == MetodoPago.EFECTIVO).Sum(p => p.MontoCentavos) - change;
        if (cash > 0) result.Add(new(MetodoPago.EFECTIVO, cash));
        result.AddRange(payments.Where(p => p.MetodoPago != MetodoPago.EFECTIVO));
        return result;
    }

    private async Task<Caja?> GetCashForMethodAsync(MetodoPago method, CancellationToken cancellationToken) =>
        method == MetodoPago.EFECTIVO
            ? await GetOpenCashAsync(cancellationToken)
                ?? throw new InvalidOperationException("El efectivo requiere caja abierta.")
            : null;

    private Task<Caja?> GetOpenCashAsync(CancellationToken cancellationToken)
    {
        return CashRegisterStateQueries.GetCurrentOpenCashAsync(Context, cancellationToken);
    }

    private static void RecalculateFinancialState(PedidoPersonalizado order)
    {
        var net = Math.Max(0, order.TotalPagadoCentavos - order.TotalReembolsadoCentavos);
        order.SaldoPendienteCentavos = Math.Max(0, order.TotalEstimadoCentavos - net);
        order.EstadoFinanciero = order.TotalReembolsadoCentavos > 0
            ? net == 0
                ? EstadoFinancieroPedido.REEMBOLSADO
                : EstadoFinancieroPedido.REEMBOLSADO_PARCIAL
            : net == 0
                ? EstadoFinancieroPedido.SIN_PAGO
                : net >= order.TotalEstimadoCentavos
                    ? EstadoFinancieroPedido.PAGADO
                    : EstadoFinancieroPedido.PARCIAL;
    }

    private async Task<string> GenerateSaleNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = $"VTA-{DateTime.Now:yyyyMMdd}-";
        var next = await Context.Ventas.CountAsync(v => v.NumeroVenta.StartsWith(prefix), cancellationToken) + 1;
        while (await Context.Ventas.AnyAsync(v => v.NumeroVenta == $"{prefix}{next:0000}", cancellationToken)) next++;
        return $"{prefix}{next:0000}";
    }

    private async Task<CustomOrderPaymentResult> GetPaymentResultAsync(int id, CancellationToken cancellationToken) =>
        await Context.PagosPedidoPersonalizado.AsNoTracking()
            .Where(p => p.PagoPedidoPersonalizadoId == id)
            .Select(p => new CustomOrderPaymentResult(
                p.PagoPedidoPersonalizadoId, p.CodigoPago, p.OperacionId, p.PedidoPersonalizadoId,
                p.TipoPago, p.MetodoPago, p.MontoCentavos, p.Referencia, p.Fecha,
                p.Usuario.NombreUsuario, p.Observaciones, p.AfectaCaja, p.CajaId,
                p.SaldoAnteriorCentavos, p.SaldoNuevoCentavos, p.VentaIdAplicada))
            .SingleAsync(cancellationToken);

    private async Task<CustomOrderRefundResult> GetRefundResultAsync(int id, CancellationToken cancellationToken) =>
        await Context.ReembolsosPedidoPersonalizado.AsNoTracking()
            .Where(r => r.ReembolsoPedidoPersonalizadoId == id)
            .Select(r => new CustomOrderRefundResult(
                r.ReembolsoPedidoPersonalizadoId, r.CodigoReembolso, r.OperacionId,
                r.PedidoPersonalizadoId, r.MetodoPago, r.MontoCentavos, r.Referencia,
                r.Motivo, r.Fecha, r.Usuario.NombreUsuario, r.AfectaCaja, r.CajaId,
                r.SaldoAnteriorCentavos, r.SaldoNuevoCentavos))
            .SingleAsync(cancellationToken);

    private async Task<CustomOrderConversionResult> BuildConversionResultAsync(
        int orderId,
        int saleId,
        CancellationToken cancellationToken)
    {
        var order = await Context.PedidosPersonalizados.AsNoTracking()
            .Include(p => p.Venta).ThenInclude(v => v!.CuentaPorCobrar)
            .Include(p => p.Pagos)
            .SingleAsync(p => p.PedidoPersonalizadoId == orderId, cancellationToken);
        var prior = order.Pagos.Where(p => p.TipoPago != TipoPagoPedido.PAGO_FINAL).Sum(p => p.MontoCentavos)
            - order.TotalReembolsadoCentavos;
        var final = order.Pagos.Where(p => p.TipoPago == TipoPagoPedido.PAGO_FINAL).Sum(p => p.MontoCentavos);
        return new(orderId, saleId, order.Venta!.NumeroVenta, order.Venta.TotalCentavos,
            prior, final, order.Venta.CuentaPorCobrar?.CapitalFinanciadoCentavos ?? 0,
            order.Venta.CambioCentavos, order.Venta.CuentaPorCobrar?.CuentaPorCobrarId);
    }

    private Task AuditAsync(
        TipoBitacora type,
        string entity,
        int entityId,
        string description,
        CancellationToken cancellationToken) =>
        _audit.RegistrarAsync(new AuditEntryRequest(
            CurrentUserId(), type, entity, entityId, description), cancellationToken);

    private int CurrentUserId() => _session.UsuarioActual?.UsuarioId
        ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");

    private static Guid DeterministicGuid(Guid seed, int index)
    {
        var bytes = seed.ToByteArray().Concat(BitConverter.GetBytes(index)).ToArray();
        return new Guid(SHA256.HashData(bytes)[..16]);
    }

    private static IReadOnlyList<long> Allocate(long amount, IReadOnlyList<long> bases)
    {
        var result = new long[bases.Count];
        if (amount <= 0 || bases.Count == 0) return result;
        var total = bases.Sum();
        long allocated = 0;
        for (var i = 0; i < bases.Count; i++)
        {
            result[i] = i == bases.Count - 1 ? amount - allocated : amount * bases[i] / total;
            allocated += result[i];
        }
        return result;
    }
}
