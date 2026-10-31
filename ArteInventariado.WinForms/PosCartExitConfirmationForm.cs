using ArteInventariado.Application.Services;

namespace ArteInventariado.WinForms;

internal sealed class PosCartExitConfirmationForm : Form
{
    public PosCartExitConfirmationForm(PosCartExitSummary summary, PosExitReason reason, int draftCount = 1)
    {
        BuildUi(summary, reason, draftCount);
    }

    public bool DiscardConfirmed { get; private set; }

    private void BuildUi(PosCartExitSummary summary, PosExitReason reason, int draftCount)
    {
        Text = "Venta en preparación";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 245);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UITheme.DashboardBackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var title = new Label
        {
            Text = draftCount > 1 ? "Hay ventas pendientes" : "El carrito todavía contiene productos",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelHeader(title);
        root.Controls.Add(title, 0, 0);
        var message = new Label
        {
            Text = PosCartExitPolicy.BuildMultiDraftMessage(draftCount, summary, reason),
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(message, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var discard = new Button { Text = "Salir y descartar" };
        UITheme.StyleDangerButton(discard);
        UITheme.EnsureButtonContentFits(discard, 170, 44, 42);
        discard.Size = discard.MinimumSize;
        discard.Click += (_, _) =>
        {
            DiscardConfirmed = true;
            DialogResult = DialogResult.OK;
            Close();
        };
        var keep = new Button { Text = "Continuar venta", DialogResult = DialogResult.Cancel };
        UITheme.StylePrimaryButton(keep);
        UITheme.EnsureButtonContentFits(keep, 170, 44, 42);
        keep.Size = keep.MinimumSize;
        actions.Controls.AddRange([discard, keep]);
        root.Controls.Add(actions, 0, 2);
        AcceptButton = keep;
        CancelButton = keep;
        Controls.Add(root);
    }
}
