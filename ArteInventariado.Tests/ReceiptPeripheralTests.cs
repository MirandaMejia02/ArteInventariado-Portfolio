using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Services;

namespace ArteInventariado.Tests;

public sealed class ReceiptPeripheralTests
{
    [Fact]
    public void CurrentSale_BuildsCanonicalReceiptWithSkuVariantDiscountCashAndChange()
    {
        var sale = BuildSaleResult();

        var receipt = ReceiptDataFactory.FromSaleResult(
            sale,
            new ReceiptBusinessData("ArteInventariado Demo", "Centro", "0000-0000"),
            "Cajera Uno");

        var line = Assert.Single(receipt.Lines);
        Assert.Equal("SKU-ROSA", line.HistoricalSku);
        Assert.Equal("Rosa", line.HistoricalVariant);
        Assert.Equal(100, line.DiscountCents);
        Assert.Equal(5000, receipt.PaidCents);
        Assert.Equal(800, receipt.ChangeCents);
        Assert.Contains("SKU-ROSA", ReceiptTextRenderer.RenderText(receipt));
    }

    [Fact]
    public void HistoricalReceipt_UsesRecordedValuesAndDoesNotRequireCurrentProduct()
    {
        var detail = new SalesHistoryDetail(
            9, "V-000009", DateTime.UtcNow, "Cajero historico", "Cliente historico",
            2200, 200, 2000, 2000, 0,
            [new SalesHistoryLine("Nombre historico · Azul", "SKU-HIST", 2, 1100, 200, 2200, 2000,
                "PROD-HIST", "SKU-HIST", "Azul")],
            [new SalesHistoryPayment(MetodoPago.TRANSFERENCIA, 2000, "REF-HIST")]);

        var receipt = ReceiptDataFactory.FromSalesHistory(detail, new ReceiptBusinessData("Negocio"));

        var line = Assert.Single(receipt.Lines);
        Assert.Equal("Nombre historico", line.ProductName);
        Assert.Equal("PROD-HIST", line.HistoricalCode);
        Assert.Equal("SKU-HIST", line.HistoricalSku);
        Assert.Equal("Azul", line.HistoricalVariant);
        Assert.Equal(1100, line.UnitPriceCents);
    }

    [Fact]
    public void OldSaleWithoutSku_UsesLegitimateHistoricalProductCodeFallback()
    {
        var detail = new SalesHistoryDetail(
            3, "V-OLD", DateTime.UtcNow, "admin", "Consumidor final",
            1000, 0, 1000, 1000, 0,
            [new SalesHistoryLine("Producto viejo", "ART-001", 1, 1000, 0, 1000, 1000,
                "ART-001", null, null)],
            [new SalesHistoryPayment(MetodoPago.EFECTIVO, 1000, null)]);

        var receipt = ReceiptDataFactory.FromSalesHistory(detail, new ReceiptBusinessData("Negocio"));

        var line = Assert.Single(receipt.Lines);
        Assert.Null(line.HistoricalSku);
        Assert.Equal("ART-001", line.HistoricalCode);
        Assert.Contains("ART-001", ReceiptTextRenderer.RenderText(receipt));
    }

