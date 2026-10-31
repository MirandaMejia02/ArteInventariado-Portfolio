using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly IInstallationContext? _installationContext;

    [ActivatorUtilitiesConstructor]
    public AccountService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        IInstallationContext installationContext)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
        _installationContext = installationContext;
    }

    internal AccountService(AppDbContext context, ICurrentSession currentSession, IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la cuenta de usuario.");

    public async Task ChangeCurrentPasswordAsync(
        ChangeCurrentPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            throw new ArgumentException("La contrasena actual es obligatoria.", nameof(request));
        }

        PasswordPolicy.EnsureValid(request.NewPassword, nameof(request.NewPassword));

        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new InvalidOperationException("La nueva contrasena y la confirmacion no coinciden.");
        }

        var usuarioId = GetCurrentUserId();
        var usuario = await Context.Usuarios
            .FirstOrDefaultAsync(u => u.UsuarioId == usuarioId, cancellationToken)
            ?? throw new InvalidOperationException("El usuario actual no existe.");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, usuario.PasswordHash))
        {
            throw new InvalidOperationException("La contrasena actual es incorrecta.");
        }

        using var setupAuthorization = ShouldAllowProtectedAdminBootstrap(usuario.NombreUsuario)
            ? InstallationWriteAuthorizationScope.Begin()
            : null;
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuario.UsuarioId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "usuarios",
                usuario.UsuarioId,
                $"Usuario '{usuario.NombreUsuario}' cambio su propia contrasena."),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    }

    internal AccountService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    private bool ShouldAllowProtectedAdminBootstrap(string userName) =>
        _installationContext is not null &&
        !_installationContext.Status.IsConfigured &&
        ProtectedAdministrator.IsUserName(userName);
}
