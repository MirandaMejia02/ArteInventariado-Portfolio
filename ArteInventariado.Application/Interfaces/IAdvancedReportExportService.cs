using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAdvancedReportExportService
{
    Task<AdvancedReportExportResult> ExportExcelAsync(
        AdvancedReportRequest request,
        CancellationToken cancellationToken = default);
}
