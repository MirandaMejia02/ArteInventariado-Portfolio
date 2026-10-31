using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

/// <summary>
/// Administrative payment input. AmountCentavos follows the canonical money
/// representation used by the rest of the application.
/// </summary>
public sealed record RegisterEmployeePaymentRequest(
    int UserId,
    long AmountCentavos,
    EmployeePaymentType PaymentType,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    int? DaysConsidered = null,
    string? Note = null,
    MetodoPago? MetodoPago = null,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null,
    string? Referencia = null,
    Guid OperacionId = default);

public sealed record CancelEmployeePaymentRequest(
    int EmployeePaymentId,
    string Reason)
{
    public int Id => EmployeePaymentId;
}

/// <summary>
/// Local business-date filters. Infrastructure converts the dates to UTC
/// bounds using negocio.zona_horaria before filtering CreatedAtUtc.
/// </summary>
public sealed record EmployeePaymentFilter(
    int? UserId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDateExclusive = null,
    EmployeePaymentType? PaymentType = null,
    EmployeePaymentStatus? Status = null);

public sealed record EmployeePaymentHistoryRow(
    int PaymentId,
    int UserId,
    string EmployeeName,
    string EmployeeUserName,
    long AmountCentavos,
    EmployeePaymentType PaymentType,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int? DaysConsidered,
    string? Note,
    int RegisteredByUserId,
    string RegisteredByUserName,
    DateTime CreatedAtUtc,
    EmployeePaymentStatus Status,
    int? CancelledByUserId,
    string? CancelledByUserName,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    MetodoPago? MetodoPago = null,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null,
    string? Referencia = null,
    int? CajaId = null,
    Guid? OperacionId = null)
{
    public int Id => PaymentId;
    public long Amount => AmountCentavos;
}

public sealed record EmployeePaymentRecord(
    int PaymentId,
    int UserId,
    string EmployeeName,
    string EmployeeUserName,
    long AmountCentavos,
    EmployeePaymentType PaymentType,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int? DaysConsidered,
    string? Note,
    int RegisteredByUserId,
    string RegisteredByUserName,
    DateTime CreatedAtUtc,
    EmployeePaymentStatus Status,
    int? CancelledByUserId,
    string? CancelledByUserName,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    MetodoPago? MetodoPago = null,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null,
    string? Referencia = null,
    int? CajaId = null,
    Guid? OperacionId = null)
{
    public int Id => PaymentId;
    public long Amount => AmountCentavos;
}

public sealed record EmployeePaymentSummary(
    int UserId,
    long TotalPaidCentavos,
    int PaymentCount,
    DateTime? LastPaymentCreatedAtUtc,
    int CancelledPaymentCount,
    int TotalRecordCount)
{
    public long TotalAmountCentavos => TotalPaidCentavos;
}

public enum EmployeePaymentGrouping
{
    Day = 1,
    Week = 2,
    Biweekly = 3,
    Month = 4
}

/// <summary>
/// Read-only payment aggregate. LocalPeriodStart is expressed in the
/// configured business timezone; cancelled payments never contribute.
/// </summary>
public sealed record EmployeePaymentTrendPoint(
    DateOnly LocalPeriodStart,
    int PaymentCount,
    long TotalPaidCentavos);
