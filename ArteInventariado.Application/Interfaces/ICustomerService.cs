using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICustomerService
{
    Task<CustomerDetail> CreateAsync(CustomerSaveRequest request, CancellationToken cancellationToken = default);

    Task<CustomerDetail> UpdateAsync(CustomerSaveRequest request, CancellationToken cancellationToken = default);

    Task SetStateAsync(CustomerStateChangeRequest request, CancellationToken cancellationToken = default);

    Task<CustomerDuplicateCheckResult> FindDuplicatesAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default);
}
