namespace ArteInventariado.WinForms.Controls;

internal sealed class AnalyticsNavigationButton : Button
{
    private readonly string _iconGlyph;
    private readonly Font _iconFont = new("Segoe MDL2 Assets", 14f);
    private bool _selected;
    private bool _hovered;

    public AnalyticsNavigationButton(string title, string iconGlyph)
    {
        _iconGlyph = iconGlyph;
        Text = title;
        AccessibleName = title;
        Dock = DockStyle.Fill;
        Margin = new Padding(4, 3, 4, 3);
        Padding = new Padding(8, 0, 8, 0);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseOverBackColor = UITheme.DashboardAccentSoftColor;
        FlatAppearance.MouseDownBackColor = UITheme.SoftPinkColor;
        BackColor = UITheme.SurfaceColor;
        ForeColor = UITheme.SecondaryColor;
        Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
        TextAlign = ContentAlignment.MiddleCenter;
        Cursor = Cursors.Hand;
        AutoEllipsis = true;
        UseVisualStyleBackColor = false;
        Tag = "analytics-section-button";
        UpdateAppearance();
    }

    public void SetSelected(bool selected)
    {
        if (_selected == selected)
        {
            return;
        }

        _selected = selected;
        UpdateAppearance();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var background = _selected || _hovered
            ? UITheme.DashboardAccentSoftColor
            : UITheme.SurfaceColor;
        pevent.Graphics.Clear(background);
        using var border = new Pen(_selected ? UITheme.PrimaryColor : UITheme.DashboardBorderColor);
        pevent.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

        if (_selected)
        {
            var thickness = Math.Max(2, (int)Math.Round(3 * DeviceDpi / 96d));
            using var brush = new SolidBrush(UITheme.PrimaryColor);
            pevent.Graphics.FillRectangle(brush, 0, 0, Width, thickness);
        }

        var foreground = _selected ? UITheme.PrimaryColor : UITheme.SecondaryColor;
        var content = Rectangle.Inflate(ClientRectangle, -8, -4);
        var iconSize = TextRenderer.MeasureText(_iconGlyph, _iconFont, Size.Empty, TextFormatFlags.NoPadding);
        var titleSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var gap = 8;
        var groupWidth = Math.Min(content.Width, iconSize.Width + gap + titleSize.Width);
        var groupLeft = content.Left + Math.Max(0, (content.Width - groupWidth) / 2);
        var iconBounds = new Rectangle(groupLeft, content.Top, iconSize.Width, content.Height);
        var titleBounds = new Rectangle(
            iconBounds.Right + gap,
            content.Top,
            Math.Max(1, content.Right - iconBounds.Right - gap),
            content.Height);
        TextRenderer.DrawText(
            pevent.Graphics,
            _iconGlyph,
            _iconFont,
            iconBounds,
            foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            titleBounds,
            foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private void UpdateAppearance()
    {
        BackColor = _selected ? UITheme.DashboardAccentSoftColor : UITheme.SurfaceColor;
        ForeColor = _selected ? UITheme.PrimaryColor : UITheme.SecondaryColor;
        FlatAppearance.BorderColor = _selected ? UITheme.PrimaryColor : UITheme.DashboardBorderColor;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
