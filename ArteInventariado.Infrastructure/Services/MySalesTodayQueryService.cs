using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Read-only, server-side scoped query for the authenticated user's sales today.
/// The user and date boundaries are deliberately not caller-controlled.
/// </summary>
public sealed class MySalesTodayQueryService : IMySalesTodayQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly TimeProvider _timeProvider;

    [ActivatorUtilitiesConstructor]
    public MySalesTodayQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _timeProvider = timeProvider;
    }

    internal MySalesTodayQueryService(
        AppDbContext context,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        TimeProvider timeProvider)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<MySalesTodaySummary>> GetSalesAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = await OpenScopeAsync(cancellationToken);
        await using (scope.Lease.ConfigureAwait(false))
        {
            var (userId, timeZone, startUtc, endUtcExclusive) = scope.Scope;
            var sales = await scope.Lease.Context.Ventas
                .AsNoTracking()
                .Where(v =>
                    v.UsuarioId == userId &&
                    v.FechaVenta >= startUtc &&
                    v.FechaVenta < endUtcExclusive)
                .OrderByDescending(v => v.FechaVenta)
                .Select(v => new SaleHeader(
                    v.VentaId,
                    v.FechaVenta,
                    v.NumeroVenta,
                    v.Estado,
                    v.DescuentoCentavos,
                    v.TotalCentavos))
                .ToListAsync(cancellationToken);

            if (sales.Count == 0)
            {
                return [];
            }

            var saleIds = sales.Select(sale => sale.VentaId).ToArray();
            var payments = await scope.Lease.Context.PagosVenta
                .AsNoTracking()
                .Where(payment => saleIds.Contains(payment.VentaId))
                .OrderBy(payment => payment.PagoVentaId)
                .Select(payment => new PaymentRow(payment.VentaId, payment.MetodoPago))
                .ToListAsync(cancellationToken);
            var creditSaleIds = await scope.Lease.Context.CuentasPorCobrar
                .AsNoTracking()
                .Where(account => saleIds.Contains(account.VentaId))
                .Select(account => account.VentaId)
                .ToListAsync(cancellationToken);
            var creditSales = creditSaleIds.ToHashSet();

            return sales
                .Select(sale => new MySalesTodaySummary(
                    sale.VentaId,
                    sale.NumeroVenta,
                    ToBusinessLocal(sale.FechaVentaUtc, timeZone),
                    sale.Estado,
                    sale.DescuentoCentavos,
                    sale.TotalCentavos,
                    FormatPaymentMethods(
                        payments.Where(payment => payment.VentaId == sale.VentaId).Select(payment => payment.MetodoPago),
                        creditSales.Contains(sale.VentaId))))
                .ToList();
        }
    }

    public async Task<MySalesTodayDetail?> GetSaleDetailAsync(
        int ventaId,
        CancellationToken cancellationToken = default)
    {
        var scope = await OpenScopeAsync(cancellationToken);
        await using (scope.Lease.ConfigureAwait(false))
        {
            var (userId, timeZone, startUtc, endUtcExclusive) = scope.Scope;
            var sale = await scope.Lease.Context.Ventas
                .AsNoTracking()
                .Where(v =>
                    v.VentaId == ventaId &&
                    v.UsuarioId == userId &&
                    v.FechaVenta >= startUtc &&
                    v.FechaVenta < endUtcExclusive)
                .Select(v => new SaleHeaderWithSubtotal(
                    v.VentaId,
                    v.FechaVenta,
                    v.NumeroVenta,
                    v.Estado,
                    v.SubtotalCentavos,
                    v.DescuentoCentavos,
                    v.TotalCentavos))
                .SingleOrDefaultAsync(cancellationToken);

            if (sale is null)
            {
                return null;
            }

            var lines = await scope.Lease.Context.DetalleVentas
                .AsNoTracking()
                .Where(line => line.VentaId == sale.VentaId)
                .OrderBy(line => line.DetalleVentaId)
                .Select(line => new MySalesTodayLine(
                    line.NombreVarianteSnapshot == null || line.NombreVarianteSnapshot == ""
                        ? line.ProductoNombreSnapshot ?? line.Producto.Nombre
                        : (line.ProductoNombreSnapshot ?? line.Producto.Nombre) + " · " + line.NombreVarianteSnapshot,
                    line.CodigoSkuSnapshot ?? line.ProductoCodigoSnapshot ?? line.Producto.CodigoInterno,
                    line.Cantidad,
                    line.PrecioUnitarioCentavos,
                    line.DescuentoCentavos + (line.DescuentoGeneralProporcionalCentavos ?? 0),
                    line.TotalCentavos - (line.DescuentoGeneralProporcionalCentavos ?? 0)))
                .ToListAsync(cancellationToken);

            var paymentRows = await scope.Lease.Context.PagosVenta
                .AsNoTracking()
                .Where(payment => payment.VentaId == sale.VentaId)
                .OrderBy(payment => payment.PagoVentaId)
                .Select(payment => new PaymentDetailRow(payment.MetodoPago, payment.MontoCentavos))
                .ToListAsync(cancellationToken);

            var payments = paymentRows
                .Select(payment => new MySalesTodayPayment(payment.MetodoPago.ToString(), payment.MontoCentavos))
                .ToList();

            var hasCredit = await scope.Lease.Context.CuentasPorCobrar
                .AsNoTracking()
                .AnyAsync(account => account.VentaId == sale.VentaId, cancellationToken);
            if (hasCredit)
            {
                payments = [.. payments, new MySalesTodayPayment("CREDITO", 0)];
            }

            return new MySalesTodayDetail(
                sale.VentaId,
                sale.NumeroVenta,
                ToBusinessLocal(sale.FechaVentaUtc, timeZone),
                sale.Estado,
                sale.SubtotalCentavos,
                sale.DescuentoCentavos,
                sale.TotalCentavos,
                lines,
                payments);
        }
    }

    internal static (DateTime StartUtc, DateTime EndUtcExclusive) CalculateUtcDayBounds(
        DateTimeOffset now,
        TimeZoneInfo businessTimeZone)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, businessTimeZone).DateTime);
        var startLocal = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, businessTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, businessTimeZone));
    }

    private async Task<QueryScope> OpenScopeAsync(CancellationToken cancellationToken)
    {
        await _permissionService.EnsurePermissionAsync(
            PermisoEmpleado.VENTAS_PROPIAS_DIA_VER,
            cancellationToken);

        var userId = _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("No existe una sesión autenticada.");
        var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(lease.Context, cancellationToken);
        var bounds = CalculateUtcDayBounds(_timeProvider.GetUtcNow(), timeZone);
        return new QueryScope(lease, (userId, timeZone, bounds.StartUtc, bounds.EndUtcExclusive));
    }

    private static async Task<TimeZoneInfo> ResolveBusinessTimeZoneAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var configuredId = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(item => item.Clave == DashboardQueryService.BusinessTimeZoneKey)
            .Select(item => item.Valor)
            .FirstOrDefaultAsync(cancellationToken);
        var timeZoneId = string.IsNullOrWhiteSpace(configuredId)
            ? DashboardQueryService.DefaultBusinessTimeZoneId
            : configuredId.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static DateTime ToBusinessLocal(DateTime utcDateTime, TimeZoneInfo timeZone)
    {
        var utc = new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
        return TimeZoneInfo.ConvertTime(utc, timeZone).DateTime;
    }

    private static string FormatPaymentMethods(IEnumerable<MetodoPago> methods, bool hasCredit)
    {
        var values = methods.Select(method => method.ToString()).Distinct().ToList();
        if (hasCredit)
        {
            values.Add("CREDITO");
        }

        return string.Join(", ", values);
    }

    private sealed record SaleHeader(
        int VentaId,
        DateTime FechaVentaUtc,
        string NumeroVenta,
        EstadoVenta Estado,
        long DescuentoCentavos,
        long TotalCentavos);

    private sealed record SaleHeaderWithSubtotal(
        int VentaId,
        DateTime FechaVentaUtc,
        string NumeroVenta,
        EstadoVenta Estado,
        long SubtotalCentavos,
        long DescuentoCentavos,
        long TotalCentavos);

    private sealed record PaymentRow(int VentaId, MetodoPago MetodoPago);

    private sealed record PaymentDetailRow(MetodoPago MetodoPago, long MontoCentavos);

    private sealed class QueryScope(
        AppDbContextLease lease,
        (int UserId, TimeZoneInfo TimeZone, DateTime StartUtc, DateTime EndUtcExclusive) scope)
    {
        public AppDbContextLease Lease { get; } = lease;
        public (int UserId, TimeZoneInfo TimeZone, DateTime StartUtc, DateTime EndUtcExclusive) Scope { get; } = scope;
    }
}
