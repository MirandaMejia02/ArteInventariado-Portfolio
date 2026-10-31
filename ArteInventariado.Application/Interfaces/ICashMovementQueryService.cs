using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICashMovementQueryService
{
    Task<CashMovementSearchResult> SearchAsync(
        CashMovementFilter filter,
        CancellationToken cancellationToken = default);
}
