using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IPosCatalogService
{
    Task<IReadOnlyList<PosSkuSaleItem>> SearchAsync(
        PosSkuCatalogFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<PosSkuSaleItem?> GetBySkuIdAsync(
        int productoSkuId,
        CancellationToken cancellationToken = default);
}
