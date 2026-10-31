using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISalesQueryService
{
    Task<IReadOnlyList<SalesHistorySummary>> GetSalesAsync(
        SalesHistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<SalesHistoryDetail?> GetSaleDetailAsync(
        int ventaId,
        CancellationToken cancellationToken = default);
}
