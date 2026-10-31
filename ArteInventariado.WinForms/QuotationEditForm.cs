using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class QuotationEditForm : Form
{
    private readonly IQuotationService _quotationService;
    private readonly ICustomerQueryService _customerQueryService;
    private readonly IProductService _productService;
    private readonly QuotationDetail? _existing;
    private readonly BindingSource _lineBinding = new();
    private readonly ComboBox _cmbCustomer = new();
    private readonly TextBox _txtRecipient = new();
    private readonly TextBox _txtPhone = new();
    private readonly TextBox _txtEmail = new();
    private readonly TextBox _txtAddress = new();
    private readonly DateTimePicker _dtpDate = new();
    private readonly DateTimePicker _dtpExpiry = new();
    private readonly NumericUpDown _numGeneralDiscount = new();
    private readonly TextBox _txtConditions = new();
    private readonly TextBox _txtObservations = new();
    private readonly Label _lblSelectedProduct = new();
    private readonly TextBox _txtLineDescription = new();
    private readonly NumericUpDown _numQuantity = new();
    private readonly NumericUpDown _numPrice = new();
    private readonly NumericUpDown _numLineDiscount = new();
    private readonly TextBox _txtLineNotes = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSubtotal = new();
    private readonly Label _lblTotal = new();
    private readonly Button _btnRemove = new();
    private List<LineRow> _lines = [];
    private List<QuotationProductOption> _products = [];
    private QuotationProductOption? _selectedProduct;

    public QuotationDetail? SavedQuotation { get; private set; }

    public QuotationEditForm(
        IQuotationService quotationService,
        ICustomerQueryService customerQueryService,
        IProductService productService,
        QuotationDetail? existing = null)
    {
        _quotationService = quotationService;
        _customerQueryService = customerQueryService;
        _productService = productService;
        _existing = existing;
        BuildUi();
        Load += QuotationEditForm_Load;
    }

    private async void QuotationEditForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await LoadCatalogsAsync();
            LoadExisting();
            RefreshLines();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cotizacion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = _existing is null ? "Nueva cotizacion" : $"Editar {_existing.CodigoCotizacion}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1320, 860);
        MinimumSize = new Size(1100, 720);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = _existing is null ? "Registrar cotizacion" : $"Editar {_existing.CodigoCotizacion}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "La cotizacion no afecta inventario, caja ni cuentas.",
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
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddLabel(layout, "Cliente opcional", 0, 0);
        AddLabel(layout, "Destinatario", 1, 0);
        AddLabel(layout, "Telefono", 2, 0);
        AddLabel(layout, "Fecha", 3, 0);
        AddLabel(layout, "Vencimiento", 4, 0);
        AddLabel(layout, "Descuento general ($)", 5, 0);
        AddLabel(layout, "Correo", 0, 2);
        AddLabel(layout, "Direccion", 1, 2);
        AddLabel(layout, "Condiciones comerciales", 3, 2);
        AddLabel(layout, "Observaciones", 5, 2);

        ConfigureCombo(_cmbCustomer);
        _cmbCustomer.SelectedIndexChanged += async (_, _) => await LoadSelectedCustomerAsync();
        ConfigureText(_txtRecipient);
        ConfigureText(_txtPhone);
        ConfigureText(_txtEmail);
        ConfigureText(_txtAddress, multiline: true);
        ConfigureDate(_dtpDate, DateTime.Today);
        ConfigureDate(_dtpExpiry, DateTime.Today.AddDays(15));
        ConfigureMoney(_numGeneralDiscount);
        _numGeneralDiscount.ValueChanged += (_, _) => UpdateTotals();
        ConfigureText(_txtConditions, multiline: true);
        ConfigureText(_txtObservations, multiline: true);

        layout.Controls.Add(_cmbCustomer, 0, 1);
        layout.Controls.Add(_txtRecipient, 1, 1);
        layout.Controls.Add(_txtPhone, 2, 1);
        layout.Controls.Add(_dtpDate, 3, 1);
        layout.Controls.Add(_dtpExpiry, 4, 1);
        layout.Controls.Add(_numGeneralDiscount, 5, 1);
        layout.Controls.Add(_txtEmail, 0, 3);
        layout.Controls.Add(_txtAddress, 1, 3);
        layout.SetColumnSpan(_txtAddress, 2);
        layout.Controls.Add(_txtConditions, 3, 3);
        layout.SetColumnSpan(_txtConditions, 2);
        layout.Controls.Add(_txtObservations, 5, 3);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildLineEditor()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 2 };
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
        AddLabel(layout, "Nota", 5, 0);
        ConfigureText(_txtLineDescription);
        _numQuantity.Minimum = 1;
        _numQuantity.Maximum = 100000;
        _numQuantity.Value = 1;
        _numQuantity.Dock = DockStyle.Fill;
        ConfigureMoney(_numPrice);
        ConfigureMoney(_numLineDiscount);
        ConfigureText(_txtLineNotes);
        var add = CreateButton("Agregar linea", UITheme.StylePrimaryButton, (_, _) => AddLine(), width: 108);
        add.Name = "AddQuotationLineButton";
        add.Height = 38;
        add.Margin = Padding.Empty;
        add.Anchor = AnchorStyles.None;
        add.TextAlign = ContentAlignment.MiddleCenter;
        _btnRemove.Text = "Quitar linea";
        _btnRemove.Dock = DockStyle.Fill;
        _btnRemove.Click += (_, _) => RemoveLine();
        UITheme.StyleDangerButton(_btnRemove);
        layout.Controls.Add(BuildProductSelector(), 0, 1);
        layout.Controls.Add(_txtLineDescription, 1, 1);
        layout.Controls.Add(_numQuantity, 2, 1);
        layout.Controls.Add(_numPrice, 3, 1);
        layout.Controls.Add(_numLineDiscount, 4, 1);
        layout.Controls.Add(_txtLineNotes, 5, 1);
        layout.Controls.Add(add, 6, 1);
        layout.Controls.Add(_btnRemove, 7, 1);
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
        AddColumn("Codigo", "Codigo", 90, 10);
        AddColumn("Descripcion", "Descripcion", 240, 30);
        AddColumn("Cantidad", "Cantidad", 80, 8);
        AddColumn("Precio", "Precio", 100, 10);
        AddColumn("Descuento", "Descuento", 100, 10);
        AddColumn("Subtotal", "Subtotal", 110, 12);
        AddColumn("Observaciones", "Observaciones", 180, 20);
        _grid.DataSource = _lineBinding;
        _grid.SelectionChanged += (_, _) => _btnRemove.Enabled = _lineBinding.Current is LineRow;
        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildActions()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600));
        var totals = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        ConfigureTotal(_lblSubtotal);
        ConfigureTotal(_lblTotal);
        totals.Controls.AddRange([_lblSubtotal, _lblTotal]);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 5, 0, 5) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        var saveDraft = CreateDockedButton("Guardar borrador", UITheme.StylePrimaryButton, async (_, _) => await SaveAsync(false));
        var saveAndIssue = CreateDockedButton("Guardar y emitir", UITheme.StyleSuccessButton, async (_, _) => await SaveAsync(true));
        var cancel = CreateDockedButton("Cancelar", UITheme.StyleSecondaryButton, (_, _) => Close());
        foreach (var button in new[] { saveDraft, saveAndIssue, cancel })
        {
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = new Padding(0, 0, 0, 8);
        }
        actions.Controls.Add(saveDraft, 0, 0);
        actions.Controls.Add(saveAndIssue, 1, 0);
        actions.Controls.Add(cancel, 2, 0);
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
        var products = await _productService.GetAllAsync(new ProductFilter(Estado: EstadoProducto.ACTIVO));
        _products = new[] { new QuotationProductOption(null, "(Linea descriptiva)", null, null, 0) }
            .Concat(products.OrderBy(p => p.Nombre).Select(p => new QuotationProductOption(
                p.ProductoId,
                $"{p.CodigoInterno} - {p.Nombre}",
                p.CodigoInterno,
                p.Nombre,
                p.PrecioVentaCentavos,
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
        _dtpDate.Value = _existing.Fecha;
        _dtpExpiry.Value = _existing.FechaVencimiento;
        _numGeneralDiscount.Value = ToDollars(_existing.DescuentoGeneralCentavos);
        _txtConditions.Text = _existing.CondicionesComerciales ?? string.Empty;
        _txtObservations.Text = _existing.Observaciones ?? string.Empty;
        _lines = _existing.Detalles.Select(d => new LineRow(
            d.ProductoId, d.Codigo, d.Descripcion, d.Cantidad, d.PrecioUnitarioCentavos,
            d.DescuentoLineaCentavos, d.SubtotalCentavos, d.Observaciones)).ToList();
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

    internal void ApplySelectedProduct(QuotationProductOption product, bool focusQuantity = true)
    {
        _selectedProduct = product;
        _lblSelectedProduct.Text = product.Id.HasValue
            ? $"{product.Name} · {product.DisplayCategory} · {product.DisplaySubcategory}"
            : product.Name;
        _lblSelectedProduct.Tag = product.Name;
        _txtLineDescription.Text = product.NameSnapshot ?? string.Empty;
        _numPrice.Value = ToDollars(product.PriceCents);
        if (focusQuantity)
        {
            _numQuantity.Focus();
            _numQuantity.Select(0, _numQuantity.Text.Length);
        }
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
            _lines.Add(new LineRow(product?.Id, product?.Code, description, quantity, price, discount, gross - discount, Normalize(_txtLineNotes.Text)));
            RefreshLines();
            _txtLineNotes.Clear();
            _numQuantity.Value = 1;
            _numLineDiscount.Value = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Linea de cotizacion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private async Task SaveAsync(bool emit)
    {
        try
        {
            var request = new QuotationSaveRequest(
                _existing?.CotizacionId,
                _cmbCustomer.SelectedValue as int?,
                _txtRecipient.Text,
                _txtPhone.Text,
                _txtEmail.Text,
                _txtAddress.Text,
                _dtpDate.Value.Date,
                _dtpExpiry.Value.Date,
                ToCents(_numGeneralDiscount.Value),
                _txtConditions.Text,
                _txtObservations.Text,
                _lines.Select(l => new QuotationLineRequest(
                    l.ProductoId, l.Descripcion, l.Cantidad, l.PrecioUnitarioCentavos,
                    l.DescuentoLineaCentavos, l.Observaciones)).ToList());
            SavedQuotation = _existing is null
                ? await _quotationService.CreateDraftAsync(request)
                : await _quotationService.UpdateDraftAsync(request);
            if (emit)
            {
                SavedQuotation = await _quotationService.EmitAsync(SavedQuotation.CotizacionId);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Guardar cotizacion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
    private static Button CreateDockedButton(string text, Action<Button> style, EventHandler click)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(5, 0, 5, 0) };
        style(button);
        button.Click += click;
        return button;
    }
    private Control BuildProductSelector()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _lblSelectedProduct.Name = "SelectedQuotationProductLabel";
        _lblSelectedProduct.Dock = DockStyle.Fill;
        _lblSelectedProduct.AutoEllipsis = true;
        _lblSelectedProduct.TextAlign = ContentAlignment.MiddleLeft;
        _lblSelectedProduct.Padding = new Padding(6, 0, 4, 0);
        _lblSelectedProduct.BorderStyle = BorderStyle.FixedSingle;
        _lblSelectedProduct.BackColor = Color.White;
        var search = new Button { Text = "Buscar", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        search.Name = "QuotationProductSearchButton";
        UITheme.StyleSecondaryButton(search);
        search.Click += (_, _) => SelectProduct();
        var tooltip = new ToolTip();
        tooltip.SetToolTip(_lblSelectedProduct, "Producto seleccionado. Use Buscar para cambiarlo.");
        layout.Controls.Add(_lblSelectedProduct, 0, 0);
        layout.Controls.Add(search, 1, 0);
        return layout;
    }
    private void SelectProduct()
    {
        using var form = new QuotationProductPickerForm(_products);
        if (form.ShowDialog(this) != DialogResult.OK || form.SelectedProduct is null) return;
        ApplySelectedProduct(form.SelectedProduct);
    }
    internal QuotationProductOption? SelectedProduct => _selectedProduct;
    internal int LineCount => _lines.Count;
    internal int ProductOptionCount => _products.Count;
    internal string CurrentLineDescription => _txtLineDescription.Text;
    internal decimal CurrentLinePrice => _numPrice.Value;
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
    private sealed record LineRow(
        int? ProductoId,
        string? Codigo,
        string Descripcion,
        int Cantidad,
        long PrecioUnitarioCentavos,
        long DescuentoLineaCentavos,
        long SubtotalCentavos,
        string? Observaciones)
    {
        public string Precio => Money(PrecioUnitarioCentavos);
        public string Descuento => Money(DescuentoLineaCentavos);
        public string Subtotal => Money(SubtotalCentavos);
    }
}
