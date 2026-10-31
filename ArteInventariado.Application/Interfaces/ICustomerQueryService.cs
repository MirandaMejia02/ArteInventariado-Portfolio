using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICustomerQueryService
{
    Task<IReadOnlyList<CustomerSummary>> SearchAsync(CustomerFilter filter, CancellationToken cancellationToken = default);

    Task<CustomerDetail?> GetByIdAsync(int clienteId, CancellationToken cancellationToken = default);

    Task<CustomerSaleSelection?> GetSaleSelectionAsync(int clienteId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerPurchaseHistoryRow>> GetPurchaseHistoryAsync(
        int clienteId,
        CancellationToken cancellationToken = default);

    Task<SalesHistoryDetail?> GetPurchaseDetailAsync(
        int clienteId,
        int ventaId,
        CancellationToken cancellationToken = default);
}
