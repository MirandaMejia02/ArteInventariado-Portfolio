using System.Diagnostics;
using System.Globalization;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

/// <summary>
/// Generates a retained, synthetic workbook through the real export pipeline.
/// The evidence is intentionally written outside the repository so it cannot
/// become part of a release payload or contain workstation data.
/// </summary>
public sealed class InventoryValuationMassExportEvidenceTests
{
    [Fact]
    public void CanonicalSummary_ReconcilesPotentialFormulasWhenCoverageIsComplete()
    {
        var row = TestValuationRow(costCentavos: 300, priceCentavos: 500);
        var totals = InventoryValuationCalculator.Summarize([row]);

        Assert.Equal(3_000L, totals.InvestmentCurrentCentavos);
        Assert.Equal(5_000L, totals.PotentialSalesValueCentavos);
        Assert.Equal(4_000L, totals.AvailableSalesValueCentavos);
        Assert.Equal(
            totals.PotentialSalesValueCentavos!.Value - totals.InvestmentCurrentCentavos!.Value,
            totals.PotentialGrossProfitCentavos);
        Assert.Equal(
            totals.PotentialGrossProfitCentavos!.Value * 100m / totals.PotentialSalesValueCentavos!.Value,
            totals.PotentialGrossMarginPercentage);
    }

    [Fact]
    public void CanonicalSummary_DoesNotPublishPartialTotalsWhenCoverageIsUnknown()
    {
        var known = TestValuationRow(costCentavos: 300, priceCentavos: 500);
        var unknownCost = TestValuationRow(productId: 2, costCentavos: null, priceCentavos: 500);
        var totals = InventoryValuationCalculator.Summarize([known, unknownCost]);

        Assert.Equal(2, totals.RowCount);
        Assert.Null(totals.InvestmentCurrentCentavos);
        Assert.Equal(5_000L + 5_000L, totals.PotentialSalesValueCentavos);
        Assert.Null(totals.PotentialGrossProfitCentavos);
        Assert.Null(totals.PotentialGrossMarginPercentage);
        Assert.Equal(1, totals.NonCalculableRows);
    }

