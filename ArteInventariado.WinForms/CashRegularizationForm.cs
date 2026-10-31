using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class CashRegularizationForm : Form
{
    private readonly NumericUpDown _counted = new();
    private readonly TextBox _reason = new();
    private readonly TextBox _password = new();

    public CashRegularizationForm(CashSummary cash)
    {
        CashId = cash.CajaId ?? throw new ArgumentException("La caja debe existir.", nameof(cash));
        ExpectedCashCentavos = cash.TotalEsperadoCentavos;
        BuildUi(cash);
    }

    public int CashId { get; }
    public long ExpectedCashCentavos { get; }
    public long CountedCashCentavos => ToCentavos(_counted.Value);
    public string Reason => _reason.Text.Trim();
    public string Password => _password.Text;

    private void BuildUi(CashSummary cash)
    {
        Text = "Regularizar caja abierta";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 470);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 2,
            RowCount = 7,
            BackColor = UITheme.DashboardBackgroundColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var title = new Label
        {
            Text = $"Caja #{cash.CajaId} · {cash.FechaOperacion:dd/MM/yyyy}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);

        AddLabel(root, "Efectivo esperado", 1);
        root.Controls.Add(ReadOnlyValue($"${Format(cash.TotalEsperadoCentavos)}"), 1, 1);
        AddLabel(root, "Efectivo contado real", 2);
        ConfigureMoney(_counted);
        root.Controls.Add(_counted, 1, 2);
        AddLabel(root, "Motivo obligatorio", 3);
        _reason.Multiline = true;
        _reason.MaxLength = 500;
        _reason.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_reason);
        root.Controls.Add(_reason, 1, 3);
        AddLabel(root, "Contraseña actual", 4);
        _password.UseSystemPasswordChar = true;
        _password.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_password);
        root.Controls.Add(_password, 1, 4);

        var notice = new Label
        {
            Dock = DockStyle.Fill,
            Text = "La diferencia se calculará con las reglas normales. Esta acción quedará identificada como RECUPERACIÓN y no puede deshacerse desde esta pantalla.",
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalFont,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(notice, 0, 5);
        root.SetColumnSpan(notice, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var confirm = new Button { Text = "Regularizar y cerrar" };
        UITheme.StyleDangerButton(confirm);
        UITheme.EnsureButtonContentFits(confirm, 190, 44, 44);
        confirm.Size = confirm.MinimumSize;
        confirm.Click += Confirm_Click;
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.EnsureButtonContentFits(cancel, 120, 44);
        cancel.Size = cancel.MinimumSize;
        actions.Controls.AddRange([confirm, cancel]);
        root.Controls.Add(actions, 0, 6);
        root.SetColumnSpan(actions, 2);
        CancelButton = cancel;
        Controls.Add(root);
    }

    private void Confirm_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_reason.Text))
        {
            MessageBox.Show("Indique el motivo de la regularización.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reason.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_password.Text))
        {
            MessageBox.Show("Ingrese su contraseña actual.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _password.Focus();
            return;
        }

        var difference = CountedCashCentavos - ExpectedCashCentavos;
        var confirmed = MessageBox.Show(
            $"Efectivo esperado: ${Format(ExpectedCashCentavos)}\r\n" +
            $"Efectivo contado: ${Format(CountedCashCentavos)}\r\n" +
            $"Diferencia: ${Format(difference)}\r\n\r\n" +
            "¿Confirma la regularización de esta caja?",
            "Confirmar recuperación de caja",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmed != DialogResult.Yes)
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddLabel(TableLayoutPanel root, string text, int row)
    {
        var label = new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelNormal(label);
        label.Font = UITheme.NormalBoldFont;
        root.Controls.Add(label, 0, row);
    }

    private static Label ReadOnlyValue(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = UITheme.NormalBoldFont,
        ForeColor = UITheme.TextDark
    };

    private static void ConfigureMoney(NumericUpDown control)
    {
        control.Dock = DockStyle.Fill;
        control.Minimum = 0;
        control.Maximum = 9_999_999.99m;
        control.DecimalPlaces = 2;
        control.ThousandsSeparator = true;
        control.Increment = 0.25m;
    }

    private static long ToCentavos(decimal dollars) =>
        decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));

    private static string Format(long cents) => (cents / 100m).ToString("N2");
}
