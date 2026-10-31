using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Services;

public static class OpenCashLogoutPolicy
{
    public static bool ShouldLogOut(CashRecoveryStatus? recovery, bool confirmedToLeaveCashOpen) =>
        recovery is null || confirmedToLeaveCashOpen;

    public static string BuildConfirmationMessage(CashRecoveryStatus recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        var cashLabel = recovery.Cash.CajaId.HasValue
            ? $"#{recovery.Cash.CajaId.Value}"
            : "activa";
        var additionalCashMessage = recovery.AdditionalOpenCashSessions > 0
            ? $"\r\nAdemás, hay {recovery.AdditionalOpenCashSessions} otras cajas abiertas que deberán revisarse."
            : string.Empty;

        return $"Hay una caja abierta ({cashLabel}, sesión {recovery.Cash.FechaOperacion:dd/MM/yyyy}).\r\n\r\n" +
            "Al cerrar sesión, la caja permanecerá abierta. No se registrará un cierre ni un efectivo contado automáticamente." +
            additionalCashMessage +
            "\r\n\r\nElija No para cancelar el cierre de sesión y permanecer en el panel. La caja solo se cierra mediante el flujo autorizado." +
            "\r\n\r\n¿Desea cerrar sesión dejando la caja abierta?";
    }
}
