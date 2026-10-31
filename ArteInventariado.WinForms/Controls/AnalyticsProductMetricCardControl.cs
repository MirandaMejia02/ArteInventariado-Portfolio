namespace ArteInventariado.WinForms.Controls;

internal sealed class AnalyticsProductMetricCardControl : DashboardSurfacePanel
{
    internal const int LogicalHeight = 88;

    private readonly Label _icon = new();
    private readonly Label _title = new();
    private readonly Label _name = new();
    private readonly Label _value = new();
    private readonly ToolTip _toolTip = new();

    public AnalyticsProductMetricCardControl(string title, string iconGlyph, Color accent, Color iconBackground)
    {
        Name = $"ProductKpi{title.Replace(" ", string.Empty)}";
        Dock = DockStyle.Fill;
        MinimumSize = new Size(170, LogicalHeight);
        Margin = new Padding(0, 0, 10, 0);
        Padding = new Padding(10);

        _icon.Text = iconGlyph;
        _icon.Dock = DockStyle.Fill;
        _icon.Font = new Font("Segoe MDL2 Assets", 20f);
        _icon.ForeColor = accent;
        _icon.BackColor = iconBackground;
        _icon.TextAlign = ContentAlignment.MiddleCenter;
        _icon.Margin = Padding.Empty;

        _title.Text = title;
        _title.Dock = DockStyle.Fill;
        _title.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _title.ForeColor = UITheme.MutedTextColor;
        _title.TextAlign = ContentAlignment.BottomLeft;
        _title.Margin = new Padding(10, 0, 0, 0);

        _name.Text = "—";
        _name.Dock = DockStyle.Fill;
        _name.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _name.ForeColor = accent;
        _name.TextAlign = ContentAlignment.MiddleLeft;
        _name.AutoEllipsis = true;
        _name.Margin = new Padding(10, 0, 0, 0);

        _value.Text = "Sin actividad";
        _value.Dock = DockStyle.Fill;
        _value.Font = new Font("Segoe UI", 8.7f);
        _value.ForeColor = UITheme.TextDark;
        _value.TextAlign = ContentAlignment.TopLeft;
        _value.AutoEllipsis = false;
        _value.Margin = new Padding(10, 0, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        layout.Controls.Add(_icon, 0, 0);
        layout.SetRowSpan(_icon, 3);
        layout.Controls.Add(_title, 1, 0);
        layout.Controls.Add(_name, 1, 1);
        layout.Controls.Add(_value, 1, 2);
        Controls.Add(layout);
    }

    public void SetContent(string name, string value)
    {
        _name.Text = name;
        _value.Text = value;
        _toolTip.SetToolTip(_name, name);
        _toolTip.SetToolTip(_value, value);
    }

    public void SetEmpty(string message = "Sin actividad") => SetContent("—", message);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}
