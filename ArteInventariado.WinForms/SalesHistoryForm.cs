using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SalesHistoryForm : Form
{
    private readonly ISalesQueryService _salesQueryService;
    private readonly IUserService _userService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IProductAssetService? _productAssets;
    private readonly IReceiptDocumentService? _receiptDocuments;
    private readonly IInstallationContext? _installation;
    private readonly ICurrentSession? _currentSession;
    private readonly BindingSource _salesBinding = new();
    private readonly HashSet<int> _openingDetailTabs = [];
    private readonly BindingSource _linesBinding = new();
    private readonly BindingSource _paymentsBinding = new();

    private readonly DateTimePicker _dtpDesde = new();
    private readonly DateTimePicker _dtpHasta = new();
    private readonly TextBox _txtNumeroVenta = new();
    private readonly ComboBox _cmbUsuario = new();
    private readonly ComboBox _cmbMetodoPago = new();
    private readonly Button _btnBuscar = new();
    private readonly Button _btnReprintReceipt = new();
    private readonly Button _btnSalesToday = new();
    private readonly DataGridView _gridSales = new();
    private readonly DataGridView _gridLines = new();
    private readonly DataGridView _gridPayments = new();
    private readonly TabControl _resultTabs = new();

    public SalesHistoryForm(
        ISalesQueryService salesQueryService,
        IUserService userService,
        IEmployeePermissionService permissionService,
        IReceiptDocumentService? receiptDocuments = null,
        IInstallationContext? installation = null,
        IProductAssetService? productAssets = null,
        ICurrentSession? currentSession = null)
    {
        _salesQueryService = salesQueryService;
        _userService = userService;
        _permissionService = permissionService;
        _receiptDocuments = receiptDocuments;
        _installation = installation;
        _productAssets = productAssets;
        _currentSession = currentSession;

        BuildUi();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _resultTabs.SelectedTab?.Tag is int)
            {
                e.Handled = true;
                CloseDetailTab(_resultTabs.SelectedTab);
            }
        };
        Load += SalesHistoryForm_Load;
    }

    private async void SalesHistoryForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.HISTORIAL_VENTAS_VER);
            _btnSalesToday.Visible = _currentSession?.EsAdmin == true;
            var canReprint = (_installation?.AllowsOperationalWrites ?? true) &&
                await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPROBANTE_REIMPRIMIR);
            _btnReprintReceipt.Visible = true;
            _btnReprintReceipt.Text = canReprint ? "Reimprimir" : "Ver ticket";
            await LoadFiltersAsync();
            await RefreshSalesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Historial de ventas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Historial de ventas";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 680);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filtersHeader = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 8, 18, 8),
            Margin = new Padding(0)
        };

        var lblTitle = new Label
        {
            Text = "Historial de ventas",
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(lblTitle);

        var btnToggleFilters = new Button
        {
            Text = "Ocultar filtros",
            Dock = DockStyle.Right,
            Width = 150
        };
        UITheme.StyleSecondaryButton(btnToggleFilters);

        _btnReprintReceipt.Text = "Ver ticket";
        _btnReprintReceipt.Dock = DockStyle.Right;
        _btnReprintReceipt.Width = 150;
        _btnReprintReceipt.TextAlign = ContentAlignment.MiddleCenter;
        _btnReprintReceipt.Click += async (_, _) => await ReprintSelectedSaleReceiptAsync();
        UITheme.StyleSecondaryButton(_btnReprintReceipt);

        _btnSalesToday.Text = "Ventas de hoy";
        _btnSalesToday.Dock = DockStyle.Right;
        _btnSalesToday.Width = 150;
        _btnSalesToday.TextAlign = ContentAlignment.MiddleCenter;
        _btnSalesToday.Visible = false;
        _btnSalesToday.Click += (_, _) => OpenAdminSalesPeriodView();
        UITheme.StylePrimaryButton(_btnSalesToday);

        filtersHeader.Controls.AddRange([_btnSalesToday, btnToggleFilters, _btnReprintReceipt, lblTitle]);

        var filtersPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 12, 18, 18),
            Margin = new Padding(0, 0, 0, 14)
        };

        ConfigureDate(_dtpDesde, 0, 0, DateTime.Today.AddDays(-30));
        ConfigureDate(_dtpHasta, 0, 0, DateTime.Today);

        UITheme.StyleTextBox(_txtNumeroVenta);
        _txtNumeroVenta.KeyDown += TxtNumeroVenta_KeyDown;

        _cmbUsuario.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbUsuario.DisplayMember = nameof(UserOption.Name);
        _cmbUsuario.ValueMember = nameof(UserOption.Id);
        _cmbUsuario.Font = UITheme.NormalFont;

        _cmbMetodoPago.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMetodoPago.DisplayMember = nameof(PaymentMethodOption.Name);
        _cmbMetodoPago.ValueMember = nameof(PaymentMethodOption.Value);
        _cmbMetodoPago.Font = UITheme.NormalFont;

        _btnBuscar.Text = "Buscar";
        _btnBuscar.Size = new Size(124, 36);
        _btnBuscar.Click += async (_, _) => await RefreshSalesAsync();
        UITheme.StylePrimaryButton(_btnBuscar);

        var btnClearFilters = new Button
        {
            Text = "Limpiar filtros"
        };
        btnClearFilters.Click += async (_, _) => await ClearFiltersAsync();
        UITheme.StyleSecondaryButton(btnClearFilters);

        var filterGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            ColumnCount = 3,
            RowCount = 2
        };
        filterGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        filterGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (var column = 0; column < 3; column++)
        {
            filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        }

        AddResponsiveFilterField(filterGrid, 0, 0, "Fecha desde", _dtpDesde);
        AddResponsiveFilterField(filterGrid, 1, 0, "Fecha hasta", _dtpHasta);
        AddResponsiveFilterField(filterGrid, 2, 0, "Numero de venta", _txtNumeroVenta);
        AddResponsiveFilterField(filterGrid, 0, 1, "Usuario", _cmbUsuario);
        AddResponsiveFilterField(filterGrid, 1, 1, "Metodo de pago", _cmbMetodoPago);

        var actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(8, 6, 0, 0),
            BackColor = UITheme.SurfaceColor
        };
        _btnBuscar.Size = new Size(110, 34);
        btnClearFilters.Size = new Size(130, 34);
        actionsPanel.Controls.AddRange([_btnBuscar, btnClearFilters]);
        filterGrid.Controls.Add(actionsPanel, 2, 1);
        filtersPanel.Controls.Add(filterGrid);

        var filtersVisible = true;
        btnToggleFilters.Click += (_, _) =>
        {
            filtersVisible = !filtersVisible;
            filtersPanel.Visible = filtersVisible;
            root.RowStyles[1].Height = filtersVisible ? 132 : 0;
            btnToggleFilters.Text = filtersVisible ? "Ocultar filtros" : "Mostrar filtros";
        };

        ConfigureSalesGrid();
        ConfigureLinesGrid();
        ConfigurePaymentsGrid();

        var pnlSales = CreateGridSection("Ventas registradas", _gridSales);
        pnlSales.Margin = new Padding(0, 0, 0, 14);

        var detailLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var pnlLines = CreateGridSection("Detalle de productos", _gridLines);
        pnlLines.Margin = new Padding(0, 0, 10, 0);
        var pnlPayments = CreateGridSection("Pagos", _gridPayments);
        pnlPayments.Margin = new Padding(10, 0, 0, 0);
        detailLayout.Controls.Add(pnlLines, 0, 0);
        detailLayout.Controls.Add(pnlPayments, 1, 0);

        var overviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(4),
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty
        };
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        overviewLayout.Controls.Add(pnlSales, 0, 0);
        overviewLayout.Controls.Add(detailLayout, 0, 1);

        var salesTab = new TabPage("Ventas")
        {
            BackColor = UITheme.BackgroundColor,
            Tag = "sales-overview",
            Padding = Padding.Empty
        };
        salesTab.Controls.Add(overviewLayout);
        _resultTabs.Dock = DockStyle.Fill;
        _resultTabs.TabPages.Add(salesTab);

        root.Controls.Add(filtersHeader, 0, 0);
        root.Controls.Add(filtersPanel, 0, 1);
        root.Controls.Add(_resultTabs, 0, 2);
        Controls.Add(root);
    }

    private void BuildLegacyUi()
    {
        Text = "Historial de ventas";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 640);
        UITheme.StyleForm(this);

        // ── TOP FILTER PANEL ────────────────────────────────────────────────
        var pnlFilters = new Panel
        {
            Dock = DockStyle.Top,
            Height = 140,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(15, 10, 15, 10)
        };

        var lblTitle = new Label
        {
            Text = "Historial de ventas",
            AutoSize = true,
            Location = new Point(15, 10)
        };
        UITheme.StyleLabelTitle(lblTitle);

        AddLabelTo(pnlFilters, "Desde", 15, 55);
        ConfigureDate(_dtpDesde, 15, 78, DateTime.Today.AddDays(-30));

        AddLabelTo(pnlFilters, "Hasta", 145, 55);
        ConfigureDate(_dtpHasta, 145, 78, DateTime.Today);

        AddLabelTo(pnlFilters, "Numero de venta", 275, 55);
        _txtNumeroVenta.Location = new Point(275, 78);
        _txtNumeroVenta.Size = new Size(160, 30);
        UITheme.StyleTextBox(_txtNumeroVenta);
        _txtNumeroVenta.KeyDown += TxtNumeroVenta_KeyDown;

        AddLabelTo(pnlFilters, "Usuario", 448, 55);
        _cmbUsuario.Location = new Point(448, 78);
        _cmbUsuario.Size = new Size(160, 30);
        _cmbUsuario.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbUsuario.DisplayMember = nameof(UserOption.Name);
        _cmbUsuario.ValueMember = nameof(UserOption.Id);
        _cmbUsuario.Font = UITheme.NormalFont;

        AddLabelTo(pnlFilters, "Metodo", 625, 55);
        _cmbMetodoPago.Location = new Point(625, 78);
        _cmbMetodoPago.Size = new Size(140, 30);
        _cmbMetodoPago.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMetodoPago.DisplayMember = nameof(PaymentMethodOption.Name);
        _cmbMetodoPago.ValueMember = nameof(PaymentMethodOption.Value);
        _cmbMetodoPago.Font = UITheme.NormalFont;

        _btnBuscar.Text = "Buscar";
        _btnBuscar.Location = new Point(782, 78);
        _btnBuscar.Size = new Size(110, 32);
        _btnBuscar.Click += async (_, _) => await RefreshSalesAsync();
        UITheme.StylePrimaryButton(_btnBuscar);

        pnlFilters.Controls.AddRange([
            lblTitle, _dtpDesde, _dtpHasta, _txtNumeroVenta,
            _cmbUsuario, _cmbMetodoPago, _btnBuscar
        ]);

        // ── MASTER/DETAIL SPLIT CONTAINER ───────────────────────────────────
        // Outer: vertical split (top = sales grid, bottom = detail panels)
        var splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        // Top: sales grid fills the entire upper panel
        ConfigureSalesGrid();
        _gridSales.Dock = DockStyle.Fill;
        splitMain.Panel1.Controls.Add(_gridSales);

        // Bottom: inner horizontal split (lines | payments)
        var splitDetail = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        var pnlLines = new Panel { Dock = DockStyle.Fill };
        var lblLines = new Label
        {
            Text = "Detalle de productos",
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 6, 0, 4)
        };
        UITheme.StyleLabelHeader(lblLines);
        ConfigureLinesGrid();
        _gridLines.Dock = DockStyle.Fill;
        pnlLines.Controls.Add(_gridLines);
        pnlLines.Controls.Add(lblLines);

        var pnlPayments = new Panel { Dock = DockStyle.Fill };
        var lblPayments = new Label
        {
            Text = "Pagos",
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 6, 0, 4)
        };
        UITheme.StyleLabelHeader(lblPayments);
        ConfigurePaymentsGrid();
        _gridPayments.Dock = DockStyle.Fill;
        pnlPayments.Controls.Add(_gridPayments);
        pnlPayments.Controls.Add(lblPayments);

        splitDetail.Panel1.Controls.Add(pnlLines);
        splitDetail.Panel2.Controls.Add(pnlPayments);
        splitMain.Panel2.Controls.Add(splitDetail);

        // ── ASSEMBLY ────────────────────────────────────────────────────────
        Controls.Add(splitMain);
        Controls.Add(pnlFilters);

        Shown += (_, _) =>
        {
            ApplySafeSplitLayout(splitMain, splitMain.Height * 55 / 100, 150, 150);
            ApplySafeSplitLayout(splitDetail, splitDetail.Width * 65 / 100, 200, 200);

            splitMain.SizeChanged += (_, _) => ApplySafeSplitLayout(splitMain, splitMain.SplitterDistance, 150, 150);
            splitDetail.SizeChanged += (_, _) => ApplySafeSplitLayout(splitDetail, splitDetail.SplitterDistance, 200, 200);
        };
    }

    private async Task LoadFiltersAsync()
    {
        var users = await _userService.GetAllAsync();
        _cmbUsuario.DataSource = users
            .Select(u => new UserOption(u.UsuarioId, u.NombreUsuario))
            .Prepend(new UserOption(null, "Todos"))
            .ToList();

        _cmbMetodoPago.DataSource = Enum.GetValues<MetodoPago>()
            .Select(m => new PaymentMethodOption(m, m.ToString()))
            .Prepend(new PaymentMethodOption(null, "Todos"))
            .ToList();
    }

    private async Task ClearFiltersAsync()
    {
        _dtpDesde.Checked = false;
        _dtpHasta.Checked = false;
        _txtNumeroVenta.Clear();

        if (_cmbUsuario.Items.Count > 0)
        {
            _cmbUsuario.SelectedIndex = 0;
        }

        if (_cmbMetodoPago.Items.Count > 0)
        {
            _cmbMetodoPago.SelectedIndex = 0;
        }

        await RefreshSalesAsync();
    }

    private void AddFilterColumn(TableLayoutPanel layout, int column, string labelText, Control input)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 12, 0)
        };
        UITheme.StyleLabelNormal(label);

        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 3, 12, 3);
        input.Font = UITheme.NormalFont;

        layout.Controls.Add(label, column, 0);
        layout.Controls.Add(input, column, 1);
    }

    private static void AddResponsiveFilterField(
        TableLayoutPanel layout,
        int column,
        int row,
        string labelText,
        Control input)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 4)
        };
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
        input.Margin = Padding.Empty;
        field.Controls.Add(label, 0, 0);
        field.Controls.Add(input, 0, 1);
        layout.Controls.Add(field, column, row);
    }

    private Panel CreateFilterField(string labelText, Control input, int width)
    {
        var container = new Panel
        {
            Width = width,
            Height = 72,
            Margin = new Padding(0, 0, 14, 0),
            BackColor = UITheme.SurfaceColor
        };

        var label = new Label
        {
            Text = labelText,
            Location = new Point(0, 0),
            Size = new Size(width, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelNormal(label);

        input.Location = new Point(0, 30);
        input.Size = new Size(width, 32);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        input.Font = UITheme.NormalFont;

        container.Controls.AddRange([label, input]);
        return container;
    }

    private static Panel CreateButtonField(Button button)
    {
        var container = new Panel
        {
            Width = 132,
            Height = 72,
            Margin = new Padding(0, 0, 0, 0),
            BackColor = UITheme.SurfaceColor,
        };

        button.Location = new Point(0, 30);
        button.Size = new Size(124, 34);
        container.Controls.Add(button);
        return container;
    }

    private Panel CreateGridSection(string title, DataGridView grid)
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelHeader(label);

        grid.Dock = DockStyle.Fill;
        container.Controls.Add(grid);
        container.Controls.Add(label);
        return container;
    }

    private void OpenAdminSalesPeriodView()
    {
        if (_currentSession?.EsAdmin != true)
        {
            return;
        }

        using var form = new AdminSalesPeriodForm(_salesQueryService, _currentSession);
        form.ShowDialog(this);
    }

    private async Task RefreshSalesAsync()
    {
        try
        {
            _btnBuscar.Enabled = false;
            _linesBinding.DataSource = Array.Empty<SaleLineGridRow>();
            _paymentsBinding.DataSource = Array.Empty<SalePaymentGridRow>();

            var filter = new SalesHistoryFilter(
                _dtpDesde.Checked ? DateOnly.FromDateTime(_dtpDesde.Value.Date) : null,
                _dtpHasta.Checked ? DateOnly.FromDateTime(_dtpHasta.Value.Date) : null,
                _txtNumeroVenta.Text,
                _cmbUsuario.SelectedValue as int?,
                _cmbMetodoPago.SelectedValue is MetodoPago method ? method : null);

            var sales = await _salesQueryService.GetSalesAsync(filter);
            _salesBinding.DataSource = sales.Select(SaleGridRow.FromSummary).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Historial de ventas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnBuscar.Enabled = true;
        }
    }

    private async Task LoadSelectedSaleDetailAsync()
    {
        if (_gridSales.CurrentRow?.DataBoundItem is not SaleGridRow row)
        {
            return;
        }

        try
        {
            var detail = await _salesQueryService.GetSaleDetailAsync(row.VentaId);
            if (detail is null)
            {
                _linesBinding.DataSource = Array.Empty<SaleLineGridRow>();
                _paymentsBinding.DataSource = Array.Empty<SalePaymentGridRow>();
                return;
            }

            _linesBinding.DataSource = detail.Lines.Select(SaleLineGridRow.FromLine).ToList();
            _paymentsBinding.DataSource = detail.Payments.Select(SalePaymentGridRow.FromPayment).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Detalle de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ReprintSelectedSaleReceiptAsync()
    {
        if (_gridSales.CurrentRow?.DataBoundItem is not SaleGridRow row)
        {
            MessageBox.Show("Seleccione una venta para reimprimir el ticket.", "Historial de ventas", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var canReprint = (_installation?.AllowsOperationalWrites ?? true) &&
                await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPROBANTE_REIMPRIMIR);
            SaleReceiptViewModel? receipt;
            if (_receiptDocuments is not null)
            {
                var canonical = await _receiptDocuments.GetHistoricalAsync(row.VentaId);
                receipt = canonical is null ? null : SaleReceiptViewModel.FromReceiptData(canonical);
                if (canReprint && canonical is not null)
                    await _receiptDocuments.RegisterReprintAsync(row.VentaId);
            }
            else
            {
                var detail = await _salesQueryService.GetSaleDetailAsync(row.VentaId);
                receipt = detail is null ? null : SaleReceiptViewModel.FromSalesHistoryDetail(detail);
            }

            if (receipt is null)
            {
                MessageBox.Show("No se encontro la venta seleccionada.", "Historial de ventas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var receiptForm = new SaleReceiptForm(
                receipt,
                isCompletedSale: false,
                primaryButtonText: "Cerrar");
            receiptForm.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Reimprimir ticket", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ConfigureSalesGrid()
    {
        ConfigureReadOnlyGrid(_gridSales);
        _gridSales.DataSource = _salesBinding;
        _gridSales.SelectionChanged += async (_, _) => await LoadSelectedSaleDetailAsync();
        _gridSales.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || _gridSales.Rows[e.RowIndex].DataBoundItem is not SaleGridRow row)
            {
                return;
            }

            await OpenSaleDetailAsync(row);
        };

        AddGridColumn(_gridSales, "Fecha", "Fecha/hora", 145);
        AddGridColumn(_gridSales, "NumeroVenta", "Numero de venta", 145);
        AddGridColumn(_gridSales, "Usuario", "Usuario", 120);
        AddGridColumn(_gridSales, "Cliente", "Cliente", 150);
        AddGridColumn(_gridSales, "Total", "Total", 90);
        AddGridColumn(_gridSales, "Descuento", "Descuento", 90);
        AddGridColumn(_gridSales, "MetodosPago", "Metodo(s) de pago", 150);
        AddGridColumn(_gridSales, "Caja", "Caja", 70);
        AddGridColumn(_gridSales, "Estado", "Estado", 110);
    }

    private async Task OpenSaleDetailAsync(SaleGridRow row)
    {
        var ownsOpeningSlot = false;
        try
        {
            var existingTab = _resultTabs.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(page => page.Tag is int saleId && saleId == row.VentaId);
            if (existingTab is not null)
            {
                _resultTabs.SelectedTab = existingTab;
                return;
            }

            if (!_openingDetailTabs.Add(row.VentaId))
            {
                return;
            }
            ownsOpeningSlot = true;

            var detail = await _salesQueryService.GetSaleDetailAsync(row.VentaId);
            if (detail is null)
            {
                MessageBox.Show(this, "No se encontró el detalle de la venta seleccionada.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var tab = new TabPage($"Venta {detail.NumeroVenta}")
            {
                BackColor = UITheme.BackgroundColor,
                Tag = row.VentaId,
                Padding = Padding.Empty
            };
            var view = new SalesHistoryDetailForm(row.Summary, detail, _productAssets)
            {
                Dock = DockStyle.Fill
            };
            view.CloseRequested += (_, _) => CloseDetailTab(tab);
            tab.Controls.Add(view);
            _resultTabs.TabPages.Add(tab);
            _resultTabs.SelectedTab = tab;
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("sales-history-detail", ex);
            MessageBox.Show(this, $"No se pudo abrir el detalle de la venta:\r\n\r\n{ex.Message}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (ownsOpeningSlot)
            {
                _openingDetailTabs.Remove(row.VentaId);
            }
        }
    }

    private void CloseDetailTab(TabPage tab)
    {
        if (!_resultTabs.TabPages.Contains(tab)) return;
        var wasSelected = _resultTabs.SelectedTab == tab;
        _resultTabs.TabPages.Remove(tab);
        tab.Dispose();
        if (wasSelected && _resultTabs.TabPages.Count > 0)
        {
            _resultTabs.SelectedIndex = 0;
        }
    }

    private void ConfigureLinesGrid()
    {
        ConfigureReadOnlyGrid(_gridLines);
        _gridLines.DataSource = _linesBinding;

        AddGridColumn(_gridLines, "Producto", "Producto", 220);
        AddGridColumn(_gridLines, "Codigo", "Codigo", 110);
        AddGridColumn(_gridLines, "Cantidad", "Cantidad", 80);
        AddGridColumn(_gridLines, "PrecioUnitario", "Precio unitario", 110);
        AddGridColumn(_gridLines, "Descuento", "Descuento", 100);
        AddGridColumn(_gridLines, "Subtotal", "Subtotal", 100);
    }

    private void ConfigurePaymentsGrid()
    {
        ConfigureReadOnlyGrid(_gridPayments);
        _gridPayments.DataSource = _paymentsBinding;

        AddGridColumn(_gridPayments, "Metodo", "Metodo", 100);
        AddGridColumn(_gridPayments, "Monto", "Monto", 100);
        AddGridColumn(_gridPayments, "Referencia", "Referencia", 180);
    }

    private void ConfigureReadOnlyGrid(DataGridView grid)
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.MultiSelect = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.ScrollBars = ScrollBars.Both;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UITheme.StyleDataGridView(grid);
        grid.GridColor = Color.FromArgb(228, 233, 237);
        grid.ColumnHeadersHeight = 44;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 251);
    }

    private void AddLabelTo(Control container, string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y)
        };
        UITheme.StyleLabelNormal(label);
        container.Controls.Add(label);
    }

    private static void ConfigureDate(DateTimePicker picker, int x, int y, DateTime value)
    {
        picker.Location = new Point(x, y);
        picker.Size = new Size(110, 30);
        picker.Format = DateTimePickerFormat.Short;
        picker.ShowCheckBox = true;
        picker.Checked = true;
        picker.Value = value;
        picker.Font = UITheme.NormalFont;
    }

    private static void AddGridColumn(DataGridView grid, string propertyName, string headerText, int width)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            MinimumWidth = width,
            FillWeight = width
        });
    }

    private static void ApplySafeSplitLayout(
        SplitContainer splitContainer,
        int preferredDistance,
        int preferredPanel1MinSize,
        int preferredPanel2MinSize)
    {
        var availableSize = GetAvailableSplitterSize(splitContainer);
        if (availableSize <= 0)
        {
            return;
        }

        var totalPreferredMinSize = preferredPanel1MinSize + preferredPanel2MinSize;
        var panel1MinSize = preferredPanel1MinSize;
        var panel2MinSize = preferredPanel2MinSize;

        if (availableSize < totalPreferredMinSize)
        {
            panel1MinSize = Math.Max(25, availableSize * preferredPanel1MinSize / totalPreferredMinSize);
            panel2MinSize = Math.Max(25, availableSize - panel1MinSize);
        }

        panel2MinSize = Math.Min(panel2MinSize, Math.Max(25, availableSize - panel1MinSize));
        panel1MinSize = Math.Min(panel1MinSize, Math.Max(25, availableSize - panel2MinSize));

        var min = panel1MinSize;
        var max = availableSize - panel2MinSize;
        if (max < min)
        {
            return;
        }

        var targetDistance = Math.Clamp(preferredDistance, min, max);
        splitContainer.SplitterDistance = targetDistance;
        splitContainer.Panel1MinSize = panel1MinSize;
        splitContainer.Panel2MinSize = panel2MinSize;
        splitContainer.SplitterDistance = targetDistance;
    }

    private static int GetAvailableSplitterSize(SplitContainer splitContainer)
    {
        return splitContainer.Orientation == Orientation.Vertical
            ? splitContainer.Width
            : splitContainer.Height;
    }

    private async void TxtNumeroVenta_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        await RefreshSalesAsync();
    }

    private sealed record UserOption(int? Id, string Name);

    private sealed record PaymentMethodOption(MetodoPago? Value, string Name);

    private sealed record SaleGridRow(
        int VentaId,
        string Fecha,
        string NumeroVenta,
        string Usuario,
        string Cliente,
        string Total,
        string Descuento,
        string MetodosPago,
        string Caja,
        string Estado,
        SalesHistorySummary Summary)
    {
        public static SaleGridRow FromSummary(SalesHistorySummary summary)
        {
            return new SaleGridRow(
                summary.VentaId,
                ToLocal(summary.FechaVenta).ToString("yyyy-MM-dd HH:mm"),
                summary.NumeroVenta,
                summary.Usuario,
                summary.Cliente,
                FormatDollars(summary.TotalCentavos),
                FormatDollars(summary.DescuentoCentavos),
                summary.MetodosPago,
                summary.CajaId.ToString(),
                summary.Estado.ToString(),
                summary);
        }
    }

    private sealed record SaleLineGridRow(
        string Producto,
        string Codigo,
        int Cantidad,
        string PrecioUnitario,
        string Descuento,
        string Subtotal)
    {
        public static SaleLineGridRow FromLine(SalesHistoryLine line)
        {
            return new SaleLineGridRow(
                line.Producto,
                line.Codigo,
                line.Cantidad,
                FormatDollars(line.PrecioUnitarioCentavos),
                FormatDollars(line.DescuentoCentavos),
                FormatDollars(line.SubtotalCentavos));
        }
    }

    private sealed record SalePaymentGridRow(
        string Metodo,
        string Monto,
        string Referencia)
    {
        public static SalePaymentGridRow FromPayment(SalesHistoryPayment payment)
        {
            return new SalePaymentGridRow(
                payment.MetodoPago.ToString(),
                FormatDollars(payment.MontoCentavos),
                payment.Referencia ?? string.Empty);
        }
    }

    private static DateTime ToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => value,
            DateTimeKind.Utc => value.ToLocalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
        };
    }

    private static string FormatDollars(long centavos)
    {
        return (centavos / 100m).ToString("0.00");
    }
}
