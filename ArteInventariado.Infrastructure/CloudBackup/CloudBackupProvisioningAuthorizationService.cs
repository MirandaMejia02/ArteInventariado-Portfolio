using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Authorization boundary for human cloud provisioning.  The application
/// account is not a Windows account: an elevated process therefore receives
/// only a short-lived, one-use grant created after the protected principal
/// reauthenticates.  The grant contains no password or cloud secret and is
/// checked again against the persisted principal verifier before use.
/// </summary>
public sealed class CloudBackupProvisioningAuthorizationService
{
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(2);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ArteInventariadoPathProvider _paths;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly IInstallationContext _installationContext;
    private readonly StationEventLog _eventLog;
    private readonly TimeProvider _timeProvider;

    public CloudBackupProvisioningAuthorizationService(
        IDbContextFactory<AppDbContext> contextFactory,
        ArteInventariadoPathProvider paths,
        ICurrentSession currentSession,
        IAuditService auditService,
        IInstallationContext installationContext,
        StationEventLog eventLog,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _currentSession = currentSession ?? throw new ArgumentNullException(nameof(currentSession));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _installationContext = installationContext ?? throw new ArgumentNullException(nameof(installationContext));
        _eventLog = eventLog ?? throw new ArgumentNullException(nameof(eventLog));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Used by the settings screen only to hide the entry point for delegated
    /// administrators and employees.  The action method still performs the
    /// full database check, so this is never the security boundary by itself.
    /// </summary>
    public async Task<bool> CanCurrentSessionOpenAsync(CancellationToken cancellationToken = default)
    {
        var current = _currentSession.UsuarioActual;
        if (current is null || current.Rol != RolUsuario.ADMIN)
        {
            return false;
        }

        var principal = await LoadPrincipalAsync(current.UsuarioId, cancellationToken)
            .ConfigureAwait(false);
        return principal is not null;
    }

    /// <summary>
    /// Reauthenticates the protected application principal, records the
    /// attempt without sensitive values, and creates the one-use UAC grant.
    /// </summary>
    public async Task<CloudBackupProvisioningAuthorizationGrant> ReauthenticateAndCreateGrantAsync(
        string password,
        bool confirmationAccepted,
        CancellationToken cancellationToken = default)
    {
        var current = _currentSession.UsuarioActual;
        if (!confirmationAccepted)
        {
            await RecordAttemptAsync(
                current?.UsuarioId,
                current?.NombreUsuario,
                "CLOUD_PROVISIONING_REJECTED",
                "El administrador no confirmó el aprovisionamiento cloud.",
                cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                "Debe confirmar conscientemente el aprovisionamiento del uploader cloud.");
        }

        if (current is null || current.Rol != RolUsuario.ADMIN)
        {
            await RecordAttemptAsync(
                current?.UsuarioId,
                current?.NombreUsuario,
                "CLOUD_PROVISIONING_REJECTED",
                "Intento de aprovisionamiento cloud sin una sesión ADMIN válida.",
                cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException(
                "Solo el Administrador Principal puede aprovisionar el uploader cloud.");
        }

        var principal = await LoadPrincipalAsync(current.UsuarioId, cancellationToken)
            .ConfigureAwait(false);
        if (principal is null)
        {
            await RecordAttemptAsync(
                current.UsuarioId,
                current.NombreUsuario,
                "CLOUD_PROVISIONING_REJECTED",
                "La sesión no corresponde al Administrador Principal protegido.",
                cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException(
                "Solo el Administrador Principal puede aprovisionar el uploader cloud.");
        }

        if (string.IsNullOrWhiteSpace(password) ||
            !BCrypt.Net.BCrypt.Verify(password, principal.PasswordHash))
        {
            await RecordAttemptAsync(
                principal.UsuarioId,
                principal.NombreUsuario,
                "CLOUD_PROVISIONING_REAUTHENTICATION_FAILED",
                "La reautenticación del Administrador Principal fue rechazada.",
                cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException(
                "La contraseña actual del Administrador Principal es incorrecta.");
        }

        var installationId = _installationContext.Status.InstallationId;
        if (!_installationContext.Status.IsConfigured ||
            !installationId.HasValue ||
            installationId.Value == Guid.Empty)
        {
            await RecordAttemptAsync(
                principal.UsuarioId,
                principal.NombreUsuario,
                "CLOUD_PROVISIONING_REJECTED",
                "La estación no tiene una identidad local configurada.",
                cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                "La estación debe estar configurada antes de aprovisionar el uploader cloud.");
        }

        var now = _timeProvider.GetUtcNow();
        var grant = new CloudBackupProvisioningAuthorizationGrant(
            principal.UsuarioId,
            installationId.Value,
            now,
            now.Add(GrantLifetime),
            Proof: string.Empty);
        grant = grant with { Proof = CreateProof(grant, principal.PasswordHash) };
        CloudBackupProvisioningHandoffStore.Save(_paths, grant);

        await RecordAttemptAsync(
            principal.UsuarioId,
            principal.NombreUsuario,
            "CLOUD_PROVISIONING_AUTHORIZED",
            "El Administrador Principal reautenticó el aprovisionamiento cloud; se emitió una autorización temporal.",
            cancellationToken).ConfigureAwait(false);
        return grant;
    }

    /// <summary>
    /// Consumes the pending handoff for the elevated continuation.  A missing,
    /// expired, replayed or tampered handoff is rejected before any form opens.
    /// </summary>
    public async Task<CloudBackupProvisioningAuthorizationGrant> ConsumeHandoffAsync(
        CancellationToken cancellationToken = default)
    {
        var grant = CloudBackupProvisioningHandoffStore.Consume(_paths);
        if (grant is null)
        {
            await RecordAttemptAsync(
                null,
                null,
                "CLOUD_PROVISIONING_REJECTED",
                "Se rechazó una continuación cloud sin una autorización temporal válida.",
                cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException(
                "La continuación elevada del aprovisionamiento cloud no está autorizada.");
        }

        try
        {
            await EnsureGrantAsync(grant, cancellationToken).ConfigureAwait(false);
            await RecordAttemptAsync(
                grant.PrincipalUserId,
                ProtectedAdministrator.UserName,
                "CLOUD_PROVISIONING_CONTINUATION_ACCEPTED",
                "La continuación elevada del aprovisionamiento cloud fue aceptada.",
                cancellationToken).ConfigureAwait(false);
            return grant;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            await RecordAttemptAsync(
                grant.PrincipalUserId,
                ProtectedAdministrator.UserName,
                "CLOUD_PROVISIONING_REJECTED",
                "Se rechazó una autorización temporal cloud inválida o vencida.",
                cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException(
                "La autorización temporal del aprovisionamiento cloud no es válida.",
                ex);
        }
    }

    /// <summary>Discards a grant when the owner cancels the UAC boundary.</summary>
    public void DiscardPendingHandoff() => CloudBackupProvisioningHandoffStore.Discard(_paths);

    /// <summary>
    /// Final service-layer check used by both preflight and persistence.  It
    /// deliberately does not trust the form, command line or Windows token.
    /// </summary>
    public async Task EnsureGrantAsync(
        CloudBackupProvisioningAuthorizationGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ValidateGrantShape(grant);

        var now = _timeProvider.GetUtcNow();
        if (grant.ExpiresAtUtc < now || grant.IssuedAtUtc > now.Add(TimeSpan.FromSeconds(10)))
        {
            throw new UnauthorizedAccessException("La autorización temporal del aprovisionamiento cloud expiró.");
        }

        var status = new LocalStationConfigurationStore(_paths).Load().Status;
        if (!status.IsConfigured || status.InstallationId != grant.InstallationId)
        {
            throw new InvalidDataException(
                "La identidad de la estación cambió antes de completar el aprovisionamiento cloud.");
        }

        var principal = await LoadPrincipalAsync(grant.PrincipalUserId, cancellationToken)
            .ConfigureAwait(false);
        if (principal is null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(grant.Proof),
                Encoding.UTF8.GetBytes(CreateProof(grant, principal.PasswordHash))))
        {
            throw new UnauthorizedAccessException(
                "La autorización no corresponde al Administrador Principal protegido.");
        }
    }

    private async Task<PrincipalRecord?> LoadPrincipalAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await context.Usuarios
            .AsNoTracking()
            .Where(user =>
                user.UsuarioId == userId &&
                user.Activo &&
                user.Rol == RolUsuario.ADMIN &&
                user.NombreUsuario == ProtectedAdministrator.UserName)
            .Select(user => new PrincipalRecord(
                user.UsuarioId,
                user.NombreUsuario,
                user.PasswordHash))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RecordAttemptAsync(
        int? userId,
        string? userName,
        string eventName,
        string description,
        CancellationToken cancellationToken)
    {
        _eventLog.Write(
            eventName,
            string.Equals(userName, ProtectedAdministrator.UserName, StringComparison.OrdinalIgnoreCase)
                ? ProtectedAdministrator.UserName
                : userName,
            _installationContext.Status.Mode,
            description);
        try
        {
            await _auditService.RegistrarAsync(
                new AuditEntryRequest(
                    userId,
                    TipoBitacora.CONFIGURACION_MODIFICADA,
                    "cloud_backup_provisioning",
                    userId,
                    description),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _eventLog.Write(
                eventName + "_AUDIT_FALLBACK",
                string.Equals(userName, ProtectedAdministrator.UserName, StringComparison.OrdinalIgnoreCase)
                    ? ProtectedAdministrator.UserName
                    : userName,
                _installationContext.Status.Mode,
                "El evento local se conservó; la bitácora de DB no pudo actualizarse: " + ex.GetType().Name + ".");
        }
    }

    private static void ValidateGrantShape(CloudBackupProvisioningAuthorizationGrant grant)
    {
        if (grant.PrincipalUserId <= 0 ||
            grant.InstallationId == Guid.Empty ||
            grant.ExpiresAtUtc <= grant.IssuedAtUtc ||
            grant.ExpiresAtUtc - grant.IssuedAtUtc > GrantLifetime ||
            string.IsNullOrWhiteSpace(grant.Proof) ||
            grant.Proof.Length != 64 ||
            grant.Proof.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("La autorización temporal del aprovisionamiento cloud está dañada.");
        }
    }

    internal static string CreateProof(
        CloudBackupProvisioningAuthorizationGrant grant,
        string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash));
        var payload = string.Join(
            "|",
            grant.PrincipalUserId,
            grant.InstallationId.ToString("D"),
            grant.IssuedAtUtc.UtcTicks,
            grant.ExpiresAtUtc.UtcTicks);
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
    }

    private sealed record PrincipalRecord(int UsuarioId, string NombreUsuario, string PasswordHash);
}

/// <summary>
/// The handoff is metadata plus an HMAC proof, never a credential.  It lives
/// in the launching user's local data root, is atomically replaced, and is
/// deleted before validation so the UAC continuation cannot be replayed.
/// </summary>
internal static class CloudBackupProvisioningHandoffStore
{
    private const int MaximumBytes = 16 * 1024;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static string GetPath(ArteInventariadoPathProvider paths) =>
        Path.Combine(paths.UserDataRoot, "CloudProvisioning", "authorization.json");

    public static void Save(
        ArteInventariadoPathProvider paths,
        CloudBackupProvisioningAuthorizationGrant grant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(grant);
        if (!paths.IsMachineWide)
        {
            throw new InvalidOperationException("La autorización cloud requiere el layout machine-wide.");
        }

        var path = GetPath(paths);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".authorization-{Guid.NewGuid():N}.tmp");
        lock (Sync)
        {
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, grant, SerializerOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                TryDelete(temporary);
            }
        }
    }

    public static CloudBackupProvisioningAuthorizationGrant? Consume(
        ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide)
        {
            throw new InvalidOperationException("La autorización cloud requiere el layout machine-wide.");
        }

        var path = GetPath(paths);
        lock (Sync)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var file = new FileInfo(path);
                if (file.Length <= 0 || file.Length > MaximumBytes)
                {
                    return null;
                }

                var text = File.ReadAllText(path);
                return JsonSerializer.Deserialize<CloudBackupProvisioningAuthorizationGrant>(text, SerializerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
            finally
            {
                TryDelete(path);
            }
        }
    }

    public static void Discard(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (Sync)
        {
            TryDelete(GetPath(paths));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
