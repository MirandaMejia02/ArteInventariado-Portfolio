using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ArteInventariado.Application.Services;

namespace ArteInventariado.Infrastructure.Services;

public sealed class EmployeeActivityQueryService(IDbContextFactory<AppDbContext> factory, ICurrentSession session, TimeProvider timeProvider) : IEmployeeActivityQueryService
{
    public async Task<IReadOnlyList<EmployeeActivitySummary>> GetEmployeesAsync(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default)
    {
        _ = timeProvider.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await EmployeeModuleAccessGuard.EnsureAsync(db, session, cancellationToken);
        var users = await db.Usuarios.AsNoTracking().Where(u => u.Rol == RolUsuario.EMPLEADO).ToListAsync(cancellationToken);
        var events = db.EmployeeLoginEvents.AsNoTracking();
        if (fromUtc.HasValue) events = events.Where(e => e.LoggedInAtUtc >= fromUtc.Value.UtcDateTime);
        if (toUtc.HasValue) events = events.Where(e => e.LoggedInAtUtc < toUtc.Value.UtcDateTime);
        var timeZone = await ResolveTimeZoneAsync(db, cancellationToken);
        var rowsRaw = await events.GroupBy(e => e.UserId).Select(g => new { UserId = g.Key, Last = g.Max(e => e.LoggedInAtUtc), Dates = g.Select(e => e.LoggedInAtUtc) }).ToListAsync(cancellationToken);
        var rows = rowsRaw.Select(g => new { g.UserId, g.Last, Days = g.Dates.Select(d => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d, DateTimeKind.Utc), timeZone).Date).Distinct().Count() }).ToList();
        return users.Select(u => { var r = rows.FirstOrDefault(x => x.UserId == u.UsuarioId); return new EmployeeActivitySummary(u.UsuarioId, u.NombreUsuario, u.NombreCompleto, u.Rol, u.Activo, u.FechaCreacion, r?.Last, r?.Days ?? 0); }).ToList();
    }

    public async Task<IReadOnlyList<EmployeeLoginActivity>> GetLoginHistoryAsync(int userId, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await EmployeeModuleAccessGuard.EnsureAsync(db, session, cancellationToken);
        var query = db.EmployeeLoginEvents.AsNoTracking().Where(e => e.UserId == userId);
        if (fromUtc.HasValue) query = query.Where(e => e.LoggedInAtUtc >= fromUtc.Value.UtcDateTime);
        if (toUtc.HasValue) query = query.Where(e => e.LoggedInAtUtc < toUtc.Value.UtcDateTime);
        return await query.OrderByDescending(e => e.LoggedInAtUtc).Select(e => new EmployeeLoginActivity(e.EmployeeLoginEventId, e.UserId, e.LoggedInAtUtc, e.InstallationId, e.StationMode)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeActivityDay>> GetActivityCalendarAsync(
        int userId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await EmployeeModuleAccessGuard.EnsureAsync(db, session, cancellationToken);

        var timeZone = await ResolveTimeZoneAsync(db, cancellationToken);
        var localStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddMonths(1);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone);

        var timestamps = await db.EmployeeLoginEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.LoggedInAtUtc >= fromUtc && e.LoggedInAtUtc < toUtc)
            .Select(e => e.LoggedInAtUtc)
            .ToListAsync(cancellationToken);

        return timestamps
            .Select(timestamp =>
                DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
                        timeZone)))
            .GroupBy(date => date)
            .OrderBy(group => group.Key)
            .Select(group => new EmployeeActivityDay(group.Key, group.Count()))
            .ToList();
    }

    private static async Task<TimeZoneInfo> ResolveTimeZoneAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var id = await db.ConfiguracionSistema.AsNoTracking().Where(x => x.Clave == DashboardQueryService.BusinessTimeZoneKey).Select(x => x.Valor).FirstOrDefaultAsync(cancellationToken) ?? DashboardQueryService.DefaultBusinessTimeZoneId;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { return TimeZoneInfo.Local; }
    }
}
