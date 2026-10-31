namespace ArteInventariado.Application.Models;

public enum PeripheralAvailability
{
    NotConfigured = 1,
    Disabled = 2,
    Available = 3,
    Unavailable = 4
}

public sealed record PeripheralStatus(
    PeripheralAvailability Availability,
    string Message,
    string? LogicalName = null);

public sealed record PeripheralOperationResult(
    bool Succeeded,
    PeripheralStatus Status,
    string Message)
{
    public static PeripheralOperationResult Success(string message, string? logicalName = null) =>
        new(true, new PeripheralStatus(PeripheralAvailability.Available, message, logicalName), message);

    public static PeripheralOperationResult Skipped(PeripheralAvailability availability, string message) =>
        new(false, new PeripheralStatus(availability, message), message);

    public static PeripheralOperationResult Failed(string message, string? logicalName = null) =>
        new(false, new PeripheralStatus(PeripheralAvailability.Unavailable, message, logicalName), message);
}

public sealed record LocalPeripheralSettings(
    bool TicketsEnabled,
    string? ReceiptPrinterLogicalName,
    bool LabelsEnabled,
    string? LabelPrinterLogicalName,
    bool CashDrawerEnabled,
    string? CashDrawerLogicalName,
    string ConfigurationPath)
{
    public PeripheralStatus ReceiptStatus => BuildStatus(TicketsEnabled, ReceiptPrinterLogicalName, "Tickets");
    public PeripheralStatus LabelStatus => BuildStatus(LabelsEnabled, LabelPrinterLogicalName, "Etiquetas");
    public PeripheralStatus CashDrawerStatus => BuildStatus(CashDrawerEnabled, CashDrawerLogicalName, "Gaveta");

    private static PeripheralStatus BuildStatus(bool enabled, string? name, string label) => !enabled
        ? new(PeripheralAvailability.Disabled, $"{label}: deshabilitado")
        : string.IsNullOrWhiteSpace(name)
            ? new(PeripheralAvailability.NotConfigured, $"{label}: no configurado")
            : new(PeripheralAvailability.Unavailable, $"{label}: configurado, pendiente de hardware", name);
}

public sealed record UpdateLocalPeripheralSettingsRequest(
    bool TicketsEnabled,
    string? ReceiptPrinterLogicalName,
    bool LabelsEnabled,
    string? LabelPrinterLogicalName,
    bool CashDrawerEnabled,
    string? CashDrawerLogicalName);
