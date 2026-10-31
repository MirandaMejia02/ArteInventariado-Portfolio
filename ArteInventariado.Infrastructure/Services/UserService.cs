using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UsuarioEntity = ArteInventariado.Domain.Entities.Usuario;

namespace ArteInventariado.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public UserService(IDbContextFactory<AppDbContext> contextFactory, IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
    }

    internal UserService(AppDbContext context, IAuditService auditService)
    {
        _legacyContext = context;
        _auditService = auditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion de usuarios.");

    public async Task<IReadOnlyList<UserSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        return await Context.Usuarios
            .AsNoTracking()
            .OrderBy(u => u.NombreUsuario)
            .Select(u => new UserSummary(
                u.UsuarioId,
                u.NombreCompleto,
                u.NombreUsuario,
                u.Rol,
                u.Activo,
                u.FechaCreacion,
                u.UltimoLogin))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserSummary?> GetByIdAsync(int usuarioId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        return await Context.Usuarios
            .AsNoTracking()
            .Where(u => u.UsuarioId == usuarioId)
            .Select(u => new UserSummary(
                u.UsuarioId,
                u.NombreCompleto,
                u.NombreUsuario,
                u.Rol,
                u.Activo,
                u.FechaCreacion,
                u.UltimoLogin))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserSummary> CreateAsync(
        CreateUserRequest request,
        int? usuarioActualId = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        ValidarRequestCreacion(request);

        var actor = await GetActorAsync(usuarioActualId, cancellationToken);
        await EnsureCanCreateUserAsync(actor, request.Rol, usuarioActualId, cancellationToken);

        var nombreUsuario = request.NombreUsuario.Trim();
        if (IsProtectedAdminName(nombreUsuario))
        {
            await RejectUserManagementAsync(
                usuarioActualId,
                null,
                "Intento bloqueado de crear manualmente el usuario tecnico 'admin'.",
                "El usuario admin esta reservado y no puede crearse manualmente.",
                cancellationToken);
        }

        var nombreUsuarioNormalizado = nombreUsuario.ToUpper();
        var existeUsuario = await Context.Usuarios
            .AnyAsync(u => u.NombreUsuario.ToUpper() == nombreUsuarioNormalizado, cancellationToken);

        if (existeUsuario)
        {
            throw new InvalidOperationException("Ya existe un usuario con ese nombre de usuario.");
        }

        var usuario = new Domain.Entities.Usuario
        {
            NombreCompleto = request.NombreCompleto.Trim(),
            NombreUsuario = nombreUsuario,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Rol = request.Rol,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync(cancellationToken);

        if (usuario.Rol == RolUsuario.EMPLEADO)
        {
            await AddMissingRecommendedPermissionsAsync(usuario.UsuarioId, cancellationToken);
        }

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioActualId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "usuarios",
                usuario.UsuarioId,
                $"Usuario '{usuario.NombreUsuario}' creado con rol {usuario.Rol}."),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ToSummary(usuario.UsuarioId, usuario.NombreCompleto, usuario.NombreUsuario, usuario.Rol, usuario.Activo, usuario.FechaCreacion, usuario.UltimoLogin);
    }

    public async Task<UserSummary> UpdateAsync(
        UpdateUserRequest request,
        int? usuarioActualId = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        ValidarRequestActualizacion(request);

        var usuario = await Context.Usuarios
            .FirstOrDefaultAsync(u => u.UsuarioId == request.UsuarioId, cancellationToken);

        if (usuario is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        var actor = await GetActorAsync(usuarioActualId, cancellationToken);
        await EnsureCanModifyUserAsync(actor, usuario, request.Rol, usuarioActualId, cancellationToken);

        if (usuario.Activo &&
            usuario.Rol == RolUsuario.ADMIN &&
            request.Rol != RolUsuario.ADMIN &&
            await EsUnicoAdministradorActivoAsync(usuario.UsuarioId, cancellationToken))
        {
            throw new InvalidOperationException("No se puede quitar el rol ADMIN al unico administrador activo.");
        }

        var rolAnterior = usuario.Rol;
        var nombreAnterior = usuario.NombreCompleto;

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        usuario.NombreCompleto = request.NombreCompleto.Trim();
        usuario.Rol = request.Rol;

        await Context.SaveChangesAsync(cancellationToken);

        if (usuario.Rol == RolUsuario.EMPLEADO)
        {
            await AddMissingRecommendedPermissionsAsync(usuario.UsuarioId, cancellationToken);
        }

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioActualId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "usuarios",
                usuario.UsuarioId,
                $"Usuario '{usuario.NombreUsuario}' editado. Nombre: '{nombreAnterior}' -> '{usuario.NombreCompleto}'. Rol: {rolAnterior} -> {usuario.Rol}."),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ToSummary(usuario.UsuarioId, usuario.NombreCompleto, usuario.NombreUsuario, usuario.Rol, usuario.Activo, usuario.FechaCreacion, usuario.UltimoLogin);
    }

    public async Task SetActiveAsync(
        int usuarioId,
        bool activo,
        int? usuarioActualId = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var usuario = await Context.Usuarios
            .FirstOrDefaultAsync(u => u.UsuarioId == usuarioId, cancellationToken);

        if (usuario is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        var actor = await GetActorAsync(usuarioActualId, cancellationToken);
        await EnsureCanModifyUserAsync(actor, usuario, usuario.Rol, usuarioActualId, cancellationToken);

        if (usuario.Activo == activo)
        {
            return;
        }

        if (!activo && IsProtectedAdmin(usuario))
        {
            await RejectUserManagementAsync(
                usuarioActualId,
                usuario.UsuarioId,
                "Intento bloqueado de desactivar el administrador principal 'admin'.",
                "No se puede desactivar el administrador principal.",
                cancellationToken);
        }

        if (!activo &&
            usuario.Rol == RolUsuario.ADMIN &&
            await EsUnicoAdministradorActivoAsync(usuario.UsuarioId, cancellationToken))
        {
            throw new InvalidOperationException("No se puede desactivar al unico administrador activo.");
        }

        var estadoAnterior = usuario.Activo;
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        usuario.Activo = activo;
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioActualId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "usuarios",
                usuario.UsuarioId,
                $"Usuario '{usuario.NombreUsuario}' cambiado de activo={estadoAnterior} a activo={activo}."),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(
        int usuarioId,
        string nuevoPassword,
        int? usuarioActualId = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        PasswordPolicy.EnsureValid(nuevoPassword, nameof(nuevoPassword));

        var usuario = await Context.Usuarios
            .FirstOrDefaultAsync(u => u.UsuarioId == usuarioId, cancellationToken);

        if (usuario is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        var actor = await GetActorAsync(usuarioActualId, cancellationToken);
        await EnsureCanModifyUserAsync(actor, usuario, usuario.Rol, usuarioActualId, cancellationToken);

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(nuevoPassword);
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioActualId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "usuarios",
                usuario.UsuarioId,
                $"Contrasena actualizada para usuario '{usuario.NombreUsuario}'."),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static UserSummary ToSummary(
        int usuarioId,
        string nombreCompleto,
        string nombreUsuario,
        RolUsuario rol,
        bool activo,
        DateTime fechaCreacion,
        DateTime? ultimoLogin)
    {
        return new UserSummary(usuarioId, nombreCompleto, nombreUsuario, rol, activo, fechaCreacion, ultimoLogin);
    }

    private async Task<UsuarioEntity?> GetActorAsync(int? usuarioActualId, CancellationToken cancellationToken)
    {
        if (usuarioActualId is null)
        {
            return null;
        }

        var actor = await Context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UsuarioId == usuarioActualId, cancellationToken);

        if (actor is null)
        {
            throw new InvalidOperationException("La sesion actual no corresponde a un usuario valido.");
        }

        return actor;
    }

    private async Task EnsureCanCreateUserAsync(
        UsuarioEntity? actor,
        RolUsuario requestedRole,
        int? usuarioActualId,
        CancellationToken cancellationToken)
    {
        await EnsureCanManageUsersAsync(actor, usuarioActualId, cancellationToken);

        if (requestedRole == RolUsuario.ADMIN && !IsTechnicalAdmin(actor))
        {
            await RejectUserManagementAsync(
                usuarioActualId,
                null,
                "Intento bloqueado de crear un usuario ADMIN por un administrador no tecnico.",
                "Solo el administrador principal puede crear usuarios ADMIN.",
                cancellationToken);
        }
    }

    private async Task EnsureCanModifyUserAsync(
        UsuarioEntity? actor,
        UsuarioEntity target,
        RolUsuario requestedRole,
        int? usuarioActualId,
        CancellationToken cancellationToken)
    {
        await EnsureCanManageUsersAsync(actor, usuarioActualId, cancellationToken);

        if (IsProtectedAdmin(target) && !IsTechnicalAdmin(actor))
        {
            await RejectUserManagementAsync(
                usuarioActualId,
                target.UsuarioId,
                "Intento bloqueado de modificar el administrador principal 'admin'.",
                "No tiene permiso para modificar el administrador principal.",
                cancellationToken);
        }

        if (IsProtectedAdmin(target) && requestedRole != RolUsuario.ADMIN)
        {
            await RejectUserManagementAsync(
                usuarioActualId,
                target.UsuarioId,
                "Intento bloqueado de quitar el rol ADMIN al administrador principal 'admin'.",
                "No se puede quitar el rol ADMIN al administrador principal.",
                cancellationToken);
        }

        if (!IsTechnicalAdmin(actor) &&
            (target.Rol == RolUsuario.ADMIN || requestedRole == RolUsuario.ADMIN))
        {
            await RejectUserManagementAsync(
                usuarioActualId,
                target.UsuarioId,
                "Intento bloqueado de administrar usuarios ADMIN por un administrador no tecnico.",
                "Solo el administrador principal puede administrar usuarios ADMIN.",
                cancellationToken);
        }
    }

    private async Task EnsureCanManageUsersAsync(
        UsuarioEntity? actor,
        int? usuarioActualId,
        CancellationToken cancellationToken)
    {
        if (actor is null || !actor.Activo || actor.Rol != RolUsuario.ADMIN)
        {
            await RejectUserManagementAsync(
                usuarioActualId,
                null,
                "Intento bloqueado de administrar usuarios sin permisos de ADMIN.",
                "No tiene permiso para administrar usuarios.",
                cancellationToken);
        }
    }

    private async Task RejectUserManagementAsync(
        int? usuarioActualId,
        int? targetUsuarioId,
        string auditDescription,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioActualId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "usuarios",
                targetUsuarioId,
                auditDescription),
            cancellationToken);

        throw new InvalidOperationException(errorMessage);
    }

    private static bool IsTechnicalAdmin(UsuarioEntity? usuario)
    {
        return usuario is not null && IsProtectedAdmin(usuario);
    }

    private static bool IsProtectedAdmin(UsuarioEntity usuario)
    {
        return IsProtectedAdminName(usuario.NombreUsuario);
    }

    private static bool IsProtectedAdminName(string nombreUsuario)
    {
        return ProtectedAdministrator.IsUserName(nombreUsuario);
    }

    private static void ValidarRequestCreacion(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NombreCompleto))
        {
            throw new ArgumentException("El nombre completo es obligatorio.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.NombreUsuario))
        {
            throw new ArgumentException("El nombre de usuario es obligatorio.", nameof(request));
        }

        PasswordPolicy.EnsureValid(request.Password, nameof(request.Password));

        if (request.Rol is not RolUsuario.ADMIN and not RolUsuario.EMPLEADO)
        {
            throw new ArgumentException("El rol debe ser ADMIN o EMPLEADO.", nameof(request));
        }
    }

    private static void ValidarRequestActualizacion(UpdateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NombreCompleto))
        {
            throw new ArgumentException("El nombre completo es obligatorio.", nameof(request));
        }

        if (request.Rol is not RolUsuario.ADMIN and not RolUsuario.EMPLEADO)
        {
            throw new ArgumentException("El rol debe ser ADMIN o EMPLEADO.", nameof(request));
        }
    }

    private async Task<bool> EsUnicoAdministradorActivoAsync(int usuarioId, CancellationToken cancellationToken)
    {
        var existeOtroAdministrador = await Context.Usuarios
            .Where(u => u.Activo && u.Rol == RolUsuario.ADMIN && u.UsuarioId != usuarioId)
            .AnyAsync(cancellationToken);

        return !existeOtroAdministrador;
    }

    private async Task AddMissingRecommendedPermissionsAsync(int usuarioId, CancellationToken cancellationToken)
    {
        var existing = await Context.UsuarioPermisos
            .Where(p => p.UsuarioId == usuarioId)
            .Select(p => p.Permiso)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var permiso in EmployeePermissionCatalog.AllPermissions)
        {
            if (existingSet.Contains(permiso))
            {
                continue;
            }

            Context.UsuarioPermisos.Add(new UsuarioPermiso
            {
                UsuarioId = usuarioId,
                Permiso = permiso,
                Habilitado = EmployeePermissionCatalog.RecommendedNewEmployeePermissions.Contains(permiso),
                FechaActualizacion = now
            });
        }

        await Context.SaveChangesAsync(cancellationToken);
    }
}
