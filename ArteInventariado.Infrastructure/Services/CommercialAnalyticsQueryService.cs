using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class CommercialAnalyticsQueryService : ICommercialAnalyticsQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ISalesAnalyticsQueryService _salesAnalytics;
    private readonly TimeProvider _timeProvider;

    [ActivatorUtilitiesConstructor]
    public CommercialAnalyticsQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ISalesAnalyticsQueryService salesAnalytics,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _salesAnalytics = salesAnalytics;
        _timeProvider = timeProvider;
    }

    internal CommercialAnalyticsQueryService(
        AppDbContext context,
        ISalesAnalyticsQueryService salesAnalytics,
        TimeProvider timeProvider)
    {
        _legacyContext = context;
        _salesAnalytics = salesAnalytics;
        _timeProvider = timeProvider;
    }

    public async Task<CommercialAnalyticsReport> GetAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Reuse the canonical analytics period and its ADMIN guard.
        var canonical = await _salesAnalytics.GetSalesAsync(request, cancellationToken);
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(lease.Context, cancellationToken);
        var current = await LoadFactsAsync(lease.Context, canonical.Period, cancellationToken);
        var compared = await LoadFactsAsync(lease.Context, canonical.ComparedPeriod, cancellationToken);

        return new CommercialAnalyticsReport(
            canonical.Period,
            canonical.ComparedPeriod,
            BuildQuotations(current, compared),
            BuildOrders(
                current,
                compared,
                TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone).Date),
            BuildConversions(current),
            BuildTrend(current, canonical.Period, timeZone),
            BuildCustomerRanking(current),
            "Cobertura exacta basada en fechas, estados e historiales registrados. " +
            "Cotización → venta directa no está disponible porque el flujo real requiere un pedido.");
    }

    private static async Task<CommercialFacts> LoadFactsAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var quotations = await context.Cotizaciones
            .AsNoTracking()
            .Where(quotation => quotation.FechaCreacion >= period.UtcStart &&
                quotation.FechaCreacion < period.UtcEndExclusive)
            .Select(quotation => new QuotationFact(
                quotation.CotizacionId,
                quotation.FechaCreacion,
                quotation.TotalCentavos,
                quotation.Estado,
                quotation.ClienteId,
                quotation.Cliente != null ? quotation.Cliente.CodigoCliente : "-",
                quotation.NombreDestinatarioSnapshot,
                quotation.HistorialEstados.Any(history => history.EstadoNuevo == EstadoCotizacion.ACEPTADA),
                quotation.HistorialEstados.Any(history => history.EstadoNuevo == EstadoCotizacion.RECHAZADA),
                quotation.HistorialEstados.Any(history => history.EstadoNuevo == EstadoCotizacion.VENCIDA),
                quotation.HistorialEstados
                    .Where(history => history.EstadoNuevo == EstadoCotizacion.CONVERTIDA)
                    .Select(history => (DateTime?)history.Fecha)
                    .Min(),
                quotation.PedidoPersonalizado != null))
            .ToListAsync(cancellationToken);

        var orders = await context.PedidosPersonalizados
            .AsNoTracking()
            .Where(order => order.FechaCreacion >= period.UtcStart &&
                order.FechaCreacion < period.UtcEndExclusive)
            .Select(order => new OrderFact(
                order.PedidoPersonalizadoId,
                order.FechaCreacion,
                order.FechaPedido,
                order.FechaPrometida,
                order.FechaEntregaReal,
                order.Estado,
                order.TotalEstimadoCentavos,
                order.SaldoPendienteCentavos,
                order.VentaId.HasValue,
                order.ClienteId,
                order.Cliente != null ? order.Cliente.CodigoCliente : "-",
                order.NombreDestinatarioSnapshot,
                order.CotizacionId.HasValue,
                order.HistorialEstados.Any(history => history.EstadoNuevo == EstadoPedidoPersonalizado.ENTREGADO),
                order.HistorialEstados.Any(history => history.EstadoNuevo == EstadoPedidoPersonalizado.CANCELADO)))
            .ToListAsync(cancellationToken);

        var payments = await context.PagosPedidoPersonalizado
            .AsNoTracking()
            .Where(payment => payment.Fecha >= period.UtcStart && payment.Fecha < period.UtcEndExclusive)
            .GroupBy(_ => 1)
            .Select(group => new PaymentTotals(
                group.Where(payment => payment.TipoPago == TipoPagoPedido.ANTICIPO)
                    .Sum(payment => (long?)payment.MontoCentavos) ?? 0,
                group.Sum(payment => (long?)payment.MontoCentavos) ?? 0))
            .FirstOrDefaultAsync(cancellationToken) ?? new PaymentTotals(0, 0);

        var refunds = await context.ReembolsosPedidoPersonalizado
            .AsNoTracking()
            .Where(refund => refund.Fecha >= period.UtcStart && refund.Fecha < period.UtcEndExclusive)
            .SumAsync(refund => (long?)refund.MontoCentavos, cancellationToken) ?? 0;

        return new CommercialFacts(quotations, orders, payments.AdvancesCentavos, payments.PaymentsCentavos, refunds);
    }

    private static QuotationCommercialAnalytics BuildQuotations(
        CommercialFacts current,
        CommercialFacts compared)
    {
        var converted = current.Quotations.Where(item => item.ConvertedAtUtc.HasValue).ToList();
        return new QuotationCommercialAnalytics(
            Metric(current.Quotations.Count, compared.Quotations.Count),
            Metric(current.Quotations.Sum(item => item.TotalCentavos), compared.Quotations.Sum(item => item.TotalCentavos)),
            current.Quotations.Count(item => item.WasAccepted),
            current.Quotations.Count(item => item.WasRejected),
            current.Quotations.Count(item => item.WasExpired),
            current.Quotations.Count(item => item.State == EstadoCotizacion.EMITIDA),
            converted.Count,
            converted.Sum(item => item.TotalCentavos),
            AveragePositiveHours(converted.Select(item => (item.ConvertedAtUtc!.Value - item.CreatedUtc).TotalHours)));
    }

    private static CustomOrderCommercialAnalytics BuildOrders(
        CommercialFacts current,
        CommercialFacts compared,
        DateTime businessDate)
    {
        var completed = current.Orders.Where(item => item.DeliveryUtc.HasValue).ToList();
        return new CustomOrderCommercialAnalytics(
            Metric(current.Orders.Count, compared.Orders.Count),
            current.Orders.Count(item => item.State is EstadoPedidoPersonalizado.BORRADOR or EstadoPedidoPersonalizado.CONFIRMADO),
            current.Orders.Count(item => item.State == EstadoPedidoPersonalizado.EN_PRODUCCION),
            current.Orders.Count(item => item.State == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA),
            current.Orders.Count(item => item.State == EstadoPedidoPersonalizado.ENTREGADO),
            current.Orders.Count(item => item.State == EstadoPedidoPersonalizado.CANCELADO),
            current.Orders.Count(item => IsDelayed(item, businessDate)),
            current.Orders.Sum(item => item.TotalCentavos),
            current.AdvancesCentavos,
            current.PaymentsCentavos,
            current.RefundsCentavos,
            current.Orders.Sum(item => item.OutstandingCentavos),
            current.Orders.Count(item => item.ConvertedToSale),
            AveragePositiveHours(completed.Select(item => (item.DeliveryUtc!.Value - item.CreatedUtc).TotalHours)));
    }

    private static IReadOnlyList<CommercialConversionMetric> BuildConversions(CommercialFacts facts)
    {
        var quotations = facts.Quotations.Count;
        var orders = facts.Orders.Count;
        return
        [
            Rate("Cotizaciones aceptadas", facts.Quotations.Count(item => item.WasAccepted), quotations),
            Rate("Cotización → pedido", facts.Quotations.Count(item => item.HasOrder), quotations),
            new CommercialConversionMetric(
                "Cotización → venta directa", 0, 0, null, false,
                "No existe relación directa; el flujo real pasa por pedido."),
            Rate("Pedido → venta", facts.Orders.Count(item => item.ConvertedToSale), orders),
            Rate("Pedidos completados", facts.Orders.Count(item => item.WasDelivered), orders),
            Rate("Pedidos cancelados", facts.Orders.Count(item => item.WasCancelled), orders)
        ];
    }

    private static IReadOnlyList<CommercialAnalyticsPoint> BuildTrend(
        CommercialFacts facts,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone)
    {
        return BuildBuckets(period, timeZone)
            .Select(bucket =>
            {
                var quotations = facts.Quotations.Where(item => BucketKey(item.CreatedUtc, period.Grouping, timeZone) == bucket.Key).ToList();
                var orders = facts.Orders.Where(item => BucketKey(item.CreatedUtc, period.Grouping, timeZone) == bucket.Key).ToList();
                return new CommercialAnalyticsPoint(
                    bucket.Start,
                    bucket.Label,
                    quotations.Count,
                    quotations.Sum(item => item.TotalCentavos),
                    orders.Count,
                    orders.Sum(item => item.TotalCentavos));
            })
            .ToList();
    }

    private static IReadOnlyList<CommercialCustomerRankingRow> BuildCustomerRanking(CommercialFacts facts)
    {
        var quoteRows = facts.Quotations.GroupBy(item => new { item.CustomerId, item.CustomerCode, item.CustomerName })
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Amount = group.Sum(item => item.TotalCentavos)
            });
        var orderRows = facts.Orders.GroupBy(item => new { item.CustomerId, item.CustomerCode, item.CustomerName })
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Amount = group.Sum(item => item.TotalCentavos)
            });
        return quoteRows.Select(row => new { row.Key.CustomerId, row.Key.CustomerCode, row.Key.CustomerName })
            .Concat(orderRows.Select(row => new { row.Key.CustomerId, row.Key.CustomerCode, row.Key.CustomerName }))
            .Distinct()
            .Select(key =>
            {
                var quote = quoteRows.FirstOrDefault(row => row.Key.CustomerId == key.CustomerId &&
                    row.Key.CustomerCode == key.CustomerCode && row.Key.CustomerName == key.CustomerName);
                var order = orderRows.FirstOrDefault(row => row.Key.CustomerId == key.CustomerId &&
                    row.Key.CustomerCode == key.CustomerCode && row.Key.CustomerName == key.CustomerName);
                return new CommercialCustomerRankingRow(
                    key.CustomerId,
                    key.CustomerCode,
                    key.CustomerName,
                    quote?.Count ?? 0,
                    quote?.Amount ?? 0,
                    order?.Count ?? 0,
                    order?.Amount ?? 0);
            })
            .OrderByDescending(row => row.QuotedAmountCentavos + row.OrderAmountCentavos)
            .ThenBy(row => row.CustomerName)
            .Take(10)
            .ToList();
    }

    internal static bool IsDelayed(DateTime orderedDate, DateTime promisedDate, DateTime? deliveredDate, EstadoPedidoPersonalizado state, DateTime businessDate) =>
        IsDelayed(new OrderFact(0, DateTime.SpecifyKind(orderedDate, DateTimeKind.Utc), orderedDate, promisedDate,
            deliveredDate, state, 0, 0, false, null, "-", "-", false, false, false), businessDate);

    private static bool IsDelayed(OrderFact item, DateTime businessDate)
    {
        var validPromisedDate = item.PromisedDate != default && item.PromisedDate.Date >= item.OrderDate.Date;
        if (!validPromisedDate || item.State is EstadoPedidoPersonalizado.ENTREGADO or EstadoPedidoPersonalizado.CANCELADO)
        {
            return false;
        }
        return item.PromisedDate.Date < businessDate.Date;
    }

    private static AnalyticsMetricResult Metric(long current, long previous) =>
        new(current, Comparison(current, previous));

    private static MetricComparison Comparison(long current, long previous)
    {
        if (previous == 0)
        {
            return new MetricComparison(current, previous, null,
                current == 0 ? MetricComparisonState.NoComparisonBase : MetricComparisonState.NewMovement);
        }
        var percentage = (current - previous) * 100m / previous;
        var state = percentage > 0 ? MetricComparisonState.Increase : percentage < 0
            ? MetricComparisonState.Decrease : MetricComparisonState.NoChange;
        return new MetricComparison(current, previous, percentage, state);
    }

    private static CommercialConversionMetric Rate(string name, int numerator, int denominator) =>
        new(name, numerator, denominator,
            denominator == 0 ? null : numerator * 100m / denominator,
            true);

    private static decimal? AveragePositiveHours(IEnumerable<double> hours)
    {
        var values = hours.Where(value => value >= 0).ToList();
        return values.Count == 0 ? null : Math.Round((decimal)values.Average(), 2);
    }

    private static IReadOnlyList<TimeBucket> BuildBuckets(AnalyticsPeriodDefinition period, TimeZoneInfo timeZone)
    {
        var buckets = new List<TimeBucket>();
        var cursor = TimeZoneInfo.ConvertTime(new DateTimeOffset(period.UtcStart, TimeSpan.Zero), timeZone);
        var end = TimeZoneInfo.ConvertTime(new DateTimeOffset(period.UtcEndExclusive, TimeSpan.Zero), timeZone);
        while (cursor < end)
        {
            var key = period.Grouping switch
            {
                AnalyticsGrouping.Hour => new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0),
                AnalyticsGrouping.Day => cursor.Date,
                AnalyticsGrouping.Month => new DateTime(cursor.Year, cursor.Month, 1),
                _ => new DateTime(cursor.Year, 1, 1)
            };
            if (buckets.Count == 0 || buckets[^1].Key != key)
            {
                var label = period.Grouping switch
                {
                    AnalyticsGrouping.Hour => key.ToString("HH:mm"),
                    AnalyticsGrouping.Day => key.ToString("dd/MM"),
                    AnalyticsGrouping.Month => key.ToString("MMM yy"),
                    _ => key.ToString("yyyy")
                };
                buckets.Add(new TimeBucket(key, cursor, label));
            }
            cursor = period.Grouping switch
            {
                AnalyticsGrouping.Hour => cursor.AddHours(1),
                AnalyticsGrouping.Day => cursor.AddDays(1),
                AnalyticsGrouping.Month => cursor.AddMonths(1),
                _ => cursor.AddYears(1)
            };
        }
        return buckets;
    }

    private static DateTime BucketKey(DateTime utcDate, AnalyticsGrouping grouping, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDate, DateTimeKind.Utc), timeZone);
        return grouping switch
        {
            AnalyticsGrouping.Hour => new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0),
            AnalyticsGrouping.Day => local.Date,
            AnalyticsGrouping.Month => new DateTime(local.Year, local.Month, 1),
            _ => new DateTime(local.Year, 1, 1)
        };
    }

    private static async Task<TimeZoneInfo> ResolveBusinessTimeZoneAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var id = await context.ConfiguracionSistema.AsNoTracking()
            .Where(item => item.Clave == DashboardQueryService.BusinessTimeZoneKey)
            .Select(item => item.Valor)
            .FirstOrDefaultAsync(cancellationToken);
        id = string.IsNullOrWhiteSpace(id) ? DashboardQueryService.DefaultBusinessTimeZoneId : id;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Local; }
    }

    private sealed record QuotationFact(
        int Id, DateTime CreatedUtc, long TotalCentavos, EstadoCotizacion State,
        int? CustomerId, string CustomerCode, string CustomerName,
        bool WasAccepted, bool WasRejected, bool WasExpired, DateTime? ConvertedAtUtc, bool HasOrder);

    private sealed record OrderFact(
        int Id, DateTime CreatedUtc, DateTime OrderDate, DateTime PromisedDate, DateTime? DeliveryUtc,
        EstadoPedidoPersonalizado State, long TotalCentavos, long OutstandingCentavos, bool ConvertedToSale,
        int? CustomerId, string CustomerCode, string CustomerName, bool FromQuotation,
        bool WasDelivered, bool WasCancelled);

    private sealed record CommercialFacts(
        IReadOnlyList<QuotationFact> Quotations,
        IReadOnlyList<OrderFact> Orders,
        long AdvancesCentavos,
        long PaymentsCentavos,
        long RefundsCentavos);

    private sealed record PaymentTotals(long AdvancesCentavos, long PaymentsCentavos);
    private sealed record TimeBucket(DateTime Key, DateTimeOffset Start, string Label);
}
