namespace ArteInventariado.Application.Models;

public sealed record CategorySummary(
    int CategoriaId,
    string Nombre,
    string? Descripcion,
    bool Activa);

public sealed record CreateCategoryRequest(
    string Nombre,
    string? Descripcion = null);

public sealed record UpdateCategoryRequest(
    int CategoriaId,
    string Nombre,
    string? Descripcion = null);
