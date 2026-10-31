using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISaleCorrectionQueryService
{
    Task<IReadOnlyList<SaleCorrectionSaleSummary>> SearchSalesForCorrectionAsync(
        SaleCorrectionSalesFilter filter,
        CancellationToken cancellationToken = default);

    Task<SaleCorrectionSearchResult?> GetSaleForCorrectionAsync(
        string numeroVenta,
        CancellationToken cancellationToken = default);

    Task<SaleCorrectionSearchResult?> GetSaleForCorrectionByIdAsync(
        int ventaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnHistorySummary>> GetReturnHistoryAsync(
        ReturnHistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<ReturnHistoryDetail?> GetReturnDetailAsync(
        int devolucionId,
        CancellationToken cancellationToken = default);
}
