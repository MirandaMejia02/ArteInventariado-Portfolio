using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Codifica el valor ya persistido del barcode con ZXing.Net y lo rasteriza con
/// píxeles enteros sobre SkiaSharp. No aplica escalado posterior ni antialiasing.
/// </summary>
public sealed class Code128BarcodeRenderer : ICode128BarcodeRenderer
{
    public Code128BarcodeRender Render(string barcodeValue, ProductLabelProfile profile)
    {
        if (string.IsNullOrWhiteSpace(barcodeValue))
        {
            throw new ArgumentException("El valor del código de barras es obligatorio.", nameof(barcodeValue));
        }

        if (!string.Equals(barcodeValue, barcodeValue.Trim(), StringComparison.Ordinal) ||
            barcodeValue.Any(char.IsControl))
        {
            throw new ArgumentException("El valor del código de barras contiene caracteres no válidos.", nameof(barcodeValue));
        }

        var definition = ProductLabelProfiles.Get(profile);
        var labelWidthPixels = MillimetersToPixels(definition.WidthMillimeters, definition.Dpi);
        var barcodeWidthPixels = Math.Max(180, labelWidthPixels - (definition.ContentPaddingPixels * 2));
        var matrix = new MultiFormatWriter().encode(
            barcodeValue,
            BarcodeFormat.CODE_128,
            barcodeWidthPixels,
            definition.BarcodeHeightPixels,
            new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.MARGIN] = 10,
                [EncodeHintType.CHARACTER_SET] = "ISO-8859-1"
            });

        using var bitmap = new SKBitmap(matrix.Width, matrix.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new Code128BarcodeRender(
            barcodeValue,
            bitmap.Width,
            bitmap.Height,
            data.ToArray());
    }

    private static int MillimetersToPixels(int millimeters, int dpi) =>
        Math.Max(1, (int)Math.Round(millimeters / 25.4d * dpi, MidpointRounding.AwayFromZero));
}
