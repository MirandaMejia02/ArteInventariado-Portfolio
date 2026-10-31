using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class AccountReceivableDetailForm : Form
{
    private readonly int _cuentaPorCobrarId;
    private readonly ICreditService _creditService;
    private readonly IAccountPaymentQueryService _paymentQueryService;
    private readonly IAccountPaymentService _paymentService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly BindingSource _linesBinding = new();
    private readonly BindingSource _paymentsBinding = new();
    private readonly Dictionary<string, Label> _summaryLabels = [];
    private readonly Dictionary<string, Label> _totalsLabels = [];
    private readonly ToolTip _toolTip = new();
    private readonly DataGridView _gridLines = new();
    private readonly DataGridView _gridPayments = new();
    private readonly Button _btnReceipt = new();
    private readonly Button _btnRegisterPayment = new();
    private readonly Button _btnBack = new();
    private readonly TableLayoutPanel _root = new();
    private readonly TableLayoutPanel _summaryPanel = new();

    private AccountReceivableDetail? _detail;
    private bool _canRegisterPayment;

    public AccountReceivableDetailForm(
        int cuentaPorCobrarId,
        ICreditService creditService,
        IAccountPaymentQueryService paymentQueryService,
        IAccountPaymentService paymentService,
        IEmployeePermissionService permissionService)
    {
        _cuentaPorCobrarId = cuentaPorCobrarId;
        _creditService = creditService;
        _paymentQueryService = paymentQueryService;
        _paymentService = paymentService;
        _permissionService = permissionService;
        BuildUi();
        Load += AccountReceivableDetailForm_Load;
    }

    private async void AccountReceivableDetailForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER);
            _canRegisterPayment = await _permissionService.HasPermissionAsync(PermisoEmpleado.ABONO_REGISTRAR);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Detalle de cuenta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Detalle de cuenta por cobrar";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(940, 660);
        MaximizeBox = true;
        UITheme.StyleForm(this);

        _root.Dock = DockStyle.Fill;
        _root.ColumnCount = 1;
        _root.RowCount = 4;
        _root.Padding = new Padding(18);
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        _root.Controls.Add(BuildSummary(), 0, 0);
        _root.Controls.Add(BuildTabs(), 0, 1);
        _root.Controls.Add(BuildTotals(), 0, 2);
        _root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(_root);
        Resize += (_, _) => ApplySummaryLayout();
    }

    private Control BuildSummary()
    {
        _summaryPanel.Dock = DockStyle.Fill;
        _summaryPanel.BackColor = UITheme.SurfaceColor;
        _summaryPanel.Padding = new Padding(14);
        _summaryPanel.Margin = new Padding(0, 0, 0, 10);
        ApplySummaryLayout();
        return _summaryPanel;
    }

    private void ApplySummaryLayout()
    {
        var useCompactLayout = ClientSize.Width > 0 && ClientSize.Width < 1020;
        var columnCount = useCompactLayout ? 2 : 4;
        var summaryHeight = useCompactLayout ? 342 : 228;
        if (_root.RowStyles.Count > 0)
        {
            _root.RowStyles[0].Height = summaryHeight;
        }

        _summaryPanel.SuspendLayout();
        _summaryPanel.Controls.Clear();
        _summaryPanel.ColumnStyles.Clear();
        _summaryPanel.RowStyles.Clear();
        _summaryPanel.ColumnCount = columnCount;
        _summaryPanel.RowCount = useCompactLayout ? 7 : 4;

        for (var i = 0; i < columnCount; i++)
        {
            _summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columnCount));
        }

        for (var i = 0; i < _summaryPanel.RowCount; i++)
        {
            _summaryPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, i == _summaryPanel.RowCount - 1 ? 62 : 48));
        }

        if (useCompactLayout)
        {
            AddMetric(_summaryPanel, 0, 0, "Cliente", "Cliente");
            AddMetric(_summaryPanel, 1, 0, "Cuenta", "Cuenta");
            AddMetric(_summaryPanel, 0, 1, "Venta", "Venta");
            AddMetric(_summaryPanel, 1, 1, "Estado", "Estado");
            AddMetric(_summaryPanel, 0, 2, "Capital", "Capital");
            AddMetric(_summaryPanel, 1, 2, "Interes", "Interes");
            AddMetric(_summaryPanel, 0, 3, "Total", "Total");
            AddMetric(_summaryPanel, 1, 3, "Pagado", "Pagado");
            AddMetric(_summaryPanel, 0, 4, "Saldo", "Saldo");
            AddMetric(_summaryPanel, 1, 4, "Vencimiento", "Vencimiento");
            AddMetric(_summaryPanel, 0, 5, "Caja", "Caja");
            AddMetric(_summaryPanel, 1, 5, "Usuario", "Usuario");
            AddMetric(_summaryPanel, 0, 6, "Observaciones", "Observaciones", columnSpan: 2);
        }
        else
        {
            AddMetric(_summaryPanel, 0, 0, "Cliente", "Cliente");
            AddMetric(_summaryPanel, 1, 0, "Cuenta", "Cuenta");
            AddMetric(_summaryPanel, 2, 0, "Venta", "Venta");
            AddMetric(_summaryPanel, 3, 0, "Estado", "Estado");
            AddMetric(_summaryPanel, 0, 1, "Capital", "Capital");
            AddMetric(_summaryPanel, 1, 1, "Interes", "Interes");
            AddMetric(_summaryPanel, 2, 1, "Total", "Total");
            AddMetric(_summaryPanel, 3, 1, "Pagado", "Pagado");
            AddMetric(_summaryPanel, 0, 2, "Saldo", "Saldo");
            AddMetric(_summaryPanel, 1, 2, "Vencimiento", "Vencimiento");
            AddMetric(_summaryPanel, 2, 2, "Caja", "Caja");
            AddMetric(_summaryPanel, 3, 2, "Usuario", "Usuario");
            AddMetric(_summaryPanel, 0, 3, "Observaciones", "Observaciones", columnSpan: 4);
        }

        _summaryPanel.ResumeLayout();
        if (_detail is not null)
        {
            SetSummaryValues(_detail);
        }
    }

    private Control BuildTotals()
    {
        var totals = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty
        };
        for (var i = 0; i < 4; i++)
        {
            totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        AddTotal(totals, 0, "TotalAbonado");
        AddTotal(totals, 1, "InteresAplicado");
        AddTotal(totals, 2, "CapitalAplicado");
        AddTotal(totals, 3, "SaldoActual");
        return totals;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        var productsTab = new TabPage("Productos financiados") { BackColor = UITheme.BackgroundColor };
        ConfigureLinesGrid();
        productsTab.Controls.Add(_gridLines);

        var paymentsTab = new TabPage("Historial de abonos") { BackColor = UITheme.BackgroundColor };
        ConfigurePaymentsGrid();
        paymentsTab.Controls.Add(_gridPayments);

        tabs.TabPages.Add(productsTab);
        tabs.TabPages.Add(paymentsTab);
        return tabs;
    }

    private Control BuildActions()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };

        ConfigureButton(_btnBack, "Volver", 110, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnBack);
        ConfigureButton(_btnReceipt, "Ver comprobante", 160, async (_, _) => await OpenReceiptAsync());
        _btnReceipt.Enabled = false;
        UITheme.StylePrimaryButton(_btnReceipt);
        ConfigureButton(_btnRegisterPayment, "Registrar abono", 160, async (_, _) => await RegisterPaymentAsync());
        UITheme.StyleSuccessButton(_btnRegisterPayment);

        actions.Controls.AddRange([_btnBack, _btnReceipt, _btnRegisterPayment]);
        return actions;
    }

    private async Task RefreshAsync()
    {
        _detail = await _creditService.GetAccountDetailAsync(_cuentaPorCobrarId);
        if (_detail is null)
        {
            MessageBox.Show("La cuenta seleccionada ya no existe.", "Detalle de cuenta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        Text = $"Cuenta por cobrar {_detail.Summary.CodigoCuenta}";
        SetSummaryValues(_detail);
        _linesBinding.DataSource = _detail.Lines.Select(LineRow.FromDetail).ToList();
        _gridLines.DataSource = _linesBinding;

        var payments = await _paymentQueryService.GetHistoryAsync(new AccountPaymentHistoryFilter(CuentaPorCobrarId: _cuentaPorCobrarId));
        _paymentsBinding.DataSource = payments.Select(PaymentRow.FromSummary).ToList();
        _gridPayments.DataSource = _paymentsBinding;
        _btnReceipt.Enabled = _paymentsBinding.Current is PaymentRow;
        _btnRegisterPayment.Visible = _canRegisterPayment;
        _btnRegisterPayment.Enabled = _canRegisterPayment &&
            _detail.Summary.Estado == EstadoCuentaPorCobrar.ABIERTA &&
            _detail.Summary.SaldoCentavos > 0;

        SetTotal("TotalAbonado", $"Total abonado: {SaleReceiptViewModel.FormatDollars(payments.Sum(p => p.MontoCentavos))}");
        SetTotal("InteresAplicado", $"Interes aplicado: {SaleReceiptViewModel.FormatDollars(payments.Sum(p => p.AplicadoInteresCentavos))}");
        SetTotal("CapitalAplicado", $"Capital aplicado: {SaleReceiptViewModel.FormatDollars(payments.Sum(p => p.AplicadoCapitalCentavos))}");
        SetTotal("SaldoActual", $"Saldo actual: {SaleReceiptViewModel.FormatDollars(_detail.Summary.SaldoCentavos)}");
    }

    private async Task RegisterPaymentAsync()
    {
        if (_detail is null || !_canRegisterPayment)
        {
            return;
        }

        var cuentaPorCobrarId = _detail.Summary.CuentaPorCobrarId;
        var keepRegistering = true;
        while (keepRegistering)
        {
            keepRegistering = false;
            using var form = new AccountPaymentForm(
                cuentaPorCobrarId,
                _creditService,
                _paymentService,
                _paymentQueryService,
                _permissionService);
            var result = form.ShowDialog(this);
            if (result is DialogResult.OK or DialogResult.Retry)
            {
                await TryRefreshAfterConfirmedPaymentAsync();
            }

            if (result == DialogResult.Retry &&
                _detail is not null &&
                _detail.Summary.Estado == EstadoCuentaPorCobrar.ABIERTA &&
                _detail.Summary.SaldoCentavos > 0 &&
                _canRegisterPayment)
            {
                keepRegistering = true;
            }
        }
    }

    private async Task OpenReceiptAsync()
    {
        if (_paymentsBinding.Current is not PaymentRow row)
        {
            return;
        }

        var detail = await _paymentQueryService.GetDetailAsync(row.AbonoCuentaPorCobrarId);
        if (detail is null)
        {
            MessageBox.Show("El abono seleccionado ya no existe.", "Detalle de cuenta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshAsync();
            return;
        }

        using var receipt = new AccountPaymentReceiptForm(
            AccountPaymentReceiptViewModel.FromDetail(detail),
            canNewPayment: false);
        receipt.ShowDialog(this);
    }

    private void SetSummaryValues(AccountReceivableDetail detail)
    {
        var summary = detail.Summary;
        SetValue("Cliente", summary.Cliente);
        SetValue("Cuenta", summary.CodigoCuenta);
        SetValue("Venta", summary.NumeroVenta);
        SetValue("Estado", $"{summary.Estado} / {summary.Situacion}".Replace('_', ' '));
        SetValue("Capital", SaleReceiptViewModel.FormatDollars(summary.CapitalCentavos));
        SetValue("Interes", SaleReceiptViewModel.FormatDollars(summary.InteresCentavos));
        SetValue("Total", SaleReceiptViewModel.FormatDollars(summary.TotalOriginalCentavos));
        SetValue("Pagado", SaleReceiptViewModel.FormatDollars(summary.TotalPagadoCentavos));
        SetValue("Saldo", SaleReceiptViewModel.FormatDollars(summary.SaldoCentavos));
        SetValue("Vencimiento", SaleReceiptViewModel.ToLocal(summary.FechaVencimiento).ToString("yyyy-MM-dd"));
        SetValue("Caja", detail.CajaId.ToString());
        SetValue("Usuario", detail.UsuarioRegistro);
        SetValue("Observaciones", string.IsNullOrWhiteSpace(detail.Observaciones) ? "Sin observaciones" : detail.Observaciones);
    }

    private async Task TryRefreshAfterConfirmedPaymentAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
            MessageBox.Show(
                "El abono fue registrado, pero no se pudo actualizar una pantalla. Vuelva a abrir el modulo.",
                "Detalle de cuenta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void ConfigureLinesGrid()
    {
        ConfigureGrid(_gridLines);
        _gridLines.DataSource = _linesBinding;
        AddColumn(_gridLines, "Producto", "Producto", 300, 42);
        AddColumn(_gridLines, "Cantidad", "Cant.", 80, 9, DataGridViewContentAlignment.MiddleCenter);
        AddColumn(_gridLines, "Precio", "Precio", 100, 11, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridLines, "Descuento", "Desc.", 100, 11, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridLines, "Subtotal", "Subtotal credito", 145, 14, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridLines, "Capital", "Capital pendiente", 155, 15, DataGridViewContentAlignment.MiddleRight);
    }

    private void ConfigurePaymentsGrid()
    {
        ConfigureGrid(_gridPayments);
        _gridPayments.DataSource = _paymentsBinding;
        _gridPayments.SelectionChanged += (_, _) => _btnReceipt.Enabled = _paymentsBinding.Current is PaymentRow;
        _gridPayments.CellDoubleClick += async (_, _) => await OpenReceiptAsync();
        AddColumn(_gridPayments, "Codigo", "Codigo", 130, 13);
        AddColumn(_gridPayments, "Fecha", "Fecha y hora", 130, 13);
        AddColumn(_gridPayments, "Metodo", "Metodo", 105, 10);
        AddColumn(_gridPayments, "Monto", "Monto", 90, 10, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridPayments, "Interes", "Interes", 90, 10, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridPayments, "Capital", "Capital", 90, 10, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridPayments, "SaldoAnterior", "Saldo anterior", 110, 12, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridPayments, "SaldoNuevo", "Saldo nuevo", 110, 12, DataGridViewContentAlignment.MiddleRight);
        AddColumn(_gridPayments, "Usuario", "Usuario", 100, 10);
        AddColumn(_gridPayments, "Referencia", "Referencia", 140, 14);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        UITheme.StyleDataGridView(grid);
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
    }

    private static void AddColumn(
        DataGridView grid,
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private static void AddFixedColumn(
        DataGridView grid,
        string property,
        string header,
        int width,
        DataGridViewContentAlignment alignment)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            MinimumWidth = width,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private void AddMetric(TableLayoutPanel layout, int column, int row, string labelText, string key, int columnSpan = 1)
    {
        var metric = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(8, 4, 8, 4),
            Padding = new Padding(0)
        };
        metric.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metric.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true
        };
        var value = new Label
        {
            Text = "-",
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };

        _summaryLabels[key] = value;
        metric.Controls.Add(label, 0, 0);
        metric.Controls.Add(value, 0, 1);
        layout.Controls.Add(metric, column, row);
        if (columnSpan > 1)
        {
            layout.SetColumnSpan(metric, columnSpan);
        }
    }

    private void AddTotal(TableLayoutPanel layout, int column, string key)
    {
        var label = new Label
        {
            Text = "-",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true,
            Margin = new Padding(8, 0, 8, 0)
        };
        _totalsLabels[key] = label;
        layout.Controls.Add(label, column, 0);
    }

    private void SetValue(string key, string value)
    {
        if (_summaryLabels.TryGetValue(key, out var label))
        {
            label.Text = value;
            _toolTip.SetToolTip(label, value);
        }
    }

    private void SetTotal(string key, string value)
    {
        if (_totalsLabels.TryGetValue(key, out var label))
        {
            label.Text = value;
            _toolTip.SetToolTip(label, value);
        }
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 38;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
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

    private sealed record LineRow(
        string Producto,
        int Cantidad,
        string Precio,
        string Descuento,
        string Subtotal,
        string Capital)
    {
        public static LineRow FromDetail(AccountReceivableLineDetail detail)
        {
            return new LineRow(
                $"{detail.Producto} ({detail.Codigo})",
                detail.CantidadFinanciada,
                SaleReceiptViewModel.FormatDollars(detail.PrecioUnitarioCentavos),
                SaleReceiptViewModel.FormatDollars(detail.DescuentoLineaCentavos + detail.DescuentoGeneralProporcionalCentavos),
                SaleReceiptViewModel.FormatDollars(detail.SubtotalFinanciadoCentavos),
                SaleReceiptViewModel.FormatDollars(detail.CapitalPendienteCentavos));
        }
    }

    private sealed record PaymentRow(
        int AbonoCuentaPorCobrarId,
        string Codigo,
        string Fecha,
        string Metodo,
        string Monto,
        string Interes,
        string Capital,
        string SaldoAnterior,
        string SaldoNuevo,
        string Usuario,
        string Referencia)
    {
        public static PaymentRow FromSummary(AccountPaymentSummary summary)
        {
            return new PaymentRow(
                summary.AbonoCuentaPorCobrarId,
                summary.CodigoAbono,
                SaleReceiptViewModel.ToLocal(summary.FechaAbono).ToString("yyyy-MM-dd HH:mm"),
                summary.MetodoPago.ToString(),
                SaleReceiptViewModel.FormatDollars(summary.MontoCentavos),
                SaleReceiptViewModel.FormatDollars(summary.AplicadoInteresCentavos),
                SaleReceiptViewModel.FormatDollars(summary.AplicadoCapitalCentavos),
                SaleReceiptViewModel.FormatDollars(summary.SaldoAnteriorCentavos),
                SaleReceiptViewModel.FormatDollars(summary.SaldoNuevoCentavos),
                summary.Usuario,
                summary.Referencia ?? "-");
        }
    }
}
