using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class PurchaseEditForm : Form
{
    public PurchaseDetail? SavedPurchase { get; private set; }
    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierQueryService _supplierQueryService;
    private readonly IPosCatalogService _catalogService;
    private readonly PurchaseDetail? _existing;
    private readonly BindingSource _linesBinding = new();

    private readonly ComboBox _cmbSupplier = new();
    private readonly DateTimePicker _dtpPurchaseDate = new();
    private readonly TextBox _txtObservations = new();
    private readonly Label _lblSelectedProduct = new();
    private readonly Button _btnSearchProduct = new();
    private readonly NumericUpDown _numQuantity = new();
    private readonly NumericUpDown _numCost = new();
    private readonly NumericUpDown _numLineDiscount = new();
    private readonly TextBox _txtLineNotes = new();
    private readonly NumericUpDown _numPurchaseDiscount = new();
    private readonly Label _lblSubtotal = new();
    private readonly Label _lblTotal = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnAddLine = new();
    private readonly Button _btnRemoveLine = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();
    private readonly DataGridView _grid = new();

    private List<PurchaseLineEditRow> _lines = [];
    private List<SupplierOption> _suppliers = [];
    private PurchaseProductOption? _selectedProduct;

    public PurchaseEditForm(
        IPurchaseService purchaseService,
        ISupplierQueryService supplierQueryService,
        IPosCatalogService catalogService,
        PurchaseDetail? existing = null,
        bool loadDataOnShow = true)
    {
        _purchaseService = purchaseService;
        _supplierQueryService = supplierQueryService;
        _catalogService = catalogService;
        _existing = existing;

        BuildUi();
        if (loadDataOnShow) Load += PurchaseEditForm_Load;
    }

    private async void PurchaseEditForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await LoadCatalogsAsync();
            LoadExistingPurchase();
            RefreshLineGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = _existing is null ? "Nueva compra" : $"Editar compra {_existing.CodigoCompra}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(1080, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildGeneralData(), 0, 1);
        root.Controls.Add(BuildLineEditor(), 0, 2);
        root.Controls.Add(BuildLinesGrid(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = _existing is null ? "Registrar compra" : $"Editar compra {_existing.CodigoCompra}",
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "La compra no afecta inventario hasta registrar recepcion.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildGeneralData()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddLabel(layout, "Proveedor activo", 0, 0);
        AddLabel(layout, "Fecha compra", 1, 0);
        AddLabel(layout, "Descuento compra ($)", 2, 0);
        AddLabel(layout, "Observaciones", 3, 0);

        _cmbSupplier.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSupplier.DisplayMember = nameof(SupplierOption.Name);
        _cmbSupplier.ValueMember = nameof(SupplierOption.Id);
        _cmbSupplier.Dock = DockStyle.Fill;
        _cmbSupplier.Font = UITheme.NormalFont;

        _dtpPurchaseDate.Format = DateTimePickerFormat.Short;
        _dtpPurchaseDate.Value = DateTime.Today;
        _dtpPurchaseDate.Dock = DockStyle.Fill;
        _dtpPurchaseDate.Font = UITheme.NormalFont;

        ConfigureMoney(_numPurchaseDiscount);
        _numPurchaseDiscount.ValueChanged += (_, _) => UpdateTotals();

        _txtObservations.Multiline = true;
        _txtObservations.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_txtObservations);

        layout.Controls.Add(_cmbSupplier, 0, 1);
        layout.Controls.Add(_dtpPurchaseDate, 1, 1);
        layout.Controls.Add(_numPurchaseDiscount, 2, 1);
        layout.Controls.Add(_txtObservations, 3, 1);
        layout.SetRowSpan(_txtObservations, 2);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildLineEditor()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));

        AddLabel(layout, "Producto seleccionado", 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 5);
        AddLabel(layout, "Cantidad", 0, 2);
        AddLabel(layout, "Costo unitario ($)", 1, 2);
        AddLabel(layout, "Descuento ($)", 2, 2);
        AddLabel(layout, "Notas del producto", 3, 2);
        AddLabel(layout, "Acciones", 4, 2);

        _lblSelectedProduct.Name = "SelectedPurchaseProductLabel";
        _lblSelectedProduct.Text = "Ningún producto seleccionado";
        _lblSelectedProduct.Dock = DockStyle.Fill;
        _lblSelectedProduct.TextAlign = ContentAlignment.MiddleLeft;
        _lblSelectedProduct.Padding = new Padding(8, 0, 4, 0);
        _lblSelectedProduct.BorderStyle = BorderStyle.FixedSingle;
        _lblSelectedProduct.BackColor = UITheme.SurfaceColor;
        _lblSelectedProduct.ForeColor = UITheme.MutedTextColor;
        _lblSelectedProduct.Font = UITheme.NormalFont;
        _lblSelectedProduct.AutoEllipsis = true;
        ConfigureButton(_btnSearchProduct, "Buscar producto", 135, (_, _) => SelectProduct());
        _btnSearchProduct.Name = "SearchPurchaseProductButton";
        _btnSearchProduct.Dock = DockStyle.Fill;
        _btnSearchProduct.Margin = new Padding(8, 0, 0, 0);
        UITheme.StyleSecondaryButton(_btnSearchProduct);
        UITheme.EnsureButtonContentFits(_btnSearchProduct, 135, 38, 28);
        var productSelector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        productSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        productSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        productSelector.Controls.Add(_lblSelectedProduct, 0, 0);
        productSelector.Controls.Add(_btnSearchProduct, 1, 0);

        _numQuantity.Minimum = 1;
        _numQuantity.Maximum = 100000;
        _numQuantity.Value = 1;
        _numQuantity.Dock = DockStyle.Fill;
        _numQuantity.Font = UITheme.NormalFont;
        ConfigureMoney(_numCost);
        ConfigureMoney(_numLineDiscount);

        _txtLineNotes.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_txtLineNotes);

        ConfigureButton(_btnAddLine, "Agregar a la compra", 175, (_, _) => AddLine());
        _btnAddLine.Name = "AddPurchaseLineButton";
        UITheme.StylePrimaryButton(_btnAddLine);
        UITheme.EnsureButtonContentFits(_btnAddLine, 175, 40, 32);
        _btnAddLine.Size = _btnAddLine.MinimumSize;
        ConfigureButton(_btnRemoveLine, "Quitar producto", 160, (_, _) => RemoveSelectedLine());
        UITheme.StyleDangerButton(_btnRemoveLine);
        UITheme.EnsureButtonContentFits(_btnRemoveLine, 160, 40, 24);
        _btnRemoveLine.Size = _btnRemoveLine.MinimumSize;

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _btnAddLine.Dock = DockStyle.Fill;
        _btnAddLine.Margin = new Padding(0, 0, 6, 0);
        _btnRemoveLine.Dock = DockStyle.Fill;
        _btnRemoveLine.Margin = new Padding(6, 0, 0, 0);
        actions.Controls.Add(_btnAddLine, 0, 0);
        actions.Controls.Add(_btnRemoveLine, 1, 0);

        layout.Controls.Add(productSelector, 0, 1);
        layout.SetColumnSpan(productSelector, 5);
        layout.Controls.Add(_numQuantity, 0, 3);
        layout.Controls.Add(_numCost, 1, 3);
        layout.Controls.Add(_numLineDiscount, 2, 3);
        layout.Controls.Add(_txtLineNotes, 3, 3);
        layout.Controls.Add(actions, 4, 3);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildLinesGrid()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };

        ConfigureGrid();
        _grid.DataSource = _linesBinding;
        _grid.SelectionChanged += (_, _) => UpdateActionState();
        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var statusLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ConfigureTotalLabel(_lblSubtotal, "Subtotal: 0.00");
        ConfigureTotalLabel(_lblTotal, "Total: 0.00");
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = UITheme.NormalBoldFont;
        _lblStatus.ForeColor = UITheme.MutedTextColor;
        _lblStatus.AutoSize = true;
        _lblStatus.Margin = new Padding(0, 9, 24, 0);
        statusLayout.Controls.AddRange([_lblStatus, _lblSubtotal, _lblTotal]);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true
        };

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnCancel);
        ConfigureButton(_btnSave, "Guardar", 120, async (_, _) => await SaveAsync());
        UITheme.StyleSuccessButton(_btnSave);
        actions.Controls.AddRange([_btnCancel, _btnSave]);

        panel.Controls.Add(statusLayout, 0, 0);
        panel.Controls.Add(actions, 1, 0);
        return panel;
    }

    private async Task LoadCatalogsAsync()
    {
        var suppliers = await _supplierQueryService.SearchAsync(new SupplierFilter(
            Estado: EstadoProveedor.ACTIVO,
            PageSize: 500));
        _suppliers = suppliers
            .OrderBy(s => s.NombreRazonSocial)
            .Select(s => new SupplierOption(s.ProveedorId, $"{s.CodigoProveedor} - {s.NombreRazonSocial}"))
            .ToList();
        _cmbSupplier.DataSource = _suppliers;

    }

    private void LoadExistingPurchase()
    {
        if (_existing is null)
        {
            _lblStatus.Text = "Nueva compra pendiente de guardar.";
            return;
        }

        SelectComboValue(_cmbSupplier, _existing.ProveedorId);
        _dtpPurchaseDate.Value = _existing.FechaCompra.Date;
        _numPurchaseDiscount.Value = ToDollarsValue(_existing.DescuentoCentavos);
        _txtObservations.Text = _existing.Observaciones ?? string.Empty;
        _lines = _existing.Detalles
            .Select(line => new PurchaseLineEditRow(
                line.ProductoId,
                line.CodigoProducto,
                line.Producto,
                line.CantidadComprada,
                line.CostoUnitarioCentavos,
                line.DescuentoLineaCentavos,
                line.SubtotalCentavos,
                line.Observaciones ?? string.Empty,
                line.ProductoSkuId,
                line.Variante))
            .ToList();
        _lblStatus.Text = $"{_existing.CodigoCompra} lista para editar.";
    }

    private void SelectProduct()
    {
        using var form = new PurchaseProductPickerForm(_catalogService);
        if (form.ShowDialog(this) != DialogResult.OK || form.SelectedProduct is null) return;
        ApplySelectedProduct(form.SelectedProduct);
    }

    internal void ApplySelectedProduct(PurchaseProductOption product)
    {
        _selectedProduct = product;
        _lblSelectedProduct.Text =
            $"{product.DisplayName} · {product.Category} · {product.DisplaySubcategory}";
        _lblSelectedProduct.ForeColor = UITheme.TextDark;
        _lblSelectedProduct.AccessibleDescription =
            $"Producto seleccionado: {product.ProductName}, código {product.Code}, " +
            $"categoría {product.Category}, subcategoría {product.DisplaySubcategory}.";
        _numCost.Value = ToDollarsValue(product.CostCents);
        _numQuantity.Focus();
    }

    internal PurchaseProductOption? SelectedProduct => _selectedProduct;
    internal int LineCount => _lines.Count;

    private void AddLine()
    {
        if (_selectedProduct is not { } product)
        {
            MessageBox.Show("Seleccione un producto.", "Compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_lines.Any(l => product.SkuId.HasValue
                ? l.ProductoSkuId == product.SkuId
                : !l.ProductoSkuId.HasValue && l.ProductoId == product.Id))
        {
            MessageBox.Show("El producto ya esta agregado a la compra.", "Compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var quantity = (int)_numQuantity.Value;
        var cost = FromDollarsValue(_numCost);
        var discount = FromDollarsValue(_numLineDiscount);
        var gross = checked(quantity * cost);
        if (discount > gross)
        {
            MessageBox.Show("El descuento de linea no puede superar el subtotal.", "Compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _lines.Add(new PurchaseLineEditRow(
            product.Id,
            product.Code,
            product.ProductName,
            quantity,
            cost,
            discount,
            gross - discount,
            _txtLineNotes.Text.Trim(),
            product.SkuId,
            product.Variant));
        _txtLineNotes.Clear();
        _numQuantity.Value = 1;
        _numLineDiscount.Value = 0;
        RefreshLineGrid();
    }

    private void RemoveSelectedLine()
    {
        if (_linesBinding.Current is not PurchaseLineGridRow row)
        {
            return;
        }

        _lines = _lines.Where(l => row.ProductoSkuId.HasValue
                ? l.ProductoSkuId != row.ProductoSkuId
                : l.ProductoId != row.ProductoId || l.ProductoSkuId.HasValue)
            .ToList();
        RefreshLineGrid();
    }

    private async Task SaveAsync()
    {
        if (_cmbSupplier.SelectedItem is not SupplierOption supplier)
        {
            MessageBox.Show("Seleccione un proveedor activo.", "Compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_lines.Count == 0)
        {
            MessageBox.Show("Agregue al menos un producto.", "Compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _btnSave.Enabled = false;
            var lines = _lines
                .Select(l => new PurchaseLineRequest(
                    l.ProductoId,
                    l.Cantidad,
                    l.CostoUnitarioCentavos,
                    l.DescuentoLineaCentavos,
                    string.IsNullOrWhiteSpace(l.Notas) ? null : l.Notas,
                    l.ProductoSkuId))
                .ToList();
            var purchaseDiscount = FromDollarsValue(_numPurchaseDiscount);

            if (_existing is null)
            {
                SavedPurchase = await _purchaseService.CreateAsync(new CreatePurchaseRequest(
                    supplier.Id,
                    _dtpPurchaseDate.Value.Date,
                    lines,
                    purchaseDiscount,
                    _txtObservations.Text.Trim()));
                MessageBox.Show("Compra registrada.", "Compra", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                SavedPurchase = await _purchaseService.UpdateAsync(new UpdatePurchaseRequest(
                    _existing.CompraId,
                    supplier.Id,
                    _dtpPurchaseDate.Value.Date,
                    lines,
                    purchaseDiscount,
                    _txtObservations.Text.Trim()));
                MessageBox.Show("Compra actualizada.", "Compra", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }

    private void RefreshLineGrid()
    {
        _linesBinding.DataSource = _lines.Select(PurchaseLineGridRow.FromEditRow).ToList();
        UpdateTotals();
        UpdateActionState();
    }

    private void UpdateTotals()
    {
        var subtotal = _lines.Sum(l => l.SubtotalCentavos);
        var discount = FromDollarsValue(_numPurchaseDiscount);
        if (discount > subtotal)
        {
            _numPurchaseDiscount.Value = ToDollarsValue(subtotal);
            discount = subtotal;
        }

        _lblSubtotal.Text = $"Subtotal: {SaleReceiptViewModel.FormatDollars(subtotal)}";
        _lblTotal.Text = $"Total: {SaleReceiptViewModel.FormatDollars(subtotal - discount)}";
    }

    private void UpdateActionState()
    {
        _btnRemoveLine.Enabled = _linesBinding.Current is PurchaseLineGridRow;
        _btnSave.Enabled = _lines.Count > 0;
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;

        AddGridColumn("Codigo", "Codigo", 115, 10);
        AddGridColumn("Producto", "Producto", 280, 35);
        AddGridColumn("Variante", "Variante / SKU", 130, 14);
        AddGridColumn("Cantidad", "Cantidad", 90, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Costo", "Costo unitario", 120, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("Descuento", "Descuento", 110, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("Subtotal", "Subtotal", 120, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("Notas", "Notas", 180, 20);
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
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int column, int row)
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
        layout.Controls.Add(label, column, row);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private static void ConfigureMoney(NumericUpDown numeric)
    {
        numeric.DecimalPlaces = 2;
        numeric.Minimum = 0;
        numeric.Maximum = 9999999;
        numeric.Increment = 0.25m;
        numeric.ThousandsSeparator = true;
        numeric.Dock = DockStyle.Fill;
        numeric.Font = UITheme.NormalFont;
    }

    private static void ConfigureTotalLabel(Label label, string text)
    {
        label.Text = text;
        label.AutoSize = true;
        label.Font = UITheme.SubheaderFont;
        label.ForeColor = UITheme.PrimaryColor;
        label.Margin = new Padding(0, 8, 24, 0);
    }

    private static void SelectComboValue(ComboBox combo, int id)
    {
        combo.SelectedValue = id;
    }

    private static long FromDollarsValue(NumericUpDown numeric)
    {
        return (long)Math.Round(numeric.Value * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static decimal ToDollarsValue(long centavos)
    {
        return centavos / 100m;
    }

    private sealed record SupplierOption(int Id, string Name);

    private sealed record PurchaseLineEditRow(
        int ProductoId,
        string Codigo,
        string Producto,
        int Cantidad,
        long CostoUnitarioCentavos,
        long DescuentoLineaCentavos,
        long SubtotalCentavos,
        string Notas,
        int? ProductoSkuId,
        string? Variante);

    private sealed record PurchaseLineGridRow(
        int ProductoId,
        int? ProductoSkuId,
        string Codigo,
        string Producto,
        string? Variante,
        int Cantidad,
        string Costo,
        string Descuento,
        string Subtotal,
        string Notas)
    {
        public static PurchaseLineGridRow FromEditRow(PurchaseLineEditRow row)
        {
            return new PurchaseLineGridRow(
                row.ProductoId,
                row.ProductoSkuId,
                row.Codigo,
                row.Producto,
                row.Variante,
                row.Cantidad,
                SaleReceiptViewModel.FormatDollars(row.CostoUnitarioCentavos),
                SaleReceiptViewModel.FormatDollars(row.DescuentoLineaCentavos),
                SaleReceiptViewModel.FormatDollars(row.SubtotalCentavos),
                row.Notas);
        }
    }
}
