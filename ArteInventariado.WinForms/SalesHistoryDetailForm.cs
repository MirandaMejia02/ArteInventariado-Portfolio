using System.Drawing;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

internal sealed class SalesHistoryDetailForm : UserControl
{
    private readonly SalesHistorySummary _summary;
    private readonly SalesHistoryDetail _detail;
    private readonly IProductAssetService? _assets;
    private readonly BindingSource _linesBinding = new();
    private readonly BindingSource _paymentsBinding = new();
    private readonly DataGridView _linesGrid = new();
    private readonly DataGridView _paymentsGrid = new();
    private readonly PictureBox _picture = new();
    private readonly Label _imageStatus = new();
    private int _imageRequestVersion;

    public event EventHandler? CloseRequested;

    public SalesHistoryDetailForm(
        SalesHistorySummary summary,
        SalesHistoryDetail detail,
        IProductAssetService? assets)
    {
        _summary = summary;
        _detail = detail;
        _assets = assets;
        BuildUi();
        _linesGrid.SelectionChanged += async (_, _) => await LoadSelectedProductImageAsync();
        Load += async (_, _) => await LoadSelectedProductImageAsync();
        Disposed += (_, _) =>
        {
            Interlocked.Increment(ref _imageRequestVersion);
            _picture.Image?.Dispose();
            _picture.Image = null;
        };
    }

    private void BuildUi()
    {
        BackColor = UITheme.BackgroundColor;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildTotals(), 0, 1);
        root.Controls.Add(BuildLinesAndImage(), 0, 2);
        root.Controls.Add(BuildPaymentsAndCredit(), 0, 3);
        root.Controls.Add(BuildFooter(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10, 0, 10, 0),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = $"Venta {_detail.NumeroVenta} · {_summary.Estado}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        title.Font = UITheme.HeaderFont;
        var date = _detail.FechaVenta.Kind switch
        {
            DateTimeKind.Local => _detail.FechaVenta,
            DateTimeKind.Utc => _detail.FechaVenta.ToLocalTime(),
            _ => DateTime.SpecifyKind(_detail.FechaVenta, DateTimeKind.Utc).ToLocalTime()
        };
        var subtitle = new Label
        {
            Text = $"{date:dd/MM/yyyy HH:mm} · Usuario: {_detail.Usuario} · Cliente: {_detail.Cliente} · Caja: #{_summary.CajaId}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.SubheaderFont,
            AutoEllipsis = true
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildTotals()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 4)
        };
        for (var index = 0; index < 5; index++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        panel.Controls.Add(BuildMetric("Subtotal", Money(_detail.SubtotalCentavos)), 0, 0);
        panel.Controls.Add(BuildMetric("Descuento", Money(_detail.DescuentoCentavos)), 1, 0);
        panel.Controls.Add(BuildMetric("Total", Money(_detail.TotalCentavos)), 2, 0);
        panel.Controls.Add(BuildMetric("Pagado registrado", Money(_detail.TotalPagadoCentavos)), 3, 0);
        panel.Controls.Add(BuildMetric("Cambio / vuelto", Money(_detail.CambioCentavos)), 4, 0);
        return panel;
    }

