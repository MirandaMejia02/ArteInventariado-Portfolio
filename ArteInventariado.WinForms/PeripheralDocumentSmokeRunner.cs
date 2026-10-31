using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Services;

namespace ArteInventariado.WinForms;

internal static class PeripheralDocumentSmokeRunner
{
    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.PeripheralDocumentSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var log = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };
        try
        {
            var current = BuildCurrentReceipt();
            log.Add("ReceiptData actual=OK");
            var historical = BuildHistoricalReceipt();
            log.Add("ReceiptData historico snapshots=OK");
            var originalHistoricalText = ReceiptTextRenderer.RenderText(historical);
            if (originalHistoricalText != ReceiptTextRenderer.RenderText(historical))
                throw new InvalidOperationException("La reimpresion no produjo el mismo documento historico.");
            log.Add("Reimpresion sin efectos comerciales=OK");

            var pdfPath = Path.Combine(outputDirectory, "ticket-preview.pdf");
            SaleReceiptPdfExporter.Save(SaleReceiptViewModel.FromReceiptData(current), pdfPath);
            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 100)
                throw new InvalidOperationException("El PDF de ticket no fue generado correctamente.");
            log.Add($"PDF ticket=OK; path={pdfPath}");

            var label = BuildLabel();
            if (label.Barcode is not null)
                throw new InvalidOperationException("La etiqueta smoke debia validar el fallback SKU sin barcode.");
            log.Add("LabelData SKU sin barcode=OK");
            var barcodeLabel = BuildBarcodeLabel();
            var renderer = new Code128BarcodeRenderer();
            if (renderer.Render(barcodeLabel.Barcode!, ProductLabelProfile.Mm40x30).PngBytes.Length < 100)
                throw new InvalidOperationException("No se pudo renderizar el Code 128 de la etiqueta.");
            log.Add("Label Code128 gráfico=OK");
            var batch = new LabelBatchPreview(
                ProductLabelProfile.Mm40x30,
                [
                    new LabelBatchItem(barcodeLabel),
                    new LabelBatchItem(barcodeLabel with { Quantity = 2 })
                ]);
            if (batch.TotalCopies != 4)
                throw new InvalidOperationException("El lote de etiquetas no conservó su cantidad de copias.");
            log.Add("Label preview lote=OK");

            foreach (var scale in new[] { 1f, 1.25f, 1.5f })
            {
                Smoke(new SaleReceiptForm(
                        SaleReceiptViewModel.FromReceiptData(historical),
                        isCompletedSale: false,
                        primaryButtonText: "Cerrar"),
                    scale,
                    Path.Combine(outputDirectory, $"ticket-dpi-{ScaleName(scale)}.png"),
                    log);
                Smoke(new LabelPreviewForm(label, renderer),
                    scale,
                    Path.Combine(outputDirectory, $"label-sin-barcode-dpi-{ScaleName(scale)}.png"),
                    log);
                foreach (var profile in new[] { ProductLabelProfile.Mm30x20, ProductLabelProfile.Mm40x30, ProductLabelProfile.Mm50x30 })
                {
                    Smoke(new LabelPreviewForm(barcodeLabel, renderer, profile),
                        scale,
                        Path.Combine(outputDirectory, $"label-code128-{profile}-{ScaleName(scale)}.png"),
                        log);
                }
                Smoke(new LabelPreviewForm(batch, renderer),
                    scale,
                    Path.Combine(outputDirectory, $"label-lote-{ScaleName(scale)}.png"),
                    log);
            }

            log.Add("Perifericos deshabilitados/no configurados: cubiertos por servicios y tests");
            log.Add("POS tradicional/rapido PRINCIPAL/CONSULTA: smoke dedicado ARTE_POS_BARCODE_SMOKE");
            log.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("RESULT=FAILED");
            log.Add(ex.ToString());
            return 1;
        }
        finally
        {
            log.Add($"END={DateTimeOffset.UtcNow:O}");
            File.WriteAllLines(Path.Combine(outputDirectory, "peripheral-document-smoke.log"), log);
        }
    }

    private static void Smoke(Form form, float scale, string imagePath, ICollection<string> log)
    {
        using (form)
        {
            if (scale != 1f) form.Scale(new SizeF(scale, scale));
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            form.PerformLayout();
            ValidateButtons(form, scale);
            using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(imagePath);
            form.Hide();
            log.Add($"{form.GetType().Name}@{scale:P0}=OK; render={imagePath}");
        }
    }

    private static void ValidateButtons(Control root, float scale)
    {
        foreach (var button in Descendants(root).OfType<Button>().Where(item => item.Visible))
        {
            var measured = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (button.ClientSize.Width < measured.Width + 10 || button.ClientSize.Height < measured.Height + 6)
                throw new InvalidOperationException(
                    $"{root.GetType().Name}: texto cortado en '{button.Text}' a {scale:P0}; contenido={measured}, boton={button.ClientSize}.");
            if (button.Parent is not null && !button.Parent.ClientRectangle.Contains(button.Bounds))
                throw new InvalidOperationException(
                    $"{root.GetType().Name}: boton '{button.Text}' fuera de su contenedor a {scale:P0}; bounds={button.Bounds}, parent={button.Parent.ClientRectangle}.");
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static ReceiptData BuildCurrentReceipt()
    {
        var sale = new SaleResult(
            1, "V-SMOKE", 1, 1, null, "Consumidor final", DateTime.UtcNow,
            4200, 200, 4000, 5000, 1000,
            [new SaleLineResult(1, 1, "ART-1", "Alcancia artesanal", 2, 2100, 4200, 200, 4000,
                10, "SKU-ROSA", "Rosa")],
            [new SalePaymentResult(1, MetodoPago.EFECTIVO, 5000, null)]);
        return ReceiptDataFactory.FromSaleResult(sale,
            new ReceiptBusinessData("ArteInventariado Demo", "San Salvador", "0000-0000"),
            "Administrador del sistema");
    }

    private static ReceiptData BuildHistoricalReceipt()
    {
        var detail = new SalesHistoryDetail(
            1, "V-SMOKE", DateTime.UtcNow, "Administrador del sistema", "Consumidor final",
            4200, 200, 4000, 5000, 1000,
            [new SalesHistoryLine("Alcancia artesanal · Rosa", "SKU-ROSA", 2, 2100, 200, 4200, 4000,
                "ART-1", "SKU-ROSA", "Rosa")],
            [new SalesHistoryPayment(MetodoPago.EFECTIVO, 5000, null)]);
        return ReceiptDataFactory.FromSalesHistory(detail,
            new ReceiptBusinessData("ArteInventariado Demo", "San Salvador", "0000-0000"));
    }

    private static LabelData BuildLabel()
    {
        var product = new ProductSummary(1, "ART-1", "Alcancia artesanal", null, 1, "Artesanias", null,
            1000, 2100, 5, 2, EstadoProducto.ACTIVO, DateTime.UtcNow, null,
            ModoProducto.POR_VARIANTES, 5, 5, 0, 0, 0);
        var sku = new ProductSkuSummary(10, 1, "SKU-ROSA", "Rosa", true, true, null, 2100, 1000,
            2100, 1000, 5, 0, 0, 0, 5, 5);
        return LabelDataFactory.Create(product, sku, [], 3);
    }

    private static LabelData BuildBarcodeLabel()
    {
        var product = new ProductSummary(2, "ART-C128", "Alcancía artesanal", null, 1, "Artesanías", null,
            1000, 2100, 5, 2, EstadoProducto.ACTIVO, DateTime.UtcNow, null,
            ModoProducto.POR_VARIANTES, 5, 5, 0, 0, 0);
        var sku = new ProductSkuSummary(11, 2, "SKU-C128", "Edición azul", true, true, null, 2100, 1000,
            2100, 1000, 5, 0, 0, 0, 5, 5);
        return LabelDataFactory.Create(product, sku,
            [new ProductSkuBarcodeSummary(1, sku.ProductoSkuId, "AI0123456789", true, true)], 2);
    }

    private static string ScaleName(float scale) => $"{Math.Round(scale * 100):0}";
}
