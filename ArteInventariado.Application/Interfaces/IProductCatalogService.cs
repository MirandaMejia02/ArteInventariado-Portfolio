using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IProductCatalogService
{
    Task<ProductCatalogDetail?> GetDetailAsync(int productoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductSummary>> GetByLocationAsync(ArteInventariado.Domain.Enums.UbicacionInventario location, CancellationToken cancellationToken = default);
    Task<ProductSkuSummary> CreateSkuAsync(CreateProductSkuRequest request, CancellationToken cancellationToken = default);
    Task<ProductSkuSummary> UpdateSkuAsync(UpdateProductSkuRequest request, CancellationToken cancellationToken = default);
    Task SetSkuActiveAsync(int productoSkuId, bool active, CancellationToken cancellationToken = default);
    Task DeleteSkuAsync(int productoSkuId, CancellationToken cancellationToken = default);
}