    [Fact]
    public async Task ExportExcelAsync_GeneratesRetained500ProductEvidenceWorkbook()
    {
        await using var fixture = await MassExportFixture.CreateAsync();
        var request = new AdvancedReportRequest(
            AdvancedReportKind.CurrentInventoryValue,
            new AnalyticsQueryRequest(new AnalyticsPeriodRequest(AnalyticsPeriodKind.AllHistory)),
            Page: 1,
            PageSize: 200);

        var queryTimer = Stopwatch.StartNew();
        var first = await fixture.Query.QueryAsync(request);
        queryTimer.Stop();

        var exportTimer = Stopwatch.StartNew();
        var result = await fixture.Service.ExportExcelAsync(request);
        exportTimer.Stop();

        var reopenTimer = Stopwatch.StartNew();
        using var workbook = new XLWorkbook(result.FilePath);
        reopenTimer.Stop();

        var expectedSheets = new[]
        {
            "Resumen global",
            "Todos los productos",
            "Detalle SKU",
            "Por categoría",
            "Por subcategoría",
            "Rentabilidad histórica",
            "Definiciones y cobertura"
        };
        Assert.Equal(
            expectedSheets.OrderBy(name => name, StringComparer.Ordinal),
            workbook.Worksheets.Select(sheet => sheet.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(600, result.ExportedRows);
        Assert.Equal(600, SheetTable(workbook, "Detalle SKU").DataRange.RowCount());
        Assert.Equal(500, SheetTable(workbook, "Todos los productos").DataRange.RowCount());
        Assert.True(SheetTable(workbook, "Por categoría").DataRange.RowCount() >= 10);
        Assert.True(SheetTable(workbook, "Por subcategoría").DataRange.RowCount() >= 10);
        Assert.Equal(500, SheetTable(workbook, "Rentabilidad histórica").DataRange.RowCount());

        var allProducts = workbook.Worksheet("Todos los productos");
        var detail = workbook.Worksheet("Detalle SKU");
        Assert.Equal("P0001", allProducts.Cell(10, 1).GetString());
        Assert.Equal(XLDataType.Text, allProducts.Cell(10, 1).DataType);
        Assert.Contains(detail.CellsUsed(), cell => cell.GetString().StartsWith("SKU000001", StringComparison.Ordinal));
        Assert.Contains(allProducts.CellsUsed(), cell => cell.GetString() == "Sin subcategoría");
        Assert.Contains(detail.CellsUsed(), cell => cell.GetString() == "Pendiente de ubicación");
        Assert.Contains(allProducts.CellsUsed(), cell => cell.GetString().Contains("No calculable", StringComparison.Ordinal));
        Assert.Contains(allProducts.CellsUsed(), cell => cell.GetString().Contains("Legacy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(workbook.Worksheet("Rentabilidad histórica").CellsUsed(), cell => cell.GetString() == "No disponible (histórico)");

        var detailHeaderRow = FindHeaderRow(detail, "Inversión actual", "Venta potencial");
        var investmentColumn = FindHeaderColumn(detail, "Inversión actual", detailHeaderRow);
        var potentialColumn = FindHeaderColumn(detail, "Venta potencial", detailHeaderRow);
        var allInvestmentColumn = FindHeaderColumn(allProducts, "Inversión actual", 9);
        var allPotentialColumn = FindHeaderColumn(allProducts, "Venta potencial", 9);
        Assert.Equal(
            SumMoney(detail, investmentColumn, detailHeaderRow + 1),
            SumMoney(allProducts, allInvestmentColumn, 10));
        Assert.Equal(
            SumMoney(detail, potentialColumn, detailHeaderRow + 1),
            SumMoney(allProducts, allPotentialColumn, 10));
        var detailTotalsRow = detail.CellsUsed()
            .Single(cell => cell.GetString() == "Totales")
            .Address.RowNumber;
        Assert.Equal("No calculable", detail.Cell(detailTotalsRow, investmentColumn).GetString());
        Assert.Equal("No calculable", detail.Cell(detailTotalsRow, potentialColumn).GetString());

        var category = workbook.Worksheet("Por categoría");
        var categoryInvestmentColumn = FindHeaderColumn(category, "Inversión actual", 8);
        Assert.Equal(
            SumMoney(detail, investmentColumn, detailHeaderRow + 1),
            SumMoney(category, categoryInvestmentColumn, 9));
        var subcategory = workbook.Worksheet("Por subcategoría");
        var subcategoryInvestmentColumn = FindHeaderColumn(subcategory, "Inversión actual", 8);
        Assert.Equal(
            SumMoney(detail, investmentColumn, detailHeaderRow + 1),
            SumMoney(subcategory, subcategoryInvestmentColumn, 9));

        Assert.Equal(AdvancedReportExportService.CurrencyFormat, detail.Cell(detailHeaderRow + 1, investmentColumn).Style.NumberFormat.Format);
        var historical = workbook.Worksheet("Rentabilidad histórica");
        var historicalMarginColumn = FindHeaderColumn(historical, "Margen histórico", 8);
        Assert.Equal(AdvancedReportExportService.PercentageFormat, historical.Cell(9, historicalMarginColumn).Style.NumberFormat.Format);
        Assert.Contains(
            workbook.Worksheet("Resumen global").CellsUsed(),
            cell => cell.GetString().Contains("Todo el historial:", StringComparison.Ordinal));
        Assert.Contains(
            workbook.Worksheet("Resumen global").CellsUsed(),
            cell => cell.GetString().Contains("Inventario valorado al", StringComparison.Ordinal));
        Assert.Contains(
            workbook.Worksheet("Definiciones y cobertura").CellsUsed(),
            cell => cell.GetString().Contains("no es pronóstico", StringComparison.OrdinalIgnoreCase));

        var summary = workbook.Worksheet("Resumen global");
        var summaryTable = Assert.Single(summary.Tables);
        var summaryLabels = summaryTable.DataRange.Rows()
            .Select(row => row.Cell(1).GetString())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToList();
        Assert.Equal(
            summaryLabels.Count,
            summaryLabels.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("Productos", summaryLabels);
        Assert.Contains("Productos totales", summaryLabels);
        Assert.Contains("SKU actuales", summaryLabels);
        Assert.Contains("Filas legacy sin SKU", summaryLabels);
        Assert.Contains("Filas totales de detalle", summaryLabels);
        Assert.Contains("Filas actuales no calculables", summaryLabels);
        Assert.Contains("Productos históricos sin cobertura de costo", summaryLabels);
        Assert.DoesNotContain("Productos/ventas no calculables", summaryLabels);
        Assert.Equal(
            450,
            summaryTable.DataRange.Rows()
                .Single(row => row.Cell(1).GetString() == "Productos activos")
                .Cell(2)
                .GetValue<int>());
        Assert.Equal(
            50,
            summaryTable.DataRange.Rows()
                .Single(row => row.Cell(1).GetString() == "Productos inactivos")
                .Cell(2)
                .GetValue<int>());
        var currentNonCalculable = Enumerable.Range(detailHeaderRow + 1, 600)
            .Count(row => detail.Cell(row, investmentColumn).GetString() == "No calculable" ||
                          detail.Cell(row, potentialColumn).GetString() == "No calculable");
        Assert.Equal(currentNonCalculable, SummaryValue(summary, "Filas actuales no calculables"));
        Assert.Equal(
            fixture.Query.History.Count(row => row.CoverageState == ProductProfitabilityCoverageState.NoCoverage),
            SummaryValue(summary, "Productos históricos sin cobertura de costo"));
        var investmentSummaryRow = summaryTable.DataRange.Rows()
            .Single(row => row.Cell(1).GetString() == "Inversión actual")
            .RowNumber();
        Assert.Equal("No calculable", summary.Cell(investmentSummaryRow, 2).GetString());
        var potentialSummaryRow = summaryTable.DataRange.Rows()
            .Single(row => row.Cell(1).GetString() == "Venta potencial física")
            .RowNumber();
        Assert.Equal("No calculable", summary.Cell(potentialSummaryRow, 2).GetString());
        Assert.DoesNotContain(
            summary.Column(3).CellsUsed(),
            cell => decimal.TryParse(cell.GetString(), NumberStyles.Number, CultureInfo.CurrentCulture, out _));

        var definitions = workbook.Worksheet("Definiciones y cobertura");
        Assert.True(definitions.Column(2).Width >= 40);
        Assert.All(
            definitions.Range(10, 2, 21, 2).Cells(),
            cell => Assert.True(cell.Style.Alignment.WrapText));
        for (var definitionRow = 10; definitionRow <= 21; definitionRow++)
        {
            Assert.True(definitions.Row(definitionRow).Height >= 24);
        }

        var allText = string.Join("|", workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()).Select(cell => cell.GetString()));
        Assert.DoesNotContain("PasswordHash", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessKey", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", allText, StringComparison.OrdinalIgnoreCase);

        var metrics =
            $"MASS_EXPORT_EVIDENCE={result.FilePath}{Environment.NewLine}" +
            $"PRODUCTS=500{Environment.NewLine}" +
            $"SKU_ROWS=600{Environment.NewLine}" +
            $"QUERY_MS={queryTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
            $"EXPORT_MS={exportTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
            $"REOPEN_MS={reopenTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
            $"SIZE_BYTES={new FileInfo(result.FilePath).Length}{Environment.NewLine}" +
            $"SHEETS={string.Join("|", expectedSheets)}";
        File.WriteAllText(Path.Combine(fixture.EvidenceDirectory, "metrics.txt"), metrics);
        Console.WriteLine(metrics);
    }

    [Fact]
    public void SummaryCountsProductStateWithoutConfusingSkuState()
    {
        var rows = new[]
        {
            SummaryRow(1, "ACTIVO / SKU inactivo"),
            SummaryRow(1, "ACTIVO / SKU activo"),
            SummaryRow(2, "INACTIVO / SKU activo")
        };
        var report = new AdvancedReportResult(
            AdvancedReportCatalog.Get(AdvancedReportKind.CurrentInventoryValue),
            new AnalyticsPeriodDefinition(
                AnalyticsPeriodKind.AllHistory,
                new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc),
                AnalyticsGrouping.Year,
                "Todo el historial",
                true),
            ["Sin filtros específicos"],
            [
                new AdvancedReportColumn("productId", "Producto ID", AdvancedReportValueKind.Integer),
                new AdvancedReportColumn("state", "Estado producto/SKU"),
                new AdvancedReportColumn("stock", "Stock físico", AdvancedReportValueKind.Integer),
                new AdvancedReportColumn("reserved", "Reservado", AdvancedReportValueKind.Integer),
                new AdvancedReportColumn("available", "Disponible", AdvancedReportValueKind.Integer),
                new AdvancedReportColumn("investment", "Inversión actual", AdvancedReportValueKind.Money),
                new AdvancedReportColumn("potential", "Venta potencial", AdvancedReportValueKind.Money)
            ],
            rows,
            rows.Length,
            [],
            "Cobertura sintética.",
            "Sin datos");

        using var workbook = AdvancedReportExportService.BuildInventoryValuationWorkbook(
            report,
            rows,
            "Negocio",
            "admin",
            new DateTime(2026, 9, 13, 18, 0, 0));
        var summary = workbook.Worksheet("Resumen global");
        Assert.Equal(2, SummaryValue(summary, "Productos totales"));
        Assert.Equal(1, SummaryValue(summary, "Productos activos"));
        Assert.Equal(1, SummaryValue(summary, "Productos inactivos"));
        Assert.Contains(
            summary.CellsUsed(),
            cell => cell.GetString() == "Filas actuales no calculables");
        Assert.DoesNotContain(
            summary.CellsUsed(),
            cell => cell.GetString() == "Productos/ventas no calculables");
    }

    private static IXLTable SheetTable(XLWorkbook workbook, string sheetName) =>
        Assert.Single(workbook.Worksheet(sheetName).Tables);

    private static AdvancedReportRow SummaryRow(int productId, string state) =>
        new(new Dictionary<string, object?>
        {
            ["productId"] = productId,
            ["state"] = state,
            ["stock"] = 1,
            ["reserved"] = 0,
            ["available"] = 1,
            ["investment"] = 3m,
            ["potential"] = 5m
        });

    private static int SummaryValue(IXLWorksheet sheet, string label)
    {
        var cell = sheet.Column(1).CellsUsed().Single(item => item.GetString() == label);
        return cell.Worksheet.Cell(cell.Address.RowNumber, 2).GetValue<int>();
    }

    private static int FindHeaderColumn(IXLWorksheet sheet, string header, int headerRow)
    {
        var cell = sheet.Row(headerRow).CellsUsed().FirstOrDefault(item => item.GetString() == header);
        Assert.NotNull(cell);
        return cell!.Address.ColumnNumber;
    }

    private static int FindHeaderRow(IXLWorksheet sheet, params string[] headers) =>
        sheet.RowsUsed()
            .First(row => headers.All(header =>
                row.CellsUsed().Any(cell => cell.GetString() == header)))
            .RowNumber();

    private static decimal SumMoney(IXLWorksheet sheet, int column, int firstDataRow)
    {
        var table = Assert.Single(sheet.Tables);
        var lastDataRow = table.DataRange.RangeAddress.LastAddress.RowNumber;
        return Enumerable.Range(firstDataRow, Math.Max(0, lastDataRow - firstDataRow + 1))
            .Select(row => sheet.Cell(row, column))
            .Where(cell => cell.DataType != XLDataType.Text)
            .Sum(cell => cell.GetValue<decimal>());
    }

    private static InventoryValuationRow TestValuationRow(
        int productId = 1,
        long? costCentavos = 300,
        long? priceCentavos = 500)
        => new(
            ProductId: productId,
            SkuId: productId,
            ProductCode: $"P-{productId}",
            SkuCode: $"SKU-{productId}",
            ProductName: $"Producto {productId}",
            Variant: null,
            CategoryId: 1,
            Category: "Categoría",
            CategoryActive: true,
            SubcategoryId: 1,
            Subcategory: "Subcategoría",
            SubcategoryActive: true,
            ProductState: EstadoProducto.ACTIVO,
            SkuActive: true,
            ExhibitionStock: 6,
            ExhibitionReserved: 1,
            WarehouseStock: 3,
            WarehouseReserved: 1,
            UnassignedStock: 1,
            UnassignedReserved: 0,
            LegacyPhysicalStock: null,
            CostEffectiveCentavos: costCentavos,
            SalePriceEffectiveCentavos: priceCentavos);

    private sealed class MassExportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private MassExportFixture(
            SqliteConnection connection,
            AppDbContext context,
            AdvancedReportExportService service,
            MassQueryService query,
            string evidenceDirectory)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Query = query;
            EvidenceDirectory = evidenceDirectory;
        }

        public AppDbContext Context { get; }
        public AdvancedReportExportService Service { get; }
        public MassQueryService Query { get; }
        public string EvidenceDirectory { get; }

        public static async Task<MassExportFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var admin = new Usuario
            {
                NombreCompleto = "Administrador de evidencia",
                NombreUsuario = "admin-evidence",
                PasswordHash = "evidence-hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(admin);
            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = SystemConfigService.BusinessNameKey,
                Valor = "Negocio sintético de validación",
                FechaModificacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var evidenceDirectory = Path.Combine(
                Path.GetTempPath(),
                "ArteInventariado-2B-R2-evidence",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidenceDirectory);
            var session = new EvidenceSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                RolUsuario.ADMIN));
            var query = new MassQueryService();
            var service = new AdvancedReportExportService(
                context,
                query,
                session,
                new EvidenceAuditService(),
                new EvidencePathProvider(evidenceDirectory),
                new FixedTimeProvider(new DateTimeOffset(2026, 9, 13, 18, 0, 0, TimeSpan.FromHours(-6))),
                new MassProfitabilityService());
            return new MassExportFixture(connection, context, service, query, evidenceDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            // EvidenceDirectory is intentionally retained for visual review.
        }
    }

