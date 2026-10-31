using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Services;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Enforces the internal recovery boundary in the service layer.  The
/// protected administrator is the existing authenticated `admin` account;
/// being a delegated ADMIN is intentionally insufficient.
/// </summary>
public sealed class CloudRecoveryAuthorizationService : ICloudRecoveryAuthorizationService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentSession _currentSession;

    public CloudRecoveryAuthorizationService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _currentSession = currentSession ?? throw new ArgumentNullException(nameof(currentSession));
    }

    public async Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
    {
        var current = _currentSession.UsuarioActual;
        if (!ProtectedAdministrator.IsProtectedAdministrator(current))
        {
            throw new UnauthorizedAccessException(
                "La recuperación cloud está reservada al Administrador Principal.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var persisted = await context.Usuarios
            .AsNoTracking()
            .Where(user => user.UsuarioId == current!.UsuarioId)
            .Select(user => new { user.Activo, user.Rol, user.NombreUsuario })
            .SingleOrDefaultAsync(cancellationToken);

        if (persisted is null ||
            !persisted.Activo ||
            !ProtectedAdministrator.IsUserName(persisted.NombreUsuario) ||
            persisted.Rol != Domain.Enums.RolUsuario.ADMIN)
        {
            throw new UnauthorizedAccessException(
                "La sesión del Administrador Principal ya no es válida para recuperación cloud.");
        }
    }
}
