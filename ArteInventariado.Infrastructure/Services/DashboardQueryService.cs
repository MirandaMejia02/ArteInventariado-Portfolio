using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class DashboardQueryService : IDashboardQueryService
{
    internal const string BusinessTimeZoneKey = "negocio.zona_horaria";
    internal const string DefaultBusinessTimeZoneId = "Central America Standard Time";

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly ICashService _cashService;
    private readonly TimeProvider _timeProvider;

    [ActivatorUtilitiesConstructor]
    public DashboardQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        ICashService cashService,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _cashService = cashService;
        _timeProvider = timeProvider;
    }

    internal DashboardQueryService(
        AppDbContext context,
        ICurrentSession currentSession,
        ICashService cashService,
        TimeProvider timeProvider)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _cashService = cashService;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        DashboardSalesToday salesToday;
        int activeProducts;
        int registeredCustomers;
        int pendingOrders;

        await using (var lease = await CreateContextAsync(cancellationToken))
        {
            var context = lease.Context;
            await EnsureActiveAdminAsync(context, cancellationToken);
            var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
            var currentPeriod = AnalyticsPeriodFactory.Create(
                new AnalyticsPeriodRequest(AnalyticsPeriodKind.Today),
                now,
                timeZone);
            var previousPeriod = AnalyticsPeriodFactory.CreatePreviousEquivalent(currentPeriod, timeZone);

            var currentNet = await GetNetSalesAsync(context, currentPeriod, cancellationToken);
            var previousNet = await GetNetSalesAsync(context, previousPeriod, cancellationToken);
            var saleCount = await context.Ventas
                .AsNoTracking()
                .CountAsync(
                    v => v.FechaVenta >= currentPeriod.UtcStart &&
                        v.FechaVenta < currentPeriod.UtcEndExclusive &&
                        v.Estado == EstadoVenta.COMPLETADA,
                    cancellationToken);

            salesToday = new DashboardSalesToday(
                currentNet,
                saleCount,
                MetricComparisonCalculator.Calculate(currentNet, previousNet));
            activeProducts = await context.Productos
                .AsNoTracking()
                .CountAsync(p => p.Estado == EstadoProducto.ACTIVO, cancellationToken);
            registeredCustomers = await context.Clientes
                .AsNoTracking()
                .CountAsync(cancellationToken);
            pendingOrders = await context.PedidosPersonalizados
                .AsNoTracking()
                .CountAsync(
                    p => p.Estado == EstadoPedidoPersonalizado.CONFIRMADO ||
                        p.Estado == EstadoPedidoPersonalizado.EN_PRODUCCION ||
                        p.Estado == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA,
                    cancellationToken);
        }

        var cash = await _cashService.GetTodaySummaryAsync(cancellationToken);
        var cashExpected = cash.CajaId.HasValue && cash.Estado == EstadoCaja.ABIERTA
            ? new DashboardCashExpected(true, cash.CajaId, cash.TotalEsperadoCentavos)
            : new DashboardCashExpected(false, null, null);

        return new DashboardOverview(
            now.UtcDateTime,
            salesToday,
            activeProducts,
            registeredCustomers,
            cashExpected,
            pendingOrders);
    }

    public async Task<DashboardSalesTrend> GetSalesTrendAsync(
        DashboardSalesRange range,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = CreateDashboardPeriod(range, _timeProvider.GetUtcNow(), timeZone);

        var sales = await context.Ventas
            .AsNoTracking()
            .Where(v => v.FechaVenta >= period.UtcStart && v.FechaVenta < period.UtcEndExclusive)
            .Select(v => new DatedAmount(v.FechaVenta, v.TotalCentavos))
            .ToListAsync(cancellationToken);
        var corrections = await context.Devoluciones
            .AsNoTracking()
            .Where(d => d.FechaDevolucion >= period.UtcStart && d.FechaDevolucion < period.UtcEndExclusive)
            .Select(d => new DatedAmount(d.FechaDevolucion, -d.TotalDevueltoCentavos))
            .ToListAsync(cancellationToken);
        var changes = await context.DetallesCambioVenta
            .AsNoTracking()
            .Where(d => d.Devolucion.FechaDevolucion >= period.UtcStart &&
                d.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .Select(d => new DatedAmount(d.Devolucion.FechaDevolucion, d.TotalCentavos))
            .ToListAsync(cancellationToken);

        var totals = sales
            .Concat(corrections)
            .Concat(changes)
            .GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(x.DateUtc, timeZone)))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountCentavos));
        var startDate = DateOnly.FromDateTime(period.LocalStart.DateTime);
        var endDate = DateOnly.FromDateTime(period.LocalEndExclusive.DateTime);
        var points = new List<DashboardSalesTrendPoint>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            points.Add(new DashboardSalesTrendPoint(
                date,
                totals.GetValueOrDefault(date)));
        }

        return new DashboardSalesTrend(range, period.DisplayName, points);
    }

    public async Task<DashboardCategorySales> GetCategorySalesAsync(
        DashboardSalesRange range,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = CreateDashboardPeriod(range, _timeProvider.GetUtcNow(), timeZone);

        var saleLines = await context.DetalleVentas
            .AsNoTracking()
            .Where(d => d.Venta.FechaVenta >= period.UtcStart && d.Venta.FechaVenta < period.UtcEndExclusive)
            .OrderBy(d => d.VentaId)
            .ThenBy(d => d.DetalleVentaId)
            .Select(d => new CategorySaleLine(
                d.VentaId,
                d.DetalleVentaId,
                d.Venta.DescuentoCentavos,
                d.DescuentoCentavos,
                d.DescuentoGeneralProporcionalCentavos,
                d.TotalCentavos,
                d.Producto.CategoriaId,
                d.Producto.Categoria.Nombre))
            .ToListAsync(cancellationToken);
        var returnedLines = await context.DetalleDevoluciones
            .AsNoTracking()
            .Where(d => d.Devolucion.FechaDevolucion >= period.UtcStart &&
                d.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .Select(d => new CategoryAdjustment(
                d.Producto.CategoriaId,
                d.Producto.Categoria.Nombre,
                -d.SubtotalDevueltoCentavos))
            .ToListAsync(cancellationToken);
        var changedLines = await context.DetallesCambioVenta
            .AsNoTracking()
            .Where(d => d.Devolucion.FechaDevolucion >= period.UtcStart &&
                d.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .Select(d => new CategoryAdjustment(
                d.ProductoNuevo.CategoriaId,
                d.ProductoNuevo.Categoria.Nombre,
                d.TotalCentavos))
            .ToListAsync(cancellationToken);

        var adjustments = new List<CategoryAdjustment>();
        foreach (var sale in saleLines.GroupBy(l => l.SaleId))
        {
            var ordered = sale.OrderBy(l => l.SaleLineId).ToList();
            var lineDiscount = ordered.Sum(l => l.LineDiscountCentavos);
            var allocations = ordered.All(line => line.StoredGeneralDiscountCentavos.HasValue)
                ? ordered.Select(line => line.StoredGeneralDiscountCentavos!.Value).ToList()
                : ProportionalAmountAllocator.Allocate(
                    Math.Max(0, ordered[0].SaleDiscountCentavos - lineDiscount),
                    ordered.Select(l => l.LineTotalCentavos).ToList());

            adjustments.AddRange(ordered.Select((line, index) => new CategoryAdjustment(
                line.CategoryId,
                line.CategoryName,
                line.LineTotalCentavos - allocations[index])));
        }

        adjustments.AddRange(returnedLines);
        adjustments.AddRange(changedLines);

        var netByCategory = adjustments
            .GroupBy(a => new { a.CategoryId, a.CategoryName })
            .Select(g => new
            {
                g.Key.CategoryId,
                Name = string.IsNullOrWhiteSpace(g.Key.CategoryName) ? "Sin categoria historica" : g.Key.CategoryName,
                Amount = g.Sum(x => x.AmountCentavos)
            })
            .OrderByDescending(x => x.Amount)
            .ToList();
        var positiveTotal = netByCategory.Where(x => x.Amount > 0).Sum(x => x.Amount);
        var rows = netByCategory
            .Where(x => x.Amount > 0)
            .Select(x => new DashboardCategorySalesPoint(
                x.CategoryId,
                x.Name,
                x.Amount,
                positiveTotal == 0
                    ? 0
                    : decimal.Round(x.Amount * 100m / positiveTotal, 1, MidpointRounding.AwayFromZero)))
            .ToList();

        return new DashboardCategorySales(
            range,
            positiveTotal,
            netByCategory.Any(x => x.Amount < 0),
            rows);
    }

    public async Task<IReadOnlyList<DashboardTopProduct>> GetTopProductsAsync(
        DashboardSalesRange range,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "El limite debe estar entre 1 y 50.");
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = CreateDashboardPeriod(range, _timeProvider.GetUtcNow(), timeZone);

        var sold = await context.DetalleVentas
            .AsNoTracking()
            .Where(d => d.Venta.FechaVenta >= period.UtcStart && d.Venta.FechaVenta < period.UtcEndExclusive)
            .GroupBy(d => new
            {
                d.ProductoId,
                d.Producto.CodigoInterno,
                d.Producto.Nombre,
                StockActual = d.Producto.Skus.SelectMany(sku => sku.SaldosInventario)
                    .Sum(balance => balance.ExistenciaFisica)
            })
            .Select(g => new ProductUnits(
                g.Key.ProductoId,
                g.Key.CodigoInterno,
                g.Key.Nombre,
                g.Key.StockActual,
                g.Sum(x => x.Cantidad)))
            .ToListAsync(cancellationToken);
        var returned = await context.DetalleDevoluciones
            .AsNoTracking()
            .Where(d => d.Devolucion.FechaDevolucion >= period.UtcStart &&
                d.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .GroupBy(d => d.ProductoId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Cantidad) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, cancellationToken);

        return sold
            .Select(p => new DashboardTopProduct(
                p.ProductId,
                p.Code,
                p.Name,
                p.Quantity - returned.GetValueOrDefault(p.ProductId),
                p.Stock))
            .Where(p => p.NetUnitsSold > 0)
            .OrderByDescending(p => p.NetUnitsSold)
            .ThenBy(p => p.ProductName)
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<DashboardRecentActivity>> GetRecentActivityAsync(
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "El limite debe estar entre 1 y 50.");
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var rows = await context.Bitacora
            .AsNoTracking()
            .OrderByDescending(b => b.FechaAccion)
            .Take(limit)
            .Select(b => new AuditActivityRaw(
                b.BitacoraId,
                b.FechaAccion,
                b.Usuario == null ? "Sistema" : b.Usuario.NombreUsuario,
                b.TipoAccion,
                b.Descripcion))
            .ToListAsync(cancellationToken);

        return rows.Select(TranslateActivity).ToList();
    }

    private async ValueTask<AppDbContextLease> CreateContextAsync(CancellationToken cancellationToken) =>
        await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);

    private async Task EnsureActiveAdminAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var user = _currentSession.UsuarioActual;
        if (user is null || !_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede consultar el dashboard administrativo.");
        }

        var isActiveAdmin = await context.Usuarios
            .AsNoTracking()
            .AnyAsync(
                u => u.UsuarioId == user.UsuarioId && u.Activo && u.Rol == RolUsuario.ADMIN,
                cancellationToken);
        if (!isActiveAdmin)
        {
            throw new InvalidOperationException("La sesion ADMIN ya no se encuentra activa.");
        }
    }

    private static async Task<TimeZoneInfo> ResolveBusinessTimeZoneAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var configuredId = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => c.Clave == BusinessTimeZoneKey)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);
        var timeZoneId = string.IsNullOrWhiteSpace(configuredId)
            ? DefaultBusinessTimeZoneId
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

    private static AnalyticsPeriodDefinition CreateDashboardPeriod(
        DashboardSalesRange range,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        return range switch
        {
            DashboardSalesRange.LastSevenDays => CreateDayRange(
                range,
                today.AddDays(-6),
                localNow,
                timeZone,
                "Ultimos 7 dias"),
            DashboardSalesRange.LastThirtyDays => CreateDayRange(
                range,
                today.AddDays(-29),
                localNow,
                timeZone,
                "Ultimos 30 dias"),
            DashboardSalesRange.CurrentMonth => AnalyticsPeriodFactory.Create(
                new AnalyticsPeriodRequest(AnalyticsPeriodKind.CurrentMonth),
                now,
                timeZone),
            _ => throw new ArgumentOutOfRangeException(nameof(range), "Rango del dashboard no soportado.")
        };
    }

    private static AnalyticsPeriodDefinition CreateDayRange(
        DashboardSalesRange range,
        DateOnly startDate,
        DateTimeOffset localNow,
        TimeZoneInfo timeZone,
        string label)
    {
        var startLocalDateTime = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var start = new DateTimeOffset(startLocalDateTime, timeZone.GetUtcOffset(startLocalDateTime));
        return new AnalyticsPeriodDefinition(
            AnalyticsPeriodKind.CustomRange,
            start,
            localNow,
            start.UtcDateTime,
            localNow.UtcDateTime,
            AnalyticsGrouping.Day,
            label,
            true);
    }

    private static async Task<long> GetNetSalesAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var sales = await context.Ventas
            .AsNoTracking()
            .Where(v => v.FechaVenta >= period.UtcStart && v.FechaVenta < period.UtcEndExclusive)
            .SumAsync(v => (long?)v.TotalCentavos, cancellationToken) ?? 0;
        var returned = await context.Devoluciones
            .AsNoTracking()
            .Where(d => d.FechaDevolucion >= period.UtcStart && d.FechaDevolucion < period.UtcEndExclusive)
            .SumAsync(d => (long?)d.TotalDevueltoCentavos, cancellationToken) ?? 0;
        var changed = await context.DetallesCambioVenta
            .AsNoTracking()
            .Where(d => d.Devolucion.FechaDevolucion >= period.UtcStart &&
                d.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .SumAsync(d => (long?)d.TotalCentavos, cancellationToken) ?? 0;
        return sales - returned + changed;
    }

    private static DashboardRecentActivity TranslateActivity(AuditActivityRaw row)
    {
        var (title, icon) = row.Type switch
        {
            TipoBitacora.VENTA_REALIZADA => ("Venta registrada", "sale"),
            TipoBitacora.PRODUCTO_CREADO => ("Producto agregado", "product"),
            TipoBitacora.PRODUCTO_EDITADO => ("Producto actualizado", "product"),
            TipoBitacora.DEVOLUCION_REGISTRADA => ("Devolucion registrada", "return"),
            TipoBitacora.CAMBIO_REGISTRADO => ("Cambio de producto registrado", "return"),
            TipoBitacora.ANULACION_REGISTRADA => ("Venta anulada", "return"),
            TipoBitacora.COMPRA_CREADA => ("Compra registrada", "purchase"),
            TipoBitacora.RECEPCION_COMPRA_REGISTRADA => ("Mercancia recibida", "purchase"),
            TipoBitacora.COTIZACION_CREADA => ("Cotizacion creada", "quotation"),
            TipoBitacora.COTIZACION_CONVERTIDA_PEDIDO => ("Cotizacion convertida en pedido", "quotation"),
            TipoBitacora.PEDIDO_CREADO => ("Pedido creado", "order"),
            TipoBitacora.PEDIDO_ESTADO_MODIFICADO => ("Pedido actualizado", "order"),
            TipoBitacora.PEDIDO_CONVERTIDO_VENTA => ("Pedido convertido en venta", "order"),
            TipoBitacora.CAJA_ABIERTA => ("Caja abierta", "cash"),
            TipoBitacora.CAJA_CERRADA => ("Caja cerrada", "cash"),
            TipoBitacora.APORTE_EFECTIVO_CAJA => ("Aporte de efectivo a caja", "cash"),
            TipoBitacora.GASTO_REGISTRADO => ("Gasto registrado", "cash"),
            TipoBitacora.LOGIN => ("Inicio de sesion", "user"),
            TipoBitacora.LOGOUT => ("Cierre de sesion", "user"),
            _ => ("Actividad administrativa", "activity")
        };

        var description = SanitizeDescription(row.Description, title);
        return new DashboardRecentActivity(
            row.AuditId,
            row.DateUtc,
            row.UserName,
            title,
            description,
            icon);
    }

    private static string SanitizeDescription(string? description, string fallback)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return fallback;
        }

        var clean = string.Join(' ', description
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (clean.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("System.", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return clean.Length <= 180 ? clean : $"{clean[..177]}...";
    }

    private sealed record DatedAmount(DateTime DateUtc, long AmountCentavos);
    private sealed record CategorySaleLine(
        int SaleId,
        int SaleLineId,
        long SaleDiscountCentavos,
        long LineDiscountCentavos,
        long? StoredGeneralDiscountCentavos,
        long LineTotalCentavos,
        int CategoryId,
        string CategoryName);
    private sealed record CategoryAdjustment(int CategoryId, string CategoryName, long AmountCentavos);
    private sealed record ProductUnits(int ProductId, string Code, string Name, int Stock, int Quantity);
    private sealed record AuditActivityRaw(
        int AuditId,
        DateTime DateUtc,
        string UserName,
        TipoBitacora Type,
        string Description);
}
