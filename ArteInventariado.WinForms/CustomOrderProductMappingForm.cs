using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CustomOrderProductMappingForm : Form
{
    private readonly ICustomOrderFinanceService _finance;
    private readonly IPosCatalogService _products;
    private readonly DataGridView _grid = new();
    private readonly ComboBox _product = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        FormattingEnabled = true
    };
    private readonly ToolTip _productToolTip = new();
    private readonly List<CustomOrderConversionLine> _lines;

    public CustomOrderProductMappingForm(
        IReadOnlyList<CustomOrderConversionLine> lines,
        ICustomOrderFinanceService finance,
        IPosCatalogService products)
    {
        _lines = lines.Where(l => !l.ProductoId.HasValue).ToList();
        _finance = finance;
        _products = products;
        Text = "Vincular productos finales";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 560);
        UITheme.StyleForm(this);
        BuildUi();
        Load += async (_, _) => await LoadProductsAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            RowCount = 4,
            ColumnCount = 1,
            AutoSize = false
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var title = new Label
        {
            Text = "Vincular lineas descriptivas a productos existentes\r\nEl snapshot original del pedido se conservara.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = false,
            Padding = new Padding(0, 0, 0, 4)
        };
        UITheme.StyleLabelTitle(title); root.Controls.Add(title);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        UITheme.StyleDataGridView(_grid);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Description", HeaderText = "Descripcion personalizada", FillWeight = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Quantity", HeaderText = "Cantidad", FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Price", HeaderText = "Precio", FillWeight = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Status", HeaderText = "Estado", FillWeight = 80 });
        _grid.DataSource = _lines.Select(l => new Row(l.DetallePedidoPersonalizadoId, l.Descripcion, l.Cantidad, Money(l.PrecioUnitarioCentavos), "Pendiente")).ToList();
        root.Controls.Add(_grid);
        var picker = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 8, 0, 8),
            Margin = new Padding(0, 0, 0, 4),
            MinimumSize = new Size(0, 58)
        };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        var productLabel = new Label
        {
            Text = "Producto final asignado",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 10, 0)
        };
        _product.Dock = DockStyle.Fill;
        _product.MinimumSize = new Size(120, 36);
        _product.Margin = new Padding(0, 0, 8, 0);
        picker.Controls.Add(productLabel, 0, 0);
        picker.Controls.Add(_product, 1, 0);
        root.Controls.Add(picker);
        var buttons = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty
        };
        var close = new Button { Text = "Cerrar", Dock = DockStyle.None, DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        var map = new Button { Text = "Vincular seleccionado", Dock = DockStyle.None, Margin = new Padding(8, 0, 0, 0) };
        UITheme.StyleSecondaryButton(close); UITheme.StyleSuccessButton(map);
        UITheme.EnsureButtonContentFits(close, 110, 42, 28);
        UITheme.EnsureButtonContentFits(map, 190, 42, 28);
        map.Click += async (_, _) => await MapAsync();
        buttons.Controls.Add(close);
        buttons.Controls.Add(map);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        map.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        buttons.Resize += (_, _) => LayoutActionButtons(buttons, close, map);
        LayoutActionButtons(buttons, close, map);
        root.Controls.Add(buttons);
        Controls.Add(root);
        root.Resize += (_, _) => LayoutRows(root, title, picker, buttons);
        LayoutRows(root, title, picker, buttons);
    }

    private void LayoutRows(TableLayoutPanel root, Label title, TableLayoutPanel picker, Panel buttons)
    {
        var availableWidth = Math.Max(120, root.ClientSize.Width - root.Padding.Horizontal);
        var titleHeight = TextRenderer.MeasureText(
            title.Text,
            title.Font,
            new Size(availableWidth, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 8;
        root.RowStyles[0].Height = Math.Max(70, titleHeight);
        root.RowStyles[2].Height = Math.Max(62, _product.PreferredSize.Height + picker.Padding.Vertical + 4);
        var actionHeight = buttons.Controls
            .OfType<Button>()
            .Select(button => button.Height)
            .DefaultIfEmpty(42)
            .Max();
        root.RowStyles[3].Height = Math.Max(52, actionHeight + buttons.Padding.Vertical + 8);
    }

    private async Task LoadProductsAsync()
    {
        var products = await _products.SearchAsync(new PosSkuCatalogFilter(
            Estado: EstadoProducto.ACTIVO, SkuActivo: true));
        var productList = products.ToList();
        _product.DataSource = productList;
        _product.Format += (_, e) =>
        {
            if (e.ListItem is PosSkuSaleItem sku)
            {
                var variant = string.IsNullOrWhiteSpace(sku.VarianteNombre)
                    ? string.Empty
                    : $" / {sku.VarianteNombre}";
                var subcategory = string.IsNullOrWhiteSpace(sku.SubcategoriaNombre)
                    ? "Sin subcategoría"
                    : sku.SubcategoriaNombre;
                e.Value = $"{sku.CodigoSku} - {sku.ProductoNombre}{variant} · " +
                    $"{sku.CategoriaNombre} · {subcategory}";
            }
        };
        _product.SelectedIndexChanged += (_, _) =>
        {
            _productToolTip.SetToolTip(_product, _product.Text);
        };
        var longestTextWidth = productList
            .Select(FormatProductText)
            .Select(text => TextRenderer.MeasureText(text, _product.Font).Width)
            .DefaultIfEmpty(_product.Width)
            .Max();
        _product.DropDownWidth = Math.Min(760, Math.Max(_product.Width, longestTextWidth + 28));
        _productToolTip.SetToolTip(_product, _product.Text);
    }

    private static string FormatProductText(PosSkuSaleItem sku)
    {
        var variant = string.IsNullOrWhiteSpace(sku.VarianteNombre)
            ? string.Empty
            : $" / {sku.VarianteNombre}";
        var subcategory = string.IsNullOrWhiteSpace(sku.SubcategoriaNombre)
            ? "Sin subcategoría"
            : sku.SubcategoriaNombre;
        return $"{sku.CodigoSku} - {sku.ProductoNombre}{variant} · {sku.CategoriaNombre} · {subcategory}";
    }

    private static void LayoutActionButtons(Panel panel, Button close, Button map)
    {
        close.Width = Math.Max(110, TextRenderer.MeasureText(close.Text, close.Font).Width + 28);
        map.Width = Math.Max(190, TextRenderer.MeasureText(map.Text, map.Font).Width + 28);
        close.Height = map.Height = Math.Max(42, close.PreferredSize.Height);
        var top = panel.Padding.Top;
        map.Location = new Point(panel.ClientSize.Width - panel.Padding.Right - map.Width, top);
        close.Location = new Point(map.Left - 8 - close.Width, top);
    }

    private async Task MapAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Row line || _product.SelectedItem is not PosSkuSaleItem product) return;
        try
        {
            await _finance.MapProductAsync(new(line.Id, product.ProductoId, product.ProductoSkuId));
            line.Status = $"{product.CodigoSku} - {product.ProductoNombre}";
            _grid.Refresh();
            if (_grid.Rows.Cast<DataGridViewRow>().All(r =>
                r.DataBoundItem is Row item && item.Status != "Pendiente"))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private sealed class Row
    {
        public Row(int id, string description, int quantity, string price, string status)
        { Id = id; Description = description; Quantity = quantity; Price = price; Status = status; }
        public int Id { get; }
        public string Description { get; }
        public int Quantity { get; }
        public string Price { get; }
        public string Status { get; set; }
    }
}
