namespace ArteInventariado.Application.Models;

public sealed record ProductImportPreviewResult(
    string FilePath,
    int FilasLeidas,
    int FilasValidas,
    int FilasConError,
    int Duplicados,
    IReadOnlyList<string> GlobalErrors,
    IReadOnlyList<ProductImportPreviewRow> Rows)
{
    public bool CanImport => FilasValidas > 0 && GlobalErrors.Count == 0;
}

public sealed record ProductImportPreviewRow(
    int RowNumber,
    string? Codigo,
    string Nombre,
    string Categoria,
    string? Descripcion,
    decimal PrecioCompra,
    decimal PrecioVenta,
    int StockActual,
    int StockMinimo,
    string Estado,
    string? Observaciones,
    bool CanImport,
    bool IsDuplicate,
    string? Error);

public sealed record ProductImportResult(
    int ImportacionExcelId,
    string FilePath,
    string BackupPath,
    int FilasLeidas,
    int ProductosImportados,
    int FilasConError,
    int DuplicadosIgnorados);
