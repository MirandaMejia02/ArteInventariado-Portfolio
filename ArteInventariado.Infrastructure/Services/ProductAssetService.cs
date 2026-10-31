using System.Security.Cryptography;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ArteInventariado.Infrastructure.Services;

public sealed class ProductAssetService : IProductAssetService
{
    internal const long MaximumSourceBytes = 12L * 1024 * 1024;
    internal const int MaximumCanonicalPixels = 2048;
    internal const int MinimumThumbnailPixels = 64;
    internal const int MaximumThumbnailPixels = 800;

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService? _permissions;
    private readonly IInstallationContext? _installation;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;
    private readonly ProductAssetStoragePaths _paths;

    [ActivatorUtilitiesConstructor]
    public ProductAssetService(
        IDbContextFactory<AppDbContext> contextFactory,
        IEmployeePermissionService permissions,
        IInstallationContext installation,
        ICurrentSession session,
        IAuditService audit,
        ProductAssetStoragePaths paths)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _installation = installation;
        _session = session;
        _audit = audit;
        _paths = paths;
    }

    internal ProductAssetService(
        AppDbContext context,
        ICurrentSession session,
        IAuditService audit,
        ProductAssetStoragePaths paths)
    {
        _legacyContext = context;
        _session = session;
        _audit = audit;
        _paths = paths;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para assets de producto.");

    public async Task<IReadOnlyList<ProductAssetSummary>> GetByProductAsync(
        int productoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var productAssets = await Context.ProductoAssets.AsNoTracking()
            .Where(asset => asset.ProductoId == productoId)
            .OrderByDescending(asset => asset.EsPrincipal).ThenBy(asset => asset.Orden)
            .Select(asset => new ProductAssetSummary(
                asset.ProductoAssetId, ProductAssetOwnerKind.Product, asset.ProductoId, asset.ProductoId, null,
                asset.StorageKey, asset.RutaRelativa, asset.NombreOriginal, asset.MimeType, asset.Extension,
                asset.SizeBytes, asset.Width, asset.Height, asset.Sha256, asset.Orden, asset.EsPrincipal, asset.FechaCreacion))
            .ToListAsync(cancellationToken);
        var skuAssets = await Context.ProductoSkuAssets.AsNoTracking()
            .Where(asset => asset.ProductoSku.ProductoId == productoId)
            .OrderBy(asset => asset.ProductoSkuId).ThenByDescending(asset => asset.EsPrincipal).ThenBy(asset => asset.Orden)
            .Select(asset => new ProductAssetSummary(
                asset.ProductoSkuAssetId, ProductAssetOwnerKind.Sku, asset.ProductoSkuId,
                asset.ProductoSku.ProductoId, asset.ProductoSkuId, asset.StorageKey, asset.RutaRelativa,
                asset.NombreOriginal, asset.MimeType, asset.Extension, asset.SizeBytes, asset.Width, asset.Height,
                asset.Sha256, asset.Orden, asset.EsPrincipal, asset.FechaCreacion))
            .ToListAsync(cancellationToken);
        return productAssets.Concat(skuAssets).ToList();
    }

    public async Task<ProductAssetSummary> ImportAsync(
        ImportProductAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureImageWriteAllowedAsync("Importar imagen de producto", cancellationToken);
        if (!File.Exists(request.SourcePath))
            throw new FileNotFoundException("La imagen seleccionada ya no existe.", request.SourcePath);
        var source = new FileInfo(request.SourcePath);
        if (source.Length is <= 0 or > MaximumSourceBytes)
            throw new InvalidDataException("La imagen debe pesar como maximo 12 MB.");

        cancellationToken.ThrowIfCancellationRequested();
        int width;
        int height;
        SKEncodedImageFormat encodedFormat;
        SKEncodedOrigin origin;
        using (var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var codec = SKCodec.Create(input)
                ?? throw new InvalidDataException("No se pudo reconocer el formato real de la imagen.");
            origin = codec.EncodedOrigin;
            var swapsAxes = SwapsAxes(origin);
            width = swapsAxes ? codec.Info.Height : codec.Info.Width;
            height = swapsAxes ? codec.Info.Width : codec.Info.Height;
            encodedFormat = codec.EncodedFormat;
        }

        var (extension, mimeType) = encodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => (".jpg", "image/jpeg"),
            SKEncodedImageFormat.Png => (".png", "image/png"),
            _ => throw new InvalidDataException("Solo se permiten imagenes JPEG y PNG reales.")
        };
        var key = Guid.NewGuid();
        var ownerFolder = request.ProductoSkuId.HasValue ? "Skus" : "Products";
        var relative = $"{ownerFolder}/{key:N}{extension}";
        var finalPath = _paths.ResolveManagedPath(relative);
        Directory.CreateDirectory(_paths.StagingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var staged = Path.Combine(_paths.StagingDirectory, $"{key:N}{extension}.tmp");

        try
        {
            if (Math.Max(width, height) <= MaximumCanonicalPixels && IsDefaultOrientation(origin))
            {
                await CopyNewAsync(source.FullName, staged, cancellationToken);
            }
            else
            {
                using var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var codec = SKCodec.Create(input)
                    ?? throw new InvalidDataException("No se pudo decodificar la imagen seleccionada.");
                using var bitmap = DecodeOrientedBitmap(codec);
                var scale = Math.Min(1d,
                    MaximumCanonicalPixels / (double)Math.Max(bitmap.Width, bitmap.Height));
                width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                await ResizeAndSaveAsync(bitmap, staged, width, height, encodedFormat, 90, cancellationToken);
            }

            var hash = await ComputeSha256Async(staged, cancellationToken);
            var canonicalSize = new FileInfo(staged).Length;
            await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
            if (!await Context.Productos.AnyAsync(product => product.ProductoId == request.ProductoId, cancellationToken))
                throw new InvalidOperationException("El producto no existe.");
            if (request.ProductoSkuId.HasValue && !await Context.ProductoSkus.AnyAsync(
                    sku => sku.ProductoSkuId == request.ProductoSkuId.Value && sku.ProductoId == request.ProductoId,
                    cancellationToken))
                throw new InvalidOperationException("El SKU no pertenece al producto seleccionado.");

            ProductAssetSummary result;
            if (request.ProductoSkuId.HasValue)
            {
                var existingCount = await Context.ProductoSkuAssets.CountAsync(
                    asset => asset.ProductoSkuId == request.ProductoSkuId.Value, cancellationToken);
                if (request.SetAsPrimary || existingCount == 0)
                    await Context.ProductoSkuAssets.Where(asset => asset.ProductoSkuId == request.ProductoSkuId.Value)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.EsPrincipal, false), cancellationToken);
                var entity = new ProductoSkuAsset
                {
                    ProductoSkuId = request.ProductoSkuId.Value, StorageKey = key, RutaRelativa = relative,
                    NombreOriginal = source.Name, MimeType = mimeType, Extension = extension,
                    SizeBytes = canonicalSize, Width = width, Height = height, Sha256 = hash,
                    Orden = existingCount, EsPrincipal = request.SetAsPrimary || existingCount == 0, FechaCreacion = DateTime.UtcNow
                };
                Context.ProductoSkuAssets.Add(entity);
                await Context.SaveChangesAsync(cancellationToken);
                File.Move(staged, finalPath);
                result = ToSummary(entity, request.ProductoId);
            }
            else
            {
                var existingCount = await Context.ProductoAssets.CountAsync(
                    asset => asset.ProductoId == request.ProductoId, cancellationToken);
                if (request.SetAsPrimary || existingCount == 0)
                    await Context.ProductoAssets.Where(asset => asset.ProductoId == request.ProductoId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.EsPrincipal, false), cancellationToken);
                var entity = new ProductoAsset
                {
                    ProductoId = request.ProductoId, StorageKey = key, RutaRelativa = relative,
                    NombreOriginal = source.Name, MimeType = mimeType, Extension = extension,
                    SizeBytes = canonicalSize, Width = width, Height = height, Sha256 = hash,
                    Orden = existingCount, EsPrincipal = request.SetAsPrimary || existingCount == 0, FechaCreacion = DateTime.UtcNow
                };
                Context.ProductoAssets.Add(entity);
                await Context.SaveChangesAsync(cancellationToken);
                File.Move(staged, finalPath);
                result = ToSummary(entity);
            }

            await AuditAsync(TipoBitacora.PRODUCTO_ASSET_CREADO, result.AssetId,
                $"Imagen administrada agregada a {result.OwnerKind} {result.OwnerId}.", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            DeleteBestEffort(staged);
            DeleteBestEffort(finalPath);
            throw;
        }
    }

    public async Task SetPrimaryAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureImageWriteAllowedAsync("Seleccionar imagen principal", cancellationToken);
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        if (ownerKind == ProductAssetOwnerKind.Product)
        {
            var target = await Context.ProductoAssets.SingleOrDefaultAsync(asset => asset.ProductoAssetId == assetId, cancellationToken)
                ?? throw new InvalidOperationException("La imagen de producto no existe.");
            await Context.ProductoAssets.Where(asset => asset.ProductoId == target.ProductoId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.EsPrincipal, false), cancellationToken);
            target.EsPrincipal = true;
            Context.Entry(target).Property(asset => asset.EsPrincipal).IsModified = true;
        }
        else
        {
            var target = await Context.ProductoSkuAssets.SingleOrDefaultAsync(asset => asset.ProductoSkuAssetId == assetId, cancellationToken)
                ?? throw new InvalidOperationException("La imagen de SKU no existe.");
            await Context.ProductoSkuAssets.Where(asset => asset.ProductoSkuId == target.ProductoSkuId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.EsPrincipal, false), cancellationToken);
            target.EsPrincipal = true;
            Context.Entry(target).Property(asset => asset.EsPrincipal).IsModified = true;
        }
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.PRODUCTO_ASSET_PRINCIPAL_MODIFICADO, assetId,
            "Imagen principal del catalogo modificada.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureImageWriteAllowedAsync("Eliminar imagen de producto", cancellationToken);
        string relative;
        await using (var transaction = await Context.Database.BeginTransactionAsync(cancellationToken))
        {
            if (ownerKind == ProductAssetOwnerKind.Product)
            {
                var target = await Context.ProductoAssets.SingleOrDefaultAsync(asset => asset.ProductoAssetId == assetId, cancellationToken)
                    ?? throw new InvalidOperationException("La imagen de producto no existe.");
                relative = target.RutaRelativa;
                var ownerId = target.ProductoId;
                var wasPrimary = target.EsPrincipal;
                Context.ProductoAssets.Remove(target);
                await Context.SaveChangesAsync(cancellationToken);
                if (wasPrimary)
                {
                    var next = await Context.ProductoAssets.Where(asset => asset.ProductoId == ownerId)
                        .OrderBy(asset => asset.Orden).FirstOrDefaultAsync(cancellationToken);
                    if (next is not null) next.EsPrincipal = true;
                }
            }
            else
            {
                var target = await Context.ProductoSkuAssets.SingleOrDefaultAsync(asset => asset.ProductoSkuAssetId == assetId, cancellationToken)
                    ?? throw new InvalidOperationException("La imagen de SKU no existe.");
                relative = target.RutaRelativa;
                var ownerId = target.ProductoSkuId;
                var wasPrimary = target.EsPrincipal;
                Context.ProductoSkuAssets.Remove(target);
                await Context.SaveChangesAsync(cancellationToken);
                if (wasPrimary)
                {
                    var next = await Context.ProductoSkuAssets.Where(asset => asset.ProductoSkuId == ownerId)
                        .OrderBy(asset => asset.Orden).FirstOrDefaultAsync(cancellationToken);
                    if (next is not null) next.EsPrincipal = true;
                }
            }
            await Context.SaveChangesAsync(cancellationToken);
            await AuditAsync(TipoBitacora.PRODUCTO_ASSET_ELIMINADO, assetId, "Imagen administrada eliminada.", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        DeleteBestEffort(_paths.ResolveManagedPath(relative));
        DeleteThumbnailFamily(relative);
    }

    public async Task<string?> GetThumbnailPathAsync(
        ProductAssetOwnerKind ownerKind, int assetId, int maximumPixels = 400,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        maximumPixels = Math.Clamp(maximumPixels, MinimumThumbnailPixels, MaximumThumbnailPixels);
        Guid key;
        string relative;
        if (ownerKind == ProductAssetOwnerKind.Product)
        {
            var metadata = await Context.ProductoAssets.AsNoTracking()
                .SingleOrDefaultAsync(asset => asset.ProductoAssetId == assetId, cancellationToken);
            if (metadata is null) return null;
            key = metadata.StorageKey;
            relative = metadata.RutaRelativa;
        }
        else
        {
            var sku = await Context.ProductoSkuAssets.AsNoTracking().Where(asset => asset.ProductoSkuAssetId == assetId)
                .Select(asset => new { asset.StorageKey, asset.RutaRelativa }).SingleOrDefaultAsync(cancellationToken);
            if (sku is null) return null;
            key = sku.StorageKey;
            relative = sku.RutaRelativa;
        }
        var source = _paths.ResolveManagedPath(relative);
        if (!File.Exists(source)) return null;
        Directory.CreateDirectory(_paths.ThumbnailCacheDirectory);
        // v2 prevents an old, incorrectly oriented thumbnail from being reused after this fix.
        var target = Path.Combine(_paths.ThumbnailCacheDirectory, $"{key:N}-{maximumPixels}-v2.jpg");
        if (File.Exists(target)) return target;
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var codec = SKCodec.Create(input)
                ?? throw new InvalidDataException("No se pudo decodificar la imagen administrada.");
            using var bitmap = DecodeOrientedBitmap(codec);
            var scale = Math.Min(1d, maximumPixels / (double)Math.Max(bitmap.Width, bitmap.Height));
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            await ResizeAndSaveAsync(bitmap, temporary, width, height, SKEncodedImageFormat.Jpeg, 84, cancellationToken);
            try { File.Move(temporary, target); }
            catch (IOException) when (File.Exists(target)) { DeleteBestEffort(temporary); }
            return target;
        }
        catch
        {
            DeleteBestEffort(temporary);
            throw;
        }
    }

    public async Task<AssetCleanupResult> CleanupOrphansAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureProductManagementAllowedAsync("Limpiar assets huerfanos", cancellationToken);
        if (minimumAge < TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(minimumAge), "La ventana de seguridad minima es una hora.");
        var referenced = (await Context.ProductoAssets.AsNoTracking().Select(asset => asset.RutaRelativa).ToListAsync(cancellationToken))
            .Concat(await Context.ProductoSkuAssets.AsNoTracking().Select(asset => asset.RutaRelativa).ToListAsync(cancellationToken))
            .Select(path => path.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cutoff = DateTime.UtcNow - minimumAge;
        var inspected = 0;
        var quarantined = 0;
        var warnings = new List<string>();
        foreach (var directory in new[] { _paths.ProductAssetsDirectory, _paths.SkuAssetsDirectory })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inspected++;
                var name = Path.GetFileNameWithoutExtension(file);
                if (!Guid.TryParseExact(name, "N", out _) || File.GetLastWriteTimeUtc(file) > cutoff) continue;
                var relative = Path.GetRelativePath(_paths.AssetsRoot, file).Replace('\\', '/');
                if (referenced.Contains(relative)) continue;
                try
                {
                    Directory.CreateDirectory(_paths.QuarantineDirectory);
                    File.Move(file, Path.Combine(_paths.QuarantineDirectory,
                        $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Path.GetFileName(file)}"));
                    quarantined++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"No se pudo poner en cuarentena '{Path.GetFileName(file)}': {ex.Message}");
                }
            }
        }
        return new AssetCleanupResult(inspected, quarantined, warnings);
    }

    private async Task EnsureImageWriteAllowedAsync(string operation, CancellationToken cancellationToken)
    {
        _installation?.EnsureOperationalWriteAllowed(operation);
        if (_permissions is null)
        {
            return;
        }

        if (await _permissions.HasAnyPermissionAsync(
                [PermisoEmpleado.PRODUCTOS_GESTIONAR, PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR],
                cancellationToken: cancellationToken))
        {
            return;
        }

        var definition = EmployeePermissionCatalog.GetDefinition(PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR);
        throw new InvalidOperationException($"No tiene permiso para: {definition.Nombre}.");
    }

    private async Task EnsureProductManagementAllowedAsync(string operation, CancellationToken cancellationToken)
    {
        _installation?.EnsureOperationalWriteAllowed(operation);
        if (_permissions is not null)
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR, cancellationToken);
        }
    }

    private async Task AuditAsync(TipoBitacora type, int id, string description, CancellationToken cancellationToken)
    {
        var userId = _session.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para administrar imagenes.");
        await _audit.RegistrarAsync(new AuditEntryRequest(userId, type, "producto_assets", id, description), cancellationToken);
    }

    private static ProductAssetSummary ToSummary(ProductoAsset asset) => new(
        asset.ProductoAssetId, ProductAssetOwnerKind.Product, asset.ProductoId, asset.ProductoId, null,
        asset.StorageKey, asset.RutaRelativa, asset.NombreOriginal, asset.MimeType, asset.Extension,
        asset.SizeBytes, asset.Width, asset.Height, asset.Sha256, asset.Orden, asset.EsPrincipal, asset.FechaCreacion);

    private static ProductAssetSummary ToSummary(ProductoSkuAsset asset, int productId) => new(
        asset.ProductoSkuAssetId, ProductAssetOwnerKind.Sku, asset.ProductoSkuId, productId, asset.ProductoSkuId,
        asset.StorageKey, asset.RutaRelativa, asset.NombreOriginal, asset.MimeType, asset.Extension,
        asset.SizeBytes, asset.Width, asset.Height, asset.Sha256, asset.Orden, asset.EsPrincipal, asset.FechaCreacion);

    private static async Task ResizeAndSaveAsync(SKBitmap source, string path, int width, int height,
        SKEncodedImageFormat format, int quality, CancellationToken cancellationToken)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("No se pudo preparar la imagen normalizada.");
        surface.Canvas.Clear(SKColors.Transparent);
        using var sourceImage = SKImage.FromBitmap(source);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawImage(sourceImage,
            new SKRect(0, 0, source.Width, source.Height),
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality)
            ?? throw new InvalidDataException("No se pudo codificar la imagen administrada.");
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await data.AsStream().CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static bool IsDefaultOrientation(SKEncodedOrigin origin) =>
        origin == SKEncodedOrigin.Default || origin == SKEncodedOrigin.TopLeft;

    private static bool SwapsAxes(SKEncodedOrigin origin) => origin is
        SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
        SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    private static SKBitmap DecodeOrientedBitmap(SKCodec codec)
    {
        var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("No se pudo decodificar la imagen seleccionada.");
        var origin = codec.EncodedOrigin;
        if (IsDefaultOrientation(origin)) return decoded;

        var width = decoded.Width;
        var height = decoded.Height;
        var swapsAxes = SwapsAxes(origin);
        var oriented = new SKBitmap(swapsAxes ? height : width, swapsAxes ? width : height);
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
            _ => throw new InvalidDataException($"Orientacion EXIF no reconocida: {origin}.")
        };

        try
        {
            using var canvas = new SKCanvas(oriented);
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(decoded, 0, 0);
            canvas.Flush();
            return oriented;
        }
        catch
        {
            oriented.Dispose();
            throw;
        }
        finally
        {
            decoded.Dispose();
        }
    }

    private static async Task CopyNewAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
    }

    private void DeleteThumbnailFamily(string relative)
    {
        var name = Path.GetFileNameWithoutExtension(relative);
        if (!Directory.Exists(_paths.ThumbnailCacheDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(_paths.ThumbnailCacheDirectory, $"{name}-*.jpg"))
            DeleteBestEffort(path);
    }

    private static void DeleteBestEffort(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