    private Control BuildLinesAndImage()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 4)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));

        ConfigureGrid(_linesGrid);
        _linesGrid.DataSource = _linesBinding;
        AddColumn(_linesGrid, nameof(SaleLineRow.Producto), "Producto", 240);
        AddColumn(_linesGrid, nameof(SaleLineRow.Codigo), "Código / SKU", 120);
        AddColumn(_linesGrid, nameof(SaleLineRow.Cantidad), "Cant.", 64);
        AddColumn(_linesGrid, nameof(SaleLineRow.Precio), "Precio unitario", 105);
        AddColumn(_linesGrid, nameof(SaleLineRow.Descuento), "Descuento", 96);
        AddColumn(_linesGrid, nameof(SaleLineRow.Subtotal), "Subtotal", 96);
        AddColumn(_linesGrid, nameof(SaleLineRow.Total), "Total", 96);
        _linesBinding.DataSource = _detail.Lines.Select(SaleLineRow.From).ToList();
        if (_linesGrid.Rows.Count > 0) _linesGrid.Rows[0].Selected = true;

        var linePanel = BuildSection("Productos vendidos", _linesGrid);
        split.Controls.Add(linePanel, 0, 0);

        var imagePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(8, 0, 0, 0)
        };
        imagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        imagePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        imagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        imagePanel.Controls.Add(new Label
        {
            Text = "Imagen de referencia actual",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.SecondaryColor
        }, 0, 0);
        _picture.Dock = DockStyle.Fill;
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        _picture.BackColor = UITheme.SurfaceMutedColor;
        imagePanel.Controls.Add(_picture, 0, 1);
        _imageStatus.Dock = DockStyle.Fill;
        _imageStatus.Text = "Selecciona un producto.";
        _imageStatus.TextAlign = ContentAlignment.MiddleCenter;
        _imageStatus.ForeColor = UITheme.MutedTextColor;
        _imageStatus.Font = UITheme.CaptionFont;
        imagePanel.Controls.Add(_imageStatus, 0, 2);
        split.Controls.Add(imagePanel, 1, 0);
        return split;
    }

    private Control BuildPaymentsAndCredit()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        ConfigureGrid(_paymentsGrid);
        _paymentsGrid.DataSource = _paymentsBinding;
        AddColumn(_paymentsGrid, nameof(SalePaymentRow.Metodo), "Método", 115);
        AddColumn(_paymentsGrid, nameof(SalePaymentRow.Monto), "Monto recibido", 110);
        AddColumn(_paymentsGrid, nameof(SalePaymentRow.Referencia), "Referencia", 220);
        _paymentsBinding.DataSource = _detail.Payments.Select(SalePaymentRow.From).ToList();
        layout.Controls.Add(BuildSection("Pagos registrados", _paymentsGrid), 0, 0);

        var creditText = _detail.Credit is null
            ? "Esta venta no tiene una cuenta de crédito asociada."
            : $"Cuenta: {_detail.Credit.CodigoCuenta}\r\n" +
              $"Total original: {Money(_detail.Credit.TotalCuentaCentavos)}\r\n" +
              $"Saldo actual: {Money(_detail.Credit.SaldoActualCentavos)}\r\n" +
              $"Vencimiento: {_detail.Credit.FechaVencimiento:dd/MM/yyyy}";
        var credit = new Label
        {
            Text = creditText,
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = UITheme.SurfaceColor,
            ForeColor = UITheme.TextDark,
            Font = UITheme.NormalFont,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(BuildSection("Crédito relacionado", credit), 1, 0);
        return layout;
    }

    private Control BuildFooter()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.Controls.Add(new Label
        {
            Text = "La imagen es la referencia actual del producto; no representa necesariamente la foto que existía al vender.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.CaptionFont
        }, 0, 0);
        var close = new Button { Text = "Cerrar pestaña", Dock = DockStyle.Fill, Margin = Padding.Empty };
        UITheme.EnsureButtonContentFits(close, 150, 40, 36);
        close.Size = close.MinimumSize;
        UITheme.StyleSecondaryButton(close);
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        panel.Controls.Add(close, 1, 0);
        return panel;
    }

    private static Control BuildMetric(string title, string value)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(3, 0, 3, 0),
            Padding = new Padding(10, 1, 10, 1)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.CaptionFont,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            ForeColor = UITheme.TextDark,
            Font = UITheme.NormalBoldFont,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            MinimumSize = new Size(0, 26)
        }, 0, 1);
        return panel;
    }

    private static Control BuildSection(string title, Control content)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(8),
            Margin = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.SecondaryColor
        }, 0, 0);
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.ScrollBars = ScrollBars.Both;
        UITheme.StyleDataGridView(grid);
    }

    private static void AddColumn(DataGridView grid, string property, string title, int width)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = title,
            Width = width,
            MinimumWidth = Math.Min(width, 62),
            FillWeight = width,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private async Task LoadSelectedProductImageAsync()
    {
        var requestVersion = Interlocked.Increment(ref _imageRequestVersion);
        if (_linesGrid.CurrentRow?.DataBoundItem is not SaleLineRow row)
        {
            SetImage(null, "Selecciona un producto.");
            return;
        }

        _imageStatus.Text = "Cargando referencia…";
        try
        {
            var productId = row.Source.ProductoIdReferencia;
            if (_assets is null || !productId.HasValue)
            {
                SetImage(null, "No hay una imagen de referencia disponible para esta línea histórica.");
                return;
            }

            var assets = await _assets.GetByProductAsync(productId.Value);
            if (requestVersion != Volatile.Read(ref _imageRequestVersion)) return;

            ProductAssetSummary? selected = null;
            if (row.Source.ProductoSkuIdReferencia is int skuId)
            {
                selected = assets
                    .Where(asset => asset.OwnerKind == ProductAssetOwnerKind.Sku && asset.ProductoSkuId == skuId)
                    .OrderByDescending(asset => asset.IsPrimary)
                    .ThenBy(asset => asset.Order)
                    .FirstOrDefault();
            }

            selected ??= assets
                .Where(asset => asset.OwnerKind == ProductAssetOwnerKind.Product)
                .OrderByDescending(asset => asset.IsPrimary)
                .ThenBy(asset => asset.Order)
                .FirstOrDefault();

            if (selected is null)
            {
                SetImage(null, "El producto ya no tiene una imagen actual administrada.");
                return;
            }

            var path = await _assets.GetThumbnailPathAsync(selected.OwnerKind, selected.AssetId, 520);
            if (requestVersion != Volatile.Read(ref _imageRequestVersion)) return;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetImage(null, "No se pudo cargar la miniatura actual del producto.");
                return;
            }

            var image = LoadImageCopy(path);
            if (requestVersion != Volatile.Read(ref _imageRequestVersion))
            {
                image.Dispose();
                return;
            }

            SetImage(image, $"{selected.OriginalFileName}\r\nImagen actual, solo referencia.");
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("sales-history-current-product-image", ex);
            if (requestVersion == Volatile.Read(ref _imageRequestVersion))
                SetImage(null, "La imagen de referencia no está disponible.");
        }
    }

    private void SetImage(Image? image, string status)
    {
        var previous = _picture.Image;
        _picture.Image = image;
        previous?.Dispose();
        _imageStatus.Text = status;
    }

    private static Image LoadImageCopy(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static string Money(long centavos) => $"${centavos / 100m:0.00}";

    private sealed record SaleLineRow(
        string Producto,
        string Codigo,
        int Cantidad,
        string Precio,
        string Descuento,
        string Subtotal,
        string Total,
        SalesHistoryLine Source)
    {
        public static SaleLineRow From(SalesHistoryLine line) => new(
            line.Producto,
            line.Codigo,
            line.Cantidad,
            Money(line.PrecioUnitarioCentavos),
            Money(line.DescuentoCentavos),
            Money(line.SubtotalCentavos),
            Money(line.TotalCentavos),
            line);
    }

    private sealed record SalePaymentRow(string Metodo, string Monto, string Referencia)
    {
        public static SalePaymentRow From(SalesHistoryPayment payment) => new(
            payment.MetodoPago.ToString(),
            Money(payment.MontoCentavos),
            payment.Referencia ?? string.Empty);
    }
}
