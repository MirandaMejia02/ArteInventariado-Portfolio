using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Station;

public sealed class InstallationManagementService : IInstallationManagementService
{
    private readonly LocalStationConfigurationStore _store;
    private readonly IInstallationContext _installationContext;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly StationEventLog _eventLog;
    private readonly Func<InstallationMode, Guid?, InstallationMode?, CancellationToken, Task>? _machineWideModeWriter;

    internal InstallationManagementService(
        LocalStationConfigurationStore store,
        IInstallationContext installationContext,
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        StationEventLog eventLog,
        Func<InstallationMode, Guid?, InstallationMode?, CancellationToken, Task>? machineWideModeWriter = null)
    {
        _store = store;
        _installationContext = installationContext;
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
        _eventLog = eventLog;
        _machineWideModeWriter = machineWideModeWriter;
    }

    public async Task<InstallationSetupProfile> GetSetupProfileAsync(
        CancellationToken cancellationToken = default)
    {
        var status = _installationContext.Status;
        var protectedAdmin = IsProtectedAdministratorSession();

        if (status.IsConfigured)
        {
            return new InstallationSetupProfile(
                InstallationSetupKind.AlreadyConfigured,
                status,
                protectedAdmin,
                $"Esta estacion ya esta configurada en {status.DisplayName}.");
        }

        if (status.State is StationConfigurationState.Corrupt or
            StationConfigurationState.Invalid or
            StationConfigurationState.Unsupported)
        {
            return new InstallationSetupProfile(
                InstallationSetupKind.ConfigurationRecovery,
                status,
                protectedAdmin,
                "La configuracion local debe recuperarse de forma explicita y segura.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var hasExistingBusinessData =
            await context.Usuarios.CountAsync(cancellationToken) > 1 ||
            await context.Productos.AnyAsync(cancellationToken) ||
            await context.Ventas.AnyAsync(cancellationToken) ||
            await context.Clientes.AnyAsync(cancellationToken) ||
            await context.Proveedores.AnyAsync(cancellationToken) ||
            await context.Compras.AnyAsync(cancellationToken) ||
            await context.Cotizaciones.AnyAsync(cancellationToken) ||
            await context.PedidosPersonalizados.AnyAsync(cancellationToken);

        return hasExistingBusinessData
            ? new InstallationSetupProfile(
                InstallationSetupKind.ExistingInstallationUpgrade,
                status,
                protectedAdmin,
                "Esta base existente requiere confirmar una sola vez la identidad local de la computadora. No se modificaran usuarios ni credenciales.")
            : new InstallationSetupProfile(
                InstallationSetupKind.NewInstallation,
                status,
                protectedAdmin,
                "Esta es una instalacion nueva. El propietario debe completar la configuracion inicial de la estacion.");
    }

    public async Task<InstallationConfigurationResult> CompleteSetupAsync(
        CompleteInstallationSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMode(request.Mode);
        if (!request.ConfirmationAccepted)
        {
            throw new InvalidOperationException("Debe confirmar conscientemente el modo de esta instalacion.");
        }

        if (_installationContext.Status.IsConfigured)
        {
            throw new InvalidOperationException("La configuracion inicial de esta estacion ya fue completada.");
        }

        var status = _installationContext.Status;
        var admin = await ReauthenticateProtectedAdministratorAsync(request.Password, cancellationToken);
        await SaveConfiguredAsync(request.Mode, status.InstallationId, status.Mode, cancellationToken);

        var description = request.Mode == InstallationMode.Principal
            ? "Configuracion inicial completada. Esta estacion fue establecida como PRINCIPAL."
            : "Configuracion inicial completada. Esta estacion fue establecida como CONSULTA.";
        _eventLog.Write("INSTALLATION_SETUP_COMPLETED", admin.NombreUsuario, request.Mode, description);
        await TryRegisterDatabaseAuditAsync(admin.UsuarioId, description, allowSetupWrite: true, cancellationToken);

        return new InstallationConfigurationResult(
            request.Mode,
            RestartRequired: true,
            "La configuracion local fue guardada. Reinicie ArteInventariado para aplicar el modo de forma segura.");
    }

    public async Task<InstallationConfigurationResult> ChangeModeAsync(
        ChangeInstallationModeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMode(request.Mode);
        if (!request.ConfirmationAccepted)
        {
            throw new InvalidOperationException("Debe aceptar la advertencia antes de cambiar el modo de instalacion.");
        }

        var status = _installationContext.Status;
        if (!status.IsConfigured || !status.Mode.HasValue)
        {
            throw new InvalidOperationException("La estacion debe completar primero su configuracion inicial.");
        }

        var admin = await ReauthenticateProtectedAdministratorAsync(request.Password, cancellationToken);
        if (status.Mode == request.Mode)
        {
            return new InstallationConfigurationResult(
                request.Mode,
                RestartRequired: false,
                $"La estacion ya esta configurada en {status.DisplayName}.");
        }

        var description = $"Cambio de identidad local autorizado: {status.Mode} -> {request.Mode}. Se aplicara al reiniciar.";
        if (status.AllowsOperationalWrites)
        {
            await TryRegisterDatabaseAuditAsync(admin.UsuarioId, description, allowSetupWrite: false, cancellationToken);
        }

        await SaveConfiguredAsync(request.Mode, status.InstallationId, status.Mode, cancellationToken);
        _eventLog.Write("INSTALLATION_MODE_CHANGED", admin.NombreUsuario, request.Mode, description);

        return new InstallationConfigurationResult(
            request.Mode,
            RestartRequired: true,
            "El cambio fue guardado y se aplicara al reiniciar. No existe sincronizacion ni fusion de bases en V1.");
    }

    private async Task<ProtectedAdminRecord> ReauthenticateProtectedAdministratorAsync(
        string password,
        CancellationToken cancellationToken)
    {
        if (!IsProtectedAdministratorSession())
        {
            throw new InvalidOperationException("Solo el administrador principal 'admin' puede configurar la identidad de instalacion.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("La contrasena actual es obligatoria.", nameof(password));
        }

        var current = _currentSession.UsuarioActual!;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var admin = await context.Usuarios
            .AsNoTracking()
            .Where(user =>
                user.UsuarioId == current.UsuarioId &&
                user.Activo &&
                user.Rol == RolUsuario.ADMIN)
            .Select(user => new ProtectedAdminRecord(
                user.UsuarioId,
                user.NombreUsuario,
                user.PasswordHash))
            .SingleOrDefaultAsync(cancellationToken);

        if (admin is null || !ProtectedAdministrator.IsUserName(admin.NombreUsuario))
        {
            throw new InvalidOperationException("La sesion no corresponde al administrador principal protegido.");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
        {
            _eventLog.Write(
                "INSTALLATION_REAUTHENTICATION_FAILED",
                current.NombreUsuario,
                _installationContext.Status.Mode,
                "Reautenticacion rechazada para una operacion critica de estacion.");
            throw new InvalidOperationException("La contrasena actual del administrador principal es incorrecta.");
        }

        return admin;
    }

    private async Task TryRegisterDatabaseAuditAsync(
        int userId,
        string description,
        bool allowSetupWrite,
        CancellationToken cancellationToken)
    {
        try
        {
            using var authorization = allowSetupWrite
                ? InstallationWriteAuthorizationScope.Begin()
                : null;
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    userId,
                    TipoBitacora.CONFIGURACION_MODIFICADA,
                    "configuracion_estacion",
                    null,
                    description),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _eventLog.Write(
                "INSTALLATION_DATABASE_AUDIT_FAILED",
                _currentSession.UsuarioActual?.NombreUsuario,
                _installationContext.Status.Mode,
                $"El evento local se conservo, pero la bitacora de DB no pudo actualizarse: {ex.GetType().Name}.");
        }
    }

    private bool IsProtectedAdministratorSession()
    {
        var current = _currentSession.UsuarioActual;
        return current is not null &&
            current.Rol == RolUsuario.ADMIN &&
            ProtectedAdministrator.IsUserName(current.NombreUsuario);
    }

    private Task SaveConfiguredAsync(
        InstallationMode mode,
        Guid? expectedInstallationId,
        InstallationMode? expectedCurrentMode,
        CancellationToken cancellationToken)
    {
        if (_store.IsMachineWideSplit)
        {
            if (_machineWideModeWriter is null)
            {
                throw new InvalidOperationException(
                    "La instalacion machine-wide no tiene disponible el helper protegido de station.");
            }

            return _machineWideModeWriter(
                mode,
                expectedInstallationId,
                expectedCurrentMode,
                cancellationToken);
        }

        _store.SaveConfigured(mode);
        return Task.CompletedTask;
    }

    private static void ValidateMode(InstallationMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "El modo de instalacion no es valido.");
        }
    }

    private sealed record ProtectedAdminRecord(int UsuarioId, string NombreUsuario, string PasswordHash);
}
