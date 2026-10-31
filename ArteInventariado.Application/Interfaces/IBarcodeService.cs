using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IBarcodeService
{
    Task<IReadOnlyList<ProductSkuBarcodeSummary>> GetBySkuAsync(
        int productoSkuId,
        CancellationToken cancellationToken = default);

    Task<BarcodeLookupResult?> LookupExactAsync(
        string codigo,
        CancellationToken cancellationToken = default);

    /// <summary>Lista SKU activos para revisión administrativa de barcode/etiquetas.</summary>
    Task<IReadOnlyList<BarcodeSkuCandidate>> GetSkuCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera únicamente códigos internos para SKU elegibles. Cada SKU queda aislado
    /// para que un error parcial se informe sin deshacer generaciones válidas previas.
    /// </summary>
    Task<GenerateMissingBarcodesResult> GenerateMissingBarcodesAsync(
        GenerateMissingBarcodesRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductSkuBarcodeSummary> AssignAsync(
        AssignProductSkuBarcodeRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductSkuBarcodeSummary> GenerateInternalAsync(
        GenerateProductSkuBarcodeRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductSkuBarcodeSummary> GenerateQrAsync(
        GenerateProductSkuQrRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("La generación QR no está disponible en esta implementación.");

    Task SetPrincipalAsync(
        int productoSkuBarcodeId,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        int productoSkuBarcodeId,
        bool active,
        CancellationToken cancellationToken = default);
}
