using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAccountService
{
    Task ChangeCurrentPasswordAsync(
        ChangeCurrentPasswordRequest request,
        CancellationToken cancellationToken = default);

}
