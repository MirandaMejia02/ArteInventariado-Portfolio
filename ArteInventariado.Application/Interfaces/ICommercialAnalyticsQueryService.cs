using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICommercialAnalyticsQueryService
{
    Task<CommercialAnalyticsReport> GetAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);
}