    [Fact]
    public void ReceiptRenderer_StaysWithinConfiguredEightyMillimeterTextWidth()
    {
        var receipt = ReceiptDataFactory.FromSaleResult(
            BuildSaleResult(),
            new ReceiptBusinessData("Nombre de negocio artesanal deliberadamente largo"),
            "Cajera Uno");

        var lines = ReceiptTextRenderer.Render(receipt, new ReceiptRenderOptions(42));

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(line.Length <= 44, $"Linea demasiado larga: {line}"));
        var renderedText = string.Join(" ", lines);
        Assert.Contains("Cambios y devoluciones sujetos a revisión", renderedText);
        Assert.Contains("Conserve este recibo.", renderedText);
    }

    [Fact]
    public void Label_UsesPrincipalActiveBarcodeAndRequestedCopies()
    {
        var (product, sku) = BuildProduct();
        var label = LabelDataFactory.Create(product, sku,
        [
            new ProductSkuBarcodeSummary(1, sku.ProductoSkuId, "SEC", true, false),
            new ProductSkuBarcodeSummary(2, sku.ProductoSkuId, "MAIN", true, true)
        ], 7);

        var preview = LabelTextRenderer.Render(label);

        Assert.Equal("MAIN", label.Barcode);
        Assert.Equal("Rosa", label.VariantName);
        Assert.Equal(7, preview.Copies);
        Assert.Contains("Precio: $12.00", preview.Lines);
    }

    [Fact]
    public void Label_WithoutBarcode_RemainsValidAndDoesNotInventCode()
    {
        var (product, sku) = BuildProduct();
        var label = LabelDataFactory.Create(product, sku, [], 2);

        var preview = LabelTextRenderer.Render(label);

        Assert.Null(label.Barcode);
        Assert.Contains("Sin codigo de barras", preview.Lines);
        Assert.Contains("SKU: SKU-ROSA", preview.Lines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Label_RejectsInvalidCopyCount(int quantity)
    {
        var (product, sku) = BuildProduct();
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelDataFactory.Create(product, sku, [], quantity));
    }

    [Fact]
    public async Task DisabledPeripherals_DoNotCallDevices()
    {
        var devices = new FakeDevices(throwOnOperation: true);
        var coordinator = new PeripheralOutputCoordinator(
            new FakeStationSettings(new LocalPeripheralSettings(false, null, false, null, false, null, "station.json")),
            devices, devices, devices);

        var receiptResult = await coordinator.TryPrintReceiptAsync(BuildReceipt());
        var labelResult = await coordinator.TryPrintLabelsAsync(BuildLabel());
        var drawerResult = await coordinator.TryOpenCashDrawerAsync();

        Assert.Equal(PeripheralAvailability.Disabled, receiptResult.Status.Availability);
        Assert.Equal(PeripheralAvailability.Disabled, labelResult.Status.Availability);
        Assert.Equal(PeripheralAvailability.Disabled, drawerResult.Status.Availability);
        Assert.Equal(0, devices.OperationCount);
    }

    [Fact]
    public async Task ConfiguredDeviceFailure_IsContainedAndReportsUnavailable()
    {
        var devices = new FakeDevices(throwOnOperation: true);
        var coordinator = new PeripheralOutputCoordinator(
            new FakeStationSettings(new LocalPeripheralSettings(true, "Ticket", true, "Label", true, "Drawer", "station.json")),
            devices, devices, devices);

        var receiptResult = await coordinator.TryPrintReceiptAsync(BuildReceipt());
        var drawerResult = await coordinator.TryOpenCashDrawerAsync();

        Assert.False(receiptResult.Succeeded);
        Assert.False(drawerResult.Succeeded);
        Assert.Equal(PeripheralAvailability.Unavailable, receiptResult.Status.Availability);
        Assert.Contains("venta permanece confirmada", receiptResult.Message);
        Assert.Equal(2, devices.OperationCount);
    }

    [Fact]
    public void RenderingOrReprintingCanonicalData_HasNoCommercialSideEffects()
    {
        var receipt = BuildReceipt();
        var before = receipt with { };

        _ = ReceiptTextRenderer.RenderText(receipt);
        _ = ReceiptTextRenderer.RenderText(receipt);

        Assert.Equal(before, receipt);
        Assert.Equal(4200, receipt.TotalCents);
        Assert.Single(receipt.Lines);
        Assert.Single(receipt.Payments);
    }

    private static SaleResult BuildSaleResult() => new(
        5, "V-000005", 1, 2, null, "Consumidor final", DateTime.UtcNow,
        4300, 100, 4200, 5000, 800,
        [new SaleLineResult(7, 4, "PRD-4", "Alcancia", 1, 4300, 4300, 100, 4200,
            8, "SKU-ROSA", "Rosa")],
        [new SalePaymentResult(1, MetodoPago.EFECTIVO, 5000, null)]);

    private static ReceiptData BuildReceipt() => ReceiptDataFactory.FromSaleResult(
        BuildSaleResult(), new ReceiptBusinessData("Negocio"), "Cajera");

    private static LabelData BuildLabel()
    {
        var (product, sku) = BuildProduct();
        return LabelDataFactory.Create(product, sku, [], 1);
    }

    private static (ProductSummary Product, ProductSkuSummary Sku) BuildProduct()
    {
        var product = new ProductSummary(4, "PRD-4", "Alcancia", null, 1, "Artesanias", null,
            500, 1200, 8, 2, EstadoProducto.ACTIVO, DateTime.UtcNow, null,
            ModoProducto.POR_VARIANTES, 8, 8, 0, 0, 0);
        var sku = new ProductSkuSummary(8, 4, "SKU-ROSA", "Rosa", true, true, null, 1200, 500,
            1200, 500, 8, 0, 0, 0, 8, 8);
        return (product, sku);
    }

    private sealed class FakeStationSettings(LocalPeripheralSettings settings) : IStationSettingsService
    {
        public Task<LocalReportSettings> GetReportSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalReportSettings("reports", "station.json"));
        public Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken = default) => Task.FromResult("backup");
        public Task<LocalPosSettings> GetPosSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalPosSettings(false, "station.json"));
        public Task<LocalPeripheralSettings> GetPeripheralSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task UpdateReportSettingsAsync(UpdateLocalReportSettingsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePosSettingsAsync(UpdateLocalPosSettingsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePeripheralSettingsAsync(UpdateLocalPeripheralSettingsRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDevices(bool throwOnOperation) : IReceiptPrinter, ILabelPrinter, ICashDrawer
    {
        public int OperationCount { get; private set; }
        public Task<PeripheralStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PeripheralStatus(PeripheralAvailability.Available, "Disponible"));

        public Task<PeripheralOperationResult> PrintAsync(ReceiptData receipt, CancellationToken cancellationToken = default) => Operate();
        public Task<PeripheralOperationResult> PrintAsync(LabelData label, CancellationToken cancellationToken = default) => Operate();
        public Task<PeripheralOperationResult> OpenAsync(CancellationToken cancellationToken = default) => Operate();

        private Task<PeripheralOperationResult> Operate()
        {
            OperationCount++;
            if (throwOnOperation) throw new IOException("Fallo simulado");
            return Task.FromResult(PeripheralOperationResult.Success("OK"));
        }
    }
}
