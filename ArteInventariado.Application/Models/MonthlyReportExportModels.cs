namespace ArteInventariado.Application.Models;

public sealed record MonthlyReportExportRequest(
    int Year,
    int Month);

public sealed record MonthlyReportExportResult(
    string FilePath,
    string FileName,
    int Year,
    int Month,
    DateTime GeneratedAt);
