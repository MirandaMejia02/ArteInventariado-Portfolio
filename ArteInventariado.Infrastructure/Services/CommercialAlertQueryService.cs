using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class CommercialAlertQueryService : ICommercialAlertQueryService
{
    private const int UpcomingDays = 3;
    private const int NoResponseDays = 3;
    private const int NoUpdateDays = 3;

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissions;

    [ActivatorUtilitiesConstructor]
    public CommercialAlertQueryService(IDbContextFactory<AppDbContext> contextFactory, IEmployeePermissionService permissions)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
    }

    internal CommercialAlertQueryService(AppDbContext context, IEmployeePermissionService permissions)
    {
        _legacyContext = context;
        _permissions = permissions;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar alertas comerciales.");

    public async Task<CommercialAlertSearchResult> SearchAsync(
        CommercialAlertFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureAccessAsync(cancellationToken);
        var rows = await BuildAlertsAsync(cancellationToken);
        IEnumerable<CommercialAlertRow> filtered = rows;

        if (filter.Tipo.HasValue)
            filtered = filtered.Where(x => x.Tipo == filter.Tipo.Value);
        if (!string.IsNullOrWhiteSpace(filter.Estado))
            filtered = filtered.Where(x => x.Estado.Equals(filter.Estado.Trim(), StringComparison.OrdinalIgnoreCase));
        if (filter.UsarFechas)
        {
            if (filter.Desde.HasValue)
                filtered = filtered.Where(x => x.FechaRelevante.Date >= filter.Desde.Value.Date);
            if (filter.Hasta.HasValue)
                filtered = filtered.Where(x => x.FechaRelevante.Date <= filter.Hasta.Value.Date);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            filtered = filtered.Where(x =>
                x.Documento.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.ClienteDestinatario.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Descripcion.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.UsuarioResponsable.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return new(BuildSummary(rows), filtered
            .OrderByDescending(x => x.DiasAtraso)
            .ThenBy(x => x.FechaRelevante)
            .ThenBy(x => x.Documento)
            .ToList());
    }

    public async Task<CommercialAlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureAccessAsync(cancellationToken);
        return BuildSummary(await BuildAlertsAsync(cancellationToken));
    }

    private async Task<List<CommercialAlertRow>> BuildAlertsAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var nowUtc = DateTime.UtcNow;
        var rows = new List<CommercialAlertRow>();

        var quotations = await Context.Cotizaciones.AsNoTracking()
            .Include(c => c.UsuarioUltimaActualizacion)
            .Where(c => c.Estado == EstadoCotizacion.EMITIDA)
            .ToListAsync(cancellationToken);
        foreach (var quotation in quotations)
            AddQuotationAlerts(rows, quotation, today);

        var orders = await Context.PedidosPersonalizados.AsNoTracking()
            .Include(p => p.UsuarioUltimaActualizacion)
            .Include(p => p.Detalles)
            .Include(p => p.Reservas)
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
            AddOrderAlerts(rows, order, today, nowUtc);

        return rows;
    }

    private static void AddQuotationAlerts(List<CommercialAlertRow> rows, Cotizacion quotation, DateTime today)
    {
        var days = (quotation.FechaVencimiento.Date - today).Days;
        if (days < 0)
            rows.Add(QuotationAlert(quotation, CommercialAlertType.COTIZACION_VENCIDA,
                "Cotizacion vencida sin respuesta.", -days));
        else if (days == 0)
            rows.Add(QuotationAlert(quotation, CommercialAlertType.COTIZACION_VENCE_HOY,
                "La cotizacion vence hoy.", 0));
        else if (days <= UpcomingDays)
            rows.Add(QuotationAlert(quotation, CommercialAlertType.COTIZACION_PROXIMA_VENCER,
                $"La cotizacion vence en {days} dia(s).", -days));

        var issuedDate = quotation.FechaEmision?.ToLocalTime().Date ?? quotation.Fecha.Date;
        var unansweredDays = (today - issuedDate).Days;
        if (days >= 0 && unansweredDays >= NoResponseDays)
            rows.Add(QuotationAlert(quotation, CommercialAlertType.COTIZACION_SIN_RESPUESTA,
                $"Emitida sin respuesta durante {unansweredDays} dia(s).", unansweredDays));
    }

    private static void AddOrderAlerts(
        List<CommercialAlertRow> rows,
        PedidoPersonalizado order,
        DateTime today,
        DateTime nowUtc)
    {
        var active = order.Estado is not (EstadoPedidoPersonalizado.ENTREGADO or EstadoPedidoPersonalizado.CANCELADO);
        var days = (order.FechaPrometida.Date - today).Days;
        if (active && days < 0)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_ATRASADO,
                $"Pedido atrasado {-days} dia(s).", -days));
        else if (active && days == 0)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_VENCE_HOY,
                "El pedido debe entregarse hoy.", 0));
        else if (active && days <= UpcomingDays)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_PROXIMO_ENTREGAR,
                $"El pedido debe entregarse en {days} dia(s).", -days));

        if (order.Estado == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_LISTO_ENTREGA,
                "Pedido listo para entregar y convertir en venta.", Math.Max(0, -days)));

        var staleDays = (nowUtc - order.FechaActualizacion).Days;
        if (order.Estado == EstadoPedidoPersonalizado.CONFIRMADO && !order.FechaInicio.HasValue && staleDays >= NoUpdateDays)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_CONFIRMADO_SIN_INICIAR,
                $"Confirmado sin iniciar produccion durante {staleDays} dia(s).", staleDays));
        if (order.Estado == EstadoPedidoPersonalizado.EN_PRODUCCION && staleDays >= NoUpdateDays)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_PRODUCCION_SIN_ACTUALIZAR,
                $"En produccion sin actualizacion durante {staleDays} dia(s).", staleDays));
        if (order.Estado == EstadoPedidoPersonalizado.ENTREGADO && order.SaldoPendienteCentavos > 0)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_ENTREGADO_CON_SALDO,
                "Pedido entregado con saldo pendiente.", 0));

        var refundable = Math.Max(0, order.TotalPagadoCentavos - order.TotalReembolsadoCentavos);
        if (order.Estado == EstadoPedidoPersonalizado.CANCELADO && refundable > 0)
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_CANCELADO_REEMBOLSO_PENDIENTE,
                $"Pedido cancelado con {Money(refundable)} pendiente de reembolso.", 0, refundable));

        if (order.Estado is not (EstadoPedidoPersonalizado.BORRADOR or EstadoPedidoPersonalizado.CANCELADO or EstadoPedidoPersonalizado.ENTREGADO) &&
            order.Detalles.Any(d => !d.ProductoId.HasValue))
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_LINEA_SIN_PRODUCTO,
                "Existen lineas descriptivas pendientes de vincular a producto.", 0));

        if (HasInconsistentReservations(order))
            rows.Add(OrderAlert(order, CommercialAlertType.PEDIDO_RESERVA_INCONSISTENTE,
                "Las reservas activas no coinciden con las lineas del pedido.", 0));
    }

    private static bool HasInconsistentReservations(PedidoPersonalizado order)
    {
        var activeReservations = order.Reservas
            .Where(r => r.Estado == EstadoReservaInventarioPedido.ACTIVA)
            .ToList();
        if (order.Estado is EstadoPedidoPersonalizado.CANCELADO or EstadoPedidoPersonalizado.ENTREGADO)
            return activeReservations.Count > 0;
        if (order.Estado is not (EstadoPedidoPersonalizado.CONFIRMADO or EstadoPedidoPersonalizado.EN_PRODUCCION or EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA))
            return false;

        foreach (var line in order.Detalles.Where(d => d.ProductoId.HasValue))
        {
            var reserved = activeReservations
                .Where(r => r.DetallePedidoPersonalizadoId == line.DetallePedidoPersonalizadoId && r.ProductoId == line.ProductoId)
                .Sum(r => r.CantidadReservada - r.CantidadConsumida - r.CantidadLiberada);
            if (reserved != line.Cantidad)
                return true;
        }
        return activeReservations.Any(r => order.Detalles.All(d => d.DetallePedidoPersonalizadoId != r.DetallePedidoPersonalizadoId));
    }

    private static CommercialAlertRow QuotationAlert(
        Cotizacion quotation,
        CommercialAlertType type,
        string description,
        int days) => new(
            type, CommercialDocumentType.COTIZACION, quotation.CotizacionId,
            quotation.CodigoCotizacion, quotation.NombreDestinatarioSnapshot,
            quotation.FechaVencimiento, quotation.Estado.ToString(), "Normal",
            quotation.TotalCentavos, 0, quotation.TotalCentavos, days,
            quotation.UsuarioUltimaActualizacion.NombreUsuario, description);

    private static CommercialAlertRow OrderAlert(
        PedidoPersonalizado order,
        CommercialAlertType type,
        string description,
        int days,
        long? displayedBalance = null) => new(
            type, CommercialDocumentType.PEDIDO, order.PedidoPersonalizadoId,
            order.CodigoPedido, order.NombreDestinatarioSnapshot,
            order.FechaPrometida, order.Estado.ToString(), order.Prioridad.ToString(),
            order.TotalEstimadoCentavos, order.TotalPagadoCentavos,
            displayedBalance ?? order.SaldoPendienteCentavos, days,
            order.UsuarioUltimaActualizacion.NombreUsuario, description);

    private static CommercialAlertSummary BuildSummary(IReadOnlyCollection<CommercialAlertRow> rows) => new(
        rows.Count(x => x.Tipo == CommercialAlertType.COTIZACION_VENCIDA),
        rows.Count(x => x.Tipo == CommercialAlertType.PEDIDO_ATRASADO),
        rows.Count(x => x.Tipo == CommercialAlertType.PEDIDO_LISTO_ENTREGA),
        rows.Count(x => x.Tipo == CommercialAlertType.PEDIDO_ENTREGADO_CON_SALDO),
        rows.Count(x => x.Tipo == CommercialAlertType.PEDIDO_CANCELADO_REEMBOLSO_PENDIENTE),
        rows.Count);

    private async Task EnsureAccessAsync(CancellationToken cancellationToken)
    {
        var allowed = await _permissions.HasAnyPermissionAsync(
            [PermisoEmpleado.ALERTAS_COMERCIALES_VER, PermisoEmpleado.REPORTES_COMERCIALES_VER],
            cancellationToken: cancellationToken);
        if (!allowed)
            throw new InvalidOperationException("No tiene permiso para consultar alertas comerciales.");
    }

    private static string Money(long cents) => $"{cents / 100m:C2}";
}
