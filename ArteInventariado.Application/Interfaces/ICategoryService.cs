using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICategoryService
{
    Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<CategorySummary> GetDefaultCategoryAsync(CancellationToken cancellationToken = default);

    Task<CategorySummary> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<CategorySummary> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    Task SetActiveAsync(int categoriaId, bool activa, CancellationToken cancellationToken = default);

    Task DeleteAsync(int categoriaId, CancellationToken cancellationToken = default);
}
