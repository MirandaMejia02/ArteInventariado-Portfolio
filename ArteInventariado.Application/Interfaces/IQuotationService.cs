using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IQuotationService
{
    Task<QuotationDetail> CreateDraftAsync(QuotationSaveRequest request, CancellationToken cancellationToken = default);
    Task<QuotationDetail> UpdateDraftAsync(QuotationSaveRequest request, CancellationToken cancellationToken = default);
    Task<QuotationDetail> EmitAsync(int cotizacionId, CancellationToken cancellationToken = default);
    Task<QuotationDetail> ChangeStateAsync(QuotationStateChangeRequest request, CancellationToken cancellationToken = default);
    Task<QuotationDetail> DuplicateAsync(int cotizacionId, CancellationToken cancellationToken = default);
    Task<int> UpdateExpiredAsync(CancellationToken cancellationToken = default);
}
