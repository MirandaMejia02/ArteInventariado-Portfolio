using System.Globalization;
using System.Text;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

internal sealed record QuotationProductOption(
    int? Id,
    string Name,
    string? Code,
    string? NameSnapshot,
    long PriceCents,
    string? Category = null,
    string? Subcategory = null,
    int? SkuId = null)
{
    public string DisplayCode => Code ?? "-";
    public string DisplayName => NameSnapshot ?? Name;
    public string Price => (PriceCents / 100m).ToString("$0.00");
    public string DisplayCategory => string.IsNullOrWhiteSpace(Category) ? "No disponible" : Category;
    public string DisplaySubcategory => string.IsNullOrWhiteSpace(Subcategory) ? "Sin subcategoría" : Subcategory;
}

internal sealed class QuotationProductPickerForm : Form
{
    private readonly IReadOnlyList<QuotationProductOption> _products;
    private readonly BindingSource _binding = new();
    private readonly TextBox _search = new();
    private readonly Button _clearSearch = new();
    private readonly DataGridView _grid = new();
    private LiveSearchController? _liveSearch;

    public QuotationProductOption? SelectedProduct { get; private set; }

    public QuotationProductPickerForm(IReadOnlyList<QuotationProductOption> products)
    {
        _products = products;
        Text = "Buscar producto";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 620);
        MinimumSize = new Size(780, 500);
        UITheme.StyleForm(this);
        BuildUi();
        _liveSearch = new LiveSearchController(
            _search,
            _clearSearch,
            ApplyFilterAsync,
            ex => MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning));
        ApplyFilter();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var searchPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10, 5, 10, 5),
            MinimumSize = new Size(0, 74)
        };
        var label = new Label
        {
            Text = "Código, nombre, categoría o subcategoría",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 22,
            Font = UITheme.NormalBoldFont,
            Margin = new Padding(0, 0, 0, 5)
        };
        _search.Dock = DockStyle.None;
        _search.MinimumSize = new Size(120, 36);
        _search.AccessibleName = "Buscar producto por código, nombre, categoría o subcategoría";
        _search.PlaceholderText = "Código, nombre, categoría o subcategoría...";
        UITheme.StyleTextBox(_search);
        _clearSearch.Text = "X";
        _clearSearch.AccessibleName = "Limpiar búsqueda";
        _clearSearch.Dock = DockStyle.None;
        _clearSearch.Margin = new Padding(0, 0, 8, 0);
        _clearSearch.MinimumSize = new Size(42, 36);
        UITheme.StyleSecondaryButton(_clearSearch);
        var searchButton = new Button { Text = "Buscar", Dock = DockStyle.None, Margin = new Padding(0) };
        searchButton.MinimumSize = new Size(104, 36);
        UITheme.StylePrimaryButton(searchButton);
        searchButton.Click += (_, _) =>
        {
            _liveSearch?.CancelPending();
            ApplyFilter();
        };
        searchPanel.Controls.Add(label);
        searchPanel.Controls.Add(_search);
        searchPanel.Controls.Add(_clearSearch);
        searchPanel.Controls.Add(searchButton);
        searchPanel.Resize += (_, _) => LayoutSearchPanel(searchPanel, label, searchButton);
        LayoutSearchPanel(searchPanel, label, searchButton);

        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(QuotationProductOption.DisplayCode), HeaderText = "Código", Width = 128, MinimumWidth = 120, FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(QuotationProductOption.DisplayName), HeaderText = "Producto", Width = 240, MinimumWidth = 220, FillWeight = 34 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(QuotationProductOption.DisplayCategory), HeaderText = "Categoría", Width = 140, MinimumWidth = 130, FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(QuotationProductOption.DisplaySubcategory), HeaderText = "Subcategoría", Width = 160, MinimumWidth = 145, FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(QuotationProductOption.Price), HeaderText = "Precio de venta", Width = 150, MinimumWidth = 140, FillWeight = 14, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            var measuredHeader = TextRenderer.MeasureText(
                column.HeaderText,
                _grid.ColumnHeadersDefaultCellStyle.Font ?? _grid.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 24;
            column.Width = Math.Max(column.Width, measuredHeader);
            column.MinimumWidth = Math.Max(column.MinimumWidth, measuredHeader - 8);
        }
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex is < 1 or > 3)
            {
                return;
            }

            var text = Convert.ToString(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
            if (!string.IsNullOrWhiteSpace(text) &&
                TextRenderer.MeasureText(text, _grid.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width >
                    _grid.Columns[e.ColumnIndex].Width - 10)
            {
                e.ToolTipText = text;
            }
        };
        _grid.DataSource = _binding;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                AcceptRow(e.RowIndex);
        };

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            MinimumSize = new Size(0, 58)
        };
        var select = new Button { Text = "Seleccionar", Dock = DockStyle.None, Margin = new Padding(8, 0, 0, 0) };
        var cancel = new Button { Text = "Cancelar", Dock = DockStyle.None, DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        UITheme.StylePrimaryButton(select);
        UITheme.StyleSecondaryButton(cancel);
        UITheme.EnsureButtonContentFits(select, 120, 42, 28);
        UITheme.EnsureButtonContentFits(cancel, 120, 42, 28);
        select.Name = "SelectQuotationProductButton";
        cancel.Name = "CancelQuotationProductButton";
        select.Click += (_, _) => AcceptSelection();
        select.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        footer.Controls.Add(select);
        footer.Controls.Add(cancel);
        footer.Resize += (_, _) => LayoutFooter(footer, select, cancel);
        LayoutFooter(footer, select, cancel);

        root.Controls.Add(searchPanel, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        AcceptButton = select;
        CancelButton = cancel;
    }

    private void LayoutSearchPanel(Panel panel, Label label, Button searchButton)
    {
        _clearSearch.Width = Math.Max(42, TextRenderer.MeasureText(_clearSearch.Text, _clearSearch.Font).Width + 22);
        searchButton.Width = Math.Max(104, TextRenderer.MeasureText(searchButton.Text, searchButton.Font).Width + 28);
        _search.Width = Math.Max(
            120,
            panel.ClientSize.Width - panel.Padding.Horizontal - _clearSearch.Width - searchButton.Width - 16);
        _search.Height = Math.Max(36, _search.PreferredHeight);
        _clearSearch.Height = searchButton.Height = Math.Max(36, _search.Height);
        var top = panel.Padding.Top + label.Height + 4;
        _search.Location = new Point(panel.Padding.Left, top);
        _clearSearch.Location = new Point(_search.Right + 8, top);
        searchButton.Location = new Point(_clearSearch.Right + 8, top);
    }

    private static void LayoutFooter(Panel panel, Button select, Button cancel)
    {
        select.Width = Math.Max(120, TextRenderer.MeasureText(select.Text, select.Font).Width + 28);
        cancel.Width = Math.Max(120, TextRenderer.MeasureText(cancel.Text, cancel.Font).Width + 28);
        select.Height = cancel.Height = Math.Max(42, select.PreferredSize.Height);
        var top = panel.Padding.Top;
        cancel.Location = new Point(panel.ClientSize.Width - panel.Padding.Right - cancel.Width, top);
        select.Location = new Point(cancel.Left - 8 - select.Width, top);
    }

    private void ApplyFilter()
    {
        SelectedProduct = null;
        var terms = Normalize(_search.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _binding.DataSource = _products
            .Where(product => terms.Length == 0 || terms.All(term => Normalize(
                $"{product.Code} {product.DisplayName} {product.DisplayCategory} {product.DisplaySubcategory}")
                .Contains(term, StringComparison.Ordinal)))
            .OrderBy(product => product.Id.HasValue)
            .ThenBy(product => product.DisplayName)
            .Take(250)
            .ToList();
    }

    private Task ApplyFilterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyFilter();
        return Task.CompletedTask;
    }

    internal bool AcceptSelection(bool showValidationMessage = true)
    {
        var selected = _grid.CurrentRow?.DataBoundItem as QuotationProductOption
            ?? _binding.Current as QuotationProductOption;
        if (selected is null)
        {
            if (showValidationMessage)
                MessageBox.Show("Seleccione un producto.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
