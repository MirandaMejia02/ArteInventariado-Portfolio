using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

/// <summary>
/// Safe, read-only projection used by EmployeeInventoryLookupForm.
/// Deliberately excludes purchase cost, margin and profitability fields.
/// </summary>
public sealed record EmployeeInventoryLookupItem(
    string CodigoInterno,
    string CodigoSku,
    string ProductoNombre,
    string? VarianteNombre,
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
    int CategoriaId = 0,
    string CategoriaNombre = "",
    int? SubcategoriaId = null,
    string? SubcategoriaNombre = null,
    long PrecioVentaEfectivoCentavos = 0,
    DateTime FechaCreacion = default,
    int StockMinimo = 0,
    bool TieneImagen = false)
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
}
