using System.Globalization;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class AdvancedReportExportService : IAdvancedReportExportService
{
    internal const string CurrencyFormat = "$#,##0.00";
    internal const string PercentageFormat = "0.00%";
    private const int ExportBatchSize = 1000;
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IAdvancedReportQueryService _query;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;
    private readonly IReportPathProvider _paths;
    private readonly TimeProvider _timeProvider;
    private readonly IProductProfitabilityQueryService? _profitability;
    private readonly IFinancialAnalyticsQueryService? _financial;

    [ActivatorUtilitiesConstructor]
    public AdvancedReportExportService(
        IDbContextFactory<AppDbContext> contextFactory,
        IAdvancedReportQueryService query,
        ICurrentSession session,
        IAuditService audit,
        IReportPathProvider paths,
        TimeProvider timeProvider,
        IProductProfitabilityQueryService? profitability = null,
        IFinancialAnalyticsQueryService? financial = null)
    {
        _contextFactory = contextFactory;
        _query = query;
        _session = session;
        _audit = audit;
        _paths = paths;
        _timeProvider = timeProvider;
        _profitability = profitability;
        _financial = financial;
    }

    internal AdvancedReportExportService(
        AppDbContext context,
        IAdvancedReportQueryService query,
        ICurrentSession session,
        IAuditService audit,
        IReportPathProvider paths,
        TimeProvider timeProvider,
        IProductProfitabilityQueryService? profitability = null,
        IFinancialAnalyticsQueryService? financial = null)
    {
        _legacyContext = context;
        _query = query;
        _session = session;
        _audit = audit;
        _paths = paths;
        _timeProvider = timeProvider;
        _profitability = profitability;
        _financial = financial;
    }

    public async Task<AdvancedReportExportResult> ExportExcelAsync(
        AdvancedReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        UsuarioSesion user;
        if (request.Kind == AdvancedReportKind.ProductProfitability)
        {
            if (_profitability is null)
            {
                throw new InvalidOperationException(
                    "El servicio de rentabilidad por producto no está configurado.");
            }

            await _profitability.EnsureAuthorizedAsync(cancellationToken);
            user = await EnsureActiveUserAsync(lease.Context, cancellationToken);
        }
        else
        {
            user = await EnsureActiveAdminAsync(lease.Context, cancellationToken);
        }
        var businessName = await GetBusinessNameAsync(lease.Context, cancellationToken);
        var generatedAt = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), TimeZoneInfo.Local).DateTime;

        var first = await _query.QueryAsync(request with { Page = 1, PageSize = ExportBatchSize }, cancellationToken);
        var rows = new List<AdvancedReportRow>(Math.Min(first.TotalRows, 10_000));
        rows.AddRange(first.Rows);
        var page = 2;
        while (rows.Count < first.TotalRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = await _query.QueryAsync(request with { Page = page, PageSize = ExportBatchSize }, cancellationToken);
            if (next.Rows.Count == 0)
            {
                break;
            }
            rows.AddRange(next.Rows);
            page++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var directory = await _paths.GetDirectoryAsync("Reportes avanzados", cancellationToken);
        var safeName = SanitizeFileName(first.Definition.Kind == AdvancedReportKind.CurrentInventoryValue
            ? "Valoración general de productos"
            : first.Definition.Name);
        var fileName = $"{generatedAt:yyyyMMdd_HHmmss}_{safeName}.xlsx";
        var filePath = UniquePath(directory, fileName);

        ProductProfitabilityReport? historical = null;
        FinancialAnalyticsReport? financial = null;
        if (first.Definition.Kind == AdvancedReportKind.CurrentInventoryValue)
        {
            historical = await LoadAllHistoricalProfitabilityAsync(request, cancellationToken);
            if (_financial is not null)
            {
                var financialRequest = request.Analytics with
                {
                    CategoryId = request.CategoryId,
                    SubcategoryId = request.SubcategoryId,
                    SubcategoryFilter = request.SubcategoryFilter
                };
                financial = await _financial.GetAsync(financialRequest, cancellationToken);
            }
        }

        try
        {
            // ClosedXML builds and serializes the workbook synchronously. Keep
            // that CPU/file work off the WinForms synchronization context so
            // the progress status remains repaintable and cancellation can be
            // honored before the file is committed.
            await Task.Run(() =>
            {
                using var workbook = first.Definition.Kind == AdvancedReportKind.CurrentInventoryValue
                    ? BuildInventoryValuationWorkbook(first, rows, businessName, user.NombreUsuario, generatedAt, historical, financial)
                    : BuildWorkbook(first, rows, businessName, user.NombreUsuario, generatedAt);
                cancellationToken.ThrowIfCancellationRequested();
                workbook.SaveAs(filePath);
            }, cancellationToken);
            await _audit.RegistrarAsync(new AuditEntryRequest(
                user.UsuarioId,
                TipoBitacora.EXPORTACION_EXCEL,
                "reportes_avanzados",
                null,
                $"Exportó {first.Definition.Name} ({rows.Count:N0} filas)."), cancellationToken);
            return new AdvancedReportExportResult(filePath, rows.Count, generatedAt);
        }
        catch
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            throw;
        }
    }

    private async Task<ProductProfitabilityReport?> LoadAllHistoricalProfitabilityAsync(
        AdvancedReportRequest request,
        CancellationToken cancellationToken)
    {
        if (_profitability is null)
        {
            return null;
        }

        var first = await _profitability.QueryAsync(
            new ProductProfitabilityRequest(
                request.Analytics,
                Search: request.Search,
                ProductId: request.ProductId,
                CategoryId: request.CategoryId,
                SubcategoryId: request.SubcategoryId,
                SubcategoryFilter: request.SubcategoryFilter,
                Page: 1,
                PageSize: 1000),
            cancellationToken);
        var all = new List<ProductProfitabilityRow>(Math.Max(first.TotalRows, first.Rows.Count));
        all.AddRange(first.Rows);
        var page = 2;
        while (all.Count < first.TotalRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = await _profitability.QueryAsync(
                new ProductProfitabilityRequest(
                    request.Analytics,
                    Search: request.Search,
                    ProductId: request.ProductId,
                    CategoryId: request.CategoryId,
                    SubcategoryId: request.SubcategoryId,
                    SubcategoryFilter: request.SubcategoryFilter,
                    Page: page++,
                    PageSize: 1000),
                cancellationToken);
            if (next.Rows.Count == 0)
            {
                break;
            }
            all.AddRange(next.Rows);
        }

        return first with { Rows = all, TotalRows = all.Count };
    }

    internal static XLWorkbook BuildWorkbook(
        AdvancedReportResult report,
        IReadOnlyList<AdvancedReportRow> rows,
        string businessName,
        string userName,
        DateTime generatedAt)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SanitizeWorksheetName(report.Definition.Name));
        var width = Math.Max(1, report.Columns.Count);
        var preserveUncalculableValues = report.Definition.Kind is
            AdvancedReportKind.ProductProfitability or AdvancedReportKind.CurrentInventoryValue;
        var row = 1;

        sheet.Cell(row, 1).Value = businessName;
        sheet.Range(row, 1, row, width).Merge();
        sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.White);
        sheet.Cell(row, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#8C1D40"));
        row++;
        sheet.Cell(row, 1).Value = report.Definition.Name;
        sheet.Range(row, 1, row, width).Merge();
        sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.FromHtml("#8C1D40"));
        row++;
        WriteMetadata(sheet, row++, width, "Período", report.Period.DisplayName);
        WriteMetadata(sheet, row++, width, "Filtros", string.Join(" | ", report.AppliedFilters));
        WriteMetadata(sheet, row++, width, "Generado", generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        WriteMetadata(sheet, row++, width, "Usuario", userName);
        if (!string.IsNullOrWhiteSpace(report.CoverageNote))
        {
            WriteMetadata(sheet, row++, width, "Cobertura", report.CoverageNote);
        }
        row++;

        if (report.Summary.Count > 0)
        {
            sheet.Cell(row, 1).Value = "Resumen";
            sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#8C1D40"));
            row++;
            foreach (var item in report.Summary)
            {
                sheet.Cell(row, 1).Value = item.Label;
                if (width > 1)
                {
                    SetCellValue(
                        sheet.Cell(row, 2),
                        item.Value,
                        item.ValueKind,
                        preserveUncalculableValues);
                }
                row++;
            }
            row++;
        }

        var headerRow = row;
        for (var columnIndex = 0; columnIndex < report.Columns.Count; columnIndex++)
        {
            sheet.Cell(headerRow, columnIndex + 1).Value = report.Columns[columnIndex].Title;
        }
        var header = sheet.Range(headerRow, 1, headerRow, width);
        header.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
        header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#A62A57"));
        header.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        row++;

        if (rows.Count == 0)
        {
            sheet.Cell(row, 1).Value = "Sin datos para los filtros seleccionados.";
            sheet.Range(row, 1, row, width).Merge();
            sheet.Cell(row, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
            sheet.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;
        }
        else
        {
            foreach (var dataRow in rows)
            {
                cancellationSafeWrite(
                    report.Columns,
                    dataRow,
                    sheet,
                    row,
                    preserveUncalculableValues);
                row++;
            }
            AddExcelTable(sheet, headerRow, width, row - 1, "DetalleReporte");
            WriteTotals(
                report.Columns,
                rows,
                sheet,
                row,
                preserveUncalculableValues);
            row++;
        }

        var lastRow = Math.Max(headerRow, row - 1);
        sheet.Range(headerRow, 1, lastRow, width).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        sheet.Range(headerRow, 1, lastRow, width).Style.Border.SetInsideBorder(XLBorderStyleValues.Hair);
        if (!sheet.Tables.Any())
        {
            sheet.Range(headerRow, 1, lastRow, width).SetAutoFilter();
        }
        sheet.SheetView.FreezeRows(headerRow);
        ApplyReadableLayout(sheet, width, headerRow, lastRow);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        sheet.PageSetup.Footer.Center.AddText("Página &P de &N");
        return workbook;
    }

    internal static XLWorkbook BuildInventoryValuationWorkbook(
        AdvancedReportResult report,
        IReadOnlyList<AdvancedReportRow> rows,
        string businessName,
        string userName,
        DateTime generatedAt)
        => BuildInventoryValuationWorkbook(
            report,
            rows,
            businessName,
            userName,
            generatedAt,
            historical: null,
            financial: null);

    internal static XLWorkbook BuildInventoryValuationWorkbook(
        AdvancedReportResult report,
        IReadOnlyList<AdvancedReportRow> rows,
        string businessName,
        string userName,
        DateTime generatedAt,
        ProductProfitabilityReport? historical,
        FinancialAnalyticsReport? financial)
    {
        var workbook = BuildWorkbook(report, rows, businessName, userName, generatedAt);
        workbook.Worksheet(1).Name = "Detalle SKU";

        var summary = workbook.Worksheets.Add("Resumen global", 1);
        WriteValuationSummarySheet(summary, report, rows, businessName, userName, generatedAt, historical, financial);
        WriteAllProductsSheet(
            workbook.Worksheets.Add("Todos los productos"),
            rows,
            historical,
            report,
            businessName,
            userName,
            generatedAt);
        WriteValuationGroupSheet(
            workbook.Worksheets.Add("Por categoría"),
            rows,
            "category",
            "Categoría",
            report,
            businessName,
            userName,
            generatedAt);
        WriteValuationGroupSheet(
            workbook.Worksheets.Add("Por subcategoría"),
            rows,
            "subcategory",
            "Subcategoría",
            report,
            businessName,
            userName,
            generatedAt);
        WriteHistoricalProfitabilitySheet(
            workbook.Worksheets.Add("Rentabilidad histórica"),
            historical,
            report,
            businessName,
            userName,
            generatedAt);
        WriteDefinitionsSheet(
            workbook.Worksheets.Add("Definiciones y cobertura"),
            report,
            historical,
            financial,
            businessName,
            userName,
            generatedAt);
        return workbook;
    }

    private static void WriteValuationSummarySheet(
        IXLWorksheet sheet,
        AdvancedReportResult report,
        IReadOnlyList<AdvancedReportRow> rows,
        string businessName,
        string userName,
        DateTime generatedAt,
        ProductProfitabilityReport? historical,
        FinancialAnalyticsReport? financial)
    {
        const int width = 3;
        sheet.Cell(1, 1).Value = businessName;
        sheet.Range(1, 1, 1, width).Merge();
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.White);
        sheet.Cell(1, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#8C1D40"));
        sheet.Cell(2, 1).Value = "Valor y ganancia potencial de inventario";
        sheet.Range(2, 1, 2, width).Merge();
        sheet.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.FromHtml("#8C1D40"));
        WriteMetadata(sheet, 4, width, "Filtros", string.Join(" | ", report.AppliedFilters));
        WriteMetadata(sheet, 5, width, "Generado", generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        WriteMetadata(sheet, 6, width, "Usuario", userName);
        WriteMetadata(sheet, 7, width, "Inventario valorado al", generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        WriteMetadata(sheet, 8, width, "Actividad histórica", FormatHistoricalPeriod(historical?.Period ?? report.Period, generatedAt, historical));
        WriteMetadata(sheet, 9, width, "Fuente", "inventario_saldos; una fila por SKU y sus tres ubicaciones.");
        WriteMetadata(sheet, 10, width, "Definición", "La ganancia es bruta potencial; no descuenta gastos, impuestos, descuentos ni mermas futuras.");

        sheet.Cell(12, 1).Value = "Métrica";
        sheet.Cell(12, 2).Value = "Valor";
        sheet.Cell(12, 3).Value = "Notas";
        StyleHeader(sheet.Range(12, 1, 12, width));
        var row = 13;
        foreach (var item in report.Summary)
        {
            // A generic "Productos" value is ambiguous in a valuation
            // export (it was historically confused with products having
            // sales). The explicit catalog metrics below are authoritative.
            if (string.Equals(item.Label, "Productos", StringComparison.OrdinalIgnoreCase) ||
                sheet.Column(1).CellsUsed().Any(cell =>
                    string.Equals(cell.GetString(), item.Label, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            sheet.Cell(row, 1).Value = item.Label;
            SetCellValue(sheet.Cell(row, 2), item.Value, item.ValueKind, preserveProductProfitabilityStates: true);
            sheet.Cell(row, 3).Value = item.Label switch
            {
                "Inversión actual" => "Stock físico × costo efectivo vigente",
                "Venta potencial física" => "Stock físico × precio de venta efectivo",
                "Venta disponible" => "Disponible (físico − reservado) × precio efectivo",
                "Ganancia bruta potencial" => "Venta potencial física − inversión actual",
                "Margen bruto potencial" => "Ganancia potencial ÷ venta potencial física",
                _ => string.Empty
            };
            row++;
        }

        var totals = SummarizeCurrentRows(rows);
        AddSummaryMetric(sheet, ref row, "Productos totales", totals.productCount, AdvancedReportValueKind.Integer, "Catálogo actual completo, incluidos productos sin movimiento.");
        AddSummaryMetric(sheet, ref row, "Productos activos", totals.activeProducts, AdvancedReportValueKind.Integer, "Productos activos del catálogo actual.");
        AddSummaryMetric(sheet, ref row, "Productos inactivos", totals.inactiveProducts, AdvancedReportValueKind.Integer, "Productos inactivos que conservan su valoración si tienen stock.");
        AddSummaryMetric(sheet, ref row, "SKU actuales", totals.skuCount, AdvancedReportValueKind.Integer, "SKU actuales; las filas legacy no inventan SKU.");
        AddSummaryMetric(sheet, ref row, "Filas legacy sin SKU", totals.legacyRows, AdvancedReportValueKind.Integer, "Filas históricas sin SKU o ubicación reconstruible.");
        AddSummaryMetric(sheet, ref row, "Filas totales de detalle", totals.detailRows, AdvancedReportValueKind.Integer, "Total de filas exportadas en Detalle SKU.");
        AddSummaryMetric(sheet, ref row, "Unidades físicas", totals.physical, AdvancedReportValueKind.Integer, "Exhibición + bodega + sin asignar.");
        AddSummaryMetric(sheet, ref row, "Unidades reservadas", totals.reserved, AdvancedReportValueKind.Integer, "Puede ser no calculable si existe una fila legacy.");
        AddSummaryMetric(sheet, ref row, "Unidades disponibles", totals.available, AdvancedReportValueKind.Integer, "Físico − reservado.");
        AddSummaryMetric(sheet, ref row, "Ventas netas históricas", Money(financial?.Finance.NetSales.Value ?? historical?.NetSalesCentavos), AdvancedReportValueKind.Money, "Actividad del período seleccionado.");
        AddSummaryMetric(sheet, ref row, "Costo histórico vendido cubierto", Money(financial?.Finance.CostOfGoodsSold.Value ?? historical?.KnownCostCentavos), AdvancedReportValueKind.Money, "Solo costo histórico cubierto.");
        AddSummaryMetric(sheet, ref row, "Ganancia bruta histórica realizada", Money(financial?.Finance.GrossProfit.Value ?? historical?.CalculableGainCentavos), AdvancedReportValueKind.Money, "Ganancia realizada; no es potencial.");
        AddSummaryMetric(sheet, ref row, "Gastos operativos históricos", Money(financial?.Finance.OperatingExpenses.Value), AdvancedReportValueKind.Money, "Gastos del período; no afectan la ganancia potencial.");
        long? netHistorical = financial is null
            ? null
            : checked(financial.Finance.GrossProfit.Value - financial.Finance.OperatingExpenses.Value);
        AddSummaryMetric(sheet, ref row, "Resultado neto histórico", Money(netHistorical), AdvancedReportValueKind.Money, "Ganancia histórica cubierta − gastos operativos.");
        AddSummaryMetric(sheet, ref row, "Cobertura de costos históricos", financial?.Finance.CostCoverage.Percentage ?? historical?.CostCoveragePercentage, AdvancedReportValueKind.Percentage, "Cobertura del costo histórico; no se promedia margen de filas.");
        AddSummaryMetric(
            sheet,
            ref row,
            "Filas actuales no calculables",
            totals.nonCalculable,
            AdvancedReportValueKind.Integer,
            "Filas SKU/legacy cuya valoración actual no puede calcularse completamente.");
        AddSummaryMetric(
            sheet,
            ref row,
            "Productos históricos sin cobertura de costo",
            historical?.NoCoverageProducts,
            AdvancedReportValueKind.Integer,
            "Productos cuya rentabilidad histórica no tiene cobertura completa.");

        AddExcelTable(sheet, 12, width, row - 1, "ResumenGlobal");

        sheet.Cell(row + 1, 1).Value = "Cobertura / alertas";
        sheet.Cell(row + 1, 1).Style.Font.SetBold();
        sheet.Cell(row + 2, 1).Value = report.CoverageNote ?? "Sin observaciones.";
        sheet.Range(row + 2, 1, row + 2, width).Merge();
        sheet.Cell(row + 2, 1).Style.Alignment.WrapText = true;
        FinishSheet(sheet, width, 12, row + 2);
    }

    private static void AddSummaryMetric(
        IXLWorksheet sheet,
        ref int row,
        string label,
        object? value,
        AdvancedReportValueKind valueKind,
        string note)
    {
        if (sheet.Column(1).CellsUsed().Any(cell =>
                string.Equals(cell.GetString(), label, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        sheet.Cell(row, 1).Value = label;
        SetCellValue(sheet.Cell(row, 2), value, valueKind, preserveProductProfitabilityStates: true);
        sheet.Cell(row, 3).Value = note;
        row++;
    }

    private static (
        int productCount,
        int activeProducts,
        int inactiveProducts,
        int skuCount,
        int legacyRows,
        int detailRows,
        int physical,
        int? reserved,
        int? available,
        int nonCalculable)
        SummarizeCurrentRows(IReadOnlyList<AdvancedReportRow> rows)
    {
        var productCount = rows
            .Select(row => row["productId"])
            .Where(value => value is not null)
            .Select(ToInt)
            .Distinct()
            .Count();
        var productStates = rows
            .Where(row => row["productId"] is not null)
            .GroupBy(row => ToInt(row["productId"]))
            .Select(group => group.Any(IsInactiveProduct))
            .ToList();
        var inactiveProducts = productStates.Count(value => value);
        var activeProducts = productStates.Count - inactiveProducts;
        var skuCount = rows.Count(HasSku);
        var legacyRows = rows.Count(row => !HasSku(row));
        var physical = rows.Sum(row => ToInt(row["stock"]));
        var reservedValues = rows.Select(row => row["reserved"]).ToList();
        var availableValues = rows.Select(row => row["available"]).ToList();
        var reserved = reservedValues.Any(value => value is null)
            ? (int?)null
            : checked(reservedValues.Sum(ToInt));
        var available = availableValues.Any(value => value is null)
            ? (int?)null
            : checked(availableValues.Sum(ToInt));
        var nonCalculable = rows.Count(row => row["investment"] is null || row["potential"] is null);
        return (
            productCount,
            activeProducts,
            inactiveProducts,
            skuCount,
            legacyRows,
            rows.Count,
            physical,
            reserved,
            available,
            nonCalculable);
    }

    private static bool IsInactiveProduct(AdvancedReportRow row)
    {
        // Current inventory rows expose a combined product/SKU state for
        // compatibility. Only the product portion before the separator is
        // authoritative for the product-level count.
        var structuredState = row["productState"]?.ToString();
        var state = string.IsNullOrWhiteSpace(structuredState)
            ? row["state"]?.ToString()
            : structuredState;
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        var separator = state.IndexOf('/');
        var productState = separator >= 0 ? state[..separator] : state;
        return string.Equals(productState.Trim(), "INACTIVO", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatHistoricalPeriod(
        AnalyticsPeriodDefinition period,
        DateTime generatedAt,
        ProductProfitabilityReport? historical = null)
    {
        var effectiveStart = period.LocalStart;
        var startText = string.Empty;
        if (period.Kind == AnalyticsPeriodKind.AllHistory)
        {
            var firstSaleUtc = historical?.Rows
                .Select(row => row.FirstSaleUtc)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .OrderBy(value => value)
                .FirstOrDefault();
            if (firstSaleUtc.HasValue && firstSaleUtc.Value != default)
            {
                effectiveStart = TimeZoneInfo.ConvertTime(
                    new DateTimeOffset(DateTime.SpecifyKind(firstSaleUtc.Value, DateTimeKind.Utc)),
                    TimeZoneInfo.Local);
            }
            else
            {
                // The period factory uses an open lower bound for querying all
                // history. Never expose that sentinel (01/01/1970) as if it
                // were a real business operation when no sale snapshot exists.
                startText = "desde el primer registro válido";
            }
        }

        var start = string.IsNullOrWhiteSpace(startText)
            ? effectiveStart.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)
            : startText;
        var end = period.LocalEndExclusive > new DateTimeOffset(generatedAt)
            ? generatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)
            : period.LocalEndExclusive.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        return $"{period.DisplayName}: {start} a {end}";
    }

    private static int ToInt(object? value) => value switch
    {
        int number => number,
        long number => checked((int)number),
        decimal number => checked((int)number),
        _ => int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0
    };

    private static void WriteAllProductsSheet(
        IXLWorksheet sheet,
        IReadOnlyList<AdvancedReportRow> currentRows,
        ProductProfitabilityReport? historical,
        AdvancedReportResult report,
        string businessName,
        string userName,
        DateTime generatedAt)
    {
        const int width = 24;
        WriteSheetTitle(sheet, businessName, "Valoración general de productos", width);
        WriteMetadata(sheet, 4, width, "Filtros", string.Join(" | ", report.AppliedFilters));
        WriteMetadata(sheet, 5, width, "Inventario valorado al", generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        WriteMetadata(sheet, 6, width, "Actividad histórica", FormatHistoricalPeriod(historical?.Period ?? report.Period, generatedAt, historical));
        WriteMetadata(sheet, 7, width, "Cobertura", report.CoverageNote ?? "Sin observaciones.");

        var headers = new[]
        {
            "Código", "Producto", "Categoría", "Subcategoría", "Estado", "Cantidad de SKU",
            "Stock físico", "Reservado", "Disponible", "Exhibición", "Bodega", "Sin asignar",
            "Inversión actual", "Venta potencial", "Venta disponible", "Ganancia bruta potencial",
            "Margen potencial", "Unidades vendidas históricas", "Ventas netas históricas",
            "Costo histórico vendido", "Ganancia histórica realizada", "Margen histórico", "Devoluciones", "Calidad/cobertura"
        };
        const int headerRow = 9;
        WriteHeaders(sheet, headerRow, headers);
        var historyByProduct = historical?.Rows
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.First());
        var rowIndex = headerRow + 1;
        foreach (var group in currentRows
            .Where(row => row["productId"] is not null)
            .GroupBy(row => ToInt(row["productId"]))
            .OrderBy(group => group.First()["product"]?.ToString(), StringComparer.CurrentCultureIgnoreCase))
        {
            var values = group.ToList();
            ProductProfitabilityRow? history = null;
            historyByProduct?.TryGetValue(group.Key, out history);
            var categories = DistinctText(values, "category");
            var subcategories = DistinctText(values, "subcategory");
            var quality = string.Join(" · ", DistinctText(values, "quality")
                .Concat(history is null ? [] : [CoverageStateText(history.CoverageState)])
                .Distinct(StringComparer.CurrentCultureIgnoreCase));

            sheet.Cell(rowIndex, 1).Value = FirstText(values, "code");
            sheet.Cell(rowIndex, 2).Value = FirstText(values, "product");
            sheet.Cell(rowIndex, 3).Value = categories.Count == 1 ? categories[0] : "Múltiples";
            sheet.Cell(rowIndex, 4).Value = subcategories.Count == 1 ? subcategories[0] : "Múltiples";
            sheet.Cell(rowIndex, 5).Value = string.Join(" / ", DistinctText(values, "state"));
            SetCellValue(sheet.Cell(rowIndex, 6), values.Count(HasSku), AdvancedReportValueKind.Integer);
            SetCellValue(sheet.Cell(rowIndex, 7), SumDecimal(values, "stock"), AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(rowIndex, 8), SumDecimal(values, "reserved"), AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(rowIndex, 9), SumDecimal(values, "available"), AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(rowIndex, 10), SumDecimal(values, "exhibition"), AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(rowIndex, 11), SumDecimal(values, "warehouse"), AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(rowIndex, 12), SumDecimal(values, "unassigned"), AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(rowIndex, 13), SumDecimal(values, "investment"), AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 14), SumDecimal(values, "potential"), AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 15), SumDecimal(values, "availableValue"), AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 16), SumDecimal(values, "gain"), AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 17), MarginFromRows(values), AdvancedReportValueKind.Percentage, true);
            SetCellValue(sheet.Cell(rowIndex, 18), history?.NetUnits, AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(rowIndex, 19), history is null ? null : Money(history.NetSalesCentavos), AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 20), history?.KnownCostCentavos is long known ? Money(known) : null, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 21), history?.TotalGainCentavos is long gain ? Money(gain) : null, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 22), history?.MarginPercentage, AdvancedReportValueKind.Percentage, true);
            SetCellValue(sheet.Cell(rowIndex, 23), history?.ReturnedUnits, AdvancedReportValueKind.Integer, true);
            sheet.Cell(rowIndex, 24).Value = string.IsNullOrWhiteSpace(quality) ? "Exacto" : quality;
            rowIndex++;
        }

        AddExcelTable(sheet, headerRow, width, rowIndex - 1, "TodosProductos");

        if (rowIndex == headerRow + 1)
        {
            sheet.Cell(rowIndex, 1).Value = "Sin productos para los filtros seleccionados.";
            sheet.Range(rowIndex, 1, rowIndex, width).Merge();
        }

        FinishSheet(sheet, width, headerRow, Math.Max(headerRow, rowIndex - 1));
    }

    private static void WriteHistoricalProfitabilitySheet(
        IXLWorksheet sheet,
        ProductProfitabilityReport? historical,
        AdvancedReportResult report,
        string businessName,
        string userName,
        DateTime generatedAt)
    {
        const int width = 14;
        WriteSheetTitle(sheet, businessName, "Rentabilidad histórica", width);
        WriteMetadata(sheet, 4, width, "Filtros", string.Join(" | ", report.AppliedFilters));
        WriteMetadata(sheet, 5, width, "Actividad histórica", FormatHistoricalPeriod(historical?.Period ?? report.Period, generatedAt, historical));
        WriteMetadata(sheet, 6, width, "Cobertura", historical?.CoverageNote ?? "No se pudo cargar el historial; valoración actual disponible.");
        var headers = new[]
        {
            "Código", "Producto", "Categoría", "Subcategoría", "SKU", "Unidades vendidas netas",
            "Ventas netas", "Devoluciones", "Costo histórico cubierto", "Ganancia bruta realizada",
            "Margen histórico", "Cobertura", "Primera venta", "Última venta"
        };
        const int headerRow = 8;
        WriteHeaders(sheet, headerRow, headers);
        var rowIndex = headerRow + 1;
        foreach (var item in historical?.Rows ?? [])
        {
            sheet.Cell(rowIndex, 1).Value = item.ProductCode;
            sheet.Cell(rowIndex, 2).Value = item.ProductName;
            sheet.Cell(rowIndex, 3).Value = item.CategoryName;
            sheet.Cell(rowIndex, 4).Value = item.SubcategoryName ?? "No disponible (histórico)";
            sheet.Cell(rowIndex, 5).Value = item.SkuCodes is { Count: > 0 } ? string.Join(", ", item.SkuCodes) : "Sin SKU (histórico)";
            SetCellValue(sheet.Cell(rowIndex, 6), item.NetUnits, AdvancedReportValueKind.Integer);
            SetCellValue(sheet.Cell(rowIndex, 7), Money(item.NetSalesCentavos), AdvancedReportValueKind.Money);
            SetCellValue(sheet.Cell(rowIndex, 8), item.ReturnedUnits, AdvancedReportValueKind.Integer);
            SetCellValue(sheet.Cell(rowIndex, 9), item.KnownCostCentavos is long known ? Money(known) : null, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 10), item.TotalGainCentavos is long gain ? Money(gain) : null, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(rowIndex, 11), item.MarginPercentage, AdvancedReportValueKind.Percentage, true);
            SetCellValue(sheet.Cell(rowIndex, 12), item.CostCoveragePercentage, AdvancedReportValueKind.Percentage, true);
            SetCellValue(sheet.Cell(rowIndex, 13), item.FirstSaleUtc, AdvancedReportValueKind.DateTime, true);
            SetCellValue(sheet.Cell(rowIndex, 14), item.LastSaleUtc, AdvancedReportValueKind.DateTime, true);
            rowIndex++;
        }
        AddExcelTable(sheet, headerRow, width, rowIndex - 1, "RentabilidadHistorica");
        if (rowIndex == headerRow + 1)
        {
            sheet.Cell(rowIndex, 1).Value = "Sin movimientos históricos en el período seleccionado.";
            sheet.Range(rowIndex, 1, rowIndex, width).Merge();
        }
        FinishSheet(sheet, width, headerRow, Math.Max(headerRow, rowIndex - 1));
    }

    private static void WriteDefinitionsSheet(
        IXLWorksheet sheet,
        AdvancedReportResult report,
        ProductProfitabilityReport? historical,
        FinancialAnalyticsReport? financial,
        string businessName,
        string userName,
        DateTime generatedAt)
    {
        const int width = 3;
        WriteSheetTitle(sheet, businessName, "Definiciones y cobertura", width);
        WriteMetadata(sheet, 4, width, "Generado", generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        WriteMetadata(sheet, 5, width, "Inventario valorado al", generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        WriteMetadata(sheet, 6, width, "Actividad histórica", FormatHistoricalPeriod(historical?.Period ?? report.Period, generatedAt, historical));
        WriteMetadata(sheet, 7, width, "Usuario", userName);
        var definitions = new (string Name, string Definition)[]
        {
            ("Inversión actual", "Stock físico actual × costo efectivo vigente por SKU."),
            ("Valor potencial de venta", "Stock físico actual × precio de venta efectivo vigente; no es pronóstico."),
            ("Venta disponible", "Stock disponible (físico − reservado) × precio efectivo vigente."),
            ("Ganancia bruta potencial", "Valor potencial − inversión actual; no es ganancia realizada ni neta."),
            ("Margen bruto potencial", "Ganancia bruta potencial ÷ valor potencial total de venta × 100; no se promedian márgenes individuales."),
            ("Ganancia histórica", "Ventas históricas netas − costo histórico cubierto, respetando snapshots de la operación."),
            ("Gastos operativos", "Gastos registrados en el período histórico seleccionado."),
            ("Resultado neto histórico", "Ganancia histórica cubierta − gastos operativos; no representa una ganancia futura."),
            ("Cobertura parcial", "Solo parte de las unidades tiene costo histórico confiable; lo demás se marca no calculable."),
            ("Costo no calculable", "No existe un costo válido en el snapshot o en la valoración efectiva; no se inventa."),
            ("Legacy", "Registro histórico sin saldo canónico, SKU o ubicación reconstruible; no se duplica con inventario_saldos."),
            ("Fuente actual", "inventario_saldos es la única fuente canónica de existencia, reservado y disponible.")
        };
        sheet.Cell(9, 1).Value = "Concepto";
        sheet.Cell(9, 2).Value = "Definición";
        sheet.Cell(9, 3).Value = "Estado";
        StyleHeader(sheet.Range(9, 1, 9, width));
        var row = 10;
        foreach (var item in definitions)
        {
            sheet.Cell(row, 1).Value = item.Name;
            sheet.Cell(row, 2).Value = item.Definition;
            sheet.Cell(row, 3).Value = item.Name switch
            {
                "Cobertura parcial" when financial is not null => financial.Finance.CostCoverage.Percentage.ToString("N2") + "%",
                "Costo no calculable" when historical is null => "No verificado",
                _ => "Aplicado"
            };
            row++;
        }
        AddExcelTable(sheet, 9, width, row - 1, "DefinicionesCobertura");
        FinishSheet(sheet, width, 9, row - 1);
    }

    private static void WriteSheetTitle(IXLWorksheet sheet, string businessName, string title, int width)
    {
        sheet.Cell(1, 1).Value = businessName;
        sheet.Range(1, 1, 1, width).Merge();
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.White);
        sheet.Cell(1, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#8C1D40"));
        sheet.Cell(2, 1).Value = title;
        sheet.Range(2, 1, 2, width).Merge();
        sheet.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.FromHtml("#8C1D40"));
    }

    private static void WriteHeaders(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            sheet.Cell(row, index + 1).Value = headers[index];
        }
        StyleHeader(sheet.Range(row, 1, row, headers.Count));
    }

    private static string FirstText(IReadOnlyList<AdvancedReportRow> rows, string key) =>
        rows.Select(row => row[key]?.ToString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";

    private static IReadOnlyList<string> DistinctText(IReadOnlyList<AdvancedReportRow> rows, string key) =>
        rows.Select(row => row[key]?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static bool HasSku(AdvancedReportRow row)
    {
        if (row["skuId"] is not null)
        {
            return true;
        }

        var sku = row["sku"]?.ToString();
        return !string.IsNullOrWhiteSpace(sku) &&
            !sku.StartsWith("Sin SKU", StringComparison.OrdinalIgnoreCase) &&
            !sku.Contains("histórico", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? MarginFromRows(IReadOnlyList<AdvancedReportRow> rows)
    {
        var potential = SumDecimal(rows, "potential");
        var gain = SumDecimal(rows, "gain");
        return potential is decimal potentialValue && potentialValue != 0m && gain is decimal gainValue
            ? decimal.Round(gainValue * 100m / potentialValue, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    private static string CoverageStateText(ProductProfitabilityCoverageState state) => state switch
    {
        ProductProfitabilityCoverageState.Complete => "Costo histórico completo",
        ProductProfitabilityCoverageState.Partial => "Costo histórico parcial",
        ProductProfitabilityCoverageState.NoCoverage => "Costo histórico no calculable",
        _ => "Sin movimiento histórico"
    };

    private static void WriteValuationGroupSheet(
        IXLWorksheet sheet,
        IReadOnlyList<AdvancedReportRow> rows,
        string groupKey,
        string groupTitle,
        AdvancedReportResult report,
        string businessName,
        string userName,
        DateTime generatedAt)
    {
        const int width = 17;
        sheet.Cell(1, 1).Value = businessName;
        sheet.Range(1, 1, 1, width).Merge();
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.White);
        sheet.Cell(1, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#8C1D40"));
        sheet.Cell(2, 1).Value = $"Resumen por {groupTitle.ToLowerInvariant()}";
        sheet.Range(2, 1, 2, width).Merge();
        sheet.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.FromHtml("#8C1D40"));
        WriteMetadata(sheet, 4, width, "Filtros", string.Join(" | ", report.AppliedFilters));
        WriteMetadata(sheet, 5, width, "Generado", generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        WriteMetadata(sheet, 6, width, "Usuario", userName);

        var headerRow = 8;
        var headers = new[]
        {
            groupTitle, "Productos", "SKU", "Filas", "Stock físico", "Reservado", "Disponible",
            "En exhibición", "En bodega", "Sin asignar", "Inversión actual",
            "Venta potencial", "Venta disponible", "Ganancia bruta potencial",
            "Margen bruto potencial", "Calidad/cobertura", "Reconciliación"
        };
        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(headerRow, index + 1).Value = headers[index];
        }
        StyleHeader(sheet.Range(headerRow, 1, headerRow, width));

        var row = headerRow + 1;
        foreach (var group in rows
            .GroupBy(item => groupKey == "subcategory"
                ? $"{Convert.ToString(item["category"], CultureInfo.InvariantCulture) ?? "Sin categoría"} · {Convert.ToString(item[groupKey], CultureInfo.InvariantCulture) ?? "Sin subcategoría"}"
                : Convert.ToString(item[groupKey], CultureInfo.InvariantCulture) ?? "Sin dato")
            .OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var list = group.ToList();
            var products = list
                .Select(item => item["productId"])
                .Where(value => value is not null)
                .Select(ToInt)
                .Distinct()
                .Count();
            var skus = list.Count(HasSku);
            var physical = SumDecimal(list, "stock");
            var reserved = SumDecimal(list, "reserved");
            var available = SumDecimal(list, "available");
            var exhibition = SumDecimal(list, "exhibition");
            var warehouse = SumDecimal(list, "warehouse");
            var unassigned = SumDecimal(list, "unassigned");
            var investment = SumDecimal(list, "investment");
            var potential = SumDecimal(list, "potential");
            var availableValue = SumDecimal(list, "availableValue");
            var gain = SumDecimal(list, "gain");
            decimal? margin = potential is not null and not 0 && gain is not null
                ? decimal.Round(gain.Value * 100m / potential.Value, 2, MidpointRounding.AwayFromZero)
                : null;
            var quality = string.Join(" · ", list
                .Select(item => item["quality"]?.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase));
            var reconciliation = list.Any(item => item["locationQuality"]?.ToString() == "Legacy sin ubicación")
                ? "Legacy sin ubicación; disponible no reconstruible"
                : "Suma por SKU, sin doble conteo";

            sheet.Cell(row, 1).Value = group.Key;
            SetCellValue(sheet.Cell(row, 2), products, AdvancedReportValueKind.Integer);
            SetCellValue(sheet.Cell(row, 3), skus, AdvancedReportValueKind.Integer);
            SetCellValue(sheet.Cell(row, 4), list.Count, AdvancedReportValueKind.Integer);
            SetCellValue(sheet.Cell(row, 5), physical, AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(row, 6), reserved, AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(row, 7), available, AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(row, 8), exhibition, AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(row, 9), warehouse, AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(row, 10), unassigned, AdvancedReportValueKind.Integer, true);
            SetCellValue(sheet.Cell(row, 11), investment, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(row, 12), potential, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(row, 13), availableValue, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(row, 14), gain, AdvancedReportValueKind.Money, true);
            SetCellValue(sheet.Cell(row, 15), margin, AdvancedReportValueKind.Percentage, true);
            sheet.Cell(row, 16).Value = string.IsNullOrWhiteSpace(quality) ? "Exacto" : quality;
            sheet.Cell(row, 17).Value = reconciliation;
            row++;
        }

        AddExcelTable(sheet, headerRow, width, row - 1, groupKey == "subcategory"
            ? "ResumenSubcategorias"
            : "ResumenCategorias");

        if (row == headerRow + 1)
        {
            sheet.Cell(row, 1).Value = "Sin registros para los filtros seleccionados.";
            sheet.Range(row, 1, row, width).Merge();
        }

        FinishSheet(sheet, width, headerRow, Math.Max(headerRow, row - 1));
    }

    private static decimal? SumDecimal(
        IReadOnlyList<AdvancedReportRow> rows,
        string key)
    {
        var values = rows.Select(item => item[key]).ToList();
        return values.Any(value => value is null)
            ? null
            : checked(values.Sum(ToDecimal));
    }

    private static void StyleHeader(IXLRange header)
    {
        header.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
        header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#A62A57"));
        header.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
    }

    private static void FinishSheet(IXLWorksheet sheet, int width, int headerRow, int lastRow)
    {
        sheet.Range(headerRow, 1, lastRow, width).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        sheet.Range(headerRow, 1, lastRow, width).Style.Border.SetInsideBorder(XLBorderStyleValues.Hair);
        if (!sheet.Tables.Any())
        {
            sheet.Range(headerRow, 1, lastRow, width).SetAutoFilter();
        }
        sheet.SheetView.FreezeRows(headerRow);
        ApplyReadableLayout(sheet, width, headerRow, lastRow);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        sheet.PageSetup.Footer.Center.AddText("Página &P de &N");
    }

    private static void ApplyReadableLayout(
        IXLWorksheet sheet,
        int width,
        int headerRow,
        int lastRow)
    {
        // ClosedXML font measurement is expensive on large ranges. Measure a
        // representative sample for data sheets; the definitions sheet is
        // small and is measured completely so wrapped text remains legible.
        var sampleEnd = sheet.Name == "Definiciones y cobertura"
            ? lastRow
            : Math.Min(lastRow, headerRow + 80);
        sheet.Columns(1, width).AdjustToContents(headerRow, sampleEnd, 8, 55);
        foreach (var column in sheet.Columns(1, width))
        {
            column.Width = Math.Clamp(column.Width + 1, 10, 55);
        }

        if (sheet.Name == "Definiciones y cobertura")
        {
            sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 24);
            sheet.Column(2).Width = Math.Max(sheet.Column(2).Width, 48);
            sheet.Column(3).Width = Math.Max(sheet.Column(3).Width, 22);
            sheet.Range(headerRow, 2, lastRow, 2).Style.Alignment.WrapText = true;
            sheet.Range(headerRow, 3, lastRow, 3).Style.Alignment.WrapText = true;
            sheet.Rows(headerRow, lastRow).AdjustToContents();
            for (var row = headerRow; row <= lastRow; row++)
            {
                sheet.Row(row).Height = Math.Max(24, sheet.Row(row).Height);
            }
        }
        else
        {
            // Non-wrapped data rows use the default height. Only metadata and
            // headers need a row-height measurement.
            sheet.Rows(1, Math.Min(lastRow, headerRow)).AdjustToContents();
        }
    }

    private static void AddExcelTable(
        IXLWorksheet sheet,
        int headerRow,
        int width,
        int lastDataRow,
        string baseName)
    {
        if (lastDataRow <= headerRow)
        {
            return;
        }

        sheet.Range(headerRow, 1, lastDataRow, width)
            .CreateTable(SanitizeTableName(baseName));
    }

    private static void cancellationSafeWrite(
        IReadOnlyList<AdvancedReportColumn> columns,
        AdvancedReportRow dataRow,
        IXLWorksheet sheet,
        int row,
        bool preserveProductProfitabilityStates)
    {
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            SetCellValue(
                sheet.Cell(row, columnIndex + 1),
                dataRow[column.Key],
                column.ValueKind,
                preserveProductProfitabilityStates);
        }
    }

    private static void WriteTotals(
        IReadOnlyList<AdvancedReportColumn> columns,
        IReadOnlyList<AdvancedReportRow> rows,
        IXLWorksheet sheet,
        int row,
        bool preserveProductProfitabilityStates)
    {
        sheet.Cell(row, 1).Value = "Totales";
        sheet.Cell(row, 1).Style.Font.SetBold();
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (!column.IncludeInTotal)
            {
                continue;
            }
            var values = rows.Select(item => item[column.Key]).ToList();
            if (preserveProductProfitabilityStates && values.Any(value => value is null))
            {
                SetCellValue(
                    sheet.Cell(row, index + 1),
                    null,
                    column.ValueKind,
                    preserveProductProfitabilityStates);
                continue;
            }
            var total = values.Sum(ToDecimal);
            SetCellValue(
                sheet.Cell(row, index + 1),
                total,
                column.ValueKind,
                preserveProductProfitabilityStates);
            sheet.Cell(row, index + 1).Style.Font.SetBold();
        }
        sheet.Range(row, 1, row, Math.Max(1, columns.Count)).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F6E5EC"));
    }

    private static void SetCellValue(
        IXLCell cell,
        object? value,
        AdvancedReportValueKind kind,
        bool preserveProductProfitabilityStates = false)
    {
        if (value is null)
        {
            if (preserveProductProfitabilityStates &&
                kind is AdvancedReportValueKind.Money or
                    AdvancedReportValueKind.Percentage or
                    AdvancedReportValueKind.Integer)
            {
                cell.Value = "No calculable";
            }
            else
            {
                cell.Clear();
            }
            return;
        }
        switch (kind)
        {
            case AdvancedReportValueKind.Money:
                cell.Value = ToDecimal(value);
                cell.Style.NumberFormat.Format = CurrencyFormat;
                break;
            case AdvancedReportValueKind.Percentage:
                cell.Value = ToDecimal(value) / 100m;
                cell.Style.NumberFormat.Format = PercentageFormat;
                break;
            case AdvancedReportValueKind.Integer:
                cell.Value = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                cell.Style.NumberFormat.Format = "#,##0";
                break;
            case AdvancedReportValueKind.Date:
                cell.Value = Convert.ToDateTime(value, CultureInfo.InvariantCulture);
                cell.Style.DateFormat.Format = "dd/MM/yyyy";
                break;
            case AdvancedReportValueKind.DateTime:
                cell.Value = Convert.ToDateTime(value, CultureInfo.InvariantCulture);
                cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                break;
            case AdvancedReportValueKind.Boolean:
                cell.Value = Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "Sí" : "No";
                break;
            default:
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    private static decimal ToDecimal(object? value) => value switch
    {
        decimal number => number,
        int number => number,
        long number => number,
        double number => (decimal)number,
        float number => (decimal)number,
        _ => decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0
    };

    private static decimal Money(long centavos) => centavos / 100m;

    private static decimal? Money(long? centavos) => centavos.HasValue ? centavos.Value / 100m : null;

    private static void WriteMetadata(IXLWorksheet sheet, int row, int width, string label, string value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.SetBold();
        if (width > 1)
        {
            sheet.Cell(row, 2).Value = value;
            sheet.Range(row, 2, row, width).Merge();
        }
    }

    private async Task<UsuarioSesion> EnsureActiveAdminAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var user = _session.UsuarioActual;
        if (user is null || user.Rol != RolUsuario.ADMIN)
            throw new UnauthorizedAccessException("Los reportes avanzados son exclusivos de ADMIN.");
        if (!await context.Usuarios.AsNoTracking().AnyAsync(
                item => item.UsuarioId == user.UsuarioId && item.Activo && item.Rol == RolUsuario.ADMIN,
                cancellationToken))
            throw new UnauthorizedAccessException("La sesión ADMIN no está activa.");
        return user;
    }

    private async Task<UsuarioSesion> EnsureActiveUserAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var user = _session.UsuarioActual;
        if (user is null || !await context.Usuarios.AsNoTracking().AnyAsync(
                item => item.UsuarioId == user.UsuarioId && item.Activo,
                cancellationToken))
        {
            throw new UnauthorizedAccessException("La sesión actual no está activa.");
        }

        return user;
    }

    private static async Task<string> GetBusinessNameAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var name = await context.ConfiguracionSistema.AsNoTracking()
            .Where(item => item.Clave == SystemConfigService.BusinessNameKey)
            .Select(item => item.Valor)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(name) ? "ArteInventariado Demo" : name.Trim();
    }

    internal static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.Normalize().Select(character =>
            invalid.Contains(character) || character is '/' or '\\' ? '_' : character).ToArray();
        var safe = new string(characters).Trim().Trim('.');
        while (safe.Contains("..", StringComparison.Ordinal)) safe = safe.Replace("..", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(safe) ? "Reporte" : safe;
    }

    private static string SanitizeWorksheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = "Reporte";
        return safe.Length <= 31 ? safe : safe[..31];
    }

    private static string SanitizeTableName(string value)
    {
        var safe = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character == '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "Tabla";
        }
        if (!char.IsLetter(safe[0]) && safe[0] != '_')
        {
            safe = "Tabla_" + safe;
        }
        return safe.Length <= 200 ? safe : safe[..200];
    }

    private static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; ; suffix++)
        {
            path = Path.Combine(directory, $"{name}_{suffix}{extension}");
            if (!File.Exists(path)) return path;
        }
    }
}
