using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Tests;

public sealed class OpenCashLogoutPolicyTests
{
    [Fact]
    public void NoOpenCash_AllowsLogoutWithoutConfirmation()
    {
        Assert.True(OpenCashLogoutPolicy.ShouldLogOut(null, confirmedToLeaveCashOpen: false));
    }

    [Fact]
    public void OpenCash_CancelingConfirmationKeepsSessionOpen()
    {
        Assert.False(OpenCashLogoutPolicy.ShouldLogOut(CreateRecovery(), confirmedToLeaveCashOpen: false));
    }

    [Fact]
    public void OpenCash_ExplicitConfirmationAllowsLogoutWithoutClosingCash()
    {
        Assert.True(OpenCashLogoutPolicy.ShouldLogOut(CreateRecovery(), confirmedToLeaveCashOpen: true));
    }

    [Fact]
    public void ConfirmationMessageExplainsCashWillRemainOpen()
    {
        var message = OpenCashLogoutPolicy.BuildConfirmationMessage(CreateRecovery());

        Assert.Contains("caja abierta (#12, sesión 04/10/2026)", message);
        Assert.Contains("permanecerá abierta", message);
        Assert.Contains("No se registrará un cierre ni un efectivo contado automáticamente", message);
        Assert.Contains("¿Desea cerrar sesión dejando la caja abierta?", message);
    }

    [Fact]
    public void ConfirmationMessageIncludesOtherOpenCashSessions()
    {
        var message = OpenCashLogoutPolicy.BuildConfirmationMessage(CreateRecovery(additionalOpenCashSessions: 2));

        Assert.Contains("2 otras cajas abiertas", message);
    }

    private static CashRecoveryStatus CreateRecovery(int additionalOpenCashSessions = 0)
    {
        var cash = new CashSummary(
            CajaId: 12,
            FechaOperacion: new DateOnly(2026, 10, 4),
            Estado: EstadoCaja.ABIERTA,
            MontoInicialCentavos: 10_000,
            TotalVentasCentavos: 0,
            TotalRetirosCentavos: 0,
            TotalGastosCentavos: 0,
            TotalReembolsosCentavos: 0,
            TotalEsperadoCentavos: 10_000,
            MontoCierreCentavos: null,
            FechaApertura: DateTime.UtcNow,
            FechaCierre: null,
            UsuarioApertura: "admin",
            UsuarioCierre: null);

        return new CashRecoveryStatus(
            cash,
            IsFromPreviousDay: false,
            AdditionalOpenCashSessions: additionalOpenCashSessions);
    }
}