    private sealed class MassQueryService : IAdvancedReportQueryService
    {
        private static readonly IReadOnlyList<AdvancedReportColumn> Columns =
        [
            new("productId", "Producto ID", AdvancedReportValueKind.Integer),
            new("code", "Código producto"),
            new("sku", "SKU"),
            new("product", "Producto"),
            new("variant", "Variante"),
            new("category", "Categoría"),
            new("subcategory", "Subcategoría"),
            new("state", "Estado producto/SKU"),
            new("stock", "Stock físico", AdvancedReportValueKind.Integer, true),
            new("reserved", "Reservado", AdvancedReportValueKind.Integer, true),
            new("available", "Disponible", AdvancedReportValueKind.Integer, true),
            new("exhibition", "En exhibición", AdvancedReportValueKind.Integer, true),
            new("warehouse", "En bodega", AdvancedReportValueKind.Integer, true),
            new("unassigned", "Sin asignar", AdvancedReportValueKind.Integer, true),
            new("cost", "Costo efectivo", AdvancedReportValueKind.Money),
            new("price", "Precio venta efectivo", AdvancedReportValueKind.Money),
            new("value", "Valor actual", AdvancedReportValueKind.Money, true),
            new("investment", "Inversión actual", AdvancedReportValueKind.Money, true),
            new("potential", "Venta potencial", AdvancedReportValueKind.Money, true),
            new("availableValue", "Venta disponible", AdvancedReportValueKind.Money, true),
            new("gain", "Ganancia bruta potencial", AdvancedReportValueKind.Money, true),
            new("margin", "Margen bruto potencial", AdvancedReportValueKind.Percentage),
            new("locationQuality", "Calidad de ubicación"),
            new("quality", "Calidad/cobertura")
        ];

