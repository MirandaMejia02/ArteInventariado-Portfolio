using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>QR renderer with a quiet zone and integer-pixel output for 203 DPI labels.</summary>
public sealed class QrCodeRenderer : IQrCodeRenderer
{
    public Code128BarcodeRender Render(string payload, ProductLabelProfile profile)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload != payload.Trim() || payload.Any(char.IsControl))
        {
            throw new ArgumentException("El payload QR no es válido.", nameof(payload));
        }
        if (!ProductLabelProfiles.SupportsQr(profile))
        {
            throw new InvalidOperationException("El perfil 25 × 15 mm es exclusivo para Code 128; seleccione un tamaño QR compatible.");
        }

        var definition = ProductLabelProfiles.Get(profile);
        var side = Math.Max(128, Math.Min(
            (int)Math.Round(definition.WidthMillimeters / 25.4d * definition.Dpi),
            (int)Math.Round(definition.HeightMillimeters / 25.4d * definition.Dpi)) - (definition.ContentPaddingPixels * 2));
        var matrix = new MultiFormatWriter().encode(payload, BarcodeFormat.QR_CODE, side, side,
            new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.MARGIN] = 4,
                [EncodeHintType.CHARACTER_SET] = "UTF-8",
                [EncodeHintType.ERROR_CORRECTION] = ZXing.QrCode.Internal.ErrorCorrectionLevel.M
            });
        using var bitmap = new SKBitmap(matrix.Width, matrix.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (var y = 0; y < matrix.Height; y++)
        for (var x = 0; x < matrix.Width; x++) bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new Code128BarcodeRender(payload, bitmap.Width, bitmap.Height, data.ToArray());
    }
}
