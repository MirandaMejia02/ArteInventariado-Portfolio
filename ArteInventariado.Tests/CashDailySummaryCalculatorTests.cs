using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Tests;

public sealed class CashDailySummaryCalculatorTests
{
    [Fact]
    public void Aggregate_AddsDayMovementsButUsesOnlyTheLastClosure()
    {
        var date = new DateOnly(2026, 10, 9);
        var sessions = new[]
        {
            Session(1, date, 10_000, 14_000, 13_000, 5_000, 1_000, 9),
            Session(2, date, 13_000, 15_500, 16_000, 3_000, 500, 18)
        };

        var summary = Assert.Single(CashDailySummaryCalculator.Aggregate(sessions));

        Assert.Equal(2, summary.CantidadSesiones);
        Assert.Equal(10_000, summary.AperturaInicialCentavos);
        Assert.Equal(8_000, summary.EntradasFisicasCentavos);
        Assert.Equal(1_500, summary.SalidasFisicasCentavos);
        Assert.Equal(0, summary.AjustesEntreSesionesCentavos);
        Assert.Equal(16_500, summary.EfectivoEsperadoAcumuladoCentavos);
        Assert.Equal(16_000, summary.UltimoCierreContadoCentavos);
        Assert.Equal(-500, summary.DiferenciaFinalCentavos);
    }

    [Fact]
    public void Aggregate_SeparatesReopeningCountAdjustmentFromBusinessMovements()
    {
        var date = new DateOnly(2026, 10, 9);
        var sessions = new[]
        {
            Session(1, date, 10_000, 14_000, 13_000, 5_000, 1_000, 9),
            Session(2, date, 13_500, 16_000, 16_000, 3_000, 500, 18)
        };

        var summary = Assert.Single(CashDailySummaryCalculator.Aggregate(sessions));

        Assert.Equal(500, summary.AjustesEntreSesionesCentavos);
        Assert.Equal(17_000, summary.EfectivoEsperadoAcumuladoCentavos);
        Assert.Equal(16_000, summary.UltimoCierreContadoCentavos);
        Assert.Equal(-1_000, summary.DiferenciaFinalCentavos);
    }

    [Fact]
    public void Aggregate_AccumulatesExpectedCashAndKeepsLastPhysicalCloseSeparate()
    {
        var date = new DateOnly(2026, 10, 13);
        var sessions = new[]
        {
            Session(18, date, 11_974, 8_975, 8_975, 57, 3_056, 18),
            Session(19, date, 5_112, 5_112, 5_112, 0, 0, 20)
        };

        var summary = Assert.Single(CashDailySummaryCalculator.Aggregate(sessions));

        Assert.Equal(5_112, summary.EfectivoEsperadoAcumuladoCentavos);
        Assert.Equal(14_087, summary.SumaSaldosPorSesionCentavos);
        Assert.Equal(5_112, summary.UltimoCierreContadoCentavos);
    }

    [Fact]
    public void Aggregate_KeepsExpectedForOpenLastSessionAndDoesNotInventIntermediateCounts()
    {
        var date = new DateOnly(2026, 10, 9);
        var open = Session(2, date, 13_000, 15_500, null, 3_000, 500, 18) with
        {
            Estado = EstadoCaja.ABIERTA,
            FechaCierre = null
        };

        var summary = Assert.Single(CashDailySummaryCalculator.Aggregate([
            Session(1, date, 10_000, 14_000, 13_000, 5_000, 1_000, 9), open]));

        Assert.Equal(16_500, summary.EfectivoEsperadoAcumuladoCentavos);
        Assert.Equal(28_500, summary.SumaSaldosPorSesionCentavos);
        Assert.Null(summary.UltimoCierreContadoCentavos);
        Assert.Null(summary.DiferenciaFinalCentavos);
        Assert.False(summary.TieneCierreIntermedioSinConteo);

        var missingIntermediate = Session(1, date, 10_000, 14_000, null, 5_000, 1_000, 9);
        var unavailable = CashDailySummaryCalculator.SummarizeDay(date, [missingIntermediate, open]);
        Assert.True(unavailable.TieneCierreIntermedioSinConteo);
        Assert.Null(unavailable.EfectivoEsperadoAcumuladoCentavos);
    }

    private static CashSummary Session(
        int id,
        DateOnly date,
        long opening,
        long expected,
        long? closing,
        long entries,
        long outputs,
        int hour)
    {
        var openedAt = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Local);
        return new CashSummary(
            id,
            date,
            closing.HasValue ? EstadoCaja.CERRADA : EstadoCaja.ABIERTA,
            opening,
            entries,
            outputs,
            0,
            0,
            expected,
            closing,
            openedAt,
            closing.HasValue ? openedAt.AddHours(1) : null,
            "admin",
            closing.HasValue ? "admin" : null,
            VentasEfectivoCentavos: entries);
    }
}
