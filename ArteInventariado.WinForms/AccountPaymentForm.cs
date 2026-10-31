using System.Globalization;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class AccountPaymentForm : Form
{
    private readonly int _cuentaPorCobrarId;
    private readonly ICreditService _creditService;
    private readonly IAccountPaymentService _paymentService;
    private readonly IAccountPaymentQueryService _paymentQueryService;
    private readonly IEmployeePermissionService _permissionService;

    private readonly Dictionary<string, Label> _summaryLabels = [];
    private readonly Dictionary<string, Label> _previewLabels = [];
    private readonly TextBox _txtAmount = new();
    private readonly ComboBox _cmbMethod = new();
    private readonly TextBox _txtReference = new();
    private readonly TextBox _txtNotes = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnRegister = new();
    private readonly Button _btnCancel = new();
    private readonly ToolTip _toolTip = new();

    private AccountReceivableDetail? _account;
    private Guid _operationId = Guid.NewGuid();
    private bool _processing;
    private bool _registered;
    private int _previewVersion;

    public AccountPaymentForm(
        int cuentaPorCobrarId,
        ICreditService creditService,
        IAccountPaymentService paymentService,
        IAccountPaymentQueryService paymentQueryService,
        IEmployeePermissionService permissionService)
    {
        _cuentaPorCobrarId = cuentaPorCobrarId;
        _creditService = creditService;
        _paymentService = paymentService;
        _paymentQueryService = paymentQueryService;
        _permissionService = permissionService;

        BuildUi();
        Load += AccountPaymentForm_Load;
    }

    private async void AccountPaymentForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.ABONO_REGISTRAR);
            await LoadAccountAsync();
            await UpdatePreviewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ToFriendlyMessage(ex), "Registrar abono", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Registrar abono";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 740);
        MinimumSize = new Size(960, 720);
        MaximizeBox = true;
        KeyPreview = true;
        KeyDown += AccountPaymentForm_KeyDown;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 224));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildAccountSummary(), 0, 1);
        root.Controls.Add(BuildPaymentBody(), 0, 2);
        root.Controls.Add(_lblStatus, 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 8, 16, 8),
            Margin = new Padding(0, 0, 0, 10)
        };

        var title = new Label
        {
            Text = "Registrar abono",
            Dock = DockStyle.Left,
            Width = 330,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Cuenta por cobrar - abono a interes y capital",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.SubheaderFont
        };

        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildAccountSummary()
    {
        var panel = new TableLayoutPanel
        {
            Name = "AccountPaymentSummary",
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 10)
        };

        for (var i = 0; i < 4; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(
                SizeType.Percent,
                i switch
                {
                    0 => 22,
                    1 => 24,
                    2 => 24,
                    _ => 30
                }));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        }

        AddMetric(panel, 0, 0, "Cliente", "Cliente");
        AddMetric(panel, 1, 0, "Cuenta", "Cuenta");
        AddMetric(panel, 2, 0, "Venta", "Venta");
        AddMetric(panel, 3, 0, "Situacion", "Situacion");
        AddMetric(panel, 0, 1, "Vencimiento", "Vencimiento");
        AddMetric(panel, 1, 1, "Capital original", "Capital");
        AddMetric(panel, 2, 1, "Interes original", "Interes");
        AddMetric(panel, 3, 1, "Total original", "Total");
        AddMetric(panel, 0, 2, "Total pagado", "Pagado");
        AddMetric(panel, 1, 2, "Interes pendiente", "InteresPendiente");
        AddMetric(panel, 2, 2, "Capital pendiente", "CapitalPendiente");
        AddMetric(panel, 3, 2, "Saldo actual", "Saldo");
        AddMetric(panel, 0, 3, "Dias", "Dias");
        return panel;
    }

    private Control BuildPaymentBody()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        split.Controls.Add(BuildPaymentFields(), 0, 0);
        split.Controls.Add(BuildPreview(), 1, 0);
        return split;
    }

    private Control BuildPaymentFields()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            AutoScroll = true,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 10, 0)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));

        _txtAmount.Dock = DockStyle.Fill;
        _txtAmount.PlaceholderText = "0.00";
        _txtAmount.TextChanged += async (_, _) => await UpdatePreviewAsync();
        _txtAmount.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await RegisterAsync();
            }
        };
        UITheme.StyleTextBox(_txtAmount);

        _cmbMethod.Dock = DockStyle.Fill;
        _cmbMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMethod.Items.AddRange([
            new MethodOption("Efectivo", MetodoPago.EFECTIVO),
            new MethodOption("Transferencia", MetodoPago.TRANSFERENCIA),
            new MethodOption("Bitcoin", MetodoPago.BITCOIN)
        ]);
        _cmbMethod.SelectedIndex = 0;
        _cmbMethod.SelectedIndexChanged += async (_, _) =>
        {
            UpdateReferenceState();
            await UpdatePreviewAsync();
        };

        _txtReference.Dock = DockStyle.Fill;
        _txtReference.PlaceholderText = "Referencia para transferencia o Bitcoin";
        _txtReference.TextChanged += async (_, _) => await UpdatePreviewAsync();
        UITheme.StyleTextBox(_txtReference);

        _txtNotes.Dock = DockStyle.Fill;
        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.MinimumSize = new Size(0, 110);
        _txtNotes.PlaceholderText = "Observaciones";
        UITheme.StyleTextBox(_txtNotes);

        panel.Controls.Add(BuildField("Monto a abonar", _txtAmount), 0, 0);
        panel.Controls.Add(BuildField("Metodo", _cmbMethod), 0, 1);
        panel.Controls.Add(BuildField("Referencia", _txtReference), 0, 2);
        panel.Controls.Add(BuildField("Observaciones", _txtNotes), 0, 3);
        host.Controls.Add(panel);
        return host;
    }

    private Control BuildPreview()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(10, 0, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddPreviewRow(panel, 0, "Saldo anterior", "SaldoAnterior");
        AddPreviewRow(panel, 1, "Monto del abono", "Monto");
        AddPreviewRow(panel, 2, "Aplicado a interes", "Interes");
        AddPreviewRow(panel, 3, "Aplicado a capital", "Capital");
        AddPreviewRow(panel, 4, "Saldo nuevo", "SaldoNuevo");
        AddPreviewRow(panel, 5, "Estado resultante", "Estado");
        AddPreviewRow(panel, 6, "Caja", "Caja");
        AddPreviewRow(panel, 7, "Operacion", "Operacion");
        AddPreviewRow(panel, 8, "Validacion", "Validacion");
        return panel;
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
            Padding = new Padding(0, 12, 0, 0)
        };

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnCancel);
        ConfigureButton(_btnRegister, "Registrar abono", 160, async (_, _) => await RegisterAsync());
        UITheme.StyleSuccessButton(_btnRegister);

        actions.Controls.AddRange([_btnCancel, _btnRegister]);
        return actions;
    }

    private async Task LoadAccountAsync()
    {
        _account = await _creditService.GetAccountDetailAsync(_cuentaPorCobrarId)
            ?? throw new InvalidOperationException("La cuenta seleccionada ya no existe.");

        var summary = _account.Summary;
        SetSummary("Cliente", summary.Cliente);
        SetSummary("Cuenta", summary.CodigoCuenta);
        SetSummary("Venta", summary.NumeroVenta);
        SetSummary("Situacion", $"{summary.Estado} / {summary.Situacion}");
        SetSummary("Vencimiento", SaleReceiptViewModel.ToLocal(summary.FechaVencimiento).ToString("yyyy-MM-dd"));
        SetSummary("Capital", SaleReceiptViewModel.FormatDollars(summary.CapitalCentavos));
        SetSummary("Interes", SaleReceiptViewModel.FormatDollars(summary.InteresCentavos));
        SetSummary("Total", SaleReceiptViewModel.FormatDollars(summary.TotalOriginalCentavos));
        SetSummary("Pagado", SaleReceiptViewModel.FormatDollars(summary.TotalPagadoCentavos));

        var interestPaid = Math.Min(summary.TotalPagadoCentavos, summary.InteresCentavos);
        var interestPending = Math.Max(0, summary.InteresCentavos - interestPaid);
        var capitalPending = _account.Lines.Sum(line => line.CapitalPendienteCentavos);
        SetSummary("InteresPendiente", SaleReceiptViewModel.FormatDollars(interestPending));
        SetSummary("CapitalPendiente", SaleReceiptViewModel.FormatDollars(capitalPending));
        SetSummary("Saldo", SaleReceiptViewModel.FormatDollars(summary.SaldoCentavos));
        SetSummary("Dias", BuildDaysText(summary));

        _btnRegister.Enabled = summary.Estado == EstadoCuentaPorCobrar.ABIERTA && summary.SaldoCentavos > 0;
    }

    private async Task UpdatePreviewAsync()
    {
        var version = ++_previewVersion;
        if (!TryReadAmount(out var amount))
        {
            ClearPreview("Escriba un monto valido.");
            return;
        }

        if (amount <= 0)
        {
            ClearPreview("El monto debe ser mayor que cero.");
            return;
        }

        try
        {
            var preview = await _paymentQueryService.PreviewAsync(new AccountPaymentPreviewRequest(
                _cuentaPorCobrarId,
                amount,
                CurrentMethod(),
                _txtReference.Text));
            if (version != _previewVersion)
            {
                return;
            }

            SetPreview("SaldoAnterior", SaleReceiptViewModel.FormatDollars(preview.SaldoAnteriorCentavos));
            SetPreview("Monto", SaleReceiptViewModel.FormatDollars(preview.MontoCentavos));
            SetPreview("Interes", SaleReceiptViewModel.FormatDollars(preview.AplicadoInteresCentavos));
            SetPreview("Capital", SaleReceiptViewModel.FormatDollars(preview.AplicadoCapitalCentavos));
            SetPreview("SaldoNuevo", SaleReceiptViewModel.FormatDollars(preview.SaldoNuevoCentavos));
            SetPreview("Estado", preview.EstadoResultante.ToString());
            SetPreview("Caja", preview.CajaDescripcion ?? "-");
            SetPreview("Operacion", _operationId.ToString("N"));
            SetPreview("Validacion", "Lista para registrar.");
            _lblStatus.Text = "Vista previa calculada con reglas del servicio.";
        }
        catch (Exception ex)
        {
            ClearPreview(ToFriendlyMessage(ex));
        }
    }

    private async Task RegisterAsync()
    {
        if (_processing || _registered)
        {
            return;
        }

        if (!TryReadAmount(out var amount) || amount <= 0)
        {
            MessageBox.Show("El monto del abono debe ser mayor que cero.", "Registrar abono", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RegisterAccountPaymentResult result;
        try
        {
            SetBusy(true, "Registrando abono...");
            result = await _paymentService.RegisterPaymentAsync(new RegisterAccountPaymentRequest(
                _operationId,
                _cuentaPorCobrarId,
                amount,
                CurrentMethod(),
                _txtReference.Text,
                _txtNotes.Text));
            _registered = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ToFriendlyMessage(ex), "Registrar abono", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetBusy(false, ToFriendlyMessage(ex));
            return;
        }

        try
        {

            var detail = await _paymentQueryService.GetDetailAsync(result.AbonoCuentaPorCobrarId);
            if (detail is null)
            {
                MessageBox.Show("El abono fue registrado, pero no se pudo abrir el comprobante. Puede reabrirlo desde el historial.", "Registrar abono", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var canNewPayment = result.SaldoNuevoCentavos > 0 &&
                await _permissionService.HasPermissionAsync(PermisoEmpleado.ABONO_REGISTRAR);
            using var receipt = new AccountPaymentReceiptForm(
                AccountPaymentReceiptViewModel.FromDetail(detail),
                canNewPayment);
            var receiptResult = receipt.ShowDialog(this);
            if (receiptResult == DialogResult.Retry)
            {
                DialogResult = DialogResult.Retry;
                Close();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
            MessageBox.Show(
                "El abono fue registrado, pero no se pudo actualizar una pantalla. Vuelva a abrir el modulo.",
                "Registrar abono",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void AccountPaymentForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            _ = RegisterAsync();
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            Close();
            return;
        }

        if (e.Alt && e.KeyCode == Keys.E)
        {
            SelectMethod(MetodoPago.EFECTIVO);
        }
        else if (e.Alt && e.KeyCode == Keys.T)
        {
            SelectMethod(MetodoPago.TRANSFERENCIA);
        }
        else if (e.Alt && e.KeyCode == Keys.B)
        {
            SelectMethod(MetodoPago.BITCOIN);
        }
    }

    private void UpdateReferenceState()
    {
        var requiresReference = CurrentMethod() is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN;
        _txtReference.Enabled = requiresReference;
        if (!requiresReference)
        {
            _txtReference.Clear();
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _processing = busy;
        _txtAmount.Enabled = !busy;
        _cmbMethod.Enabled = !busy;
        _txtReference.Enabled = !busy && CurrentMethod() is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN;
        _txtNotes.Enabled = !busy;
        _btnRegister.Enabled = !busy && !_registered;
        _btnCancel.Enabled = !busy;
        _lblStatus.Text = status;
    }

    private bool TryReadAmount(out long cents)
    {
        cents = 0;
        var text = _txtAmount.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.Currency, CultureInfo.CurrentCulture, out var value) &&
            !decimal.TryParse(text, NumberStyles.Currency, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        cents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    private MetodoPago CurrentMethod()
    {
        return _cmbMethod.SelectedItem is MethodOption option
            ? option.Method
            : MetodoPago.EFECTIVO;
    }

    private void SelectMethod(MetodoPago method)
    {
        for (var i = 0; i < _cmbMethod.Items.Count; i++)
        {
            if (_cmbMethod.Items[i] is MethodOption option && option.Method == method)
            {
                _cmbMethod.SelectedIndex = i;
                break;
            }
        }
    }

    private void ClearPreview(string message)
    {
        foreach (var key in _previewLabels.Keys.ToList())
        {
            SetPreview(key, "-");
        }

        SetPreview("Operacion", _operationId.ToString("N"));
        SetPreview("Validacion", message);
        _lblStatus.Text = message;
    }

    private static string BuildDaysText(AccountReceivableSummary summary)
    {
        if (summary.Estado is EstadoCuentaPorCobrar.PAGADA or EstadoCuentaPorCobrar.ANULADA)
        {
            return "Cuenta cerrada";
        }

        var due = SaleReceiptViewModel.ToLocal(summary.FechaVencimiento).Date;
        var days = (due - DateTime.Today).Days;
        return days switch
        {
            > 0 => $"Vence en {days} dia(s)",
            0 => "Vence hoy",
            _ => $"{Math.Abs(days)} dia(s) de atraso"
        };
    }

    private Control BuildField(string labelText, Control control)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };
        wrapper.Controls.Add(label, 0, 0);
        wrapper.Controls.Add(control, 0, 1);
        return wrapper;
    }

    private void AddMetric(TableLayoutPanel layout, int column, int row, string labelText, string key)
    {
        var metric = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 8)
        };
        metric.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        metric.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true
        };
        var value = new Label
        {
            Text = "-",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };

        _summaryLabels[key] = value;
        metric.Controls.Add(label, 0, 0);
        metric.Controls.Add(value, 0, 1);
        layout.Controls.Add(metric, column, row);
    }

    private void AddPreviewRow(TableLayoutPanel layout, int row, string labelText, string key)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 11.11f));
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
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
        _previewLabels[key] = value;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private void SetSummary(string key, string value)
    {
        if (_summaryLabels.TryGetValue(key, out var label))
        {
            label.Text = value;
            _toolTip.SetToolTip(label, value);
        }
    }

    private void SetPreview(string key, string value)
    {
        if (_previewLabels.TryGetValue(key, out var label))
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
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Padding = Padding.Empty;
        button.UseCompatibleTextRendering = false;
        button.Click += handler;
    }

    private static string ToFriendlyMessage(Exception ex)
    {
        var message = ex.Message;
        return message switch
        {
            "El abono no puede superar el saldo actual de la cuenta." => "El monto supera el saldo actual de la cuenta.",
            "No se puede registrar un abono en efectivo sin caja abierta." => "Para registrar un abono en efectivo debe existir una caja abierta.",
            _ when message.Contains("actualizada por otra operacion", StringComparison.OrdinalIgnoreCase) =>
                "La cuenta fue actualizada por otra operacion. Revise el nuevo saldo.",
            _ when message.Contains("referencia", StringComparison.OrdinalIgnoreCase) =>
                "La referencia es obligatoria para Transferencia y Bitcoin.",
            _ => message
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
                Path.Combine(logDirectory, "account-payments-ui.log"),
                $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Could not write account payment UI log.");
        }
    }

    private sealed record MethodOption(string Text, MetodoPago Method)
    {
        public override string ToString() => Text;
    }
}
