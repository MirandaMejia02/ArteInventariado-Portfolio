using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CreditService : ICreditService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public CreditService(
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

    internal CreditService(
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

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion de credito.");

    public async Task<CreditPolicySettings> GetGeneralPolicyAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();
        return await ReadGeneralPolicyAsync(cancellationToken);
    }

    public async Task UpdateGeneralPolicyAsync(
        UpdateCreditPolicySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();
        ValidateGeneralPolicy(request);
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await EnsureCreditSettingsAsync(cancellationToken);

        var values = new Dictionary<string, string>
        {
            [CreditConfigurationKeys.DefaultLimitCentavos] = request.DefaultLimitCentavos.ToString(),
            [CreditConfigurationKeys.DefaultMaxActiveAccounts] = request.DefaultMaxActiveAccounts.ToString(),
            [CreditConfigurationKeys.DefaultTermDays] = request.DefaultTermDays.ToString(),
            [CreditConfigurationKeys.EmployeeMaxTermDays] = request.EmployeeMaxTermDays.ToString(),
            [CreditConfigurationKeys.AdminMaxTermDays] = request.AdminMaxTermDays.ToString(),
            [CreditConfigurationKeys.DefaultInterestPercent] = request.DefaultInterestPercent.ToString("0.####"),
            [CreditConfigurationKeys.DaysPastDueForMoroso] = request.DaysPastDueForMoroso.ToString(),
            [CreditConfigurationKeys.AllowCreditWithOutstandingBalance] = request.AllowCreditWithOutstandingBalance.ToString()
        };

        var configs = await Context.ConfiguracionSistema
            .Where(c => values.Keys.Contains(c.Clave))
            .ToDictionaryAsync(c => c.Clave, cancellationToken);
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
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await Context.SaveChangesAsync(cancellationToken);
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                _currentSession.UsuarioActual?.UsuarioId,
                TipoBitacora.POLITICA_CREDITO_MODIFICADA,
                "configuracion_sistema",
                null,
                $"Politica general de credito modificada. {string.Join("; ", changes)}"),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CustomerCreditPolicy> GetCustomerPolicyAsync(
        int clienteId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER, cancellationToken);

        var customer = await Context.Clientes
            .AsNoTracking()
            .Include(c => c.CreditoUsuarioActualizacion)
            .FirstOrDefaultAsync(c => c.ClienteId == clienteId, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");

        return ToCustomerPolicy(customer);
    }

    public async Task UpdateCustomerPolicyAsync(
        UpdateCustomerCreditPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();
        ValidateCustomerPolicy(request);

        var customer = await Context.Clientes
            .FirstOrDefaultAsync(c => c.ClienteId == request.ClienteId, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");

        var before = ToPolicyText(customer);
        customer.CreditoHabilitado = request.CreditoHabilitado;
        customer.CreditoBloqueoMotivo = NormalizeOptional(request.CreditoBloqueoMotivo);
        customer.CreditoLimiteCentavos = request.LimiteCentavos;
        customer.CreditoMaximoCuentasActivas = request.MaximoCuentasActivas;
        customer.CreditoPlazoMaximoDias = request.PlazoMaximoDias;
        customer.CreditoPermitirSaldoVigente = request.PermitirSaldoVigente;
        customer.CreditoInteresPorcentaje = request.InteresPorcentaje;
        customer.CreditoUsuarioActualizacionId = _currentSession.UsuarioActual?.UsuarioId;
        customer.CreditoFechaActualizacion = DateTime.UtcNow;
        customer.FechaActualizacion = DateTime.UtcNow;

        var after = ToPolicyText(customer);
        if (before == after)
        {
            return;
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                _currentSession.UsuarioActual?.UsuarioId,
                TipoBitacora.POLITICA_CREDITO_MODIFICADA,
                "clientes",
                customer.ClienteId,
                $"Politica crediticia del cliente {customer.CodigoCliente} modificada.",
                before,
                after),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CustomerCreditSummary> GetCustomerSummaryAsync(
        int clienteId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        if (!await _permissionService.HasAnyPermissionAsync(
            [PermisoEmpleado.CUENTAS_COBRAR_VER, PermisoEmpleado.CREDITO_REGISTRAR],
            cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException("No tiene permiso para consultar la situacion crediticia del cliente.");
        }

        var customer = await Context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClienteId == clienteId, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");

        var policy = await BuildEffectivePolicyAsync(customer, cancellationToken);
        var accounts = await GetOpenAccountsForCustomerAsync(clienteId, cancellationToken);
        var situation = CalculateSituation(accounts, policy.DaysPastDueForMoroso, DateTime.UtcNow);
        var saldo = accounts.Sum(c => c.SaldoActualCentavos);
        var nextDue = accounts
            .Where(c => c.SaldoActualCentavos > 0)
            .OrderBy(c => c.FechaVencimiento)
            .Select(c => (DateTime?)c.FechaVencimiento)
            .FirstOrDefault();

        return new CustomerCreditSummary(
            customer.ClienteId,
            customer.CodigoCliente,
            customer.NombreCompleto,
            customer.Estado == EstadoCliente.ACTIVO,
            customer.CreditoHabilitado,
            customer.CreditoBloqueoMotivo,
            situation,
            saldo,
            policy.LimitCentavos,
            Math.Max(0, policy.LimitCentavos - saldo),
            accounts.Count,
            policy.MaxActiveAccounts,
            nextDue,
            policy);
    }

    public async Task<IReadOnlyList<AccountReceivableSummary>> GetAccountsAsync(
        AccountReceivableFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER, cancellationToken);
        var generalPolicy = await ReadGeneralPolicyAsync(cancellationToken);

        var query = Context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Venta)
            .AsQueryable();

        if (filter.ClienteId.HasValue)
        {
            query = query.Where(c => c.ClienteId == filter.ClienteId.Value);
        }

        if (filter.Estado.HasValue)
        {
            query = query.Where(c => c.Estado == filter.Estado.Value);
        }
        else if (!filter.IncludeClosed)
        {
            query = query.Where(c => c.Estado == EstadoCuentaPorCobrar.ABIERTA);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(c =>
                c.CodigoCuenta.Contains(search) ||
                c.Venta.NumeroVenta.Contains(search) ||
                c.Cliente.NombreCompleto.Contains(search) ||
                c.Cliente.CodigoCliente.Contains(search));
        }

        var rows = await query
            .OrderByDescending(c => c.FechaCreacion)
            .Take(1000)
            .ToListAsync(cancellationToken);

        return rows.Select(c => ToSummary(c, generalPolicy.DaysPastDueForMoroso)).ToList();
    }

    public async Task<AccountReceivableDetail?> GetAccountDetailAsync(
        int cuentaPorCobrarId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER, cancellationToken);
        var generalPolicy = await ReadGeneralPolicyAsync(cancellationToken);

        var account = await Context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Venta)
            .Include(c => c.UsuarioRegistro)
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.CuentaPorCobrarId == cuentaPorCobrarId, cancellationToken);

        if (account is null)
        {
            return null;
        }

        var summary = ToSummary(account, generalPolicy.DaysPastDueForMoroso);
        var lines = account.Detalles
            .OrderBy(d => d.CuentaPorCobrarDetalleId)
            .Select(d => new AccountReceivableLineDetail(
                d.CuentaPorCobrarDetalleId,
                d.DetalleVentaId,
                d.ProductoId,
                d.ProductoCodigoSnapshot,
                d.ProductoNombreSnapshot,
                d.CantidadFinanciada,
                d.PrecioUnitarioCentavos,
                d.DescuentoLineaCentavos,
                d.DescuentoGeneralProporcionalCentavos,
                d.SubtotalFinanciadoCentavos,
                d.CapitalPendienteCentavos))
            .ToList();

        return new AccountReceivableDetail(
            summary,
            account.UsuarioRegistro.NombreUsuario,
            account.CajaId,
            account.Observaciones,
            lines);
    }

    internal async Task<EffectiveCreditPolicy> BuildEffectivePolicyAsync(
        Cliente customer,
        CancellationToken cancellationToken)
    {
        return await BuildEffectivePolicyAsync(Context, customer, cancellationToken);
    }

    internal async Task<EffectiveCreditPolicy> BuildEffectivePolicyAsync(
        AppDbContext context,
        Cliente customer,
        CancellationToken cancellationToken)
    {
        await EnsureCreditSettingsAsync(context, cancellationToken);
        var general = await ReadGeneralPolicyAsync(context, cancellationToken);
        return new EffectiveCreditPolicy(
            customer.ClienteId,
            customer.CreditoLimiteCentavos ?? general.DefaultLimitCentavos,
            customer.CreditoMaximoCuentasActivas ?? general.DefaultMaxActiveAccounts,
            Math.Min(customer.CreditoPlazoMaximoDias ?? general.DefaultTermDays, general.EmployeeMaxTermDays),
            customer.CreditoPermitirSaldoVigente ?? general.AllowCreditWithOutstandingBalance,
            customer.CreditoInteresPorcentaje ?? general.DefaultInterestPercent,
            general.DaysPastDueForMoroso);
    }

    internal async Task ValidateCreditEligibilityAsync(
        Cliente customer,
        EffectiveCreditPolicy policy,
        long projectedNewDebtCentavos,
        int requestedTermDays,
        CancellationToken cancellationToken)
    {
        await ValidateCreditEligibilityAsync(
            Context,
            customer,
            policy,
            projectedNewDebtCentavos,
            requestedTermDays,
            cancellationToken);

    }

    internal async Task ValidateCreditEligibilityAsync(
        AppDbContext context,
        Cliente customer,
        EffectiveCreditPolicy policy,
        long projectedNewDebtCentavos,
        int requestedTermDays,
        CancellationToken cancellationToken)
    {
        if (customer.Estado != EstadoCliente.ACTIVO)
        {
            throw new InvalidOperationException("El cliente esta inactivo y no puede recibir credito.");
        }

        if (!customer.CreditoHabilitado)
        {
            var reason = string.IsNullOrWhiteSpace(customer.CreditoBloqueoMotivo)
                ? "sin motivo registrado"
                : customer.CreditoBloqueoMotivo;
            throw new InvalidOperationException($"El credito del cliente esta bloqueado: {reason}.");
        }

        if (requestedTermDays <= 0 || requestedTermDays > policy.MaxTermDays)
        {
            throw new InvalidOperationException($"El plazo solicitado no puede superar {policy.MaxTermDays} dia(s).");
        }

        var accounts = await GetOpenAccountsForCustomerAsync(context, customer.ClienteId, cancellationToken);
        var situation = CalculateSituation(accounts, policy.DaysPastDueForMoroso, DateTime.UtcNow);
        if (situation is SituacionCrediticia.VENCIDO or SituacionCrediticia.MOROSO)
        {
            throw new InvalidOperationException($"El cliente tiene credito {situation.ToString().ToLowerInvariant()}.");
        }

        if (accounts.Count > 0 && !policy.AllowOutstandingBalance)
        {
            throw new InvalidOperationException("La politica no permite nuevo credito con saldo vigente.");
        }

        if (accounts.Count >= policy.MaxActiveAccounts)
        {
            throw new InvalidOperationException("El cliente ya alcanzo el maximo de cuentas activas permitido.");
        }

        var currentDebt = accounts.Sum(c => c.SaldoActualCentavos);
        if (currentDebt + projectedNewDebtCentavos > policy.LimitCentavos)
        {
            throw new InvalidOperationException("El saldo proyectado supera el limite de credito autorizado.");
        }
    }

    internal async Task<string> GenerateAccountCodeAsync(CancellationToken cancellationToken)
    {
        return await GenerateAccountCodeAsync(Context, cancellationToken);
    }

    internal async Task<string> GenerateAccountCodeAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var prefix = $"CXC-{DateTime.Now:yyyyMMdd}-";
        var countToday = await context.CuentasPorCobrar
            .CountAsync(c => c.CodigoCuenta.StartsWith(prefix), cancellationToken);
        var next = countToday + 1;

        while (true)
        {
            var code = $"{prefix}{next:0000}";
            var exists = await context.CuentasPorCobrar
                .AnyAsync(c => c.CodigoCuenta == code, cancellationToken);
            if (!exists)
            {
                return code;
            }

            next++;
        }
    }

    internal async Task EnsureCreditSettingsAsync(CancellationToken cancellationToken)
    {
        await EnsureCreditSettingsAsync(Context, cancellationToken);
    }

    private static async Task EnsureCreditSettingsAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        foreach (var definition in CreditConfigurationKeys.Definitions)
        {
            var exists = await context.ConfiguracionSistema
                .AnyAsync(c => c.Clave == definition.Key, cancellationToken);
            if (exists)
            {
                continue;
            }

            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = definition.Key,
                Valor = definition.DefaultValue,
                Descripcion = definition.Description,
                FechaModificacion = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    internal async Task<CreditPolicySettings> ReadGeneralPolicyAsync(CancellationToken cancellationToken)
    {
        return await ReadGeneralPolicyAsync(Context, cancellationToken);
    }

    private static async Task<CreditPolicySettings> ReadGeneralPolicyAsync(
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

    private async Task<List<CuentaPorCobrar>> GetOpenAccountsForCustomerAsync(
        int clienteId,
        CancellationToken cancellationToken)
    {
        return await GetOpenAccountsForCustomerAsync(Context, clienteId, cancellationToken);
    }

    private static async Task<List<CuentaPorCobrar>> GetOpenAccountsForCustomerAsync(
        AppDbContext context,
        int clienteId,
        CancellationToken cancellationToken)
    {
        return await context.CuentasPorCobrar
            .AsNoTracking()
            .Where(c =>
                c.ClienteId == clienteId &&
                c.Estado == EstadoCuentaPorCobrar.ABIERTA &&
                c.SaldoActualCentavos > 0)
            .ToListAsync(cancellationToken);
    }

    private static SituacionCrediticia CalculateSituation(
        IReadOnlyList<CuentaPorCobrar> openAccounts,
        int daysPastDueForMoroso,
        DateTime now)
    {
        if (openAccounts.Count == 0 || openAccounts.Sum(c => c.SaldoActualCentavos) <= 0)
        {
            return SituacionCrediticia.AL_DIA;
        }

        var worstDays = openAccounts
            .Where(c => c.FechaVencimiento.Date < now.Date)
            .Select(c => (now.Date - c.FechaVencimiento.Date).Days)
            .DefaultIfEmpty(0)
            .Max();

        if (worstDays >= daysPastDueForMoroso)
        {
            return SituacionCrediticia.MOROSO;
        }

        return worstDays > 0
            ? SituacionCrediticia.VENCIDO
            : SituacionCrediticia.SALDO_VIGENTE;
    }

    private static AccountReceivableSummary ToSummary(
        CuentaPorCobrar account,
        int daysPastDueForMoroso)
    {
        var situation = account.Estado == EstadoCuentaPorCobrar.ABIERTA
            ? CalculateSituation([account], daysPastDueForMoroso, DateTime.UtcNow)
            : SituacionCrediticia.AL_DIA;

        return new AccountReceivableSummary(
            account.CuentaPorCobrarId,
            account.CodigoCuenta,
            account.VentaId,
            account.Venta.NumeroVenta,
            account.ClienteId,
            account.Cliente.CodigoCliente,
            account.Cliente.NombreCompleto,
            account.FechaCreacion,
            account.CapitalFinanciadoCentavos,
            account.InteresCalculadoCentavos,
            account.TotalOriginalCentavos,
            account.TotalPagadoCentavos,
            account.SaldoActualCentavos,
            account.FechaVencimiento,
            account.Estado,
            situation);
    }

    private static CustomerCreditPolicy ToCustomerPolicy(Cliente customer)
    {
        return new CustomerCreditPolicy(
            customer.ClienteId,
            customer.CreditoHabilitado,
            customer.CreditoBloqueoMotivo,
            customer.CreditoLimiteCentavos,
            customer.CreditoMaximoCuentasActivas,
            customer.CreditoPlazoMaximoDias,
            customer.CreditoPermitirSaldoVigente,
            customer.CreditoInteresPorcentaje,
            customer.CreditoUsuarioActualizacion?.NombreUsuario,
            customer.CreditoFechaActualizacion);
    }

    private void EnsureAdminSession()
    {
        if (!_currentSession.EsAdmin || _currentSession.UsuarioActual is null)
        {
            throw new InvalidOperationException("Solo ADMIN puede modificar politicas de credito.");
        }
    }

    private static void ValidateGeneralPolicy(UpdateCreditPolicySettingsRequest request)
    {
        if (request.DefaultLimitCentavos <= 0)
        {
            throw new ArgumentException("El limite predeterminado debe ser mayor que cero.", nameof(request));
        }

        if (request.DefaultMaxActiveAccounts <= 0)
        {
            throw new ArgumentException("El maximo de cuentas activas debe ser mayor que cero.", nameof(request));
        }

        if (request.DefaultTermDays <= 0 || request.EmployeeMaxTermDays <= 0 || request.AdminMaxTermDays <= 0)
        {
            throw new ArgumentException("Los plazos deben ser mayores que cero.", nameof(request));
        }

        if (request.DefaultTermDays > request.AdminMaxTermDays || request.EmployeeMaxTermDays > request.AdminMaxTermDays)
        {
            throw new ArgumentException("Los plazos predeterminados no pueden superar el maximo administrativo.", nameof(request));
        }

        if (request.DefaultInterestPercent < 0)
        {
            throw new ArgumentException("El interes no puede ser negativo.", nameof(request));
        }

        if (request.DaysPastDueForMoroso < 0)
        {
            throw new ArgumentException("Los dias para mora no pueden ser negativos.", nameof(request));
        }
    }

    private static void ValidateCustomerPolicy(UpdateCustomerCreditPolicyRequest request)
    {
        if (!request.CreditoHabilitado && string.IsNullOrWhiteSpace(request.CreditoBloqueoMotivo))
        {
            throw new ArgumentException("Debe indicar motivo para bloquear credito.", nameof(request));
        }

        if (request.LimiteCentavos is <= 0)
        {
            throw new ArgumentException("El limite individual debe ser mayor que cero.", nameof(request));
        }

        if (request.MaximoCuentasActivas is <= 0)
        {
            throw new ArgumentException("El maximo individual de cuentas debe ser mayor que cero.", nameof(request));
        }

        if (request.PlazoMaximoDias is <= 0)
        {
            throw new ArgumentException("El plazo individual debe ser mayor que cero.", nameof(request));
        }

        if (request.InteresPorcentaje is < 0)
        {
            throw new ArgumentException("El interes individual no puede ser negativo.", nameof(request));
        }
    }

    private static string ToPolicyText(Cliente customer)
    {
        return string.Join("; ", [
            $"habilitado={customer.CreditoHabilitado}",
            $"motivo={customer.CreditoBloqueoMotivo}",
            $"limite={customer.CreditoLimiteCentavos}",
            $"max_cuentas={customer.CreditoMaximoCuentasActivas}",
            $"plazo={customer.CreditoPlazoMaximoDias}",
            $"permitir_saldo={customer.CreditoPermitirSaldoVigente}",
            $"interes={customer.CreditoInteresPorcentaje}"
        ]);
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

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
