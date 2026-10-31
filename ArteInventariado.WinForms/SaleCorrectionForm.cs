using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SaleCorrectionForm : Form
{
    private readonly ISaleCorrectionQueryService _queryService;
    private readonly IReturnService _returnService;
    private readonly ISaleChangeService _saleChangeService;
    private readonly ISaleCancellationService _saleCancellationService;
    private readonly IPosCatalogService _productService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ICurrentSession _currentSession;
    private readonly BindingSource _salesBinding = new();
    private readonly BindingSource _linesBinding = new();
    private readonly BindingSource _paymentsBinding = new();

    private readonly DateTimePicker _dtpDesde = new();
    private readonly DateTimePicker _dtpHasta = new();
    private readonly TextBox _txtNumeroVenta = new();
    private readonly Button _btnBuscarNumero = new();
    private readonly Button _btnVentasHoy = new();
    private readonly Button _btnUltimasVentas = new();
    private readonly Button _btnDevolver = new();
    private readonly Button _btnCambiar = new();
    private readonly Button _btnAnular = new();
    private readonly Label _lblSaleInfo = new();
    private readonly DataGridView _gridSales = new();
    private readonly DataGridView _gridLines = new();
    private readonly DataGridView _gridPayments = new();

    private SaleCorrectionSearchResult? _currentSale;
    private bool _loadingSalesList;
    private bool _canReturn;
    private bool _canChange;

    public SaleCorrectionForm(
        ISaleCorrectionQueryService queryService,
        IReturnService returnService,
        ISaleChangeService saleChangeService,
        ISaleCancellationService saleCancellationService,
        IPosCatalogService productService,
        IEmployeePermissionService permissionService,
        ICurrentSession currentSession)
    {
        _queryService = queryService;
        _returnService = returnService;
        _saleChangeService = saleChangeService;
        _saleCancellationService = saleCancellationService;
        _productService = productService;
        _permissionService = permissionService;
        _currentSession = currentSession;

        BuildUi();
        Load += SaleCorrectionForm_Load;
    }

    private async void SaleCorrectionForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER);
            _canReturn = await _permissionService.HasPermissionAsync(PermisoEmpleado.DEVOLUCION_REGISTRAR);
            _canChange = await _permissionService.HasPermissionAsync(PermisoEmpleado.CAMBIO_REGISTRAR);
            _btnDevolver.Visible = _canReturn;
            _btnCambiar.Visible = _canChange;
            _btnAnular.Visible = _currentSession.EsAdmin;
            await LoadTodaySalesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Devoluciones";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(960, 620);
        MaximizeBox = true;
        WindowState = FormWindowState.Maximized;
        AutoScroll = true;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(24, 18, 24, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblTitle = new Label
        {
            Text = "Devoluciones y reembolsos",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14)
        };
        UITheme.StyleLabelTitle(lblTitle);

        var filterPanel = CreateFilterPanel();

        ConfigureGrid(_gridSales);
        _gridSales.DataSource = _salesBinding;
        _gridSales.SelectionChanged += GridSales_SelectionChanged;
        AddGridColumn(_gridSales, "Fecha", "Fecha", 130, 16);
        AddGridColumn(_gridSales, "NumeroVenta", "Número de venta", 170, 22);
        AddGridColumn(_gridSales, "Usuario", "Usuario", 130, 18);
        AddGridColumn(_gridSales, "Total", "Total", 95, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridSales, "MetodosPago", "Pago", 150, 20);
        AddGridColumn(_gridSales, "Estado", "Estado", 105, 14);
        var salesSection = CreateSection("Ventas recientes / encontradas", _gridSales);
        salesSection.Margin = new Padding(0, 0, 0, 12);

        _lblSaleInfo.Dock = DockStyle.Fill;
        _lblSaleInfo.AutoSize = false;
        _lblSaleInfo.MinimumSize = new Size(0, 42);
        _lblSaleInfo.Padding = new Padding(0);
        _lblSaleInfo.TextAlign = ContentAlignment.MiddleLeft;
        UITheme.StyleLabelHeader(_lblSaleInfo);
        var saleInfoPanel = CreateInfoPanel(_lblSaleInfo);

        var lowerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        lowerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        lowerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        lowerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureGrid(_gridLines);
        _gridLines.DataSource = _linesBinding;
        AddGridColumn(_gridLines, "Producto", "Producto", 220, 34);
        AddGridColumn(_gridLines, "CodigoInterno", "Codigo", 105, 13);
        AddGridColumn(_gridLines, "CantidadVendida", "Vendida", 78, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridLines, "CantidadDevuelta", "Procesada", 88, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridLines, "CantidadDisponible", "Disponible", 88, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridLines, "Precio", "Precio", 82, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridLines, "Total", "Total", 82, 10, DataGridViewContentAlignment.MiddleRight);

        ConfigureGrid(_gridPayments);
        _gridPayments.DataSource = _paymentsBinding;
        AddGridColumn(_gridPayments, "Metodo", "Metodo", 96, 24);
        AddGridColumn(_gridPayments, "Monto", "Monto", 85, 18, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridPayments, "Referencia", "Referencia", 150, 58);

        var linesSection = CreateSection("Detalle de venta seleccionada", _gridLines);
        linesSection.Margin = new Padding(0, 0, 8, 0);
        var paymentsSection = CreateSection("Pagos", _gridPayments);
        paymentsSection.Margin = new Padding(8, 0, 0, 0);
        lowerLayout.Controls.Add(linesSection, 0, 0);
        lowerLayout.Controls.Add(paymentsSection, 1, 0);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(0, 6, 0, 0),
            MinimumSize = new Size(0, 54)
        };
        ConfigureButton(_btnAnular, "Anular venta", 130, BtnAnular_Click);
        UITheme.StyleDangerButton(_btnAnular);
        ConfigureButton(_btnCambiar, "Registrar cambio de producto", 320, BtnCambiar_Click);
        UITheme.StyleSecondaryButton(_btnCambiar);
        _btnAnular.Enabled = false;
        _btnCambiar.Enabled = false;
        actionPanel.Controls.AddRange([_btnAnular, _btnCambiar]);

        root.Controls.Add(lblTitle, 0, 0);
        root.Controls.Add(filterPanel, 0, 1);
        root.Controls.Add(salesSection, 0, 2);
        root.Controls.Add(saleInfoPanel, 0, 3);
        root.Controls.Add(lowerLayout, 0, 4);
        root.Controls.Add(actionPanel, 0, 5);
        Controls.Add(root);
    }

    private Control CreateFilterPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 14),
            MinimumSize = new Size(0, 86),
            BorderStyle = BorderStyle.FixedSingle
        };

        var filterGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        var columnWeights = new[] { 10f, 10f, 17f, 10f, 14f, 18f, 21f };
        foreach (var weight in columnWeights)
        {
            filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weight));
        }
        filterGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureDate(_dtpDesde, DateTime.Today);
        ConfigureDate(_dtpHasta, DateTime.Today);
        ConfigureTextBox(_txtNumeroVenta, 185);
        _txtNumeroVenta.KeyDown += TxtNumeroVenta_KeyDown;

        ConfigureButton(_btnBuscarNumero, "Buscar", 105, BtnBuscarNumero_Click);
        UITheme.StylePrimaryButton(_btnBuscarNumero);
        ConfigureButton(_btnVentasHoy, "Ventas del día", 138, BtnVentasHoy_Click);
        UITheme.StyleSecondaryButton(_btnVentasHoy);
        ConfigureButton(_btnUltimasVentas, "Últimas ventas", 194, BtnUltimasVentas_Click);
        UITheme.StyleSecondaryButton(_btnUltimasVentas);
        ConfigureButton(_btnDevolver, "Registrar devolución", 178, BtnDevolver_Click);
        UITheme.StyleDangerButton(_btnDevolver);
        _btnDevolver.Enabled = false;

        filterGrid.Controls.Add(CreateFilterField("Desde", _dtpDesde), 0, 0);
        filterGrid.Controls.Add(CreateFilterField("Hasta", _dtpHasta), 1, 0);
        filterGrid.Controls.Add(CreateFilterField("Número de venta", _txtNumeroVenta), 2, 0);
        filterGrid.Controls.Add(CreateFilterButtonField(_btnBuscarNumero), 3, 0);
        filterGrid.Controls.Add(CreateFilterButtonField(_btnVentasHoy), 4, 0);
        filterGrid.Controls.Add(CreateFilterButtonField(_btnUltimasVentas), 5, 0);
        filterGrid.Controls.Add(CreateFilterButtonField(_btnDevolver), 6, 0);
        panel.Controls.Add(filterGrid);

        return panel;
    }

    private async void BtnBuscarNumero_Click(object? sender, EventArgs e)
    {
        await SearchByNumberAsync();
    }

    private async void BtnVentasHoy_Click(object? sender, EventArgs e)
    {
        await LoadTodaySalesAsync();
    }

    private async void BtnUltimasVentas_Click(object? sender, EventArgs e)
    {
        await SearchSalesListAsync(new SaleCorrectionSalesFilter(Limit: 50));
    }

    private async void TxtNumeroVenta_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        await SearchByNumberAsync();
    }

    private async void GridSales_SelectionChanged(object? sender, EventArgs e)
    {
        if (_loadingSalesList || _gridSales.CurrentRow?.DataBoundItem is not SaleGridRow row)
        {
            return;
        }

        await LoadSaleByIdAsync(row.VentaId);
    }

    private async Task LoadTodaySalesAsync()
    {
        _dtpDesde.Value = DateTime.Today;
        _dtpHasta.Value = DateTime.Today;
        await SearchSalesListAsync(new SaleCorrectionSalesFilter(
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            Limit: 50));
    }

    private async Task SearchByNumberAsync()
    {
        try
        {
            SetActionsEnabled(false);
            var numeroVenta = _txtNumeroVenta.Text.Trim();
            if (string.IsNullOrWhiteSpace(numeroVenta))
            {
                await SearchSalesListAsync(new SaleCorrectionSalesFilter(
                    DateOnly.FromDateTime(_dtpDesde.Value.Date),
                    DateOnly.FromDateTime(_dtpHasta.Value.Date),
                    Limit: 50));
                return;
            }

            await SearchSalesListAsync(new SaleCorrectionSalesFilter(NumeroVenta: numeroVenta, Limit: 50));

            var exactSale = await _queryService.GetSaleForCorrectionAsync(numeroVenta);
            if (exactSale is not null)
            {
                RenderSaleDetail(exactSale);
            }
            else if (_salesBinding.Count == 0)
            {
                ClearSaleDetail("Venta no encontrada.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async Task SearchSalesListAsync(SaleCorrectionSalesFilter filter)
    {
        try
        {
            SetActionsEnabled(false);
            _loadingSalesList = true;
            ClearSaleDetail("Seleccione una venta para ver su detalle.");

            var sales = await _queryService.SearchSalesForCorrectionAsync(filter);
            var rows = sales.Select(SaleGridRow.FromSummary).ToList();
            _salesBinding.DataSource = rows;

            if (rows.Count == 0)
            {
                ClearSaleDetail("No hay ventas con esos filtros.");
                return;
            }

            _gridSales.ClearSelection();
            _gridSales.Rows[0].Selected = true;
            _gridSales.CurrentCell = _gridSales.Rows[0].Cells[0];
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _loadingSalesList = false;
            SetActionsEnabled(true);
        }

        if (_gridSales.CurrentRow?.DataBoundItem is SaleGridRow row)
        {
            await LoadSaleByIdAsync(row.VentaId);
        }
    }

    private async Task LoadSaleByIdAsync(int ventaId)
    {
        try
        {
            SetActionsEnabled(false);
            var sale = await _queryService.GetSaleForCorrectionByIdAsync(ventaId);
            if (sale is null)
            {
                ClearSaleDetail("Venta no encontrada.");
                return;
            }

            RenderSaleDetail(sale);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private void RenderSaleDetail(SaleCorrectionSearchResult sale)
    {
        _currentSale = sale;
        _lblSaleInfo.Text =
            $"Venta {sale.NumeroVenta} | Fecha: {sale.FechaVenta.ToLocalTime():yyyy-MM-dd HH:mm} | Usuario: {sale.Usuario} | Total: {FormatDollars(sale.TotalCentavos)}";

        _linesBinding.DataSource = sale.Lines.Select(SaleLineGridRow.FromLine).ToList();
        _paymentsBinding.DataSource = sale.Payments.Select(PaymentGridRow.FromPayment).ToList();
        SetActionsEnabled(true);
    }

    private void ClearSaleDetail(string message)
    {
        _currentSale = null;
        _lblSaleInfo.Text = message;
        _linesBinding.DataSource = Array.Empty<SaleLineGridRow>();
        _paymentsBinding.DataSource = Array.Empty<PaymentGridRow>();
        SetActionsEnabled(true);
    }

    private async void BtnDevolver_Click(object? sender, EventArgs e)
    {
        if (_currentSale is null)
        {
            return;
        }

        if (!_canReturn)
        {
            MessageBox.Show("No tiene permiso para registrar devoluciones.", "Devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new ReturnForm(_currentSale, _returnService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Devolución registrada.", "Devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadSaleByIdAsync(_currentSale.VentaId);
        }
    }

    private async void BtnCambiar_Click(object? sender, EventArgs e)
    {
        if (_currentSale is null)
        {
            return;
        }

        if (!_canChange)
        {
            MessageBox.Show("No tiene permiso para registrar cambios.", "Cambios", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new SaleChangeForm(_currentSale, _productService, _saleChangeService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Cambio registrado.", "Cambios", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadSaleByIdAsync(_currentSale.VentaId);
        }
    }

    private async void BtnAnular_Click(object? sender, EventArgs e)
    {
        if (_currentSale is null)
        {
            return;
        }

        using var form = new SaleCancellationForm(_currentSale, _saleCancellationService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Anulación registrada.", "Anulaciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await SearchSalesListAsync(new SaleCorrectionSalesFilter(Limit: 50));
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        _btnBuscarNumero.Enabled = enabled;
        _btnVentasHoy.Enabled = enabled;
        _btnUltimasVentas.Enabled = enabled;
        var hasAvailableLines = _currentSale?.Lines.Any(l => l.CantidadDisponible > 0) == true;
        _btnDevolver.Enabled = enabled && _canReturn && hasAvailableLines;
        _btnCambiar.Enabled = enabled && _canChange && hasAvailableLines;
        _btnAnular.Enabled = enabled && _currentSession.EsAdmin && hasAvailableLines;
    }

    private static Label Header(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8)
        };
        UITheme.StyleLabelHeader(label);
        return label;
    }

    private static Control CreateSection(string title, Control content)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(12),
            BackColor = UITheme.SurfaceColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = Header(title);
        panel.Controls.Add(header, 0, 0);
        content.Dock = DockStyle.Fill;
        content.Margin = Padding.Empty;
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private static Control CreateInfoPanel(Label label)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SoftPinkColor,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(0, 0, 0, 12),
            MinimumSize = new Size(0, 54)
        };
        label.Dock = DockStyle.Fill;
        panel.Controls.Add(label);
        return panel;
    }

    private static Control CreateFilterField(string labelText, Control input)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(6, 0, 6, 0),
            Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        UITheme.StyleLabelNormal(label);
        UITheme.StyleInput(input);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 2, 0, 0);
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private static Control CreateFilterButtonField(Button button)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 6, 0),
            Padding = new Padding(0, 24, 0, 0)
        };
        button.Dock = DockStyle.Fill;
        button.Margin = Padding.Empty;
        panel.Controls.Add(button);
        return panel;
    }

    private static void ConfigureDate(DateTimePicker picker, DateTime value)
    {
        picker.Size = new Size(110, 30);
        picker.Format = DateTimePickerFormat.Short;
        picker.Value = value;
    }

    private static void ConfigureTextBox(TextBox textBox, int width)
    {
        textBox.Size = new Size(width, 30);
        UITheme.StyleTextBox(textBox);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Size = new Size(width, 42);
        button.AutoSize = false;
        button.Click += handler;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ScrollBars = ScrollBars.Both;
        grid.ShowCellToolTips = true;
        grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        UITheme.StyleDataGridView(grid);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.ColumnHeadersHeight = 44;
    }

    private static void AddGridColumn(
        DataGridView grid,
        string propertyName,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = { Alignment = alignment }
        });
    }

    private static void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        e.ToolTipText = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
    }

    private static string FormatDollars(long centavos) => (centavos / 100m).ToString("0.00");

    private sealed record SaleGridRow(
        int VentaId,
        string Fecha,
        string NumeroVenta,
        string Usuario,
        string Total,
        string MetodosPago,
        string Estado)
    {
        public static SaleGridRow FromSummary(SaleCorrectionSaleSummary summary)
        {
            return new SaleGridRow(
                summary.VentaId,
                summary.FechaVenta.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                summary.NumeroVenta,
                summary.Usuario,
                FormatDollars(summary.TotalCentavos),
                summary.MetodosPago,
                summary.Estado.ToString());
        }
    }

    private sealed record SaleLineGridRow(
        int DetalleVentaId,
        string Producto,
        string CodigoInterno,
        int CantidadVendida,
        int CantidadDevuelta,
        int CantidadDisponible,
        string Precio,
        string Total)
    {
        public static SaleLineGridRow FromLine(SaleCorrectionLine line)
        {
            return new SaleLineGridRow(
                line.DetalleVentaId,
                line.Producto,
                line.CodigoInterno,
                line.CantidadVendida,
                line.CantidadDevuelta,
                line.CantidadDisponible,
                FormatDollars(line.PrecioUnitarioCentavos),
                FormatDollars(line.TotalLineaCentavos));
        }
    }

    private sealed record PaymentGridRow(string Metodo, string Monto, string? Referencia)
    {
        public static PaymentGridRow FromPayment(SaleCorrectionPayment payment)
        {
            return new PaymentGridRow(
                payment.MetodoPago.ToString(),
                FormatDollars(payment.MontoCentavos),
                payment.Referencia);
        }
    }
}
