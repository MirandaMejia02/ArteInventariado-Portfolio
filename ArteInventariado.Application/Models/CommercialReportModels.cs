namespace ArteInventariado.Application.Models;

public sealed record CommercialReportRequest(DateTime Desde, DateTime Hasta);

public sealed record ReportExportResult(
    string FilePath,
    string FileName,
    DateTime GeneratedAt,
    DateTime Desde,
    DateTime Hasta);
