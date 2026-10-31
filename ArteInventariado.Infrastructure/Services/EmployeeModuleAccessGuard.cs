using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Shared authorization boundary for read-only employee administration.
/// The generic employee permission service grants ADMIN broadly for other
/// modules, therefore this guard enforces EMPLEADOS_VER for delegated admins.
/// </summary>
internal static class EmployeeModuleAccessGuard
{
    public static async Task EnsureAsync(
        AppDbContext db,
        ICurrentSession session,
        CancellationToken cancellationToken)
    {
        var current = session.UsuarioActual
            ?? throw new InvalidOperationException("Debe iniciar sesión para consultar empleados.");

        var user = await db.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.UsuarioId == current.UsuarioId && u.Activo,
                cancellationToken);

        if (user is null || user.Rol != RolUsuario.ADMIN)
        {
            throw new InvalidOperationException("No tiene permiso para consultar empleados.");
        }

        if (ProtectedAdministrator.IsProtectedAdministrator(current))
        {
            return;
        }

        var allowed = await db.UsuarioPermisos
            .AsNoTracking()
            .AnyAsync(
                p => p.UsuarioId == current.UsuarioId &&
                    p.Permiso == PermisoEmpleado.EMPLEADOS_VER &&
                    p.Habilitado,
                cancellationToken);

        if (!allowed)
        {
            throw new InvalidOperationException("No tiene permiso para consultar empleados.");
        }
    }

    /// <summary>
    /// Authorization boundary for administrative employee payments. Unlike
    /// the generic permission service, delegated ADMIN users must hold the
    /// exact payment permission requested; EMPLEADOS_VER is not implicit.
    /// </summary>
    public static async Task EnsurePaymentAsync(
        AppDbContext db,
        ICurrentSession session,
        PermisoEmpleado requiredPermission,
        CancellationToken cancellationToken)
    {
        if (requiredPermission is not
            PermisoEmpleado.EMPLEADOS_PAGOS_VER and
            not PermisoEmpleado.EMPLEADOS_PAGOS_REGISTRAR and
            not PermisoEmpleado.EMPLEADOS_PAGOS_ANULAR)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredPermission));
        }

        var current = session.UsuarioActual
            ?? throw new InvalidOperationException("Debe iniciar sesión para administrar pagos de empleados.");

        var user = await db.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.UsuarioId == current.UsuarioId && u.Activo,
                cancellationToken);

        if (user is null || user.Rol != RolUsuario.ADMIN)
        {
            throw new InvalidOperationException("No tiene permiso para administrar pagos de empleados.");
        }

        if (ProtectedAdministrator.IsProtectedAdministrator(current))
        {
            return;
        }

        var allowed = await db.UsuarioPermisos
            .AsNoTracking()
            .AnyAsync(
                p => p.UsuarioId == current.UsuarioId &&
                    p.Permiso == requiredPermission &&
                    p.Habilitado,
                cancellationToken);

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"No tiene permiso para: {EmployeePermissionCatalog.GetDefinition(requiredPermission).Nombre}.");
        }
    }
}
