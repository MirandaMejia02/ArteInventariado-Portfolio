using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CreditAlertQueryService : ICreditAlertQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public CreditAlertQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
    }

    internal CreditAlertQueryService(
        AppDbContext context,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
    }

    public async Task<CreditAlertSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.ALERTAS_CREDITO_VER, cancellationToken);
        return await ReadSettingsAsync(lease.Context, cancellationToken);
    }

    public async Task UpdateSettingsAsync(
        UpdateCreditAlertSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;

        EnsureAdminSession();
        ValidateSettings(request);

        var values = new Dictionary<string, string>
        {
            [CreditConfigurationKeys.AlertUpcomingDueDays] = request.UpcomingDueDays.ToString(),
            [CreditConfigurationKeys.AlertNoPaymentDays] = request.NoPaymentDays.ToString(),
            [CreditConfigurationKeys.AlertNearLimitPercent] = request.NearLimitPercent.ToString("0.####"),
            [CreditConfigurationKeys.AlertEnableDueToday] = request.EnableDueToday.ToString(),
            [CreditConfigurationKeys.AlertEnableUpcomingDue] = request.EnableUpcomingDue.ToString(),
            [CreditConfigurationKeys.AlertEnableOverdue] = request.EnableOverdue.ToString(),
            [CreditConfigurationKeys.AlertEnableMoroso] = request.EnableMoroso.ToString(),
            [CreditConfigurationKeys.AlertEnableNoRecentPayment] = request.EnableNoRecentPayment.ToString(),
            [CreditConfigurationKeys.AlertEnableNearLimit] = request.EnableNearLimit.ToString(),
            [CreditConfigurationKeys.AlertEnableLimitReached] = request.EnableLimitReached.ToString()
        };

        var configs = await context.ConfiguracionSistema
            .Where(c => values.Keys.Contains(c.Clave))
            .ToDictionaryAsync(c => c.Clave, cancellationToken);

        foreach (var definition in CreditConfigurationKeys.Definitions.Where(d => values.ContainsKey(d.Key)))
        {
            if (configs.ContainsKey(definition.Key))
            {
                continue;
            }

            var config = new ConfiguracionSistema
            {
                Clave = definition.Key,
                Valor = definition.DefaultValue,
                Descripcion = definition.Description,
                FechaModificacion = DateTime.UtcNow
            };
            context.ConfiguracionSistema.Add(config);
            configs[definition.Key] = config;
        }

        var changes = new List<string>();
        foreach (var (key, value) in values)
        {
            var config = configs[key];
            if (config.Valor == value)
            {
                continue;
            }

            changes.Add($"{key}: {config.Valor} -> {value}");
            config.Valor = value;
            config.FechaModificacion = DateTime.UtcNow;
        }

        if (changes.Count == 0)
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                _currentSession.UsuarioActual?.UsuarioId,
                TipoBitacora.POLITICA_CREDITO_MODIFICADA,
                "configuracion_sistema",
                null,
                $"Configuracion de alertas de credito modificada. {string.Join("; ", changes)}"),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CreditAlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var alerts = await GetAlertsAsync(null, cancellationToken);
        return BuildSummary(alerts);
    }

    public async Task<IReadOnlyList<CreditAlertItem>> GetAlertsAsync(
        CreditAlertFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;

        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.ALERTAS_CREDITO_VER, cancellationToken);

        filter ??= new CreditAlertFilter();
        var settings = await ReadSettingsAsync(context, cancellationToken);
        var generalPolicy = await ReadGeneralCreditPolicyAsync(context, cancellationToken);
        var today = DateTime.Today;
        var morosoDays = Math.Max(1, generalPolicy.DaysPastDueForMoroso);

        var accounts = await context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Venta)
            .Include(c => c.Abonos)
            .Where(c => c.SaldoActualCentavos > 0)
            .Where(c => !filter.OnlyOpenAccounts || c.Estado == EstadoCuentaPorCobrar.ABIERTA)
            .Where(c => c.Estado == EstadoCuentaPorCobrar.ABIERTA)
            .ToListAsync(cancellationToken);

        var customerBalances = accounts
            .GroupBy(c => c.ClienteId)
            .ToDictionary(group => group.Key, group => group.Sum(c => c.SaldoActualCentavos));

        var alerts = new List<CreditAlertItem>();
        foreach (var account in accounts)
        {
            var customerBalance = customerBalances.GetValueOrDefault(account.ClienteId, account.SaldoActualCentavos);
            var customerLimit = account.Cliente.CreditoLimiteCentavos ?? generalPolicy.DefaultLimitCentavos;
            AddAccountAlerts(alerts, account, settings, morosoDays, today, customerBalance, customerLimit);
        }

        AddLimitAlerts(alerts, accounts, settings, generalPolicy);

        return alerts
            .Where(alert => MatchesFilter(alert, filter))
            .OrderByDescending(alert => SeverityRank(alert.Severity))
            .ThenBy(alert => alert.FechaVencimiento ?? DateTime.MaxValue)
            .ThenBy(alert => alert.Cliente)
            .ThenBy(alert => alert.TypeText)
            .ToList();
    }

    public async Task<CreditCustomerAlertIndicator> GetCustomerIndicatorAsync(
        int clienteId,
        CancellationToken cancellationToken = default)
    {
        var alerts = await GetAlertsAsync(new CreditAlertFilter(ClienteId: clienteId), cancellationToken);
        if (alerts.Count == 0)
        {
            return new CreditCustomerAlertIndicator(clienteId, 0, "Sin alertas", null);
        }

        var highest = alerts
            .OrderByDescending(alert => SeverityRank(alert.Severity))
            .First()
            .Severity;
        var priority = alerts
            .OrderByDescending(alert => SeverityRank(alert.Severity))
            .ThenBy(alert => alert.FechaVencimiento ?? DateTime.MaxValue)
            .First();

        var text = priority.Type switch
        {
            CreditAlertType.DueToday => alerts.Count == 1 ? "1 cuenta vence hoy" : $"{alerts.Count} alertas; una vence hoy",
            CreditAlertType.Moroso => "Cliente moroso",
            CreditAlertType.Overdue => "Cliente con cuenta vencida",
            CreditAlertType.LimitReached => "Limite alcanzado",
            CreditAlertType.NearLimit => "Cerca del limite",
            CreditAlertType.NoRecentPayment => "Sin abonos recientes",
            _ => $"{alerts.Count} alerta(s) de credito"
        };

        return new CreditCustomerAlertIndicator(clienteId, alerts.Count, text, highest);
    }

    private void AddAccountAlerts(
        ICollection<CreditAlertItem> alerts,
        CuentaPorCobrar account,
        CreditAlertSettings settings,
        int morosoDays,
        DateTime today,
        long customerBalance,
        long customerLimit)
    {
        var dueDate = ToLocalDate(account.FechaVencimiento);
        var daysToDue = (dueDate - today.Date).Days;
        var daysPastDue = daysToDue < 0 ? Math.Abs(daysToDue) : 0;
        var lastPayment = account.Abonos
            .OrderByDescending(a => a.FechaAbono)
            .Select(a => (DateTime?)a.FechaAbono)
            .FirstOrDefault();

        if (settings.EnableDueToday && daysToDue == 0)
        {
            alerts.Add(CreateAccountAlert(
                account,
                CreditAlertType.DueToday,
                CreditAlertSeverity.Alta,
                0,
                lastPayment,
                customerBalance,
                customerLimit,
                $"La cuenta {account.CodigoCuenta} vence hoy.",
                "Contactar al cliente y confirmar el pago."));
        }

        if (settings.EnableUpcomingDue && daysToDue > 0 && daysToDue <= settings.UpcomingDueDays)
        {
            alerts.Add(CreateAccountAlert(
                account,
                CreditAlertType.UpcomingDue,
                CreditAlertSeverity.Advertencia,
                daysToDue,
                lastPayment,
                customerBalance,
                customerLimit,
                $"La cuenta {account.CodigoCuenta} vence en {daysToDue} dia(s).",
                "Recordar al cliente la fecha de vencimiento."));
        }

        if (settings.EnableMoroso && daysPastDue >= morosoDays)
        {
            alerts.Add(CreateAccountAlert(
                account,
                CreditAlertType.Moroso,
                CreditAlertSeverity.Critica,
                daysPastDue,
                lastPayment,
                customerBalance,
                customerLimit,
                $"La cuenta {account.CodigoCuenta} tiene {daysPastDue} dia(s) de atraso.",
                "Priorizar gestion de cobro o revision administrativa."));
        }
        else if (settings.EnableOverdue && daysPastDue > 0)
        {
            alerts.Add(CreateAccountAlert(
                account,
                CreditAlertType.Overdue,
                CreditAlertSeverity.Alta,
                daysPastDue,
                lastPayment,
                customerBalance,
                customerLimit,
                $"La cuenta {account.CodigoCuenta} esta vencida.",
                "Contactar al cliente para programar abono."));
        }

        if (!settings.EnableNoRecentPayment)
        {
            return;
        }

        var referenceDate = ToLocalDate(lastPayment ?? account.FechaCreacion);
        var accountAgeDays = (today.Date - ToLocalDate(account.FechaCreacion)).Days;
        var daysWithoutPayment = (today.Date - referenceDate).Days;
        if (accountAgeDays >= settings.NoPaymentDays && daysWithoutPayment >= settings.NoPaymentDays)
        {
            alerts.Add(CreateAccountAlert(
                account,
                CreditAlertType.NoRecentPayment,
                CreditAlertSeverity.Advertencia,
                daysWithoutPayment,
                lastPayment,
                customerBalance,
                customerLimit,
                $"La cuenta {account.CodigoCuenta} no registra abonos recientes.",
                "Revisar seguimiento con el cliente."));
        }
    }

    private void AddLimitAlerts(
        ICollection<CreditAlertItem> alerts,
        IReadOnlyCollection<CuentaPorCobrar> accounts,
        CreditAlertSettings settings,
        CreditPolicySettings generalPolicy)
    {
        if (!settings.EnableNearLimit && !settings.EnableLimitReached)
        {
            return;
        }

        foreach (var group in accounts.GroupBy(c => c.ClienteId))
        {
            var sample = group.First();
            var limit = sample.Cliente.CreditoLimiteCentavos ?? generalPolicy.DefaultLimitCentavos;
            if (limit <= 0)
            {
                continue;
            }

            var totalBalance = group.Sum(c => c.SaldoActualCentavos);
            if (settings.EnableLimitReached && totalBalance >= limit)
            {
                alerts.Add(CreateCustomerLimitAlert(
                    sample.Cliente,
                    CreditAlertType.LimitReached,
                    CreditAlertSeverity.Critica,
                    totalBalance,
                    limit,
                    "El cliente alcanzo o supero su limite de credito.",
                    "Revisar politica crediticia antes de nuevos creditos."));
                continue;
            }

            var threshold = (long)Math.Ceiling(limit * settings.NearLimitPercent / 100m);
            if (settings.EnableNearLimit && totalBalance >= threshold)
            {
                alerts.Add(CreateCustomerLimitAlert(
                    sample.Cliente,
                    CreditAlertType.NearLimit,
                    CreditAlertSeverity.Advertencia,
                    totalBalance,
                    limit,
                    "El cliente esta cerca de alcanzar su limite de credito.",
                    "Validar cupo disponible antes de vender a credito."));
            }
        }
    }

    private static CreditAlertItem CreateAccountAlert(
        CuentaPorCobrar account,
        CreditAlertType type,
        CreditAlertSeverity severity,
        int days,
        DateTime? lastPayment,
        long customerBalance,
        long customerLimit,
        string message,
        string action)
    {
        return new CreditAlertItem(
            type,
            severity,
            account.ClienteId,
            account.Cliente.CodigoCliente,
            account.Cliente.NombreCompleto,
            account.CuentaPorCobrarId,
            account.CodigoCuenta,
            account.VentaId,
            account.Venta.NumeroVenta,
            account.SaldoActualCentavos,
            account.FechaVencimiento,
            days,
            lastPayment,
            customerLimit,
            customerBalance,
            message,
            action);
    }

    private static CreditAlertItem CreateCustomerLimitAlert(
        Cliente customer,
        CreditAlertType type,
        CreditAlertSeverity severity,
        long totalBalance,
        long limit,
        string message,
        string action)
    {
        return new CreditAlertItem(
            type,
            severity,
            customer.ClienteId,
            customer.CodigoCliente,
            customer.NombreCompleto,
            null,
            null,
            null,
            null,
            totalBalance,
            null,
            null,
            null,
            limit,
            totalBalance,
            message,
            action);
    }

    private static bool MatchesFilter(CreditAlertItem alert, CreditAlertFilter filter)
    {
        if (filter.Type.HasValue && alert.Type != filter.Type.Value)
        {
            return false;
        }

        if (filter.ClienteId.HasValue && alert.ClienteId != filter.ClienteId.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            var haystack = string.Join(" ", [
                alert.CodigoCliente,
                alert.Cliente,
                alert.CodigoCuenta ?? string.Empty,
                alert.NumeroVenta ?? string.Empty,
                alert.Message
            ]);
            if (!haystack.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (filter.FromDate.HasValue || filter.ToDate.HasValue)
        {
            if (!alert.FechaVencimiento.HasValue)
            {
                return false;
            }

            var dueDate = ToLocalDate(alert.FechaVencimiento.Value);
            if (filter.FromDate.HasValue && dueDate < filter.FromDate.Value.Date)
            {
                return false;
            }

            if (filter.ToDate.HasValue && dueDate > filter.ToDate.Value.Date)
            {
                return false;
            }
        }

        return true;
    }

    private static CreditAlertSummary BuildSummary(IReadOnlyList<CreditAlertItem> alerts)
    {
        return new CreditAlertSummary(
            alerts.Count,
            alerts.Count(a => a.Type == CreditAlertType.DueToday),
            alerts.Count(a => a.Type == CreditAlertType.UpcomingDue),
            alerts.Count(a => a.Type == CreditAlertType.Overdue),
            alerts.Count(a => a.Type == CreditAlertType.Moroso),
            alerts.Count(a => a.Type == CreditAlertType.NoRecentPayment),
            alerts.Count(a => a.Type == CreditAlertType.NearLimit),
            alerts.Count(a => a.Type == CreditAlertType.LimitReached));
    }

    private static async Task<CreditAlertSettings> ReadSettingsAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var values = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => CreditConfigurationKeys.Definitions.Select(d => d.Key).Contains(c.Clave))
            .ToDictionaryAsync(c => c.Clave, c => c.Valor, cancellationToken);

        return new CreditAlertSettings(
            ReadInt(values, CreditConfigurationKeys.AlertUpcomingDueDays, 3),
            ReadInt(values, CreditConfigurationKeys.AlertNoPaymentDays, 7),
            ReadDecimal(values, CreditConfigurationKeys.AlertNearLimitPercent, 80),
            ReadBool(values, CreditConfigurationKeys.AlertEnableDueToday, true),
            ReadBool(values, CreditConfigurationKeys.AlertEnableUpcomingDue, true),
            ReadBool(values, CreditConfigurationKeys.AlertEnableOverdue, true),
            ReadBool(values, CreditConfigurationKeys.AlertEnableMoroso, true),
            ReadBool(values, CreditConfigurationKeys.AlertEnableNoRecentPayment, true),
            ReadBool(values, CreditConfigurationKeys.AlertEnableNearLimit, true),
            ReadBool(values, CreditConfigurationKeys.AlertEnableLimitReached, true));
    }

    private static async Task<CreditPolicySettings> ReadGeneralCreditPolicyAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var values = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => CreditConfigurationKeys.Definitions.Select(d => d.Key).Contains(c.Clave))
            .ToDictionaryAsync(c => c.Clave, c => c.Valor, cancellationToken);

        return new CreditPolicySettings(
            ReadLong(values, CreditConfigurationKeys.DefaultLimitCentavos, 7500),
            ReadInt(values, CreditConfigurationKeys.DefaultMaxActiveAccounts, 3),
            ReadInt(values, CreditConfigurationKeys.DefaultTermDays, 7),
            ReadInt(values, CreditConfigurationKeys.EmployeeMaxTermDays, 7),
            ReadInt(values, CreditConfigurationKeys.AdminMaxTermDays, 90),
            ReadDecimal(values, CreditConfigurationKeys.DefaultInterestPercent, 0),
            ReadInt(values, CreditConfigurationKeys.DaysPastDueForMoroso, 7),
            ReadBool(values, CreditConfigurationKeys.AllowCreditWithOutstandingBalance, true));
    }

    private void EnsureAdminSession()
    {
        if (!_currentSession.EsAdmin || _currentSession.UsuarioActual is null)
        {
            throw new InvalidOperationException("Solo ADMIN puede modificar alertas de credito.");
        }
    }

    private static void ValidateSettings(UpdateCreditAlertSettingsRequest request)
    {
        if (request.UpcomingDueDays < 0 || request.UpcomingDueDays > 365)
        {
            throw new ArgumentException("Los dias de vencimiento proximo deben estar entre 0 y 365.", nameof(request));
        }

        if (request.NoPaymentDays <= 0 || request.NoPaymentDays > 365)
        {
            throw new ArgumentException("Los dias sin abono deben estar entre 1 y 365.", nameof(request));
        }

        if (request.NearLimitPercent <= 0 || request.NearLimitPercent > 100)
        {
            throw new ArgumentException("El porcentaje de proximidad al limite debe estar entre 1 y 100.", nameof(request));
        }
    }

    private static int SeverityRank(CreditAlertSeverity severity)
    {
        return severity switch
        {
            CreditAlertSeverity.Critica => 4,
            CreditAlertSeverity.Alta => 3,
            CreditAlertSeverity.Advertencia => 2,
            CreditAlertSeverity.Informativa => 1,
            _ => 0
        };
    }

    private static DateTime ToLocalDate(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => value.Date,
            DateTimeKind.Utc => value.ToLocalTime().Date,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().Date
        };
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
    {
        return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static long ReadLong(IReadOnlyDictionary<string, string> values, string key, long fallback)
    {
        return values.TryGetValue(key, out var value) && long.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, string> values, string key, decimal fallback)
    {
        return values.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
    {
        return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }
}
