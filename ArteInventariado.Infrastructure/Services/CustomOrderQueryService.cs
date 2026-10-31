using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomOrderQueryService : ICustomOrderQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissions;

    [ActivatorUtilitiesConstructor]
    public CustomOrderQueryService(IDbContextFactory<AppDbContext> contextFactory, IEmployeePermissionService permissions)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
    }

    internal CustomOrderQueryService(AppDbContext context, IEmployeePermissionService permissions)
    {
        _legacyContext = context;
        _permissions = permissions;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar pedidos.");

    public async Task<IReadOnlyList<CustomOrderSummary>> SearchAsync(
        CustomOrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER, cancellationToken);
        var canViewFinances = await _permissions.HasPermissionAsync(
            PermisoEmpleado.PEDIDO_FINANZAS_VER, cancellationToken: cancellationToken);
        var today = DateTime.Today;
        var rows = await ApplyFilter(BaseQuery(), filter, today)
            .OrderBy(p => p.Estado == EstadoPedidoPersonalizado.ENTREGADO || p.Estado == EstadoPedidoPersonalizado.CANCELADO)
            .ThenBy(p => p.FechaPrometida)
            .ThenByDescending(p => p.PedidoPersonalizadoId)
            .Skip((Math.Max(1, filter.Page) - 1) * Math.Clamp(filter.PageSize, 1, 250))
            .Take(Math.Clamp(filter.PageSize, 1, 250))
            .Select(p => new
            {
                p.PedidoPersonalizadoId,
                p.CodigoPedido,
                CodigoCotizacion = p.Cotizacion != null ? p.Cotizacion.CodigoCotizacion : null,
                p.FechaPedido,
                p.FechaPrometida,
                Destinatario = p.NombreDestinatarioSnapshot,
                p.Estado,
                p.Prioridad,
                Lineas = p.Detalles.Count,
                p.TotalEstimadoCentavos,
                p.EstadoFinanciero,
                p.TotalPagadoCentavos,
                p.TotalReembolsadoCentavos,
                p.SaldoPendienteCentavos,
                p.VentaId,
                Usuario = p.UsuarioCreador.NombreUsuario
            })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new CustomOrderSummary(
            p.PedidoPersonalizadoId, p.CodigoPedido, p.CodigoCotizacion, p.FechaPedido,
            p.FechaPrometida, p.Destinatario, p.Estado, p.Prioridad, p.Lineas,
            p.TotalEstimadoCentavos, (p.FechaPrometida.Date - today).Days,
            p.Estado != EstadoPedidoPersonalizado.ENTREGADO &&
                p.Estado != EstadoPedidoPersonalizado.CANCELADO &&
                p.FechaPrometida.Date < today,
            p.Usuario,
            canViewFinances ? p.EstadoFinanciero : EstadoFinancieroPedido.SIN_PAGO,
            canViewFinances ? p.TotalPagadoCentavos : 0,
            canViewFinances ? p.TotalReembolsadoCentavos : 0,
            canViewFinances ? p.SaldoPendienteCentavos : 0,
            canViewFinances ? p.VentaId : null)).ToList();
    }

    public async Task<CustomOrderDetail?> GetByIdAsync(
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER, cancellationToken);
        var order = await BaseQuery()
            .Include(p => p.Cliente)
            .Include(p => p.Cotizacion)
            .Include(p => p.Venta)
            .Include(p => p.Detalles)
            .Include(p => p.HistorialEstados).ThenInclude(h => h.Usuario)
            .FirstOrDefaultAsync(p => p.PedidoPersonalizadoId == pedidoPersonalizadoId, cancellationToken);
        if (order is null) return null;
        var includeHistory = await _permissions.HasPermissionAsync(
            PermisoEmpleado.PEDIDO_HISTORIAL_VER,
            cancellationToken: cancellationToken);
        var includeFinances = await _permissions.HasPermissionAsync(
            PermisoEmpleado.PEDIDO_FINANZAS_VER,
            cancellationToken: cancellationToken);
        return ToDetail(order, includeHistory, includeFinances);
    }

    public async Task<CustomOrderStats> GetStatsAsync(
        CustomOrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER, cancellationToken);
        var today = DateTime.Today;
        var rows = await ApplyFilter(BaseQuery(), filter, today)
            .Select(p => new { p.Estado, p.FechaPrometida })
            .ToListAsync(cancellationToken);
        return new(
            rows.Count(x => x.Estado == EstadoPedidoPersonalizado.BORRADOR),
            rows.Count(x => x.Estado == EstadoPedidoPersonalizado.CONFIRMADO),
            rows.Count(x => x.Estado == EstadoPedidoPersonalizado.EN_PRODUCCION),
            rows.Count(x => x.Estado == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA),
            rows.Count(x => x.Estado != EstadoPedidoPersonalizado.ENTREGADO &&
                x.Estado != EstadoPedidoPersonalizado.CANCELADO &&
                x.FechaPrometida < today));
    }

    private IQueryable<PedidoPersonalizado> BaseQuery() => Context.PedidosPersonalizados
        .AsNoTracking()
        .Include(p => p.Cotizacion)
        .Include(p => p.Cliente)
        .Include(p => p.UsuarioCreador)
        .Include(p => p.UsuarioUltimaActualizacion)
        .Include(p => p.Detalles);

    private static IQueryable<PedidoPersonalizado> ApplyFilter(
        IQueryable<PedidoPersonalizado> query,
        CustomOrderFilter filter,
        DateTime today)
    {
        if (filter.Estado.HasValue) query = query.Where(p => p.Estado == filter.Estado);
        if (filter.Prioridad.HasValue) query = query.Where(p => p.Prioridad == filter.Prioridad);
        if (filter.ClienteId.HasValue) query = query.Where(p => p.ClienteId == filter.ClienteId);
        if (filter.SoloAtrasados)
        {
            query = query.Where(p => p.Estado != EstadoPedidoPersonalizado.ENTREGADO &&
                p.Estado != EstadoPedidoPersonalizado.CANCELADO && p.FechaPrometida < today);
        }
        if (filter.UsarFechas)
        {
            if (filter.Desde.HasValue) query = query.Where(p => p.FechaPedido >= filter.Desde.Value.Date);
            if (filter.Hasta.HasValue)
            {
                var end = filter.Hasta.Value.Date.AddDays(1);
                query = query.Where(p => p.FechaPedido < end);
            }
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            query = query.Where(p =>
                p.CodigoPedido.Contains(text) ||
                p.NombreDestinatarioSnapshot.Contains(text) ||
                (p.Cotizacion != null && p.Cotizacion.CodigoCotizacion.Contains(text)) ||
                (p.Cliente != null && (p.Cliente.CodigoCliente.Contains(text) || p.Cliente.NombreCompleto.Contains(text))) ||
                p.Detalles.Any(d => d.NombreDescripcionSnapshot.Contains(text) ||
                    (d.CodigoProductoSnapshot != null && d.CodigoProductoSnapshot.Contains(text)) ||
                    (d.CodigoSkuSnapshot != null && d.CodigoSkuSnapshot.Contains(text)) ||
                    (d.NombreVarianteSnapshot != null && d.NombreVarianteSnapshot.Contains(text))));
        }
        return query;
    }

    private static CustomOrderDetail ToDetail(PedidoPersonalizado p, bool includeHistory, bool includeFinances) => new(
        p.PedidoPersonalizadoId, p.CodigoPedido, p.CotizacionId, p.Cotizacion?.CodigoCotizacion,
        p.ClienteId, p.Cliente?.CodigoCliente, p.NombreDestinatarioSnapshot, p.TelefonoSnapshot,
        p.CorreoSnapshot, p.DireccionSnapshot, p.Estado, p.Prioridad, p.FechaPedido,
        p.FechaPrometida, p.FechaInicio, p.FechaFinalizacion, p.FechaEntregaReal,
        p.SubtotalCentavos, p.DescuentoGeneralCentavos, p.TotalEstimadoCentavos,
        p.DescripcionGeneral, p.EspecificacionesGenerales, p.ObservacionesInternas,
        p.NotasCliente, p.UsuarioCreador.NombreUsuario, p.UsuarioUltimaActualizacion.NombreUsuario,
        p.FechaCreacion, p.FechaActualizacion, p.FechaCancelacion, p.MotivoCancelacion,
        p.Detalles.OrderBy(d => d.DetallePedidoPersonalizadoId).Select(d => new CustomOrderLineDetail(
            d.DetallePedidoPersonalizadoId, d.ProductoId, d.CodigoProductoSnapshot,
            d.NombreDescripcionSnapshot, d.Cantidad, d.PrecioUnitarioCentavos,
            d.DescuentoLineaCentavos, d.SubtotalCentavos, d.Especificaciones, d.Color,
            d.Medidas, d.Material, d.TextoPersonalizado, d.Observaciones,
            d.ProductoSkuId, d.CodigoSkuSnapshot, d.NombreVarianteSnapshot)).ToList(),
        (includeHistory ? p.HistorialEstados : []).OrderByDescending(h => h.Fecha)
            .Select(h => new CustomOrderStatusHistoryRow(
                h.HistorialEstadoPedidoId, h.EstadoAnterior, h.EstadoNuevo,
                h.Motivo, h.Usuario.NombreUsuario, h.Fecha)).ToList(),
        includeFinances ? p.EstadoFinanciero : EstadoFinancieroPedido.SIN_PAGO,
        includeFinances ? p.TotalPagadoCentavos : 0,
        includeFinances ? p.TotalReembolsadoCentavos : 0,
        includeFinances ? p.SaldoPendienteCentavos : 0,
        includeFinances ? p.VentaId : null,
        includeFinances ? p.Venta?.NumeroVenta : null);
}
