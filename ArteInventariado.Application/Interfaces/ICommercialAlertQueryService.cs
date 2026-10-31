using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICommercialAlertQueryService
{
    Task<CommercialAlertSearchResult> SearchAsync(
        CommercialAlertFilter filter,
        CancellationToken cancellationToken = default);

    Task<CommercialAlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