        private readonly IReadOnlyList<AdvancedReportRow> _rows = BuildRows();
        private readonly IReadOnlyList<ProductProfitabilityRow> _history = BuildHistory();

        public Task<AdvancedReportResult> QueryAsync(
            AdvancedReportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var period = AnalyticsPeriodFactory.Create(
                request.Analytics.Period,
                new DateTimeOffset(2026, 9, 13, 18, 0, 0, TimeSpan.FromHours(-6)),
                TimeZoneInfo.Local);
            var skip = Math.Max(0, request.Page - 1) * Math.Max(1, request.PageSize);
            var rows = _rows.Skip(skip).Take(request.PageSize).ToList();
            var report = new AdvancedReportResult(
                AdvancedReportCatalog.Get(AdvancedReportKind.CurrentInventoryValue),
                period,
                ["Sin filtros específicos"],
                Columns,
                rows,
                _rows.Count,
                BuildSummary(),
                "Cobertura sintética: incluye filas inactivas, legacy y valores no calculables.",
                "Sin datos para los filtros seleccionados.");
            return Task.FromResult(report);
        }

        public IReadOnlyList<ProductProfitabilityRow> History => _history;

        private static IReadOnlyList<AdvancedReportSummaryItem> BuildSummary()
        {
            var totals = InventoryValuationCalculator.Summarize(BuildValuationRows());
            return
            [
                new("Inversión actual", Money(totals.InvestmentCurrentCentavos), AdvancedReportValueKind.Money),
                new("Venta potencial física", Money(totals.PotentialSalesValueCentavos), AdvancedReportValueKind.Money),
                new("Venta disponible", Money(totals.AvailableSalesValueCentavos), AdvancedReportValueKind.Money),
                new("Ganancia bruta potencial", Money(totals.PotentialGrossProfitCentavos), AdvancedReportValueKind.Money),
                new("Margen bruto potencial", totals.PotentialGrossMarginPercentage, AdvancedReportValueKind.Percentage)
            ];
        }

