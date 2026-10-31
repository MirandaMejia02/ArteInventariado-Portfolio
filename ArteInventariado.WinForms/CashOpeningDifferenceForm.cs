using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class CashOpeningDifferenceForm : Form
{
    private readonly TextBox _reason = new();

    public CashOpeningDifferenceForm(CashOpeningReference previousClose, long openingAmountCentavos)
    {
        BuildUi(previousClose, openingAmountCentavos);
    }

    public string Reason { get; private set; } = string.Empty;

    private void BuildUi(CashOpeningReference previousClose, long openingAmountCentavos)
    {
        Text = "Justificar conteo de apertura";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 350);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.DashboardBackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var title = new Label
        {
            Text = "El conteo difiere de la referencia",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);

        var summary = new Label
        {
            Text = $"{DescribeReference(previousClose)}: ${Format(previousClose.MontoCierreCentavos)}\r\n" +
                   $"Efectivo contado para abrir: ${Format(openingAmountCentavos)}. La referencia anterior se conserva sin cambios.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalFont
        };
        root.Controls.Add(summary, 0, 1);

        _reason.Multiline = true;
        _reason.MaxLength = 500;
        _reason.Dock = DockStyle.Fill;
        _reason.ScrollBars = ScrollBars.Vertical;
        UITheme.StyleTextBox(_reason);
        root.Controls.Add(_reason, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var save = new Button { Text = "Continuar con este conteo" };
        UITheme.StylePrimaryButton(save);
        UITheme.EnsureButtonContentFits(save, 230, 42, 42);
        save.Size = save.MinimumSize;
        save.Click += Save_Click;
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.EnsureButtonContentFits(cancel, 120, 42);
        cancel.Size = cancel.MinimumSize;
        actions.Controls.AddRange([save, cancel]);
        root.Controls.Add(actions, 0, 3);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(root);
        Shown += (_, _) => _reason.Focus();
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        var reason = _reason.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(this, "Escriba por qué el conteo de apertura difiere de la referencia anterior.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reason.Focus();
            return;
        }

        Reason = reason;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string Format(long centavos) =>
        (centavos / 100m).ToString("N2", System.Globalization.CultureInfo.CurrentCulture);

    private static string DescribeReference(CashOpeningReference reference) =>
        $"Último conteo físico ({reference.FechaOperacion:dd/MM/yyyy}, caja #{reference.CajaId})";
}
