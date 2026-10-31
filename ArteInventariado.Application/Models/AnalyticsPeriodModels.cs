namespace ArteInventariado.Application.Models;

public enum AnalyticsPeriodKind
{
    Today = 1,
    Yesterday = 2,
    CurrentWeek = 3,
    CurrentMonth = 4,
    LastThreeMonths = 5,
    CurrentQuarter = 6,
    PreviousQuarter = 7,
    LastSixMonths = 8,
    CurrentSemester = 9,
    PreviousSemester = 10,
    CurrentYear = 11,
    PreviousYear = 12,
    MultipleYears = 13,
    CustomRange = 14,
    AllHistory = 15
}

public enum AnalyticsGrouping
{
    Hour = 1,
    Day = 2,
    Month = 3,
    Year = 4
}

public enum MetricComparisonState
{
    Increase = 1,
    Decrease = 2,
    NoChange = 3,
    NewMovement = 4,
    NoComparisonBase = 5
}

public sealed record AnalyticsPeriodRequest(
    AnalyticsPeriodKind Kind,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int? StartYear = null,
    int? EndYear = null);

public sealed record AnalyticsPeriodDefinition(
    AnalyticsPeriodKind Kind,
    DateTimeOffset LocalStart,
    DateTimeOffset LocalEndExclusive,
    DateTime UtcStart,
    DateTime UtcEndExclusive,
    AnalyticsGrouping Grouping,
    string DisplayName,
    bool IsIncomplete);

public sealed record MetricComparison(
    long CurrentValue,
    long PreviousValue,
    decimal? Percentage,
    MetricComparisonState State);
