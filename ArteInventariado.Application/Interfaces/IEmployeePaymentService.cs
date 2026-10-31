using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IEmployeePaymentService
{
    Task<bool> CanRegisterAsync(CancellationToken cancellationToken = default);

    Task<bool> CanCancelAsync(CancellationToken cancellationToken = default);

    Task<EmployeePaymentRecord> RegisterPaymentAsync(
        RegisterEmployeePaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployeePaymentRecord> CancelPaymentAsync(
        CancelEmployeePaymentRequest request,
        CancellationToken cancellationToken = default);
}
