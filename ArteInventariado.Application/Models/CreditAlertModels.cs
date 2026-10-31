using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public enum CreditAlertType
{
    DueToday,
    UpcomingDue,
    Overdue,
    Moroso,
    NoRecentPayment,
    NearLimit,
    LimitReached
}

public enum CreditAlertSeverity
{
    Informativa,
    Advertencia,
    Alta,
    Critica
}

public sealed record CreditAlertSettings(
    int UpcomingDueDays,
    int NoPaymentDays,
    decimal NearLimitPercent,
    bool EnableDueToday,
    bool EnableUpcomingDue,
    bool EnableOverdue,
    bool EnableMoroso,
    bool EnableNoRecentPayment,
    bool EnableNearLimit,
    bool EnableLimitReached);

public sealed record UpdateCreditAlertSettingsRequest(
    int UpcomingDueDays,
    int NoPaymentDays,
    decimal NearLimitPercent,
    bool EnableDueToday,
    bool EnableUpcomingDue,
    bool EnableOverdue,
    bool EnableMoroso,
    bool EnableNoRecentPayment,
    bool EnableNearLimit,
    bool EnableLimitReached);

public sealed record CreditAlertFilter(
    CreditAlertType? Type = null,
    int? ClienteId = null,
    string? Search = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool OnlyOpenAccounts = true);

public sealed record CreditAlertSummary(
    int TotalActive,
    int DueToday,
    int UpcomingDue,
    int Overdue,
    int Moroso,
    int NoRecentPayment,
    int NearLimit,
    int LimitReached);

public sealed record CreditAlertItem(
    CreditAlertType Type,
    CreditAlertSeverity Severity,
    int ClienteId,
    string CodigoCliente,
    string Cliente,
    int? CuentaPorCobrarId,
    string? CodigoCuenta,
    int? VentaId,
    string? NumeroVenta,
    long SaldoCentavos,
    DateTime? FechaVencimiento,
    int? Dias,
    DateTime? FechaUltimoAbono,
    long? LimiteClienteCentavos,
    long SaldoTotalClienteCentavos,
    string Message,
    string RecommendedAction)
{
    public string TypeText => Type switch
    {
        CreditAlertType.DueToday => "Vence hoy",
        CreditAlertType.UpcomingDue => "Proxima a vencer",
        CreditAlertType.Overdue => "Vencida",
        CreditAlertType.Moroso => "Morosa",
        CreditAlertType.NoRecentPayment => "Sin abono reciente",
        CreditAlertType.NearLimit => "Cerca del limite",
        CreditAlertType.LimitReached => "Limite alcanzado",
        _ => "Alerta"
    };

    public string SeverityText => Severity switch
    {
        CreditAlertSeverity.Informativa => "Informativa",
        CreditAlertSeverity.Advertencia => "Advertencia",
        CreditAlertSeverity.Alta => "Alta",
        CreditAlertSeverity.Critica => "Critica",
        _ => "Alerta"
    };
}

public sealed record CreditCustomerAlertIndicator(
    int ClienteId,
    int TotalAlerts,
    string Text,
    CreditAlertSeverity? HighestSeverity);
