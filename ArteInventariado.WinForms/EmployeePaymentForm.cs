using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

/// <summary>
/// Small coordinator form for registering an administrative employee payment.
/// Validation and authorization remain enforced by EmployeePaymentService.
/// </summary>
public sealed class EmployeePaymentForm : Form
{
    private readonly EmployeeActivitySummary _employee;
    private readonly IEmployeePaymentService _service;
    private readonly ComboBox _type = new();
    private readonly ComboBox _method = new();
    private readonly ComboBox _fundingOrigin = new();
    private readonly NumericUpDown _amount = new()
    {
        DecimalPlaces = 2,
        Minimum = 0,
        Maximum = 100000000,
        ThousandsSeparator = true
    };
    private readonly DateTimePicker _periodStart = CreateDatePicker();
    private readonly DateTimePicker _periodEnd = CreateDatePicker();
    private readonly NumericUpDown _days = new()
    {
        Minimum = 0,
        Maximum = 366,
        ThousandsSeparator = true,
        Enabled = false
    };
    private readonly CheckBox _daysEnabled = new() { Text = "Especificar días", AutoSize = true };
    private readonly TextBox _responsible = new() { MaxLength = 160 };
    private readonly TextBox _reference = new() { MaxLength = 180 };
    private readonly TextBox _note = new();
    private readonly Button _save = new();
    private readonly Guid _operationId = Guid.NewGuid();

    public EmployeePaymentRecord? Result { get; private set; }

