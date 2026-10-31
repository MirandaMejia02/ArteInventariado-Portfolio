using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<UserSummary?> GetByIdAsync(int usuarioId, CancellationToken cancellationToken = default);

    Task<UserSummary> CreateAsync(CreateUserRequest request, int? usuarioActualId = null, CancellationToken cancellationToken = default);

    Task<UserSummary> UpdateAsync(UpdateUserRequest request, int? usuarioActualId = null, CancellationToken cancellationToken = default);

    Task SetActiveAsync(int usuarioId, bool activo, int? usuarioActualId = null, CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(int usuarioId, string nuevoPassword, int? usuarioActualId = null, CancellationToken cancellationToken = default);
}
