using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Creates the protected application administrator only during an explicit
/// first-run owner flow. The structural database initializer deliberately
/// never creates a user or a password.
/// </summary>
public sealed class PrincipalProvisioningService : IPrincipalProvisioningService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ArteInventariadoPathProvider _paths;
    private readonly IInstallationContext _installationContext;
    private readonly IAuditService _auditService;
    private readonly Guid? _databaseResetOperationId;
    private readonly Func<bool> _isAdministrator;
    private readonly bool _allowSyntheticResetAuthorization;

    public PrincipalProvisioningService(
        IDbContextFactory<AppDbContext> contextFactory,
        ArteInventariadoPathProvider paths,
        IInstallationContext installationContext,
        IAuditService auditService)
        : this(contextFactory, paths, installationContext, auditService, null, MachineWideElevation.IsAdministrator)
    {
    }

    public PrincipalProvisioningService(
        IDbContextFactory<AppDbContext> contextFactory,
        ArteInventariadoPathProvider paths,
        IInstallationContext installationContext,
        IAuditService auditService,
        Guid? databaseResetOperationId)
        : this(
            contextFactory,
            paths,
            installationContext,
            auditService,
            databaseResetOperationId,
            MachineWideElevation.IsAdministrator)
    {
    }

    internal PrincipalProvisioningService(
        IDbContextFactory<AppDbContext> contextFactory,
        ArteInventariadoPathProvider paths,
        IInstallationContext installationContext,
        IAuditService auditService,
        Guid? databaseResetOperationId,
        Func<bool> isAdministrator,
        bool allowSyntheticResetAuthorization = false)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _installationContext = installationContext;
        _auditService = auditService;
        _databaseResetOperationId = databaseResetOperationId;
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        _allowSyntheticResetAuthorization = allowSyntheticResetAuthorization;
    }

    public async Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, null, cancellationToken);
        if (_installationContext.Status.IsConfigured && !IsAuthorizedDatabaseResetProvisioning())
        {
            return false;
        }

        var users = await lease.Context.Usuarios
            .AsNoTracking()
            .Select(user => new { user.NombreUsuario })
            .ToListAsync(cancellationToken);

        // Only a completely empty, still-unconfigured database is eligible.
        // A configured or partially populated database must go through the
        // existing recovery path instead of allowing an account takeover.
        return users.Count == 0;
    }

    public async Task ProvisionAsync(
        PrincipalProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PasswordPolicy.EnsureValid(request.Password, nameof(request.Password));
        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("La contraseña y su confirmación no coinciden.");
        }

        if (_installationContext.Status.IsConfigured && !IsAuthorizedDatabaseResetProvisioning())
        {
            throw new InvalidOperationException(
                "La instalación ya está configurada; no se puede volver a provisionar el Administrador Principal.");
        }

        if (_paths.IsMachineWide && !_isAdministrator())
        {
            throw new UnauthorizedAccessException(
                "La provisión inicial machine-wide requiere consentimiento UAC del propietario.");
        }

        // The scope is intentionally limited to this one structural first-run
        // write. The clear-text password remains in the request only for the
        // duration of hashing and is never logged or persisted.
        using var authorization = InstallationWriteAuthorizationScope.Begin();
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, null, cancellationToken);
        var context = lease.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Only the existence of a user matters here.  Do not materialize
        // existing password hashes while checking that first-run is empty.
        var users = await context.Usuarios
            .Select(user => user.UsuarioId)
            .ToListAsync(cancellationToken);
        if (users.Count != 0)
        {
            throw new InvalidOperationException(
                "La instalación ya contiene usuarios; la provisión inicial debe resolverse mediante recuperación protegida.");
        }

        if (await context.Usuarios.AnyAsync(
                user => user.NombreUsuario == ProtectedAdministrator.UserName,
                cancellationToken))
        {
            throw new InvalidOperationException("El Administrador Principal ya fue provisionado.");
        }

        var principal = new Usuario
        {
            NombreCompleto = "Administrador principal",
            NombreUsuario = ProtectedAdministrator.UserName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        context.Usuarios.Add(principal);
        await context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                null,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "usuarios",
                principal.UsuarioId,
                "Administrador Principal provisionado durante la configuración inicial."),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private bool IsAuthorizedDatabaseResetProvisioning() =>
        _databaseResetOperationId is { } operationId &&
        (_allowSyntheticResetAuthorization
            ? MachineWideDatabaseResetJournalStore.IsProvisioningRequestSynthetic(_paths, operationId)
            : _paths.IsMachineWide
            ? MachineWideDatabaseResetJournalStore.IsProvisioningRequest(_paths, operationId)
            : false);
}
