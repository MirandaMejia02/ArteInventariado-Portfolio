namespace ArteInventariado.WinForms.Controls;

internal sealed class AnalyticsStatusStripControl : DashboardSurfacePanel
{
    private readonly Label _capture;
    private readonly Label _coverage;
    private readonly Label _status;
    private readonly Label _statusIcon;
    private readonly ToolTip _toolTip = new();

    public AnalyticsStatusStripControl()
    {
        Name = "ProductAnalyticsStatusStrip";
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 5, 0, 0);
        Padding = new Padding(10, 6, 10, 6);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var captureBlock = CreateBlock("\uE823", Color.FromArgb(205, 43, 116), Color.FromArgb(255, 239, 246), out _, out _capture);
        var coverageBlock = CreateBlock("\uE9D2", Color.FromArgb(114, 78, 164), Color.FromArgb(246, 241, 255), out _, out _coverage);
        var statusBlock = CreateBlock("\uE73E", Color.FromArgb(67, 145, 91), Color.FromArgb(239, 248, 240), out _statusIcon, out _status);
        captureBlock.Margin = new Padding(0, 0, 8, 0);
        coverageBlock.Margin = new Padding(0, 0, 8, 0);
        statusBlock.Margin = Padding.Empty;
        layout.Controls.Add(captureBlock, 0, 0);
        layout.Controls.Add(coverageBlock, 1, 0);
        layout.Controls.Add(statusBlock, 2, 0);
        Controls.Add(layout);

        SetLoading("Cargando analítica de productos…");
    }

    public void SetCapture(string text)
    {
        _capture.Text = text;
        _toolTip.SetToolTip(_capture, text);
    }

    public void SetCoverage(string text)
    {
        _coverage.Text = text;
        _toolTip.SetToolTip(_coverage, text);
    }

    public void SetSuccess(string text)
    {
        _status.Text = text;
        _status.ForeColor = UITheme.TextDark;
        _statusIcon.Text = "\uE73E";
        _statusIcon.ForeColor = Color.FromArgb(67, 145, 91);
        _statusIcon.BackColor = Color.FromArgb(239, 248, 240);
        _toolTip.SetToolTip(_status, text);
    }

    public void SetLoading(string text)
    {
        _status.Text = text;
        _status.ForeColor = UITheme.MutedTextColor;
        _statusIcon.Text = "\uE895";
        _statusIcon.ForeColor = UITheme.PrimaryColor;
        _statusIcon.BackColor = UITheme.DashboardAccentSoftColor;
        _toolTip.SetToolTip(_status, text);
    }

    public void SetError(string text)
    {
        _status.Text = text;
        _status.ForeColor = UITheme.DangerColor;
        _statusIcon.Text = "\uEA39";
        _statusIcon.ForeColor = UITheme.DangerColor;
        _statusIcon.BackColor = Color.FromArgb(255, 239, 242);
        _toolTip.SetToolTip(_status, text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Control CreateBlock(
        string glyph,
        Color iconColor,
        Color iconBackground,
        out Label icon,
        out Label text)
    {
        var block = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = Padding.Empty
        };
        block.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        icon = new Label
        {
            Text = glyph,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe MDL2 Assets", 16f),
            ForeColor = iconColor,
            BackColor = iconBackground,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 2, 8, 2)
        };
        text = new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.6f),
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            Margin = Padding.Empty
        };
        block.Controls.Add(icon, 0, 0);
        block.Controls.Add(text, 1, 0);
        return block;
    }
}
