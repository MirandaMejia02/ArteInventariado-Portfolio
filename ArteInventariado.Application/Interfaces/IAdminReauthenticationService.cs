namespace ArteInventariado.Application.Interfaces;

public interface IAdminReauthenticationService
{
    Task<int> VerifyCurrentAdminAsync(
        string password,
        CancellationToken cancellationToken = default);
}
