namespace ArteInventariado.Application.Models;

public sealed record CommercialConversionMetric(
    string Name,
    int Numerator,
    int Denominator,
    decimal? Percentage,
    bool Available,
    string? Limitation = null);

public sealed record QuotationCommercialAnalytics(
    AnalyticsMetricResult Created,
    AnalyticsMetricResult QuotedAmount,
    int Accepted,
    int Rejected,
    int Expired,
    int Pending,
    int Converted,
    long ConvertedValueCentavos,
    decimal? AverageConversionHours);

public sealed record CustomOrderCommercialAnalytics(
    AnalyticsMetricResult Created,
    int Pending,
    int InProduction,
    int ReadyForDelivery,
    int Delivered,
    int Cancelled,
    int Delayed,
    long TotalAmountCentavos,
    long AdvancesCentavos,
    long PaymentsCentavos,
    long RefundsCentavos,
    long OutstandingBalanceCentavos,
    int ConvertedToSale,
    decimal? AverageFulfillmentHours);

public sealed record CommercialAnalyticsPoint(
    DateTimeOffset BucketStart,
    string Label,
    int QuotationsCreated,
    long QuotedAmountCentavos,
    int OrdersCreated,
    long OrderAmountCentavos);

public sealed record CommercialCustomerRankingRow(
    int? CustomerId,
    string CustomerCode,
    string CustomerName,
    int Quotations,
    long QuotedAmountCentavos,
    int Orders,
    long OrderAmountCentavos);

public sealed record CommercialAnalyticsReport(
    AnalyticsPeriodDefinition Period,
    AnalyticsPeriodDefinition ComparedPeriod,
    QuotationCommercialAnalytics Quotations,
    CustomOrderCommercialAnalytics Orders,
    IReadOnlyList<CommercialConversionMetric> Conversions,
    IReadOnlyList<CommercialAnalyticsPoint> Trend,
    IReadOnlyList<CommercialCustomerRankingRow> TopCustomers,
    string CoverageNote);
