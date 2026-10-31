using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISaleCancellationService
{
    Task<SaleCancellationResult> CancelSaleAsync(
        RegisterSaleCancellationRequest request,
        CancellationToken cancellationToken = default);
}
