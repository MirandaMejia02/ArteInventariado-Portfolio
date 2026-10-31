using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed partial class CashRecoveryServiceTests
{
    [Fact]
    public async Task ExternalExpense_CanBeRegisteredWithoutOpenCashAndDoesNotCreateCashEffect()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.RegisterExpenseAsync(ExternalExpense(3_250));

        Assert.Null(result.CajaId);
        Assert.False(result.AfectaCaja);
        Assert.Equal(OrigenFondos.FONDOS_EXTERNOS_DUENO, result.OrigenFondos);
        Assert.Equal("Dueño", result.ResponsableOrigen);
        Assert.Single(await fixture.Service.GetTodayExpensesAsync());
        var cash = await fixture.Service.GetTodaySummaryAsync();
        Assert.Null(cash.CajaId);
        Assert.Equal(0, cash.TotalGastosCentavos);
    }

    [Fact]
    public async Task CashExpense_RequiresOpenCashAndCannotExceedAvailableCash()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = CashExpense(2_000);

        var withoutCash = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterExpenseAsync(request));
        Assert.Contains("sin caja abierta", withoutCash.Message, StringComparison.OrdinalIgnoreCase);

        await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 5_000);
        var registered = await fixture.Service.RegisterExpenseAsync(request);
        Assert.True(registered.AfectaCaja);

        var insufficient = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterExpenseAsync(CashExpense(3_001)));
        Assert.Contains("insuficiente", insufficient.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3_000, (await fixture.Service.GetTodaySummaryAsync()).TotalEsperadoCentavos);
        Assert.Single(await fixture.Service.GetTodayExpensesAsync());
    }

    [Fact]
    public async Task ExternalCashMethod_LeavesDrawerBalanceUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 5_000);

        var result = await fixture.Service.RegisterExpenseAsync(ExternalExpense(2_000));

        Assert.Null(result.CajaId);
        Assert.False(result.AfectaCaja);
        Assert.Equal(5_000, (await fixture.Service.GetTodaySummaryAsync()).TotalEsperadoCentavos);
        Assert.Equal(2_000, (await fixture.Service.GetTodayExpensesAsync()).Single().MontoCentavos);
    }

    [Fact]
    public async Task ExpenseOperation_IsIdempotentAndRejectsChangedPayload()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = ExternalExpense(1_250);

        var first = await fixture.Service.RegisterExpenseAsync(request);
        var replay = await fixture.Service.RegisterExpenseAsync(request);
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterExpenseAsync(request with { MontoCentavos = 1_500 }));

        Assert.Equal(first.GastoId, replay.GastoId);
        Assert.Contains("otro gasto", conflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await fixture.Service.GetTodayExpensesAsync());
        Assert.Equal(1, await fixture.CountAuditsAsync(TipoBitacora.GASTO_REGISTRADO, first.GastoId));
    }

    [Fact]
    public async Task ExpenseRejectsInvalidFundingCombinationsAndMissingExternalResponsible()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegisterExpenseAsync(
            new RegisterExpenseRequest(100, "Transferencia desde caja", MetodoPago: MetodoPago.TRANSFERENCIA,
                Referencia: "TRX-1", OrigenFondos: OrigenFondos.CAJA_FISICA, OperacionId: Guid.NewGuid())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegisterExpenseAsync(
            new RegisterExpenseRequest(100, "Fondo externo sin responsable", OrigenFondos: OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA,
                OperacionId: Guid.NewGuid())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegisterExpenseAsync(
            new RegisterExpenseRequest(100, "Transferencia sin referencia", MetodoPago: MetodoPago.TRANSFERENCIA,
                OrigenFondos: OrigenFondos.FONDOS_EXTERNOS_DUENO, ResponsableOrigen: "Dueño", OperacionId: Guid.NewGuid())));

        Assert.Empty(await fixture.Service.GetTodayExpensesAsync());
    }

    [Fact]
    public async Task ExpenseAuditFailure_RollsBackExpenseAndCashEffect()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 5_000);
        await fixture.ExecuteSqlAsync("""
            CREATE TRIGGER fail_expense_audit
            BEFORE INSERT ON bitacora
            WHEN NEW.tipo_accion = 'GASTO_REGISTRADO'
            BEGIN
                SELECT RAISE(ABORT, 'simulated expense audit failure');
            END;
            """);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            fixture.Service.RegisterExpenseAsync(CashExpense(1_000)));

        Assert.Empty(await fixture.Service.GetTodayExpensesAsync());
        Assert.Equal(5_000, (await fixture.Service.GetTodaySummaryAsync()).TotalEsperadoCentavos);
    }

    [Fact]
    public async Task ConsultaStation_RejectsExpenseWriteEvenForExternalFunds()
    {
        await using var fixture = await Fixture.CreateAsync(allowsWrites: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterExpenseAsync(ExternalExpense(500)));
        Assert.Empty(await fixture.Service.GetTodayExpensesAsync());
    }

    private static RegisterExpenseRequest ExternalExpense(long amount) => new(
        amount,
        "Gasto pagado fuera de caja",
        MetodoPago: MetodoPago.EFECTIVO,
        OrigenFondos: OrigenFondos.FONDOS_EXTERNOS_DUENO,
        ResponsableOrigen: "Dueño",
        OperacionId: Guid.NewGuid());

    private static RegisterExpenseRequest CashExpense(long amount) => new(
        amount,
        "Gasto pagado desde caja",
        MetodoPago: MetodoPago.EFECTIVO,
        OrigenFondos: OrigenFondos.CAJA_FISICA,
        OperacionId: Guid.NewGuid());
}
