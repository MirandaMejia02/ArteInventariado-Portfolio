namespace ArteInventariado.Application.Models;

public sealed record SubcategorySummary(
    int SubcategoriaId,
    int CategoriaId,
    string Nombre,
    string? Descripcion,
    bool Activa);

public sealed record CreateSubcategoryRequest(
    int CategoriaId,
    string Nombre,
    string? Descripcion = null);

public sealed record UpdateSubcategoryRequest(
    int SubcategoriaId,
    string Nombre,
    string? Descripcion = null);
