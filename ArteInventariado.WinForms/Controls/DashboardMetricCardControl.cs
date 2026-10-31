namespace ArteInventariado.WinForms.Controls;

internal sealed class DashboardMetricCardControl : DashboardSurfacePanel
{
    private readonly Label _icon = new();
    private readonly Label _title = new();
    private readonly Label _value = new();
    private readonly Label _support = new();
    private readonly Label _comparison = new();

    public DashboardMetricCardControl(string title, string iconGlyph, Color accentColor, bool highlighted = false)
    {
        MinimumSize = new Size(205, 132);
        Height = 132;
        Margin = new Padding(0, 0, 12, 12);
        Padding = new Padding(14);

        var accentSurface = highlighted ? accentColor : Color.FromArgb(255, 239, 246);
        var titleColor = highlighted ? Color.White : accentColor;
        var valueColor = highlighted ? Color.White : UITheme.TextDark;
        var mutedColor = highlighted ? Color.FromArgb(255, 238, 246) : UITheme.MutedTextColor;

        if (highlighted)
        {
            BackColor = accentColor;
        }

        _icon.Text = iconGlyph;
        _icon.Font = new Font("Segoe MDL2 Assets", 20, FontStyle.Regular);
        _icon.ForeColor = titleColor;
        _icon.BackColor = accentSurface;
        _icon.TextAlign = ContentAlignment.MiddleCenter;
        _icon.Dock = DockStyle.Fill;

        _title.Text = title;
        _title.AutoEllipsis = true;
        _title.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _title.ForeColor = titleColor;
        _title.Dock = DockStyle.Fill;
        _title.TextAlign = ContentAlignment.BottomLeft;

        _value.Text = "Cargando...";
        _value.AutoEllipsis = true;
        _value.Font = new Font("Segoe UI", 17, FontStyle.Bold);
        _value.ForeColor = valueColor;
        _value.Dock = DockStyle.Fill;
        _value.TextAlign = ContentAlignment.MiddleLeft;

        _support.AutoEllipsis = true;
        _support.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        _support.ForeColor = mutedColor;
        _support.Dock = DockStyle.Fill;
        _support.TextAlign = ContentAlignment.TopLeft;

        _comparison.AutoEllipsis = true;
        _comparison.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _comparison.ForeColor = mutedColor;
        _comparison.Dock = DockStyle.Fill;
        _comparison.TextAlign = ContentAlignment.TopLeft;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_icon, 0, 0);
        layout.SetRowSpan(_icon, 3);
        layout.Controls.Add(_title, 1, 0);
        layout.Controls.Add(_value, 1, 1);
        layout.Controls.Add(_support, 1, 2);
        layout.Controls.Add(_comparison, 0, 3);
        layout.SetColumnSpan(_comparison, 2);
        Controls.Add(layout);
    }

    public void SetContent(string value, string support, string comparison = "", Color? comparisonColor = null)
    {
        _value.Text = value;
        _support.Text = support;
        _comparison.Text = comparison;
        if (comparisonColor.HasValue)
        {
            _comparison.ForeColor = comparisonColor.Value;
        }
    }

    public void SetError() => SetContent("No disponible", "No se pudo cargar", "Reintente en unos segundos", UITheme.DangerColor);
}
