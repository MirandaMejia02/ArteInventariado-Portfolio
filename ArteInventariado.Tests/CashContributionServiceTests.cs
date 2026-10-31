using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed partial class CashRecoveryServiceTests
{
    [Fact]
    public async Task Contribution_IncreasesExpectedCashAndIsNotRecordedAsSaleOrExpense()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);

        var saved = await fixture.Service.RegisterCashContributionAsync(new(3_500, "Fondo para dar cambio"));
        var summary = await fixture.Service.GetTodaySummaryAsync();

        Assert.Equal(cashId, saved.CajaId);
        Assert.Equal(fixture.UserId, saved.UsuarioId);
        Assert.Equal("recovery-user", saved.Usuario);
        Assert.Equal(3_500, saved.MontoCentavos);
        Assert.Equal("Fondo para dar cambio", saved.Motivo);
        Assert.Equal(fixture.TimeProvider.GetUtcNow().UtcDateTime, saved.FechaAporte);
        Assert.Equal(13_500, summary.TotalEsperadoCentavos);
        Assert.Equal(3_500, summary.TotalAportesCajaCentavos);
        Assert.Equal(0, summary.TotalVentasCentavos);
        Assert.Equal(0, summary.TotalGastosCentavos);

        await using var context = fixture.CreateContext();
        var contribution = await context.AportesCaja.AsNoTracking().SingleAsync();
        var audit = await context.Bitacora.AsNoTracking().SingleAsync(entry =>
            entry.TipoAccion == TipoBitacora.APORTE_EFECTIVO_CAJA);
        Assert.Equal(cashId, contribution.CajaId);
        Assert.Equal(fixture.UserId, contribution.UsuarioId);
        Assert.Equal(saved.AporteCajaId, audit.EntidadId);
        Assert.Equal("aportes_caja", audit.Entidad);
        Assert.Contains("Fondo para dar cambio", audit.Descripcion);

        var movementQuery = new CashMovementQueryService(context, new AllowCashMovementQueryPermission());
        var movementDate = new DateTime(2026, 1, 15);
        var movements = await movementQuery.SearchAsync(new(movementDate, movementDate));
        var movement = Assert.Single(movements.Movements, row => row.Origen == CashMovementOrigin.APORTE_CAJA);
        Assert.Equal(cashId, movement.CajaId);
        Assert.Equal(fixture.UserId, movement.UsuarioId);
        Assert.Equal("Fondo para dar cambio", movement.Descripcion);
        Assert.Equal(3_500, movement.EntradaCentavos);
        Assert.Equal(0, movement.SalidaCentavos);
        Assert.Equal(13_500, movement.EfectivoDespuesCentavos);
        Assert.Equal(3_500, movements.Summary.AportesCajaCentavos);
        Assert.Equal(0, movements.Summary.VentasEfectivoCentavos);
        Assert.Equal(13_500, movements.Summary.EfectivoEsperadoCentavos);
    }

    [Fact]
    public async Task CashMovementSummary_UsesFirstOpeningAndLastClosureAcrossSessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateOnly(2026, 1, 15);
        var first = await fixture.AddOpenCashAsync(date, 10_000);
        await fixture.Service.CloseCashAsync(new CloseCashRequest(12_000));
        await fixture.Service.OpenCashAsync(new OpenCashRequest(
            13_000,
            CajaCierreAnteriorId: first,
            MotivoVariacionCierreAnterior: "Conteo diferente al cierre anterior"));
        await fixture.Service.CloseCashAsync(new CloseCashRequest(11_500));

        await using var context = fixture.CreateContext();
        var movementQuery = new CashMovementQueryService(context, new AllowCashMovementQueryPermission());
        var start = date.ToDateTime(TimeOnly.MinValue);
        var result = await movementQuery.SearchAsync(new(start, start));

        Assert.Equal(2, await context.Cajas.CountAsync(cash => cash.FechaOperacion == date));
        Assert.Equal(10_000, result.Summary.MontoAperturaCentavos);
        Assert.Equal(11_000, result.Summary.EfectivoEsperadoCentavos);
        Assert.Equal(11_500, result.Summary.EfectivoContadoCentavos);
        Assert.Equal(500, result.Summary.DiferenciaCentavos);

        var closureAdjustments = result.Movements
            .Where(row => row.Origen == CashMovementOrigin.AJUSTE_CIERRE_CAJA)
            .ToList();
        var reopeningAdjustments = result.Movements
            .Where(row => row.Origen == CashMovementOrigin.AJUSTE_APERTURA_CAJA)
            .ToList();
        Assert.Equal(
            new long?[] { 11_500, 12_000 },
            closureAdjustments.Select(row => row.EfectivoDespuesCentavos).Order().ToArray());
        var openingAdjustment = Assert.Single(reopeningAdjustments);
        Assert.Contains(closureAdjustments, row => row.EntradaCentavos == 2_000 && row.EfectivoDespuesCentavos == 12_000);
        Assert.Equal(1_000, openingAdjustment.EntradaCentavos);
        Assert.Equal(13_000, openingAdjustment.EfectivoDespuesCentavos);
        Assert.Contains(closureAdjustments, row => row.SalidaCentavos == 1_500 && row.EfectivoDespuesCentavos == 11_500);
    }

    [Theory]
    [InlineData(0, "Cambio")]
    [InlineData(-100, "Cambio")]
    [InlineData(100, "   ")]
    public async Task Contribution_RequiresPositiveAmountAndReason(long amount, string reason)
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.RegisterCashContributionAsync(new(amount, reason)));

        await using var context = fixture.CreateContext();
        Assert.Equal(0, await context.AportesCaja.CountAsync());
        Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.APORTE_EFECTIVO_CAJA, cashId));
        Assert.Equal(10_000, (await fixture.Service.GetTodaySummaryAsync()).TotalEsperadoCentavos);
    }

    [Fact]
    public async Task Contribution_EmployeeNeedsExplicitPermissionAndConsultaCannotWrite()
    {
        await using (var employee = await Fixture.CreateAsync(role: RolUsuario.EMPLEADO))
        {
            await employee.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                employee.Service.RegisterCashContributionAsync(new(500, "Cambio")));

            await employee.GrantPermissionAsync(PermisoEmpleado.CAJA_APORTE_REGISTRAR);
            var saved = await employee.Service.RegisterCashContributionAsync(new(500, "Cambio"));
            Assert.Equal(500, saved.MontoCentavos);
            Assert.DoesNotContain(
                EmployeePermissionCatalog.RecommendedExistingEmployeePermissions,
                permission => permission == PermisoEmpleado.CAJA_APORTE_REGISTRAR);
        }

        await using (var consulta = await Fixture.CreateAsync(allowsWrites: false))
        {
            await consulta.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                consulta.Service.RegisterCashContributionAsync(new(500, "Cambio")));
            await using var context = consulta.CreateContext();
            Assert.Equal(0, await context.AportesCaja.CountAsync());
        }
    }

    [Fact]
    public async Task Contribution_AuditFailureRollsBackTheCashMovement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashId = await fixture.AddOpenCashAsync(new DateOnly(2026, 1, 15), 10_000);
        await fixture.ExecuteSqlAsync("""
            CREATE TRIGGER fail_cash_contribution_audit
            BEFORE INSERT ON bitacora
            WHEN NEW.tipo_accion = 'APORTE_EFECTIVO_CAJA'
            BEGIN
                SELECT RAISE(ABORT, 'simulated audit failure');
            END;
            """);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            fixture.Service.RegisterCashContributionAsync(new(500, "Cambio de efectivo")));

        await using var context = fixture.CreateContext();
        Assert.Equal(0, await context.AportesCaja.CountAsync());
        Assert.Equal(0, await fixture.CountAuditsAsync(TipoBitacora.APORTE_EFECTIVO_CAJA, cashId));
        Assert.Equal(10_000, (await fixture.Service.GetTodaySummaryAsync()).TotalEsperadoCentavos);
    }

    private sealed class AllowCashMovementQueryPermission : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(PermisoEmpleado permiso, int? usuarioId = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HasAnyPermissionAsync(IReadOnlyCollection<PermisoEmpleado> permisos, int? usuarioId = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EnsurePermissionAsync(PermisoEmpleado permiso, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePermissionsAsync(UpdateEmployeePermissionsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreRecommendedTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplySafeOperationalTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsureRecommendedPermissionsForEmployeeAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
