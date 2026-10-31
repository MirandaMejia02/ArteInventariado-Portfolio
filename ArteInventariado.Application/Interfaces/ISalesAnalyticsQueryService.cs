using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public static class SalesAnalyticsQueryLimits
{
    public const int MinimumProducts = 1;
    public const int MaximumProducts = 100;
}

public interface ISalesAnalyticsQueryService
{
    Task<SalesAnalyticsReport> GetSalesAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductAnalyticsReport> GetProductsAsync(
        AnalyticsQueryRequest request,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<ProductTrendReport> GetProductTrendAsync(
        int productId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<CategoryAnalyticsReport> GetCategoriesAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);
}
