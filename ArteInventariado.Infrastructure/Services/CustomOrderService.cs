using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomOrderService : ICustomOrderService
{
    private const string ConcurrencyMessage =
        "El pedido fue actualizado. Cierre y vuelva a abrir el modulo antes de intentarlo nuevamente.";

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _session;
    private readonly IEmployeePermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly ICustomOrderQueryService _query;

    [ActivatorUtilitiesConstructor]
    public CustomOrderService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession session,
        IEmployeePermissionService permissions,
        IAuditService audit,
        ICustomOrderQueryService query)
    {
        _contextFactory = contextFactory;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _query = query;
    }

    internal CustomOrderService(
        AppDbContext context,
        ICurrentSession session,
        IEmployeePermissionService permissions,
        IAuditService audit,
        ICustomOrderQueryService query)
    {
        _legacyContext = context;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _query = query;
    }

    public async Task<CustomOrderDetail> CreateDraftAsync(
        CustomOrderSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CREAR, cancellationToken);
        var userId = CurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var draft = await BuildDraftAsync(context, request, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var order = new PedidoPersonalizado
        {
            CodigoPedido = $"TMP-{Guid.NewGuid():N}"[..30],
            ClienteId = draft.ClienteId,
            NombreDestinatarioSnapshot = draft.Nombre,
            TelefonoSnapshot = draft.Telefono,
            CorreoSnapshot = draft.Correo,
            DireccionSnapshot = draft.Direccion,
            Estado = EstadoPedidoPersonalizado.BORRADOR,
            Prioridad = request.Prioridad,
            FechaPedido = request.FechaPedido.Date,
            FechaPrometida = request.FechaPrometida.Date,
            SubtotalCentavos = draft.Subtotal,
            DescuentoGeneralCentavos = request.DescuentoGeneralCentavos,
            TotalEstimadoCentavos = draft.Subtotal - request.DescuentoGeneralCentavos,
            EstadoFinanciero = EstadoFinancieroPedido.SIN_PAGO,
            SaldoPendienteCentavos = draft.Subtotal - request.DescuentoGeneralCentavos,
            DescripcionGeneral = Normalize(request.DescripcionGeneral),
            EspecificacionesGenerales = Normalize(request.EspecificacionesGenerales),
            ObservacionesInternas = Normalize(request.ObservacionesInternas),
            NotasCliente = Normalize(request.NotasCliente),
            UsuarioCreadorId = userId,
            UsuarioUltimaActualizacionId = userId,
            FechaCreacion = now,
            FechaActualizacion = now
        };
        foreach (var line in draft.Lines) order.Detalles.Add(line);
        context.PedidosPersonalizados.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        order.CodigoPedido = $"PED-{order.PedidoPersonalizadoId:000000}";
        order.HistorialEstados.Add(NewHistory(
            order.PedidoPersonalizadoId, null, EstadoPedidoPersonalizado.BORRADOR,
            "Borrador creado.", userId, now));
        context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId, TipoBitacora.PEDIDO_CREADO, "pedidos_personalizados",
            order.PedidoPersonalizadoId, $"Pedido {order.CodigoPedido} creado como borrador.",
            DatosDespues: Snapshot(order))));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await _query.GetByIdAsync(order.PedidoPersonalizadoId, cancellationToken))!;
    }

    public async Task<CustomOrderDetail> UpdateDraftAsync(
        CustomOrderSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_EDITAR, cancellationToken);
        if (!request.PedidoPersonalizadoId.HasValue)
            throw new ArgumentException("El pedido es obligatorio.");
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var order = await context.PedidosPersonalizados.Include(p => p.Detalles)
            .FirstOrDefaultAsync(p => p.PedidoPersonalizadoId == request.PedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");
        if (order.Estado != EstadoPedidoPersonalizado.BORRADOR)
            throw new InvalidOperationException("Solo se pueden editar pedidos en borrador.");

        var draft = await BuildDraftAsync(context, request, cancellationToken);
        var userId = CurrentUserId();
        var before = Snapshot(order);
        order.ClienteId = draft.ClienteId;
        order.NombreDestinatarioSnapshot = draft.Nombre;
        order.TelefonoSnapshot = draft.Telefono;
        order.CorreoSnapshot = draft.Correo;
        order.DireccionSnapshot = draft.Direccion;
        order.Prioridad = request.Prioridad;
        order.FechaPedido = request.FechaPedido.Date;
        order.FechaPrometida = request.FechaPrometida.Date;
        order.SubtotalCentavos = draft.Subtotal;
        order.DescuentoGeneralCentavos = request.DescuentoGeneralCentavos;
        order.TotalEstimadoCentavos = draft.Subtotal - request.DescuentoGeneralCentavos;
        order.SaldoPendienteCentavos = order.TotalEstimadoCentavos;
        order.DescripcionGeneral = Normalize(request.DescripcionGeneral);
        order.EspecificacionesGenerales = Normalize(request.EspecificacionesGenerales);
        order.ObservacionesInternas = Normalize(request.ObservacionesInternas);
        order.NotasCliente = Normalize(request.NotasCliente);
        order.UsuarioUltimaActualizacionId = userId;
        order.FechaActualizacion = DateTime.UtcNow;
        context.DetallePedidosPersonalizados.RemoveRange(order.Detalles);
        order.Detalles.Clear();
        foreach (var line in draft.Lines) order.Detalles.Add(line);
        context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId, TipoBitacora.PEDIDO_EDITADO, "pedidos_personalizados",
            order.PedidoPersonalizadoId, $"Pedido {order.CodigoPedido} editado.",
            before, Snapshot(order))));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await _query.GetByIdAsync(order.PedidoPersonalizadoId, cancellationToken))!;
    }

    public async Task<CustomOrderDetail> ConfirmAsync(
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CONFIRMAR, cancellationToken);
        return await ChangeStateInternalAsync(new(
            pedidoPersonalizadoId, EstadoPedidoPersonalizado.CONFIRMADO, DateTime.Now,
            "Pedido confirmado."), TipoBitacora.PEDIDO_CONFIRMADO, cancellationToken);
    }

    public async Task<CustomOrderDetail> ConvertQuotationAsync(
        int cotizacionId,
        DateTime fechaPrometida,
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CREAR, cancellationToken);
        var userId = CurrentUserId();
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var quotation = await context.Cotizaciones
            .Include(c => c.Detalles)
            .Include(c => c.PedidoPersonalizado)
            .FirstOrDefaultAsync(c => c.CotizacionId == cotizacionId, cancellationToken)
            ?? throw new InvalidOperationException("La cotizacion no existe.");
        if (quotation.Estado != EstadoCotizacion.ACEPTADA)
            throw new InvalidOperationException("Solo una cotizacion aceptada puede convertirse en pedido.");
        if (quotation.PedidoPersonalizado is not null)
            throw new InvalidOperationException("La cotizacion ya fue convertida en pedido.");
        if (quotation.Detalles.Count == 0)
            throw new InvalidOperationException("La cotizacion no tiene lineas validas.");
        if (fechaPrometida.Date < DateTime.Today)
            throw new ArgumentException("La fecha prometida no puede ser anterior a hoy.");

        var now = DateTime.UtcNow;
        var order = new PedidoPersonalizado
        {
            CodigoPedido = $"TMP-{Guid.NewGuid():N}"[..30],
            CotizacionId = quotation.CotizacionId,
            ClienteId = quotation.ClienteId,
            NombreDestinatarioSnapshot = quotation.NombreDestinatarioSnapshot,
            TelefonoSnapshot = quotation.TelefonoSnapshot,
            CorreoSnapshot = quotation.CorreoSnapshot,
            DireccionSnapshot = quotation.DireccionSnapshot,
            Estado = EstadoPedidoPersonalizado.CONFIRMADO,
            Prioridad = PrioridadPedido.NORMAL,
            FechaPedido = DateTime.Today,
            FechaPrometida = fechaPrometida.Date,
            SubtotalCentavos = quotation.SubtotalCentavos,
            DescuentoGeneralCentavos = quotation.DescuentoGeneralCentavos,
            TotalEstimadoCentavos = quotation.TotalCentavos,
            EstadoFinanciero = EstadoFinancieroPedido.SIN_PAGO,
            SaldoPendienteCentavos = quotation.TotalCentavos,
            DescripcionGeneral = $"Pedido originado desde {quotation.CodigoCotizacion}.",
            NotasCliente = quotation.CondicionesComerciales,
            ObservacionesInternas = quotation.Observaciones,
            UsuarioCreadorId = userId,
            UsuarioUltimaActualizacionId = userId,
            FechaCreacion = now,
            FechaActualizacion = now
        };
        foreach (var line in quotation.Detalles)
        {
            order.Detalles.Add(new DetallePedidoPersonalizado
            {
                ProductoId = line.ProductoId,
                CodigoProductoSnapshot = line.CodigoProductoSnapshot,
                NombreDescripcionSnapshot = line.NombreDescripcionSnapshot,
                Cantidad = line.Cantidad,
                PrecioUnitarioCentavos = line.PrecioUnitarioCentavos,
                DescuentoLineaCentavos = line.DescuentoLineaCentavos,
                SubtotalCentavos = line.SubtotalCentavos,
                Observaciones = line.Observaciones
            });
        }
        context.PedidosPersonalizados.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        order.CodigoPedido = $"PED-{order.PedidoPersonalizadoId:000000}";
        order.HistorialEstados.Add(NewHistory(order.PedidoPersonalizadoId, null,
            EstadoPedidoPersonalizado.CONFIRMADO,
            $"Creado desde {quotation.CodigoCotizacion}.", userId, now));
        var previous = quotation.Estado;
        quotation.Estado = EstadoCotizacion.CONVERTIDA;
        quotation.UsuarioUltimaActualizacionId = userId;
        quotation.FechaActualizacion = now;
        quotation.FechaCierre = now;
        quotation.HistorialEstados.Add(new HistorialEstadoCotizacion
        {
            CotizacionId = quotation.CotizacionId,
            EstadoAnterior = previous,
            EstadoNuevo = EstadoCotizacion.CONVERTIDA,
            Motivo = $"Convertida en pedido {order.CodigoPedido}.",
            UsuarioId = userId,
            Fecha = now
        });
        await context.SaveChangesAsync(cancellationToken);
        foreach (var line in order.Detalles.Where(d => d.ProductoId.HasValue))
        {
            line.Producto = await context.Productos
                .FirstAsync(p => p.ProductoId == line.ProductoId, cancellationToken);
        }
        await CreateReservationsAsync(context, order, userId, cancellationToken);
        context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId, TipoBitacora.COTIZACION_CONVERTIDA_PEDIDO, "pedidos_personalizados",
            order.PedidoPersonalizadoId,
            $"Cotizacion {quotation.CodigoCotizacion} convertida en pedido {order.CodigoPedido}.",
            DatosDespues: Snapshot(order))));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await _query.GetByIdAsync(order.PedidoPersonalizadoId, cancellationToken))!;
    }

    public async Task<CustomOrderDetail> ChangeStateAsync(
        CustomOrderStateChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Estado == EstadoPedidoPersonalizado.CANCELADO)
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CANCELAR, cancellationToken);
            if (string.IsNullOrWhiteSpace(request.Motivo))
                throw new ArgumentException("El motivo de cancelacion es obligatorio.");
            return await ChangeStateInternalAsync(request, TipoBitacora.PEDIDO_CANCELADO, cancellationToken);
        }
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_ESTADO_CAMBIAR, cancellationToken);
        return await ChangeStateInternalAsync(request, TipoBitacora.PEDIDO_ESTADO_MODIFICADO, cancellationToken);
    }

    public async Task<CustomOrderDetail> DuplicateAsync(
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDO_CREAR, cancellationToken);
        var source = await _query.GetByIdAsync(pedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");
        var copy = await CreateDraftAsync(new(
            null, source.ClienteId, source.Destinatario, source.Telefono, source.Correo,
            source.Direccion, DateTime.Today,
            DateTime.Today.AddDays(Math.Max(1, (source.FechaPrometida.Date - source.FechaPedido.Date).Days)),
            source.Prioridad, source.DescuentoGeneralCentavos, source.DescripcionGeneral,
            source.EspecificacionesGenerales, source.ObservacionesInternas, source.NotasCliente,
            source.Detalles.Select(d => new CustomOrderLineRequest(
                d.ProductoId, d.Descripcion, d.Cantidad, d.PrecioUnitarioCentavos,
                d.DescuentoLineaCentavos, d.Especificaciones, d.Color, d.Medidas,
                d.Material, d.TextoPersonalizado, d.Observaciones, d.ProductoSkuId)).ToList()), cancellationToken);
        await AuditAsync(CurrentUserId(), TipoBitacora.PEDIDO_DUPLICADO, null,
            $"Pedido {source.CodigoPedido} duplicado como {copy.CodigoPedido}.",
            cancellationToken, copy.PedidoPersonalizadoId);
        return copy;
    }

    private async Task<CustomOrderDetail> ChangeStateInternalAsync(
        CustomOrderStateChangeRequest request,
        TipoBitacora auditType,
        CancellationToken cancellationToken)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var order = await context.PedidosPersonalizados
            .Include(p => p.HistorialEstados)
            .Include(p => p.Detalles).ThenInclude(d => d.Producto)
            .Include(p => p.Reservas)
            .FirstOrDefaultAsync(p => p.PedidoPersonalizadoId == request.PedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");
        ValidateTransition(order.Estado, request.Estado);
        var effective = request.FechaEfectiva;
        if (effective > DateTime.Now.AddMinutes(5))
            throw new ArgumentException("La fecha efectiva no puede estar en el futuro.");
        var userId = CurrentUserId();
        var previous = order.Estado;
        order.Estado = request.Estado;
        order.UsuarioUltimaActualizacionId = userId;
        order.FechaActualizacion = DateTime.UtcNow;
        if (request.Estado == EstadoPedidoPersonalizado.EN_PRODUCCION) order.FechaInicio = effective;
        if (request.Estado == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA) order.FechaFinalizacion = effective;
        if (request.Estado == EstadoPedidoPersonalizado.ENTREGADO) order.FechaEntregaReal = effective;
        if (request.Estado == EstadoPedidoPersonalizado.CANCELADO)
        {
            order.FechaCancelacion = effective;
            order.MotivoCancelacion = request.Motivo!.Trim();
            await ReleaseReservationsAsync(context, order, request.Motivo, userId, cancellationToken);
        }
        if (request.Estado == EstadoPedidoPersonalizado.CONFIRMADO)
            await CreateReservationsAsync(context, order, userId, cancellationToken);
        order.HistorialEstados.Add(NewHistory(order.PedidoPersonalizadoId, previous,
            request.Estado, Normalize(request.Motivo), userId, effective.ToUniversalTime()));
        context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId, auditType, "pedidos_personalizados", order.PedidoPersonalizadoId,
            $"Pedido {order.CodigoPedido}: {previous} -> {request.Estado}. {Normalize(request.Motivo)}",
            DatosDespues: Snapshot(order))));
        await PersistenceGuard.SaveChangesAsync(
            context,
            "custom-order-state-change",
            ConcurrencyMessage,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await _query.GetByIdAsync(order.PedidoPersonalizadoId, cancellationToken))!;
    }

    private async Task<OrderDraft> BuildDraftAsync(
        AppDbContext context,
        CustomOrderSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FechaPrometida.Date < request.FechaPedido.Date)
            throw new ArgumentException("La fecha prometida no puede ser anterior a la fecha del pedido.");
        if (!Enum.IsDefined(request.Prioridad))
            throw new ArgumentException("La prioridad no es valida.");
        if (request.Detalles.Count == 0)
            throw new ArgumentException("El pedido debe tener al menos una linea.");

        Cliente? client = null;
        if (request.ClienteId.HasValue)
        {
            client = await context.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClienteId == request.ClienteId, cancellationToken)
                ?? throw new InvalidOperationException("El cliente no existe.");
            if (client.Estado != EstadoCliente.ACTIVO)
                throw new InvalidOperationException("No se puede crear un pedido para un cliente inactivo.");
        }
        var name = Normalize(request.NombreDestinatario) ?? client?.NombreCompleto;
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre del destinatario es obligatorio.");

        var productIds = request.Detalles.Where(d => d.ProductoId.HasValue)
            .Select(d => d.ProductoId!.Value).Distinct().ToList();
        var products = await context.Productos
            .Include(p => p.Skus)
            .Where(p => productIds.Contains(p.ProductoId))
            .ToDictionaryAsync(p => p.ProductoId, cancellationToken);
        if (products.Count != productIds.Count)
            throw new InvalidOperationException("Uno de los productos no existe.");

        var lines = new List<DetallePedidoPersonalizado>();
        foreach (var input in request.Detalles)
        {
            if (input.Cantidad <= 0) throw new ArgumentException("La cantidad debe ser mayor que cero.");
            if (input.PrecioUnitarioCentavos < 0) throw new ArgumentException("El precio unitario no puede ser negativo.");
            var gross = checked(input.Cantidad * input.PrecioUnitarioCentavos);
            if (input.DescuentoLineaCentavos < 0 || input.DescuentoLineaCentavos > gross)
                throw new ArgumentException("El descuento de linea no es valido.");
            Producto? product = input.ProductoId.HasValue ? products[input.ProductoId.Value] : null;
            if (product is null && input.ProductoSkuId.HasValue)
                throw new InvalidOperationException("No se puede seleccionar un SKU sin producto.");
            if (product is not null && product.Estado != EstadoProducto.ACTIVO)
                throw new InvalidOperationException($"El producto '{product.Nombre}' no esta activo.");
            ProductoSku? sku = null;
            if (product is not null)
            {
                sku = product.Skus.Count == 0 && !input.ProductoSkuId.HasValue
                    ? await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                        context, product.ProductoId, cancellationToken)
                    : ResolveOrderSku(product, input.ProductoSkuId, allowHistoricalFallback: false);
            }
            var description = Normalize(input.Descripcion) ?? product?.Nombre;
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("La descripcion de la linea es obligatoria.");
            lines.Add(new DetallePedidoPersonalizado
            {
                ProductoId = product?.ProductoId,
                ProductoSku = sku,
                CodigoProductoSnapshot = product?.CodigoInterno,
                CodigoSkuSnapshot = sku?.CodigoSku,
                NombreVarianteSnapshot = sku?.NombreVariante,
                NombreDescripcionSnapshot = description,
                Cantidad = input.Cantidad,
                PrecioUnitarioCentavos = input.PrecioUnitarioCentavos,
                DescuentoLineaCentavos = input.DescuentoLineaCentavos,
                SubtotalCentavos = gross - input.DescuentoLineaCentavos,
                Especificaciones = Normalize(input.Especificaciones),
                Color = Normalize(input.Color),
                Medidas = Normalize(input.Medidas),
                Material = Normalize(input.Material),
                TextoPersonalizado = Normalize(input.TextoPersonalizado),
                Observaciones = Normalize(input.Observaciones)
            });
        }
        var subtotal = lines.Sum(l => l.SubtotalCentavos);
        if (request.DescuentoGeneralCentavos < 0 || request.DescuentoGeneralCentavos > subtotal)
            throw new ArgumentException("El descuento general debe estar entre cero y el subtotal.");
        return new(client?.ClienteId, name,
            Normalize(request.Telefono) ?? client?.TelefonoCelular,
            Normalize(request.Correo) ?? client?.CorreoElectronico,
            Normalize(request.Direccion) ?? client?.Direccion,
            subtotal, lines);
    }

    private static void ValidateTransition(EstadoPedidoPersonalizado current, EstadoPedidoPersonalizado target)
    {
        var allowed = target == EstadoPedidoPersonalizado.CANCELADO
            ? current is EstadoPedidoPersonalizado.BORRADOR or EstadoPedidoPersonalizado.CONFIRMADO
                or EstadoPedidoPersonalizado.EN_PRODUCCION or EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA
            : current switch
            {
                EstadoPedidoPersonalizado.BORRADOR => target == EstadoPedidoPersonalizado.CONFIRMADO,
                EstadoPedidoPersonalizado.CONFIRMADO => target == EstadoPedidoPersonalizado.EN_PRODUCCION,
                EstadoPedidoPersonalizado.EN_PRODUCCION => target == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA,
                EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA => target == EstadoPedidoPersonalizado.ENTREGADO,
                _ => false
            };
        if (!allowed) throw new InvalidOperationException($"No se puede cambiar un pedido de {current} a {target}.");
    }

    private int CurrentUserId() => _session.UsuarioActual?.UsuarioId
        ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static HistorialEstadoPedido NewHistory(
        int orderId, EstadoPedidoPersonalizado? previous, EstadoPedidoPersonalizado target,
        string? reason, int userId, DateTime date) => new()
        {
            PedidoPersonalizadoId = orderId,
            EstadoAnterior = previous,
            EstadoNuevo = target,
            Motivo = reason,
            UsuarioId = userId,
            Fecha = date
        };
    private Task AuditAsync(
        int userId, TipoBitacora type, PedidoPersonalizado? order, string description,
        CancellationToken cancellationToken, int? entityId = null) =>
        _audit.RegistrarAsync(new AuditEntryRequest(
            userId, type, "pedidos_personalizados",
            entityId ?? order?.PedidoPersonalizadoId, description,
            DatosDespues: order is null ? null : Snapshot(order)), cancellationToken);
    private static string Snapshot(PedidoPersonalizado p) =>
        $"Codigo={p.CodigoPedido}; CotizacionId={p.CotizacionId}; ClienteId={p.ClienteId}; " +
        $"Destinatario={p.NombreDestinatarioSnapshot}; Estado={p.Estado}; Prioridad={p.Prioridad}; " +
        $"Subtotal={p.SubtotalCentavos}; Descuento={p.DescuentoGeneralCentavos}; Total={p.TotalEstimadoCentavos}; Lineas={p.Detalles.Count}";

    private async Task CreateReservationsAsync(
        AppDbContext context,
        PedidoPersonalizado order,
        int userId,
        CancellationToken cancellationToken)
    {
        foreach (var line in order.Detalles.Where(d => d.ProductoId.HasValue))
        {
            var product = line.Producto!;
            var alreadyReserved = await context.ReservasInventarioPedido.AnyAsync(
                r => r.DetallePedidoPersonalizadoId == line.DetallePedidoPersonalizadoId,
                cancellationToken);
            if (alreadyReserved) continue;

            var sku = line.ProductoSkuId.HasValue
                ? await InventoryStockEngine.GetSkuAsync(context, line.ProductoSkuId.Value, cancellationToken)
                : await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                    context, product.ProductoId, cancellationToken);
            if (sku.ProductoId != product.ProductoId)
                throw new InvalidOperationException("El SKU de la linea no pertenece al producto del pedido.");
            line.ProductoSku = sku;
            line.CodigoSkuSnapshot ??= sku.CodigoSku;
            line.NombreVarianteSnapshot ??= sku.NombreVariante;
            var reason = $"Reserva automatica al confirmar {order.CodigoPedido}.";
            var allocations = InventoryStockEngine.Reserve(
                context, sku, line.Cantidad, userId, reason, DateTime.UtcNow);
            foreach (var allocation in allocations)
            {
                context.ReservasInventarioPedido.Add(new ReservaInventarioPedido
                {
                    PedidoPersonalizadoId = order.PedidoPersonalizadoId,
                    DetallePedidoPersonalizadoId = line.DetallePedidoPersonalizadoId,
                    ProductoId = product.ProductoId,
                    ProductoSku = sku,
                    Ubicacion = allocation.Balance.Ubicacion,
                    CantidadReservada = allocation.Quantity,
                    Estado = EstadoReservaInventarioPedido.ACTIVA,
                    FechaReserva = DateTime.UtcNow,
                    UsuarioId = userId,
                    Motivo = reason
                });
            }
            context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
                userId, TipoBitacora.RESERVA_PEDIDO_CREADA, "reservas_inventario_pedido",
                order.PedidoPersonalizadoId,
                $"Reservadas {line.Cantidad} unidad(es) de {product.CodigoInterno} para {order.CodigoPedido}.")));
        }
    }

    private static async Task ReleaseReservationsAsync(
        AppDbContext context,
        PedidoPersonalizado order,
        string? reason,
        int userId,
        CancellationToken cancellationToken)
    {
        var active = order.Reservas.Where(r => r.Estado == EstadoReservaInventarioPedido.ACTIVA).ToList();
        if (active.Count == 0) return;
        foreach (var reservation in active)
        {
            var quantity = reservation.CantidadReservada - reservation.CantidadConsumida;
            var sku = await InventoryStockEngine.GetSkuAsync(
                context, reservation.ProductoSkuId, cancellationToken);
            InventoryStockEngine.ReleaseReservation(context, sku,
                reservation.Ubicacion,
                quantity, userId, Normalize(reason) ?? "Cancelacion de pedido", DateTime.UtcNow);
            reservation.ProductoSku = sku;
            reservation.CantidadLiberada = quantity;
            reservation.Estado = EstadoReservaInventarioPedido.LIBERADA;
            reservation.FechaLiberacion = DateTime.UtcNow;
            reservation.Motivo = Normalize(reason);
        }
        context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId, TipoBitacora.RESERVA_PEDIDO_LIBERADA, "reservas_inventario_pedido",
            order.PedidoPersonalizadoId,
            $"Reservas del pedido {order.CodigoPedido} liberadas por cancelacion.")));
    }

    private ValueTask<AppDbContextLease> CreateContextAsync(CancellationToken cancellationToken) =>
        AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
    private sealed record OrderDraft(
        int? ClienteId, string Nombre, string? Telefono, string? Correo,
        string? Direccion, long Subtotal, IReadOnlyList<DetallePedidoPersonalizado> Lines);

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
}
