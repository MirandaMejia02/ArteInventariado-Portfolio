using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICommercialOperationsReportService
{
    Task<ReportExportResult> ExportCommercialOperationsAsync(
        CommercialReportRequest request,
        CancellationToken cancellationToken = default);

    Task<ReportExportResult> ExportCashDetailAsync(
        CommercialReportRequest request,
        CancellationToken cancellationToken = default);
}
