using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IQuotationQueryService
{
    Task<IReadOnlyList<QuotationSummary>> SearchAsync(QuotationFilter filter, CancellationToken cancellationToken = default);
    Task<QuotationDetail?> GetByIdAsync(int cotizacionId, CancellationToken cancellationToken = default);
    Task<QuotationStats> GetStatsAsync(QuotationFilter filter, CancellationToken cancellationToken = default);
}
