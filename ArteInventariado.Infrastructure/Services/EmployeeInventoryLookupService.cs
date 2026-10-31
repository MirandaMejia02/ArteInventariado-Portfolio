using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Dedicated read-only projection for employee inventory consultation.
/// Unlike PosCatalogService, this query never materializes ProductoSku or Producto
/// entities and therefore never selects purchase-cost columns.
/// </summary>
public sealed class EmployeeInventoryLookupService : IEmployeeInventoryLookupService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;

    [ActivatorUtilitiesConstructor]
    public EmployeeInventoryLookupService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    internal EmployeeInventoryLookupService(AppDbContext context)
    {
        _legacyContext = context;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar el inventario.");

    public async Task<IReadOnlyList<EmployeeInventoryLookupItem>> SearchAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory,
            _legacyContext,
            cancellationToken);

        var trimmedSearch = searchText?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedSearch))
        {
            // Exact lookup uses the unique BINARY barcode index. It deliberately
            // accepts principal and secondary active barcodes alike.
            var barcodeSkuId = await Context.ProductoSkuBarcodes
                .AsNoTracking()
                .Where(barcode =>
                    barcode.Codigo == trimmedSearch &&
                    barcode.Activo &&
                    barcode.ProductoSku.Activo &&
                    barcode.ProductoSku.Producto.Estado == EstadoProducto.ACTIVO)
                .Select(barcode => (int?)barcode.ProductoSkuId)
                .SingleOrDefaultAsync(cancellationToken);

            if (barcodeSkuId.HasValue)
            {
                var exact = await BuildProjection()
                    .Where(item =>
                        item.ProductoSkuId == barcodeSkuId.Value &&
                        item.SkuActivo &&
                        item.Estado == EstadoProducto.ACTIVO)
                    .SingleOrDefaultAsync(cancellationToken);
                if (exact is not null)
                {
                    return [ToModel(exact)];
                }
            }
        }

        // Preserve the existing textual behavior: this fallback searches the
        // declared product/SKU/variant/category fields and keeps its existing
        // state semantics. It never performs a LIKE against barcode values.
        var query = BuildProjection();
        if (!string.IsNullOrWhiteSpace(trimmedSearch))
        {
            var pattern = BuildLikePattern(trimmedSearch);
            query = query.Where(item =>
                EF.Functions.Like(item.CodigoInterno, pattern, "\\") ||
                EF.Functions.Like(item.ProductoNombre, pattern, "\\") ||
                EF.Functions.Like(item.CategoriaNombre, pattern, "\\") ||
                (item.SubcategoriaNombre != null &&
                    EF.Functions.Like(item.SubcategoriaNombre, pattern, "\\")) ||
                EF.Functions.Like(item.CodigoSku, pattern, "\\") ||
                (item.VarianteNombre != null && EF.Functions.Like(item.VarianteNombre, pattern, "\\")));
        }

        var rows = await query
            .OrderBy(item => item.ProductoNombre)
            .ThenByDescending(item => item.SkuPrincipal)
            .ThenBy(item => item.VarianteNombre)
            .ThenBy(item => item.CodigoSku)
            .ToListAsync(cancellationToken);

        return rows.Select(ToModel).ToList();
    }

    private IQueryable<LookupProjection> BuildProjection() =>
        Context.ProductoSkus
            .AsNoTracking()
            .Select(sku => new LookupProjection
            {
                ProductoSkuId = sku.ProductoSkuId,
                CodigoInterno = sku.Producto.CodigoInterno,
                CodigoSku = sku.CodigoSku,
                ProductoNombre = sku.Producto.Nombre,
                VarianteNombre = sku.NombreVariante,
                CategoriaId = sku.Producto.CategoriaId,
                CategoriaNombre = sku.Producto.Categoria.Nombre,
                SubcategoriaId = sku.Producto.SubcategoriaId,
                SubcategoriaNombre = sku.Producto.Subcategoria == null
                    ? null
                    : sku.Producto.Subcategoria.Nombre,
                Estado = sku.Producto.Estado,
                SkuActivo = sku.Activo,
                SkuPrincipal = sku.EsPrincipal,
                PrecioVentaEfectivoCentavos = sku.PrecioVentaOverrideCentavos ?? sku.Producto.PrecioVentaCentavos,
                StockFisicoExhibicion = sku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                    .Select(balance => (int?)balance.ExistenciaFisica)
                    .FirstOrDefault() ?? 0,
                StockReservadoExhibicion = sku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                    .Select(balance => (int?)balance.Reservado)
                    .FirstOrDefault() ?? 0,
                StockFisicoBodega = sku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
                    .Select(balance => (int?)balance.ExistenciaFisica)
                    .FirstOrDefault() ?? 0,
                StockReservadoBodega = sku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
                    .Select(balance => (int?)balance.Reservado)
                    .FirstOrDefault() ?? 0,
                StockFisicoSinAsignar = sku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
                    .Select(balance => (int?)balance.ExistenciaFisica)
                    .FirstOrDefault() ?? 0,
                StockReservadoSinAsignar = sku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
                    .Select(balance => (int?)balance.Reservado)
                    .FirstOrDefault() ?? 0,
                SkuImageId = sku.Assets
                    .OrderByDescending(asset => asset.EsPrincipal)
                    .ThenBy(asset => asset.Orden)
                    .ThenBy(asset => asset.ProductoSkuAssetId)
                    .Select(asset => (int?)asset.ProductoSkuAssetId)
                    .FirstOrDefault(),
                SkuImageRelativePath = sku.Assets
                    .OrderByDescending(asset => asset.EsPrincipal)
                    .ThenBy(asset => asset.Orden)
                    .ThenBy(asset => asset.ProductoSkuAssetId)
                    .Select(asset => asset.RutaRelativa)
                    .FirstOrDefault(),
                ProductImageId = sku.Producto.Assets
                    .OrderByDescending(asset => asset.EsPrincipal)
                    .ThenBy(asset => asset.Orden)
                    .ThenBy(asset => asset.ProductoAssetId)
                    .Select(asset => (int?)asset.ProductoAssetId)
                    .FirstOrDefault(),
                ProductImageRelativePath = sku.Producto.Assets
                    .OrderByDescending(asset => asset.EsPrincipal)
                    .ThenBy(asset => asset.Orden)
                    .ThenBy(asset => asset.ProductoAssetId)
                    .Select(asset => asset.RutaRelativa)
                    .FirstOrDefault(),
                LegacyImagePath = sku.Producto.ImagenLocalPath,
                FechaCreacion = sku.Producto.FechaCreacion,
                StockMinimo = sku.Producto.StockMinimo,
                TieneImagen = sku.Assets.Any() || sku.Producto.Assets.Any() ||
                    (sku.Producto.ImagenLocalPath != null && sku.Producto.ImagenLocalPath != string.Empty)
            });

    private static EmployeeInventoryLookupItem ToModel(LookupProjection row)
    {
        var hasSkuImage = row.SkuImageId.HasValue;
        var hasProductImage = row.ProductImageId.HasValue;

        return new EmployeeInventoryLookupItem(
            row.CodigoInterno,
            row.CodigoSku,
            row.ProductoNombre,
            row.VarianteNombre,
            row.StockFisicoExhibicion,
            row.StockReservadoExhibicion,
            row.StockFisicoBodega,
            row.StockReservadoBodega,
            row.StockFisicoSinAsignar,
            row.StockReservadoSinAsignar,
            hasSkuImage
                ? ProductAssetOwnerKind.Sku
                : hasProductImage
                    ? ProductAssetOwnerKind.Product
                    : null,
            hasSkuImage ? row.SkuImageId : row.ProductImageId,
            hasSkuImage
                ? row.SkuImageRelativePath
                : hasProductImage
                    ? row.ProductImageRelativePath
                    : null,
            hasSkuImage || hasProductImage ? null : row.LegacyImagePath,
            row.CategoriaId,
            row.CategoriaNombre,
            row.SubcategoriaId,
            row.SubcategoriaNombre,
            row.PrecioVentaEfectivoCentavos,
            row.FechaCreacion,
            row.StockMinimo,
            row.TieneImagen);
    }

    private static string BuildLikePattern(string searchText)
    {
        var escaped = searchText.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private sealed class LookupProjection
    {
        public int ProductoSkuId { get; init; }
        public string CodigoInterno { get; init; } = string.Empty;
        public string CodigoSku { get; init; } = string.Empty;
        public string ProductoNombre { get; init; } = string.Empty;
        public string? VarianteNombre { get; init; }
        public int CategoriaId { get; init; }
        public string CategoriaNombre { get; init; } = string.Empty;
        public int? SubcategoriaId { get; init; }
        public string? SubcategoriaNombre { get; init; }
        public EstadoProducto Estado { get; init; }
        public bool SkuActivo { get; init; }
        public bool SkuPrincipal { get; init; }
        public long PrecioVentaEfectivoCentavos { get; init; }
        public int StockFisicoExhibicion { get; init; }
        public int StockReservadoExhibicion { get; init; }
        public int StockFisicoBodega { get; init; }
        public int StockReservadoBodega { get; init; }
        public int StockFisicoSinAsignar { get; init; }
        public int StockReservadoSinAsignar { get; init; }
        public int? SkuImageId { get; init; }
        public string? SkuImageRelativePath { get; init; }
        public int? ProductImageId { get; init; }
        public string? ProductImageRelativePath { get; init; }
        public string? LegacyImagePath { get; init; }
        public DateTime FechaCreacion { get; init; }
        public int StockMinimo { get; init; }
        public bool TieneImagen { get; init; }
    }
}
