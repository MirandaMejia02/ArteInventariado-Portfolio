using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CashRecoveryCoordinator(
    ICashService cashService,
    IEmployeePermissionService permissions,
    IInstallationContext installation)
{
    private int? _acknowledgedCashId;
    private bool _showing;

    public async Task WarnOpenCashWillRemainAsync(
        IWin32Window owner,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var recovery = await cashService.GetOpenCashRecoveryAsync(cancellationToken);
            if (recovery is null)
            {
                return;
            }

            MessageBox.Show(
                $"Existe una caja abierta (#{recovery.Cash.CajaId}, sesión {recovery.Cash.FechaOperacion:dd/MM/yyyy}).\r\n" +
                "Permanecerá abierta al cerrar ArteInventariado; no se registrará un cierre ni un efectivo contado automático.",
                "Caja permanecerá abierta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-open-exit-warning", ex);
        }
    }

    public async Task<bool> ConfirmLogoutWithOpenCashAsync(
        IWin32Window owner,
        CancellationToken cancellationToken = default)
    {
        CashRecoveryStatus? recovery;
        try
        {
            recovery = await cashService.GetOpenCashRecoveryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-open-logout-confirmation", ex);
            return MessageBox.Show(
                owner,
                "No se pudo verificar si hay una caja abierta. No se cerrará ninguna caja automáticamente.\r\n\r\n" +
                "¿Desea cerrar sesión de todos modos?",
                "No se pudo verificar la caja",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        if (recovery is null)
        {
            return true;
        }

        var confirmedToLeaveCashOpen = MessageBox.Show(
            owner,
            OpenCashLogoutPolicy.BuildConfirmationMessage(recovery),
            "Caja abierta",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        return OpenCashLogoutPolicy.ShouldLogOut(recovery, confirmedToLeaveCashOpen);
    }

    public async Task<bool> EnsureReviewedAsync(
        IWin32Window owner,
        bool requireContinuationForOperation,
        CancellationToken cancellationToken = default)
    {
        if (_showing)
        {
            return !requireContinuationForOperation;
        }

        _showing = true;
        try
        {
            while (true)
            {
                var recovery = await cashService.GetOpenCashRecoveryAsync(cancellationToken);
                if (recovery is null)
                {
                    _acknowledgedCashId = null;
                    return true;
                }

                if (_acknowledgedCashId == recovery.Cash.CajaId)
                {
                    return true;
                }

                var readOnly = !installation.AllowsOperationalWrites;
                var canContinue = !readOnly && await permissions.HasPermissionAsync(
                    PermisoEmpleado.CAJA_ABRIR,
                    cancellationToken: cancellationToken);
                var canRegularize = !readOnly && await permissions.HasPermissionAsync(
                    PermisoEmpleado.CAJA_CERRAR,
                    cancellationToken: cancellationToken);
                using var prompt = new CashRecoveryForm(recovery, canContinue, canRegularize, readOnly);
                prompt.ShowDialog(owner);

                if (prompt.Choice == CashRecoveryChoice.Continue)
                {
                    try
                    {
                        await cashService.ContinueCashAsync(
                            new ContinueCashRequest(recovery.Cash.CajaId!.Value, Guid.NewGuid()),
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        ShowRecoveryError("cash-recovery-continue", ex);
                        continue;
                    }

                    _acknowledgedCashId = recovery.Cash.CajaId;
                    return true;
                }

                if (prompt.Choice == CashRecoveryChoice.Regularize)
                {
                    using var regularization = new CashRegularizationForm(recovery.Cash);
                    if (regularization.ShowDialog(owner) != DialogResult.OK)
                    {
                        continue;
                    }

                    try
                    {
                        await cashService.RegularizeCashAsync(new RegularizeCashRequest(
                            regularization.CashId,
                            regularization.CountedCashCentavos,
                            regularization.Reason,
                            regularization.Password,
                            Guid.NewGuid()), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        ShowRecoveryError("cash-recovery-regularize", ex);
                        continue;
                    }

                    _acknowledgedCashId = null;
                    continue;
                }

                return !requireContinuationForOperation;
            }
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-recovery", ex);
            MessageBox.Show(
                $"No se pudo completar la revisión de la caja abierta.\r\n\r\n{ex.Message}",
                "Recuperación de caja",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return !requireContinuationForOperation;
        }
        finally
        {
            _showing = false;
        }
    }

    private static void ShowRecoveryError(string area, Exception exception)
    {
        TechnicalErrorLogger.Log(area, exception);
        MessageBox.Show(
            $"No se pudo completar la operación de recuperación.\r\n\r\n{exception.Message}",
            "Recuperación de caja",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
