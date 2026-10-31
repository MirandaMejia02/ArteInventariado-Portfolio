using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAdvancedReportFilterOptionsService
{
    Task<IReadOnlyList<AdvancedReportFilterOption>> GetOptionsAsync(
        AdvancedReportKind reportKind,
        AdvancedReportFilterKind filterKind,
        string? search = null,
        int limit = 75,
        CancellationToken cancellationToken = default);
}
