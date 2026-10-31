using ArteInventariado.Infrastructure.Station;

namespace ArteInventariado.Infrastructure.Services;

public sealed record ProductAssetStoragePaths(
    string AssetsRoot,
    string ProductAssetsDirectory,
    string SkuAssetsDirectory,
    string ThumbnailCacheDirectory,
    string QuarantineDirectory,
    string StagingDirectory)
{
    public static ProductAssetStoragePaths CreateDefault()
        => CreateDefault(ArteInventariadoPathProvider.CreateDefault());

    public static ProductAssetStoragePaths CreateDefault(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var assets = paths.AssetsRoot;
        return new ProductAssetStoragePaths(
            assets,
            Path.Combine(assets, "Products"),
            Path.Combine(assets, "Skus"),
            paths.ThumbnailRoot,
            paths.AssetQuarantineRoot,
            paths.AssetStagingRoot);
    }

    public string ResolveManagedPath(string relativePath)
    {
        var root = Path.GetFullPath(AssetsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(AssetsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La ruta administrada del asset no es segura.");
        }

        return resolved;
    }
}
