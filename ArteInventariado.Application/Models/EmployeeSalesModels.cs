namespace ArteInventariado.Application.Models;

/// <summary>
/// Local-date range used by employee sales queries. Infrastructure converts it
/// to UTC with the configured business timezone before querying SQLite.
/// </summary>
public sealed record EmployeeSalesPeriod(DateOnly StartDate, DateOnly EndDateExclusive);

public enum EmployeeSalesGrouping
{
    Day = 1,
    Week = 2,
    Month = 3
}

public sealed record EmployeeSalesSummary(
    int UserId,
    int SaleCount,
    long TotalSoldCentavos,
    long AverageTicketCentavos,
    DateTime? LastSaleUtc);

public sealed record EmployeeSalesTrendPoint(
    DateOnly LocalPeriodStart,
    int SaleCount,
    long TotalSoldCentavos);

public sealed record EmployeeSalesReport(
    int UserId,
    EmployeeSalesPeriod? Period,
    EmployeeSalesSummary Summary,
    IReadOnlyList<EmployeeSalesTrendPoint> DailyTrend);

public sealed record EmployeeSalesRankingRow(
    int Position,
    int UserId,
    string FullName,
    string UserName,
    bool Active,
    int SaleCount,
    long TotalSoldCentavos,
    long AverageTicketCentavos);
