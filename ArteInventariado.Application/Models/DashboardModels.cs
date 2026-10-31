namespace ArteInventariado.Application.Models;

public enum DashboardSalesRange
{
    LastSevenDays = 1,
    LastThirtyDays = 2,
    CurrentMonth = 3
}

public sealed record DashboardSalesToday(
    long NetSalesCentavos,
    int SaleCount,
    MetricComparison Comparison);

public sealed record DashboardCashExpected(
    bool HasOpenCash,
    int? CashId,
    long? ExpectedAmountCentavos);

public sealed record DashboardOverview(
    DateTime GeneratedAtUtc,
    DashboardSalesToday SalesToday,
    int ActiveProducts,
    int RegisteredCustomers,
    DashboardCashExpected CashExpected,
    int PendingOrders);

public sealed record DashboardSalesTrendPoint(
    DateOnly Date,
    long NetSalesCentavos);

public sealed record DashboardSalesTrend(
    DashboardSalesRange Range,
    string Label,
    IReadOnlyList<DashboardSalesTrendPoint> Points);

public sealed record DashboardCategorySalesPoint(
    int CategoryId,
    string CategoryName,
    long NetSalesCentavos,
    decimal Percentage);

public sealed record DashboardCategorySales(
    DashboardSalesRange Range,
    long PositiveNetSalesCentavos,
    bool HasNegativeAdjustments,
    IReadOnlyList<DashboardCategorySalesPoint> Categories);

public sealed record DashboardTopProduct(
    int ProductId,
    string Code,
    string ProductName,
    int NetUnitsSold,
    int CurrentStock);

public sealed record DashboardRecentActivity(
    int AuditId,
    DateTime OccurredAtUtc,
    string UserName,
    string Title,
    string Description,
    string IconKey);
