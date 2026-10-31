using ArteInventariado.Application.Interfaces;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Reautentica al ADMIN de la sesion actual sin aceptar una bandera fabricada
/// por la UI ni persistir la contrasena. La cuenta protegida de instalacion
/// sigue usando su servicio canonico; los ajustes requieren al ADMIN actual.
/// </summary>
public sealed class AdminReauthenticationService : IAdminReauthenticationService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly StationEventLog? _eventLog;

    [ActivatorUtilitiesConstructor]
    public AdminReauthenticationService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        StationEventLog? eventLog = null)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _eventLog = eventLog;
    }

    internal AdminReauthenticationService(
        AppDbContext context,
        ICurrentSession currentSession,
        StationEventLog? eventLog = null)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _eventLog = eventLog;
    }

    public async Task<int> VerifyCurrentAdminAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        var current = _currentSession.UsuarioActual;
        if (current is null || current.Rol != Domain.Enums.RolUsuario.ADMIN)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN autenticado puede ajustar existencias.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("La contrasena del usuario ADMIN es obligatoria.", nameof(password));
        }

        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var user = await lease.Context.Usuarios
            .AsNoTracking()
            .Where(item => item.UsuarioId == current.UsuarioId &&
                item.Activo && item.Rol == Domain.Enums.RolUsuario.ADMIN)
            .Select(item => new { item.UsuarioId, item.NombreUsuario, item.PasswordHash })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            _eventLog?.Write(
                "INVENTORY_REAUTHENTICATION_FAILED",
                current.NombreUsuario,
                null,
                "Reautenticacion rechazada para un ajuste de existencias.");
            throw new InvalidOperationException("La contrasena del usuario ADMIN es incorrecta.");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _eventLog?.Write(
                "INVENTORY_REAUTHENTICATION_FAILED",
                current.NombreUsuario,
                null,
                "Reautenticacion rechazada para un ajuste de existencias.");
            throw new InvalidOperationException("La contrasena del usuario ADMIN es incorrecta.");
        }

        return user.UsuarioId;
    }
}
