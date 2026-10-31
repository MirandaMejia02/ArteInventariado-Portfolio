using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SalesQueryService : ISalesQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public SalesQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
    }

    internal SalesQueryService(AppDbContext context, ICurrentSession currentSession, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<SalesHistorySummary>> GetSalesAsync(
        SalesHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.HISTORIAL_VENTAS_VER, cancellationToken);

        var query = lease.Context.Ventas
            .AsNoTracking()
            .Include(v => v.Usuario)
            .Include(v => v.Pagos)
            .Include(v => v.CuentaPorCobrar)
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
            var numero = filter.NumeroVenta.Trim();
            query = query.Where(v => v.NumeroVenta.Contains(numero));
        }

        if (filter.UsuarioId.HasValue)
        {
            query = query.Where(v => v.UsuarioId == filter.UsuarioId.Value);
        }

        if (filter.MetodoPago.HasValue)
        {
            query = query.Where(v => v.Pagos.Any(p => p.MetodoPago == filter.MetodoPago.Value));
        }

        var sales = await query
            .OrderByDescending(v => v.FechaVenta)
            .Take(1000)
            .ToListAsync(cancellationToken);

        return sales
            .Select(v => new SalesHistorySummary(
                v.VentaId,
                v.FechaVenta,
                v.NumeroVenta,
                v.UsuarioNombreSnapshot ?? v.Usuario.NombreUsuario,
                v.ClienteNombreSnapshot,
                v.TotalCentavos,
                v.DescuentoCentavos,
                FormatPaymentMethods(v.Pagos.Select(p => p.MetodoPago), v.CuentaPorCobrar is not null),
                v.CajaId,
                v.Estado,
                v.CuentaPorCobrar is not null))
            .ToList();
    }

    public async Task<SalesHistoryDetail?> GetSaleDetailAsync(
        int ventaId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.HISTORIAL_VENTAS_VER, cancellationToken);

        var sale = await lease.Context.Ventas
            .AsNoTracking()
            .Include(v => v.Usuario)
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .Include(v => v.Pagos)
            .Include(v => v.CuentaPorCobrar)
            .ThenInclude(c => c!.Detalles)
            .FirstOrDefaultAsync(v => v.VentaId == ventaId, cancellationToken);

        if (sale is null)
        {
            return null;
        }

        var lines = sale.Detalles
            .OrderBy(d => d.DetalleVentaId)
            .Select(d => new SalesHistoryLine(
                FormatHistoricalProductName(
                    d.ProductoNombreSnapshot ?? d.Producto.Nombre,
                    d.NombreVarianteSnapshot),
                d.CodigoSkuSnapshot ?? d.ProductoCodigoSnapshot ?? d.Producto.CodigoInterno,
                d.Cantidad,
                d.PrecioUnitarioCentavos,
                d.DescuentoCentavos + (d.DescuentoGeneralProporcionalCentavos ?? 0),
                d.SubtotalCentavos,
                d.TotalCentavos - (d.DescuentoGeneralProporcionalCentavos ?? 0),
                d.ProductoCodigoSnapshot ?? d.Producto.CodigoInterno,
                d.CodigoSkuSnapshot,
                d.NombreVarianteSnapshot,
                d.ProductoId,
                d.ProductoSkuId))
            .ToList();

        var payments = sale.Pagos
            .OrderBy(p => p.PagoVentaId)
            .Select(p => new SalesHistoryPayment(
                p.MetodoPago,
                p.MontoCentavos,
                p.Referencia))
            .ToList();

        var totalPagado = sale.PagadoCentavos > 0
            ? sale.PagadoCentavos
            : sale.Pagos.Sum(p => p.MontoCentavos);

        SalesHistoryCredit? credit = null;
        if (sale.CuentaPorCobrar is not null)
        {
            credit = new SalesHistoryCredit(
                sale.CuentaPorCobrar.CuentaPorCobrarId,
                sale.CuentaPorCobrar.CodigoCuenta,
                sale.CuentaPorCobrar.CapitalFinanciadoCentavos,
                sale.CuentaPorCobrar.InteresPorcentaje,
                sale.CuentaPorCobrar.InteresCalculadoCentavos,
                sale.CuentaPorCobrar.TotalOriginalCentavos,
                sale.CuentaPorCobrar.SaldoActualCentavos,
                sale.CuentaPorCobrar.FechaVencimiento,
                sale.CuentaPorCobrar.Detalles
                    .OrderBy(d => d.CuentaPorCobrarDetalleId)
                    .Select(d => new SalesHistoryCreditLine(
                        d.ProductoNombreSnapshot,
                        d.ProductoCodigoSnapshot,
                        d.CantidadFinanciada,
                        d.SubtotalFinanciadoCentavos,
                        d.CapitalPendienteCentavos))
                    .ToList());
        }

        return new SalesHistoryDetail(
            sale.VentaId,
            sale.NumeroVenta,
            sale.FechaVenta,
            sale.UsuarioNombreSnapshot ?? sale.Usuario.NombreCompleto,
            sale.ClienteNombreSnapshot,
            sale.SubtotalCentavos,
            sale.DescuentoCentavos,
            sale.TotalCentavos,
            totalPagado,
            sale.CambioCentavos,
            lines,
            payments,
            credit);
    }

    private static string FormatPaymentMethods(IEnumerable<MetodoPago> payments, bool hasCredit)
    {
        var methods = payments
            .OrderBy(p => p)
            .Select(p => p.ToString())
            .Distinct()
            .ToList();

        if (hasCredit)
        {
            methods.Add("CREDITO");
        }

        return string.Join(", ", methods);
    }

    private static string FormatHistoricalProductName(string productName, string? variantName)
    {
        return string.IsNullOrWhiteSpace(variantName)
            ? productName
            : $"{productName} · {variantName}";
    }

    private static DateTime ToUtcStart(DateOnly date)
    {
        return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
    }
}
