namespace ArteInventariado.Application.Models;

public enum ProductLabelProfile
{
    Mm30x20 = 1,
    Mm40x30 = 2,
    Mm50x30 = 3,
    Mm25x15 = 4,
    Mm25x25 = 5,
    Mm25x30 = 6,
    Mm30x30 = 7,
    Mm40x40 = 8
}

public sealed record ProductLabelProfileDefinition(
    ProductLabelProfile Profile,
    string DisplayName,
    int WidthMillimeters,
    int HeightMillimeters,
    int Dpi,
    int BarcodeHeightPixels,
    int ContentPaddingPixels);

public static class ProductLabelProfiles
{
    public const int DefaultDpi = 203;
    public const int MaxCopiesPerSku = 1000;
    public const int MaxCopiesPerBatch = 1000;

    public static ProductLabelProfileDefinition Get(ProductLabelProfile profile) => profile switch
    {
        ProductLabelProfile.Mm30x20 => new(profile, "30 × 20 mm", 30, 20, DefaultDpi, 48, 8),
        ProductLabelProfile.Mm40x30 => new(profile, "40 × 30 mm", 40, 30, DefaultDpi, 78, 10),
        ProductLabelProfile.Mm50x30 => new(profile, "50 × 30 mm", 50, 30, DefaultDpi, 88, 12),
        ProductLabelProfile.Mm25x15 => new(profile, "25 × 15 mm (Code 128)", 25, 15, DefaultDpi, 38, 6),
        ProductLabelProfile.Mm25x25 => new(profile, "25 × 25 mm", 25, 25, DefaultDpi, 44, 7),
        ProductLabelProfile.Mm25x30 => new(profile, "25 × 30 mm", 25, 30, DefaultDpi, 52, 7),
        ProductLabelProfile.Mm30x30 => new(profile, "30 × 30 mm (QR recomendado)", 30, 30, DefaultDpi, 54, 8),
        ProductLabelProfile.Mm40x40 => new(profile, "40 × 40 mm", 40, 40, DefaultDpi, 76, 10),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "El perfil de etiqueta no es válido.")
    };

    public static IReadOnlyList<ProductLabelProfileDefinition> All { get; } =
    [
        Get(ProductLabelProfile.Mm30x20),
        Get(ProductLabelProfile.Mm40x30),
        Get(ProductLabelProfile.Mm50x30),
        Get(ProductLabelProfile.Mm25x15),
        Get(ProductLabelProfile.Mm25x25),
        Get(ProductLabelProfile.Mm25x30),
        Get(ProductLabelProfile.Mm30x30),
        Get(ProductLabelProfile.Mm40x40)
    ];

    public static bool SupportsQr(ProductLabelProfile profile) => profile != ProductLabelProfile.Mm25x15;
}

public sealed record LabelBatchItem(LabelData Label);

public sealed record LabelBatchPreview(
    ProductLabelProfile Profile,
    IReadOnlyList<LabelBatchItem> Items)
{
    public int TotalCopies => Items.Sum(item => item.Label.Quantity);
}

/// <summary>
/// Imagen PNG de un Code 128 para usar en UI/preview. El valor textual nunca se altera.
/// </summary>
public sealed record Code128BarcodeRender(
    string Value,
    int WidthPixels,
    int HeightPixels,
    byte[] PngBytes);
