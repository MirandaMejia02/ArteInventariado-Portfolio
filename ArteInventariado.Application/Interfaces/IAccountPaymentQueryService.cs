using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAccountPaymentQueryService
{
    Task<AccountPaymentPreview> PreviewAsync(
        AccountPaymentPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountPaymentSummary>> GetHistoryAsync(
        AccountPaymentHistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<AccountPaymentDetail?> GetDetailAsync(
        int abonoCuentaPorCobrarId,
        CancellationToken cancellationToken = default);
}
