using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class PurchaseQueryService : IPurchaseQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public PurchaseQueryService(IDbContextFactory<AppDbContext> contextFactory, IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _permissionService = permissionService;
    }

    internal PurchaseQueryService(AppDbContext context, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar compras.");

    public async Task<IReadOnlyList<PurchaseSummary>> SearchAsync(
        PurchaseFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRAS_ACCEDER, cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 250);
        var query = ApplyFilter(BaseQuery(), filter);

        return await query
            .OrderByDescending(c => c.FechaCompra)
            .ThenByDescending(c => c.CompraId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => ToSummary(c))
            .ToListAsync(cancellationToken);
    }

    public async Task<PurchaseDetail?> GetByIdAsync(
        int compraId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRAS_ACCEDER, cancellationToken);

        var compra = await BaseQuery()
            .Include(c => c.Detalles)
                .ThenInclude(d => d.Producto)
            .Include(c => c.Detalles)
                .ThenInclude(d => d.ProductoSku)
            .Include(c => c.Recepciones)
                .ThenInclude(r => r.UsuarioRecepcion)
            .Include(c => c.Recepciones)
                .ThenInclude(r => r.Detalles)
                    .ThenInclude(d => d.Producto)
            .Include(c => c.Recepciones)
                .ThenInclude(r => r.Detalles)
                    .ThenInclude(d => d.ProductoSku)
            .Include(c => c.Recepciones)
                .ThenInclude(r => r.Detalles)
                    .ThenInclude(d => d.MovimientoInventario)
            .FirstOrDefaultAsync(c => c.CompraId == compraId, cancellationToken);

        if (compra is null)
        {
            return null;
        }

        var canViewCosts = await _permissionService.HasPermissionAsync(
            PermisoEmpleado.COSTOS_VER,
            cancellationToken: cancellationToken);
        var costs = canViewCosts
            ? await GetCostHistoryInternalAsync(compraId, cancellationToken)
            : [];

        return ToDetail(compra, costs);
    }

    public async Task<IReadOnlyList<PurchaseSummary>> GetBySupplierAsync(
        int proveedorId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRAS_ACCEDER, cancellationToken);

        return await BaseQuery()
            .Where(c => c.ProveedorId == proveedorId)
            .OrderByDescending(c => c.FechaCompra)
            .ThenByDescending(c => c.CompraId)
            .Take(250)
            .Select(c => ToSummary(c))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CostHistoryRow>> GetCostHistoryByPurchaseAsync(
        int compraId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COSTOS_VER, cancellationToken);
        return await GetCostHistoryInternalAsync(compraId, cancellationToken);
    }

    public async Task<PurchaseStats> GetStatsAsync(
        PurchaseFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRAS_ACCEDER, cancellationToken);

        var query = ApplyFilter(BaseQuery(), filter);
        var rows = await query
            .Select(c => new { c.EstadoRecepcion, c.TotalCentavos, c.TotalPagadoCentavos, c.SaldoPendienteCentavos })
            .ToListAsync(cancellationToken);

        return new PurchaseStats(
            rows.Count(r => r.EstadoRecepcion == EstadoRecepcionCompra.PENDIENTE),
            rows.Count(r => r.EstadoRecepcion == EstadoRecepcionCompra.PARCIAL),
            rows.Count(r => r.EstadoRecepcion == EstadoRecepcionCompra.RECIBIDA),
            rows.Sum(r => r.TotalCentavos),
            rows.Sum(r => r.TotalPagadoCentavos),
            rows.Sum(r => r.SaldoPendienteCentavos));
    }

    private IQueryable<Compra> BaseQuery()
    {
        return Context.Compras
            .AsNoTracking()
            .Include(c => c.Proveedor)
            .Include(c => c.UsuarioRegistro)
            .Include(c => c.Detalles)
            .Include(c => c.CuentaPorPagar);
    }

    private static IQueryable<Compra> ApplyFilter(IQueryable<Compra> query, PurchaseFilter filter)
    {
        if (filter.EstadoRecepcion.HasValue)
        {
            query = query.Where(c => c.EstadoRecepcion == filter.EstadoRecepcion.Value);
        }

        if (filter.UsarFechas)
        {
            if (filter.Desde.HasValue)
            {
                var desde = filter.Desde.Value.Date;
                query = query.Where(c => c.FechaCompra >= desde);
            }

            if (filter.Hasta.HasValue)
            {
                var hasta = filter.Hasta.Value.Date.AddDays(1);
                query = query.Where(c => c.FechaCompra < hasta);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(c =>
                c.CodigoCompra.Contains(search) ||
                c.Proveedor.CodigoProveedor.Contains(search) ||
                c.Proveedor.NombreRazonSocial.Contains(search) ||
                c.Detalles.Any(d =>
                    d.CodigoProductoSnapshot.Contains(search) ||
                    d.NombreProductoSnapshot.Contains(search) ||
                    (d.CodigoSkuSnapshot != null && d.CodigoSkuSnapshot.Contains(search)) ||
                    (d.NombreVarianteSnapshot != null && d.NombreVarianteSnapshot.Contains(search))));
        }

        return query;
    }

    private static PurchaseSummary ToSummary(Compra compra)
    {
        var cantidadComprada = compra.Detalles.Sum(d => d.CantidadComprada);
        var cantidadRecibida = compra.Detalles.Sum(d => d.CantidadRecibida);
        return new PurchaseSummary(
            compra.CompraId,
            compra.CodigoCompra,
            compra.FechaCompra,
            compra.ProveedorId,
            compra.Proveedor.CodigoProveedor,
            compra.Proveedor.NombreRazonSocial,
            compra.EstadoRecepcion,
            compra.Detalles.Count,
            compra.TotalCentavos,
            cantidadComprada,
            cantidadRecibida,
            cantidadComprada - cantidadRecibida,
            compra.UsuarioRegistro.NombreUsuario,
            compra.EstadoFinanciero,
            compra.TotalPagadoCentavos,
            compra.SaldoPendienteCentavos,
            compra.CuentaPorPagar == null ? null : compra.CuentaPorPagar.CuentaPorPagarId);
    }

    private static PurchaseDetail ToDetail(Compra compra, IReadOnlyList<CostHistoryRow> costs)
    {
        return new PurchaseDetail(
            compra.CompraId,
            compra.CodigoCompra,
            compra.ProveedorId,
            compra.Proveedor.CodigoProveedor,
            compra.Proveedor.NombreRazonSocial,
            compra.FechaCompra,
            compra.FechaRegistro,
            compra.EstadoRecepcion,
            compra.SubtotalCentavos,
            compra.DescuentoCentavos,
            compra.TotalCentavos,
            compra.Observaciones,
            compra.UsuarioRegistro.NombreUsuario,
            compra.Detalles
                .OrderBy(d => d.NombreProductoSnapshot)
                .Select(ToLineSummary)
                .ToList(),
            compra.Recepciones
                .OrderByDescending(r => r.FechaRecepcion)
                .Select(ToReceptionSummary)
                .ToList(),
            costs,
            compra.EstadoFinanciero,
            compra.TotalPagadoCentavos,
            compra.SaldoPendienteCentavos,
            compra.CuentaPorPagar?.CuentaPorPagarId);
    }

    private static PurchaseLineSummary ToLineSummary(DetalleCompra detalle)
    {
        return new PurchaseLineSummary(
            detalle.DetalleCompraId,
            detalle.ProductoId,
            detalle.CodigoProductoSnapshot,
            detalle.NombreProductoSnapshot,
            detalle.CantidadComprada,
            detalle.CantidadRecibida,
            detalle.CantidadComprada - detalle.CantidadRecibida,
            detalle.CostoUnitarioCentavos,
            detalle.DescuentoLineaCentavos,
            detalle.SubtotalCentavos,
            detalle.Observaciones,
            detalle.ProductoSkuId,
            detalle.CodigoSkuSnapshot,
            detalle.NombreVarianteSnapshot);
    }

    private static PurchaseReceptionSummary ToReceptionSummary(RecepcionCompra recepcion)
    {
        return new PurchaseReceptionSummary(
            recepcion.RecepcionCompraId,
            recepcion.CodigoRecepcion,
            recepcion.OperacionId,
            recepcion.CompraId,
            recepcion.Compra.CodigoCompra,
            recepcion.FechaRecepcion,
            recepcion.UsuarioRecepcion.NombreUsuario,
            recepcion.Observaciones,
            recepcion.Detalles
                .OrderBy(d => d.Producto.Nombre)
                .Select(d => new PurchaseReceptionLineSummary(
                    d.RecepcionCompraDetalleId,
                    d.DetalleCompraId,
                    d.ProductoId,
                    d.Producto.CodigoInterno,
                    d.Producto.Nombre,
                    d.CantidadRecibida,
                    d.StockAnterior,
                    d.StockNuevo,
                    d.CostoUnitarioCentavos,
                    d.CostoPromedioAnteriorCentavos,
                    d.CostoPromedioNuevoCentavos,
                    d.MovimientoInventarioId,
                    d.CostoNetoUnitarioCentavos,
                    d.DescuentoAsignadoCentavos,
                    d.ProductoSkuId,
                    d.CodigoSkuSnapshot,
                    d.NombreVarianteSnapshot,
                    d.UbicacionDestino))
                .ToList());
    }

    private async Task<IReadOnlyList<CostHistoryRow>> GetCostHistoryInternalAsync(
        int compraId,
        CancellationToken cancellationToken)
    {
        return await Context.HistorialCostosProducto
            .AsNoTracking()
            .Include(h => h.Producto)
            .Include(h => h.Proveedor)
            .Include(h => h.Compra)
            .Include(h => h.Usuario)
            .Where(h => h.CompraId == compraId)
            .OrderByDescending(h => h.Fecha)
            .Select(h => new CostHistoryRow(
                h.HistorialCostoProductoId,
                h.Fecha,
                h.ProductoId,
                h.Producto.CodigoInterno,
                h.Producto.Nombre,
                h.ProveedorId,
                h.Proveedor.NombreRazonSocial,
                h.CompraId,
                h.Compra.CodigoCompra,
                h.CostoAnteriorCentavos,
                h.CostoRecibidoCentavos,
                h.CostoPromedioNuevoCentavos,
                h.StockAnterior,
                h.CantidadRecibida,
                h.StockNuevo,
                h.Usuario.NombreUsuario,
                h.Observaciones))
            .ToListAsync(cancellationToken);
    }
}
