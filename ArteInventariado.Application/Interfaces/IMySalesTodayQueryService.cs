using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IMySalesTodayQueryService
{
    Task<IReadOnlyList<MySalesTodaySummary>> GetSalesAsync(
        CancellationToken cancellationToken = default);

    Task<MySalesTodayDetail?> GetSaleDetailAsync(
        int ventaId,
        CancellationToken cancellationToken = default);
}