        private static IReadOnlyList<InventoryValuationRow> BuildValuationRows()
        {
            var rows = new List<InventoryValuationRow>(600);
            for (var product = 1; product <= 500; product++)
            {
                var skuCount = product <= 100 ? 2 : 1;
                for (var skuIndex = 0; skuIndex < skuCount; skuIndex++)
                {
                    var isLegacy = product == 500;
                    var physical = (product + skuIndex) % 15;
                    var exhibition = physical == 0 ? 0 : (product + skuIndex) % (physical + 1);
                    var unassigned = product % 7 == 0 ? Math.Min(physical - exhibition, 2) : 0;
                    var warehouse = Math.Max(0, physical - exhibition - unassigned);
                    var reserved = isLegacy ? (int?)null : Math.Min(physical, (product + skuIndex) % 3);
                    decimal? cost = product % 37 == 0 ? null : product % 19 == 0 ? 0m : 300m + (product % 13) * 25m;
                    decimal? price = product % 41 == 0 ? null : product % 23 == 0 ? 0m : 500m + (product % 17) * 30m;
                    var category = cost is null
                        ? "Categoría no calculable"
                        : $"Categoría {(product % 10) + 1}";
                    var subcategory = cost is null
                        ? "Subcategoría no calculable"
                        : product % 11 == 0
                            ? "Sin subcategoría"
                            : $"Subcategoría {(product % 20) + 1}";
                    rows.Add(new InventoryValuationRow(
                        ProductId: product,
                        SkuId: isLegacy ? null : product * 10 + skuIndex + 1,
                        ProductCode: $"P{product:0000}",
                        SkuCode: isLegacy ? null : $"SKU{product:000000}{skuIndex + 1}",
                        ProductName: $"Producto artesanal {product:0000}",
                        Variant: skuCount > 1 ? $"Variante {skuIndex + 1}" : null,
                        CategoryId: (product % 10) + 1,
                        Category: category,
                        CategoryActive: true,
                        SubcategoryId: product % 11 == 0 ? null : (product % 20) + 1,
                        Subcategory: subcategory,
                        SubcategoryActive: product % 29 == 0 ? false : true,
                        ProductState: product % 10 == 0 ? EstadoProducto.INACTIVO : EstadoProducto.ACTIVO,
                        SkuActive: isLegacy || !(skuIndex == 1 && product % 9 == 0),
                        ExhibitionStock: isLegacy ? 0 : exhibition,
                        ExhibitionReserved: isLegacy ? 0 : reserved ?? 0,
                        WarehouseStock: isLegacy ? 0 : warehouse,
                        WarehouseReserved: 0,
                        UnassignedStock: isLegacy ? 0 : unassigned,
                        UnassignedReserved: 0,
                        LegacyPhysicalStock: isLegacy ? physical : null,
                        CostEffectiveCentavos: ToCentavos(cost),
                        SalePriceEffectiveCentavos: ToCentavos(price)));
                }
            }
            return rows;
        }

