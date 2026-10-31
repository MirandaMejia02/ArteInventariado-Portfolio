using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Services;
using SkiaSharp;

namespace ArteInventariado.Tests;

public sealed class Code128LabelRendererTests
{
    [Theory]
    [InlineData(ProductLabelProfile.Mm30x20)]
    [InlineData(ProductLabelProfile.Mm40x30)]
    [InlineData(ProductLabelProfile.Mm50x30)]
    public void Render_ProducesSharpBlackAndWhiteCode128ForEveryProfile(ProductLabelProfile profile)
    {
        const string barcode = "AI0123456789";
        var renderer = new Code128BarcodeRenderer();

        var result = renderer.Render(barcode, profile);

        Assert.Equal(barcode, result.Value);
        Assert.True(result.WidthPixels >= 180);
        Assert.True(result.HeightPixels > 20);
        Assert.NotEmpty(result.PngBytes);
        using var bitmap = SKBitmap.Decode(result.PngBytes);
        Assert.NotNull(bitmap);
        Assert.Equal(result.WidthPixels, bitmap.Width);
        Assert.Equal(result.HeightPixels, bitmap.Height);
        var colors = Enumerable.Range(0, bitmap.Width)
            .Select(x => bitmap.GetPixel(x, bitmap.Height / 2))
            .Select(color => color.Red)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
        Assert.Contains((byte)0, colors);
        Assert.Contains((byte)255, colors);
        Assert.All(colors, value => Assert.Contains(value, new byte[] { 0, 255 }));
    }

    [Fact]
    public void BatchFactory_PreservesEveryBarcodeValueAndRejectsAbsurdTotals()
    {
        var first = Candidate(1, "SKU-UNO", "AI111111111111111111111111");
        var second = Candidate(2, "SKU-DOS", "AI222222222222222222222222");

        var batch = LabelDataFactory.CreateBatch(
            [(first, 2), (second, 3)],
            ProductLabelProfile.Mm50x30);

        Assert.Equal(5, batch.TotalCopies);
        Assert.Equal(first.BarcodeActivo, batch.Items[0].Label.Barcode);
        Assert.Equal(second.BarcodeActivo, batch.Items[1].Label.Barcode);
        Assert.Throws<InvalidOperationException>(() => LabelDataFactory.CreateBatch(
            [(first, 600), (second, 600)], ProductLabelProfile.Mm40x30));
    }

    [Fact]
    public void LabelFactory_DoesNotInventBarcodeForSkuWithoutOne()
    {
        var candidate = Candidate(1, "SKU-SIN", null);

        var error = Assert.Throws<InvalidOperationException>(() =>
            LabelDataFactory.Create(candidate, 1));

        Assert.Contains("no tiene código", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ProductLabelProfile.Mm25x25)]
    [InlineData(ProductLabelProfile.Mm25x30)]
    [InlineData(ProductLabelProfile.Mm30x30)]
    [InlineData(ProductLabelProfile.Mm40x30)]
    [InlineData(ProductLabelProfile.Mm40x40)]
    public void QrRenderer_ProducesSquarePngForEverySupportedProfile(ProductLabelProfile profile)
    {
        var renderer = new QrCodeRenderer();

        var result = renderer.Render("AI:SKU:SKU-001", profile);

        Assert.Equal("AI:SKU:SKU-001", result.Value);
        Assert.Equal(result.WidthPixels, result.HeightPixels);
        Assert.NotEmpty(result.PngBytes);
        using var bitmap = SKBitmap.Decode(result.PngBytes);
        Assert.NotNull(bitmap);
        Assert.Equal(bitmap.Width, bitmap.Height);
    }

    [Fact]
    public void QrRenderer_Rejects25x15AndLabelFactoryKeepsTheSelectedQrValue()
    {
        var renderer = new QrCodeRenderer();
        Assert.False(ProductLabelProfiles.SupportsQr(ProductLabelProfile.Mm25x15));
        Assert.Throws<InvalidOperationException>(() => renderer.Render("AI:SKU:SKU-001", ProductLabelProfile.Mm25x15));

        var product = new ProductSummary(
            1, "PRD-001", "Producto", null, 1, "Categoría", null,
            0, 1500, 0, 0, EstadoProducto.ACTIVO, DateTime.UtcNow, null);
        var sku = new ProductSkuSummary(
            10, 1, "SKU-001", "Azul", true, true, null, null, null,
            1500, 0, 0, 0, 0, 0, 0, 0);
        var qr = new ProductSkuBarcodeSummary(100, 10, "AI:SKU:SKU-001", true, false, BarcodeSymbology.Qr);

        var label = LabelDataFactory.Create(product, sku, qr, 1);

        Assert.Equal(BarcodeSymbology.Qr, label.Symbology);
        Assert.Equal(qr.Codigo, label.Barcode);
    }

    private static BarcodeSkuCandidate Candidate(int id, string sku, string? barcode) => new(
        1,
        id,
        "PRD-001",
        "Producto artesanal con nombre deliberadamente largo para validar ellipsis visual",
        sku,
        "Variante Azul",
        1234,
        barcode,
        false);
}
