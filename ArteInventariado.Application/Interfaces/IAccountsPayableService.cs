using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAccountsPayableService
{
    Task<SupplierPaymentResult> RegisterPaymentAsync(
        RegisterSupplierPaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<SupplierPaymentBatchResult> RegisterPaymentsAsync(
        RegisterSupplierPaymentBatchRequest request,
        CancellationToken cancellationToken = default);
}
