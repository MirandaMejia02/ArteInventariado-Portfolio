using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IPurchasePaymentService
{
    Task<PurchaseFinancialResult> RegisterInitialPaymentsAsync(
        RegisterInitialPurchasePaymentsRequest request,
        CancellationToken cancellationToken = default);
}
