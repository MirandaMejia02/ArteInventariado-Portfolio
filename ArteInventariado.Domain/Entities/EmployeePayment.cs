using System.ComponentModel.DataAnnotations.Schema;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

/// <summary>
/// Immutable-in-history administrative payment record for a user. Monetary
/// values follow the application's canonical integer-cent convention.
/// </summary>
public class EmployeePayment
{
    public int EmployeePaymentId { get; set; }

    [NotMapped]
    public int Id
    {
        get => EmployeePaymentId;
        set => EmployeePaymentId = value;
    }

    public int UserId { get; set; }
    public Usuario User { get; set; } = null!;

    /// <summary>Null for historical payments whose cash effect was not recorded.</summary>
    public int? CajaId { get; set; }
    public Caja? Caja { get; set; }

    public long AmountCentavos { get; set; }

    [NotMapped]
    public long Amount
    {
        get => AmountCentavos;
        set => AmountCentavos = value;
    }

    public EmployeePaymentType PaymentType { get; set; }
    public MetodoPago? MetodoPago { get; set; }
    public string? Referencia { get; set; }
    public Guid? OperacionId { get; set; }
    public OrigenFondos? OrigenFondos { get; set; }
    public string? ResponsableOrigen { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public int? DaysConsidered { get; set; }
    public string? Note { get; set; }
    public int RegisteredByUserId { get; set; }
    public Usuario RegisteredBy { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public EmployeePaymentStatus Status { get; set; } = EmployeePaymentStatus.Active;
    public int? CancelledByUserId { get; set; }
    public Usuario? CancelledBy { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
}
