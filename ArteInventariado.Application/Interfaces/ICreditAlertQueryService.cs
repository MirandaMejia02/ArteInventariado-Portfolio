using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICreditAlertQueryService
{
    Task<CreditAlertSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task UpdateSettingsAsync(
        UpdateCreditAlertSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<CreditAlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditAlertItem>> GetAlertsAsync(
        CreditAlertFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<CreditCustomerAlertIndicator> GetCustomerIndicatorAsync(
        int clienteId,
        CancellationToken cancellationToken = default);
}
