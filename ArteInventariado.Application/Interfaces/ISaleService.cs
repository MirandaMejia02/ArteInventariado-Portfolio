using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISaleService
{
    Task<SaleResult> RegisterSaleAsync(RegisterSaleRequest request, CancellationToken cancellationToken = default);
}
