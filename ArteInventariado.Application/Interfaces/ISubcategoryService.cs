using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISubcategoryService
{
    Task<IReadOnlyList<SubcategorySummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubcategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubcategorySummary>> GetByCategoryAsync(
        int categoriaId,
        bool soloActivas = false,
        CancellationToken cancellationToken = default);

    Task<SubcategorySummary?> GetByIdAsync(
        int subcategoriaId,
        CancellationToken cancellationToken = default);

    Task<SubcategorySummary> CreateAsync(
        CreateSubcategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<SubcategorySummary> UpdateAsync(
        UpdateSubcategoryRequest request,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        int subcategoriaId,
        bool activa,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int subcategoriaId, CancellationToken cancellationToken = default);
}