        private static IReadOnlyList<AdvancedReportRow> BuildRows() =>
            BuildValuationRows().Select(row => new AdvancedReportRow(new Dictionary<string, object?>
            {
                ["productId"] = row.ProductId,
                ["code"] = row.ProductCode,
                ["sku"] = row.SkuCode ?? "Sin SKU (histórico)",
                ["product"] = row.ProductName,
                ["variant"] = row.Variant ?? string.Empty,
                ["category"] = row.Category,
                ["subcategory"] = row.Subcategory,
                ["state"] = row.StateText,
                ["stock"] = row.PhysicalStock,
                ["reserved"] = row.ReservedStock,
                ["available"] = row.AvailableStock,
                ["exhibition"] = row.IsLegacy ? (int?)null : row.ExhibitionStock,
                ["warehouse"] = row.IsLegacy ? (int?)null : row.WarehouseStock,
                ["unassigned"] = row.IsLegacy ? (int?)null : row.UnassignedStock,
                ["cost"] = Money(row.CostEffectiveCentavos),
                ["price"] = Money(row.SalePriceEffectiveCentavos),
                ["value"] = Money(row.InvestmentCurrentCentavos),
                ["investment"] = Money(row.InvestmentCurrentCentavos),
                ["potential"] = Money(row.PotentialSalesValueCentavos),
                ["availableValue"] = Money(row.AvailableSalesValueCentavos),
                ["gain"] = Money(row.PotentialGrossProfitCentavos),
                ["margin"] = row.PotentialGrossMarginPercentage,
                ["locationQuality"] = row.LocationQuality,
                ["quality"] = row.Quality
            })).ToList();

