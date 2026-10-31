using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SaleCorrectionQueryService : ISaleCorrectionQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public SaleCorrectionQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
    }

    internal SaleCorrectionQueryService(AppDbContext context, ICurrentSession currentSession, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar correcciones de venta.");

    public async Task<IReadOnlyList<SaleCorrectionSaleSummary>> SearchSalesForCorrectionAsync(
        SaleCorrectionSalesFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAuthenticated();
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER, cancellationToken);

        var limit = Math.Clamp(filter.Limit, 1, 100);
        var query = Context.Ventas
            .AsNoTracking()
            .Include(v => v.Usuario)
            .Include(v => v.Pagos)
            .Where(v => v.Estado == EstadoVenta.COMPLETADA)
            .AsQueryable();

        if (filter.FechaDesde.HasValue)
        {
            var desde = ToUtcStart(filter.FechaDesde.Value);
            query = query.Where(v => v.FechaVenta >= desde);
        }

        if (filter.FechaHasta.HasValue)
        {
            var hasta = ToUtcStart(filter.FechaHasta.Value.AddDays(1));
            query = query.Where(v => v.FechaVenta < hasta);
        }

        if (!string.IsNullOrWhiteSpace(filter.NumeroVenta))
        {
            var numeroVenta = filter.NumeroVenta.Trim();
            query = query.Where(v => v.NumeroVenta.Contains(numeroVenta));
        }

        if (filter.UsuarioId.HasValue)
        {
            query = query.Where(v => v.UsuarioId == filter.UsuarioId.Value);
        }

        var sales = await query
            .OrderByDescending(v => v.FechaVenta)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return sales
            .Select(v => new SaleCorrectionSaleSummary(
                v.VentaId,
                v.FechaVenta,
                v.NumeroVenta,
                v.UsuarioNombreSnapshot ?? v.Usuario.NombreUsuario,
                v.TotalCentavos,
                string.Join(", ", v.Pagos.OrderBy(p => p.MetodoPago).Select(p => p.MetodoPago.ToString())),
                v.Estado))
            .ToList();
    }

    public async Task<SaleCorrectionSearchResult?> GetSaleForCorrectionAsync(
        string numeroVenta,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAuthenticated();
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER, cancellationToken);

        if (string.IsNullOrWhiteSpace(numeroVenta))
        {
            throw new ArgumentException("El numero de venta es obligatorio.", nameof(numeroVenta));
        }

        return await BuildSaleForCorrectionAsync(
            Context.Ventas
                .AsNoTracking()
                .Where(v => v.NumeroVenta == numeroVenta.Trim()),
            cancellationToken);
    }

    public async Task<SaleCorrectionSearchResult?> GetSaleForCorrectionByIdAsync(
        int ventaId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAuthenticated();
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER, cancellationToken);

        if (ventaId <= 0)
        {
            throw new ArgumentException("La venta seleccionada no es valida.", nameof(ventaId));
        }

        return await BuildSaleForCorrectionAsync(
            Context.Ventas
                .AsNoTracking()
                .Where(v => v.VentaId == ventaId),
            cancellationToken);
    }

    private async Task<SaleCorrectionSearchResult?> BuildSaleForCorrectionAsync(
        IQueryable<ArteInventariado.Domain.Entities.Venta> saleQuery,
        CancellationToken cancellationToken)
    {
        var sale = await saleQuery
            .AsNoTracking()
            .Include(v => v.Usuario)
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .Include(v => v.Pagos)
            .FirstOrDefaultAsync(cancellationToken);

        if (sale is null)
        {
            return null;
        }

        var detailIds = sale.Detalles.Select(d => d.DetalleVentaId).ToList();
        var returnedByDetail = await Context.DetalleDevoluciones
            .AsNoTracking()
            .Where(d => detailIds.Contains(d.DetalleVentaId))
            .GroupBy(d => d.DetalleVentaId)
            .Select(g => new { DetalleVentaId = g.Key, Cantidad = g.Sum(x => x.Cantidad) })
            .ToDictionaryAsync(x => x.DetalleVentaId, x => x.Cantidad, cancellationToken);

        var lines = sale.Detalles
            .OrderBy(d => d.DetalleVentaId)
            .Select(d =>
            {
                var returned = returnedByDetail.GetValueOrDefault(d.DetalleVentaId);
                return new SaleCorrectionLine(
                    d.DetalleVentaId,
                    d.ProductoId,
                    d.ProductoCodigoSnapshot ?? d.Producto.CodigoInterno,
                    d.ProductoNombreSnapshot ?? d.Producto.Nombre,
                    d.Cantidad,
                    returned,
                    Math.Max(0, d.Cantidad - returned),
                    d.PrecioUnitarioCentavos,
                    d.DescuentoCentavos + (d.DescuentoGeneralProporcionalCentavos ?? 0),
                    d.TotalCentavos - (d.DescuentoGeneralProporcionalCentavos ?? 0),
                    d.ProductoSkuId,
                    d.CodigoSkuSnapshot,
                    d.NombreVarianteSnapshot);
            })
            .ToList();

        var payments = sale.Pagos
            .OrderBy(p => p.PagoVentaId)
            .Select(p => new SaleCorrectionPayment(p.MetodoPago, p.MontoCentavos, p.Referencia))
            .ToList();

        return new SaleCorrectionSearchResult(
            sale.VentaId,
            sale.NumeroVenta,
            sale.FechaVenta,
            sale.UsuarioNombreSnapshot ?? sale.Usuario.NombreUsuario,
            sale.TotalCentavos,
            sale.Estado,
            lines,
            payments);
    }

    public async Task<IReadOnlyList<ReturnHistorySummary>> GetReturnHistoryAsync(
        ReturnHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER, cancellationToken);

        var query = Context.Devoluciones
            .AsNoTracking()
            .Include(d => d.Venta)
            .Include(d => d.Usuario)
            .Include(d => d.Reembolsos)
            .AsQueryable();

        if (filter.FechaDesde.HasValue)
        {
            var desde = ToUtcStart(filter.FechaDesde.Value);
            query = query.Where(d => d.FechaDevolucion >= desde);
        }

        if (filter.FechaHasta.HasValue)
        {
            var hasta = ToUtcStart(filter.FechaHasta.Value.AddDays(1));
            query = query.Where(d => d.FechaDevolucion < hasta);
        }

        if (!string.IsNullOrWhiteSpace(filter.NumeroVenta))
        {
            var numeroVenta = filter.NumeroVenta.Trim();
            query = query.Where(d => d.Venta.NumeroVenta.Contains(numeroVenta));
        }

        if (!string.IsNullOrWhiteSpace(filter.NumeroDevolucion))
        {
            var numeroDevolucion = filter.NumeroDevolucion.Trim();
            query = query.Where(d => d.NumeroDevolucion.Contains(numeroDevolucion));
        }

        var returns = await query
            .OrderByDescending(d => d.FechaDevolucion)
            .Take(200)
            .ToListAsync(cancellationToken);

        return returns
            .Select(d => new ReturnHistorySummary(
                d.DevolucionId,
                d.NumeroDevolucion,
                d.Venta.NumeroVenta,
                d.FechaDevolucion,
                d.Usuario.NombreUsuario,
                d.TipoCorreccion,
                d.TotalDevueltoCentavos,
                d.Reembolsos.Sum(r => r.MontoCentavos),
                d.Motivo))
            .ToList();
    }

    public async Task<ReturnHistoryDetail?> GetReturnDetailAsync(
        int devolucionId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER, cancellationToken);

        var devolucion = await Context.Devoluciones
            .AsNoTracking()
            .Include(d => d.Venta)
            .Include(d => d.Detalles)
            .ThenInclude(d => d.Producto)
            .Include(d => d.Detalles)
            .ThenInclude(d => d.DetalleVenta)
            .Include(d => d.DetallesCambio)
            .ThenInclude(d => d.ProductoNuevo)
            .Include(d => d.Reembolsos)
            .ThenInclude(r => r.Usuario)
            .FirstOrDefaultAsync(d => d.DevolucionId == devolucionId, cancellationToken);

        if (devolucion is null)
        {
            return null;
        }

        return new ReturnHistoryDetail(
            devolucion.DevolucionId,
            devolucion.NumeroDevolucion,
            devolucion.Venta.NumeroVenta,
            devolucion.TipoCorreccion,
            devolucion.Detalles
                .OrderBy(d => d.DetalleDevolucionId)
                .Select(d => new ReturnHistoryLine(
                    d.DetalleVenta.ProductoNombreSnapshot ?? d.Producto.Nombre,
                    d.DetalleVenta.ProductoCodigoSnapshot ?? d.Producto.CodigoInterno,
                    d.Cantidad,
                    d.CondicionProducto,
                    d.ReintegraStock,
                    d.SubtotalDevueltoCentavos))
                .ToList(),
            devolucion.DetallesCambio
                .OrderBy(d => d.DetalleCambioVentaId)
                .Select(d => new ReturnHistoryChangeLine(
                    d.ProductoNombreSnapshot ?? d.ProductoNuevo.Nombre,
                    d.ProductoCodigoSnapshot ?? d.ProductoNuevo.CodigoInterno,
                    d.Cantidad,
                    d.TotalCentavos))
                .ToList(),
            devolucion.Reembolsos
                .OrderBy(r => r.FechaReembolso)
                .Select(r => new ReturnHistoryRefund(
                    r.MetodoReembolso,
                    r.MontoCentavos,
                    r.Motivo,
                    r.Referencia,
                    r.FechaReembolso,
                    r.Usuario.NombreUsuario))
                .ToList());
    }

    private void EnsureAuthenticated()
    {
        if (_currentSession.UsuarioActual is null)
        {
            throw new InvalidOperationException("Debe existir una sesion activa.");
        }
    }

    private static DateTime ToUtcStart(DateOnly date)
    {
        return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
    }
}
