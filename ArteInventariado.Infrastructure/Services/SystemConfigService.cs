using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SystemConfigService : ISystemConfigService
{
    public const string BusinessNameKey = "negocio.nombre";
    public const string BusinessAddressKey = "negocio.direccion";
    public const string BusinessPhoneKey = "negocio.telefono";
    public const string BusinessEmailKey = "negocio.correo";
    public const string AutomaticCashOpeningTimeKey = "caja.hora_apertura_automatica";
    public const string BitcoinEnabledKey = "pagos.bitcoin.habilitado";

    // Conservadas solo para leer instalaciones y fixtures antiguos. Las rutas nuevas son locales a la estacion.
    public const string ReportsFolderKey = "reportes.carpeta";
    public const string BackupsFolderKey = "respaldos.carpeta";
    public const string DefaultBusinessName = "ArteInventariado Demo";

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public SystemConfigService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    internal SystemConfigService(AppDbContext context, ICurrentSession currentSession, IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para configuracion.");

    public async Task<IReadOnlyList<SystemConfigSetting>> GetBasicSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();

        var definitions = BasicDefinitions();
        var keys = definitions.Select(d => d.Key).ToList();
        var stored = await Context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => keys.Contains(c.Clave))
            .ToDictionaryAsync(c => c.Clave, cancellationToken);

        return definitions
            .OrderBy(d => d.Key)
            .Select(d => stored.TryGetValue(d.Key, out var value)
                ? new SystemConfigSetting(value.Clave, value.Valor, value.Descripcion)
                : new SystemConfigSetting(d.Key, d.DefaultValue, d.Description))
            .ToList();
    }

    public async Task<bool> IsBitcoinEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var configured = await Context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => c.Clave == BitcoinEnabledKey)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        return IsBitcoinEnabledValue(configured);
    }

    public async Task UpdateBasicSettingsAsync(
        UpdateSystemConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await EnsureBasicSettingsAsync(cancellationToken);

        var allowed = BasicDefinitions().ToDictionary(d => d.Key, d => d.Description);
        var changes = new List<string>();

        foreach (var setting in request.Settings)
        {
            if (!allowed.ContainsKey(setting.Clave))
            {
                continue;
            }

            var config = await Context.ConfiguracionSistema
                .FirstAsync(c => c.Clave == setting.Clave, cancellationToken);
            var value = NormalizeValue(setting.Clave, setting.Valor);

            if (config.Valor == value)
            {
                continue;
            }

            var before = config.Valor;
            config.Valor = value;
            config.FechaModificacion = DateTime.UtcNow;
            changes.Add($"{config.Clave}: '{before}' -> '{config.Valor}'");
        }

        if (changes.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                _currentSession.UsuarioActual!.UsuarioId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "configuracion_sistema",
                null,
                $"Configuracion del sistema modificada. {string.Join("; ", changes)}"),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureBasicSettingsAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in BasicDefinitions())
        {
            var exists = await Context.ConfiguracionSistema
                .AnyAsync(c => c.Clave == definition.Key, cancellationToken);

            if (exists)
            {
                continue;
            }

            Context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = definition.Key,
                Valor = definition.DefaultValue,
                Descripcion = definition.Description,
                FechaModificacion = DateTime.UtcNow
            });
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    private void EnsureAdminSession()
    {
        if (!_currentSession.EsAdmin || _currentSession.UsuarioActual is null)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede modificar configuracion.");
        }
    }

    private static string NormalizeValue(string key, string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        if (key == BusinessNameKey && string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("El nombre comercial del negocio es obligatorio.");
        }

        if (key == AutomaticCashOpeningTimeKey &&
            !string.IsNullOrWhiteSpace(normalized) &&
            !TimeOnly.TryParse(normalized, out _))
        {
            throw new ArgumentException("La hora de apertura automatica debe tener un formato valido, por ejemplo 08:00.");
        }

        if (key == BitcoinEnabledKey)
        {
            if (!bool.TryParse(normalized, out var enabled))
            {
                throw new ArgumentException("La configuracion de Bitcoin debe ser verdadero o falso.");
            }

            return enabled ? "true" : "false";
        }

        return normalized;
    }

    internal static bool IsBitcoinEnabledValue(string? value) =>
        !bool.TryParse(value, out var enabled) || enabled;

    private static IReadOnlyList<ConfigDefinition> BasicDefinitions()
    {
        return
        [
            new ConfigDefinition(BusinessNameKey, DefaultBusinessName, "Nombre comercial del negocio."),
            new ConfigDefinition(BusinessAddressKey, string.Empty, "Direccion comercial del negocio."),
            new ConfigDefinition(BusinessPhoneKey, string.Empty, "Telefono de contacto del negocio."),
            new ConfigDefinition(BusinessEmailKey, string.Empty, "Correo de contacto del negocio."),
            new ConfigDefinition(AutomaticCashOpeningTimeKey, "08:00", "Hora sugerida para apertura automatica de caja."),
            new ConfigDefinition(BitcoinEnabledKey, "true", "Permite usar Bitcoin en operaciones futuras.")
        ];
    }

    private sealed record ConfigDefinition(string Key, string DefaultValue, string Description);
}
