using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed partial class CashRecoveryServiceTests
{
    [Fact]
    public async Task SessionHistory_ReturnsInclusivePeriodSortedByDateAndSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 9), 8_000, 8_000);
        var firstSameDay = await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 10), 10_000, 12_500);
        var secondSameDay = await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 10), 2_000, 1_500);
        var openSession = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 11), 3_500);
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 12), 5_000, 5_000);

        var history = await fixture.Service.GetSessionHistoryAsync(
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 1, 11));

        Assert.Equal(3, history.Count);
        Assert.Equal(new DateOnly(2026, 1, 11), history[0].FechaOperacion);
        Assert.Equal(openSession, history[0].CajaId);
        Assert.Null(history[0].MontoCierreCentavos);
        Assert.Equal(3_500, history[0].TotalEsperadoCentavos);
        Assert.Equal(new DateOnly(2026, 1, 10), history[1].FechaOperacion);
        Assert.Equal(secondSameDay, history[1].CajaId);
        Assert.Equal(1_500, history[1].MontoCierreCentavos);
        Assert.Equal(-500, history[1].DiferenciaCentavos);
        Assert.Equal(firstSameDay, history[2].CajaId);
        Assert.Equal(12_500, history[2].MontoCierreCentavos);
        Assert.Equal(2_500, history[2].DiferenciaCentavos);

        var persisted = await fixture.ReadCashAsync(firstSameDay);
        Assert.Equal(12_500, persisted.MontoCierreCentavos);
        Assert.Equal(EstadoCaja.CERRADA, persisted.Estado);
    }

    [Fact]
    public async Task SessionHistory_RequiresHistoryPermissionForEmployees()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.GetSessionHistoryAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15)));

        Assert.Contains("permiso", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionHistory_AllowsEmployeeWhenAdminDelegatesDedicatedReadPermission()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);
        await fixture.GrantPermissionAsync(PermisoEmpleado.CAJA_HISTORIAL_VER);
        var cashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            10_000,
            9_500);

        var history = await fixture.Service.GetSessionHistoryAsync(
            new DateOnly(2026, 1, 14),
            new DateOnly(2026, 1, 14));

        Assert.Equal(cashId, Assert.Single(history).CajaId);
    }

    [Fact]
    public async Task SessionHistory_IsReadOnlyAndAvailableInConsultaMode()
    {
        await using var fixture = await Fixture.CreateAsync(allowsWrites: false);
        var cashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            10_000,
            9_500);

        var history = await fixture.Service.GetSessionHistoryAsync(
            new DateOnly(2026, 1, 14),
            new DateOnly(2026, 1, 14));

        var session = Assert.Single(history);
        Assert.Equal(cashId, session.CajaId);
        Assert.Equal(9_500, session.MontoCierreCentavos);
        Assert.Equal(-500, session.DiferenciaCentavos);
    }

    [Fact]
    public async Task SessionHistory_RejectsReversedDateRange()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.GetSessionHistoryAsync(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 14)));
    }

    [Fact]
    public async Task TodayExpectedCashBalance_AggregatesSessionsWithoutAddingTheirClosingSnapshots()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);
        await fixture.GrantPermissionAsync(PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER);
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 15), 10_000, 10_000);
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 15), 11_000, 11_000);
        await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 11_500);

        var summary = await fixture.Service.GetTodayExpectedCashBalanceAsync();

        Assert.Equal(3, summary.CantidadSesiones);
        Assert.Equal(11_500, summary.EfectivoEsperadoCentavos);
        Assert.False(summary.TieneCierreIntermedioSinConteo);
    }

    [Fact]
    public async Task TodayExpectedCashBalance_RequiresDedicatedEmployeePermission()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.GetTodayExpectedCashBalanceAsync());

        Assert.Contains("permiso", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> AddClosedSessionAsync(
        Fixture fixture,
        DateOnly operationDate,
        long openingCentavos,
        long countedCentavos)
    {
        var cashId = await fixture.AddOpenCashAsync(operationDate, openingCentavos);
        await using var context = fixture.CreateContext();
        var cash = await context.Cajas.SingleAsync(item => item.CajaId == cashId);
        cash.Estado = EstadoCaja.CERRADA;
        cash.MontoCierreCentavos = countedCentavos;
        cash.FechaCierre = new DateTime(
            operationDate.Year,
            operationDate.Month,
            operationDate.Day,
            20,
            0,
            0,
            DateTimeKind.Utc);
        cash.UsuarioCierreId = fixture.UserId;
        await context.SaveChangesAsync();
        return cashId;
    }
}
