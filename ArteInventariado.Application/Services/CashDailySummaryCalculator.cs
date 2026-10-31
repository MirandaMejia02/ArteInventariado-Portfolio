using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Services;

/// <summary>
/// Combines movements for one operating day. Per-session counts remain snapshots;
/// their informational sum is kept separate from the actual final physical count.
/// </summary>
public static class CashDailySummaryCalculator
{
    public static IReadOnlyList<CashDailySummary> Aggregate(IEnumerable<CashSummary> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        return sessions
            .GroupBy(session => session.FechaOperacion)
            .Select(group => SummarizeDay(group.Key, group))
            .OrderByDescending(summary => summary.FechaOperacion)
            .ToList();
    }

    public static CashDailySummary SummarizeDay(DateOnly date, IEnumerable<CashSummary> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var ordered = sessions
            .Where(session => session.FechaOperacion == date)
            .OrderBy(session => session.FechaApertura)
            .ThenBy(session => session.CajaId)
            .ToList();

        if (ordered.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos una sesión de caja para resumir el día.", nameof(sessions));
        }

        long entries = 0;
        long outputs = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            var session = ordered[index];
            entries = checked(entries + PhysicalEntries(session));
            outputs = checked(outputs + PhysicalOutputs(session));
        }

        var missingIntermediateClose = ordered
            .Take(Math.Max(0, ordered.Count - 1))
            .Any(session => !session.MontoCierreCentavos.HasValue);

        long? openingAdjustments = null;
        long? expected = null;
        if (!missingIntermediateClose)
        {
            long adjustments = 0;
            for (var index = 1; index < ordered.Count; index++)
            {
                adjustments = checked(adjustments +
                    ordered[index].MontoInicialCentavos - ordered[index - 1].MontoCierreCentavos!.Value);
            }

            openingAdjustments = adjustments;
            expected = checked(ordered[0].MontoInicialCentavos + entries - outputs + adjustments);
        }

        var finalCount = ordered[^1].MontoCierreCentavos;
        var sessionBalances = ordered
            .Select(session => session.MontoCierreCentavos ??
                (session.Estado == EstadoCaja.ABIERTA ? session.TotalEsperadoCentavos : (long?)null))
            .Where(balance => balance.HasValue)
            .Select(balance => balance!.Value)
            .ToList();
        long? sessionBalancesSum = missingIntermediateClose || sessionBalances.Count == 0
            ? null
            : sessionBalances.Aggregate(0L, (total, balance) => checked(total + balance));
        long? difference = expected.HasValue && finalCount.HasValue
            ? finalCount.Value - expected.Value
            : null;
        return new CashDailySummary(
            date,
            ordered.Count,
            ordered[0].MontoInicialCentavos,
            entries,
            outputs,
            openingAdjustments,
            expected,
            sessionBalancesSum,
            finalCount,
            difference,
            missingIntermediateClose);
    }

    private static long PhysicalEntries(CashSummary session) => checked(
        session.VentasEfectivoCentavos +
        session.AbonosClientesEfectivoCentavos +
        session.AnticiposPedidosEfectivoCentavos +
        session.AbonosPedidosEfectivoCentavos +
        session.PagosFinalesPedidosEfectivoCentavos +
        session.TotalAportesCajaCentavos);

    private static long PhysicalOutputs(CashSummary session) => checked(
        session.TotalReembolsosCentavos +
        session.TotalPagosProveedoresCentavos +
        session.TotalPagosEmpleadosCentavos +
        session.TotalGastosCentavos +
        session.TotalRetirosCentavos);
}
