using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class PurchasePaymentForm : Form
{
    private readonly PurchaseDetail _purchase;
    private readonly IPurchasePaymentService _service;
    private readonly NumericUpDown _numCash = CreateMoneyInput();
    private readonly NumericUpDown _numTransfer = CreateMoneyInput();
    private readonly NumericUpDown _numBitcoin = CreateMoneyInput();
    private readonly ComboBox _cmbCashOrigin = CreateOriginCombo();
    private readonly ComboBox _cmbTransferOrigin = CreateOriginCombo();
    private readonly ComboBox _cmbBitcoinOrigin = CreateOriginCombo();
    private readonly TextBox _txtCashResponsible = new();
    private readonly TextBox _txtTransferResponsible = new();
    private readonly TextBox _txtBitcoinResponsible = new();
    private readonly TextBox _txtTransferReference = new();
    private readonly TextBox _txtBitcoinReference = new();
    private readonly DateTimePicker _dtpDue = new();
    private readonly TextBox _txtObservations = new();
    private readonly Label _lblPaid = new();
    private readonly Label _lblBalance = new();
    private readonly Button _btnConfirm = new();
    private readonly ToolTip _toolTip = new();
    private readonly Dictionary<MetodoPago, Guid> _operationIds = new()
    {
        [MetodoPago.EFECTIVO] = Guid.NewGuid(),
        [MetodoPago.TRANSFERENCIA] = Guid.NewGuid(),
        [MetodoPago.BITCOIN] = Guid.NewGuid()
    };

    public PurchaseFinancialResult? Result { get; private set; }

    public PurchasePaymentForm(PurchaseDetail purchase, IPurchasePaymentService service)
    {
        _purchase = purchase;
        _service = service;
        BuildUi();
        UpdateTotals();
    }

    private void BuildUi()
    {
        Text = $"Pago de compra {_purchase.CodigoCompra}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 940);
        MinimumSize = new Size(760, 640);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 182));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var title = new Label { Text = "Pago inicial de compra", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildPaymentArea(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(16), ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        for (var i = 0; i < 3; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        AddSummary(panel, "Compra", _purchase.CodigoCompra, 0, 0);
        AddSummary(panel, "Proveedor", _purchase.Proveedor, 1, 0);
        AddSummary(panel, "Total", SaleReceiptViewModel.FormatDollars(_purchase.TotalCentavos), 2, 0);
        AddSummary(panel, "Pagado previamente", SaleReceiptViewModel.FormatDollars(_purchase.TotalPagadoCentavos), 0, 1);
        AddSummary(panel, "Pago ahora", "0.00", 1, 1, _lblPaid);
        AddSummary(panel, "Saldo resultante", SaleReceiptViewModel.FormatDollars(_purchase.SaldoPendienteCentavos), 2, 1, _lblBalance);
        return panel;
    }

    private Control BuildPaymentArea()
    {
        var viewport = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, AutoScroll = true };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 13,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 12; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));

        AddField(panel, "Efectivo ($)", _numCash, 0);
        AddField(panel, "Origen efectivo", _cmbCashOrigin, 1);
        AddField(panel, "Responsable/fuente", _txtCashResponsible, 2);
        AddField(panel, "Transferencia ($)", _numTransfer, 3);
        AddField(panel, "Origen transferencia", _cmbTransferOrigin, 4);
        AddField(panel, "Responsable/fuente", _txtTransferResponsible, 5);
        AddField(panel, "Referencia transferencia", _txtTransferReference, 6);
        AddField(panel, "Bitcoin ($)", _numBitcoin, 7);
        AddField(panel, "Origen Bitcoin", _cmbBitcoinOrigin, 8);
        AddField(panel, "Responsable/fuente", _txtBitcoinResponsible, 9);
        AddField(panel, "Referencia Bitcoin", _txtBitcoinReference, 10);
        _dtpDue.Format = DateTimePickerFormat.Custom;
        _dtpDue.CustomFormat = "yyyy-MM-dd";
        _dtpDue.Value = _purchase.FechaCompra.Date.AddDays(30) < DateTime.Today ? DateTime.Today : _purchase.FechaCompra.Date.AddDays(30);
        AddField(panel, "Fecha de vencimiento", _dtpDue, 11);
        _txtObservations.Multiline = true;
        _txtObservations.ScrollBars = ScrollBars.Vertical;
        AddField(panel, "Observaciones", _txtObservations, 12);

        _txtTransferReference.Name = "TransferReference";
        _txtBitcoinReference.Name = "BitcoinReference";
        _numCash.Name = "CashAmount";
        _numTransfer.Name = "TransferAmount";
        _numBitcoin.Name = "BitcoinAmount";
        _cmbCashOrigin.Name = "CashFundingOrigin";
        _cmbTransferOrigin.Name = "TransferFundingOrigin";
        _cmbBitcoinOrigin.Name = "BitcoinFundingOrigin";
        _txtCashResponsible.Name = "CashFundingResponsible";
        _txtTransferResponsible.Name = "TransferFundingResponsible";
        _txtBitcoinResponsible.Name = "BitcoinFundingResponsible";
        UITheme.StyleTextBox(_txtCashResponsible);
        UITheme.StyleTextBox(_txtTransferResponsible);
        UITheme.StyleTextBox(_txtBitcoinResponsible);
        ConfigureOriginCombo(_cmbCashOrigin, OrigenFondos.CAJA_FISICA);
        // Electronic payment methods must be explicitly classified. Leaving
        // CAJA_FISICA selected would incorrectly reduce the physical drawer.
        ConfigureOriginCombo(_cmbTransferOrigin, null);
        ConfigureOriginCombo(_cmbBitcoinOrigin, null);
        _cmbCashOrigin.SelectedIndexChanged += (_, _) => UpdateOriginState(_cmbCashOrigin, _txtCashResponsible);
        _cmbTransferOrigin.SelectedIndexChanged += (_, _) => UpdateOriginState(_cmbTransferOrigin, _txtTransferResponsible);
        _cmbBitcoinOrigin.SelectedIndexChanged += (_, _) => UpdateOriginState(_cmbBitcoinOrigin, _txtBitcoinResponsible);
        _numCash.ValueChanged += (_, _) => UpdateTotals();
        _numTransfer.ValueChanged += (_, _) =>
        {
            UpdateReferenceState(_numTransfer, _txtTransferReference);
            UpdateTotals();
        };
        _numBitcoin.ValueChanged += (_, _) =>
        {
            UpdateReferenceState(_numBitcoin, _txtBitcoinReference);
            UpdateTotals();
        };
        UITheme.StyleTextBox(_txtTransferReference);
        UITheme.StyleTextBox(_txtBitcoinReference);
        UITheme.StyleTextBox(_txtObservations);
        UpdateReferenceState(_numTransfer, _txtTransferReference);
        UpdateReferenceState(_numBitcoin, _txtBitcoinReference);
        UpdateOriginState(_cmbCashOrigin, _txtCashResponsible);
        UpdateOriginState(_cmbTransferOrigin, _txtTransferResponsible);
        UpdateOriginState(_cmbBitcoinOrigin, _txtBitcoinResponsible);
        viewport.Controls.Add(panel);
        return viewport;
    }

    private Control BuildActions()
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var cancel = new Button { Text = "Cancelar", Size = new Size(120, 42) };
        UITheme.StyleSecondaryButton(cancel);
        cancel.Click += (_, _) => Close();
        _btnConfirm.Text = "Confirmar";
        _btnConfirm.Size = new Size(160, 42);
        UITheme.StyleSuccessButton(_btnConfirm);
        _btnConfirm.Click += ConfirmAsync;
        var pending = new Button { Text = "Dejar pendiente", Size = new Size(190, 42) };
        UITheme.StylePrimaryButton(pending);
        pending.Click += (_, _) => { _numCash.Value = 0; _numTransfer.Value = 0; _numBitcoin.Value = 0; };
        var fullCash = new Button { Text = "Pagar total", Size = new Size(155, 42) };
        UITheme.StylePrimaryButton(fullCash);
        _toolTip.SetToolTip(fullCash, "Llena en efectivo el saldo pendiente. No registra el pago hasta presionar Confirmar.");
        fullCash.Click += (_, _) =>
        {
            _numTransfer.Value = 0;
            _numBitcoin.Value = 0;
            _numCash.Value = Math.Min(_numCash.Maximum, CentavosToDollars(_purchase.SaldoPendienteCentavos));
        };
        actions.Controls.AddRange([cancel, _btnConfirm, pending, fullCash]);
        return actions;
    }

    private async void ConfirmAsync(object? sender, EventArgs e)
    {
        if (!ValidateReferences())
        {
            return;
        }

        _btnConfirm.Enabled = false;
        try
        {
            var payments = new List<PurchasePaymentLineRequest>();
            AddPayment(payments, MetodoPago.EFECTIVO, _numCash, _txtCashResponsible, _cmbCashOrigin, null);
            AddPayment(payments, MetodoPago.TRANSFERENCIA, _numTransfer, _txtTransferResponsible, _cmbTransferOrigin, _txtTransferReference.Text);
            AddPayment(payments, MetodoPago.BITCOIN, _numBitcoin, _txtBitcoinResponsible, _cmbBitcoinOrigin, _txtBitcoinReference.Text);
            Result = await _service.RegisterInitialPaymentsAsync(new RegisterInitialPurchasePaymentsRequest(
                _purchase.CompraId, _dtpDue.Value.Date, payments, _txtObservations.Text));
            MessageBox.Show(
                Result.SaldoPendienteCentavos == 0 ? "Compra pagada completamente." : payments.Count == 0 ? "Compra dejada pendiente." : "Pago parcial registrado.",
                "Pago de compra", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Pago de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _btnConfirm.Enabled = true;
        }
    }

    private void AddPayment(ICollection<PurchasePaymentLineRequest> payments, MetodoPago method, NumericUpDown input, TextBox responsible, ComboBox origin, string? reference)
    {
        var amount = DollarsToCentavos(input.Value);
        if (amount > 0)
        {
            payments.Add(new PurchasePaymentLineRequest(
                _operationIds[method],
                method,
                amount,
                string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
                (OrigenFondos)origin.SelectedItem!,
                string.IsNullOrWhiteSpace(responsible.Text) ? null : responsible.Text.Trim()));
        }
    }

    private bool ValidateReferences()
    {
        if (_numTransfer.Value > 0 && string.IsNullOrWhiteSpace(_txtTransferReference.Text))
        {
            MessageBox.Show("Ingrese la referencia de la transferencia.", "Pago de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtTransferReference.Focus();
            return false;
        }

        if (_numBitcoin.Value > 0 && string.IsNullOrWhiteSpace(_txtBitcoinReference.Text))
        {
            MessageBox.Show("Ingrese la referencia de Bitcoin.", "Pago de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtBitcoinReference.Focus();
            return false;
        }

        foreach (var pair in new[]
        {
            (_numCash, MetodoPago.EFECTIVO, _cmbCashOrigin, _txtCashResponsible, "efectivo"),
            (_numTransfer, MetodoPago.TRANSFERENCIA, _cmbTransferOrigin, _txtTransferResponsible, "transferencia"),
            (_numBitcoin, MetodoPago.BITCOIN, _cmbBitcoinOrigin, _txtBitcoinResponsible, "Bitcoin")
        })
        {
            if (pair.Item1.Value <= 0)
            {
                continue;
            }

            if (pair.Item3.SelectedItem is not OrigenFondos origin)
            {
                MessageBox.Show($"Seleccione el origen de los fondos para el pago por {pair.Item5}.", "Pago de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pair.Item3.Focus();
                return false;
            }

            if (origin == OrigenFondos.CAJA_FISICA && pair.Item2 != MetodoPago.EFECTIVO)
            {
                MessageBox.Show("CAJA_FISICA solo puede usarse con efectivo. Para transferencia o Bitcoin, seleccione una fuente fuera de caja.", "Pago de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pair.Item3.Focus();
                return false;
            }

            if ((origin is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA) &&
                string.IsNullOrWhiteSpace(pair.Item4.Text))
            {
                MessageBox.Show("Indique el responsable o la fuente del dinero fuera de caja.", "Pago de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pair.Item4.Focus();
                return false;
            }
        }

        return true;
    }

    private static void UpdateReferenceState(NumericUpDown amount, TextBox reference)
    {
        reference.Enabled = amount.Value > 0;
        if (!reference.Enabled)
        {
            reference.Clear();
        }
    }

    private static ComboBox CreateOriginCombo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = UITheme.NormalFont };

    private static void ConfigureOriginCombo(ComboBox combo, OrigenFondos? defaultOrigin)
    {
        combo.Items.AddRange([OrigenFondos.CAJA_FISICA, OrigenFondos.FONDOS_EXTERNOS_DUENO, OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA]);
        combo.SelectedIndex = defaultOrigin.HasValue ? combo.Items.IndexOf(defaultOrigin.Value) : -1;
    }

    private static void UpdateOriginState(ComboBox origin, TextBox responsible)
    {
        var external = origin.SelectedItem is OrigenFondos.FONDOS_EXTERNOS_DUENO;
        responsible.Enabled = external || origin.SelectedItem is OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA;
        if (!responsible.Enabled) responsible.Clear();
    }

    private void UpdateTotals()
    {
        var paid = DollarsToCentavos(_numCash.Value + _numTransfer.Value + _numBitcoin.Value);
        _lblPaid.Text = SaleReceiptViewModel.FormatDollars(paid);
        _lblBalance.Text = SaleReceiptViewModel.FormatDollars(Math.Max(0, _purchase.SaldoPendienteCentavos - paid));
        _lblBalance.ForeColor = paid > _purchase.SaldoPendienteCentavos ? Color.Firebrick : UITheme.TextDark;
        _btnConfirm.Enabled = paid <= _purchase.SaldoPendienteCentavos;
    }

    private static NumericUpDown CreateMoneyInput() => new() { DecimalPlaces = 2, Minimum = 0, Maximum = 100000000, ThousandsSeparator = true, Dock = DockStyle.Fill, Font = UITheme.NormalFont };
    private static long DollarsToCentavos(decimal value) => checked((long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
    private static decimal CentavosToDollars(long value) => value / 100m;

    private static void AddField(TableLayoutPanel panel, string text, Control control, int row)
    {
        var label = new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = UITheme.NormalBoldFont };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(8, 7, 0, 7);
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void AddSummary(TableLayoutPanel panel, string caption, string value, int column, int row, Label? target = null)
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(4) };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = caption, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor, TextAlign = ContentAlignment.MiddleLeft };
        var valueLabel = target ?? new Label();
        valueLabel.Text = value;
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = UITheme.HeaderFont;
        valueLabel.ForeColor = UITheme.TextDark;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.AutoEllipsis = true;
        valueLabel.AccessibleName = $"Resumen {caption}";
        _toolTip.SetToolTip(valueLabel, value);
        host.Controls.Add(label, 0, 0);
        host.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(host, column, row);
    }
}
