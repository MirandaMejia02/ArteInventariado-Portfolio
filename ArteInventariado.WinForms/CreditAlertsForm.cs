using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class CreditAlertsForm : Form
{
    private static readonly Color InfoBackColor = Color.FromArgb(255, 250, 225);
    private static readonly Color TodayBackColor = Color.FromArgb(255, 235, 210);
    private static readonly Color OverdueBackColor = Color.FromArgb(255, 226, 226);
    private static readonly Color MorosoBackColor = Color.FromArgb(245, 196, 205);
    private static readonly Color LimitBackColor = Color.FromArgb(255, 243, 210);
    private static readonly Color CriticalForeColor = Color.FromArgb(112, 31, 50);

    private readonly ICreditAlertQueryService _alertService;
    private readonly ICreditService _creditService;
    private readonly IAccountPaymentService _accountPaymentService;
    private readonly IAccountPaymentQueryService _accountPaymentQueryService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ICustomerQueryService _customerQueryService;
    private readonly ICustomerService _customerService;
    private readonly ICurrentSession _currentSession;
    private readonly IServiceProvider _serviceProvider;

    private readonly BindingSource _binding = new();
    private readonly ComboBox _cmbType = new();
    private readonly TextBox _txtSearch = new();
    private readonly CheckBox _chkFromDate = new();
    private readonly DateTimePicker _dtpFrom = new();
    private readonly CheckBox _chkToDate = new();
    private readonly DateTimePicker _dtpTo = new();
    private readonly CheckBox _chkOnlyOpen = new();
    private readonly Button _btnSearch = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnOpenCustomer = new();
    private readonly Button _btnOpenAccount = new();
    private readonly Button _btnRegisterPayment = new();
    private readonly Button _btnClose = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblFooter = new();
    private readonly Label _lblTotal = new();
    private readonly Label _lblDueToday = new();
    private readonly Label _lblOverdue = new();
    private readonly Label _lblMoroso = new();
    private readonly Label _lblNearLimit = new();
    private readonly Panel _emptyPanel = new();
    private readonly TableLayoutPanel _rootLayout = new();
    private readonly TableLayoutPanel _summaryCardsLayout = new();
    private readonly TableLayoutPanel _filterSecondaryLayout = new();
    private readonly TableLayoutPanel _dateFiltersLayout = new();
    private readonly TableLayoutPanel _filterCommandsLayout = new();
    private readonly TableLayoutPanel _footerLayout = new();
    private readonly FlowLayoutPanel _actionsPanel = new();
    private readonly List<Control> _summaryCards = [];

    private CreditAlertFilter? _initialFilter;
    private IReadOnlyList<CreditAlertRow> _rows = [];
    private bool _canRegisterPayment;
    private bool? _compactLayout;

    public CreditAlertsForm(
        ICreditAlertQueryService alertService,
        ICreditService creditService,
        IAccountPaymentService accountPaymentService,
        IAccountPaymentQueryService accountPaymentQueryService,
        IEmployeePermissionService permissionService,
        ICustomerQueryService customerQueryService,
        ICustomerService customerService,
        ICurrentSession currentSession,
        IServiceProvider serviceProvider)
    {
        _alertService = alertService;
        _creditService = creditService;
        _accountPaymentService = accountPaymentService;
        _accountPaymentQueryService = accountPaymentQueryService;
        _permissionService = permissionService;
        _customerQueryService = customerQueryService;
        _customerService = customerService;
        _currentSession = currentSession;
        _serviceProvider = serviceProvider;

        BuildUi();
        Load += CreditAlertsForm_Load;
        Resize += (_, _) => ApplyResponsiveLayout();
    }

    public void SetInitialFilter(CreditAlertFilter filter)
    {
        _initialFilter = filter;
    }

    private async void CreditAlertsForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.ALERTAS_CREDITO_VER);
            _canRegisterPayment = await _permissionService.HasPermissionAsync(PermisoEmpleado.ABONO_REGISTRAR);
            _btnRegisterPayment.Visible = _canRegisterPayment;
            ApplyInitialFilter();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Alertas de credito", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Alertas de credito";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(980, 640);
        MaximizeBox = true;
        UITheme.StyleForm(this);

        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.ColumnCount = 1;
        _rootLayout.RowCount = 5;
        _rootLayout.Padding = new Padding(22);
        _rootLayout.BackColor = UITheme.BackgroundColor;

        _rootLayout.Controls.Add(BuildHeader(), 0, 0);
        _rootLayout.Controls.Add(BuildSummaryCards(), 0, 1);
        _rootLayout.Controls.Add(BuildFilters(), 0, 2);
        _rootLayout.Controls.Add(BuildGridHost(), 0, 3);
        _rootLayout.Controls.Add(BuildFooter(), 0, 4);
        Controls.Add(_rootLayout);

        ApplyResponsiveLayout(force: true);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Alertas de credito",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Consulte vencimientos, mora y riesgos de la cartera de clientes.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalBoldFont
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildSummaryCards()
    {
        _summaryCardsLayout.Dock = DockStyle.Fill;
        _summaryCardsLayout.Margin = new Padding(0, 0, 0, 8);

        _summaryCards.Add(BuildSummaryCard("Total activas", _lblTotal, Color.FromArgb(255, 225, 238), UITheme.SecondaryColor));
        _summaryCards.Add(BuildSummaryCard("Vencen hoy", _lblDueToday, TodayBackColor, CriticalForeColor));
        _summaryCards.Add(BuildSummaryCard("Vencidas", _lblOverdue, OverdueBackColor, CriticalForeColor));
        _summaryCards.Add(BuildSummaryCard("Morosas", _lblMoroso, MorosoBackColor, CriticalForeColor));
        _summaryCards.Add(BuildSummaryCard("Cerca del limite", _lblNearLimit, LimitBackColor, UITheme.TextDark));
        return _summaryCardsLayout;
    }

    private static Control BuildSummaryCard(string title, Label value, Color backColor, Color foreColor)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = backColor,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 0, 10, 8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var lblTitle = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = foreColor,
            AutoEllipsis = true
        };

        value.Text = "-";
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.Font = UITheme.HeaderFont;
        value.ForeColor = foreColor;
        value.AutoEllipsis = true;

        panel.Controls.Add(lblTitle, 0, 0);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cmbType.Dock = DockStyle.Fill;
        _cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbType.Items.AddRange(GetTypeOptions().Cast<object>().ToArray());
        _cmbType.SelectedIndex = 0;

        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "Cliente, cuenta, venta o mensaje";
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await RefreshAsync();
            }
        };
        UITheme.StyleTextBox(_txtSearch);

        ConfigureDateFilter(_chkFromDate, _dtpFrom, "Usar fecha desde");
        ConfigureDateFilter(_chkToDate, _dtpTo, "Usar fecha hasta");

        _chkOnlyOpen.Text = "Solo abiertas";
        _chkOnlyOpen.Checked = true;
        _chkOnlyOpen.Dock = DockStyle.Fill;
        _chkOnlyOpen.Font = UITheme.NormalBoldFont;
        _chkOnlyOpen.TextAlign = ContentAlignment.MiddleLeft;

        _btnSearch.Text = "Buscar";
        _btnSearch.Dock = DockStyle.Fill;
        _btnSearch.Click += async (_, _) => await RefreshAsync();
        UITheme.StylePrimaryButton(_btnSearch);

        _btnClear.Text = "Limpiar";
        _btnClear.Dock = DockStyle.Fill;
        _btnClear.Click += async (_, _) => await ClearAsync();
        UITheme.StyleSecondaryButton(_btnClear);

        var primaryFilters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        primaryFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        primaryFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        primaryFilters.Controls.Add(BuildField("Tipo de alerta", _cmbType), 0, 0);
        primaryFilters.Controls.Add(BuildField("Cliente, cuenta, venta o mensaje", _txtSearch), 1, 0);

        _dateFiltersLayout.Dock = DockStyle.Fill;
        _dateFiltersLayout.ColumnCount = 2;
        _dateFiltersLayout.RowCount = 1;
        _dateFiltersLayout.Margin = new Padding(0);
        _dateFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _dateFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _dateFiltersLayout.Controls.Add(BuildDateField(_chkFromDate, _dtpFrom), 0, 0);
        _dateFiltersLayout.Controls.Add(BuildDateField(_chkToDate, _dtpTo), 1, 0);

        _filterCommandsLayout.Dock = DockStyle.Fill;
        _filterCommandsLayout.ColumnCount = 3;
        _filterCommandsLayout.RowCount = 1;
        _filterCommandsLayout.Margin = new Padding(0);
        _filterCommandsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _filterCommandsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        _filterCommandsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        _filterCommandsLayout.Controls.Add(_chkOnlyOpen, 0, 0);
        _filterCommandsLayout.Controls.Add(_btnSearch, 1, 0);
        _filterCommandsLayout.Controls.Add(_btnClear, 2, 0);

        _filterSecondaryLayout.Dock = DockStyle.Fill;
        _filterSecondaryLayout.Margin = new Padding(0);

        panel.Controls.Add(primaryFilters, 0, 0);
        panel.Controls.Add(_filterSecondaryLayout, 0, 1);
        return panel;
    }

    private Control BuildGridHost()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 0, 8)
        };

        ConfigureGrid();
        _emptyPanel.Dock = DockStyle.Fill;
        _emptyPanel.BackColor = UITheme.SurfaceColor;
        _emptyPanel.Visible = false;

        var empty = new Label
        {
            Text = "La cartera no presenta vencimientos ni riesgos activos.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.MutedTextColor
        };
        _emptyPanel.Controls.Add(empty);

        host.Controls.Add(_grid);
        host.Controls.Add(_emptyPanel);
        return host;
    }

    private Control BuildActions()
    {
        _actionsPanel.Dock = DockStyle.Fill;
        _actionsPanel.FlowDirection = FlowDirection.RightToLeft;
        _actionsPanel.WrapContents = false;
        _actionsPanel.Padding = new Padding(0, 8, 0, 0);
        _actionsPanel.Margin = new Padding(0);

        ConfigureButton(_btnRefresh, "Actualizar", 120, async (_, _) => await RefreshAsync());
        ConfigureButton(_btnRegisterPayment, "Registrar abono", 174, async (_, _) => await RegisterPaymentAsync());
        ConfigureButton(_btnOpenAccount, "Abrir cuenta", 140, async (_, _) => await OpenAccountAsync());
        ConfigureButton(_btnOpenCustomer, "Abrir cliente", 140, async (_, _) => await OpenCustomerAsync());
        ConfigureButton(_btnClose, "Cerrar", 100, (_, _) => Close());

        UITheme.StyleSecondaryButton(_btnClose);
        UITheme.StyleSecondaryButton(_btnOpenCustomer);
        UITheme.StyleSecondaryButton(_btnOpenAccount);
        UITheme.StyleSuccessButton(_btnRegisterPayment);
        UITheme.StylePrimaryButton(_btnRefresh);
        _actionsPanel.Controls.AddRange([_btnClose, _btnRefresh, _btnRegisterPayment, _btnOpenAccount, _btnOpenCustomer]);
        return _actionsPanel;
    }

    private Control BuildFooter()
    {
        _footerLayout.Dock = DockStyle.Fill;
        _footerLayout.Margin = new Padding(0);

        _lblFooter.Text = "0 alertas mostradas";
        _lblFooter.Dock = DockStyle.Fill;
        _lblFooter.TextAlign = ContentAlignment.MiddleLeft;
        _lblFooter.ForeColor = UITheme.MutedTextColor;
        _lblFooter.Font = UITheme.NormalBoldFont;

        _footerLayout.Controls.Add(_lblFooter);
        _footerLayout.Controls.Add(BuildActions());
        return _footerLayout;
    }

    private Control BuildField(string labelText, Control control)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0),
            BackColor = UITheme.SurfaceColor
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };

        wrapper.Controls.Add(label, 0, 0);
        wrapper.Controls.Add(control, 0, 1);
        return wrapper;
    }

    private static Control BuildDateField(CheckBox checkBox, DateTimePicker picker)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0),
            MinimumSize = new Size(180, 54),
            BackColor = UITheme.SurfaceColor
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        wrapper.Controls.Add(checkBox, 0, 0);
        wrapper.Controls.Add(picker, 0, 1);
        return wrapper;
    }

    private void ApplyResponsiveLayout(bool force = false)
    {
        var compact = ClientSize.Width < 1120;
        if (!force && _compactLayout == compact)
        {
            return;
        }

        _compactLayout = compact;
        SuspendLayout();
        _rootLayout.SuspendLayout();
        _summaryCardsLayout.SuspendLayout();
        _filterSecondaryLayout.SuspendLayout();
        _footerLayout.SuspendLayout();

        ConfigureRootRows(compact);
        ConfigureSummaryCards(compact);
        ConfigureSecondaryFilters(compact);
        ConfigureFooter(compact);

        _footerLayout.ResumeLayout(true);
        _filterSecondaryLayout.ResumeLayout(true);
        _summaryCardsLayout.ResumeLayout(true);
        _rootLayout.ResumeLayout(true);
        ResumeLayout(true);
    }

    private void ConfigureRootRows(bool compact)
    {
        _rootLayout.RowStyles.Clear();
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, compact ? 140 : 82));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, compact ? 184 : 150));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, compact ? 82 : 66));
    }

    private void ConfigureSummaryCards(bool compact)
    {
        _summaryCardsLayout.Controls.Clear();
        _summaryCardsLayout.ColumnStyles.Clear();
        _summaryCardsLayout.RowStyles.Clear();

        _summaryCardsLayout.ColumnCount = compact ? 3 : 5;
        _summaryCardsLayout.RowCount = compact ? 2 : 1;
        for (var column = 0; column < _summaryCardsLayout.ColumnCount; column++)
        {
            _summaryCardsLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100f / _summaryCardsLayout.ColumnCount));
        }

        for (var row = 0; row < _summaryCardsLayout.RowCount; row++)
        {
            _summaryCardsLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100f / _summaryCardsLayout.RowCount));
        }

        for (var index = 0; index < _summaryCards.Count; index++)
        {
            var column = compact ? index % 3 : index;
            var row = compact ? index / 3 : 0;
            _summaryCardsLayout.Controls.Add(_summaryCards[index], column, row);
        }
    }

    private void ConfigureSecondaryFilters(bool compact)
    {
        _filterSecondaryLayout.Controls.Clear();
        _filterSecondaryLayout.ColumnStyles.Clear();
        _filterSecondaryLayout.RowStyles.Clear();

        if (compact)
        {
            _filterSecondaryLayout.ColumnCount = 1;
            _filterSecondaryLayout.RowCount = 2;
            _filterSecondaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _filterSecondaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            _filterSecondaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            _filterSecondaryLayout.Controls.Add(_dateFiltersLayout, 0, 0);
            _filterSecondaryLayout.Controls.Add(_filterCommandsLayout, 0, 1);
            return;
        }

        _filterSecondaryLayout.ColumnCount = 2;
        _filterSecondaryLayout.RowCount = 1;
        _filterSecondaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        _filterSecondaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _filterSecondaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _filterSecondaryLayout.Controls.Add(_dateFiltersLayout, 0, 0);
        _filterSecondaryLayout.Controls.Add(_filterCommandsLayout, 1, 0);
    }

    private void ConfigureFooter(bool compact)
    {
        _footerLayout.Controls.Clear();
        _footerLayout.ColumnStyles.Clear();
        _footerLayout.RowStyles.Clear();

        if (compact)
        {
            _footerLayout.ColumnCount = 1;
            _footerLayout.RowCount = 2;
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _footerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            _footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _footerLayout.Controls.Add(_lblFooter, 0, 0);
            _footerLayout.Controls.Add(_actionsPanel, 0, 1);
            return;
        }

        _footerLayout.ColumnCount = 2;
        _footerLayout.RowCount = 1;
        _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _footerLayout.Controls.Add(_lblFooter, 0, 0);
        _footerLayout.Controls.Add(_actionsPanel, 1, 0);
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateActions();
        _grid.CellDoubleClick += async (_, _) => await OpenAccountAsync();
        _grid.DataBindingComplete += (_, _) => ApplyRowStyles();

        AddColumn("Severity", "Severidad", 100, 10, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Type", "Tipo", 126, 13);
        AddColumn("Cliente", "Cliente", 160, 18);
        AddColumn("Cuenta", "Cuenta", 118, 12);
        AddColumn("Venta", "Venta", 110, 11);
        AddColumn("Saldo", "Saldo", 82, 8, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Vencimiento", "Vencimiento", 96, 9, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Dias", "Dias", 70, 7, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("UltimoAbono", "Ultimo abono", 112, 10, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Message", "Mensaje", 220, 24);
    }

    private async Task RefreshAsync()
    {
        try
        {
            SetBusy(true);
            var alerts = await _alertService.GetAlertsAsync(BuildFilter());
            _rows = alerts.Select(CreditAlertRow.FromItem).ToList();
            _binding.DataSource = _rows;
            _emptyPanel.Visible = _rows.Count == 0;
            if (_emptyPanel.Visible)
            {
                _emptyPanel.BringToFront();
            }

            UpdateSummary(alerts);
            UpdateFooter();
            UpdateActions();
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
            MessageBox.Show(
                $"No se pudieron actualizar las alertas:\n\n{ex.Message}",
                "Alertas de credito",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ClearAsync()
    {
        _cmbType.SelectedIndex = 0;
        _txtSearch.Clear();
        _chkFromDate.Checked = false;
        _chkToDate.Checked = false;
        _dtpFrom.Value = DateTime.Today;
        _dtpTo.Value = DateTime.Today;
        _chkOnlyOpen.Checked = true;
        _initialFilter = null;
        await RefreshAsync();
    }

    private async Task OpenCustomerAsync()
    {
        if (_binding.Current is not CreditAlertRow row)
        {
            return;
        }

        using var form = new CustomerProfileForm(
            row.ClienteId,
            _customerQueryService,
            _customerService,
            _creditService,
            _permissionService,
            _currentSession,
            _serviceProvider,
            _accountPaymentService,
            _accountPaymentQueryService);
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task OpenAccountAsync()
    {
        if (_binding.Current is not CreditAlertRow row || !row.CuentaPorCobrarId.HasValue)
        {
            return;
        }

        using var form = new AccountReceivableDetailForm(
            row.CuentaPorCobrarId.Value,
            _creditService,
            _accountPaymentQueryService,
            _accountPaymentService,
            _permissionService);
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task RegisterPaymentAsync()
    {
        if (!_canRegisterPayment ||
            _binding.Current is not CreditAlertRow row ||
            !row.CuentaPorCobrarId.HasValue)
        {
            return;
        }

        var keepRegistering = true;
        while (keepRegistering)
        {
            keepRegistering = false;
            using var form = new AccountPaymentForm(
                row.CuentaPorCobrarId.Value,
                _creditService,
                _accountPaymentService,
                _accountPaymentQueryService,
                _permissionService);
            var result = form.ShowDialog(this);
            if (result is DialogResult.OK or DialogResult.Retry)
            {
                await RefreshAsync();
            }

            if (result == DialogResult.Retry && await CanRegisterPaymentAsync(row.CuentaPorCobrarId.Value))
            {
                keepRegistering = true;
            }
        }
    }

    private async Task<bool> CanRegisterPaymentAsync(int cuentaPorCobrarId)
    {
        var detail = await _creditService.GetAccountDetailAsync(cuentaPorCobrarId);
        return detail is not null &&
            detail.Summary.Estado == EstadoCuentaPorCobrar.ABIERTA &&
            detail.Summary.SaldoCentavos > 0 &&
            _canRegisterPayment;
    }

    private CreditAlertFilter BuildFilter()
    {
        var type = _cmbType.SelectedItem is AlertTypeOption option ? option.Type : null;
        return new CreditAlertFilter(
            type,
            _initialFilter?.ClienteId,
            string.IsNullOrWhiteSpace(_txtSearch.Text) ? _initialFilter?.Search : _txtSearch.Text,
            _chkFromDate.Checked ? _dtpFrom.Value.Date : _initialFilter?.FromDate,
            _chkToDate.Checked ? _dtpTo.Value.Date : _initialFilter?.ToDate,
            _chkOnlyOpen.Checked);
    }

    private void ApplyInitialFilter()
    {
        if (_initialFilter is null)
        {
            return;
        }

        if (_initialFilter.Type.HasValue)
        {
            for (var i = 0; i < _cmbType.Items.Count; i++)
            {
                if (_cmbType.Items[i] is AlertTypeOption option && option.Type == _initialFilter.Type)
                {
                    _cmbType.SelectedIndex = i;
                    break;
                }
            }
        }

        _txtSearch.Text = _initialFilter.Search ?? string.Empty;
        if (_initialFilter.FromDate.HasValue)
        {
            _chkFromDate.Checked = true;
            _dtpFrom.Value = _initialFilter.FromDate.Value.Date;
        }

        if (_initialFilter.ToDate.HasValue)
        {
            _chkToDate.Checked = true;
            _dtpTo.Value = _initialFilter.ToDate.Value.Date;
        }

        _chkOnlyOpen.Checked = _initialFilter.OnlyOpenAccounts;
    }

    private void UpdateSummary(IReadOnlyList<CreditAlertItem> alerts)
    {
        var summary = new CreditAlertSummary(
            alerts.Count,
            alerts.Count(a => a.Type == CreditAlertType.DueToday),
            alerts.Count(a => a.Type == CreditAlertType.UpcomingDue),
            alerts.Count(a => a.Type == CreditAlertType.Overdue),
            alerts.Count(a => a.Type == CreditAlertType.Moroso),
            alerts.Count(a => a.Type == CreditAlertType.NoRecentPayment),
            alerts.Count(a => a.Type == CreditAlertType.NearLimit),
            alerts.Count(a => a.Type == CreditAlertType.LimitReached));

        _lblTotal.Text = summary.TotalActive.ToString();
        _lblDueToday.Text = summary.DueToday.ToString();
        _lblOverdue.Text = summary.Overdue.ToString();
        _lblMoroso.Text = summary.Moroso.ToString();
        _lblNearLimit.Text = (summary.NearLimit + summary.LimitReached).ToString();
    }

    private void UpdateFooter()
    {
        var count = _rows.Count;
        _lblFooter.Text = count == 1 ? "1 alerta mostrada" : $"{count} alertas mostradas";
    }

    private void UpdateActions()
    {
        var row = _binding.Current as CreditAlertRow;
        _btnOpenCustomer.Enabled = row is not null;
        _btnOpenAccount.Enabled = row?.CuentaPorCobrarId.HasValue == true;
        _btnRegisterPayment.Enabled = _canRegisterPayment && row?.CuentaPorCobrarId.HasValue == true;
    }

    private void ApplyRowStyles()
    {
        foreach (DataGridViewRow gridRow in _grid.Rows)
        {
            if (gridRow.DataBoundItem is not CreditAlertRow row)
            {
                continue;
            }

            var (backColor, foreColor) = ColorsFor(row.AlertType);
            gridRow.DefaultCellStyle.BackColor = backColor;
            gridRow.DefaultCellStyle.ForeColor = foreColor;
            gridRow.DefaultCellStyle.SelectionBackColor = UITheme.SoftPinkColor;
            gridRow.DefaultCellStyle.SelectionForeColor = UITheme.TextDark;
            foreach (DataGridViewCell cell in gridRow.Cells)
            {
                cell.ToolTipText = $"{row.Type} - {row.Cliente} - {row.Message}";
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _btnSearch.Enabled = !busy;
        _btnRefresh.Enabled = !busy;
        _btnClear.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AddColumn(
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

    private static void ConfigureDateFilter(CheckBox checkBox, DateTimePicker picker, string text)
    {
        checkBox.Text = text;
        checkBox.Dock = DockStyle.Fill;
        checkBox.Font = UITheme.NormalBoldFont;
        checkBox.TextAlign = ContentAlignment.MiddleLeft;
        checkBox.AutoEllipsis = false;
        checkBox.CheckedChanged += (_, _) => picker.Enabled = checkBox.Checked;

        picker.Dock = DockStyle.Fill;
        picker.MinimumSize = new Size(140, 0);
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "yyyy-MM-dd";
        picker.ShowCheckBox = false;
        picker.Enabled = false;
        picker.Font = UITheme.NormalFont;
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private static IReadOnlyList<AlertTypeOption> GetTypeOptions()
    {
        return
        [
            new AlertTypeOption(null, "Todas"),
            new AlertTypeOption(CreditAlertType.DueToday, "Vencen hoy"),
            new AlertTypeOption(CreditAlertType.UpcomingDue, "Proximas a vencer"),
            new AlertTypeOption(CreditAlertType.Overdue, "Vencidas"),
            new AlertTypeOption(CreditAlertType.Moroso, "Morosas"),
            new AlertTypeOption(CreditAlertType.NoRecentPayment, "Sin abono reciente"),
            new AlertTypeOption(CreditAlertType.NearLimit, "Cerca del limite"),
            new AlertTypeOption(CreditAlertType.LimitReached, "Limite alcanzado")
        ];
    }

    private static (Color BackColor, Color ForeColor) ColorsFor(CreditAlertType type)
    {
        return type switch
        {
            CreditAlertType.DueToday => (TodayBackColor, CriticalForeColor),
            CreditAlertType.UpcomingDue => (InfoBackColor, UITheme.TextDark),
            CreditAlertType.Overdue => (OverdueBackColor, CriticalForeColor),
            CreditAlertType.Moroso => (MorosoBackColor, CriticalForeColor),
            CreditAlertType.NearLimit => (LimitBackColor, UITheme.TextDark),
            CreditAlertType.LimitReached => (OverdueBackColor, CriticalForeColor),
            CreditAlertType.NoRecentPayment => (InfoBackColor, UITheme.TextDark),
            _ => (UITheme.SurfaceColor, UITheme.TextDark)
        };
    }

    private static void LogTechnicalError(Exception ex)
    {
        try
        {
            var logDirectory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "credit-alerts-ui.log"),
                $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Could not write credit alerts UI log.");
        }
    }

    private sealed record AlertTypeOption(CreditAlertType? Type, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record CreditAlertRow(
        CreditAlertType AlertType,
        int ClienteId,
        int? CuentaPorCobrarId,
        string Severity,
        string Type,
        string Cliente,
        string Cuenta,
        string Venta,
        string Saldo,
        string Vencimiento,
        string Dias,
        string UltimoAbono,
        string Message)
    {
        public static CreditAlertRow FromItem(CreditAlertItem item)
        {
            return new CreditAlertRow(
                item.Type,
                item.ClienteId,
                item.CuentaPorCobrarId,
                item.SeverityText,
                item.TypeText,
                $"{item.CodigoCliente} - {item.Cliente}",
                item.CodigoCuenta ?? "-",
                item.NumeroVenta ?? "-",
                SaleReceiptViewModel.FormatDollars(item.SaldoCentavos),
                item.FechaVencimiento.HasValue ? SaleReceiptViewModel.ToLocal(item.FechaVencimiento.Value).ToString("yyyy-MM-dd") : "-",
                item.Dias.HasValue ? item.Dias.Value.ToString() : "-",
                item.FechaUltimoAbono.HasValue ? SaleReceiptViewModel.ToLocal(item.FechaUltimoAbono.Value).ToString("yyyy-MM-dd") : "-",
                item.Message);
        }
    }
}
