using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class AccountsPayableForm : Form
{
    private readonly IAccountsPayableQueryService _queryService;
    private readonly IAccountsPayableService _paymentService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly Button _btnClearSearch = new();
    private readonly ComboBox _cmbState = new();
    private readonly DateTimePicker _dtpFrom = new();
    private readonly DateTimePicker _dtpTo = new();
    private readonly CheckBox _chkDates = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblBalance = new();
    private readonly Label _lblOpen = new();
    private readonly Label _lblPartial = new();
    private readonly Label _lblOverdue = new();
    private readonly Label _lblToday = new();
    private readonly Label _lblUpcoming = new();
    private readonly Button _btnPayment = new();
    private readonly Button _btnHistory = new();
    private bool _canPay;
    private bool _canViewHistory;
    private LiveSearchController? _liveSearch;
    private bool _suppressFilterRefresh;

    public AccountsPayableForm(IAccountsPayableQueryService queryService, IAccountsPayableService paymentService, IEmployeePermissionService permissionService, IServiceProvider serviceProvider)
    {
        _queryService = queryService;
        _paymentService = paymentService;
        _permissionService = permissionService;
        _serviceProvider = serviceProvider;
        BuildUi();
        _liveSearch = new LiveSearchController(
            _txtSearch,
            _btnClearSearch,
            RefreshAsync,
            ShowSearchError);
        Load += LoadAsync;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void LoadAsync(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER);
            _canPay = await _permissionService.HasPermissionAsync(PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR);
            _canViewHistory = await _permissionService.HasPermissionAsync(PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER);
            _btnPayment.Visible = _canPay;
            _btnHistory.Visible = _canViewHistory;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cuentas por pagar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Cuentas por pagar";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 700);
        UITheme.StyleForm(this);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        var title = new Label { Text = "Cuentas por pagar a proveedores", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildStats(), 0, 1);
        root.Controls.Add(BuildFilters(), 0, 2);
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AutoGenerateColumns = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateActions();
        _grid.CellDoubleClick += async (_, _) => await ViewDetailAsync();
        root.Controls.Add(_grid, 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildStats()
    {
        var panel = new TableLayoutPanel { Name = "AccountsPayableStats", Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
        for (var i = 0; i < 6; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.67f));
        panel.Controls.Add(Card("Saldo pendiente", _lblBalance), 0, 0);
        panel.Controls.Add(Card("Abiertas", _lblOpen), 1, 0);
        panel.Controls.Add(Card("Parciales", _lblPartial), 2, 0);
        panel.Controls.Add(Card("Vencidas", _lblOverdue), 3, 0);
        panel.Controls.Add(Card("Vencen hoy", _lblToday), 4, 0);
        panel.Controls.Add(Card("Proximas 7 dias", _lblUpcoming), 5, 0);
        return panel;
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel
        {
            Name = "AccountsPayableFilters",
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        UITheme.StyleTextBox(_txtSearch);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "Compra, cuenta o proveedor";
        ConfigureClearSearchButton(_btnClearSearch);
        var searchRow = BuildFilterField("Buscar por compra, cuenta o proveedor", BuildSearchInput());

        _cmbState.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbState.Dock = DockStyle.Fill;
        _cmbState.DataSource = new object[] { "TODAS", EstadoCuentaPorPagar.ABIERTA, EstadoCuentaPorPagar.PARCIAL, EstadoCuentaPorPagar.VENCIDA, EstadoCuentaPorPagar.PAGADA };
        _dtpFrom.Format = _dtpTo.Format = DateTimePickerFormat.Short;
        _dtpFrom.Value = DateTime.Today.AddMonths(-1);
        _dtpTo.Value = DateTime.Today;
        _dtpFrom.Dock = _dtpTo.Dock = DockStyle.Fill;
        _dtpFrom.MinimumSize = _dtpTo.MinimumSize = new Size(140, 0);
        _chkDates.Text = "Usar fechas";
        _chkDates.Dock = DockStyle.Fill;
        _chkDates.Font = UITheme.NormalBoldFont;
        _chkDates.TextAlign = ContentAlignment.MiddleCenter;
        _chkDates.AutoSize = false;
        _chkDates.Margin = Padding.Empty;
        _cmbState.SelectedValueChanged += FilterChanged;
        _dtpFrom.ValueChanged += FilterChanged;
        _dtpTo.ValueChanged += FilterChanged;
        _chkDates.CheckedChanged += FilterChanged;
        var criteria = new TableLayoutPanel
        {
            Name = "AccountsPayableCriteria",
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty
        };
        criteria.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        criteria.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        criteria.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        criteria.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        criteria.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        criteria.Controls.Add(BuildFilterField("Estado", _cmbState), 0, 0);
        criteria.Controls.Add(BuildFilterField("Desde", _dtpFrom, new Padding(8, 0, 0, 0)), 1, 0);
        criteria.Controls.Add(BuildFilterField("Hasta", _dtpTo, new Padding(8, 0, 0, 0)), 2, 0);
        criteria.Controls.Add(_chkDates, 3, 0);
        var clear = Button("Limpiar", 120, UITheme.StyleSecondaryButton, async (_, _) => await ClearAsync());
        clear.Dock = DockStyle.Fill;
        clear.Margin = new Padding(8, 22, 0, 0);
        criteria.Controls.Add(clear, 4, 0);
        panel.Controls.Add(searchRow, 0, 0);
        panel.Controls.Add(criteria, 0, 1);
        return panel;
    }

    private Control BuildActions()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoScroll = false, Padding = new Padding(0, 10, 0, 0) };
        flow.Controls.Add(Button("Cerrar", 110, UITheme.StyleSecondaryButton, (_, _) => Close()));
        flow.Controls.Add(Button("Abrir proveedor", 150, UITheme.StyleSecondaryButton, async (_, _) => await OpenSupplierAsync()));
        _btnHistory.Text = "Historial de pagos";
        _btnHistory.Size = new Size(180, 42);
        _btnHistory.Margin = new Padding(8, 0, 0, 0);
        UITheme.StyleSecondaryButton(_btnHistory);
        UITheme.EnsureButtonContentFits(_btnHistory, 180, 42, 24);
        _btnHistory.Click += (_, _) => OpenHistory();
        flow.Controls.Add(_btnHistory);
        _btnPayment.Text = "Registrar pago";
        _btnPayment.Size = new Size(160, 42);
        _btnPayment.Margin = new Padding(8, 0, 0, 0);
        UITheme.StyleSuccessButton(_btnPayment);
        UITheme.EnsureButtonContentFits(_btnPayment, 160, 42, 24);
        _btnPayment.Click += async (_, _) => await RegisterPaymentAsync();
        flow.Controls.Add(_btnPayment);
        flow.Controls.Add(Button("Ver detalle", 130, UITheme.StylePrimaryButton, async (_, _) => await ViewDetailAsync()));
        return flow;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            EstadoCuentaPorPagar? state = _cmbState.SelectedItem is EstadoCuentaPorPagar value ? value : null;
            var filter = new AccountsPayableFilter(_txtSearch.Text, null, state, _dtpFrom.Value.Date, _dtpTo.Value.Date, _chkDates.Checked);
            var rows = await _queryService.SearchAsync(filter, cancellationToken);
            var stats = await _queryService.GetStatsAsync(filter, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _binding.DataSource = rows.Select(r => new AccountRow(r, r.CodigoCuenta, r.CodigoCompra, r.Proveedor,
                r.FechaCompra.ToString("yyyy-MM-dd"), r.FechaVencimiento.ToString("yyyy-MM-dd"),
                SaleReceiptViewModel.FormatDollars(r.TotalOriginalCentavos), SaleReceiptViewModel.FormatDollars(r.TotalPagadoCentavos),
                SaleReceiptViewModel.FormatDollars(r.SaldoPendienteCentavos), r.Estado.ToString(), r.Usuario)).ToList();
            _lblBalance.Text = SaleReceiptViewModel.FormatDollars(stats.SaldoTotalCentavos);
            _lblOpen.Text = stats.Abiertas.ToString(); _lblPartial.Text = stats.Parciales.ToString(); _lblOverdue.Text = stats.Vencidas.ToString();
            _lblToday.Text = stats.VencenHoy.ToString(); _lblUpcoming.Text = stats.ProximasAVencer.ToString();
            UpdateActions();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task ClearAsync()
    {
        _suppressFilterRefresh = true;
        try
        {
            _txtSearch.Clear(); _cmbState.SelectedIndex = 0; _chkDates.Checked = false;
            _dtpFrom.Value = DateTime.Today.AddMonths(-1); _dtpTo.Value = DateTime.Today;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        if (_liveSearch is not null)
        {
            await _liveSearch.SearchNowAsync();
        }
        else
        {
            await RefreshAsync();
        }
    }

    private async void FilterChanged(object? sender, EventArgs e)
    {
        if (!_suppressFilterRefresh && _liveSearch is not null)
        {
            await _liveSearch.SearchNowAsync();
        }
    }

    private Control BuildSearchInput()
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        host.Controls.Add(_txtSearch, 0, 0);
        host.Controls.Add(_btnClearSearch, 1, 0);
        return host;
    }

    private static void ConfigureClearSearchButton(Button button)
    {
        button.Text = "X";
        button.AccessibleName = "Limpiar búsqueda";
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 0, 0, 0);
        UITheme.StyleSecondaryButton(button);
    }

    private void ShowSearchError(Exception ex)
    {
        if (!IsDisposed)
        {
            MessageBox.Show(ex.Message, "Cuentas por pagar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ViewDetailAsync()
    {
        if (Current() is not { } row) return;
        var detail = await _queryService.GetDetailAsync(row.Source.CuentaPorPagarId);
        if (detail is null) return;
        using var form = new AccountPayableDetailForm(detail, _queryService);
        form.ShowDialog(this);
    }

    private async Task RegisterPaymentAsync()
    {
        if (Current() is not { Source.SaldoPendienteCentavos: > 0 } row) return;
        var detail = await _queryService.GetDetailAsync(row.Source.CuentaPorPagarId);
        if (detail is null) return;
        using var form = new SupplierPaymentForm(detail, _paymentService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            if (form.Result is not null)
            {
                var payment = await _queryService.GetPaymentAsync(form.Result.AbonoCuentaPorPagarId);
                if (payment is not null) using (var receipt = new SupplierPaymentReceiptForm(payment)) receipt.ShowDialog(this);
            }
            await RefreshAsync();
        }
    }

    private void OpenHistory()
    {
        if (!_canViewHistory)
        {
            return;
        }

        if (Current() is not { } row)
        {
            MessageBox.Show("Seleccione una cuenta por pagar.", "Historial de pagos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new SupplierPaymentsHistoryForm(_queryService, accountId: row.Source.CuentaPorPagarId);
        form.ShowDialog(this);
    }

    private async Task OpenSupplierAsync()
    {
        if (Current() is not { } row) return;
        try
        {
            var form = new SupplierProfileForm(
                row.Source.ProveedorId,
                (ISupplierQueryService)_serviceProvider.GetService(typeof(ISupplierQueryService))!,
                (ISupplierService)_serviceProvider.GetService(typeof(ISupplierService))!,
                _permissionService,
                (IProductService)_serviceProvider.GetService(typeof(IProductService))!,
                (IPurchaseQueryService)_serviceProvider.GetService(typeof(IPurchaseQueryService))!,
                (IPurchaseReceptionService)_serviceProvider.GetService(typeof(IPurchaseReceptionService))!,
                _queryService,
                _paymentService);
            using (form) form.ShowDialog(this);
            await RefreshAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void UpdateActions()
    {
        _btnPayment.Enabled = _canPay && Current()?.Source.SaldoPendienteCentavos > 0;
        _btnHistory.Enabled = _canViewHistory && Current() is not null;
    }
    private AccountRow? Current() => _binding.Current as AccountRow;
    private static Label Label(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = UITheme.NormalBoldFont };
    private static Control BuildFilterField(string labelText, Control control, Padding? margin = null)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = margin ?? Padding.Empty
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wrapper.Controls.Add(Label(labelText), 0, 0);
        wrapper.Controls.Add(control, 0, 1);
        return wrapper;
    }
    private static Button Button(string text, int width, Action<Button> style, EventHandler click)
    {
        var b = new Button { Text = text, Size = new Size(width, 42), Margin = new Padding(8, 0, 0, 0) };
        style(b);
        UITheme.EnsureButtonContentFits(b, width, 42, 24);
        b.TextAlign = ContentAlignment.MiddleCenter;
        b.Padding = Padding.Empty;
        b.Click += click;
        return b;
    }
    private static Control Card(string title, Label value)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(12), RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor }, 0, 0);
        value.Text = "-"; value.Dock = DockStyle.Fill; value.Font = UITheme.HeaderFont; value.ForeColor = UITheme.PrimaryColor; value.AutoEllipsis = true;
        panel.Controls.Add(value, 0, 1); return panel;
    }
    private sealed record AccountRow(AccountPayableSummary Source, string Cuenta, string Compra, string Proveedor, string Fecha, string Vencimiento, string Total, string Pagado, string Saldo, string Estado, string Usuario);
}
