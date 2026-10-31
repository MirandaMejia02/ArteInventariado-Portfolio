using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentSession _currentSession;
    private readonly IInstallationContext? _installationContext;
    private readonly StationEventLog? _stationEventLog;

    [ActivatorUtilitiesConstructor]
    public AuthService(
        IDbContextFactory<AppDbContext> contextFactory,
        IAuditService auditService,
        ICurrentSession currentSession,
        IInstallationContext installationContext,
        StationEventLog stationEventLog)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _currentSession = currentSession;
        _installationContext = installationContext;
        _stationEventLog = stationEventLog;
    }

    internal AuthService(
        IDbContextFactory<AppDbContext> contextFactory,
        IAuditService auditService,
        ICurrentSession currentSession)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _currentSession = currentSession;
    }

    internal AuthService(
        AppDbContext context,
        IAuditService auditService,
        ICurrentSession currentSession)
    {
        _legacyContext = context;
        _auditService = auditService;
        _currentSession = currentSession;
    }

    public async Task<AuthResult> LoginAsync(
        string nombreUsuario,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nombreUsuario) || string.IsNullOrWhiteSpace(password))
        {
            await RegistrarLoginFallidoAsync(null, nombreUsuario, "Usuario o contrasena vacios.", cancellationToken);
            return AuthResult.Fallo("Usuario o contrasena incorrectos.");
        }

        var usuarioNormalizado = nombreUsuario.Trim();

        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;
        var usuario = await context.Usuarios
            .FirstOrDefaultAsync(u => u.NombreUsuario == usuarioNormalizado, cancellationToken);

        if (usuario is null)
        {
            await RegistrarLoginFallidoAsync(null, usuarioNormalizado, "Usuario no encontrado.", cancellationToken);
            return AuthResult.Fallo("Usuario o contrasena incorrectos.");
        }

        if (!usuario.Activo)
        {
            await RegistrarLoginFallidoAsync(usuario.UsuarioId, usuario.NombreUsuario, "Usuario inactivo.", cancellationToken);
            return AuthResult.Fallo("El usuario esta inactivo.");
        }

        if (usuario.Rol is not RolUsuario.ADMIN and not RolUsuario.EMPLEADO)
        {
            await RegistrarLoginFallidoAsync(usuario.UsuarioId, usuario.NombreUsuario, "Rol no permitido.", cancellationToken);
            return AuthResult.Fallo("El rol del usuario no es valido.");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
        {
            await RegistrarLoginFallidoAsync(usuario.UsuarioId, usuario.NombreUsuario, "Contrasena incorrecta.", cancellationToken);
            return AuthResult.Fallo("Usuario o contrasena incorrectos.");
        }

        if (CanWriteDatabaseEvents())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            usuario.UltimoLogin = DateTime.UtcNow;
            context.EmployeeLoginEvents.Add(new Domain.Entities.EmployeeLoginEvent
            {
                UserId = usuario.UsuarioId,
                LoggedInAtUtc = usuario.UltimoLogin.Value,
                InstallationId = _installationContext?.Status.InstallationId?.ToString("D"),
                StationMode = _installationContext?.Status.Mode.ToString()
            });
            context.Bitacora.Add(AuditEntryFactory.Create(
                new AuditEntryRequest(
                    usuario.UsuarioId,
                    TipoBitacora.LOGIN,
                    "usuarios",
                    usuario.UsuarioId,
                    $"Inicio de sesion exitoso para usuario '{usuario.NombreUsuario}'.")));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            _stationEventLog?.Write(
                "LOGIN_READ_ONLY",
                usuario.NombreUsuario,
                _installationContext?.Status.Mode,
                "Inicio de sesion local sin modificar la copia de base de datos.");
        }

        var usuarioSesion = new UsuarioSesion(
            usuario.UsuarioId,
            usuario.NombreCompleto,
            usuario.NombreUsuario,
            usuario.Rol);

        _currentSession.IniciarSesion(usuarioSesion);

        return AuthResult.Ok(usuarioSesion);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var usuario = _currentSession.UsuarioActual;

        try
        {
            if (usuario is not null && CanWriteDatabaseEvents())
            {
                await _auditService.RegistrarAsync(
                    new AuditEntryRequest(
                        usuario.UsuarioId,
                        TipoBitacora.LOGOUT,
                        "usuarios",
                        usuario.UsuarioId,
                        $"Cierre de sesion para usuario '{usuario.NombreUsuario}'."),
                    cancellationToken);
            }
            else if (usuario is not null)
            {
                _stationEventLog?.Write(
                    "LOGOUT_READ_ONLY",
                    usuario.NombreUsuario,
                    _installationContext?.Status.Mode,
                    "Cierre de sesion local sin modificar la copia de base de datos.");
            }
        }
        finally
        {
            _currentSession.CerrarSesion();
        }
    }

    private Task RegistrarLoginFallidoAsync(
        int? usuarioId,
        string? nombreUsuario,
        string motivo,
        CancellationToken cancellationToken)
    {
        var usuarioTexto = string.IsNullOrWhiteSpace(nombreUsuario)
            ? "desconocido"
            : nombreUsuario.Trim();

        if (!CanWriteDatabaseEvents())
        {
            _stationEventLog?.Write(
                "LOGIN_FAILED_READ_ONLY",
                usuarioTexto,
                _installationContext?.Status.Mode,
                $"Intento de inicio de sesion rechazado. Motivo: {motivo}");
            return Task.CompletedTask;
        }

        return _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.LOGIN,
                "usuarios",
                usuarioId,
                $"Intento de inicio de sesion fallido para usuario '{usuarioTexto}'. Motivo: {motivo}"),
            cancellationToken);
    }

    private bool CanWriteDatabaseEvents() =>
        _installationContext is null || _installationContext.AllowsOperationalWrites;
}
