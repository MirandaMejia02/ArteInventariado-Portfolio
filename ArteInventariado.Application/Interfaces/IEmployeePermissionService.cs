using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Interfaces;

public interface IEmployeePermissionService
{
    Task<bool> HasPermissionAsync(
        PermisoEmpleado permiso,
        int? usuarioId = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyPermissionAsync(
        IReadOnlyCollection<PermisoEmpleado> permisos,
        int? usuarioId = null,
        CancellationToken cancellationToken = default);

    Task EnsurePermissionAsync(
        PermisoEmpleado permiso,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(
        int usuarioId,
        CancellationToken cancellationToken = default);

    Task UpdatePermissionsAsync(
        UpdateEmployeePermissionsRequest request,
        CancellationToken cancellationToken = default);

    Task RestoreRecommendedTemplateAsync(
        int usuarioId,
        CancellationToken cancellationToken = default);

    Task ApplySafeOperationalTemplateAsync(
        int usuarioId,
        CancellationToken cancellationToken = default);

    Task EnsureRecommendedPermissionsForEmployeeAsync(
        int usuarioId,
        CancellationToken cancellationToken = default);
}
