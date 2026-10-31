using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class CashIncidentNoteForm : Form
{
    private readonly TextBox _reason = new();

    public CashIncidentNoteForm(long expectedCashCentavos, long countedCashCentavos)
    {
        Reason = string.Empty;
        BuildUi(expectedCashCentavos, countedCashCentavos);
    }

    public string Reason { get; private set; }

    private void BuildUi(long expectedCashCentavos, long countedCashCentavos)
    {
        Text = "Nota administrativa de caja";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(580, 330);
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var title = new Label
        {
            Text = "Describa brevemente el incidente imprevisto",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);

        var summary = new Label
        {
            Text = $"Esperado: ${Format(expectedCashCentavos)}    Contado: ${Format(countedCashCentavos)}\r\n" +
                "Solo se añadirá a la bitácora; no cambia importes ni reportes.",
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
        var save = new Button { Text = "Guardar nota y cerrar caja" };
        UITheme.StylePrimaryButton(save);
        UITheme.EnsureButtonContentFits(save, 210, 42, 42);
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
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        var reason = _reason.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(this, "Escriba el motivo del incidente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reason.Focus();
            return;
        }

        Reason = reason;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string Format(long centavos) =>
        (centavos / 100m).ToString("N2", System.Globalization.CultureInfo.CurrentCulture);
}
