using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Services;

public static class MetricComparisonCalculator
{
    public static MetricComparison Calculate(long currentValue, long previousValue)
    {
        if (currentValue == 0 && previousValue == 0)
        {
            return new MetricComparison(0, 0, 0, MetricComparisonState.NoChange);
        }

        if (previousValue == 0)
        {
            var state = currentValue > 0
                ? MetricComparisonState.NewMovement
                : MetricComparisonState.NoComparisonBase;
            return new MetricComparison(currentValue, previousValue, null, state);
        }

        var percentage = decimal.Round(
            ((decimal)currentValue - previousValue) / previousValue * 100m,
            1,
            MidpointRounding.AwayFromZero);
        var comparisonState = currentValue.CompareTo(previousValue) switch
        {
            > 0 => MetricComparisonState.Increase,
            < 0 => MetricComparisonState.Decrease,
            _ => MetricComparisonState.NoChange
        };

        return new MetricComparison(currentValue, previousValue, percentage, comparisonState);
    }
}
