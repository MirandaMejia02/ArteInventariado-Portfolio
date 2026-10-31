using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IFinancialAnalyticsQueryService
{
    Task<FinancialAnalyticsReport> GetAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);
}
