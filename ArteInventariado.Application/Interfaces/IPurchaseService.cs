using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IPurchaseService
{
    Task<PurchaseDetail> CreateAsync(CreatePurchaseRequest request, CancellationToken cancellationToken = default);

    Task<PurchaseDetail> UpdateAsync(UpdatePurchaseRequest request, CancellationToken cancellationToken = default);
}
