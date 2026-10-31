using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class CustomOrderEditForm : Form
{
    private readonly ICustomOrderService _customOrderService;
    private readonly ICustomerQueryService _customerQueryService;
    private readonly IPosCatalogService _catalogService;
    private readonly CustomOrderDetail? _existing;
    private readonly BindingSource _lineBinding = new();
    private readonly ComboBox _cmbCustomer = new();
    private readonly TextBox _txtRecipient = new();
    private readonly TextBox _txtPhone = new();
    private readonly TextBox _txtEmail = new();
    private readonly TextBox _txtAddress = new();
    private readonly DateTimePicker _dtpDate = new();
    private readonly DateTimePicker _dtpPromise = new();
    private readonly ComboBox _cmbPriority = new();
    private readonly NumericUpDown _numGeneralDiscount = new();
    private readonly TextBox _txtDescription = new();
    private readonly TextBox _txtGeneralSpecs = new();
    private readonly TextBox _txtInternalNotes = new();
    private readonly TextBox _txtCustomerNotes = new();
    private readonly Label _lblSelectedProduct = new();
    private readonly Button _btnSearchProduct = new();
    private readonly TextBox _txtLineDescription = new();
    private readonly NumericUpDown _numQuantity = new();
    private readonly NumericUpDown _numPrice = new();
    private readonly NumericUpDown _numLineDiscount = new();
    private readonly TextBox _txtLineSpecs = new();
    private readonly TextBox _txtColor = new();
    private readonly TextBox _txtMeasurements = new();
    private readonly TextBox _txtMaterial = new();
    private readonly TextBox _txtCustomText = new();
    private readonly TextBox _txtLineNotes = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSubtotal = new();
    private readonly Label _lblTotal = new();
    private readonly Button _btnRemove = new();
    private List<LineRow> _lines = [];
    private List<ProductOption> _products = [];
    private ProductOption? _selectedProduct;

    public CustomOrderDetail? SavedOrder { get; private set; }

    public CustomOrderEditForm(
        ICustomOrderService customOrderService,
        ICustomerQueryService customerQueryService,
        IPosCatalogService catalogService,
        CustomOrderDetail? existing = null)
    {
        _customOrderService = customOrderService;
        _customerQueryService = customerQueryService;
        _catalogService = catalogService;
        _existing = existing;
        BuildUi();
        Load += CustomOrderEditForm_Load;
    }

    private async void CustomOrderEditForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await LoadCatalogsAsync();
            LoadExisting();
            RefreshLines();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Pedido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = _existing is null ? "Nuevo pedido personalizado" : $"Editar {_existing.CodigoPedido}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1280, 840);
        MinimumSize = new Size(1060, 720);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 245));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 195));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildGeneralData(), 0, 1);
        root.Controls.Add(BuildLineEditor(), 0, 2);
        root.Controls.Add(BuildGrid(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 8, 16, 8),
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = _existing is null ? "Registrar pedido" : $"Editar {_existing.CodigoPedido}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "El pedido no afecta inventario, caja, ventas ni cuentas por cobrar.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalBoldFont
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildGeneralData()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 10, 0, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddLabel(layout, "Cliente opcional", 0, 0);
        AddLabel(layout, "Destinatario", 1, 0);
        AddLabel(layout, "Telefono", 2, 0);
        AddLabel(layout, "Fecha", 3, 0);
        AddLabel(layout, "Fecha prometida", 4, 0);
        AddLabel(layout, "Prioridad", 5, 0);
        AddLabel(layout, "Descuento general ($)", 6, 0);
        AddLabel(layout, "Correo", 0, 2);
        AddLabel(layout, "Direccion", 1, 2);
        AddLabel(layout, "Descripcion general", 2, 2);
        AddLabel(layout, "Especificaciones generales", 3, 2);
        AddLabel(layout, "Notas para cliente", 5, 2);
        AddLabel(layout, "Observaciones internas", 6, 2);

        ConfigureCombo(_cmbCustomer);
        _cmbCustomer.SelectedIndexChanged += async (_, _) => await LoadSelectedCustomerAsync();
        ConfigureText(_txtRecipient);
        ConfigureText(_txtPhone);
        ConfigureText(_txtEmail);
        ConfigureText(_txtAddress, multiline: true);
        ConfigureDate(_dtpDate, DateTime.Today);
        ConfigureDate(_dtpPromise, DateTime.Today.AddDays(15));
        _cmbPriority.Dock = DockStyle.Fill;
        _cmbPriority.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbPriority.DataSource = Enum.GetValues<PrioridadPedido>();
        ConfigureMoney(_numGeneralDiscount);
        _numGeneralDiscount.ValueChanged += (_, _) => UpdateTotals();
        ConfigureText(_txtDescription, multiline: true);
        ConfigureText(_txtGeneralSpecs, multiline: true);
        ConfigureText(_txtCustomerNotes, multiline: true);
        ConfigureText(_txtInternalNotes, multiline: true);

        layout.Controls.Add(_cmbCustomer, 0, 1);
        layout.Controls.Add(_txtRecipient, 1, 1);
        layout.Controls.Add(_txtPhone, 2, 1);
        layout.Controls.Add(_dtpDate, 3, 1);
        layout.Controls.Add(_dtpPromise, 4, 1);
        layout.Controls.Add(_cmbPriority, 5, 1);
        layout.Controls.Add(_numGeneralDiscount, 6, 1);
        layout.Controls.Add(_txtEmail, 0, 3);
        layout.Controls.Add(_txtAddress, 1, 3);
        layout.Controls.Add(_txtDescription, 2, 3);
        layout.Controls.Add(_txtGeneralSpecs, 3, 3);
        layout.SetColumnSpan(_txtGeneralSpecs, 2);
        layout.Controls.Add(_txtCustomerNotes, 5, 3);
        layout.Controls.Add(_txtInternalNotes, 6, 3);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildLineEditor()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        AddLabel(layout, "Producto opcional", 0, 0);
        AddLabel(layout, "Descripcion", 1, 0);
        AddLabel(layout, "Cantidad", 2, 0);
        AddLabel(layout, "Precio ($)", 3, 0);
        AddLabel(layout, "Descuento ($)", 4, 0);
        AddLabel(layout, "Especificaciones", 0, 2);
        AddLabel(layout, "Color", 1, 2);
        AddLabel(layout, "Medidas", 2, 2);
        AddLabel(layout, "Material", 3, 2);
        AddLabel(layout, "Texto personalizado", 4, 2);
        AddLabel(layout, "Observaciones", 5, 2);
        ConfigureText(_txtLineDescription);
        _numQuantity.Minimum = 1;
        _numQuantity.Maximum = 100000;
        _numQuantity.Value = 1;
        _numQuantity.Dock = DockStyle.Fill;
        ConfigureMoney(_numPrice);
        ConfigureMoney(_numLineDiscount);
        ConfigureText(_txtLineSpecs);
        ConfigureText(_txtColor);
        ConfigureText(_txtMeasurements);
        ConfigureText(_txtMaterial);
        ConfigureText(_txtCustomText);
        ConfigureText(_txtLineNotes);
        var add = CreateButton("Agregar linea", UITheme.StylePrimaryButton, (_, _) => AddLine());
        _btnRemove.Text = "Quitar linea";
        _btnRemove.Dock = DockStyle.Fill;
        _btnRemove.Click += (_, _) => RemoveLine();
        UITheme.StyleDangerButton(_btnRemove);
        layout.Controls.Add(BuildProductSelector(), 0, 1);
        layout.Controls.Add(_txtLineDescription, 1, 1);
        layout.Controls.Add(_numQuantity, 2, 1);
        layout.Controls.Add(_numPrice, 3, 1);
        layout.Controls.Add(_numLineDiscount, 4, 1);
        layout.Controls.Add(add, 6, 1);
        layout.Controls.Add(_btnRemove, 7, 1);
        layout.Controls.Add(_txtLineSpecs, 0, 3);
        layout.Controls.Add(_txtColor, 1, 3);
        layout.Controls.Add(_txtMeasurements, 2, 3);
        layout.Controls.Add(_txtMaterial, 3, 3);
        layout.Controls.Add(_txtCustomText, 4, 3);
        layout.Controls.Add(_txtLineNotes, 5, 3);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildGrid()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(10) };
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        AddColumn("Codigo", "Codigo", 78, 10);
        AddColumn("Descripcion", "Descripcion", 170, 30);
        AddColumn("Cantidad", "Cantidad", 65, 8);
        AddColumn("Precio", "Precio", 82, 10);
        AddColumn("Descuento", "Descuento", 82, 10);
        AddColumn("Subtotal", "Subtotal", 90, 12);
        AddColumn("Especificaciones", "Especificaciones", 120, 16);
        AddColumn("Personalizacion", "Personalizacion", 120, 16);
        AddColumn("Observaciones", "Observaciones", 130, 20);
        _grid.DataSource = _lineBinding;
        _grid.SelectionChanged += (_, _) => _btnRemove.Enabled = _lineBinding.Current is LineRow;
        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildActions()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        var totals = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        ConfigureTotal(_lblSubtotal);
        ConfigureTotal(_lblTotal);
        totals.Controls.AddRange([_lblSubtotal, _lblTotal]);
        var actions = new FlowLayoutPanel
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 5)
        };
        actions.Controls.Add(CreateButton("Cancelar", UITheme.StyleSecondaryButton, (_, _) => Close(), 126));
        actions.Controls.Add(CreateButton("Guardar y confirmar", UITheme.StyleSuccessButton, async (_, _) => await SaveAsync(true), 202));
        actions.Controls.Add(CreateButton("Guardar borrador", UITheme.StylePrimaryButton, async (_, _) => await SaveAsync(false), 174));
        layout.Controls.Add(totals, 0, 0);
        layout.Controls.Add(actions, 1, 0);
        return layout;
    }

    private async Task LoadCatalogsAsync()
    {
        var customers = await _customerQueryService.SearchAsync(new CustomerFilter(Estado: EstadoCliente.ACTIVO, PageSize: 500));
        _cmbCustomer.DataSource = new[] { new CustomerOption(null, "(Destinatario sin cliente)") }
            .Concat(customers.OrderBy(c => c.NombreCompleto).Select(c => new CustomerOption(c.ClienteId, $"{c.CodigoCliente} - {c.NombreCompleto}")))
            .ToList();
        var products = await _catalogService.SearchAsync(new PosSkuCatalogFilter(
            Estado: EstadoProducto.ACTIVO, SkuActivo: true));
        _products = new[] { new ProductOption(null, null, "(Linea descriptiva)", null, null, 0, null) }
            .Concat(products.OrderBy(p => p.ProductoNombre).ThenBy(p => p.CodigoSku).Select(p => new ProductOption(
                p.ProductoId,
                p.ProductoSkuId,
                $"{p.CodigoSku} - {p.ProductoNombre}{(string.IsNullOrWhiteSpace(p.VarianteNombre) ? string.Empty : $" / {p.VarianteNombre}")}",
                p.CodigoSku,
                p.ProductoNombre,
                p.PrecioVentaEfectivoCentavos,
                p.VarianteNombre,
                p.CategoriaNombre,
                p.SubcategoriaNombre)))
            .ToList();
        ApplySelectedProduct(_products[0], focusQuantity: false);
    }

    private void LoadExisting()
    {
        if (_existing is null) return;
        if (_existing.ClienteId.HasValue)
        {
            _cmbCustomer.SelectedValue = _existing.ClienteId.Value;
        }
        else
        {
            _cmbCustomer.SelectedIndex = 0;
        }
        _txtRecipient.Text = _existing.Destinatario;
        _txtPhone.Text = _existing.Telefono ?? string.Empty;
        _txtEmail.Text = _existing.Correo ?? string.Empty;
        _txtAddress.Text = _existing.Direccion ?? string.Empty;
        _dtpDate.Value = _existing.FechaPedido;
        _dtpPromise.Value = _existing.FechaPrometida;
        _cmbPriority.SelectedItem = _existing.Prioridad;
        _numGeneralDiscount.Value = ToDollars(_existing.DescuentoGeneralCentavos);
        _txtDescription.Text = _existing.DescripcionGeneral ?? string.Empty;
        _txtGeneralSpecs.Text = _existing.EspecificacionesGenerales ?? string.Empty;
        _txtCustomerNotes.Text = _existing.NotasCliente ?? string.Empty;
        _txtInternalNotes.Text = _existing.ObservacionesInternas ?? string.Empty;
        _lines = _existing.Detalles.Select(d => new LineRow(
            d.ProductoId, d.ProductoSkuId, d.CodigoSku ?? d.Codigo, d.Variante, d.Descripcion, d.Cantidad, d.PrecioUnitarioCentavos,
            d.DescuentoLineaCentavos, d.SubtotalCentavos, d.Especificaciones, d.Color,
            d.Medidas, d.Material, d.TextoPersonalizado, d.Observaciones)).ToList();
    }

    private async Task LoadSelectedCustomerAsync()
    {
        if (_cmbCustomer.SelectedValue is not int id) return;
        var customer = await _customerQueryService.GetByIdAsync(id);
        if (customer is null) return;
        _txtRecipient.Text = customer.NombreCompleto;
        _txtPhone.Text = customer.TelefonoCelular ?? string.Empty;
        _txtEmail.Text = customer.CorreoElectronico ?? string.Empty;
        _txtAddress.Text = customer.Direccion ?? string.Empty;
    }

    private void ApplySelectedProduct(ProductOption product, bool focusQuantity = true)
    {
        _selectedProduct = product;
        _lblSelectedProduct.Text = product.Name;
        _lblSelectedProduct.Tag = product.Name;
        _txtLineDescription.Text = product.NameSnapshot ?? string.Empty;
        _numPrice.Value = ToDollars(product.PriceCents);
        if (focusQuantity)
        {
            _numQuantity.Focus();
            _numQuantity.Select(0, _numQuantity.Text.Length);
        }
    }

    private Control BuildProductSelector()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _lblSelectedProduct.Name = "SelectedCustomOrderProductLabel";
        _lblSelectedProduct.Dock = DockStyle.Fill;
        _lblSelectedProduct.AutoEllipsis = true;
        _lblSelectedProduct.TextAlign = ContentAlignment.MiddleLeft;
        _lblSelectedProduct.Padding = new Padding(6, 0, 4, 0);
        _lblSelectedProduct.BorderStyle = BorderStyle.FixedSingle;
        _lblSelectedProduct.BackColor = Color.White;
        _btnSearchProduct.Name = "CustomOrderProductSearchButton";
        _btnSearchProduct.Text = "Buscar";
        _btnSearchProduct.Dock = DockStyle.Fill;
        _btnSearchProduct.Margin = new Padding(6, 0, 0, 0);
        UITheme.StyleSecondaryButton(_btnSearchProduct);
        _btnSearchProduct.Click += (_, _) => SelectProduct();
        var tooltip = new ToolTip();
        tooltip.SetToolTip(_lblSelectedProduct, "Producto seleccionado. Use Buscar para cambiarlo.");
        layout.Controls.Add(_lblSelectedProduct, 0, 0);
        layout.Controls.Add(_btnSearchProduct, 1, 0);
        return layout;
    }

    private void SelectProduct()
    {
        var options = _products
            .Select(product => new QuotationProductOption(
                product.Id,
                product.Name,
                product.Code,
                product.NameSnapshot,
                product.PriceCents,
                product.Category,
                product.Subcategory,
                product.SkuId))
            .ToList();
        using var picker = new QuotationProductPickerForm(options);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedProduct is not { } selected)
            return;

        var product = _products.FirstOrDefault(candidate =>
            candidate.Id == selected.Id && candidate.SkuId == selected.SkuId)
            ?? _products.FirstOrDefault(candidate => candidate.Id == selected.Id);
        if (product is not null)
            ApplySelectedProduct(product);
    }

    private void AddLine()
    {
        try
        {
            var product = _selectedProduct;
            var description = _txtLineDescription.Text.Trim();
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("La descripcion es obligatoria.");
            var quantity = Decimal.ToInt32(_numQuantity.Value);
            var price = ToCents(_numPrice.Value);
            var discount = ToCents(_numLineDiscount.Value);
            var gross = checked(quantity * price);
            if (discount > gross) throw new ArgumentException("El descuento supera el importe de la linea.");
            _lines.Add(new LineRow(
                product?.Id, product?.SkuId, product?.Code, product?.Variant, description, quantity, price, discount, gross - discount,
                Normalize(_txtLineSpecs.Text), Normalize(_txtColor.Text), Normalize(_txtMeasurements.Text),
                Normalize(_txtMaterial.Text), Normalize(_txtCustomText.Text), Normalize(_txtLineNotes.Text)));
            RefreshLines();
            _txtLineSpecs.Clear();
            _txtColor.Clear();
            _txtMeasurements.Clear();
            _txtMaterial.Clear();
            _txtCustomText.Clear();
            _txtLineNotes.Clear();
            _numQuantity.Value = 1;
            _numLineDiscount.Value = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Linea de pedido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveLine()
    {
        if (_lineBinding.Current is not LineRow row) return;
        _lines.Remove(row);
        RefreshLines();
    }

    private void RefreshLines()
    {
        _lineBinding.DataSource = _lines.ToList();
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        var subtotal = _lines.Sum(l => l.SubtotalCentavos);
        var discount = ToCents(_numGeneralDiscount.Value);
        _lblSubtotal.Text = $"Subtotal: {Money(subtotal)}";
        _lblTotal.Text = $"Total: {Money(Math.Max(0, subtotal - discount))}";
    }

    private async Task SaveAsync(bool confirm)
    {
        try
        {
            var request = new CustomOrderSaveRequest(
                _existing?.PedidoPersonalizadoId,
                _cmbCustomer.SelectedValue as int?,
                _txtRecipient.Text,
                _txtPhone.Text,
                _txtEmail.Text,
                _txtAddress.Text,
                _dtpDate.Value.Date,
                _dtpPromise.Value.Date,
                (PrioridadPedido)_cmbPriority.SelectedItem!,
                ToCents(_numGeneralDiscount.Value),
                _txtDescription.Text,
                _txtGeneralSpecs.Text,
                _txtInternalNotes.Text,
                _txtCustomerNotes.Text,
                _lines.Select(l => new CustomOrderLineRequest(
                    l.ProductoId, l.Descripcion, l.Cantidad, l.PrecioUnitarioCentavos,
                    l.DescuentoLineaCentavos, l.Especificaciones, l.Color, l.Medidas,
                    l.Material, l.TextoPersonalizado, l.Observaciones, l.ProductoSkuId)).ToList());
            SavedOrder = _existing is null
                ? await _customOrderService.CreateDraftAsync(request)
                : await _customOrderService.UpdateDraftAsync(request);
            if (confirm)
            {
                SavedOrder = await _customOrderService.ConfirmAsync(SavedOrder.PedidoPersonalizadoId);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Guardar pedido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddColumn(string property, string header, int minWidth, float weight) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minWidth,
            FillWeight = weight
        });
    private static void ConfigureCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.DisplayMember = "Name";
        combo.ValueMember = "Id";
    }
    private static void ConfigureText(TextBox box, bool multiline = false)
    {
        box.Dock = DockStyle.Fill;
        box.Multiline = multiline;
        UITheme.StyleTextBox(box);
    }
    private static void ConfigureDate(DateTimePicker picker, DateTime value)
    {
        picker.Dock = DockStyle.Fill;
        picker.Format = DateTimePickerFormat.Short;
        picker.Value = value;
    }
    private static void ConfigureMoney(NumericUpDown input)
    {
        input.Dock = DockStyle.Fill;
        input.DecimalPlaces = 2;
        input.Maximum = 100000000;
        input.ThousandsSeparator = true;
    }
    private static Button CreateButton(string text, Action<Button> style, EventHandler click, int width = 130)
    {
        var button = new Button { Text = text, Size = new Size(width, 42), Margin = new Padding(8, 4, 0, 0) };
        style(button);
        button.Click += click;
        return button;
    }
    private static void ConfigureTotal(Label label)
    {
        label.AutoSize = true;
        label.Font = UITheme.SubheaderFont;
        label.ForeColor = UITheme.TextDark;
        label.Margin = new Padding(0, 14, 24, 0);
    }
    private static void AddLabel(TableLayoutPanel layout, string text, int column, int row) =>
        layout.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, column, row);
    private static long ToCents(decimal dollars) => checked((long)decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
    private static decimal ToDollars(long cents) => cents / 100m;
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private static string? Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private sealed record CustomerOption(int? Id, string Name);
    private sealed record ProductOption(
        int? Id,
        int? SkuId,
        string Name,
        string? Code,
        string? NameSnapshot,
        long PriceCents,
        string? Variant,
        string? Category = null,
        string? Subcategory = null);
    private sealed record LineRow(
        int? ProductoId,
        int? ProductoSkuId,
        string? Codigo,
        string? Variante,
        string Descripcion,
        int Cantidad,
        long PrecioUnitarioCentavos,
        long DescuentoLineaCentavos,
        long SubtotalCentavos,
        string? Especificaciones,
        string? Color,
        string? Medidas,
        string? Material,
        string? TextoPersonalizado,
        string? Observaciones)
    {
        public string Precio => Money(PrecioUnitarioCentavos);
        public string Descuento => Money(DescuentoLineaCentavos);
        public string Subtotal => Money(SubtotalCentavos);
        public string Personalizacion => string.Join(" | ", new[] { Color, Medidas, Material, TextoPersonalizado }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
