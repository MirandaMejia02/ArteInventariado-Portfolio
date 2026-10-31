using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IPurchaseReceptionService
{
    Task<PurchaseReceptionSummary> RegisterReceptionAsync(RegisterPurchaseReceptionRequest request, CancellationToken cancellationToken = default);
}
