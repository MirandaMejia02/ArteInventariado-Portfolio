using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SupplierProductAssociationForm : Form
{
    private readonly int _supplierId;
    private readonly ISupplierService _supplierService;
    private readonly IProductService _productService;
    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly TextBox _txtSupplierCode = new();
    private readonly TextBox _txtNotes = new();
    private readonly CheckBox _chkMain = new();
    private readonly Button _btnSearch = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();
    private readonly Label _lblStatus = new();
    private readonly DataGridView _grid = new();
    private readonly ToolTip _toolTip = new();

    public SupplierProductAssociationForm(
        int supplierId,
        ISupplierService supplierService,
        IProductService productService)
    {
        _supplierId = supplierId;
        _supplierService = supplierService;
        _productService = productService;

        BuildUi();
        Load += async (_, _) => await LoadProductsAsync();
    }

    public SupplierProductSummary? SavedAssociation { get; private set; }

    private void BuildUi()
    {
        Text = "Asociar producto";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 680);
        MinimumSize = new Size(900, 620);
        // La ventana no debe necesitar desplazamiento horizontal para alcanzar
        // las acciones principales; los grids gestionan su propio scroll.
        AutoScroll = false;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            Text = "Asociar producto existente",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        UITheme.StyleLabelTitle(header);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(BuildSearchPanel(), 0, 1);
        root.Controls.Add(BuildGridPanel(), 0, 2);
        root.Controls.Add(BuildFieldsPanel(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;
    }

    private Control BuildSearchPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12),
            MinimumSize = new Size(0, 82)
        };

        var label = new Label
        {
            Text = "Buscar por nombre, código, SKU, categoría o subcategoría",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 24,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, 6)
        };
        panel.Controls.Add(label);

        UITheme.StyleTextBox(_txtSearch);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.MinimumSize = new Size(0, 36);
        _txtSearch.AccessibleName = "Buscar producto por nombre, código, SKU, categoría o subcategoría";
        _txtSearch.PlaceholderText = "Nombre, código, SKU, categoría o subcategoría...";
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                await LoadProductsAsync();
            }
        };

        ConfigureButton(_btnSearch, "Buscar", 104, async (_, _) => await LoadProductsAsync());
        UITheme.StylePrimaryButton(_btnSearch);
        var clear = new Button();
        ConfigureButton(clear, "Limpiar", 104, async (_, _) =>
        {
            _txtSearch.Clear();
            await LoadProductsAsync();
        });
        UITheme.StyleSecondaryButton(clear);

        _txtSearch.Dock = DockStyle.None;
        _txtSearch.Margin = Padding.Empty;
        _btnSearch.Dock = DockStyle.None;
        _btnSearch.Margin = Padding.Empty;
        clear.Dock = DockStyle.None;
        panel.Controls.Add(_txtSearch);
        panel.Controls.Add(_btnSearch);
        panel.Controls.Add(clear);
        panel.Resize += (_, _) => LayoutSearchPanel(panel, label, clear);
        LayoutSearchPanel(panel, label, clear);
        return panel;
    }

    private void LayoutSearchPanel(Panel panel, Label label, Button clear)
    {
        _btnSearch.Width = Math.Max(104, TextRenderer.MeasureText(_btnSearch.Text, _btnSearch.Font).Width + 28);
        clear.Width = Math.Max(104, TextRenderer.MeasureText(clear.Text, clear.Font).Width + 28);
        _txtSearch.Width = Math.Max(
            160,
            panel.ClientSize.Width - panel.Padding.Horizontal - _btnSearch.Width - clear.Width - 16);
        _txtSearch.Height = Math.Max(36, _txtSearch.PreferredHeight);
        _btnSearch.Height = clear.Height = Math.Max(36, _txtSearch.Height);
        var top = panel.Padding.Top + label.Height + 4;
        _txtSearch.Location = new Point(panel.Padding.Left, top);
        _btnSearch.Location = new Point(_txtSearch.Right + 8, top);
        clear.Location = new Point(_btnSearch.Right + 8, top);
    }

    private Control BuildGridPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12),
            Margin = new Padding(0, 12, 0, 12)
        };

        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.ShowCellToolTips = true;
        _grid.DataSource = _binding;
        _grid.CellDoubleClick += async (_, _) => await SaveAsync();
        _grid.SizeChanged += (_, _) => FitGridColumns();
        _grid.DataBindingComplete += (_, _) => FitGridColumns();

        AddGridColumn("CodigoInterno", "Código", 95, 14);
        AddGridColumn("Nombre", "Producto", 220, 40);
        AddGridColumn("Categoria", "Categoría", 140, 16);
        AddGridColumn("Subcategoria", "Subcategoría", 160, 18);
        AddGridColumn("PrecioVenta", "Precio venta", 120, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("Stock", "Stock", 70, 7, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Estado", "Estado", 80, 9, DataGridViewContentAlignment.MiddleCenter);
        FitGridColumns();

        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildFieldsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12),
            MinimumSize = new Size(0, 170),
            ColumnCount = 2,
            RowCount = 3
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddField(panel, 0, "Codigo del proveedor", _txtSupplierCode);
        _toolTip.SetToolTip(
            _txtSupplierCode,
            "Codigo utilizado por este proveedor para identificar el producto.");
        _chkMain.Text = "Marcar como proveedor principal del producto";
        _chkMain.Dock = DockStyle.Fill;
        _chkMain.AutoSize = false;
        _chkMain.Font = UITheme.NormalBoldFont;
        _chkMain.ForeColor = UITheme.TextDark;
        _chkMain.Margin = new Padding(0, 0, 0, 4);
        panel.Controls.Add(new Label(), 0, 1);
        panel.Controls.Add(_chkMain, 1, 1);

        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.MinimumSize = new Size(0, 82);
        AddField(panel, 2, "Notas", _txtNotes);
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 12, 0, 8),
            MinimumSize = new Size(0, 84)
        };

        _lblStatus.Dock = DockStyle.Left;
        _lblStatus.AutoSize = true;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = UITheme.NormalBoldFont;
        _lblStatus.ForeColor = UITheme.MutedTextColor;

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => DialogResult = DialogResult.Cancel);
        UITheme.StyleSecondaryButton(_btnCancel);
        ConfigureButton(_btnSave, "Asociar", 112, async (_, _) => await SaveAsync());
        UITheme.StylePrimaryButton(_btnSave);

        _btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        panel.Controls.Add(_lblStatus);
        panel.Controls.Add(_btnCancel);
        panel.Controls.Add(_btnSave);
        panel.Resize += (_, _) => LayoutActionButtons(panel);
        LayoutActionButtons(panel);
        return panel;
    }

    private void LayoutActionButtons(Panel panel)
    {
        _btnCancel.Width = Math.Max(110, TextRenderer.MeasureText(_btnCancel.Text, _btnCancel.Font).Width + 28);
        _btnSave.Width = Math.Max(112, TextRenderer.MeasureText(_btnSave.Text, _btnSave.Font).Width + 28);
        _btnCancel.Height = _btnSave.Height = Math.Max(36, _btnCancel.PreferredSize.Height);
        var top = panel.Padding.Top;
        _btnCancel.Location = new Point(panel.ClientSize.Width - panel.Padding.Right - _btnCancel.Width, top);
        _btnSave.Location = new Point(_btnCancel.Left - 8 - _btnSave.Width, top);
    }

    private async Task LoadProductsAsync()
    {
        _btnSearch.Enabled = false;
        _lblStatus.Text = "Cargando productos...";

        try
        {
            var products = await _productService.GetAllAsync(new ProductFilter(
                _txtSearch.Text,
                EstadoProducto.ACTIVO));

            _binding.DataSource = products
                .Take(250)
                .Select(ProductRow.FromSummary)
                .ToList();
            _lblStatus.Text = $"{_binding.Count} producto(s) mostrados.";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnSearch.Enabled = true;
        }
    }

    private async Task SaveAsync()
    {
        if (_binding.Current is not ProductRow row)
        {
            MessageBox.Show("Seleccione un producto.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnSave.Enabled = false;
        _lblStatus.Text = "Asociando producto...";

        try
        {
            SavedAssociation = await _supplierService.AssociateProductAsync(new SupplierProductAssociationRequest(
                _supplierId,
                row.ProductoId,
                NullIfWhiteSpace(_txtSupplierCode.Text),
                _chkMain.Checked,
                NullIfWhiteSpace(_txtNotes.Text)));

            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }

    private static void AddField(TableLayoutPanel layout, int row, string labelText, TextBox textBox)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 10, 6)
        };

        UITheme.StyleTextBox(textBox);
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 0, 0, 6);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(textBox, 1, row);
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int column)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 10, 0)
        };
        layout.Controls.Add(label, column, 0);
    }

    private void AddGridColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private void FitGridColumns()
    {
        var columns = _grid.Columns
            .Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .ToList();
        var availableWidth = _grid.DisplayRectangle.Width;
        if (columns.Count == 0 || availableWidth <= 0)
        {
            return;
        }

        var minimumWidth = columns.Sum(column => column.MinimumWidth);
        if (availableWidth <= minimumWidth)
        {
            foreach (var column in columns)
            {
                column.Width = column.MinimumWidth;
            }

            return;
        }

        var extraWidth = availableWidth - minimumWidth;
        var totalWeight = columns.Sum(column => column.FillWeight);
        var assignedWidth = 0;
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var width = index == columns.Count - 1
                ? availableWidth - assignedWidth
                : column.MinimumWidth + (int)Math.Floor(extraWidth * column.FillWeight / totalWeight);
            column.Width = width;
            assignedWidth += width;
        }
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ProductRow(
        int ProductoId,
        string CodigoInterno,
        string Nombre,
        string Categoria,
        string Subcategoria,
        string PrecioVenta,
        int Stock,
        string Estado)
    {
        public static ProductRow FromSummary(ProductSummary summary)
        {
            return new ProductRow(
                summary.ProductoId,
                summary.CodigoInterno,
                summary.Nombre,
                summary.CategoriaNombre,
                string.IsNullOrWhiteSpace(summary.SubcategoriaNombre)
                    ? "Sin subcategoría"
                    : summary.SubcategoriaNombre,
                SaleReceiptViewModel.FormatDollars(summary.PrecioVentaCentavos),
                summary.StockActual,
                summary.Estado.ToString());
        }
    }
}