        private static long? ToCentavos(decimal? amount) =>
            amount.HasValue ? checked((long)(amount.Value * 100m)) : null;

        private static decimal? Money(long? centavos) =>
            centavos.HasValue ? centavos.Value / 100m : null;

        private static IReadOnlyList<ProductProfitabilityRow> BuildHistory()
        {
            var rows = new List<ProductProfitabilityRow>(500);
            var now = new DateTime(2026, 9, 13, 18, 0, 0, DateTimeKind.Utc);
            for (var product = 1; product <= 500; product++)
            {
                var noSales = product % 5 == 0;
                var netUnits = noSales ? 0 : (product % 7) + 1;
                var netSales = noSales ? 0L : product * 125L;
                var knownCost = noSales || product % 37 == 0 ? (long?)null : netSales * 55 / 100;
                var gain = knownCost.HasValue ? netSales - knownCost.Value : (long?)null;
                var subcategory = product % 11 == 0 ? null : $"Subcategoría {(product % 20) + 1}";
                var skuCodes = product == 500
                    ? Array.Empty<string>()
                    : product <= 100
                        ? [$"SKU{product:000000}1", $"SKU{product:000000}2"]
                        : [$"SKU{product:000000}1"];
                rows.Add(new ProductProfitabilityRow(
                    product,
                    $"P{product:0000}",
                    $"Producto artesanal {product:0000}",
                    $"Categoría {(product % 10) + 1}",
                    netUnits,
                    product % 17 == 0 ? 1 : 0,
                    netSales == 0 ? null : 500m,
                    knownCost is null ? null : 300m,
                    netSales,
                    knownCost,
                    knownCost,
                    knownCost.HasValue && netUnits > 0 ? (decimal?)((netSales - knownCost.Value) / (decimal)netUnits) : null,
                    gain,
                    netSales == 0 ? null : gain.HasValue ? gain.Value * 100m / netSales : null,
                    knownCost.HasValue ? 100m : null,
                    noSales ? ProductProfitabilityCoverageState.NoSales : knownCost.HasValue ? ProductProfitabilityCoverageState.Complete : ProductProfitabilityCoverageState.NoCoverage,
                    CategoryId: (product % 10) + 1,
                    SkuCodes: skuCodes,
                    SubcategoryId: product % 11 == 0 ? null : (product % 20) + 1,
                    SubcategoryName: subcategory,
                    FirstSaleUtc: noSales ? null : now.AddDays(-(product % 90)),
                    LastSaleUtc: noSales ? null : now.AddDays(-(product % 3))));
            }
            return rows;
        }
    }

