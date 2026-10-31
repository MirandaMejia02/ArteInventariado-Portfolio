using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IReceiptPrinter
{
    Task<PeripheralStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<PeripheralOperationResult> PrintAsync(ReceiptData receipt, CancellationToken cancellationToken = default);
}

public interface ILabelPrinter
{
    Task<PeripheralStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<PeripheralOperationResult> PrintAsync(LabelData label, CancellationToken cancellationToken = default);
}

public interface ICashDrawer
{
    Task<PeripheralStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<PeripheralOperationResult> OpenAsync(CancellationToken cancellationToken = default);
}

public interface IPeripheralOutputCoordinator
{
    Task<PeripheralOperationResult> TryPrintReceiptAsync(ReceiptData receipt, CancellationToken cancellationToken = default);
    Task<PeripheralOperationResult> TryPrintLabelsAsync(LabelData label, CancellationToken cancellationToken = default);
    Task<PeripheralOperationResult> TryOpenCashDrawerAsync(CancellationToken cancellationToken = default);
}