    public EmployeePaymentForm(EmployeeActivitySummary employee, IEmployeePaymentService service)
    {
        _employee = employee ?? throw new ArgumentNullException(nameof(employee));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Registrar pago de empleado";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 680);
        MinimumSize = new Size(720, 600);
        UITheme.StyleForm(this);

        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _type.Items.AddRange(["Diario", "Semanal", "Quincenal", "Mensual", "Extraordinario"]);
        _type.SelectedIndex = 0;
        _method.DropDownStyle = ComboBoxStyle.DropDownList;
        _method.Items.AddRange([MetodoPago.EFECTIVO, MetodoPago.TRANSFERENCIA, MetodoPago.BITCOIN]);
        _method.SelectedIndex = -1;
        _fundingOrigin.DropDownStyle = ComboBoxStyle.DropDownList;
        _fundingOrigin.Items.AddRange([
            OrigenFondos.CAJA_FISICA,
            OrigenFondos.FONDOS_EXTERNOS_DUENO,
            OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA]);
        _fundingOrigin.SelectedIndex = -1;
        _method.SelectedIndexChanged += (_, _) => UpdateFundingFields();
        _fundingOrigin.SelectedIndexChanged += (_, _) => UpdateFundingFields();
        UpdateFundingFields();
        _amount.Dock = DockStyle.Fill;
        _amount.Name = "EmployeePaymentAmount";
        _periodStart.ShowCheckBox = true;
        _periodEnd.ShowCheckBox = true;
        _periodStart.Checked = false;
        _periodEnd.Checked = false;
        _periodStart.Value = DateTime.Today;
        _periodEnd.Value = DateTime.Today;
        _periodStart.Dock = DockStyle.Fill;
        _periodEnd.Dock = DockStyle.Fill;
        _days.Dock = DockStyle.Fill;
        _daysEnabled.CheckedChanged += (_, _) => _days.Enabled = _daysEnabled.Checked;
        _note.Multiline = true;
        _note.ScrollBars = ScrollBars.Vertical;
        UITheme.StyleInput(_type);
        UITheme.StyleInput(_method);
        UITheme.StyleInput(_fundingOrigin);
        UITheme.StyleInput(_amount);
        UITheme.StyleInput(_periodStart);
        UITheme.StyleInput(_periodEnd);
        UITheme.StyleInput(_days);
        UITheme.StyleTextBox(_responsible);
        UITheme.StyleTextBox(_reference);
        UITheme.StyleTextBox(_note);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(
            VisualLayout.CreatePageHeader(
                "Registrar pago",
                $"Empleado: {_employee.FullName} ({_employee.UserName})\nCaja física descuenta efectivo; origen externo no altera la caja."),
            0,
            0);
        root.Controls.Add(BuildFields(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildFields()
    {
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceMd)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 9; row++)
        {
            fields.RowStyles.Add(new RowStyle(row == 8 ? SizeType.Percent : SizeType.Absolute, row == 8 ? 100 : 44));
        }

        AddField(fields, "Tipo de pago", _type, 0);
        AddField(fields, "Monto ($)", _amount, 1);
        AddField(fields, "Método de pago", _method, 2);
        AddField(fields, "Origen de fondos", _fundingOrigin, 3);
        AddField(fields, "Responsable / fuente", _responsible, 4);
        AddField(fields, "Referencia", _reference, 5);
        var periodPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        periodPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        periodPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _periodStart.Margin = new Padding(0, UITheme.SpaceXs, UITheme.SpaceSm, UITheme.SpaceXs);
        _periodEnd.Margin = new Padding(UITheme.SpaceSm, UITheme.SpaceXs, 0, UITheme.SpaceXs);
        periodPanel.Controls.Add(_periodStart, 0, 0);
        periodPanel.Controls.Add(_periodEnd, 1, 0);
        AddField(fields, "Periodo (inicio / fin)", periodPanel, 6);
        var daysPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        daysPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        daysPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        daysPanel.Controls.Add(_days, 0, 0);
        daysPanel.Controls.Add(_daysEnabled, 1, 0);
        AddField(fields, "Días considerados", daysPanel, 7);
        AddField(fields, "Nota", _note, 8);
        return fields;
    }

    private void UpdateFundingFields()
    {
        _reference.Enabled = _method.SelectedItem is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN;
        if (!_reference.Enabled)
        {
            _reference.Clear();
        }

        _responsible.Enabled = _fundingOrigin.SelectedItem is
            OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA;
        if (!_responsible.Enabled)
        {
            _responsible.Clear();
        }
    }

    private Control BuildActions()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, UITheme.SpaceXs, 0, 0)
        };
        var close = new Button { Text = "Cancelar", Size = new Size(120, 40) };
        UITheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        _save.Text = "Confirmar pago";
        _save.Size = new Size(155, 40);
        UITheme.StyleSuccessButton(_save);
        _save.Click += SaveAsync;
        actions.Controls.Add(close);
        actions.Controls.Add(_save);
        return actions;
    }

    private async void SaveAsync(object? sender, EventArgs e)
    {
        var amountCentavos = ToCentavos(_amount.Value);
        if (amountCentavos <= 0)
        {
            MessageBox.Show("El monto debe ser mayor que cero.", "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _amount.Focus();
            return;
        }

        var start = _periodStart.Checked ? (DateOnly?)DateOnly.FromDateTime(_periodStart.Value.Date) : null;
        var end = _periodEnd.Checked ? (DateOnly?)DateOnly.FromDateTime(_periodEnd.Value.Date) : null;
        if (start.HasValue && end.HasValue && end.Value < start.Value)
        {
            MessageBox.Show("El fin del periodo no puede ser anterior al inicio.", "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_method.SelectedItem is not MetodoPago method)
        {
            MessageBox.Show("Seleccione el método con el que se pagó al empleado.", "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _method.Focus();
            return;
        }

        if (_fundingOrigin.SelectedItem is not OrigenFondos origin)
        {
            MessageBox.Show("Seleccione de dónde salió el dinero para este pago.", "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _fundingOrigin.Focus();
            return;
        }

        if (origin == OrigenFondos.CAJA_FISICA && method != MetodoPago.EFECTIVO)
        {
            MessageBox.Show("Caja física solo puede combinarse con efectivo. Para transferencia o Bitcoin, elija fondos fuera de caja.", "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _fundingOrigin.Focus();
            return;
        }

        if ((origin is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA) &&
            string.IsNullOrWhiteSpace(_responsible.Text))
        {
            MessageBox.Show("Indique quién aportó el dinero o de qué fuente externa salió.", "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _responsible.Focus();
            return;
        }

        if ((method is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN) && string.IsNullOrWhiteSpace(_reference.Text))
        {
            MessageBox.Show("Indique la referencia del pago electrónico.", "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reference.Focus();
            return;
        }

        var physicalCash = origin == OrigenFondos.CAJA_FISICA;
        var fundingConfirmation = $"Método: {method}; origen: {origin}; efecto: {(physicalCash ? "descuenta de caja" : "no modifica la caja")}.";

        if (MessageBox.Show(
                $"¿Confirmas que ya se realizó el pago de {FormatMoney(amountCentavos)} para {_employee.FullName}?\n\n{fundingConfirmation}",
                "Confirmar pago",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _save.Enabled = false;
        try
        {
            Result = await _service.RegisterPaymentAsync(new RegisterEmployeePaymentRequest(
                _employee.UserId,
                amountCentavos,
                ToPaymentType(_type.SelectedIndex),
                start,
                end,
                _daysEnabled.Checked ? (int?)_days.Value : null,
                string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim(),
                method,
                origin,
                string.IsNullOrWhiteSpace(_responsible.Text) ? null : _responsible.Text.Trim(),
                string.IsNullOrWhiteSpace(_reference.Text) ? null : _reference.Text.Trim(),
                _operationId));
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Pago de empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _save.Enabled = true;
        }
    }

    private static EmployeePaymentType ToPaymentType(int index) => index switch
    {
        0 => EmployeePaymentType.Daily,
        1 => EmployeePaymentType.Weekly,
        2 => EmployeePaymentType.Biweekly,
        3 => EmployeePaymentType.Monthly,
        4 => EmployeePaymentType.Extraordinary,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static DateTimePicker CreateDatePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd/MM/yyyy",
        MinDate = new DateTime(1900, 1, 1)
    };

    private static long ToCentavos(decimal value)
        => checked((long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero));

    private static string FormatMoney(long amountCentavos) => $"${amountCentavos / 100m:N2}";

    private static void AddField(TableLayoutPanel panel, string label, Control control, int row)
    {
        panel.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(UITheme.SpaceSm, UITheme.SpaceXs, 0, UITheme.SpaceXs);
        panel.Controls.Add(control, 1, row);
    }
}

public sealed class EmployeePaymentCancellationForm : Form
{
    private readonly TextBox _reason = new();
    private readonly Button _confirm = new();

    public string Reason => _reason.Text.Trim();

    public EmployeePaymentCancellationForm()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Anular pago de empleado";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 280);
        MinimumSize = new Size(460, 240);
        UITheme.StyleForm(this);
        _reason.Multiline = true;
        _reason.ScrollBars = ScrollBars.Vertical;
        _reason.MaxLength = 1000;
        _reason.Dock = DockStyle.Fill;
        _reason.TextChanged += (_, _) => _confirm.Enabled = !string.IsNullOrWhiteSpace(_reason.Text);
        UITheme.StyleTextBox(_reason);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        var title = new Label
        {
            Text = "Indique el motivo de anulación (obligatorio). El registro permanece en el historial y la anulación no devuelve automáticamente efectivo a caja.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, UITheme.SpaceSm)
        };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_reason, 0, 1);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, UITheme.SpaceSm, 0, 0)
        };
        var close = new Button { Text = "Volver", Size = new Size(110, 38) };
        UITheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        _confirm.Text = "Anular pago";
        _confirm.Size = new Size(130, 38);
        _confirm.Enabled = false;
        UITheme.StylePrimaryButton(_confirm);
        _confirm.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        actions.Controls.Add(close);
        actions.Controls.Add(_confirm);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }
}
