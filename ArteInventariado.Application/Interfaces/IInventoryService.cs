using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IInventoryService
{
    [Obsolete("Use AdjustPhysicalCountAsync con SKU, ubicacion, reautenticacion y OperacionId.")]
    Task<InventoryMovementSummary> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default);

    Task<InventoryAdjustmentResult> AdjustPhysicalCountAsync(
        AdjustInventoryCountRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryMovementSummary>> GetMovementsByProductAsync(int productoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryBalanceSummary>> GetBalancesByProductAsync(int productoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryBalanceSummary>> TransferAsync(TransferInventoryRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryBalanceSummary>> RegisterWasteAsync(RegisterInventoryWasteRequest request, CancellationToken cancellationToken = default);

    Task SetExhibitionMinimumAsync(SetExhibitionMinimumRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReplenishmentSummary>> GetReplenishmentAsync(CancellationToken cancellationToken = default);

    Task PostponeReplenishmentAsync(PostponeReplenishmentRequest request, CancellationToken cancellationToken = default);

    Task DismissReplenishmentAsync(DismissReplenishmentRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StagnantInventorySummary>> GetStagnantInventoryAsync(StagnantInventoryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReconciliationSummary>> ReconcileAsync(CancellationToken cancellationToken = default);
}
