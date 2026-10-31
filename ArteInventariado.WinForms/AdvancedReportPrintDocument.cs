using System.Drawing.Printing;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class AdvancedReportPrintDocument : PrintDocument
{
    private const int HeaderHeight = 30;
    private const int FooterReserve = 28;
    private const int MetadataHeight = 108;
    private readonly AdvancedReportResult _report;
    private readonly string _userName;
    private readonly DateTime _generatedAt;
    private AdvancedReportPrintPlan? _plan;
    private AdvancedReportPrintCursor? _cursor;

    public AdvancedReportPrintDocument(
        AdvancedReportResult report,
        string userName,
        DateTime generatedAt)
    {
        _report = report;
        _userName = userName;
        _generatedAt = generatedAt;
        DocumentName = $"Reporte - {report.Definition.Name}";
        DefaultPageSettings.Landscape = true;
        DefaultPageSettings.Margins = new Margins(35, 35, 40, 40);
    }

    internal int TotalPages => _plan?.TotalPages ?? 0;

    internal void Prepare(
        CancellationToken cancellationToken = default,
        IProgress<(int Page, int? TotalPages)>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var graphics = PrinterSettings.CreateMeasurementGraphics(DefaultPageSettings);
        using var cellFont = CreateCellFont();
        var bounds = MarginBounds(DefaultPageSettings);
        var widths = ColumnWidths(bounds.Width);
        var availableHeight = bounds.Height - MetadataHeight - HeaderHeight - FooterReserve;
        _plan = AdvancedReportPrintPagination.Create(
            _report.Rows.Count,
            availableHeight,
            rowIndex => MeasureRowHeight(graphics, _report.Rows[rowIndex], widths, cellFont),
            cancellationToken,
            new InlineProgress<int>(page => progress?.Report((page, null))));
        _cursor = new AdvancedReportPrintCursor(_plan);
        progress?.Report((_plan.TotalPages, _plan.TotalPages));
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        if (_plan is null || _cursor is null)
        {
            throw new InvalidOperationException("La impresión debe prepararse antes de abrir la vista previa.");
        }

        _cursor.Begin();
        base.OnBeginPrint(e);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        if (_plan is null || _cursor is null || !_cursor.TryTakeNext(out var page) || page is null)
        {
            e.HasMorePages = false;
            return;
        }

        var graphics = e.Graphics ?? throw new InvalidOperationException("No existe superficie de impresión.");
        var bounds = e.MarginBounds;
        using var titleFont = new Font("Segoe UI", 14f, FontStyle.Bold);
        using var reportFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var metadataFont = new Font("Segoe UI", 7.5f);
        using var headerFont = new Font("Segoe UI", _report.Columns.Count > 10 ? 6.2f : 7.2f, FontStyle.Bold);
        using var cellFont = CreateCellFont();
        using var primary = new SolidBrush(UITheme.SecondaryColor);
        using var text = new SolidBrush(Color.Black);
        using var muted = new SolidBrush(Color.DimGray);
        using var headerBackground = new SolidBrush(UITheme.SecondaryColor);
        using var headerText = new SolidBrush(Color.White);
        using var alternate = new SolidBrush(Color.FromArgb(252, 243, 247));
        using var border = new Pen(Color.FromArgb(210, 196, 203));

        var y = DrawMetadata(graphics, bounds, titleFont, reportFont, metadataFont, primary, text, muted);
        var widths = ColumnWidths(bounds.Width);
        var x = (float)bounds.Left;
        for (var index = 0; index < _report.Columns.Count; index++)
        {
            var rectangle = new RectangleF(x, y, widths[index], HeaderHeight);
            graphics.FillRectangle(headerBackground, rectangle);
            DrawCell(graphics, _report.Columns[index].Title, headerFont, headerText, rectangle, center: true);
            graphics.DrawRectangle(border, Rectangle.Round(rectangle));
            x += widths[index];
        }
        y += HeaderHeight;

        if (page.RowCount == 0)
        {
            using var centered = Centered();
            graphics.DrawString("Sin datos para los filtros seleccionados.", reportFont, muted,
                new RectangleF(bounds.Left, y + 18, bounds.Width, 60), centered);
        }
        else
        {
            for (var offset = 0; offset < page.RowCount; offset++)
            {
                var rowIndex = page.StartRowIndex + offset;
                var row = _report.Rows[rowIndex];
                var rowHeight = page.RowHeights[offset];
                x = bounds.Left;
                if (rowIndex % 2 == 1)
                {
                    graphics.FillRectangle(alternate, new RectangleF(bounds.Left, y, bounds.Width, rowHeight));
                }
                for (var index = 0; index < _report.Columns.Count; index++)
                {
                    var column = _report.Columns[index];
                    var rectangle = new RectangleF(x, y, widths[index], rowHeight);
                    DrawCell(graphics, Format(row[column.Key], column.ValueKind), cellFont, text, rectangle,
                        center: column.ValueKind is AdvancedReportValueKind.Integer or AdvancedReportValueKind.Boolean);
                    graphics.DrawRectangle(border, Rectangle.Round(rectangle));
                    x += widths[index];
                }
                y += rowHeight;
            }
        }

        DrawFooter(graphics, bounds, metadataFont, muted, page.Number, _plan.TotalPages);
        e.HasMorePages = _cursor.HasMorePages;
        base.OnPrintPage(e);
    }

    private float DrawMetadata(
        Graphics graphics,
        Rectangle bounds,
        Font titleFont,
        Font reportFont,
        Font metadataFont,
        Brush primary,
        Brush text,
        Brush muted)
    {
        var y = (float)bounds.Top;
        graphics.DrawString(UITheme.BusinessName, titleFont, primary, bounds.Left, y);
        y += 25;
        graphics.DrawString(_report.Definition.Name, reportFont, text, bounds.Left, y);
        y += 21;
        graphics.DrawString($"Período: {_report.Period.DisplayName}", metadataFont, text, bounds.Left, y);
        y += 14;
        var filters = string.Join(" | ", _report.AppliedFilters);
        graphics.DrawString($"Filtros: {filters}", metadataFont, text,
            new RectangleF(bounds.Left, y, bounds.Width, 28), StringFormat.GenericTypographic);
        y += 28;
        graphics.DrawString($"Generado: {_generatedAt:dd/MM/yyyy HH:mm} · Usuario: {_userName}",
            metadataFont, muted, bounds.Left, y);
        return y + 20;
    }

    private float[] ColumnWidths(float totalWidth)
    {
        var weights = _report.Columns.Select(column => column.ValueKind switch
        {
            AdvancedReportValueKind.Text => 1.5f,
            AdvancedReportValueKind.DateTime => 1.25f,
            AdvancedReportValueKind.Date => 1.15f,
            _ => 1f
        }).ToArray();
        var totalWeight = weights.Sum();
        return weights.Select(weight => totalWidth * weight / totalWeight).ToArray();
    }

    private int MeasureRowHeight(Graphics graphics, AdvancedReportRow row, float[] widths, Font font)
    {
        var height = 22f;
        for (var index = 0; index < _report.Columns.Count; index++)
        {
            var column = _report.Columns[index];
            var measured = graphics.MeasureString(
                Format(row[column.Key], column.ValueKind),
                font,
                Math.Max(10, (int)widths[index] - 6),
                StringFormat.GenericTypographic);
            height = Math.Max(height, measured.Height + 7);
        }
        return (int)Math.Ceiling(height);
    }

    private static Rectangle MarginBounds(PageSettings settings)
    {
        var bounds = settings.Bounds;
        var margins = settings.Margins;
        return new Rectangle(
            bounds.Left + margins.Left,
            bounds.Top + margins.Top,
            bounds.Width - margins.Left - margins.Right,
            bounds.Height - margins.Top - margins.Bottom);
    }

    private Font CreateCellFont() =>
        new("Segoe UI", _report.Columns.Count > 10 ? 6.2f : 7.2f);

    private static void DrawFooter(
        Graphics graphics,
        Rectangle bounds,
        Font font,
        Brush brush,
        int pageNumber,
        int totalPages)
    {
        using var format = RightAligned();
        graphics.DrawString($"Página {pageNumber:N0} de {totalPages:N0}", font, brush,
            new RectangleF(bounds.Left, bounds.Bottom - 18, bounds.Width, 18), format);
    }

    private static void DrawCell(
        Graphics graphics,
        string value,
        Font font,
        Brush brush,
        RectangleF rectangle,
        bool center)
    {
        using var format = new StringFormat
        {
            Alignment = center ? StringAlignment.Center : StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit
        };
        rectangle.Inflate(-3, -2);
        graphics.DrawString(value, font, brush, rectangle, format);
    }

    private string Format(object? value, AdvancedReportValueKind kind)
    {
        if (value is null)
        {
            return _report.Definition.Kind == AdvancedReportKind.ProductProfitability &&
                kind is AdvancedReportValueKind.Money or AdvancedReportValueKind.Percentage
                ? "No calculable"
                : string.Empty;
        }
        return kind switch
        {
            AdvancedReportValueKind.Money => $"${Convert.ToDecimal(value):N2}",
            AdvancedReportValueKind.Percentage => $"{Convert.ToDecimal(value):N2}%",
            AdvancedReportValueKind.Integer => $"{Convert.ToInt64(value):N0}",
            AdvancedReportValueKind.Date => Convert.ToDateTime(value).ToString("dd/MM/yyyy"),
            AdvancedReportValueKind.DateTime => Convert.ToDateTime(value).ToString("dd/MM/yyyy HH:mm"),
            AdvancedReportValueKind.Boolean => Convert.ToBoolean(value) ? "Sí" : "No",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static StringFormat Centered() => new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center
    };

    private static StringFormat RightAligned() => new()
    {
        Alignment = StringAlignment.Far,
        LineAlignment = StringAlignment.Center
    };

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
