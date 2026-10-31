using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record ProductSkuBarcodeSummary(
    int ProductoSkuBarcodeId,
    int ProductoSkuId,
    string Codigo,
    bool Activo,
    bool EsPrincipal,
    BarcodeSymbology Symbology = BarcodeSymbology.Code128);

public sealed record BarcodeLookupResult(
    int ProductoSkuBarcodeId,
    string Barcode,
    int ProductoId,
    string ProductoCodigo,
    string ProductoNombre,
    ModoProducto ModoProducto,
    int ProductoSkuId,
    string CodigoSku,
    string? NombreVariante,
    long PrecioVentaEfectivoCentavos,
    long CostoEfectivoCentavos,
    int ExistenciaExhibicion,
    int ReservadoExhibicion,
    int DisponibleExhibicion,
    int DisponibleBodega,
    int DisponibleSinAsignar);

public sealed record AssignProductSkuBarcodeRequest(
    int ProductoSkuId,
    string Codigo,
    bool EsPrincipal = false,
    BarcodeSymbology Symbology = BarcodeSymbology.Code128);

public sealed record GenerateProductSkuBarcodeRequest(
    int ProductoSkuId,
    bool EsPrincipal = false);

public sealed record GenerateProductSkuQrRequest(
    int ProductoSkuId,
    bool EsPrincipal = false);

/// <summary>
/// SKU disponible para operaciones administrativas de barcode y etiquetas.
/// La consulta no expone ni modifica inventario: solo identifica el SKU y su barcode activo.
/// </summary>
public sealed record BarcodeSkuCandidate(
    int ProductoId,
    int ProductoSkuId,
    string ProductoCodigo,
    string ProductoNombre,
    string CodigoSku,
    string? NombreVariante,
    long PrecioVentaEfectivoCentavos,
    string? BarcodeActivo,
    bool TieneBarcodeHistoricoInactivo)
{
    public bool TieneBarcodeActivo => !string.IsNullOrWhiteSpace(BarcodeActivo);
    public bool EsElegibleParaGeneracionInterna => !TieneBarcodeActivo && !TieneBarcodeHistoricoInactivo;
}

/// <summary>
/// Cuando no se indican SKU se procesan todos los elegibles. Una selección explícita
/// solo puede reducir ese conjunto, nunca forzar el reemplazo de códigos existentes.
/// </summary>
public sealed record GenerateMissingBarcodesRequest(
    IReadOnlyCollection<int>? ProductoSkuIds = null);

public enum MissingBarcodeGenerationStatus
{
    Generated = 1,
    Omitted = 2,
    Error = 3
}

public sealed record MissingBarcodeGenerationItem(
    BarcodeSkuCandidate Candidate,
    MissingBarcodeGenerationStatus Status,
    string? Barcode,
    string? Detail);

public sealed record GenerateMissingBarcodesResult(
    IReadOnlyList<MissingBarcodeGenerationItem> Items)
{
    public int CandidateCount => Items.Count;
    public int GeneratedCount => Items.Count(item => item.Status == MissingBarcodeGenerationStatus.Generated);
    public int OmittedCount => Items.Count(item => item.Status == MissingBarcodeGenerationStatus.Omitted);
    public int ErrorCount => Items.Count(item => item.Status == MissingBarcodeGenerationStatus.Error);
}
