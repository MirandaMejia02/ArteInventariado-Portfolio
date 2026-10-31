using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public enum PosSubcategoryFilterKind
{
    Todas,
    Concreta,
    SinSubcategoria
}

public sealed record PosSubcategoryFilter(
    PosSubcategoryFilterKind Kind,
    int? SubcategoriaId = null)
{
    public static PosSubcategoryFilter Todas { get; } =
        new(PosSubcategoryFilterKind.Todas);

    public static PosSubcategoryFilter SinSubcategoria { get; } =
        new(PosSubcategoryFilterKind.SinSubcategoria);

    public static PosSubcategoryFilter Concreta(int subcategoriaId)
    {
        if (subcategoriaId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subcategoriaId),
                "El identificador de la subcategoría debe ser mayor que cero.");
        }

        return new PosSubcategoryFilter(
            PosSubcategoryFilterKind.Concreta,
            subcategoriaId);
    }
}

public sealed record PosSkuCatalogFilter(
    string? SearchText = null,
    int? CategoriaId = null,
    EstadoProducto? Estado = EstadoProducto.ACTIVO,
    bool? SkuActivo = true,
    bool IncluirBarcodeExacto = false,
    PosSubcategoryFilter? SubcategoriaFilter = null);

public sealed record PosSkuSaleItem(
    int ProductoId,
    int ProductoSkuId,
    string CodigoInterno,
    string CodigoSku,
    string ProductoNombre,
    string? VarianteNombre,
    int CategoriaId,
    string CategoriaNombre,
    ModoProducto Modo,
    EstadoProducto Estado,
    bool SkuActivo,
    bool SkuPrincipal,
    long PrecioVentaEfectivoCentavos,
    long CostoEfectivoCentavos,
    int MinimoExhibicion,
    int StockFisicoExhibicion,
    int StockReservadoExhibicion,
    int StockFisicoBodega,
    int StockReservadoBodega,
    int StockFisicoSinAsignar,
    int StockReservadoSinAsignar,
    ProductAssetOwnerKind? ImagenOwnerKind,
    int? ImagenAssetId,
    string? ImagenRutaRelativa,
    string? ImagenLocalPath,
    int? SubcategoriaId = null,
    string? SubcategoriaNombre = null)
{
    public int StockDisponibleExhibicion =>
        Math.Max(0, StockFisicoExhibicion - StockReservadoExhibicion);

    public int StockDisponibleBodega =>
        Math.Max(0, StockFisicoBodega - StockReservadoBodega);

    public int StockDisponibleSinAsignar =>
        Math.Max(0, StockFisicoSinAsignar - StockReservadoSinAsignar);

    public int StockFisicoTotal =>
        StockFisicoExhibicion + StockFisicoBodega + StockFisicoSinAsignar;

    public int StockReservadoTotal =>
        StockReservadoExhibicion + StockReservadoBodega + StockReservadoSinAsignar;

    public int StockDisponibleTotal =>
        StockDisponibleExhibicion + StockDisponibleBodega + StockDisponibleSinAsignar;

    public bool DisponibleParaVenta =>
        Estado == EstadoProducto.ACTIVO && SkuActivo && StockDisponibleExhibicion > 0;
}
