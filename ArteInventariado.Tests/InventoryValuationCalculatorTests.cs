using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;

namespace ArteInventariado.Tests;

public sealed class InventoryValuationCalculatorTests
{
    [Fact]
    public void ControlCase_UsesPhysicalReservedAndEffectivePrices()
    {
        var result = InventoryValuationCalculator.Calculate(10, 2, 300, 500);

        Assert.Equal(10, result.PhysicalStock);
        Assert.Equal(2, result.ReservedStock);
        Assert.Equal(8, result.AvailableStock);
        Assert.Equal(3_000, result.InvestmentCurrentCentavos);
        Assert.Equal(5_000, result.PotentialSalesValueCentavos);
        Assert.Equal(4_000, result.AvailableSalesValueCentavos);
        Assert.Equal(2_000, result.PotentialGrossProfitCentavos);
        Assert.Equal(40m, result.PotentialGrossMarginPercentage);
    }

    [Theory]
    [InlineData(550L, 500L, -50L)]
    [InlineData(300L, 500L, 200L)]
    [InlineData(0L, 500L, 500L)]
    public void EffectiveValues_RespectOverridesAndZero(long cost, long price, long expectedGain)
    {
        var result = InventoryValuationCalculator.Calculate(1, 0, cost, price);

        Assert.Equal(cost, result.InvestmentCurrentCentavos);
        Assert.Equal(price, result.PotentialSalesValueCentavos);
        Assert.Equal(expectedGain, result.PotentialGrossProfitCentavos);
    }

    [Fact]
    public void ZeroSalePrice_HasNoCalculableMarginButKeepsGain()
    {
        var result = InventoryValuationCalculator.Calculate(4, 1, 250, 0);

        Assert.Equal(-1_000, result.PotentialGrossProfitCentavos);
        Assert.Null(result.PotentialGrossMarginPercentage);
        Assert.Equal(0, result.AvailableSalesValueCentavos);
    }

    [Fact]
    public void ReservedIsIncludedInInvestmentAndExcludedFromAvailable()
    {
        var result = InventoryValuationCalculator.Calculate(10, 7, 300, 500);

        Assert.Equal(3_000, result.InvestmentCurrentCentavos);
        Assert.Equal(1_500, result.AvailableSalesValueCentavos);
    }

    [Fact]
    public void InvalidReservationsAreRejectedWithoutChangingInventory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryValuationCalculator.Calculate(2, 3, 100, 200));
    }

    [Fact]
    public void SummaryUsesTheThreeLocationsOnceAndGlobalMargin()
    {
        var rows = new[]
        {
            Row(exhibition: 3, warehouse: 4, unassigned: 3, reserved: 2, cost: 300, price: 500),
            Row(exhibition: 0, warehouse: 2, unassigned: 1, reserved: 1, cost: 200, price: 400)
        };

        var totals = InventoryValuationCalculator.Summarize(rows);

        Assert.Equal(13, totals.PhysicalStock);
        Assert.Equal(3, totals.ReservedStock);
        Assert.Equal(10, totals.AvailableStock);
        Assert.Equal(3_600, totals.InvestmentCurrentCentavos);
        Assert.Equal(6_200, totals.PotentialSalesValueCentavos);
        Assert.Equal(2_600, totals.PotentialGrossProfitCentavos);
        Assert.Equal(decimal.Round(2_600m * 100m / 6_200m, 2), totals.PotentialGrossMarginPercentage);
    }

    [Fact]
    public void LegacyRowKeepsPhysicalValueButMarksReservationsUnknown()
    {
        var row = new InventoryValuationRow(
            1, null, "P-1", null, "Legacy", null, 1, "Categoría", true,
            null, "Sin subcategoría", null, Domain.Enums.EstadoProducto.ACTIVO, false,
            0, 0, 0, 0, 0, 0, 10, 300, 500);

        Assert.True(row.IsLegacy);
        Assert.Equal(10, row.PhysicalStock);
        Assert.Null(row.ReservedStock);
        Assert.Null(row.AvailableStock);
        Assert.Equal("Legacy sin ubicación", row.LocationQuality);
        Assert.Contains("Legacy", row.Quality);
    }

    private static InventoryValuationRow Row(
        int exhibition,
        int warehouse,
        int unassigned,
        int reserved,
        long? cost,
        long? price) => new(
        1, 1, "P-1", "SKU-1", "Producto", null, 1, "Categoría", true,
        null, "Sin subcategoría", true, Domain.Enums.EstadoProducto.ACTIVO, true,
        exhibition, reserved / 3, warehouse, reserved / 3, unassigned,
        reserved - (reserved / 3) * 2, null, cost, price);
}
