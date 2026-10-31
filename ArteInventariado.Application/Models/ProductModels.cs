using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record ProductSummary(
    int ProductoId,
    string CodigoInterno,
    string Nombre,
    string? Descripcion,
    int CategoriaId,
    string CategoriaNombre,
    string? ImagenLocalPath,
    long PrecioCompraCentavos,
    long PrecioVentaCentavos,
    int StockActual,
    int StockMinimo,
    EstadoProducto Estado,
    DateTime FechaCreacion,
    DateTime? FechaModificacion,
    ModoProducto Modo = ModoProducto.SIMPLE,
    int StockDisponible = 0,
    int StockExhibicion = 0,
    int StockBodega = 0,
    int StockSinAsignar = 0,
    int StockReservado = 0,
    string? ManagedImageRelativePath = null,
    EstadoBarcodeProducto EstadoBarcode = EstadoBarcodeProducto.SinBarcode,
    bool CostosVisibles = true,
    int? SubcategoriaId = null,
    string? SubcategoriaNombre = null,
    bool TieneImagen = false);

public sealed record CreateProductRequest(
    string? CodigoInterno,
    string Nombre,
    string? Descripcion,
    int? CategoriaId,
    string? ImagenLocalPath,
    long? PrecioCompraCentavos,
    long? PrecioVentaCentavos,
    int? StockActual,
    int StockMinimo,
    ModoProducto Modo = ModoProducto.SIMPLE,
    int? SubcategoriaId = null,
    UbicacionInventario UbicacionInicial = UbicacionInventario.SIN_ASIGNAR);

public sealed record UpdateProductRequest(
    int ProductoId,
    string CodigoInterno,
    string Nombre,
    string? Descripcion,
    int CategoriaId,
    string? ImagenLocalPath,
    long PrecioCompraCentavos,
    long PrecioVentaCentavos,
    int StockMinimo,
    ModoProducto Modo = ModoProducto.SIMPLE,
    int? SubcategoriaId = null);

public sealed record ProductFilter(
    string? SearchText = null,
    EstadoProducto? Estado = null,
    int? CategoriaId = null,
    bool SoloBajoStock = false,
    int? SubcategoriaId = null);

public sealed record ProductCatalogDetail(ProductSummary Product, IReadOnlyList<ProductSkuSummary> Skus);

public sealed record ProductSkuSummary(
    int ProductoSkuId,
    int ProductoId,
    string CodigoSku,
    string? NombreVariante,
    bool Activo,
    bool EsPrincipal,
    int? MinimoExhibicionOverride,
    long? PrecioVentaOverrideCentavos,
    long? CostoOverrideCentavos,
    long PrecioVentaEfectivoCentavos,
    long CostoEfectivoCentavos,
    int StockExhibicion,
    int StockBodega,
    int StockSinAsignar,
    int StockReservado,
    int StockTotal,
    int Disponible,
    bool CostosVisibles = true);

public sealed record CreateProductSkuRequest(
    int ProductoId,
    string CodigoSku,
    string? NombreVariante,
    long? PrecioVentaOverrideCentavos = null,
    long? CostoOverrideCentavos = null,
    int? MinimoExhibicionOverride = null);

public sealed record UpdateProductSkuRequest(
    int ProductoSkuId,
    string? NombreVariante,
    long? PrecioVentaOverrideCentavos,
    long? CostoOverrideCentavos,
    int? MinimoExhibicionOverride);

public enum ProductAssetOwnerKind { Product = 1, Sku = 2 }

public sealed record ProductAssetSummary(
    int AssetId,
    ProductAssetOwnerKind OwnerKind,
    int OwnerId,
    int ProductoId,
    int? ProductoSkuId,
    Guid StorageKey,
    string RelativePath,
    string OriginalFileName,
    string MimeType,
    string Extension,
    long SizeBytes,
    int Width,
    int Height,
    string Sha256,
    int Order,
    bool IsPrimary,
    DateTime CreatedUtc);

public sealed record ImportProductAssetRequest(
    int ProductoId,
    string SourcePath,
    int? ProductoSkuId = null,
    bool SetAsPrimary = false);

public sealed record AssetCleanupResult(int Inspected, int Quarantined, IReadOnlyList<string> Warnings);
