using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISaleChangeService
{
    Task<SaleChangeResult> RegisterChangeAsync(
        RegisterSaleChangeRequest request,
        CancellationToken cancellationToken = default);
}
