using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICreditService
{
    Task<CreditPolicySettings> GetGeneralPolicyAsync(CancellationToken cancellationToken = default);

    Task UpdateGeneralPolicyAsync(
        UpdateCreditPolicySettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerCreditPolicy> GetCustomerPolicyAsync(
        int clienteId,
        CancellationToken cancellationToken = default);

    Task UpdateCustomerPolicyAsync(
        UpdateCustomerCreditPolicyRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerCreditSummary> GetCustomerSummaryAsync(
        int clienteId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountReceivableSummary>> GetAccountsAsync(
        AccountReceivableFilter filter,
        CancellationToken cancellationToken = default);

    Task<AccountReceivableDetail?> GetAccountDetailAsync(
        int cuentaPorCobrarId,
        CancellationToken cancellationToken = default);
}
