using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Services;

internal sealed class UnconfiguredReceiptPrinter : IReceiptPrinter
{
    private static readonly PeripheralStatus Status = new(
        PeripheralAvailability.NotConfigured,
        "Impresora de tickets no configurada.");

    public Task<PeripheralStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Status);

    public Task<PeripheralOperationResult> PrintAsync(ReceiptData receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return Task.FromResult(PeripheralOperationResult.Skipped(Status.Availability, Status.Message));
    }
}

internal sealed class UnconfiguredLabelPrinter : ILabelPrinter
{
    private static readonly PeripheralStatus Status = new(
        PeripheralAvailability.NotConfigured,
        "Impresora de etiquetas no configurada.");

    public Task<PeripheralStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Status);

    public Task<PeripheralOperationResult> PrintAsync(LabelData label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Task.FromResult(PeripheralOperationResult.Skipped(Status.Availability, Status.Message));
    }
}

internal sealed class UnconfiguredCashDrawer : ICashDrawer
{
    private static readonly PeripheralStatus Status = new(
        PeripheralAvailability.NotConfigured,
        "Gaveta no configurada.");

    public Task<PeripheralStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Status);

    public Task<PeripheralOperationResult> OpenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(PeripheralOperationResult.Skipped(Status.Availability, Status.Message));
}

internal sealed class PeripheralOutputCoordinator : IPeripheralOutputCoordinator
{
    private readonly IStationSettingsService _settings;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly ILabelPrinter _labelPrinter;
    private readonly ICashDrawer _cashDrawer;

    public PeripheralOutputCoordinator(
        IStationSettingsService settings,
        IReceiptPrinter receiptPrinter,
        ILabelPrinter labelPrinter,
        ICashDrawer cashDrawer)
    {
        _settings = settings;
        _receiptPrinter = receiptPrinter;
        _labelPrinter = labelPrinter;
        _cashDrawer = cashDrawer;
    }

    public async Task<PeripheralOperationResult> TryPrintReceiptAsync(
        ReceiptData receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var configured = await _settings.GetPeripheralSettingsAsync(cancellationToken);
        if (!configured.TicketsEnabled)
            return PeripheralOperationResult.Skipped(PeripheralAvailability.Disabled, "Tickets deshabilitados en esta estacion.");
        if (string.IsNullOrWhiteSpace(configured.ReceiptPrinterLogicalName))
            return PeripheralOperationResult.Skipped(PeripheralAvailability.NotConfigured, "Impresora de tickets no configurada.");
        return await SafeAsync(
            () => _receiptPrinter.PrintAsync(receipt, cancellationToken),
            "La impresion del ticket fallo; la venta permanece confirmada.",
            configured.ReceiptPrinterLogicalName);
    }

    public async Task<PeripheralOperationResult> TryPrintLabelsAsync(
        LabelData label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        var configured = await _settings.GetPeripheralSettingsAsync(cancellationToken);
        if (!configured.LabelsEnabled)
            return PeripheralOperationResult.Skipped(PeripheralAvailability.Disabled, "Etiquetas deshabilitadas en esta estacion.");
        if (string.IsNullOrWhiteSpace(configured.LabelPrinterLogicalName))
            return PeripheralOperationResult.Skipped(PeripheralAvailability.NotConfigured, "Impresora de etiquetas no configurada.");
        return await SafeAsync(
            () => _labelPrinter.PrintAsync(label, cancellationToken),
            "La impresion de etiquetas no pudo completarse.",
            configured.LabelPrinterLogicalName);
    }

    public async Task<PeripheralOperationResult> TryOpenCashDrawerAsync(CancellationToken cancellationToken = default)
    {
        var configured = await _settings.GetPeripheralSettingsAsync(cancellationToken);
        if (!configured.CashDrawerEnabled)
            return PeripheralOperationResult.Skipped(PeripheralAvailability.Disabled, "Gaveta deshabilitada en esta estacion.");
        if (string.IsNullOrWhiteSpace(configured.CashDrawerLogicalName))
            return PeripheralOperationResult.Skipped(PeripheralAvailability.NotConfigured, "Gaveta no configurada.");
        return await SafeAsync(
            () => _cashDrawer.OpenAsync(cancellationToken),
            "La gaveta no pudo abrirse; la venta permanece confirmada.",
            configured.CashDrawerLogicalName);
    }

    private static async Task<PeripheralOperationResult> SafeAsync(
        Func<Task<PeripheralOperationResult>> operation,
        string failureMessage,
        string? logicalName)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return PeripheralOperationResult.Failed(failureMessage, logicalName);
        }
    }
}
