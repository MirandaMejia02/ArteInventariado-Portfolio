using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IEmployeePaymentQueryService
{
    Task<EmployeePaymentSummary> GetSummaryAsync(
        int userId,
        EmployeePaymentFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeePaymentHistoryRow>> GetHistoryAsync(
        EmployeePaymentFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeePaymentHistoryRow>> GetEmployeeHistoryAsync(
        int userId,
        EmployeePaymentFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeePaymentTrendPoint>> GetPaymentTrendAsync(
        int userId,
        EmployeePaymentFilter? filter,
        EmployeePaymentGrouping grouping,
        CancellationToken cancellationToken = default);
}
