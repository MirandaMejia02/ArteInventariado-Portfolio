using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

public sealed class EmployeeSalesQueryService(
    IDbContextFactory<AppDbContext> factory,
    ICurrentSession session) : IEmployeeSalesQueryService
{
    public async Task<EmployeeSalesReport> GetEmployeeSalesAsync(
        int userId,
        EmployeeSalesPeriod? period = null,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await EmployeeModuleAccessGuard.EnsureAsync(db, session, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(db, cancellationToken);
        var facts = await LoadFactsAsync(db, userId, period, timeZone, cancellationToken);
        return new EmployeeSalesReport(
            userId,
            period,
            BuildSummary(userId, facts),
            BuildTrend(facts, EmployeeSalesGrouping.Day, timeZone));
    }

    public async Task<IReadOnlyList<EmployeeSalesTrendPoint>> GetEmployeeSalesTrendAsync(
        int userId,
        EmployeeSalesPeriod? period,
        EmployeeSalesGrouping grouping,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        if (!Enum.IsDefined(grouping))
        {
            throw new ArgumentOutOfRangeException(nameof(grouping));
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await EmployeeModuleAccessGuard.EnsureAsync(db, session, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(db, cancellationToken);
        var facts = await LoadFactsAsync(db, userId, period, timeZone, cancellationToken);
        return BuildTrend(facts, grouping, timeZone);
    }

    public async Task<IReadOnlyList<EmployeeSalesRankingRow>> GetSalesRankingAsync(
        EmployeeSalesPeriod? period = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await EmployeeModuleAccessGuard.EnsureAsync(db, session, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(db, cancellationToken);
        var (fromUtc, toUtc) = GetUtcBounds(period, timeZone);

        var query = db.Ventas
            .AsNoTracking()
            .Where(v => v.Estado == EstadoVenta.COMPLETADA);
        if (fromUtc.HasValue)
        {
            query = query.Where(v => v.FechaVenta >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(v => v.FechaVenta < toUtc.Value);
        }

        var grouped = await query
            .GroupBy(v => new
            {
                v.UsuarioId,
                FullName = v.Usuario.NombreCompleto,
                UserName = v.Usuario.NombreUsuario,
                v.Usuario.Activo
            })
            .Select(g => new
            {
                UserId = g.Key.UsuarioId,
                FullName = g.Key.FullName,
                UserName = g.Key.UserName,
                Active = g.Key.Activo,
                SaleCount = g.Count(),
                TotalSoldCentavos = g.Sum(v => v.TotalCentavos)
            })
            .OrderByDescending(g => g.TotalSoldCentavos)
            .ThenBy(g => g.FullName)
            .ThenBy(g => g.UserName)
            .ToListAsync(cancellationToken);

        return grouped
            .Select((row, index) => new EmployeeSalesRankingRow(
                index + 1,
                row.UserId,
                row.FullName,
                row.UserName,
                row.Active,
                row.SaleCount,
                row.TotalSoldCentavos,
                Average(row.TotalSoldCentavos, row.SaleCount)))
            .ToList();
    }

    private static async Task<IReadOnlyList<SaleFact>> LoadFactsAsync(
        AppDbContext db,
        int userId,
        EmployeeSalesPeriod? period,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = GetUtcBounds(period, timeZone);
        var query = db.Ventas
            .AsNoTracking()
            .Where(v => v.UsuarioId == userId && v.Estado == EstadoVenta.COMPLETADA);
        if (fromUtc.HasValue)
        {
            query = query.Where(v => v.FechaVenta >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(v => v.FechaVenta < toUtc.Value);
        }

        return await query
            .Select(v => new SaleFact(v.FechaVenta, v.TotalCentavos))
            .ToListAsync(cancellationToken);
    }

    private static EmployeeSalesSummary BuildSummary(int userId, IReadOnlyList<SaleFact> facts)
    {
        var total = facts.Sum(f => f.TotalSoldCentavos);
        return new EmployeeSalesSummary(
            userId,
            facts.Count,
            total,
            Average(total, facts.Count),
            facts.Count == 0 ? null : facts.Max(f => f.SoldAtUtc));
    }

    private static IReadOnlyList<EmployeeSalesTrendPoint> BuildTrend(
        IReadOnlyList<SaleFact> facts,
        EmployeeSalesGrouping grouping,
        TimeZoneInfo timeZone)
    {
        return facts
            .GroupBy(f => Bucket(ToLocalDate(f.SoldAtUtc, timeZone), grouping))
            .OrderBy(g => g.Key)
            .Select(g => new EmployeeSalesTrendPoint(
                g.Key,
                g.Count(),
                g.Sum(f => f.TotalSoldCentavos)))
            .ToList();
    }

    private static DateOnly ToLocalDate(DateTime utc, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc),
                timeZone));

    private static DateOnly Bucket(DateOnly localDate, EmployeeSalesGrouping grouping) => grouping switch
    {
        EmployeeSalesGrouping.Day => localDate,
        EmployeeSalesGrouping.Week => StartOfWeek(localDate),
        EmployeeSalesGrouping.Month => new DateOnly(localDate.Year, localDate.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(grouping))
    };

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static long Average(long total, int count) => count == 0
        ? 0
        : decimal.ToInt64(decimal.Round(total / (decimal)count, 0, MidpointRounding.AwayFromZero));

    private static (DateTime? FromUtc, DateTime? ToUtc) GetUtcBounds(
        EmployeeSalesPeriod? period,
        TimeZoneInfo timeZone)
    {
        if (period is null)
        {
            return (null, null);
        }

        if (period.EndDateExclusive <= period.StartDate)
        {
            throw new ArgumentException("El final del periodo debe ser posterior al inicio.", nameof(period));
        }

        return (
            ToUtc(period.StartDate, timeZone),
            ToUtc(period.EndDateExclusive, timeZone));
    }

    private static DateTime ToUtc(DateOnly localDate, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            timeZone);

    private static async Task<TimeZoneInfo> ResolveBusinessTimeZoneAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var configuredId = await db.ConfiguracionSistema
            .AsNoTracking()
            .Where(x => x.Clave == DashboardQueryService.BusinessTimeZoneKey)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);
        var timeZoneId = string.IsNullOrWhiteSpace(configuredId)
            ? DashboardQueryService.DefaultBusinessTimeZoneId
            : configuredId.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static void EnsureUserId(int userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }
    }

    private sealed record SaleFact(DateTime SoldAtUtc, long TotalSoldCentavos);
}
