using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class PosCatalogService : IPosCatalogService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;

    [ActivatorUtilitiesConstructor]
    public PosCatalogService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    internal PosCatalogService(AppDbContext context)
    {
        _legacyContext = context;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar el catalogo del POS.");

    public async Task<IReadOnlyList<PosSkuSaleItem>> SearchAsync(
        PosSkuCatalogFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory,
            _legacyContext,
            cancellationToken);

        filter ??= new PosSkuCatalogFilter();

        var searchText = filter.SearchText?.Trim();
        if (filter.IncluirBarcodeExacto && !string.IsNullOrWhiteSpace(searchText))
        {
            var barcodeSkuId = await Context.ProductoSkuBarcodes
                .AsNoTracking()
                .Where(barcode =>
                    barcode.Codigo == searchText &&
                    barcode.Activo &&
                    barcode.ProductoSku.Activo &&
                    barcode.ProductoSku.Producto.Estado == EstadoProducto.ACTIVO)
                .Select(barcode => (int?)barcode.ProductoSkuId)
                .SingleOrDefaultAsync(cancellationToken);

            if (barcodeSkuId.HasValue)
            {
                var exactQuery = BaseQuery()
                    .Where(sku => sku.ProductoSkuId == barcodeSkuId.Value);
                exactQuery = ApplyCatalogFilters(exactQuery, filter);

                var exactSku = await exactQuery.SingleOrDefaultAsync(cancellationToken);
                if (exactSku is not null)
                {
                    return [ToSaleItem(exactSku)];
                }
            }
        }

        var query = BaseQuery();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = BuildLikePattern(searchText);
            query = query.Where(sku =>
                EF.Functions.Like(sku.Producto.CodigoInterno, pattern, "\\") ||
                EF.Functions.Like(sku.Producto.Nombre, pattern, "\\") ||
                EF.Functions.Like(sku.Producto.Categoria.Nombre, pattern, "\\") ||
                (sku.Producto.Subcategoria != null &&
                    EF.Functions.Like(sku.Producto.Subcategoria.Nombre, pattern, "\\")) ||
                EF.Functions.Like(sku.CodigoSku, pattern, "\\") ||
                (sku.NombreVariante != null && EF.Functions.Like(sku.NombreVariante, pattern, "\\")));
        }

        query = ApplyCatalogFilters(query, filter);

        var skus = await query
            .OrderBy(sku => sku.Producto.Nombre)
            .ThenByDescending(sku => sku.EsPrincipal)
            .ThenBy(sku => sku.NombreVariante)
            .ThenBy(sku => sku.CodigoSku)
            .ToListAsync(cancellationToken);

        return skus.Select(ToSaleItem).ToList();
    }

    public async Task<PosSkuSaleItem?> GetBySkuIdAsync(
        int productoSkuId,
        CancellationToken cancellationToken = default)
    {
        if (productoSkuId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(productoSkuId),
                "El identificador del SKU debe ser mayor que cero.");
        }

        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory,
            _legacyContext,
            cancellationToken);

        var sku = await BaseQuery()
            .SingleOrDefaultAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken);

        return sku is null ? null : ToSaleItem(sku);
    }

    private IQueryable<ProductoSku> BaseQuery() =>
        Context.ProductoSkus
            .AsNoTracking()
            .Include(sku => sku.Producto)
                .ThenInclude(producto => producto.Categoria)
            .Include(sku => sku.Producto)
                .ThenInclude(producto => producto.Subcategoria)
            .Include(sku => sku.Producto)
                .ThenInclude(producto => producto.Assets)
            .Include(sku => sku.Assets)
            .Include(sku => sku.SaldosInventario)
            .AsSplitQuery();

    private static IQueryable<ProductoSku> ApplyCatalogFilters(
        IQueryable<ProductoSku> query,
        PosSkuCatalogFilter filter)
    {
        if (filter.CategoriaId.HasValue)
        {
            query = query.Where(sku => sku.Producto.CategoriaId == filter.CategoriaId.Value);
        }

        if (filter.SubcategoriaFilter is { } subcategoryFilter)
        {
            query = subcategoryFilter.Kind switch
            {
                PosSubcategoryFilterKind.Todas => query,
                PosSubcategoryFilterKind.SinSubcategoria =>
                    query.Where(sku => sku.Producto.SubcategoriaId == null),
                PosSubcategoryFilterKind.Concreta when subcategoryFilter.SubcategoriaId.HasValue =>
                    query.Where(sku => sku.Producto.SubcategoriaId == subcategoryFilter.SubcategoriaId.Value),
                _ => throw new ArgumentException(
                    "El filtro de subcategoría concreta requiere un identificador válido.",
                    nameof(filter))
            };
        }

        if (filter.Estado.HasValue)
        {
            query = query.Where(sku => sku.Producto.Estado == filter.Estado.Value);
        }

        if (filter.SkuActivo.HasValue)
        {
            query = query.Where(sku => sku.Activo == filter.SkuActivo.Value);
        }

        return query;
    }

    private static PosSkuSaleItem ToSaleItem(ProductoSku sku)
    {
        var exhibition = GetBalance(sku, UbicacionInventario.EXHIBICION);
        var warehouse = GetBalance(sku, UbicacionInventario.BODEGA);
        var unassigned = GetBalance(sku, UbicacionInventario.SIN_ASIGNAR);

        var skuImage = sku.Assets
            .OrderByDescending(asset => asset.EsPrincipal)
            .ThenBy(asset => asset.Orden)
            .ThenBy(asset => asset.ProductoSkuAssetId)
            .FirstOrDefault();
        var productImage = skuImage is null
            ? sku.Producto.Assets
                .OrderByDescending(asset => asset.EsPrincipal)
                .ThenBy(asset => asset.Orden)
                .ThenBy(asset => asset.ProductoAssetId)
                .FirstOrDefault()
            : null;

        return new PosSkuSaleItem(
            sku.ProductoId,
            sku.ProductoSkuId,
            sku.Producto.CodigoInterno,
            sku.CodigoSku,
            sku.Producto.Nombre,
            sku.NombreVariante,
            sku.Producto.CategoriaId,
            sku.Producto.Categoria.Nombre,
            sku.Producto.Modo,
            sku.Producto.Estado,
            sku.Activo,
            sku.EsPrincipal,
            sku.ResolverPrecioVentaCentavos(),
            sku.ResolverCostoCentavos(),
            sku.MinimoExhibicionOverride ?? sku.Producto.StockMinimo,
            exhibition.Physical,
            exhibition.Reserved,
            warehouse.Physical,
            warehouse.Reserved,
            unassigned.Physical,
            unassigned.Reserved,
            skuImage is not null
                ? ProductAssetOwnerKind.Sku
                : productImage is not null
                    ? ProductAssetOwnerKind.Product
                    : null,
            skuImage?.ProductoSkuAssetId ?? productImage?.ProductoAssetId,
            skuImage?.RutaRelativa ?? productImage?.RutaRelativa,
            skuImage is null && productImage is null ? sku.Producto.ImagenLocalPath : null,
            sku.Producto.SubcategoriaId,
            sku.Producto.Subcategoria?.Nombre);
    }

    private static (int Physical, int Reserved) GetBalance(
        ProductoSku sku,
        UbicacionInventario location)
    {
        var balance = sku.SaldosInventario.FirstOrDefault(item => item.Ubicacion == location);
        return balance is null
            ? (0, 0)
            : (balance.ExistenciaFisica, balance.Reservado);
    }

    private static string BuildLikePattern(string searchText)
    {
        var escaped = searchText.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
