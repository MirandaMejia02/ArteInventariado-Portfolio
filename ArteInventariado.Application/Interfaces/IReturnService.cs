using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IReturnService
{
    Task<ReturnResult> RegisterReturnAsync(
        RegisterReturnRequest request,
        CancellationToken cancellationToken = default);
}
