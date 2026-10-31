using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISupplierService
{
    Task<SupplierDetail> CreateAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default);

    Task<SupplierDetail> UpdateAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default);

    Task SetStateAsync(SupplierStateChangeRequest request, CancellationToken cancellationToken = default);

    Task<SupplierDuplicateCheckResult> FindDuplicatesAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default);

    Task<SupplierProductSummary> AssociateProductAsync(SupplierProductAssociationRequest request, CancellationToken cancellationToken = default);

    Task DeactivateProductAssociationAsync(int productoProveedorId, string motivo, CancellationToken cancellationToken = default);

    Task SetMainProductSupplierAsync(SupplierProductMainRequest request, CancellationToken cancellationToken = default);
}
