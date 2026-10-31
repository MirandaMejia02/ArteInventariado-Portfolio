using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class PurchasesForm : Form
{
    private const int PageSize = 100;

    private readonly IPurchaseQueryService _purchaseQueryService;
    private readonly IPurchaseService _purchaseService;
    private readonly IPurchaseReceptionService _receptionService;
    private readonly ISupplierQueryService _supplierQueryService;
    private readonly IPosCatalogService _catalogService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IPurchasePaymentService? _purchasePaymentService;
    private readonly IAccountsPayableQueryService? _accountsPayableQueryService;
    private readonly IAccountsPayableService? _accountsPayableService;

    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbState = new();
    private readonly DateTimePicker _dtpFrom = new();
    private readonly DateTimePicker _dtpTo = new();
    private readonly CheckBox _chkUseDates = new();
    private readonly Button _btnSearch = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnEdit = new();
    private readonly Button _btnDetail = new();
    private readonly Button _btnReceive = new();
    private readonly Button _btnPrevious = new();
    private readonly Button _btnNext = new();
    private readonly Button _btnClose = new();
    private readonly Button _btnPayment = new();
    private readonly Button _btnAccount = new();
    private readonly Button _btnPaymentHistory = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblPending = new();
    private readonly Label _lblPartial = new();
    private readonly Label _lblReceived = new();
    private readonly Label _lblTotal = new();
    private readonly DataGridView _grid = new();

    private bool _canCreate;
    private bool _canEdit;
    private bool _canReceive;
    private bool _canRegisterPayment;
    private bool _canAccessAccounts;
    private bool _canViewPaymentHistory;
    private int _currentPage = 1;

    public PurchasesForm(
        IPurchaseQueryService purchaseQueryService,
        IPurchaseService purchaseService,
        IPurchaseReceptionService receptionService,
        ISupplierQueryService supplierQueryService,
        IPosCatalogService catalogService,
        IEmployeePermissionService permissionService,
        IPurchasePaymentService? purchasePaymentService = null,
        IAccountsPayableQueryService? accountsPayableQueryService = null,
        IAccountsPayableService? accountsPayableService = null)
    {
        _purchaseQueryService = purchaseQueryService;
        _purchaseService = purchaseService;
        _receptionService = receptionService;
        _supplierQueryService = supplierQueryService;
        _catalogService = catalogService;
        _permissionService = permissionService;
        _purchasePaymentService = purchasePaymentService;
        _accountsPayableQueryService = accountsPayableQueryService;
        _accountsPayableService = accountsPayableService;

        BuildUi();
        Load += PurchasesForm_Load;
    }

    private async void PurchasesForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRAS_ACCEDER);
            await LoadPermissionsAsync();
            await RefreshPurchasesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Compras";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(980, 620);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStats(), 0, 1);
        root.Controls.Add(BuildFilters(), 0, 2);
        root.Controls.Add(BuildGridSection(), 0, 3);
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
            Padding = new Padding(18, 12, 18, 12),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Compras a proveedores",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Registre compras, consulte recepciones y actualice inventario al recibir productos.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalBoldFont,
            Margin = new Padding(0)
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildStats()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        for (var i = 0; i < 4; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        panel.Controls.Add(BuildStatCard("Pendientes", _lblPending), 0, 0);
        panel.Controls.Add(BuildStatCard("Parciales", _lblPartial), 1, 0);
        panel.Controls.Add(BuildStatCard("Recibidas", _lblReceived), 2, 0);
        panel.Controls.Add(BuildStatCard("Total comprado", _lblTotal), 3, 0);
        return panel;
    }

    private static Control BuildStatCard(string title, Label valueLabel)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 14, 18, 16),
            Margin = new Padding(0, 0, 12, 0),
            MinimumSize = new Size(0, 96),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        valueLabel.Text = "-";
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = UITheme.HeaderFont;
        valueLabel.ForeColor = UITheme.PrimaryColor;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.AutoEllipsis = true;

        panel.Controls.Add(titleLabel, 0, 0);
        panel.Controls.Add(valueLabel, 0, 1);
        return panel;
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 14),
            ColumnCount = 1,
            RowCount = 3
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var searchLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12)
        };
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var criteriaLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12)
        };
        criteriaLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        criteriaLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        criteriaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        criteriaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        criteriaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        criteriaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));

        UITheme.StyleTextBox(_txtSearch);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                await SearchFromFirstPageAsync();
            }
        };

        _cmbState.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbState.DisplayMember = nameof(StateOption.Name);
        _cmbState.ValueMember = nameof(StateOption.Value);
        _cmbState.Font = UITheme.NormalFont;
        _cmbState.Dock = DockStyle.Fill;
        _cmbState.DataSource = new List<StateOption>
        {
            new(null, "Todos"),
            new(EstadoRecepcionCompra.PENDIENTE, "Pendiente"),
            new(EstadoRecepcionCompra.PARCIAL, "Parcial"),
            new(EstadoRecepcionCompra.RECIBIDA, "Recibida")
        };

        ConfigureDatePicker(_dtpFrom, DateTime.Today.AddDays(-30));
        ConfigureDatePicker(_dtpTo, DateTime.Today);
        _chkUseDates.Text = "Usar fechas";
        _chkUseDates.Dock = DockStyle.Fill;
        _chkUseDates.Font = UITheme.NormalBoldFont;
        _chkUseDates.ForeColor = UITheme.TextDark;
        _chkUseDates.TextAlign = ContentAlignment.MiddleLeft;
        _chkUseDates.Margin = new Padding(0, 6, 0, 0);

        ConfigureButton(_btnSearch, "Buscar", 118, async (_, _) => await SearchFromFirstPageAsync());
        UITheme.StylePrimaryButton(_btnSearch);
        ConfigureButton(_btnClear, "Limpiar", 118, async (_, _) => await ClearFiltersAsync());
        UITheme.StyleSecondaryButton(_btnClear);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 0)
        };
        actions.Controls.AddRange([_btnSearch, _btnClear]);

        var actionsHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 0)
        };
        actionsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionsHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionsHost.Controls.Add(actions, 1, 0);

        AddLabel(searchLayout, "Buscar por compra, proveedor o producto", 0);
        searchLayout.Controls.Add(_txtSearch, 0, 1);

        AddLabel(criteriaLayout, "Estado recepcion", 0);
        AddLabel(criteriaLayout, "Desde", 1);
        AddLabel(criteriaLayout, "Hasta", 2);
        AddLabel(criteriaLayout, "Fechas", 3);
        criteriaLayout.Controls.Add(_cmbState, 0, 1);
        criteriaLayout.Controls.Add(_dtpFrom, 1, 1);
        criteriaLayout.Controls.Add(_dtpTo, 2, 1);
        criteriaLayout.Controls.Add(_chkUseDates, 3, 1);

        panel.Controls.Add(searchLayout, 0, 0);
        panel.Controls.Add(criteriaLayout, 0, 1);
        panel.Controls.Add(actionsHost, 0, 2);
        return panel;
    }

    private Control BuildGridSection()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };

        ConfigureGrid();
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateActionState();
        _grid.CellDoubleClick += (_, _) => OpenDetail();
        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = UITheme.NormalBoldFont;
        _lblStatus.ForeColor = UITheme.MutedTextColor;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0, 6, 0, 0)
        };

        ConfigureButton(_btnClose, "Cerrar", 90, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnClose);
        ConfigureButton(_btnPaymentHistory, "Historial pagos", 135, (_, _) => OpenPaymentHistory());
        UITheme.StyleSecondaryButton(_btnPaymentHistory);
        ConfigureButton(_btnAccount, "Cuenta por pagar", 145, async (_, _) => await OpenAccountAsync());
        UITheme.StyleSecondaryButton(_btnAccount);
        ConfigureButton(_btnPayment, "Registrar pago", 135, async (_, _) => await RegisterInitialPaymentAsync());
        UITheme.StyleSuccessButton(_btnPayment);
        ConfigureButton(_btnReceive, "Registrar recepcion", 160, async (_, _) => await ReceiveSelectedAsync());
        UITheme.StyleSuccessButton(_btnReceive);
        ConfigureButton(_btnDetail, "Ver detalle", 110, (_, _) => OpenDetail());
        UITheme.StyleSecondaryButton(_btnDetail);
        ConfigureButton(_btnEdit, "Editar", 90, async (_, _) => await EditSelectedAsync());
        UITheme.StyleSecondaryButton(_btnEdit);
        ConfigureButton(_btnNew, "Nueva compra", 128, async (_, _) => await CreatePurchaseAsync());
        UITheme.StylePrimaryButton(_btnNew);
        ConfigureButton(_btnNext, "Siguiente", 100, async (_, _) => await ChangePageAsync(1));
        UITheme.StyleSecondaryButton(_btnNext);
        ConfigureButton(_btnPrevious, "Anterior", 100, async (_, _) => await ChangePageAsync(-1));
        UITheme.StyleSecondaryButton(_btnPrevious);

        actions.Controls.AddRange([_btnClose, _btnPaymentHistory, _btnAccount, _btnPayment, _btnReceive, _btnDetail, _btnEdit, _btnNew, _btnNext, _btnPrevious]);
        panel.Controls.Add(_lblStatus, 0, 0);
        panel.Controls.Add(actions, 0, 1);
        return panel;
    }

    private async Task LoadPermissionsAsync()
    {
        _canCreate = await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPRA_CREAR);
        _canEdit = await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPRA_EDITAR);
        _canReceive = await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPRA_RECEPCIONAR);
        _canRegisterPayment = _purchasePaymentService is not null && await _permissionService.HasPermissionAsync(PermisoEmpleado.PAGO_COMPRA_REGISTRAR);
        _canAccessAccounts = _accountsPayableQueryService is not null && await _permissionService.HasPermissionAsync(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER);
        _canViewPaymentHistory = _accountsPayableQueryService is not null && await _permissionService.HasPermissionAsync(PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER);

        _btnNew.Visible = _canCreate;
        _btnEdit.Visible = _canEdit;
        _btnReceive.Visible = _canReceive;
        _btnPayment.Visible = _canRegisterPayment;
        _btnAccount.Visible = _canAccessAccounts;
        _btnPaymentHistory.Visible = _canViewPaymentHistory;
    }

    private async Task RefreshPurchasesAsync()
    {
        SetBusy(true, "Cargando compras...");
        try
        {
            var filter = BuildFilter(_currentPage);
            var rows = await _purchaseQueryService.SearchAsync(filter);
            var stats = await _purchaseQueryService.GetStatsAsync(filter with { Page = 1, PageSize = 250 });

            _binding.DataSource = rows.Select(PurchaseGridRow.FromSummary).ToList();
            _lblPending.Text = stats.Pendientes.ToString();
            _lblPartial.Text = stats.Parciales.ToString();
            _lblReceived.Text = stats.Recibidas.ToString();
            _lblTotal.Text = SaleReceiptViewModel.FormatDollars(stats.TotalCompradoCentavos);
            _lblStatus.Text = $"Pagina {_currentPage} - {rows.Count} compra(s) mostrada(s).";
            _btnPrevious.Enabled = _currentPage > 1;
            _btnNext.Enabled = rows.Count == PageSize;
            UpdateActionState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private PurchaseFilter BuildFilter(int page)
    {
        var state = _cmbState.SelectedItem is StateOption stateOption ? stateOption.Value : null;
        return new PurchaseFilter(
            _txtSearch.Text,
            state,
            _dtpFrom.Value.Date,
            _dtpTo.Value.Date,
            _chkUseDates.Checked,
            page,
            PageSize);
    }

    private async Task ClearFiltersAsync()
    {
        _currentPage = 1;
        _txtSearch.Clear();
        _cmbState.SelectedIndex = 0;
        _chkUseDates.Checked = false;
        _dtpFrom.Value = DateTime.Today.AddDays(-30);
        _dtpTo.Value = DateTime.Today;
        await RefreshPurchasesAsync();
    }

    private async Task SearchFromFirstPageAsync()
    {
        _currentPage = 1;
        await RefreshPurchasesAsync();
    }

    private async Task ChangePageAsync(int delta)
    {
        _currentPage = Math.Max(1, _currentPage + delta);
        await RefreshPurchasesAsync();
    }

    private async Task CreatePurchaseAsync()
    {
        using var form = new PurchaseEditForm(_purchaseService, _supplierQueryService, _catalogService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            if (form.SavedPurchase is { } purchase && _canRegisterPayment && _purchasePaymentService is not null)
            {
                using var paymentForm = new PurchasePaymentForm(purchase, _purchasePaymentService);
                paymentForm.ShowDialog(this);
            }
            await RefreshPurchasesAsync();
        }
    }

    private async Task EditSelectedAsync()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        try
        {
            var detail = await _purchaseQueryService.GetByIdAsync(row.CompraId);
            if (detail is null)
            {
                MessageBox.Show("La compra seleccionada ya no existe.", "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await RefreshPurchasesAsync();
                return;
            }

            using var form = new PurchaseEditForm(_purchaseService, _supplierQueryService, _catalogService, detail);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                await RefreshPurchasesAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ReceiveSelectedAsync()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        try
        {
            var detail = await _purchaseQueryService.GetByIdAsync(row.CompraId);
            if (detail is null)
            {
                MessageBox.Show("La compra seleccionada ya no existe.", "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await RefreshPurchasesAsync();
                return;
            }

            using var form = new PurchaseReceptionForm(detail, _receptionService);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                await RefreshPurchasesAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Recepcion de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void OpenDetail()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        try
        {
            var detail = await _purchaseQueryService.GetByIdAsync(row.CompraId);
            if (detail is null)
            {
                MessageBox.Show("La compra seleccionada ya no existe.", "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await RefreshPurchasesAsync();
                return;
            }

            using var form = new PurchaseDetailForm(detail, _purchasePaymentService, _accountsPayableQueryService, _accountsPayableService);
            form.ShowDialog(this);
            await RefreshPurchasesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Detalle de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        _grid.ShowCellToolTips = true;

        AddGridColumn("CodigoCompra", "Compra", 120, 10);
        AddGridColumn("FechaCompra", "Fecha", 120, 10);
        AddGridColumn("CodigoProveedor", "Cod. proveedor", 120, 10);
        AddGridColumn("Proveedor", "Proveedor", 260, 28);
        AddGridColumn("Estado", "Estado", 110, 10, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Productos", "Productos", 90, 7, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Total", "Total", 120, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("EstadoFinanciero", "Estado pago", 110, 10, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Pagado", "Pagado", 110, 9, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("Saldo", "Saldo", 110, 9, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("Comprado", "Comprado", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Recibido", "Recibido", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Pendiente", "Pendiente", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("UsuarioRegistro", "Usuario", 120, 10);
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

    private void UpdateActionState()
    {
        var row = CurrentRow();
        var hasSelection = row is not null;
        _btnDetail.Enabled = hasSelection;
        _btnEdit.Enabled = hasSelection && _canEdit && row!.EstadoValue == EstadoRecepcionCompra.PENDIENTE && row.Recibido == 0;
        _btnReceive.Enabled = hasSelection && _canReceive && row!.Pendiente > 0;
        _btnPayment.Enabled = hasSelection && _canRegisterPayment && row!.TotalPagadoCentavos == 0 && row.SaldoPendienteCentavos > 0;
        _btnAccount.Enabled = hasSelection && _canAccessAccounts && row!.CuentaPorPagarId.HasValue;
        _btnPaymentHistory.Enabled = hasSelection && _canViewPaymentHistory && row!.CuentaPorPagarId.HasValue;
    }

    private PurchaseGridRow? CurrentRow()
    {
        return _binding.Current as PurchaseGridRow;
    }

    private async Task RegisterInitialPaymentAsync()
    {
        if (_purchasePaymentService is null || CurrentRow() is not { } row) return;
        var detail = await _purchaseQueryService.GetByIdAsync(row.CompraId);
        if (detail is null) return;
        using var form = new PurchasePaymentForm(detail, _purchasePaymentService);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshPurchasesAsync();
    }

    private async Task OpenAccountAsync()
    {
        if (_accountsPayableQueryService is null || CurrentRow() is not { CuentaPorPagarId: int accountId }) return;
        var detail = await _accountsPayableQueryService.GetDetailAsync(accountId);
        if (detail is null) return;
        using var form = new AccountPayableDetailForm(detail, _accountsPayableQueryService);
        form.ShowDialog(this);
    }

    private void OpenPaymentHistory()
    {
        if (_accountsPayableQueryService is null || CurrentRow() is not { CuentaPorPagarId: int accountId }) return;
        using var form = new SupplierPaymentsHistoryForm(_accountsPayableQueryService, accountId: accountId);
        form.ShowDialog(this);
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _btnSearch.Enabled = !busy;
        _btnClear.Enabled = !busy;
        _grid.Enabled = !busy;
        if (status is not null)
        {
            _lblStatus.Text = status;
        }
    }

    private static void ConfigureDatePicker(DateTimePicker picker, DateTime value)
    {
        picker.Format = DateTimePickerFormat.Short;
        picker.Value = value;
        picker.Dock = DockStyle.Fill;
        picker.Font = UITheme.NormalFont;
        picker.Margin = new Padding(0, 3, 10, 3);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        using var font = UITheme.ButtonFont;
        width = Math.Max(width, TextRenderer.MeasureText(text, font).Width + 32);
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.MinimumSize = new Size(width, 36);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Margin = new Padding(8, 0, 0, 6);
        button.Click += handler;
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int column)
    {
        AddLabel(layout, text, column, 0);
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

    private sealed record StateOption(EstadoRecepcionCompra? Value, string Name);

    private sealed record PurchaseGridRow(
        int CompraId,
        string CodigoCompra,
        string FechaCompra,
        string CodigoProveedor,
        string Proveedor,
        EstadoRecepcionCompra EstadoValue,
        string Estado,
        int Productos,
        string Total,
        EstadoFinancieroCompra EstadoFinanciero,
        string Pagado,
        string Saldo,
        long TotalPagadoCentavos,
        long SaldoPendienteCentavos,
        int? CuentaPorPagarId,
        int Comprado,
        int Recibido,
        int Pendiente,
        string UsuarioRegistro)
    {
        public static PurchaseGridRow FromSummary(PurchaseSummary summary)
        {
            return new PurchaseGridRow(
                summary.CompraId,
                summary.CodigoCompra,
                summary.FechaCompra.ToString("yyyy-MM-dd"),
                summary.CodigoProveedor,
                summary.Proveedor,
                summary.EstadoRecepcion,
                summary.EstadoRecepcion.ToString(),
                summary.Productos,
                SaleReceiptViewModel.FormatDollars(summary.TotalCentavos),
                summary.EstadoFinanciero,
                SaleReceiptViewModel.FormatDollars(summary.TotalPagadoCentavos),
                SaleReceiptViewModel.FormatDollars(summary.SaldoPendienteCentavos),
                summary.TotalPagadoCentavos,
                summary.SaldoPendienteCentavos,
                summary.CuentaPorPagarId,
                summary.CantidadComprada,
                summary.CantidadRecibida,
                summary.CantidadPendiente,
                summary.UsuarioRegistro);
        }
    }
}
