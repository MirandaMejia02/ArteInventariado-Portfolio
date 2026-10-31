using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class CashDayRegularizationForm : Form
{
    private readonly NumericUpDown _counted = new();
    private readonly TextBox _reason = new();
    private readonly TextBox _password = new();

    public CashDayRegularizationForm(CashDailySummary daily)
    {
        Daily = daily;
        BuildUi();
    }

    public CashDailySummary Daily { get; }
    public long CountedCashCentavos => ToCentavos(_counted.Value);
    public string Reason => _reason.Text.Trim();
    public string Password => _password.Text;

    private void BuildUi()
    {
        Text = "Regularizar cierre del día";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(650, 500);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 2,
            RowCount = 8,
            BackColor = UITheme.DashboardBackgroundColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var title = new Label
        {
            Text = $"Cierre consolidado · {Daily.FechaOperacion:dd/MM/yyyy} · {Daily.CantidadSesiones} sesión(es)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);

        AddLabel(root, "Efectivo esperado", 1);
        root.Controls.Add(ReadOnlyValue(Format(Daily.EfectivoEsperadoAcumuladoCentavos ?? 0)), 1, 1);
        AddLabel(root, "Último cierre guardado", 2);
        root.Controls.Add(ReadOnlyValue(Daily.UltimoCierreContadoCentavos.HasValue
            ? Format(Daily.UltimoCierreContadoCentavos.Value)
            : "Pendiente"), 1, 2);
        AddLabel(root, "Conteo físico real", 3);
        ConfigureMoney(_counted);
        _counted.Value = Math.Min(_counted.Maximum, Math.Max(_counted.Minimum,
            Daily.UltimoCierreContadoCentavos.GetValueOrDefault() / 100m));
        root.Controls.Add(_counted, 1, 3);
        AddLabel(root, "Motivo obligatorio", 4);
        _reason.Multiline = true;
        _reason.MaxLength = 500;
        _reason.ScrollBars = ScrollBars.Vertical;
        _reason.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_reason);
        root.Controls.Add(_reason, 1, 4);
        AddLabel(root, "Contraseña actual", 5);
        _password.UseSystemPasswordChar = true;
        _password.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_password);
        root.Controls.Add(_password, 1, 5);

        var notice = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Actualiza únicamente el último conteo del día. Conserva sesiones, ventas y movimientos; el valor anterior y el nuevo quedan auditados. No anula una venta ni cambia inventario.",
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalFont,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(notice, 0, 6);
        root.SetColumnSpan(notice, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var confirm = new Button { Text = "Regularizar cierre" };
        UITheme.StyleDangerButton(confirm);
        UITheme.EnsureButtonContentFits(confirm, 190, 44, 44);
        confirm.Size = confirm.MinimumSize;
        confirm.Click += Confirm_Click;
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.EnsureButtonContentFits(cancel, 120, 44);
        cancel.Size = cancel.MinimumSize;
        actions.Controls.AddRange([confirm, cancel]);
        root.Controls.Add(actions, 0, 7);
        root.SetColumnSpan(actions, 2);
        AcceptButton = confirm;
        CancelButton = cancel;
        Controls.Add(root);
    }

    private void Confirm_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            MessageBox.Show(this, "Indique el motivo de la regularización.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reason.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            MessageBox.Show(this, "Ingrese su contraseña actual.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _password.Focus();
            return;
        }

        var expected = Daily.EfectivoEsperadoAcumuladoCentavos ?? 0;
        var difference = CountedCashCentavos - expected;
        var confirmed = MessageBox.Show(
            this,
            $"Día: {Daily.FechaOperacion:dd/MM/yyyy} ({Daily.CantidadSesiones} sesiones)\r\n" +
            $"Esperado por movimientos: ${Format(expected)}\r\n" +
            $"Último cierre actual: ${Format(Daily.UltimoCierreContadoCentavos.GetValueOrDefault())}\r\n" +
            $"Conteo físico nuevo: ${Format(CountedCashCentavos)}\r\n" +
            $"Diferencia frente a movimientos: ${Format(difference)}\r\n\r\n" +
            "¿Confirma regularizar el cierre consolidado?",
            "Confirmar regularización de caja",
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
        Text = $"${text}",
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