    private sealed class MassProfitabilityService : IProductProfitabilityQueryService
    {
        private readonly MassQueryService _source = new();

        public Task<ProductProfitabilityReport> QueryAsync(
            ProductProfitabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var period = AnalyticsPeriodFactory.Create(
                request.Analytics.Period,
                new DateTimeOffset(2026, 9, 13, 18, 0, 0, TimeSpan.FromHours(-6)),
                TimeZoneInfo.Local);
            var rows = _source.History.Skip(Math.Max(0, request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
            var netSales = rows.Sum(row => row.NetSalesCentavos);
            var knownCost = rows.All(row => row.KnownCostCentavos.HasValue) ? rows.Sum(row => row.KnownCostCentavos!.Value) : (long?)null;
            var gain = rows.All(row => row.TotalGainCentavos.HasValue) ? rows.Sum(row => row.TotalGainCentavos!.Value) : (long?)null;
            return Task.FromResult(new ProductProfitabilityReport(
                period,
                rows,
                _source.History.Count,
                _source.History.Count(row => row.NetUnits > 0),
                netSales,
                knownCost,
                gain,
                gain.HasValue && netSales != 0 ? gain.Value * 100m / netSales : null,
                knownCost.HasValue ? 100m : null,
                rows.Count(row => row.CoverageState == ProductProfitabilityCoverageState.Complete),
                rows.Count(row => row.CoverageState == ProductProfitabilityCoverageState.Partial),
                rows.Count(row => row.CoverageState == ProductProfitabilityCoverageState.NoCoverage),
                rows.Count(row => row.CoverageState == ProductProfitabilityCoverageState.NoSales),
                "Cobertura sintética de prueba."));
        }

        public Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EvidencePathProvider(string root) : IReportPathProvider
    {
        public Task<string> GetRootDirectoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);

        public Task<string> GetDirectoryAsync(string category, CancellationToken cancellationToken = default)
        {
            var directory = Path.Combine(root, category);
            Directory.CreateDirectory(directory);
            return Task.FromResult(directory);
        }
    }

    private sealed class EvidenceAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EvidenceSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }
}
