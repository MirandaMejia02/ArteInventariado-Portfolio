using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IPurchaseQueryService
{
    Task<IReadOnlyList<PurchaseSummary>> SearchAsync(PurchaseFilter filter, CancellationToken cancellationToken = default);

    Task<PurchaseDetail?> GetByIdAsync(int compraId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseSummary>> GetBySupplierAsync(int proveedorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CostHistoryRow>> GetCostHistoryByPurchaseAsync(int compraId, CancellationToken cancellationToken = default);

    Task<PurchaseStats> GetStatsAsync(PurchaseFilter filter, CancellationToken cancellationToken = default);
}
