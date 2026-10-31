using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Read-only inventory lookup for the employee consultation screen.
/// The returned projection intentionally contains no purchase or profitability data.
/// </summary>
public interface IEmployeeInventoryLookupService
{
    Task<IReadOnlyList<EmployeeInventoryLookupItem>> SearchAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default);
}
