using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IEmployeeActivityQueryService
{
    Task<IReadOnlyList<EmployeeActivitySummary>> GetEmployeesAsync(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeLoginActivity>> GetLoginHistoryAsync(int userId, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeActivityDay>> GetActivityCalendarAsync(int userId, int year, int month, CancellationToken cancellationToken = default);
}
