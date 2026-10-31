using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAccountPaymentService
{
    Task<RegisterAccountPaymentResult> RegisterPaymentAsync(
        RegisterAccountPaymentRequest request,
        CancellationToken cancellationToken = default);
}
