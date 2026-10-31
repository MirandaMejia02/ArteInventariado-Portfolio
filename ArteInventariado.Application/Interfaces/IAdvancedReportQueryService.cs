using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAdvancedReportQueryService
{
    Task<AdvancedReportResult> QueryAsync(
        AdvancedReportRequest request,
        CancellationToken cancellationToken = default);
}
