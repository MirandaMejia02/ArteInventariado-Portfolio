using System.Numerics;

namespace ArteInventariado.Application.Services;

public static class ProportionalAmountAllocator
{
    public static IReadOnlyList<long> Allocate(long amount, IReadOnlyList<long> bases)
    {
        ArgumentNullException.ThrowIfNull(bases);

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "El monto a distribuir no puede ser negativo.");
        }

        if (bases.Any(value => value < 0))
        {
            throw new ArgumentException("Las bases de distribucion no pueden ser negativas.", nameof(bases));
        }

        var allocations = new long[bases.Count];
        if (amount == 0 || bases.Count == 0)
        {
            return allocations;
        }

        var totalBase = bases.Aggregate(BigInteger.Zero, (sum, value) => sum + value);
        if (totalBase == BigInteger.Zero)
        {
            throw new ArgumentException("No se puede distribuir un monto positivo entre bases iguales a cero.", nameof(bases));
        }

        var remainders = new List<(int Index, BigInteger Remainder)>(bases.Count);
        var allocated = 0L;
        for (var index = 0; index < bases.Count; index++)
        {
            var weightedAmount = new BigInteger(amount) * bases[index];
            allocations[index] = (long)(weightedAmount / totalBase);
            allocated += allocations[index];
            remainders.Add((index, weightedAmount % totalBase));
        }

        var remaining = amount - allocated;
        foreach (var item in remainders
            .OrderByDescending(item => item.Remainder)
            .ThenBy(item => item.Index)
            .Take(checked((int)remaining)))
        {
            allocations[item.Index]++;
        }

        return allocations;
    }
}
