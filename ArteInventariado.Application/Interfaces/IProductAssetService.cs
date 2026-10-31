using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IProductAssetService
{
    Task<IReadOnlyList<ProductAssetSummary>> GetByProductAsync(int productoId, CancellationToken cancellationToken = default);
    Task<ProductAssetSummary> ImportAsync(ImportProductAssetRequest request, CancellationToken cancellationToken = default);
    Task SetPrimaryAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default);
    Task DeleteAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default);
    Task<string?> GetThumbnailPathAsync(ProductAssetOwnerKind ownerKind, int assetId, int maximumPixels = 400, CancellationToken cancellationToken = default);
    Task<AssetCleanupResult> CleanupOrphansAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default);
}
