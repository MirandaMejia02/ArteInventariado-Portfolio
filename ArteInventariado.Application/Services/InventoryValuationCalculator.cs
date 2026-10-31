using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Services;

public sealed record InventoryValuationComputed(
    int PhysicalStock,
    int ReservedStock,
    int AvailableStock,
    long? InvestmentCurrentCentavos,
    long? PotentialSalesValueCentavos,
    long? AvailableSalesValueCentavos,
    long? PotentialGrossProfitCentavos,
    decimal? PotentialGrossMarginPercentage);

/// <summary>
/// Single source for the arithmetic used by current inventory valuation.
/// It is intentionally independent from EF and WinForms so the same checked
/// formulas can be exercised by focused tests and reused by the query layer.
/// </summary>
public static class InventoryValuationCalculator
{
    public static InventoryValuationComputed Calculate(
        int physicalStock,
        int reservedStock,
        long? costEffectiveCentavos,
        long? salePriceEffectiveCentavos)
    {
        if (physicalStock < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalStock));
        }
        if (reservedStock < 0 || reservedStock > physicalStock)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedStock));
        }

        var availableStock = checked(physicalStock - reservedStock);
        var investment = Multiply(physicalStock, costEffectiveCentavos);
        var potentialSales = Multiply(physicalStock, salePriceEffectiveCentavos);
        var availableSales = Multiply(availableStock, salePriceEffectiveCentavos);
        long? gain = investment.HasValue && potentialSales.HasValue
            ? checked(potentialSales.Value - investment.Value)
            : null;
        decimal? margin = null;
        if (potentialSales is long potential && potential != 0 && gain is long gross)
        {
            margin = decimal.Round(gross * 100m / potential, 2, MidpointRounding.AwayFromZero);
        }

        return new InventoryValuationComputed(
            physicalStock,
            reservedStock,
            availableStock,
            investment,
            potentialSales,
            availableSales,
            gain,
            margin);
    }

    public static long? Multiply(int quantity, long? unitCentavos) =>
        unitCentavos.HasValue ? checked((long)quantity * unitCentavos.Value) : null;

    public static InventoryValuationTotals Summarize(
        IReadOnlyList<InventoryValuationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return new InventoryValuationTotals(
                0, 0, 0, 0, 0, 0, 0, 0, null,
                0, 0, 0, 0, 0, 0, 0);
        }

        var physical = checked(rows.Sum(row => row.PhysicalStock));
        var hasUnknownReserved = rows.Any(row => !row.ReservedStock.HasValue);
        var reserved = hasUnknownReserved
            ? (int?)null
            : checked(rows.Sum(row => row.ReservedStock!.Value));
        var available = hasUnknownReserved
            ? (int?)null
            : checked(rows.Sum(row => row.AvailableStock!.Value));

        return new InventoryValuationTotals(
            rows.Count,
            physical,
            reserved,
            available,
            SumNullable(rows, row => row.InvestmentCurrentCentavos),
            SumNullable(rows, row => row.PotentialSalesValueCentavos),
            SumNullable(rows, row => row.AvailableSalesValueCentavos),
            SumNullable(rows, row => row.PotentialGrossProfitCentavos),
            PotentialMargin(
                SumNullable(rows, row => row.PotentialSalesValueCentavos),
                SumNullable(rows, row => row.PotentialGrossProfitCentavos)),
            rows.Count(row => row.ProductState != Domain.Enums.EstadoProducto.ACTIVO),
            rows.Count(row => !row.IsLegacy && !row.SkuActive),
            rows.Count(row => row.SubcategoryActive == false),
            rows.Count(row => row.UnassignedStock > 0),
            rows.Count(row => row.IsLegacy),
            rows.Count(row => row.SalePriceEffectiveCentavos == 0),
            rows.Count(row => !row.InvestmentCurrentCentavos.HasValue ||
                !row.PotentialSalesValueCentavos.HasValue));
    }

    public static decimal? PotentialMargin(long? potentialSales, long? gain) =>
        potentialSales is not 0 and not null && gain.HasValue
            ? decimal.Round(gain.Value * 100m / potentialSales.Value, 2, MidpointRounding.AwayFromZero)
            : null;

    private static long? SumNullable(
        IReadOnlyList<InventoryValuationRow> rows,
        Func<InventoryValuationRow, long?> selector)
    {
        var values = rows.Select(selector).ToList();
        return values.Any(value => !value.HasValue)
            ? null
            : checked(values.Sum(value => value!.Value));
    }
}
