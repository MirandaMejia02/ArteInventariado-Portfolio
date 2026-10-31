using ArteInventariado.Domain.Enums;
using ArteInventariado.Application.Services;

namespace ArteInventariado.Application.Models;

/// <summary>
/// Read-only filters for the canonical inventory valuation query.
/// </summary>
public sealed record InventoryValuationRequest(
    string? Search = null,
    int? ProductId = null,
    int? CategoryId = null,
    int? SubcategoryId = null,
    AnalyticsSubcategoryFilter SubcategoryFilter = AnalyticsSubcategoryFilter.All,
    string? State = null,
    int Page = 1,
    int PageSize = 200);

/// <summary>
/// One valuation row. A legacy row has no SKU or physical location invented;
/// its legacy physical quantity is kept separately and its available quantity
/// remains unknown because historical reservations were not recorded.
/// </summary>
public sealed record InventoryValuationRow(
    int ProductId,
    int? SkuId,
    string ProductCode,
    string? SkuCode,
    string ProductName,
    string? Variant,
    int? CategoryId,
    string Category,
    bool CategoryActive,
    int? SubcategoryId,
    string Subcategory,
    bool? SubcategoryActive,
    EstadoProducto ProductState,
    bool SkuActive,
    int ExhibitionStock,
    int ExhibitionReserved,
    int WarehouseStock,
    int WarehouseReserved,
    int UnassignedStock,
    int UnassignedReserved,
    int? LegacyPhysicalStock,
    long? CostEffectiveCentavos,
    long? SalePriceEffectiveCentavos)
{
    public bool IsLegacy => LegacyPhysicalStock.HasValue;

    public int PhysicalStock => LegacyPhysicalStock ?? checked(
        ExhibitionStock + WarehouseStock + UnassignedStock);

    public int? ReservedStock => IsLegacy
        ? null
        : checked(ExhibitionReserved + WarehouseReserved + UnassignedReserved);

    public int? AvailableStock => ReservedStock.HasValue
        ? checked(PhysicalStock - ReservedStock.Value)
        : null;

    public long? InvestmentCurrentCentavos =>
        InventoryValuationCalculator.Multiply(PhysicalStock, CostEffectiveCentavos);

    public long? PotentialSalesValueCentavos =>
        InventoryValuationCalculator.Multiply(PhysicalStock, SalePriceEffectiveCentavos);

    public long? AvailableSalesValueCentavos =>
        AvailableStock.HasValue
            ? InventoryValuationCalculator.Multiply(AvailableStock.Value, SalePriceEffectiveCentavos)
            : null;

    public long? PotentialGrossProfitCentavos =>
        InvestmentCurrentCentavos.HasValue && PotentialSalesValueCentavos.HasValue
            ? checked(PotentialSalesValueCentavos.Value - InvestmentCurrentCentavos.Value)
            : null;

    public decimal? PotentialGrossMarginPercentage =>
        PotentialSalesValueCentavos is not 0 and not null && PotentialGrossProfitCentavos.HasValue
            ? decimal.Round(
                PotentialGrossProfitCentavos.Value * 100m / PotentialSalesValueCentavos.Value,
                2,
                MidpointRounding.AwayFromZero)
            : null;

    public string StateText => SkuId.HasValue
        ? $"{ProductState} / SKU {(SkuActive ? "activo" : "inactivo")}"
        : ProductState.ToString();

    public string LocationQuality => IsLegacy
        ? "Legacy sin ubicación"
        : UnassignedStock > 0
            ? "Pendiente de ubicación"
            : "Ubicación identificada";

    public string Quality
    {
        get
        {
            var flags = new List<string>();
            if (IsLegacy)
            {
                flags.Add("Legacy sin SKU/ubicación");
            }
            if (UnassignedStock > 0)
            {
                flags.Add("Pendiente de ubicación");
            }
            if (ProductState != EstadoProducto.ACTIVO)
            {
                flags.Add("Producto inactivo");
            }
            if (!SkuActive && !IsLegacy)
            {
                flags.Add("SKU inactivo");
            }
            if (SubcategoryActive == false)
            {
                flags.Add("Subcategoría inactiva");
            }
            if (!CostEffectiveCentavos.HasValue)
            {
                flags.Add("Costo no calculable");
            }
            if (!SalePriceEffectiveCentavos.HasValue)
            {
                flags.Add("Precio no calculable");
            }
            if (SalePriceEffectiveCentavos == 0)
            {
                flags.Add("Precio de venta cero");
            }

            return flags.Count == 0 ? "Exacto" : string.Join(" · ", flags.Distinct());
        }
    }
}

public sealed record InventoryValuationTotals(
    int RowCount,
    int PhysicalStock,
    int? ReservedStock,
    int? AvailableStock,
    long? InvestmentCurrentCentavos,
    long? PotentialSalesValueCentavos,
    long? AvailableSalesValueCentavos,
    long? PotentialGrossProfitCentavos,
    decimal? PotentialGrossMarginPercentage,
    int InactiveRows,
    int InactiveSkuRows,
    int InactiveSubcategoryRows,
    int PendingLocationRows,
    int LegacyRows,
    int ZeroSalePriceRows,
    int NonCalculableRows);

public sealed record InventoryValuationGroupTotals(
    string Key,
    string Name,
    InventoryValuationTotals Totals);

public sealed record InventoryValuationReport(
    IReadOnlyList<InventoryValuationRow> Rows,
    int TotalRows,
    InventoryValuationTotals Totals,
    IReadOnlyList<InventoryValuationGroupTotals> ByCategory,
    IReadOnlyList<InventoryValuationGroupTotals> BySubcategory,
    string CoverageNote,
    string EmptyMessage);
