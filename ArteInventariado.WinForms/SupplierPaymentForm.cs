using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SupplierPaymentForm : Form
{
    private readonly AccountPayableDetail _account;
    private readonly IAccountsPayableService _service;
    private readonly ComboBox _cmbMethod = new();
    private readonly ComboBox _cmbOrigin = new();
    private readonly TextBox _txtResponsible = new();
    private readonly NumericUpDown _numAmount = new() { DecimalPlaces = 2, Minimum = 0, Maximum = 100000000, ThousandsSeparator = true };
    private readonly TextBox _txtReference = new();
    private readonly TextBox _txtObservations = new();
    private readonly Label _lblNewBalance = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnAddLine = new();
    private readonly Label _lblAdditionalLines = new();
    private readonly ToolTip _toolTip = new();
    private readonly List<SupplierPaymentLineRequest> _additionalLines = [];
    private Guid _operationId = Guid.NewGuid();

    public SupplierPaymentResult? Result { get; private set; }

    public SupplierPaymentForm(AccountPayableDetail account, IAccountsPayableService service)
    {
        _account = account;
        _service = service;
        BuildUi();
        UpdateBalance();
    }

    private void BuildUi()
    {
        Text = $"Abono {_account.Summary.CodigoCuenta}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 720);
        MinimumSize = new Size(780, 680);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        var title = new Label { Text = "Registrar abono a proveedor", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildFields(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildSummary()
    {
        var s = _account.Summary;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        AddSummary(panel, "Proveedor", s.Proveedor, 0, 0);
        AddSummary(panel, "Compra / cuenta", $"{s.CodigoCompra} / {s.CodigoCuenta}", 1, 0);
        AddSummary(panel, "Saldo anterior", SaleReceiptViewModel.FormatDollars(s.SaldoPendienteCentavos), 0, 1);
        AddSummary(panel, "Saldo nuevo", SaleReceiptViewModel.FormatDollars(s.SaldoPendienteCentavos), 1, 1, _lblNewBalance);
        return panel;
    }

    private Control BuildFields()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) panel.RowStyles.Add(new RowStyle(i == 5 ? SizeType.Percent : SizeType.Absolute, i == 5 ? 100 : 48));

        _cmbMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMethod.Name = "PaymentMethod";
        _cmbMethod.Items.AddRange([MetodoPago.EFECTIVO, MetodoPago.TRANSFERENCIA, MetodoPago.BITCOIN]);
        _cmbMethod.Font = UITheme.NormalFont;
        _cmbMethod.SelectedIndexChanged += (_, _) =>
        {
            ClearIncompatiblePhysicalCashOrigin(_cmbMethod, _cmbOrigin);
            UpdateReferenceState();
            UpdateOriginState();
        };
        _cmbMethod.SelectedIndex = 0;
        _cmbOrigin.Name = "FundingOrigin";
        _cmbOrigin.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbOrigin.Items.AddRange([OrigenFondos.CAJA_FISICA, OrigenFondos.FONDOS_EXTERNOS_DUENO, OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA]);
        _cmbOrigin.SelectedIndex = 0;
        _cmbOrigin.SelectedIndexChanged += (_, _) => UpdateOriginState();
        _numAmount.Name = "PaymentAmount";
        _txtReference.Name = "PaymentReference";
        _txtResponsible.Name = "FundingResponsible";
        _numAmount.ValueChanged += (_, _) => UpdateBalance();
        _txtObservations.Multiline = true;
        _txtObservations.ScrollBars = ScrollBars.Vertical;
        UITheme.StyleTextBox(_txtReference);
        UITheme.StyleTextBox(_txtResponsible);
        UITheme.StyleTextBox(_txtObservations);
        AddField(panel, "Metodo", _cmbMethod, 0);
        AddField(panel, "Origen", _cmbOrigin, 1);
        AddField(panel, "Responsable/fuente", _txtResponsible, 2);
        AddField(panel, "Monto ($)", _numAmount, 3);
        AddField(panel, "Referencia", _txtReference, 4);
        AddField(panel, "Observaciones", _txtObservations, 5);
        UpdateReferenceState();
        UpdateOriginState();
        return panel;
    }

    private Control BuildActions()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var cancel = new Button { Text = "Cancelar", Size = new Size(120, 42) };
        UITheme.StyleSecondaryButton(cancel);
        cancel.Click += (_, _) => Close();
        _btnSave.Text = "Registrar abono";
        _btnSave.Size = new Size(175, 42);
        UITheme.StyleSuccessButton(_btnSave);
        _btnSave.Click += SaveAsync;
        var full = new Button { Text = "Pagar total", Size = new Size(145, 42) };
        UITheme.StylePrimaryButton(full);
        _toolTip.SetToolTip(full, "Llena el monto con el saldo pendiente. No registra el abono hasta presionar Registrar abono.");
        full.Click += (_, _) => _numAmount.Value = Math.Min(_numAmount.Maximum, _account.Summary.SaldoPendienteCentavos / 100m);
        _btnAddLine.Text = "Agregar pago";
        _btnAddLine.Size = new Size(150, 42);
        UITheme.StyleSecondaryButton(_btnAddLine);
        _toolTip.SetToolTip(_btnAddLine, "Añade otro método y monto al mismo abono.");
        _btnAddLine.Click += (_, _) => AddAdditionalLine();
        _lblAdditionalLines.AutoSize = true;
        _lblAdditionalLines.TextAlign = ContentAlignment.MiddleLeft;
        _lblAdditionalLines.Font = UITheme.NormalFont;
        _lblAdditionalLines.ForeColor = UITheme.MutedTextColor;
        _lblAdditionalLines.Margin = new Padding(8, 14, 0, 0);
        flow.Controls.AddRange([cancel, _btnSave, full, _btnAddLine, _lblAdditionalLines]);
        UpdateAdditionalLinesLabel();
        return flow;
    }

    private async void SaveAsync(object? sender, EventArgs e)
    {
        var method = (MetodoPago)_cmbMethod.SelectedItem!;
        if (_cmbOrigin.SelectedItem is not OrigenFondos origin)
        {
            MessageBox.Show("Seleccione explícitamente el origen de los fondos.", "Abono a proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _cmbOrigin.Focus();
            return;
        }
        if (!TryValidateLine(method, origin, _txtReference.Text, _txtResponsible.Text, true))
        {
            return;
        }

        _btnSave.Enabled = false;
        try
        {
            var lines = new List<SupplierPaymentLineRequest>(_additionalLines.Count + 1);
            if (_numAmount.Value > 0)
            {
                lines.Add(new SupplierPaymentLineRequest(
                    ToCentavos(_numAmount.Value), method,
                    method == MetodoPago.EFECTIVO ? null : _txtReference.Text.Trim(),
                    origin,
                    string.IsNullOrWhiteSpace(_txtResponsible.Text) ? null : _txtResponsible.Text.Trim(),
                    _operationId));
            }
            else
            {
                lines = new List<SupplierPaymentLineRequest>(_additionalLines.Count);
            }
            lines.AddRange(_additionalLines);
            var batch = await _service.RegisterPaymentsAsync(new RegisterSupplierPaymentBatchRequest(
                _operationId,
                _account.Summary.CuentaPorPagarId,
                lines,
                _txtObservations.Text));
            Result = batch.Pagos.FirstOrDefault();
            if (Result is null) throw new InvalidOperationException("El servicio no devolvio el abono registrado.");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Abono a proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _btnSave.Enabled = true;
        }
    }

    private void UpdateBalance()
    {
        var amount = checked(ToCentavos(_numAmount.Value) + _additionalLines.Sum(line => line.MontoCentavos));
        var balance = _account.Summary.SaldoPendienteCentavos - amount;
        _lblNewBalance.Text = SaleReceiptViewModel.FormatDollars(Math.Max(0, balance));
        _lblNewBalance.ForeColor = balance < 0 ? Color.Firebrick : UITheme.TextDark;
        _btnSave.Enabled = amount > 0 && balance >= 0;
    }

    private void UpdateReferenceState()
    {
        var requiresReference = _cmbMethod.SelectedItem is MetodoPago method && method is not MetodoPago.EFECTIVO;
        _txtReference.Enabled = requiresReference;
        if (!requiresReference)
        {
            _txtReference.Clear();
        }
    }

    private void UpdateOriginState()
    {
        _txtResponsible.Enabled = _cmbOrigin.SelectedItem is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA;
        if (!_txtResponsible.Enabled) _txtResponsible.Clear();
    }

    private static void ClearIncompatiblePhysicalCashOrigin(ComboBox method, ComboBox origin)
    {
        if (method.SelectedItem is MetodoPago selectedMethod && selectedMethod != MetodoPago.EFECTIVO &&
            origin.SelectedItem is OrigenFondos.CAJA_FISICA)
        {
            // Do not silently reclassify the payment as an external source;
            // require the user to choose the actual source.
            origin.SelectedIndex = -1;
        }
    }

    private void AddAdditionalLine()
    {
        using var dialog = new SupplierPaymentLineForm();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Request is null) return;
        _additionalLines.Add(dialog.Request);
        UpdateAdditionalLinesLabel();
        UpdateBalance();
    }

    private void UpdateAdditionalLinesLabel()
    {
        _lblAdditionalLines.Text = _additionalLines.Count == 0
            ? ""
            : $"Líneas adicionales: {_additionalLines.Count} ({SaleReceiptViewModel.FormatDollars(_additionalLines.Sum(line => line.MontoCentavos))})";
    }

    private bool TryValidateLine(MetodoPago method, OrigenFondos origin, string? reference, string? responsible, bool focusInvalid)
    {
        var firstLineHasAmount = _numAmount.Value > 0;
        if (!firstLineHasAmount && _additionalLines.Count == 0)
        {
            MessageBox.Show("Agregue al menos una línea con monto mayor que cero.", "Abono a proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (focusInvalid) _numAmount.Focus();
            return false;
        }

        if (firstLineHasAmount && method is not MetodoPago.EFECTIVO && string.IsNullOrWhiteSpace(reference))
        {
            MessageBox.Show(
                method == MetodoPago.TRANSFERENCIA
                    ? "Ingrese la referencia de la transferencia."
                    : "Ingrese la referencia de Bitcoin.",
                "Abono a proveedor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            if (focusInvalid) _txtReference.Focus();
            return false;
        }

        if (firstLineHasAmount && origin == OrigenFondos.CAJA_FISICA && method != MetodoPago.EFECTIVO)
        {
            MessageBox.Show("CAJA_FISICA solo puede usarse con efectivo. Seleccione una fuente fuera de caja para este método.", "Abono a proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (focusInvalid) _cmbOrigin.Focus();
            return false;
        }

        if (firstLineHasAmount && (origin is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA) &&
            string.IsNullOrWhiteSpace(responsible))
        {
            MessageBox.Show(
                "Indique el responsable o la fuente del dinero externo.",
                "Abono a proveedor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            if (focusInvalid) _txtResponsible.Focus();
            return false;
        }

        return true;
    }

    private static long ToCentavos(decimal value) => checked((long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero));

    private static void AddField(TableLayoutPanel panel, string text, Control control, int row)
    {
        panel.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = UITheme.NormalBoldFont }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(8, 7, 0, 7);
        panel.Controls.Add(control, 1, row);
    }

    private void AddSummary(TableLayoutPanel panel, string caption, string value, int column, int row, Label? target = null)
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(5) };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var captionLabel = new Label { Text = caption, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor, TextAlign = ContentAlignment.MiddleLeft };
        var valueLabel = target ?? new Label();
        valueLabel.Text = value;
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = UITheme.HeaderFont;
        valueLabel.AutoEllipsis = true;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.AccessibleName = $"Resumen {caption}";
        _toolTip.SetToolTip(valueLabel, value);
        host.Controls.Add(captionLabel, 0, 0);
        host.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(host, column, row);
    }

    private sealed class SupplierPaymentLineForm : Form
    {
        private readonly ComboBox _method = new();
        private readonly ComboBox _origin = new();
        private readonly TextBox _responsible = new();
        private readonly NumericUpDown _amount = new() { DecimalPlaces = 2, Minimum = 0, Maximum = 100000000, ThousandsSeparator = true };
        private readonly TextBox _reference = new();
        public SupplierPaymentLineRequest? Request { get; private set; }

        public SupplierPaymentLineForm()
        {
            Text = "Agregar línea de pago";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 330);
            MinimumSize = new Size(500, 300);
            UITheme.StyleForm(this);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 6; i++) panel.RowStyles.Add(new RowStyle(i == 5 ? SizeType.Percent : SizeType.Absolute, i == 5 ? 58 : 43));

            _method.DropDownStyle = ComboBoxStyle.DropDownList;
            _method.Items.AddRange([MetodoPago.EFECTIVO, MetodoPago.TRANSFERENCIA, MetodoPago.BITCOIN]);
            _method.SelectedIndex = 0;
            _method.SelectedIndexChanged += (_, _) =>
            {
                SupplierPaymentForm.ClearIncompatiblePhysicalCashOrigin(_method, _origin);
                UpdateStates();
            };
            _origin.DropDownStyle = ComboBoxStyle.DropDownList;
            _origin.Items.AddRange([OrigenFondos.CAJA_FISICA, OrigenFondos.FONDOS_EXTERNOS_DUENO, OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA]);
            _origin.SelectedIndex = 0;
            _origin.SelectedIndexChanged += (_, _) => UpdateStates();
            _amount.Name = "AdditionalPaymentAmount";
            _reference.Name = "AdditionalPaymentReference";
            _responsible.Name = "AdditionalFundingResponsible";
            UITheme.StyleTextBox(_reference);
            UITheme.StyleTextBox(_responsible);
            AddLineField(panel, "Método", _method, 0);
            AddLineField(panel, "Origen", _origin, 1);
            AddLineField(panel, "Responsable/fuente", _responsible, 2);
            AddLineField(panel, "Monto ($)", _amount, 3);
            AddLineField(panel, "Referencia", _reference, 4);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Cancelar", Size = new Size(110, 38), DialogResult = DialogResult.Cancel };
            UITheme.StyleSecondaryButton(cancel);
            var accept = new Button { Text = "Agregar", Size = new Size(110, 38) };
            UITheme.StyleSuccessButton(accept);
            accept.Click += (_, _) => Accept();
            actions.Controls.Add(cancel);
            actions.Controls.Add(accept);
            panel.Controls.Add(actions, 1, 5);
            Controls.Add(panel);
            AcceptButton = accept;
            CancelButton = cancel;
            UpdateStates();
        }

        private void Accept()
        {
            var method = (MetodoPago)_method.SelectedItem!;
            if (_amount.Value <= 0)
            {
                MessageBox.Show("El monto debe ser mayor que cero.", "Línea de pago", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_origin.SelectedItem is not OrigenFondos origin)
            {
                MessageBox.Show("Seleccione explícitamente el origen de los fondos.", "Línea de pago", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _origin.Focus();
                return;
            }
            if (origin == OrigenFondos.CAJA_FISICA && method != MetodoPago.EFECTIVO)
            {
                MessageBox.Show("CAJA_FISICA solo puede usarse con efectivo. Seleccione una fuente fuera de caja.", "Línea de pago", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _origin.Focus();
                return;
            }
            if (method is not MetodoPago.EFECTIVO && string.IsNullOrWhiteSpace(_reference.Text))
            {
                MessageBox.Show("Ingrese la referencia del método seleccionado.", "Línea de pago", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _reference.Focus();
                return;
            }
            if ((origin is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA) && string.IsNullOrWhiteSpace(_responsible.Text))
            {
                MessageBox.Show("Indique el responsable o la fuente del dinero externo.", "Línea de pago", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _responsible.Focus();
                return;
            }
            Request = new SupplierPaymentLineRequest(
                ToCentavos(_amount.Value),
                method,
                method == MetodoPago.EFECTIVO ? null : _reference.Text.Trim(),
                origin,
                string.IsNullOrWhiteSpace(_responsible.Text) ? null : _responsible.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateStates()
        {
            var method = _method.SelectedItem is MetodoPago value ? value : MetodoPago.EFECTIVO;
            _reference.Enabled = method is not MetodoPago.EFECTIVO;
            if (!_reference.Enabled) _reference.Clear();
            _responsible.Enabled = _origin.SelectedItem is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA;
            if (!_responsible.Enabled) _responsible.Clear();
        }

        private static void AddLineField(TableLayoutPanel panel, string caption, Control control, int row)
        {
            panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = UITheme.NormalBoldFont }, 0, row);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(8, 5, 0, 5);
            panel.Controls.Add(control, 1, row);
        }

        private static long ToCentavos(decimal value) => checked((long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
    }
}
