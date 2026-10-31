using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Interfaces;

public interface IProductService
{
    Task<IReadOnlyList<ProductSummary>> GetAllAsync(ProductFilter? filter = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSummary>> GetAvailableForSaleAsync(ProductFilter? filter = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSummary>> SearchByNameAsync(string nombre, CancellationToken cancellationToken = default);

    Task<ProductSummary?> GetByCodigoInternoAsync(string codigoInterno, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSummary>> GetLowStockAsync(CancellationToken cancellationToken = default);

    Task<ProductSummary> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductSummary> UpdateAsync(UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task SetEstadoAsync(int productoId, EstadoProducto estado, CancellationToken cancellationToken = default);

    Task DeleteAsync(int productoId, CancellationToken cancellationToken = default);
}
