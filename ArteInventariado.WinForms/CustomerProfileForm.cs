using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class CustomerProfileForm : Form
{
    private readonly int _clienteId;
    private readonly ICustomerQueryService _customerQueryService;
    private readonly ICustomerService _customerService;
    private readonly ICreditService _creditService;
    private readonly IAccountPaymentService _accountPaymentService;
    private readonly IAccountPaymentQueryService _accountPaymentQueryService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ICurrentSession _currentSession;
    private readonly IServiceProvider _serviceProvider;
    private readonly BindingSource _historyBinding = new();
    private readonly BindingSource _accountsBinding = new();
    private readonly BindingSource _ordersBinding = new();
    private readonly Dictionary<string, Label> _valueLabels = [];
    private readonly Dictionary<string, Label> _creditLabels = [];
    private readonly ToolTip _toolTip = new();
    private readonly Label _lblTitle = new();
    private readonly Label _lblStatus = new();
    private readonly TabControl _rightTabs = new();
    private readonly TabPage _creditTab = new("Cuentas por cobrar");
    private readonly TabPage _ordersTab = new("Pedidos");
    private readonly DataGridView _gridHistory = new();
    private readonly DataGridView _gridAccounts = new();
    private readonly DataGridView _gridOrders = new();
    private readonly Button _btnEdit = new();
    private readonly Button _btnNewSale = new();
    private readonly Button _btnToggleState = new();
    private readonly Button _btnReceipt = new();
    private readonly Button _btnCreditPolicy = new();
    private readonly Button _btnAccountDetail = new();
    private readonly Button _btnRegisterPayment = new();
    private readonly Button _btnPaymentsHistory = new();
    private readonly Button _btnOpenAlerts = new();
    private readonly Button _btnClose = new();

    private CustomerDetail? _detail;
    private bool _canEdit;
    private bool _canViewHistory;
    private bool _canViewAccounts;
    private bool _canRegisterPayment;
    private bool _canViewAlerts;
    private bool _canViewOrders;

    public CustomerProfileForm(
        int clienteId,
        ICustomerQueryService customerQueryService,
        ICustomerService customerService,
        ICreditService creditService,
        IEmployeePermissionService permissionService,
        ICurrentSession currentSession,
        IServiceProvider serviceProvider,
        IAccountPaymentService accountPaymentService,
        IAccountPaymentQueryService accountPaymentQueryService)
    {
        _clienteId = clienteId;
        _customerQueryService = customerQueryService;
        _customerService = customerService;
        _creditService = creditService;
        _accountPaymentService = accountPaymentService;
        _accountPaymentQueryService = accountPaymentQueryService;
        _permissionService = permissionService;
        _currentSession = currentSession;
        _serviceProvider = serviceProvider;

        BuildUi();
        Load += CustomerProfileForm_Load;
    }

    private async void CustomerProfileForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER);
            _canEdit = await _permissionService.HasPermissionAsync(PermisoEmpleado.CLIENTE_EDITAR);
            _canViewHistory = await _permissionService.HasPermissionAsync(PermisoEmpleado.CLIENTE_HISTORIAL_VER);
            _canViewAccounts = await _permissionService.HasPermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER);
            _canRegisterPayment = await _permissionService.HasPermissionAsync(PermisoEmpleado.ABONO_REGISTRAR);
            _canViewAlerts = await _permissionService.HasPermissionAsync(PermisoEmpleado.ALERTAS_CREDITO_VER);
            _canViewOrders = await _permissionService.HasPermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER);
            _btnEdit.Visible = _canEdit;
            _btnToggleState.Visible = _currentSession.EsAdmin;
            _btnReceipt.Visible = _canViewHistory;
            _btnCreditPolicy.Visible = _currentSession.EsAdmin;
            SetCreditTabVisibility();
            SetOrdersTabVisibility();
            await LoadCustomerAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Perfil de cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Perfil de cliente";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1040, 720);
        MinimumSize = new Size(940, 620);
        MaximizeBox = true;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(_lblStatus, 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
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

        _lblTitle.Text = "Perfil de cliente";
        _lblTitle.Dock = DockStyle.Fill;
        _lblTitle.AutoSize = true;
        _lblTitle.AutoEllipsis = false;
        _lblTitle.Margin = new Padding(0, 0, 0, 6);
        UITheme.StyleLabelTitle(_lblTitle);

        var subtitle = new Label
        {
            Text = "Perfil completo, informacion general e historial de compras del cliente.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalBoldFont,
            Margin = new Padding(0)
        };

        panel.Controls.Add(_lblTitle, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildBody()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));

        layout.Controls.Add(BuildDetailsPanel(), 0, 0);
        layout.Controls.Add(BuildRightTabs(), 1, 0);
        return layout;
    }

    private Control BuildRightTabs()
    {
        _rightTabs.Dock = DockStyle.Fill;
        _rightTabs.Margin = new Padding(10, 0, 0, 0);

        var historyTab = new TabPage("Compras") { BackColor = UITheme.BackgroundColor };
        historyTab.Controls.Add(BuildHistoryPanel());
        _creditTab.BackColor = UITheme.BackgroundColor;
        _creditTab.Controls.Add(BuildCreditPanel());

        _rightTabs.TabPages.Add(historyTab);
        _rightTabs.TabPages.Add(_creditTab);
        _ordersTab.BackColor = UITheme.BackgroundColor;
        _ordersTab.Controls.Add(BuildOrdersPanel());
        _rightTabs.TabPages.Add(_ordersTab);
        return _rightTabs;
    }

    private Control BuildOrdersPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Text = "Pedidos personalizados", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelHeader(title);
        _gridOrders.Dock = DockStyle.Fill;
        _gridOrders.ReadOnly = true;
        _gridOrders.AllowUserToAddRows = false;
        _gridOrders.AllowUserToDeleteRows = false;
        _gridOrders.AutoGenerateColumns = false;
        _gridOrders.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridOrders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridOrders.RowHeadersVisible = false;
        UITheme.StyleDataGridView(_gridOrders);
        foreach (var (property, header, weight) in new[] {
            ("Codigo","Pedido",80),("Fecha","Fecha",65),("Prometida","Prometida",70),
            ("Estado","Estado",75),("Financiero","Financiero",75),("Prioridad","Prioridad",55),
            ("Total","Total",60),("Pagado","Pagado",60),("Saldo","Saldo",60)})
            _gridOrders.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = header, FillWeight = weight });
        _gridOrders.DataSource = _ordersBinding;
        _gridOrders.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await OpenSelectedOrderAsync(); };
        layout.Controls.Add(title);
        layout.Controls.Add(_gridOrders);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildDetailsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 10, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddInfoRow(layout, 0, "Codigo", "Codigo");
        AddInfoRow(layout, 1, "Nombre", "Nombre");
        AddInfoRow(layout, 2, "Telefono", "Telefono");
        AddInfoRow(layout, 3, "WhatsApp", "WhatsApp");
        AddInfoRow(layout, 4, "Correo", "Correo");
        AddInfoRow(layout, 5, "DUI/documento", "Documento");
        AddInfoRow(layout, 6, "Direccion", "Direccion");
        AddInfoRow(layout, 7, "Estado", "Estado");
        AddInfoRow(layout, 8, "Creacion", "Creacion");
        AddInfoRow(layout, 9, "Actualizacion", "Actualizacion");

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildHistoryPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Compras",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelHeader(title);

        ConfigureHistoryGrid();
        _gridHistory.DataSource = _historyBinding;
        _gridHistory.CellDoubleClick += async (_, _) => await OpenSelectedReceiptAsync();
        _gridHistory.SelectionChanged += (_, _) => _btnReceipt.Enabled = _historyBinding.Current is not null;

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_gridHistory, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildCreditPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildCreditSummary(), 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 4),
            Margin = Padding.Empty
        };

        ConfigureButton(_btnAccountDetail, "Detalle cuenta", 136, async (_, _) => await OpenSelectedAccountAsync());
        UITheme.StyleSecondaryButton(_btnAccountDetail);
        ConfigureButton(_btnPaymentsHistory, "Historial abonos", 150, async (_, _) => await OpenPaymentsHistoryAsync());
        UITheme.StyleSecondaryButton(_btnPaymentsHistory);
        ConfigureButton(_btnRegisterPayment, "Registrar abono", 150, async (_, _) => await OpenRegisterPaymentAsync());
        UITheme.StyleSuccessButton(_btnRegisterPayment);
        ConfigureButton(_btnOpenAlerts, "Alertas", 100, (_, _) => OpenCreditAlerts());
        UITheme.StyleSecondaryButton(_btnOpenAlerts);
        _btnOpenAlerts.Visible = _canViewAlerts;
        foreach (var button in new[] { _btnAccountDetail, _btnRegisterPayment, _btnPaymentsHistory, _btnOpenAlerts })
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(4, 0, 4, 0);
            button.Margin = new Padding(4, 0, 0, 0);
        }

        actions.Controls.AddRange([_btnOpenAlerts, _btnPaymentsHistory, _btnRegisterPayment, _btnAccountDetail]);

        ConfigureAccountsGrid();
        _gridAccounts.DataSource = _accountsBinding;
        _gridAccounts.CellDoubleClick += async (_, _) => await OpenSelectedAccountAsync();
        _gridAccounts.SelectionChanged += (_, _) => UpdateAccountActions();

        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_gridAccounts, 0, 2);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildCreditSummary()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            BackColor = UITheme.SurfaceColor
        };
        for (var i = 0; i < 4; i++)
        {
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        for (var i = 0; i < 3; i++)
        {
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        }

        AddCreditMetric(summary, 0, 0, "Situacion", "Situacion");
        AddCreditMetric(summary, 1, 0, "Deuda total", "Deuda");
        AddCreditMetric(summary, 2, 0, "Disponible", "Disponible");
        AddCreditMetric(summary, 3, 0, "Limite", "Limite");
        AddCreditMetric(summary, 0, 1, "Cuentas", "Cuentas");
        AddCreditMetric(summary, 1, 1, "Prox. vencimiento", "Vencimiento");
        AddCreditMetric(summary, 2, 1, "Credito", "Credito");
        AddCreditMetric(summary, 3, 1, "Interes", "Interes");
        AddCreditMetric(summary, 0, 2, "Plazo max.", "Plazo");
        AddCreditMetric(summary, 1, 2, "Saldo vigente", "SaldoVigente");
        AddCreditMetric(summary, 2, 2, "Ult. abono", "UltimoAbono");
        AddCreditMetric(summary, 3, 2, "Fecha abono", "FechaAbono");
        return summary;
    }

    private void AddCreditMetric(TableLayoutPanel layout, int column, int row, string labelText, string key)
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 8)
        };
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true
        };

        var value = new Label
        {
            Text = "-",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };

        _creditLabels[key] = value;
        container.Controls.Add(label, 0, 0);
        container.Controls.Add(value, 0, 1);
        layout.Controls.Add(container, column, row);
    }

    private Control BuildActions()
    {
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = UITheme.MutedTextColor;
        _lblStatus.Font = UITheme.NormalBoldFont;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };

        ConfigureButton(_btnClose, "Volver", 100, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnClose);
        ConfigureButton(_btnToggleState, "Desactivar", 120, async (_, _) => await ToggleStateAsync());
        UITheme.StyleDangerButton(_btnToggleState);
        ConfigureButton(_btnReceipt, "Ver compra", 128, async (_, _) => await OpenSelectedReceiptAsync());
        UITheme.StyleSecondaryButton(_btnReceipt);
        ConfigureButton(_btnNewSale, "Nueva venta", 138, async (_, _) => await OpenNewSaleAsync());
        UITheme.StylePrimaryButton(_btnNewSale);
        ConfigureButton(_btnCreditPolicy, "Politica credito", 146, async (_, _) => await EditCreditPolicyAsync());
        UITheme.StyleSecondaryButton(_btnCreditPolicy);
        ConfigureButton(_btnEdit, "Editar", 96, async (_, _) => await EditAsync());
        UITheme.StyleSecondaryButton(_btnEdit);

        actions.Controls.AddRange([_btnClose, _btnToggleState, _btnReceipt, _btnNewSale, _btnCreditPolicy, _btnEdit]);
        return actions;
    }

    private async Task LoadCustomerAsync()
    {
        _detail = await _customerQueryService.GetByIdAsync(_clienteId)
            ?? throw new InvalidOperationException("El cliente no existe.");

        _lblTitle.Text = $"{_detail.CodigoCliente} - {_detail.NombreCompleto}";
        SetValue("Codigo", _detail.CodigoCliente);
        SetValue("Nombre", _detail.NombreCompleto);
        SetValue("Telefono", _detail.TelefonoCelular);
        SetValue("WhatsApp", _detail.WhatsApp);
        SetValue("Correo", _detail.CorreoElectronico);
        SetValue("Documento", _detail.DocumentoIdentidad);
        SetValue("Direccion", _detail.Direccion);
        SetValue("Estado", _detail.Estado.ToString());
        SetValue("Creacion", $"{SaleReceiptViewModel.ToLocal(_detail.FechaCreacion):yyyy-MM-dd HH:mm} por {_detail.UsuarioCreacion}");
        SetValue("Actualizacion", $"{SaleReceiptViewModel.ToLocal(_detail.FechaActualizacion):yyyy-MM-dd HH:mm} por {_detail.UsuarioActualizacion ?? "-"}");

        _btnNewSale.Enabled = _detail.Estado == EstadoCliente.ACTIVO;
        _btnToggleState.Text = _detail.Estado == EstadoCliente.ACTIVO ? "Desactivar" : "Reactivar";

        if (_canViewHistory)
        {
            var purchases = await _customerQueryService.GetPurchaseHistoryAsync(_clienteId);
            _historyBinding.DataSource = purchases.Select(CustomerPurchaseRow.FromHistory).ToList();
            _lblStatus.Text = $"{purchases.Count} compra(s) asociadas.";
        }
        else
        {
            _historyBinding.DataSource = new List<CustomerPurchaseRow>();
            _gridHistory.Enabled = false;
            _lblStatus.Text = "No tiene permiso para consultar el historial de compras.";
        }

        if (_canViewAccounts)
        {
            await LoadCreditAsync();
        }

        if (_canViewOrders)
        {
            var query = (ICustomOrderQueryService)_serviceProvider.GetService(typeof(ICustomOrderQueryService))!;
            var orders = await query.SearchAsync(new CustomOrderFilter(ClienteId: _clienteId, PageSize: 250));
            _ordersBinding.DataSource = orders.Select(CustomerOrderRow.From).ToList();
        }

        _btnReceipt.Enabled = _historyBinding.Current is not null;
    }

    private async Task LoadCreditAsync()
    {
        try
        {
            var summary = await _creditService.GetCustomerSummaryAsync(_clienteId);
            var accounts = await _creditService.GetAccountsAsync(new AccountReceivableFilter(
                ClienteId: _clienteId,
                IncludeClosed: true));

            SetCreditValue("Situacion", summary.Situacion.ToString());
            SetCreditValue("Deuda", SaleReceiptViewModel.FormatDollars(summary.SaldoActualCentavos));
            SetCreditValue("Disponible", SaleReceiptViewModel.FormatDollars(summary.CreditoDisponibleCentavos));
            SetCreditValue("Limite", SaleReceiptViewModel.FormatDollars(summary.LimiteCentavos));
            SetCreditValue("Cuentas", $"{summary.CuentasActivas}/{summary.MaximoCuentasActivas}");
            SetCreditValue("Vencimiento", summary.ProximoVencimiento.HasValue
                ? SaleReceiptViewModel.ToLocal(summary.ProximoVencimiento.Value).ToString("yyyy-MM-dd")
                : "-");
            SetCreditValue("Credito", summary.CreditoHabilitado ? "Habilitado" : $"Bloqueado: {summary.CreditoBloqueoMotivo ?? "-"}");
            SetCreditValue("Interes", $"{summary.Politica.InterestPercent:0.##}%");
            SetCreditValue("Plazo", $"{summary.Politica.MaxTermDays} dia(s)");
            SetCreditValue("SaldoVigente", summary.Politica.AllowOutstandingBalance ? "Permitido" : "No permitido");

            _accountsBinding.DataSource = accounts.Select(CustomerAccountRow.FromSummary).ToList();
            var payments = await _accountPaymentQueryService.GetHistoryAsync(new AccountPaymentHistoryFilter(ClienteId: _clienteId));
            var lastPayment = payments.FirstOrDefault();
            SetCreditValue("UltimoAbono", lastPayment is null ? "-" : SaleReceiptViewModel.FormatDollars(lastPayment.MontoCentavos));
            SetCreditValue("FechaAbono", lastPayment is null ? "-" : SaleReceiptViewModel.ToLocal(lastPayment.FechaAbono).ToString("yyyy-MM-dd HH:mm"));
            await LoadAlertIndicatorAsync();
            UpdateAccountActions();
        }
        catch (Exception ex)
        {
            _accountsBinding.DataSource = new List<CustomerAccountRow>();
            _lblStatus.Text = ex.Message;
        }
    }

    private async Task EditCreditPolicyAsync()
    {
        if (_detail is null)
        {
            return;
        }

        using var form = new CustomerCreditPolicyForm(
            _creditService,
            _clienteId,
            $"{_detail.CodigoCliente} - {_detail.NombreCompleto}");
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await LoadCustomerAsync();
        }
    }

    private async Task OpenSelectedAccountAsync()
    {
        if (_accountsBinding.Current is not CustomerAccountRow row)
        {
            return;
        }

        using var form = new AccountReceivableDetailForm(
            row.CuentaPorCobrarId,
            _creditService,
            _accountPaymentQueryService,
            _accountPaymentService,
            _permissionService);
        form.ShowDialog(this);
        await TryRefreshVisualAsync();
    }

    private async Task OpenRegisterPaymentAsync()
    {
        if (_accountsBinding.Current is not CustomerAccountRow row || !_canRegisterPayment || !row.CanRegisterPayment)
        {
            return;
        }

        await OpenRegisterPaymentLoopAsync(row.CuentaPorCobrarId);
    }

    private async Task OpenRegisterPaymentLoopAsync(int cuentaPorCobrarId)
    {
        var keepRegistering = true;
        while (keepRegistering)
        {
            keepRegistering = false;
            using var form = new AccountPaymentForm(
                cuentaPorCobrarId,
                _creditService,
                _accountPaymentService,
                _accountPaymentQueryService,
                _permissionService);
            var result = form.ShowDialog(this);
            if (result is DialogResult.OK or DialogResult.Retry)
            {
                await TryRefreshAfterConfirmedPaymentAsync();
            }

            if (result == DialogResult.Retry && CanRegisterPayment(cuentaPorCobrarId))
            {
                keepRegistering = true;
            }
        }
    }

    private async Task OpenPaymentsHistoryAsync()
    {
        var filter = _accountsBinding.Current is CustomerAccountRow row
            ? new AccountPaymentHistoryFilter(ClienteId: _clienteId, CuentaPorCobrarId: row.CuentaPorCobrarId)
            : new AccountPaymentHistoryFilter(ClienteId: _clienteId);

        using var form = new AccountPaymentsHistoryForm(
            _accountPaymentQueryService,
            _permissionService,
            filter);
        form.ShowDialog(this);
        await TryRefreshVisualAsync();
    }

    private void UpdateAccountActions()
    {
        var row = _accountsBinding.Current as CustomerAccountRow;
        _btnAccountDetail.Enabled = row is not null;
        _btnPaymentsHistory.Enabled = row is not null;
        _btnRegisterPayment.Visible = _canRegisterPayment;
        _btnRegisterPayment.Enabled = row is not null && _canRegisterPayment && row.CanRegisterPayment;
        _btnOpenAlerts.Visible = _canViewAlerts;
        _btnOpenAlerts.Enabled = _canViewAlerts;
    }

    private async Task LoadAlertIndicatorAsync()
    {
        if (!_canViewAlerts)
        {
            return;
        }

        try
        {
            var alertService = (ICreditAlertQueryService)_serviceProvider.GetService(typeof(ICreditAlertQueryService))!;
            var indicator = await alertService.GetCustomerIndicatorAsync(_clienteId);
            _lblStatus.Text = $"{_lblStatus.Text} Alertas: {indicator.Text}.";
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
        }
    }

    private void OpenCreditAlerts()
    {
        if (!_canViewAlerts)
        {
            return;
        }

        using var form = (CreditAlertsForm)_serviceProvider.GetService(typeof(CreditAlertsForm))!;
        form.SetInitialFilter(new CreditAlertFilter(ClienteId: _clienteId));
        form.ShowDialog(this);
    }

    private bool CanRegisterPayment(int cuentaPorCobrarId)
    {
        return _accountsBinding.DataSource is IEnumerable<CustomerAccountRow> rows &&
            rows.Any(row => row.CuentaPorCobrarId == cuentaPorCobrarId && row.CanRegisterPayment) &&
            _canRegisterPayment;
    }

    private async Task TryRefreshAfterConfirmedPaymentAsync()
    {
        try
        {
            await LoadCustomerAsync();
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
            MessageBox.Show(
                "El abono fue registrado, pero no se pudo actualizar una pantalla. Vuelva a abrir el modulo.",
                "Perfil de cliente",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private async Task TryRefreshVisualAsync()
    {
        try
        {
            await LoadCustomerAsync();
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
            MessageBox.Show(
                "No se pudo actualizar la pantalla. Vuelva a abrir el modulo.",
                "Perfil de cliente",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void SetCreditTabVisibility()
    {
        if (_canViewAccounts)
        {
            if (!_rightTabs.TabPages.Contains(_creditTab))
            {
                _rightTabs.TabPages.Add(_creditTab);
            }

            return;
        }

        if (_rightTabs.TabPages.Contains(_creditTab))
        {
            _rightTabs.TabPages.Remove(_creditTab);
        }
    }

    private void SetOrdersTabVisibility()
    {
        if (_canViewOrders)
        {
            if (!_rightTabs.TabPages.Contains(_ordersTab)) _rightTabs.TabPages.Add(_ordersTab);
        }
        else if (_rightTabs.TabPages.Contains(_ordersTab))
        {
            _rightTabs.TabPages.Remove(_ordersTab);
        }
    }

    private async Task OpenSelectedOrderAsync()
    {
        if (_ordersBinding.Current is not CustomerOrderRow row) return;
        var query = (ICustomOrderQueryService)_serviceProvider.GetService(typeof(ICustomOrderQueryService))!;
        var detail = await query.GetByIdAsync(row.Id);
        if (detail is null) return;
        var service = (ICustomOrderService)_serviceProvider.GetService(typeof(ICustomOrderService))!;
        var documents = (ICustomOrderDocumentService)_serviceProvider.GetService(typeof(ICustomOrderDocumentService))!;
        var finance = (ICustomOrderFinanceService)_serviceProvider.GetService(typeof(ICustomOrderFinanceService))!;
        var financeQuery = (ICustomOrderFinanceQueryService)_serviceProvider.GetService(typeof(ICustomOrderFinanceQueryService))!;
        var financeDocuments = (ICustomOrderFinanceDocumentService)_serviceProvider.GetService(typeof(ICustomOrderFinanceDocumentService))!;
        var products = (IProductService)_serviceProvider.GetService(typeof(IProductService))!;
        var catalog = (IPosCatalogService)_serviceProvider.GetService(typeof(IPosCatalogService))!;
        using var form = new CustomOrderDetailForm(
            detail, service, documents, _permissionService, _customerQueryService, products, catalog,
            finance, financeQuery, financeDocuments);
        form.ShowDialog(this);
        await LoadCustomerAsync();
    }

    private async Task EditAsync()
    {
        if (_detail is null)
        {
            return;
        }

        using var form = new CustomerEditForm(_customerService, _detail);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await LoadCustomerAsync();
        }
    }

    private async Task OpenNewSaleAsync()
    {
        var selection = await _customerQueryService.GetSaleSelectionAsync(_clienteId);
        if (selection is null)
        {
            MessageBox.Show("El cliente seleccionado ya no existe.", "Perfil de cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!selection.Activo)
        {
            MessageBox.Show("No se puede iniciar una venta con un cliente inactivo.", "Perfil de cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = (EmployeePOSForm)_serviceProvider.GetService(typeof(EmployeePOSForm))!;
        form.SetSelectedCustomer(selection);
        form.ShowDialog(this);
        await LoadCustomerAsync();
    }

    private async Task ToggleStateAsync()
    {
        if (_detail is null)
        {
            return;
        }

        var newState = _detail.Estado == EstadoCliente.ACTIVO ? EstadoCliente.INACTIVO : EstadoCliente.ACTIVO;
        var action = newState == EstadoCliente.ACTIVO ? "reactivar" : "desactivar";
        var motivo = PromptForText($"Motivo para {action} cliente", "Motivo obligatorio:");
        if (string.IsNullOrWhiteSpace(motivo))
        {
            return;
        }

        try
        {
            await _customerService.SetStateAsync(new CustomerStateChangeRequest(_detail.ClienteId, newState, motivo));
            await LoadCustomerAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Perfil de cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task OpenSelectedReceiptAsync()
    {
        if (_historyBinding.Current is not CustomerPurchaseRow row)
        {
            return;
        }

        try
        {
            var detail = await _customerQueryService.GetPurchaseDetailAsync(_clienteId, row.VentaId);
            if (detail is null)
            {
                MessageBox.Show("La venta seleccionada ya no existe.", "Perfil de cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var receiptForm = new SaleReceiptForm(
                SaleReceiptViewModel.FromSalesHistoryDetail(detail),
                isCompletedSale: false,
                primaryButtonText: "Cerrar");
            receiptForm.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Perfil de cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ConfigureHistoryGrid()
    {
        UITheme.StyleDataGridView(_gridHistory);
        _gridHistory.Dock = DockStyle.Fill;
        _gridHistory.ReadOnly = true;
        _gridHistory.AllowUserToAddRows = false;
        _gridHistory.AllowUserToDeleteRows = false;
        _gridHistory.AllowUserToResizeRows = false;
        _gridHistory.AutoGenerateColumns = false;
        _gridHistory.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridHistory.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridHistory.MultiSelect = false;
        _gridHistory.RowHeadersVisible = false;

        AddHistoryColumn("Fecha", "Fecha", 115, 16);
        AddHistoryColumn("NumeroVenta", "Venta", 130, 19);
        AddHistoryColumn("Usuario", "Usuario", 110, 16);
        AddHistoryColumn("Total", "Total", 86, 12, DataGridViewContentAlignment.MiddleRight);
        AddHistoryColumn("MetodosPago", "Pago", 130, 21);
        AddHistoryColumn("Estado", "Estado", 95, 14);
    }

    private void ConfigureAccountsGrid()
    {
        UITheme.StyleDataGridView(_gridAccounts);
        _gridAccounts.Dock = DockStyle.Fill;
        _gridAccounts.ReadOnly = true;
        _gridAccounts.AllowUserToAddRows = false;
        _gridAccounts.AllowUserToDeleteRows = false;
        _gridAccounts.AllowUserToResizeRows = false;
        _gridAccounts.AutoGenerateColumns = false;
        _gridAccounts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridAccounts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridAccounts.MultiSelect = false;
        _gridAccounts.RowHeadersVisible = false;

        AddAccountColumn("Codigo", "Cuenta", 130, 18);
        AddAccountColumn("Venta", "Venta", 110, 15);
        AddAccountColumn("Fecha", "Fecha", 102, 13);
        AddAccountColumn("Capital", "Capital", 86, 12, DataGridViewContentAlignment.MiddleRight);
        AddAccountColumn("Interes", "Interes", 86, 12, DataGridViewContentAlignment.MiddleRight);
        AddAccountColumn("Total", "Total", 86, 12, DataGridViewContentAlignment.MiddleRight);
        AddAccountColumn("Pagado", "Pagado", 86, 12, DataGridViewContentAlignment.MiddleRight);
        AddAccountColumn("Saldo", "Saldo", 86, 12, DataGridViewContentAlignment.MiddleRight);
        AddAccountColumn("Vence", "Vence", 102, 13);
        AddAccountColumn("Situacion", "Situacion", 110, 16);
    }

    private void AddHistoryColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private void AddAccountColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _gridAccounts.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private void AddInfoRow(TableLayoutPanel layout, int row, string labelText, string key)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 12, 8)
        };

        var value = new Label
        {
            Text = "-",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, 8)
        };

        _valueLabels[key] = value;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private void SetValue(string key, string? value)
    {
        if (_valueLabels.TryGetValue(key, out var label))
        {
            label.Text = string.IsNullOrWhiteSpace(value) ? "-" : value;
            _toolTip.SetToolTip(label, label.Text);
        }
    }

    private void SetCreditValue(string key, string? value)
    {
        if (_creditLabels.TryGetValue(key, out var label))
        {
            label.Text = string.IsNullOrWhiteSpace(value) ? "-" : value;
            _toolTip.SetToolTip(label, label.Text);
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

    private static string? PromptForText(string title, string labelText)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(460, 180),
            MinimumSize = new Size(420, 170),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        UITheme.StyleForm(form);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var label = new Label { Text = labelText, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont };
        var textBox = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        UITheme.StyleTextBox(textBox);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        var ok = new Button { Text = "Aceptar", Width = 100, Height = 34, DialogResult = DialogResult.OK };
        UITheme.StylePrimaryButton(ok);
        var cancel = new Button { Text = "Cancelar", Width = 100, Height = 34, DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        actions.Controls.AddRange([cancel, ok]);

        root.Controls.Add(label, 0, 0);
        root.Controls.Add(textBox, 0, 1);
        root.Controls.Add(actions, 0, 2);
        form.Controls.Add(root);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
    }

    private static void LogTechnicalError(Exception ex)
    {
        try
        {
            var logDirectory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "account-payments-ui.log"),
                $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Could not write account payment UI log.");
        }
    }

    private sealed record CustomerAccountRow(
        int CuentaPorCobrarId,
        string Codigo,
        string Venta,
        string Fecha,
        string Capital,
        string Interes,
        string Total,
        string Pagado,
        string Saldo,
        string Vence,
        string Situacion,
        bool CanRegisterPayment)
    {
        public static CustomerAccountRow FromSummary(AccountReceivableSummary summary)
        {
            return new CustomerAccountRow(
                summary.CuentaPorCobrarId,
                summary.CodigoCuenta,
                summary.NumeroVenta,
                SaleReceiptViewModel.ToLocal(summary.FechaCreacion).ToString("yyyy-MM-dd"),
                SaleReceiptViewModel.FormatDollars(summary.CapitalCentavos),
                SaleReceiptViewModel.FormatDollars(summary.InteresCentavos),
                SaleReceiptViewModel.FormatDollars(summary.TotalOriginalCentavos),
                SaleReceiptViewModel.FormatDollars(summary.TotalPagadoCentavos),
                SaleReceiptViewModel.FormatDollars(summary.SaldoCentavos),
                SaleReceiptViewModel.ToLocal(summary.FechaVencimiento).ToString("yyyy-MM-dd"),
                summary.Estado == EstadoCuentaPorCobrar.ABIERTA ? summary.Situacion.ToString() : summary.Estado.ToString(),
                summary.Estado == EstadoCuentaPorCobrar.ABIERTA && summary.SaldoCentavos > 0);
        }
    }

    private sealed record CustomerPurchaseRow(
        int VentaId,
        string Fecha,
        string NumeroVenta,
        string Usuario,
        string Total,
        string MetodosPago,
        string Estado)
    {
        public static CustomerPurchaseRow FromHistory(CustomerPurchaseHistoryRow row)
        {
            return new CustomerPurchaseRow(
                row.VentaId,
                SaleReceiptViewModel.ToLocal(row.FechaVenta).ToString("yyyy-MM-dd HH:mm"),
                row.NumeroVenta,
                row.Usuario,
                SaleReceiptViewModel.FormatDollars(row.TotalCentavos),
                row.MetodosPago,
                row.Estado.ToString());
        }
    }

    private sealed record CustomerOrderRow(
        int Id,
        string Codigo,
        string Fecha,
        string Prometida,
        string Estado,
        string Financiero,
        string Prioridad,
        string Total,
        string Pagado,
        string Saldo)
    {
        public static CustomerOrderRow From(CustomOrderSummary row) => new(
            row.PedidoPersonalizadoId,
            row.CodigoPedido,
            row.FechaPedido.ToString("yyyy-MM-dd"),
            row.FechaPrometida.ToString("yyyy-MM-dd"),
            CustomOrderStatusForm.Friendly(row.Estado),
            row.EstadoFinanciero.ToString().Replace('_', ' '),
            row.Prioridad.ToString(),
            SaleReceiptViewModel.FormatDollars(row.TotalEstimadoCentavos),
            SaleReceiptViewModel.FormatDollars(row.TotalPagadoCentavos),
            SaleReceiptViewModel.FormatDollars(row.SaldoPendienteCentavos));
    }
}
