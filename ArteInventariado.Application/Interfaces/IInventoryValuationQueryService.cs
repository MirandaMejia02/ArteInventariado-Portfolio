using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IInventoryValuationQueryService
{
    Task<InventoryValuationReport> QueryAsync(
        InventoryValuationRequest request,
        CancellationToken cancellationToken = default);
}
