using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed partial class CashRecoveryServiceTests
{
    [Fact]
    public async Task PreviousDayCashReference_AllowsAdminAsReadOnlyReference()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.ADMIN);
        var priorCloseId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            8_000,
            12_345);

        var reference = await fixture.Service.GetPreviousDayCashOpeningReferenceAsync();

        Assert.NotNull(reference);
        Assert.Equal(priorCloseId, reference.CajaId);
        Assert.Equal(12_345, reference.MontoCierreCentavos);
        Assert.Equal(0, await fixture.CountOpenCashAsync());
    }

    [Fact]
    public async Task NextDayCashReference_ContinuesAcrossGapsUsingNewestPhysicalCount()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);
        await fixture.GrantPermissionAsync(PermisoEmpleado.CAJA_ABRIR);
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 14), 5_000, 8_000);
        var latestCashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            8_000,
            12_345);
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 13), 2_000, 3_000);

        await using (var context = fixture.CreateContext())
        {
            var closes = await context.Cajas
                .Where(cash => cash.FechaOperacion == new DateOnly(2026, 1, 14))
                .OrderBy(cash => cash.CajaId)
                .ToListAsync();
            closes[0].FechaCierre = new DateTime(2026, 1, 14, 18, 0, 0, DateTimeKind.Utc);
            closes[1].FechaCierre = new DateTime(2026, 1, 14, 20, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();
        }

        var reference = await fixture.Service.GetPreviousDayCashOpeningReferenceAsync();

        Assert.NotNull(reference);
        Assert.Equal(latestCashId, reference.CajaId);
        Assert.Equal(new DateOnly(2026, 1, 14), reference.FechaOperacion);
        Assert.Equal(12_345, reference.MontoCierreCentavos);
        Assert.Equal(1, reference.CantidadSesionesIncluidas);
    }

    [Fact]
    public async Task SameDayCashReference_UsesMostRecentPhysicalCountInsteadOfSummingSessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 15), 5_000, 8_000);
        var latestCashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 15),
            8_000,
            12_345);

        var reference = await fixture.Service.GetPreviousDayCashOpeningReferenceAsync();

        Assert.NotNull(reference);
        Assert.Equal(latestCashId, reference.CajaId);
        Assert.Equal(new DateOnly(2026, 1, 15), reference.FechaOperacion);
        Assert.Equal(12_345, reference.MontoCierreCentavos);
        Assert.Equal(1, reference.CantidadSesionesIncluidas);
    }

    [Fact]
    public async Task PreviousDayCashReference_RequiresEmployeeCashOpenPermission()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.GetPreviousDayCashOpeningReferenceAsync());

        Assert.Contains("permiso", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LastClosedCashReference_IsAvailableAcrossDaysButUnavailableWhileAnyCashIsOpen()
    {
        await using var fixture = await Fixture.CreateAsync();
        var olderCloseId = await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 13), 2_000, 3_000);
        var olderReference = await fixture.Service.GetPreviousDayCashOpeningReferenceAsync();
        Assert.Equal(olderCloseId, olderReference!.CajaId);

        var todayOpen = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 4_000);
        Assert.Null(await fixture.Service.GetPreviousDayCashOpeningReferenceAsync());
        await fixture.AssertCashStateAsync(todayOpen, EstadoCaja.ABIERTA);
    }

    [Fact]
    public async Task OpenCash_RequiresReasonWhenOpeningCountDiffersFromLastClosure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var previousCashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            8_000,
            12_345);

        var missingReason = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.OpenCashAsync(new OpenCashRequest(
                12_300,
                CajaCierreAnteriorId: previousCashId)));
        Assert.Contains("motivo", missingReason.Message, StringComparison.OrdinalIgnoreCase);

        var amountMismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.OpenCashAsync(new OpenCashRequest(
                12_300,
                CajaCierreAnteriorId: previousCashId,
                ConfirmoEfectivoIntacto: true)));
        Assert.Contains("no coincide", amountMismatch.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await fixture.CountOpenCashAsync());
    }

    [Fact]
    public async Task OpenCash_DifferentCountKeepsPreviousClosureAndAuditsOpeningReason()
    {
        await using var fixture = await Fixture.CreateAsync();
        var previousCashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            8_000,
            12_345);
        const string reason = "Se contó el efectivo y faltaban 45 centavos.";

        var opened = await fixture.Service.OpenCashAsync(new OpenCashRequest(
            12_300,
            CajaCierreAnteriorId: previousCashId,
            MotivoVariacionCierreAnterior: reason));

        var previousCash = await fixture.ReadCashAsync(previousCashId);
        var newCash = await fixture.ReadCashAsync(opened.CajaId!.Value);
        var audit = await fixture.ReadAuditAsync(TipoBitacora.CAJA_ABIERTA, opened.CajaId.Value);

        Assert.Equal(EstadoCaja.CERRADA, previousCash.Estado);
        Assert.Equal(12_345, previousCash.MontoCierreCentavos);
        Assert.Equal(EstadoCaja.ABIERTA, newCash.Estado);
        Assert.Equal(12_300, newCash.MontoInicialCentavos);
        Assert.Contains(reason, newCash.Observaciones, StringComparison.Ordinal);
        Assert.Contains(reason, audit.Descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenCash_EqualManualCountIsAuditedWithoutClaimingCashWasUntouched()
    {
        await using var fixture = await Fixture.CreateAsync();
        var previousCashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            8_000,
            12_345);

        var opened = await fixture.Service.OpenCashAsync(new OpenCashRequest(
            12_345,
            CajaCierreAnteriorId: previousCashId));

        var newCash = await fixture.ReadCashAsync(opened.CajaId!.Value);
        var audit = await fixture.ReadAuditAsync(TipoBitacora.CAJA_ABIERTA, opened.CajaId.Value);
        Assert.Contains("[CONTEO_APERTURA_CAJA]", newCash.Observaciones);
        Assert.Contains("Conteo manual coincide", audit.Descripcion, StringComparison.Ordinal);
        Assert.DoesNotContain("efectivo intacto confirmado", audit.Descripcion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(12_345, (await fixture.ReadCashAsync(previousCashId)).MontoCierreCentavos);
    }

    [Fact]
    public async Task AutomaticOpening_WaitsForCountWhenAPreviousClosureExists()
    {
        await using var fixture = await Fixture.CreateAsync();
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 14), 8_000, 12_345);
        await using (var context = fixture.CreateContext())
        {
            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = "caja.apertura_automatica",
                Valor = "true"
            });
            await context.SaveChangesAsync();
        }

        var summary = await fixture.Service.OpenCashAutomaticallyIfEnabledAsync();

        Assert.Null(summary.CajaId);
        Assert.Equal(0, await fixture.CountOpenCashAsync());
        Assert.NotNull(await fixture.Service.GetPreviousDayCashOpeningReferenceAsync());
    }

    [Fact]
    public async Task OpenCash_CarryForwardUsesPreviousCountAsOpeningAndAuditsConfirmationWithoutCreatingMovements()
    {
        await using var fixture = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO);
        await fixture.GrantPermissionAsync(PermisoEmpleado.CAJA_ABRIR);
        await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            8_000,
            8_000);
        var previousCashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            8_000,
            12_345);
        var reference = await fixture.Service.GetPreviousDayCashOpeningReferenceAsync();
        Assert.NotNull(reference);

        var opened = await fixture.Service.OpenCashAsync(new OpenCashRequest(
            reference.MontoCierreCentavos,
            CajaCierreAnteriorId: reference.CajaId,
            ConfirmoEfectivoIntacto: true));

        var stored = await fixture.ReadCashAsync(opened.CajaId!.Value);
        var audit = await fixture.ReadAuditAsync(TipoBitacora.CAJA_ABIERTA, opened.CajaId.Value);
        await using var context = fixture.CreateContext();

        Assert.Equal(previousCashId, reference.CajaId);
        Assert.Equal(1, reference.CantidadSesionesIncluidas);
        Assert.Equal(12_345, stored.MontoInicialCentavos);
        Assert.Contains("[CONTINUIDAD_CAJA]", stored.Observaciones);
        Assert.Contains($"caja #{previousCashId}", stored.Observaciones, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"caja #{previousCashId}", audit.Descripcion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(12_345, opened.TotalEsperadoCentavos);
        Assert.Equal(0, opened.VentasEfectivoCentavos);
        Assert.Equal(0, await context.Ventas.CountAsync());
        Assert.Equal(0, await context.RetirosCaja.CountAsync());
        Assert.Equal(1, await fixture.CountOpenCashAsync());
    }

    [Fact]
    public async Task OpenCash_CarryForwardRejectsStaleReferenceAfterTodayHasSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        var previousCashId = await AddClosedSessionAsync(
            fixture,
            new DateOnly(2026, 1, 14),
            5_000,
            6_000);
        await AddClosedSessionAsync(fixture, new DateOnly(2026, 1, 15), 6_000, 6_000);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.OpenCashAsync(new OpenCashRequest(
                6_000,
                CajaCierreAnteriorId: previousCashId,
                ConfirmoEfectivoIntacto: true)));

        Assert.Contains("referencia de apertura cambió", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await fixture.CountOpenCashAsync());
    }
}
