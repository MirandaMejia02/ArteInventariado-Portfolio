using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;

namespace ArteInventariado.Tests;

public class AnalyticsPeriodFactoryTests
{
    private static readonly TimeZoneInfo BusinessTimeZone =
        TimeZoneInfo.CreateCustomTimeZone("ArteInventariado-Test", TimeSpan.FromHours(-6), "Test", "Test");

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 15, 30, 0, TimeSpan.FromHours(-6));

    [Fact]
    public void Today_UsesLocalMidnightAndCurrentTime()
    {
        var period = Create(AnalyticsPeriodKind.Today);

        Assert.Equal(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.FromHours(-6)), period.LocalStart);
        Assert.Equal(Now, period.LocalEndExclusive);
        Assert.Equal(AnalyticsGrouping.Hour, period.Grouping);
        Assert.True(period.IsIncomplete);
    }

    [Fact]
    public void CurrentWeek_StartsOnMonday()
    {
        var period = Create(AnalyticsPeriodKind.CurrentWeek);

        Assert.Equal(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.FromHours(-6)), period.LocalStart);
        Assert.Equal(AnalyticsGrouping.Day, period.Grouping);
    }

    [Fact]
    public void CurrentQuarter_IsCalendarQuarter()
    {
        var period = Create(AnalyticsPeriodKind.CurrentQuarter);

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-6)), period.LocalStart);
        Assert.Equal(AnalyticsGrouping.Month, period.Grouping);
    }

    [Fact]
    public void PreviousQuarter_IsCompleteCalendarQuarter()
    {
        var period = Create(AnalyticsPeriodKind.PreviousQuarter);

        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.FromHours(-6)), period.LocalStart);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-6)), period.LocalEndExclusive);
        Assert.False(period.IsIncomplete);
    }

    [Fact]
    public void LastThreeMonths_IsRollingAndNotCalendarQuarter()
    {
        var period = Create(AnalyticsPeriodKind.LastThreeMonths);

        Assert.Equal(new DateTimeOffset(2026, 5, 8, 15, 30, 0, TimeSpan.FromHours(-6)), period.LocalStart);
        Assert.NotEqual(Create(AnalyticsPeriodKind.CurrentQuarter).LocalStart, period.LocalStart);
    }

    [Fact]
    public void CurrentAndPreviousSemester_UseCalendarBoundaries()
    {
        var current = Create(AnalyticsPeriodKind.CurrentSemester);
        var previous = Create(AnalyticsPeriodKind.PreviousSemester);

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-6)), current.LocalStart);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-6)), previous.LocalStart);
        Assert.Equal(current.LocalStart, previous.LocalEndExclusive);
    }

    [Fact]
    public void LeapYear_CustomRange_IncludesFebruary29()
    {
        var request = new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.CustomRange,
            new DateOnly(2024, 2, 28),
            new DateOnly(2024, 3, 1));

        var period = AnalyticsPeriodFactory.Create(request, Now, BusinessTimeZone);

        Assert.Equal(3, (period.LocalEndExclusive - period.LocalStart).TotalDays);
        Assert.Equal(AnalyticsGrouping.Day, period.Grouping);
    }

    [Fact]
    public void CurrentMonth_PreviousEquivalent_UsesSameLocalDayAndTime()
    {
        var current = Create(AnalyticsPeriodKind.CurrentMonth);
        var previous = AnalyticsPeriodFactory.CreatePreviousEquivalent(current, BusinessTimeZone);

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-6)), previous.LocalStart);
        Assert.Equal(new DateTimeOffset(2026, 7, 8, 15, 30, 0, TimeSpan.FromHours(-6)), previous.LocalEndExclusive);
    }

    [Fact]
    public void MultipleYears_GroupsByYearAndClampsCurrentYear()
    {
        var request = new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.MultipleYears,
            StartYear: 2024,
            EndYear: 2026);

        var period = AnalyticsPeriodFactory.Create(request, Now, BusinessTimeZone);

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(-6)), period.LocalStart);
        Assert.Equal(Now, period.LocalEndExclusive);
        Assert.Equal(AnalyticsGrouping.Year, period.Grouping);
        Assert.True(period.IsIncomplete);
    }

    [Fact]
    public void AllHistory_StartsAtEpochAndEndsAtExportMoment()
    {
        var period = Create(AnalyticsPeriodKind.AllHistory);

        Assert.Equal(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.FromHours(-6)), period.LocalStart);
        Assert.Equal(Now, period.LocalEndExclusive);
        Assert.Equal("Todo el historial", period.DisplayName);
        Assert.Equal(AnalyticsGrouping.Year, period.Grouping);
    }

    [Theory]
    [InlineData(0, 0, MetricComparisonState.NoChange, 0.0)]
    [InlineData(100, 0, MetricComparisonState.NewMovement, null)]
    [InlineData(150, 100, MetricComparisonState.Increase, 50.0)]
    [InlineData(50, 100, MetricComparisonState.Decrease, -50.0)]
    [InlineData(100, 100, MetricComparisonState.NoChange, 0.0)]
    public void MetricComparison_HandlesZeroAndPercentageRules(
        long current,
        long previous,
        MetricComparisonState expectedState,
        double? expectedPercentage)
    {
        var result = MetricComparisonCalculator.Calculate(current, previous);

        Assert.Equal(expectedState, result.State);
        Assert.Equal(expectedPercentage is null ? null : (decimal?)expectedPercentage.Value, result.Percentage);
    }

    private static AnalyticsPeriodDefinition Create(AnalyticsPeriodKind kind) =>
        AnalyticsPeriodFactory.Create(new AnalyticsPeriodRequest(kind), Now, BusinessTimeZone);
}
