namespace ArteInventariado.Application.Models;

/// <summary>
/// UI-facing aggregate that keeps activity, commercial sales and
/// administrative payments as separate dimensions. It intentionally exposes
/// no derived score, profit or productivity calculation.
/// </summary>
public sealed record EmployeeStatisticsSummary(
    int UserId,
    DateTime? LastLoginUtc,
    int ActivityDays,
    int SaleCount,
    long TotalSoldCentavos,
    long AverageTicketCentavos,
    int PaymentCount,
    long TotalPaidCentavos,
    DateTime? LastPaymentUtc)
{
    public static EmployeeStatisticsSummary Combine(
        EmployeeActivitySummary activity,
        EmployeeSalesSummary sales,
        EmployeePaymentSummary payments)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(payments);

        if (activity.UserId != sales.UserId || activity.UserId != payments.UserId)
        {
            throw new ArgumentException("Los resúmenes pertenecen a usuarios diferentes.");
        }

        return new EmployeeStatisticsSummary(
            activity.UserId,
            activity.LastLoginUtc,
            activity.ActivityDays,
            sales.SaleCount,
            sales.TotalSoldCentavos,
            sales.AverageTicketCentavos,
            payments.PaymentCount,
            payments.TotalPaidCentavos,
            payments.LastPaymentCreatedAtUtc);
    }
}
