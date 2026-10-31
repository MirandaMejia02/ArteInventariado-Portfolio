using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public class CustomerCreditPolicyForm : Form
{
    private readonly ICreditService _creditService;
    private readonly int _clienteId;
    private readonly string _clienteNombre;
    private readonly CheckBox _chkEnabled = new();
    private readonly TextBox _txtBlockReason = new();
    private readonly CheckBox _chkCustomLimit = new();
    private readonly NumericUpDown _numLimit = new();
    private readonly CheckBox _chkCustomMaxAccounts = new();
    private readonly NumericUpDown _numMaxAccounts = new();
    private readonly CheckBox _chkCustomTerm = new();
    private readonly NumericUpDown _numTermDays = new();
    private readonly CheckBox _chkCustomOutstanding = new();
    private readonly CheckBox _chkAllowOutstanding = new();
    private readonly CheckBox _chkCustomInterest = new();
    private readonly NumericUpDown _numInterest = new();
    private readonly Label _lblAudit = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    public CustomerCreditPolicyForm(
        ICreditService creditService,
        int clienteId,
        string clienteNombre)
    {
        _creditService = creditService;
        _clienteId = clienteId;
        _clienteNombre = clienteNombre;

        BuildUi();
        Load += CustomerCreditPolicyForm_Load;
    }

    private async void CustomerCreditPolicyForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var policy = await _creditService.GetCustomerPolicyAsync(_clienteId);
            LoadPolicy(policy);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Politica crediticia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Politica crediticia";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 560);
        MinimumSize = new Size(1000, 520);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var title = new Label
        {
            Text = $"Politica crediticia - {_clienteNombre}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 14)
        };
        UITheme.StyleLabelTitle(title);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildFields(), 0, 1);

        _lblAudit.Dock = DockStyle.Fill;
        _lblAudit.TextAlign = ContentAlignment.MiddleLeft;
        _lblAudit.ForeColor = UITheme.MutedTextColor;
        _lblAudit.Font = UITheme.NormalBoldFont;
        root.Controls.Add(_lblAudit, 0, 2);

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = UITheme.MutedTextColor;
        _lblStatus.Font = UITheme.NormalBoldFont;
        root.Controls.Add(_lblStatus, 0, 3);

        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
        AcceptButton = _btnSave;
        CancelButton = _btnCancel;
    }

    private Control BuildFields()
    {
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16)
        };
        // Keep the percentage columns within the available width so the
        // right-side "Personalizar" controls are not clipped.
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));

        for (var i = 0; i < 6; i++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        }

        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _chkEnabled.Text = "Credito habilitado";
        _chkEnabled.Dock = DockStyle.Fill;
        _chkEnabled.Font = UITheme.NormalBoldFont;
        _chkEnabled.CheckedChanged += (_, _) => RefreshEnabledState();
        fields.Controls.Add(_chkEnabled, 0, 0);
        fields.SetColumnSpan(_chkEnabled, 3);

        AddTextField(fields, 1, "Motivo de bloqueo", _txtBlockReason);
        AddOptionalMoneyField(fields, 2, "Limite individual", _chkCustomLimit, _numLimit);
        AddOptionalIntegerField(fields, 3, "Maximo de cuentas", _chkCustomMaxAccounts, _numMaxAccounts, 1, 99);
        AddOptionalIntegerField(fields, 4, "Plazo maximo dias", _chkCustomTerm, _numTermDays, 1, 365);
        AddOptionalPercentField(fields, 5, "Interes individual %", _chkCustomInterest, _numInterest);

        _chkCustomOutstanding.Text = "Personalizar saldo vigente";
        _chkCustomOutstanding.Dock = DockStyle.Fill;
        _chkCustomOutstanding.Font = UITheme.NormalBoldFont;
        _chkCustomOutstanding.CheckedChanged += (_, _) => RefreshEnabledState();

        _chkAllowOutstanding.Text = "Permitir nuevo credito con saldo vigente";
        _chkAllowOutstanding.Dock = DockStyle.Fill;
        _chkAllowOutstanding.Font = UITheme.NormalBoldFont;

        fields.Controls.Add(_chkCustomOutstanding, 0, 6);
        fields.Controls.Add(_chkAllowOutstanding, 1, 6);
        fields.SetColumnSpan(_chkAllowOutstanding, 2);
        return fields;
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

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => DialogResult = DialogResult.Cancel);
        UITheme.StyleSecondaryButton(_btnCancel);
        ConfigureButton(_btnSave, "Guardar", 120, async (_, _) => await SaveAsync());
        UITheme.StylePrimaryButton(_btnSave);

        actions.Controls.AddRange([_btnCancel, _btnSave]);
        return actions;
    }

    private void LoadPolicy(CustomerCreditPolicy policy)
    {
        _chkEnabled.Checked = policy.CreditoHabilitado;
        _txtBlockReason.Text = policy.CreditoBloqueoMotivo ?? string.Empty;
        SetOptionalMoney(_chkCustomLimit, _numLimit, policy.LimiteCentavos);
        SetOptionalInteger(_chkCustomMaxAccounts, _numMaxAccounts, policy.MaximoCuentasActivas);
        SetOptionalInteger(_chkCustomTerm, _numTermDays, policy.PlazoMaximoDias);
        SetOptionalPercent(_chkCustomInterest, _numInterest, policy.InteresPorcentaje);
        _chkCustomOutstanding.Checked = policy.PermitirSaldoVigente.HasValue;
        _chkAllowOutstanding.Checked = policy.PermitirSaldoVigente ?? true;

        _lblAudit.Text = policy.FechaActualizacion.HasValue
            ? $"Ultima modificacion: {SaleReceiptViewModel.ToLocal(policy.FechaActualizacion.Value):yyyy-MM-dd HH:mm} por {policy.UsuarioActualizacion ?? "-"}"
            : "Usa la politica general mientras no se definan valores individuales.";
        RefreshEnabledState();
    }

    private async Task SaveAsync()
    {
        try
        {
            _btnSave.Enabled = false;
            _lblStatus.Text = "Guardando politica crediticia...";

            await _creditService.UpdateCustomerPolicyAsync(new UpdateCustomerCreditPolicyRequest(
                _clienteId,
                _chkEnabled.Checked,
                NullIfWhiteSpace(_txtBlockReason.Text),
                _chkCustomLimit.Checked ? DollarsToCentavos(_numLimit.Value) : null,
                _chkCustomMaxAccounts.Checked ? (int)_numMaxAccounts.Value : null,
                _chkCustomTerm.Checked ? (int)_numTermDays.Value : null,
                _chkCustomOutstanding.Checked ? _chkAllowOutstanding.Checked : null,
                _chkCustomInterest.Checked ? _numInterest.Value : null));

            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
            MessageBox.Show(ex.Message, "Politica crediticia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }

    private void RefreshEnabledState()
    {
        _txtBlockReason.Enabled = !_chkEnabled.Checked;
        _numLimit.Enabled = _chkCustomLimit.Checked;
        _numMaxAccounts.Enabled = _chkCustomMaxAccounts.Checked;
        _numTermDays.Enabled = _chkCustomTerm.Checked;
        _numInterest.Enabled = _chkCustomInterest.Checked;
        _chkAllowOutstanding.Enabled = _chkCustomOutstanding.Checked;
    }

    private void AddTextField(TableLayoutPanel layout, int row, string labelText, TextBox textBox)
    {
        var label = CreateLabel(labelText);
        UITheme.StyleTextBox(textBox);
        textBox.Dock = DockStyle.Fill;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(textBox, 1, row);
        layout.SetColumnSpan(textBox, 2);
    }

    private void AddOptionalMoneyField(TableLayoutPanel layout, int row, string labelText, CheckBox checkBox, NumericUpDown number)
    {
        ConfigureMoney(number);
        AddOptionalNumberField(layout, row, labelText, checkBox, number);
    }

    private void AddOptionalIntegerField(TableLayoutPanel layout, int row, string labelText, CheckBox checkBox, NumericUpDown number, int min, int max)
    {
        number.Minimum = min;
        number.Maximum = max;
        number.DecimalPlaces = 0;
        number.ThousandsSeparator = true;
        AddOptionalNumberField(layout, row, labelText, checkBox, number);
    }

    private void AddOptionalPercentField(TableLayoutPanel layout, int row, string labelText, CheckBox checkBox, NumericUpDown number)
    {
        number.Minimum = 0;
        number.Maximum = 100;
        number.DecimalPlaces = 2;
        number.ThousandsSeparator = true;
        AddOptionalNumberField(layout, row, labelText, checkBox, number);
    }

    private void AddOptionalNumberField(TableLayoutPanel layout, int row, string labelText, CheckBox checkBox, NumericUpDown number)
    {
        checkBox.Text = "Personalizar";
        checkBox.Dock = DockStyle.Fill;
        checkBox.Font = UITheme.NormalBoldFont;
        checkBox.CheckedChanged += (_, _) => RefreshEnabledState();

        number.Dock = DockStyle.Fill;
        number.Font = UITheme.NormalFont;

        layout.Controls.Add(CreateLabel(labelText), 0, row);
        layout.Controls.Add(number, 1, row);
        layout.Controls.Add(checkBox, 2, row);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 12, 8)
        };
    }

    private static void ConfigureMoney(NumericUpDown number)
    {
        number.Minimum = 0;
        number.Maximum = 999999;
        number.DecimalPlaces = 2;
        number.ThousandsSeparator = true;
    }

    private static void SetOptionalMoney(CheckBox checkBox, NumericUpDown number, long? centavos)
    {
        checkBox.Checked = centavos.HasValue;
        number.Value = Math.Clamp((centavos ?? 0) / 100m, number.Minimum, number.Maximum);
    }

    private static void SetOptionalInteger(CheckBox checkBox, NumericUpDown number, int? value)
    {
        checkBox.Checked = value.HasValue;
        number.Value = Math.Clamp(value ?? (int)number.Minimum, (int)number.Minimum, (int)number.Maximum);
    }

    private static void SetOptionalPercent(CheckBox checkBox, NumericUpDown number, decimal? value)
    {
        checkBox.Checked = value.HasValue;
        number.Value = Math.Clamp(value ?? 0, number.Minimum, number.Maximum);
    }

    private static long DollarsToCentavos(decimal dollars)
    {
        return (long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }
}
