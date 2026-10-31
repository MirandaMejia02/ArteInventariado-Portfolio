using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISupplierQueryService
{
    Task<IReadOnlyList<SupplierSummary>> SearchAsync(SupplierFilter filter, CancellationToken cancellationToken = default);

    Task<SupplierDetail?> GetByIdAsync(int proveedorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierProductSummary>> GetProductsAsync(int proveedorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierHistoryEntry>> GetHistoryAsync(int proveedorId, CancellationToken cancellationToken = default);
}
