using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Services;

public static class AnalyticsPeriodFactory
{
    public static AnalyticsPeriodDefinition Create(
        AnalyticsPeriodRequest request,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timeZone);

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);

        return request.Kind switch
        {
            AnalyticsPeriodKind.Today => Build(
                request.Kind,
                AtStartOfDay(today, timeZone),
                localNow,
                AnalyticsGrouping.Hour,
                "Hoy",
                true),
            AnalyticsPeriodKind.Yesterday => Build(
                request.Kind,
                AtStartOfDay(today.AddDays(-1), timeZone),
                AtStartOfDay(today, timeZone),
                AnalyticsGrouping.Hour,
                "Ayer",
                false),
            AnalyticsPeriodKind.CurrentWeek => BuildCurrentWeek(localNow, today, timeZone),
            AnalyticsPeriodKind.CurrentMonth => Build(
                request.Kind,
                AtStartOfDay(new DateOnly(today.Year, today.Month, 1), timeZone),
                localNow,
                AnalyticsGrouping.Day,
                "Mes actual",
                true),
            AnalyticsPeriodKind.LastThreeMonths => Build(
                request.Kind,
                ShiftLocal(localNow, timeZone, months: -3),
                localNow,
                AnalyticsGrouping.Month,
                "Ultimos 3 meses",
                true),
            AnalyticsPeriodKind.CurrentQuarter => BuildCurrentQuarter(localNow, today, timeZone),
            AnalyticsPeriodKind.PreviousQuarter => BuildPreviousQuarter(today, timeZone),
            AnalyticsPeriodKind.LastSixMonths => Build(
                request.Kind,
                ShiftLocal(localNow, timeZone, months: -6),
                localNow,
                AnalyticsGrouping.Month,
                "Ultimos 6 meses",
                true),
            AnalyticsPeriodKind.CurrentSemester => BuildCurrentSemester(localNow, today, timeZone),
            AnalyticsPeriodKind.PreviousSemester => BuildPreviousSemester(today, timeZone),
            AnalyticsPeriodKind.CurrentYear => Build(
                request.Kind,
                AtStartOfDay(new DateOnly(today.Year, 1, 1), timeZone),
                localNow,
                AnalyticsGrouping.Month,
                "Ano actual",
                true),
            AnalyticsPeriodKind.PreviousYear => Build(
                request.Kind,
                AtStartOfDay(new DateOnly(today.Year - 1, 1, 1), timeZone),
                AtStartOfDay(new DateOnly(today.Year, 1, 1), timeZone),
                AnalyticsGrouping.Month,
                "Ano anterior",
                false),
            AnalyticsPeriodKind.MultipleYears => BuildMultipleYears(request, localNow, timeZone),
            AnalyticsPeriodKind.CustomRange => BuildCustomRange(request, localNow, timeZone),
            AnalyticsPeriodKind.AllHistory => Build(
                request.Kind,
                AtStartOfDay(new DateOnly(1970, 1, 1), timeZone),
                localNow,
                AnalyticsGrouping.Year,
                "Todo el historial",
                true),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Periodo no soportado.")
        };
    }

    public static AnalyticsPeriodDefinition CreatePreviousEquivalent(
        AnalyticsPeriodDefinition current,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(timeZone);

        var (start, end) = current.Kind switch
        {
            AnalyticsPeriodKind.Today or AnalyticsPeriodKind.Yesterday =>
                (ShiftLocal(current.LocalStart, timeZone, days: -1), ShiftLocal(current.LocalEndExclusive, timeZone, days: -1)),
            AnalyticsPeriodKind.CurrentWeek =>
                (ShiftLocal(current.LocalStart, timeZone, days: -7), ShiftLocal(current.LocalEndExclusive, timeZone, days: -7)),
            AnalyticsPeriodKind.CurrentMonth =>
                (ShiftLocal(current.LocalStart, timeZone, months: -1), ShiftLocal(current.LocalEndExclusive, timeZone, months: -1)),
            AnalyticsPeriodKind.CurrentQuarter or AnalyticsPeriodKind.PreviousQuarter =>
                (ShiftLocal(current.LocalStart, timeZone, months: -3), ShiftLocal(current.LocalEndExclusive, timeZone, months: -3)),
            AnalyticsPeriodKind.CurrentSemester or AnalyticsPeriodKind.PreviousSemester =>
                (ShiftLocal(current.LocalStart, timeZone, months: -6), ShiftLocal(current.LocalEndExclusive, timeZone, months: -6)),
            AnalyticsPeriodKind.CurrentYear or AnalyticsPeriodKind.PreviousYear =>
                (ShiftLocal(current.LocalStart, timeZone, years: -1), ShiftLocal(current.LocalEndExclusive, timeZone, years: -1)),
            _ => PreviousByDuration(current, timeZone)
        };

        return Build(
            current.Kind,
            start,
            end,
            current.Grouping,
            "Periodo anterior equivalente",
            current.IsIncomplete);
    }

    public static AnalyticsPeriodDefinition CreateSamePeriodPreviousYear(
        AnalyticsPeriodDefinition current,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(timeZone);

        return Build(
            current.Kind,
            ShiftLocal(current.LocalStart, timeZone, years: -1),
            ShiftLocal(current.LocalEndExclusive, timeZone, years: -1),
            current.Grouping,
            "Mismo periodo del ano anterior",
            current.IsIncomplete);
    }

    private static AnalyticsPeriodDefinition BuildCurrentWeek(
        DateTimeOffset localNow,
        DateOnly today,
        TimeZoneInfo timeZone)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        return Build(
            AnalyticsPeriodKind.CurrentWeek,
            AtStartOfDay(today.AddDays(-daysSinceMonday), timeZone),
            localNow,
            AnalyticsGrouping.Day,
            "Semana actual",
            true);
    }

    private static AnalyticsPeriodDefinition BuildCurrentQuarter(
        DateTimeOffset localNow,
        DateOnly today,
        TimeZoneInfo timeZone)
    {
        var firstMonth = ((today.Month - 1) / 3 * 3) + 1;
        return Build(
            AnalyticsPeriodKind.CurrentQuarter,
            AtStartOfDay(new DateOnly(today.Year, firstMonth, 1), timeZone),
            localNow,
            AnalyticsGrouping.Month,
            "Trimestre actual",
            true);
    }

    private static AnalyticsPeriodDefinition BuildPreviousQuarter(DateOnly today, TimeZoneInfo timeZone)
    {
        var firstMonth = ((today.Month - 1) / 3 * 3) + 1;
        var currentStart = AtStartOfDay(new DateOnly(today.Year, firstMonth, 1), timeZone);
        return Build(
            AnalyticsPeriodKind.PreviousQuarter,
            ShiftLocal(currentStart, timeZone, months: -3),
            currentStart,
            AnalyticsGrouping.Month,
            "Trimestre anterior",
            false);
    }

    private static AnalyticsPeriodDefinition BuildCurrentSemester(
        DateTimeOffset localNow,
        DateOnly today,
        TimeZoneInfo timeZone)
    {
        var firstMonth = today.Month <= 6 ? 1 : 7;
        return Build(
            AnalyticsPeriodKind.CurrentSemester,
            AtStartOfDay(new DateOnly(today.Year, firstMonth, 1), timeZone),
            localNow,
            AnalyticsGrouping.Month,
            "Semestre actual",
            true);
    }

    private static AnalyticsPeriodDefinition BuildPreviousSemester(DateOnly today, TimeZoneInfo timeZone)
    {
        var firstMonth = today.Month <= 6 ? 1 : 7;
        var currentStart = AtStartOfDay(new DateOnly(today.Year, firstMonth, 1), timeZone);
        return Build(
            AnalyticsPeriodKind.PreviousSemester,
            ShiftLocal(currentStart, timeZone, months: -6),
            currentStart,
            AnalyticsGrouping.Month,
            "Semestre anterior",
            false);
    }

    private static AnalyticsPeriodDefinition BuildMultipleYears(
        AnalyticsPeriodRequest request,
        DateTimeOffset localNow,
        TimeZoneInfo timeZone)
    {
        if (request.StartYear is null || request.EndYear is null || request.StartYear > request.EndYear)
        {
            throw new ArgumentException("Varios anos requiere un ano inicial y final validos.", nameof(request));
        }

        var start = AtStartOfDay(new DateOnly(request.StartYear.Value, 1, 1), timeZone);
        var requestedEnd = AtStartOfDay(new DateOnly(request.EndYear.Value + 1, 1, 1), timeZone);
        var end = requestedEnd > localNow ? localNow : requestedEnd;
        EnsureRange(start, end, request);

        return Build(
            request.Kind,
            start,
            end,
            AnalyticsGrouping.Year,
            $"{request.StartYear}-{request.EndYear}",
            requestedEnd > localNow);
    }

    private static AnalyticsPeriodDefinition BuildCustomRange(
        AnalyticsPeriodRequest request,
        DateTimeOffset localNow,
        TimeZoneInfo timeZone)
    {
        if (request.StartDate is null || request.EndDate is null || request.StartDate > request.EndDate)
        {
            throw new ArgumentException("El rango personalizado requiere fechas validas.", nameof(request));
        }

        var start = AtStartOfDay(request.StartDate.Value, timeZone);
        var requestedEnd = AtStartOfDay(request.EndDate.Value.AddDays(1), timeZone);
        var end = requestedEnd > localNow ? localNow : requestedEnd;
        EnsureRange(start, end, request);

        var duration = end - start;
        var grouping = duration.TotalDays switch
        {
            <= 2 => AnalyticsGrouping.Hour,
            <= 62 => AnalyticsGrouping.Day,
            <= 730 => AnalyticsGrouping.Month,
            _ => AnalyticsGrouping.Year
        };

        return Build(
            request.Kind,
            start,
            end,
            grouping,
            $"{request.StartDate:dd/MM/yyyy} - {request.EndDate:dd/MM/yyyy}",
            requestedEnd > localNow);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) PreviousByDuration(
        AnalyticsPeriodDefinition current,
        TimeZoneInfo timeZone)
    {
        var duration = current.UtcEndExclusive - current.UtcStart;
        var previousEndUtc = current.UtcStart;
        var previousStartUtc = previousEndUtc - duration;
        return (
            TimeZoneInfo.ConvertTime(new DateTimeOffset(previousStartUtc, TimeSpan.Zero), timeZone),
            TimeZoneInfo.ConvertTime(new DateTimeOffset(previousEndUtc, TimeSpan.Zero), timeZone));
    }

    private static AnalyticsPeriodDefinition Build(
        AnalyticsPeriodKind kind,
        DateTimeOffset localStart,
        DateTimeOffset localEndExclusive,
        AnalyticsGrouping grouping,
        string displayName,
        bool isIncomplete)
    {
        if (localEndExclusive <= localStart)
        {
            throw new ArgumentException("El final del periodo debe ser posterior al inicio.");
        }

        return new AnalyticsPeriodDefinition(
            kind,
            localStart,
            localEndExclusive,
            localStart.UtcDateTime,
            localEndExclusive.UtcDateTime,
            grouping,
            displayName,
            isIncomplete);
    }

    private static DateTimeOffset AtStartOfDay(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static DateTimeOffset ShiftLocal(
        DateTimeOffset value,
        TimeZoneInfo timeZone,
        int years = 0,
        int months = 0,
        int days = 0)
    {
        var local = DateTime.SpecifyKind(value.DateTime, DateTimeKind.Unspecified)
            .AddYears(years)
            .AddMonths(months)
            .AddDays(days);

        if (timeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static void EnsureRange(
        DateTimeOffset start,
        DateTimeOffset end,
        AnalyticsPeriodRequest request)
    {
        if (end <= start)
        {
            throw new ArgumentException("El periodo solicitado no contiene fechas anteriores al momento actual.", nameof(request));
        }
    }
}
