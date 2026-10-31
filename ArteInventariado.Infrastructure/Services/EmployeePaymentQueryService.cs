using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class EmployeePaymentQueryService : IEmployeePaymentQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _session;

    [ActivatorUtilitiesConstructor]
    public EmployeePaymentQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession session)
    {
        _contextFactory = contextFactory;
        _session = session;
    }

    internal EmployeePaymentQueryService(AppDbContext context, ICurrentSession session)
    {
        _legacyContext = context;
        _session = session;
    }

    public async Task<EmployeePaymentSummary> GetSummaryAsync(
        int userId,
        EmployeePaymentFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        filter = (filter ?? new EmployeePaymentFilter()) with { UserId = userId };
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var db = lease.Context;
        await EmployeeModuleAccessGuard.EnsurePaymentAsync(
            db, _session, PermisoEmpleado.EMPLEADOS_PAGOS_VER, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(db, cancellationToken);
        var query = ApplyFilter(db.EmployeePayments.AsNoTracking(), filter, timeZone);

        var totalRecords = await query.CountAsync(cancellationToken);
        var cancelled = await query
            .Where(payment => payment.Status == EmployeePaymentStatus.Cancelled)
            .CountAsync(cancellationToken);
        var active = query.Where(payment => payment.Status == EmployeePaymentStatus.Active);
        var paymentCount = await active.CountAsync(cancellationToken);
        var totalPaid = await active
            .Select(payment => (long?)payment.AmountCentavos)
            .SumAsync(cancellationToken) ?? 0;
        var last = await active
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .Select(payment => (DateTime?)payment.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new EmployeePaymentSummary(
            userId,
            totalPaid,
            paymentCount,
            last,
            cancelled,
            totalRecords);
    }

    public async Task<IReadOnlyList<EmployeePaymentHistoryRow>> GetHistoryAsync(
        EmployeePaymentFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new EmployeePaymentFilter();
        ValidateFilter(filter);
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var db = lease.Context;
        await EmployeeModuleAccessGuard.EnsurePaymentAsync(
            db, _session, PermisoEmpleado.EMPLEADOS_PAGOS_VER, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(db, cancellationToken);
        var query = ApplyFilter(db.EmployeePayments.AsNoTracking(), filter, timeZone);

        return await query
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .ThenByDescending(payment => payment.EmployeePaymentId)
            .Select(payment => new EmployeePaymentHistoryRow(
                payment.EmployeePaymentId,
                payment.UserId,
                payment.User.NombreCompleto,
                payment.User.NombreUsuario,
                payment.AmountCentavos,
                payment.PaymentType,
                payment.PeriodStart,
                payment.PeriodEnd,
                payment.DaysConsidered,
                payment.Note,
                payment.RegisteredByUserId,
                payment.RegisteredBy.NombreUsuario,
                payment.CreatedAtUtc,
                payment.Status,
                payment.CancelledByUserId,
                payment.CancelledBy == null ? null : payment.CancelledBy.NombreUsuario,
                payment.CancelledAtUtc,
                payment.CancellationReason,
                payment.MetodoPago,
                payment.OrigenFondos,
                payment.ResponsableOrigen,
                payment.Referencia,
                payment.CajaId,
                payment.OperacionId))
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EmployeePaymentHistoryRow>> GetEmployeeHistoryAsync(
        int userId,
        EmployeePaymentFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (filter?.UserId is { } filterUserId && filterUserId != userId)
        {
            throw new ArgumentException("El filtro de usuario no coincide con el empleado solicitado.", nameof(filter));
        }

        return GetHistoryAsync((filter ?? new EmployeePaymentFilter()) with { UserId = userId }, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeePaymentTrendPoint>> GetPaymentTrendAsync(
        int userId,
        EmployeePaymentFilter? filter,
        EmployeePaymentGrouping grouping,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (!Enum.IsDefined(grouping))
        {
            throw new ArgumentOutOfRangeException(nameof(grouping));
        }

        filter = (filter ?? new EmployeePaymentFilter()) with
        {
            UserId = userId,
            Status = EmployeePaymentStatus.Active
        };
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var db = lease.Context;
        await EmployeeModuleAccessGuard.EnsurePaymentAsync(
            db, _session, PermisoEmpleado.EMPLEADOS_PAGOS_VER, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(db, cancellationToken);
        var facts = await ApplyFilter(db.EmployeePayments.AsNoTracking(), filter, timeZone)
            .Select(payment => new PaymentFact(payment.CreatedAtUtc, payment.AmountCentavos))
            .ToListAsync(cancellationToken);

        return facts
            .GroupBy(fact => Bucket(ToLocalDate(fact.CreatedAtUtc, timeZone), grouping))
            .OrderBy(group => group.Key)
            .Select(group => new EmployeePaymentTrendPoint(
                group.Key,
                group.Count(),
                group.Sum(fact => fact.AmountCentavos)))
            .ToList();
    }

    private static IQueryable<EmployeePayment> ApplyFilter(
        IQueryable<EmployeePayment> query,
        EmployeePaymentFilter filter,
        TimeZoneInfo timeZone)
    {
        ValidateFilter(filter);
        if (filter.UserId.HasValue)
        {
            query = query.Where(payment => payment.UserId == filter.UserId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            var fromUtc = ToUtc(filter.FromDate.Value, timeZone);
            query = query.Where(payment => payment.CreatedAtUtc >= fromUtc);
        }

        if (filter.ToDateExclusive.HasValue)
        {
            var toUtc = ToUtc(filter.ToDateExclusive.Value, timeZone);
            query = query.Where(payment => payment.CreatedAtUtc < toUtc);
        }

        if (filter.PaymentType.HasValue)
        {
            query = query.Where(payment => payment.PaymentType == filter.PaymentType.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(payment => payment.Status == filter.Status.Value);
        }

        return query;
    }

    private static void ValidateFilter(EmployeePaymentFilter filter)
    {
        if (filter.UserId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(filter), "El usuario del filtro no es valido.");
        }

        if (filter.FromDate.HasValue && filter.ToDateExclusive.HasValue &&
            filter.ToDateExclusive.Value <= filter.FromDate.Value)
        {
            throw new ArgumentException("El final del periodo debe ser posterior al inicio.", nameof(filter));
        }

        if (filter.PaymentType.HasValue && !Enum.IsDefined(filter.PaymentType.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }

        if (filter.Status.HasValue && !Enum.IsDefined(filter.Status.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }
    }

    private static DateTime ToUtc(DateOnly localDate, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            timeZone);

    private static DateOnly ToLocalDate(DateTime utc, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            timeZone));

    private static DateOnly Bucket(DateOnly localDate, EmployeePaymentGrouping grouping) => grouping switch
    {
        EmployeePaymentGrouping.Day => localDate,
        EmployeePaymentGrouping.Week => StartOfWeek(localDate),
        EmployeePaymentGrouping.Biweekly => new DateOnly(localDate.Year, localDate.Month, localDate.Day <= 15 ? 1 : 16),
        EmployeePaymentGrouping.Month => new DateOnly(localDate.Year, localDate.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(grouping))
    };

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static async Task<TimeZoneInfo> ResolveBusinessTimeZoneAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var configuredId = await db.ConfiguracionSistema
            .AsNoTracking()
            .Where(config => config.Clave == DashboardQueryService.BusinessTimeZoneKey)
            .Select(config => config.Valor)
            .FirstOrDefaultAsync(cancellationToken);
        var id = string.IsNullOrWhiteSpace(configuredId)
            ? DashboardQueryService.DefaultBusinessTimeZoneId
            : configuredId.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
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

    private sealed record PaymentFact(DateTime CreatedAtUtc, long AmountCentavos);
}
