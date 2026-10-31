namespace ArteInventariado.Application.Models;

public enum ProductProfitabilitySort
{
    UnitsSold = 1,
    NetSales = 2,
    TotalGain = 3,
    Margin = 4
}

public enum ProductProfitabilityCoverageState
{
    NoSales = 1,
    Complete = 2,
    Partial = 3,
    NoCoverage = 4
}

public sealed record ProductProfitabilityRequest(
    AnalyticsQueryRequest Analytics,
    string? Search = null,
    int? ProductId = null,
    int? CategoryId = null,
    ProductProfitabilitySort Sort = ProductProfitabilitySort.UnitsSold,
    bool Descending = true,
    int? TopLimit = null,
    IReadOnlyCollection<int>? SelectedProductIds = null,
    int Page = 1,
    int PageSize = 200,
    int? SubcategoryId = null,
    AnalyticsSubcategoryFilter SubcategoryFilter = AnalyticsSubcategoryFilter.All);

public sealed record ProductProfitabilityRow(
    int ProductId,
    string ProductCode,
    string ProductName,
    string CategoryName,
    int NetUnits,
    int ReturnedUnits,
    decimal? RealizedSalePriceCentavos,
    decimal? AppliedUnitCostCentavos,
    long NetSalesCentavos,
    long? KnownCostCentavos,
    long? TotalCostCentavos,
    decimal? UnitGainCentavos,
    long? TotalGainCentavos,
    decimal? MarginPercentage,
    decimal? CostCoveragePercentage,
    ProductProfitabilityCoverageState CoverageState,
    int? CategoryId = null,
    IReadOnlyList<string>? SkuCodes = null,
    int? SubcategoryId = null,
    string? SubcategoryName = null,
    DateTime? FirstSaleUtc = null,
    DateTime? LastSaleUtc = null);

public sealed record ProductProfitabilityReport(
    AnalyticsPeriodDefinition Period,
    IReadOnlyList<ProductProfitabilityRow> Rows,
    int TotalRows,
    int ProductsWithSales,
    long NetSalesCentavos,
    long? KnownCostCentavos,
    long? CalculableGainCentavos,
    decimal? OverallMarginPercentage,
    decimal? CostCoveragePercentage,
    int CompleteProducts,
    int PartialProducts,
    int NoCoverageProducts,
    int NoSalesProducts,
    string CoverageNote);
