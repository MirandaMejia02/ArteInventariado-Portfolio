using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IEmployeeSalesQueryService
{
    Task<EmployeeSalesReport> GetEmployeeSalesAsync(
        int userId,
        EmployeeSalesPeriod? period = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeSalesTrendPoint>> GetEmployeeSalesTrendAsync(
        int userId,
        EmployeeSalesPeriod? period,
        EmployeeSalesGrouping grouping,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeSalesRankingRow>> GetSalesRankingAsync(
        EmployeeSalesPeriod? period = null,
        CancellationToken cancellationToken = default);
}
