using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IProductImportService
{
    Task<string> CreateProductImportTemplateAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default);

    Task<ProductImportPreviewResult> PreviewProductsAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ProductImportResult> ImportProductsAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
