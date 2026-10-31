using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IMonthlyReportExportService
{
    Task<MonthlyReportExportResult> ExportMonthlyReportAsync(
        MonthlyReportExportRequest request,
        CancellationToken cancellationToken = default);
}
