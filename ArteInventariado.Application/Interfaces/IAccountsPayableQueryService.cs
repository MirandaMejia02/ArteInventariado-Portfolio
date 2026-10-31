using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAccountsPayableQueryService
{
    Task<IReadOnlyList<AccountPayableSummary>> SearchAsync(AccountsPayableFilter filter, CancellationToken cancellationToken = default);
    Task<AccountsPayableStats> GetStatsAsync(AccountsPayableFilter filter, CancellationToken cancellationToken = default);
    Task<AccountPayableDetail?> GetDetailAsync(int cuentaPorPagarId, CancellationToken cancellationToken = default);
    Task<AccountPayableDetail?> GetByPurchaseAsync(int compraId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierPaymentHistoryEntry>> GetPaymentHistoryAsync(SupplierPaymentHistoryFilter filter, CancellationToken cancellationToken = default);
    Task<SupplierPaymentSummary?> GetPaymentAsync(int abonoCuentaPorPagarId, CancellationToken cancellationToken = default);
    Task<SupplierFinancialSummary> GetSupplierSummaryAsync(int proveedorId, CancellationToken cancellationToken = default);
}
