using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record LabelData(
    int ProductId,
    int ProductSkuId,
    string ProductName,
    string SkuCode,
    string? VariantName,
    string? Barcode,
    BarcodeSymbology Symbology,
    long? PriceCents,
    int Quantity);

public static class LabelDataFactory
{
    public static LabelData Create(
        ProductSummary product,
        ProductSkuSummary sku,
        IEnumerable<ProductSkuBarcodeSummary> barcodes,
        int quantity,
        bool includePrice = true)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(sku);
        if (quantity is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(quantity), "La cantidad de etiquetas debe estar entre 1 y 1000.");
        if (sku.ProductoId != product.ProductoId)
            throw new ArgumentException("El SKU no pertenece al producto indicado.", nameof(sku));

        var selectedBarcode = barcodes
            .Where(item => item.ProductoSkuId == sku.ProductoSkuId && item.Activo)
            .OrderByDescending(item => item.EsPrincipal)
            .ThenBy(item => item.Codigo, StringComparer.Ordinal)
            .FirstOrDefault();

        if (selectedBarcode is null)
        {
            return CreateWithoutBarcode(product, sku, quantity, includePrice);
        }

        return Create(product, sku, selectedBarcode, quantity, includePrice);
    }

    public static LabelData Create(
        ProductSummary product,
        ProductSkuSummary sku,
        ProductSkuBarcodeSummary barcode,
        int quantity,
        bool includePrice = true)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(barcode);
        if (quantity is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(quantity), "La cantidad de etiquetas debe estar entre 1 y 1000.");
        if (sku.ProductoId != product.ProductoId || barcode.ProductoSkuId != sku.ProductoSkuId || !barcode.Activo)
            throw new ArgumentException("El código seleccionado no pertenece a un SKU activo de este producto.", nameof(barcode));

        return new LabelData(
            product.ProductoId,
            sku.ProductoSkuId,
            product.Nombre,
            sku.CodigoSku,
            sku.NombreVariante,
            barcode.Codigo,
            barcode.Symbology,
            includePrice ? sku.PrecioVentaEfectivoCentavos : null,
            quantity);
    }

    private static LabelData CreateWithoutBarcode(
        ProductSummary product,
        ProductSkuSummary sku,
        int quantity,
        bool includePrice) => new(
            product.ProductoId,
            sku.ProductoSkuId,
            product.Nombre,
            sku.CodigoSku,
            sku.NombreVariante,
            null,
            BarcodeSymbology.Code128,
            includePrice ? sku.PrecioVentaEfectivoCentavos : null,
            quantity);

    public static LabelData Create(
        BarcodeSkuCandidate candidate,
        int quantity,
        bool includePrice = true)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.TieneBarcodeActivo)
        {
            throw new InvalidOperationException("Este SKU no tiene código de barras activo.");
        }

        if (quantity is < 1 or > ProductLabelProfiles.MaxCopiesPerSku)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity),
                $"La cantidad de etiquetas debe estar entre 1 y {ProductLabelProfiles.MaxCopiesPerSku}.");
        }

        return new LabelData(
            candidate.ProductoId,
            candidate.ProductoSkuId,
            candidate.ProductoNombre,
            candidate.CodigoSku,
            candidate.NombreVariante,
            candidate.BarcodeActivo,
            BarcodeSymbology.Code128,
            includePrice ? candidate.PrecioVentaEfectivoCentavos : null,
            quantity);
    }

    public static LabelBatchPreview CreateBatch(
        IEnumerable<(BarcodeSkuCandidate Candidate, int Quantity)> selected,
        ProductLabelProfile profile,
        bool includePrice = true)
    {
        ArgumentNullException.ThrowIfNull(selected);
        _ = ProductLabelProfiles.Get(profile);
        var items = selected
            .Select(item => new LabelBatchItem(Create(item.Candidate, item.Quantity, includePrice)))
            .ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Seleccione al menos un SKU para preparar etiquetas.");
        }

        var batch = new LabelBatchPreview(profile, items);
        if (batch.TotalCopies > ProductLabelProfiles.MaxCopiesPerBatch)
        {
            throw new InvalidOperationException(
                $"El lote supera el máximo de {ProductLabelProfiles.MaxCopiesPerBatch} etiquetas. Ajuste las cantidades.");
        }

        return batch;
    }
}

public sealed record LabelPreviewData(IReadOnlyList<string> Lines, int Copies);

public static class LabelTextRenderer
{
    public static LabelPreviewData Render(LabelData label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var lines = new List<string> { label.ProductName };
        if (!string.IsNullOrWhiteSpace(label.VariantName)) lines.Add(label.VariantName.Trim());
        lines.Add($"SKU: {label.SkuCode}");
        lines.Add(string.IsNullOrWhiteSpace(label.Barcode)
            ? "Sin codigo de barras"
            : $"{(label.Symbology == BarcodeSymbology.Qr ? "QR" : "Código")}: {label.Barcode}");
        if (label.PriceCents.HasValue) lines.Add($"Precio: {ReceiptTextRenderer.Money(label.PriceCents.Value)}");
        return new LabelPreviewData(lines, label.Quantity);
    }
}
