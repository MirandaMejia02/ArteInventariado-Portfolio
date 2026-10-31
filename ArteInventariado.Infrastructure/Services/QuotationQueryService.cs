using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class QuotationQueryService : IQuotationQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public QuotationQueryService(IDbContextFactory<AppDbContext> contextFactory, IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _permissionService = permissionService;
    }

    internal QuotationQueryService(AppDbContext context, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar cotizaciones.");

    public async Task<IReadOnlyList<QuotationSummary>> SearchAsync(
        QuotationFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COTIZACIONES_ACCEDER, cancellationToken);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 250);

        return await ApplyFilter(BaseQuery(), filter)
            .OrderByDescending(c => c.Fecha)
            .ThenByDescending(c => c.CotizacionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new QuotationSummary(
                c.CotizacionId,
                c.CodigoCotizacion,
                c.Fecha,
                c.FechaVencimiento,
                c.Estado,
                c.ClienteId,
                c.NombreDestinatarioSnapshot,
                c.Detalles.Count,
                c.TotalCentavos,
                c.UsuarioCreador.NombreUsuario,
                c.FechaActualizacion))
            .ToListAsync(cancellationToken);
    }

    public async Task<QuotationDetail?> GetByIdAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COTIZACIONES_ACCEDER, cancellationToken);

        var quotation = await BaseQuery()
            .Include(c => c.Cliente)
            .Include(c => c.Detalles)
            .Include(c => c.HistorialEstados)
                .ThenInclude(h => h.Usuario)
            .FirstOrDefaultAsync(c => c.CotizacionId == cotizacionId, cancellationToken);

        if (quotation is null)
        {
            return null;
        }

        var canViewHistory = await _permissionService.HasPermissionAsync(
            PermisoEmpleado.COTIZACION_HISTORIAL_VER,
            cancellationToken: cancellationToken);
        return ToDetail(quotation, canViewHistory);
    }

    public async Task<QuotationStats> GetStatsAsync(
        QuotationFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COTIZACIONES_ACCEDER, cancellationToken);
        var states = await ApplyFilter(BaseQuery(), filter)
            .Select(c => c.Estado)
            .ToListAsync(cancellationToken);

        return new QuotationStats(
            states.Count(s => s == EstadoCotizacion.BORRADOR),
            states.Count(s => s == EstadoCotizacion.EMITIDA),
            states.Count(s => s == EstadoCotizacion.ACEPTADA),
            states.Count(s => s == EstadoCotizacion.VENCIDA));
    }

    private IQueryable<Cotizacion> BaseQuery()
    {
        return Context.Cotizaciones
            .AsNoTracking()
            .Include(c => c.UsuarioCreador)
            .Include(c => c.UsuarioUltimaActualizacion)
            .Include(c => c.Detalles);
    }

    private static IQueryable<Cotizacion> ApplyFilter(
        IQueryable<Cotizacion> query,
        QuotationFilter filter)
    {
        if (filter.Estado.HasValue)
        {
            query = query.Where(c => c.Estado == filter.Estado.Value);
        }

        if (filter.UsarFechas)
        {
            if (filter.Desde.HasValue)
            {
                var from = filter.Desde.Value.Date;
                query = query.Where(c => c.Fecha >= from);
            }

            if (filter.Hasta.HasValue)
            {
                var to = filter.Hasta.Value.Date.AddDays(1);
                query = query.Where(c => c.Fecha < to);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(c =>
                c.CodigoCotizacion.Contains(search) ||
                c.NombreDestinatarioSnapshot.Contains(search) ||
                (c.Cliente != null &&
                    (c.Cliente.CodigoCliente.Contains(search) ||
                     c.Cliente.NombreCompleto.Contains(search))) ||
                c.Detalles.Any(d =>
                    (d.CodigoProductoSnapshot != null && d.CodigoProductoSnapshot.Contains(search)) ||
                    d.NombreDescripcionSnapshot.Contains(search)));
        }

        return query;
    }

    private static QuotationDetail ToDetail(Cotizacion c, bool includeHistory)
    {
        return new QuotationDetail(
            c.CotizacionId,
            c.CodigoCotizacion,
            c.ClienteId,
            c.Cliente?.CodigoCliente,
            c.NombreDestinatarioSnapshot,
            c.TelefonoSnapshot,
            c.CorreoSnapshot,
            c.DireccionSnapshot,
            c.Fecha,
            c.FechaVencimiento,
            c.Estado,
            c.SubtotalCentavos,
            c.DescuentoGeneralCentavos,
            c.TotalCentavos,
            c.CondicionesComerciales,
            c.Observaciones,
            c.UsuarioCreador.NombreUsuario,
            c.UsuarioUltimaActualizacion.NombreUsuario,
            c.FechaCreacion,
            c.FechaActualizacion,
            c.FechaEmision,
            c.FechaCierre,
            c.MotivoCancelacion,
            c.Detalles
                .OrderBy(d => d.DetalleCotizacionId)
                .Select(d => new QuotationLineDetail(
                    d.DetalleCotizacionId,
                    d.ProductoId,
                    d.CodigoProductoSnapshot,
                    d.NombreDescripcionSnapshot,
                    d.Cantidad,
                    d.PrecioUnitarioCentavos,
                    d.DescuentoLineaCentavos,
                    d.SubtotalCentavos,
                    d.Observaciones))
                .ToList(),
            (includeHistory ? c.HistorialEstados : [])
                .OrderByDescending(h => h.Fecha)
                .Select(h => new QuotationStatusHistoryRow(
                    h.HistorialEstadoCotizacionId,
                    h.EstadoAnterior,
                    h.EstadoNuevo,
                    h.Motivo,
                    h.Usuario.NombreUsuario,
                    h.Fecha))
                .ToList());
    }
}
