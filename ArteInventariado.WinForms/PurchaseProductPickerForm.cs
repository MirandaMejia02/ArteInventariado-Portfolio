using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

internal sealed record PurchaseProductOption(
    int Id,
    string Code,
    string ProductName,
    string Category,
    int StockActual,
    long CostCents,
    int? SkuId = null,
    string? Variant = null,
    string? Subcategory = null)
{
    public string DisplayName => $"{Code} - {ProductName}{(string.IsNullOrWhiteSpace(Variant) ? string.Empty : $" / {Variant}")}";
    public string Cost => SaleReceiptViewModel.FormatDollars(CostCents);
    public string DisplaySubcategory => string.IsNullOrWhiteSpace(Subcategory) ? "Sin subcategoría" : Subcategory;

    public static PurchaseProductOption From(PosSkuSaleItem sku) => new(
        sku.ProductoId,
        sku.CodigoSku,
        sku.ProductoNombre,
        sku.CategoriaNombre,
        sku.StockFisicoTotal,
        sku.CostoEfectivoCentavos,
        sku.ProductoSkuId,
        sku.VarianteNombre,
        sku.SubcategoriaNombre);
}

internal sealed class PurchaseProductPickerForm : Form
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<PurchaseProductOption>>> _searchProductsAsync;
    private readonly BindingSource _binding = new();
    private readonly TextBox _search = new();
    private readonly Button _clearSearch = new();
    private readonly DataGridView _grid = new();
    private readonly Label _emptyState = new();
    private readonly Label _status = new();
    private readonly Button _select = new();
    private LiveSearchController? _liveSearch;
    private bool _disposing;

    public PurchaseProductOption? SelectedProduct { get; private set; }

    public PurchaseProductPickerForm(IPosCatalogService catalogService)
        : this(async (searchText, cancellationToken) =>
        {
            var products = await catalogService.SearchAsync(
                new PosSkuCatalogFilter(
                    SearchText: string.IsNullOrWhiteSpace(searchText) ? null : searchText,
                    Estado: EstadoProducto.ACTIVO,
                    SkuActivo: true), cancellationToken);
            return products.Select(PurchaseProductOption.From).ToList();
        })
    {
    }

    internal PurchaseProductPickerForm(IReadOnlyList<PurchaseProductOption> products)
        : this((searchText, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var terms = QuotationProductPickerForm.Normalize(searchText)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            IReadOnlyList<PurchaseProductOption> result = products
                .Where(product => terms.Length == 0 || terms.All(term =>
                    QuotationProductPickerForm.Normalize(
                            $"{product.Code} {product.ProductName} {product.Category} {product.DisplaySubcategory} {product.Variant}")
                        .Contains(term, StringComparison.Ordinal)))
                .OrderBy(product => product.ProductName)
                .ThenBy(product => product.Code)
                .ToList();
            return Task.FromResult(result);
        })
    {
    }

    private PurchaseProductPickerForm(
        Func<string, CancellationToken, Task<IReadOnlyList<PurchaseProductOption>>> searchProductsAsync)
    {
        _searchProductsAsync = searchProductsAsync;
        Text = "Buscar producto para la compra";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 680);
        MinimumSize = new Size(1060, 600);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);
        BuildUi();
        _liveSearch = new LiveSearchController(
            _search,
            _clearSearch,
            SearchAsync,
            ex => MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning));
        Load += async (_, _) => await _liveSearch.SearchNowAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposing = true;
            _liveSearch?.Dispose();
            _grid.DataSource = null;
            _binding.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.BackgroundColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 8, 16, 8),
            Margin = new Padding(0, 0, 0, 10)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = "Seleccionar producto", AutoSize = true, Font = UITheme.HeaderFont, ForeColor = UITheme.PrimaryColor };
        var explanation = new Label
        {
            Text = "Busca por nombre, código, SKU, categoría o subcategoría. Selecciona el resultado correcto para agregarlo a la compra.",
            AutoSize = true,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor
        };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(explanation, 0, 1);

        var searchPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14, 6, 14, 8),
            Margin = new Padding(0, 0, 0, 10),
            MinimumSize = new Size(0, 74)
        };
        var searchLabel = new Label
        {
            Text = "Buscar producto",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };
        _search.Name = "PurchaseProductSearchTextBox";
        _search.AccessibleName = "Buscar producto por nombre, código, SKU, categoría o subcategoría";
        _search.PlaceholderText = "Escribe nombre, código, SKU, categoría o subcategoría...";
        _search.Dock = DockStyle.None;
        _search.MinimumSize = new Size(120, 36);
        UITheme.StyleTextBox(_search);
        _clearSearch.Name = "ClearPurchaseProductSearchButton";
        _clearSearch.Text = "×";
        _clearSearch.AccessibleName = "Limpiar búsqueda de producto";
        _clearSearch.Dock = DockStyle.None;
        _clearSearch.Margin = new Padding(0, 0, 8, 0);
        _clearSearch.MinimumSize = new Size(42, 36);
        UITheme.StyleSecondaryButton(_clearSearch);
        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(_search);
        searchPanel.Controls.Add(_clearSearch);
        searchPanel.Resize += (_, _) => LayoutSearchPanel(searchPanel, searchLabel);
        LayoutSearchPanel(searchPanel, searchLabel);

        var resultsPanel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(1) };
        ConfigureGrid();
        _emptyState.Text = "No se encontraron productos.\nPrueba con otro nombre, código, categoría o SKU.";
        _emptyState.Dock = DockStyle.Fill;
        _emptyState.TextAlign = ContentAlignment.MiddleCenter;
        _emptyState.Font = UITheme.NormalBoldFont;
        _emptyState.ForeColor = UITheme.MutedTextColor;
        _emptyState.BackColor = UITheme.SurfaceColor;
        _emptyState.Visible = false;
        resultsPanel.Controls.Add(_grid);
        resultsPanel.Controls.Add(_emptyState);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 10, 0, 0),
            MinimumSize = new Size(0, 64)
        };
        _status.Text = "Preparando productos...";
        _status.Dock = DockStyle.Left;
        _status.AutoSize = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = UITheme.NormalFont;
        _status.ForeColor = UITheme.MutedTextColor;
        _select.Name = "SelectPurchaseProductButton";
        _select.Text = "Seleccionar";
        _select.Dock = DockStyle.None;
        _select.Margin = new Padding(8, 0, 0, 0);
        _select.Enabled = false;
        UITheme.StylePrimaryButton(_select);
        UITheme.EnsureButtonContentFits(_select, 150, 44, 36);
        _select.Click += (_, _) => AcceptSelection();
        var cancel = new Button
        {
            Name = "CancelPurchaseProductButton",
            Text = "Cancelar",
            Dock = DockStyle.None,
            Margin = new Padding(8, 0, 0, 0),
            DialogResult = DialogResult.Cancel
        };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.EnsureButtonContentFits(cancel, 120, 42, 28);
        _select.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        footer.Controls.Add(_status);
        footer.Controls.Add(_select);
        footer.Controls.Add(cancel);
        footer.Resize += (_, _) => LayoutFooter(footer, _select, cancel);
        LayoutFooter(footer, _select, cancel);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(searchPanel, 0, 1);
        root.Controls.Add(resultsPanel, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        AcceptButton = _select;
        CancelButton = cancel;
    }

    private void LayoutSearchPanel(Panel panel, Label label)
    {
        _clearSearch.Width = Math.Max(42, TextRenderer.MeasureText(_clearSearch.Text, _clearSearch.Font).Width + 22);
        _search.Width = Math.Max(
            120,
            panel.ClientSize.Width - panel.Padding.Horizontal - _clearSearch.Width - 8);
        _search.Height = Math.Max(36, _search.PreferredHeight);
        _clearSearch.Height = _search.Height;
        var top = panel.Padding.Top + label.Height + 4;
        _search.Location = new Point(panel.Padding.Left, top);
        _clearSearch.Location = new Point(_search.Right + 8, top);
    }

    private static void LayoutFooter(Panel panel, Button select, Button cancel)
    {
        select.Width = Math.Max(150, TextRenderer.MeasureText(select.Text, select.Font).Width + 28);
        cancel.Width = Math.Max(120, TextRenderer.MeasureText(cancel.Text, cancel.Font).Width + 28);
        select.Height = cancel.Height = Math.Max(42, select.PreferredSize.Height);
        var top = panel.Padding.Top;
        cancel.Location = new Point(panel.ClientSize.Width - panel.Padding.Right - cancel.Width, top);
        select.Location = new Point(cancel.Left - 8 - select.Width, top);
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Name = "PurchaseProductResultsGrid";
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        // Fit the table to the available width. On smaller windows the minimum
        // widths keep columns readable and let the horizontal scrollbar take over.
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PurchaseProductOption.Code), HeaderText = "Código", Width = 145,
            MinimumWidth = 120, FillWeight = 12
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PurchaseProductOption.ProductName), HeaderText = "Producto",
            Width = 280, MinimumWidth = 240, FillWeight = 24
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PurchaseProductOption.Variant), HeaderText = "Variante", Width = 150,
            MinimumWidth = 110, FillWeight = 13
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PurchaseProductOption.Category), HeaderText = "Categoría", Width = 170,
            MinimumWidth = 130, FillWeight = 14
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PurchaseProductOption.DisplaySubcategory), HeaderText = "Subcategoría", Width = 175,
            MinimumWidth = 145, FillWeight = 16
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PurchaseProductOption.StockActual), HeaderText = "Stock actual", Width = 120,
            MinimumWidth = 115, FillWeight = 10,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PurchaseProductOption.Cost), HeaderText = "Último costo", Width = 145,
            MinimumWidth = 130, FillWeight = 11,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) =>
        {
            if (!_disposing)
                _select.Enabled = _grid.CurrentRow?.DataBoundItem is PurchaseProductOption;
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) AcceptRow(e.RowIndex);
        };
    }

    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var searchText = _search.Text.Trim();
        _grid.Enabled = false;
        _select.Enabled = false;
        _status.Text = "Buscando productos...";
        try
        {
            var products = await _searchProductsAsync(searchText, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var rows = products.OrderBy(product => product.ProductName).ThenBy(product => product.Code).Take(250).ToList();
            _binding.DataSource = rows;
            _emptyState.Visible = rows.Count == 0;
            if (_emptyState.Visible) _emptyState.BringToFront(); else _grid.BringToFront();
            _status.Text = rows.Count == 0
                ? "Sin coincidencias."
                : $"{rows.Count} producto{(rows.Count == 1 ? string.Empty : "s")} encontrado{(rows.Count == 1 ? string.Empty : "s")}.";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _grid.Enabled = true;
                _select.Enabled = _grid.CurrentRow?.DataBoundItem is PurchaseProductOption;
            }
        }
    }

    internal bool AcceptSelection(bool showValidationMessage = true)
    {
        var selected = _grid.CurrentRow?.DataBoundItem as PurchaseProductOption
            ?? _binding.Current as PurchaseProductOption;
        if (selected is null)
        {
            if (showValidationMessage)
                MessageBox.Show("Selecciona un producto de los resultados.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        SelectedProduct = selected;
        DialogResult = DialogResult.OK;
        Close();
        return true;
    }

    internal bool AcceptRow(int rowIndex, bool showValidationMessage = true)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count)
            return AcceptSelection(showValidationMessage);
        _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
        return AcceptSelection(showValidationMessage);
    }

    internal Task SearchNowAsync() => _liveSearch?.SearchNowAsync() ?? Task.CompletedTask;
    internal int ResultCount => _binding.Count;
}
