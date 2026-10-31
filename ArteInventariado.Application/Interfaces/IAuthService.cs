using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string nombreUsuario, string password, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
