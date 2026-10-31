using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IDashboardQueryService
{
    Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<DashboardSalesTrend> GetSalesTrendAsync(
        DashboardSalesRange range,
        CancellationToken cancellationToken = default);

    Task<DashboardCategorySales> GetCategorySalesAsync(
        DashboardSalesRange range,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardTopProduct>> GetTopProductsAsync(
        DashboardSalesRange range,
        int limit = 5,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardRecentActivity>> GetRecentActivityAsync(
        int limit = 5,
        CancellationToken cancellationToken = default);
}
