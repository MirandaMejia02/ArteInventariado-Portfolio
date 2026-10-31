using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IProductProfitabilityQueryService
{
    Task<ProductProfitabilityReport> QueryAsync(
        ProductProfitabilityRequest request,
        CancellationToken cancellationToken = default);

    Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default);
}
