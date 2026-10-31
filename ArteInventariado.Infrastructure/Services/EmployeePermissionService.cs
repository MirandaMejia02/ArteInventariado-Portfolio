using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class EmployeePermissionService : IEmployeePermissionService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public EmployeePermissionService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    internal EmployeePermissionService(
        AppDbContext context,
        ICurrentSession currentSession,
        IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion de permisos.");

    public async Task<bool> HasPermissionAsync(
        PermisoEmpleado permiso,
        int? usuarioId = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        ValidateAssignablePermission(permiso);

        var userId = usuarioId ?? _currentSession.UsuarioActual?.UsuarioId;
        if (userId is null)
        {
            return false;
        }

        var usuario = await Context.Usuarios
            .AsNoTracking()
            .Where(u => u.UsuarioId == userId.Value)
            .Select(u => new { u.UsuarioId, u.Rol, u.Activo })
            .FirstOrDefaultAsync(cancellationToken);

        if (usuario is null || !usuario.Activo)
        {
            return false;
        }

        if (usuario.Rol == RolUsuario.ADMIN)
        {
            return true;
        }

        if (usuario.Rol != RolUsuario.EMPLEADO)
        {
            return false;
        }

        return await Context.UsuarioPermisos
            .AsNoTracking()
            .AnyAsync(
                p => p.UsuarioId == usuario.UsuarioId &&
                    p.Permiso == permiso &&
                    p.Habilitado,
                cancellationToken);
    }

    public async Task<bool> HasAnyPermissionAsync(
        IReadOnlyCollection<PermisoEmpleado> permisos,
        int? usuarioId = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        if (permisos.Count == 0)
        {
            return false;
        }

        foreach (var permiso in permisos)
        {
            ValidateAssignablePermission(permiso);
        }

        var userId = usuarioId ?? _currentSession.UsuarioActual?.UsuarioId;
        if (userId is null)
        {
            return false;
        }

        var usuario = await Context.Usuarios
            .AsNoTracking()
            .Where(u => u.UsuarioId == userId.Value)
            .Select(u => new { u.UsuarioId, u.Rol, u.Activo })
            .FirstOrDefaultAsync(cancellationToken);

        if (usuario is null || !usuario.Activo)
        {
            return false;
        }

        if (usuario.Rol == RolUsuario.ADMIN)
        {
            return true;
        }

        if (usuario.Rol != RolUsuario.EMPLEADO)
        {
            return false;
        }

        return await Context.UsuarioPermisos
            .AsNoTracking()
            .AnyAsync(
                p => p.UsuarioId == usuario.UsuarioId &&
                    permisos.Contains(p.Permiso) &&
                    p.Habilitado,
                cancellationToken);
    }

    public async Task EnsurePermissionAsync(
        PermisoEmpleado permiso,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        if (!await HasPermissionAsync(permiso, cancellationToken: cancellationToken))
        {
            var definition = EmployeePermissionCatalog.GetDefinition(permiso);
            throw new InvalidOperationException($"No tiene permiso para: {definition.Nombre}.");
        }
    }

    public async Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(
        int usuarioId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsureCurrentAdminAsync(cancellationToken);
        await EnsureTargetEmployeeExistsAsync(usuarioId, requireActive: true, cancellationToken);

        var storedPermissionRows = await Context.UsuarioPermisos
            .AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.FechaActualizacion)
            .ThenByDescending(p => p.UsuarioPermisoId)
            .ToListAsync(cancellationToken);
        var storedPermissions = storedPermissionRows
            .GroupBy(p => p.Permiso)
            .ToDictionary(group => group.Key, group => group.First().Habilitado);

        return EmployeePermissionCatalog.All
            .Select(definition => new EmployeePermissionState(
                definition.Permiso,
                definition.Grupo,
                definition.Nombre,
                definition.Descripcion,
                storedPermissions.GetValueOrDefault(definition.Permiso),
                definition.FuncionFutura))
            .ToList();
    }

    public async Task UpdatePermissionsAsync(
        UpdateEmployeePermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsureCurrentAdminAsync(cancellationToken);
        await EnsureTargetEmployeeExistsAsync(request.UsuarioId, requireActive: true, cancellationToken);
        var desiredStates = ValidateAndBuildCompleteState(request.Permisos);

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await NormalizeDuplicatePermissionsAsync(request.UsuarioId, cancellationToken);
        var changes = await ApplyPermissionStatesAsync(
            request.UsuarioId,
            desiredStates,
            cancellationToken);

        if (changes.Count > 0)
        {
            await RegisterPermissionAuditAsync(request.UsuarioId, changes, "Permisos de empleado actualizados.", cancellationToken);
        }

        await VerifyPersistedPermissionsAsync(request.UsuarioId, desiredStates, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RestoreRecommendedTemplateAsync(
        int usuarioId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsureCurrentAdminAsync(cancellationToken);
        await EnsureTargetEmployeeExistsAsync(usuarioId, requireActive: true, cancellationToken);

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await NormalizeDuplicatePermissionsAsync(usuarioId, cancellationToken);
        var changes = await ApplyTemplateAsync(
            usuarioId,
            EmployeePermissionCatalog.RecommendedNewEmployeePermissions,
            cancellationToken);

        if (changes.Count > 0)
        {
            await RegisterPermissionAuditAsync(usuarioId, changes, "Plantilla recomendada de permisos restaurada.", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApplySafeOperationalTemplateAsync(
        int usuarioId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsureCurrentAdminAsync(cancellationToken);
        await EnsureTargetEmployeeExistsAsync(usuarioId, requireActive: true, cancellationToken);

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await NormalizeDuplicatePermissionsAsync(usuarioId, cancellationToken);
        var changes = await ApplyTemplateAsync(
            usuarioId,
            EmployeePermissionCatalog.SafeOperationalPermissions,
            cancellationToken);

        if (changes.Count > 0)
        {
            await RegisterPermissionAuditAsync(usuarioId, changes, "Plantilla operativa segura aplicada.", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task EnsureRecommendedPermissionsForEmployeeAsync(
        int usuarioId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var usuario = await Context.Usuarios
            .AsNoTracking()
            .Where(u => u.UsuarioId == usuarioId)
            .Select(u => new { u.Rol })
            .FirstOrDefaultAsync(cancellationToken);

        if (usuario is null || usuario.Rol != RolUsuario.EMPLEADO)
        {
            return;
        }

        var existingPermissions = await Context.UsuarioPermisos
            .Where(p => p.UsuarioId == usuarioId)
            .Select(p => p.Permiso)
            .ToListAsync(cancellationToken);
        var existingSet = existingPermissions.ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var permission in EmployeePermissionCatalog.AllPermissions)
        {
            if (existingSet.Contains(permission))
            {
                continue;
            }

            Context.UsuarioPermisos.Add(new UsuarioPermiso
            {
                UsuarioId = usuarioId,
                Permiso = permission,
                Habilitado = EmployeePermissionCatalog.RecommendedNewEmployeePermissions.Contains(permission),
                FechaActualizacion = now
            });
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EmployeePermissionAuditChange>> ApplyTemplateAsync(
        int usuarioId,
        IReadOnlySet<PermisoEmpleado> enabledPermissions,
        CancellationToken cancellationToken)
    {
        var desired = EmployeePermissionCatalog.BuildTemplateState(enabledPermissions);

        return await ApplyPermissionStatesAsync(usuarioId, desired, cancellationToken);
    }

    private async Task<IReadOnlyList<EmployeePermissionAuditChange>> ApplyPermissionStatesAsync(
        int usuarioId,
        IReadOnlyDictionary<PermisoEmpleado, bool> desiredStates,
        CancellationToken cancellationToken)
    {
        var permissions = await Context.UsuarioPermisos
            .Where(p => p.UsuarioId == usuarioId)
            .ToDictionaryAsync(p => p.Permiso, cancellationToken);
        var now = DateTime.UtcNow;
        var changes = new List<EmployeePermissionAuditChange>();

        foreach (var (permiso, enabled) in desiredStates)
        {
            ValidateAssignablePermission(permiso);

            if (!permissions.TryGetValue(permiso, out var stored))
            {
                stored = new UsuarioPermiso
                {
                    UsuarioId = usuarioId,
                    Permiso = permiso,
                    Habilitado = enabled,
                    FechaActualizacion = now
                };
                Context.UsuarioPermisos.Add(stored);
                permissions[permiso] = stored;

                if (enabled)
                {
                    changes.Add(new EmployeePermissionAuditChange(permiso, false, true));
                }

                continue;
            }

            if (stored.Habilitado == enabled)
            {
                continue;
            }

            changes.Add(new EmployeePermissionAuditChange(permiso, stored.Habilitado, enabled));
            stored.Habilitado = enabled;
            stored.FechaActualizacion = now;
        }

        await Context.SaveChangesAsync(cancellationToken);
        return changes;
    }

    private async Task VerifyPersistedPermissionsAsync(
        int usuarioId,
        IReadOnlyDictionary<PermisoEmpleado, bool> desiredStates,
        CancellationToken cancellationToken)
    {
        Context.ChangeTracker.Clear();
        var persisted = await Context.UsuarioPermisos
            .AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);

        var duplicates = persisted
            .GroupBy(p => p.Permiso)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException("No se pudo verificar la persistencia porque existen permisos duplicados.");
        }

        var persistedStates = persisted.ToDictionary(p => p.Permiso, p => p.Habilitado);
        foreach (var permission in EmployeePermissionCatalog.AllPermissions)
        {
            if (!persistedStates.TryGetValue(permission, out var persistedValue) ||
                persistedValue != desiredStates[permission])
            {
                throw new InvalidOperationException("No se pudo verificar la persistencia de permisos.");
            }
        }
    }

    private async Task NormalizeDuplicatePermissionsAsync(
        int usuarioId,
        CancellationToken cancellationToken)
    {
        var permissions = await Context.UsuarioPermisos
            .Where(p => p.UsuarioId == usuarioId)
            .OrderBy(p => p.Permiso)
            .ThenByDescending(p => p.FechaActualizacion)
            .ThenByDescending(p => p.UsuarioPermisoId)
            .ToListAsync(cancellationToken);

        var duplicates = permissions
            .GroupBy(p => p.Permiso)
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicates.Count == 0)
        {
            return;
        }

        foreach (var group in duplicates)
        {
            var keep = group.First();
            foreach (var duplicate in group.Skip(1))
            {
                Context.UsuarioPermisos.Remove(duplicate);
            }

            keep.FechaActualizacion = DateTime.UtcNow;
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    private async Task RegisterPermissionAuditAsync(
        int targetUserId,
        IReadOnlyList<EmployeePermissionAuditChange> changes,
        string description,
        CancellationToken cancellationToken)
    {
        var adminId = _currentSession.UsuarioActual?.UsuarioId;
        var target = await Context.Usuarios
            .AsNoTracking()
            .Where(u => u.UsuarioId == targetUserId)
            .Select(u => u.NombreUsuario)
            .FirstAsync(cancellationToken);
        var details = string.Join(
            "; ",
            changes.Select(c => $"{c.Permiso}: {c.Antes} -> {c.Despues}"));

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                adminId,
                TipoBitacora.PERMISOS_MODIFICADOS,
                "usuario_permisos",
                targetUserId,
                $"{description} Empleado: {target}. Cambios: {details}",
                DatosAntes: string.Join("; ", changes.Select(c => $"{c.Permiso}={c.Antes}")),
                DatosDespues: string.Join("; ", changes.Select(c => $"{c.Permiso}={c.Despues}"))),
            cancellationToken);
    }

    private async Task EnsureCurrentAdminAsync(CancellationToken cancellationToken)
    {
        var currentUserId = _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");

        var isAdmin = await Context.Usuarios
            .AsNoTracking()
            .AnyAsync(
                u => u.UsuarioId == currentUserId &&
                    u.Activo &&
                    u.Rol == RolUsuario.ADMIN,
                cancellationToken);

        if (!isAdmin)
        {
            throw new InvalidOperationException("Solo ADMIN puede administrar permisos de empleados.");
        }
    }

    private async Task EnsureTargetEmployeeExistsAsync(
        int usuarioId,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        var usuario = await Context.Usuarios
            .AsNoTracking()
            .Where(u => u.UsuarioId == usuarioId)
            .Select(u => new { u.Rol, u.Activo })
            .FirstOrDefaultAsync(cancellationToken);

        if (usuario is null || usuario.Rol != RolUsuario.EMPLEADO)
        {
            throw new InvalidOperationException("Solo se pueden administrar permisos de usuarios EMPLEADO.");
        }

        if (requireActive && !usuario.Activo)
        {
            throw new InvalidOperationException("El empleado seleccionado ya no esta activo.");
        }
    }

    private static IReadOnlyDictionary<PermisoEmpleado, bool> ValidateAndBuildCompleteState(
        IReadOnlyList<EmployeePermissionUpdate> updates)
    {
        if (updates.Count == 0)
        {
            throw new ArgumentException("Debe enviar al menos un permiso.", nameof(updates));
        }

        var distinct = new HashSet<PermisoEmpleado>();
        var states = new Dictionary<PermisoEmpleado, bool>();
        foreach (var update in updates)
        {
            ValidateAssignablePermission(update.Permiso);
            if (!distinct.Add(update.Permiso))
            {
                throw new ArgumentException("No se puede enviar el mismo permiso mas de una vez.", nameof(updates));
            }

            states[update.Permiso] = update.Habilitado;
        }

        var missing = EmployeePermissionCatalog.AllPermissions
            .Where(permission => !states.ContainsKey(permission))
            .ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException("Debe enviar todos los permisos administrables.", nameof(updates));
        }

        var validationErrors = EmployeePermissionCatalog.ValidateStateCombination(states);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException($"La combinacion de permisos no es valida. {string.Join(" ", validationErrors)}");
        }

        return states;
    }

    private static void ValidateAssignablePermission(PermisoEmpleado permiso)
    {
        if (!Enum.IsDefined(permiso) || !EmployeePermissionCatalog.IsAssignable(permiso))
        {
            throw new ArgumentException("El permiso no es asignable a EMPLEADO.", nameof(permiso));
        }
    }
}
