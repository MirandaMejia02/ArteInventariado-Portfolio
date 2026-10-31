namespace ArteInventariado.WinForms.Controls;

internal static class VisualLayout
{
    public static TableLayoutPanel CreatePageHeader(
        string title,
        string subtitle,
        Control? trailing = null)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceMd, UITheme.SpaceLg, UITheme.SpaceMd),
            ColumnCount = trailing is null ? 1 : 2,
            RowCount = 1,
            Margin = Padding.Empty
        };

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (trailing is not null)
        {
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        var text = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        text.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        text.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        text.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.TextDark,
            Margin = Padding.Empty
        }, 0, 0);
        text.Controls.Add(new Label
        {
            Text = subtitle,
            AutoSize = true,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(0, UITheme.SpaceXs, 0, 0)
        }, 0, 1);

        header.Controls.Add(text, 0, 0);
        if (trailing is not null)
        {
            trailing.Anchor = AnchorStyles.Right;
            header.Controls.Add(trailing, 1, 0);
        }

        return header;
    }

    public static TableLayoutPanel CreateSection(
        string title,
        string description,
        Control content,
        int contentTopMargin = UITheme.SpaceMd)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceLg),
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        section.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = UITheme.SectionTitleFont,
            ForeColor = UITheme.TextDark,
            Margin = Padding.Empty
        }, 0, 0);
        section.Controls.Add(new Label
        {
            Text = description,
            AutoSize = true,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(0, UITheme.SpaceXs, 0, 0)
        }, 0, 1);
        content.Margin = new Padding(0, contentTopMargin, 0, 0);
        section.Controls.Add(content, 0, 2);

        return section;
    }

    public static TableLayoutPanel CreateField(string labelText, Control input, string? hint = null)
    {
        var rows = string.IsNullOrWhiteSpace(hint) ? 2 : 3;
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = rows,
            Margin = new Padding(UITheme.SpaceXs)
        };
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (rows == 3)
        {
            field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        field.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, UITheme.SpaceXs)
        }, 0, 0);
        UITheme.StyleInput(input);
        input.Dock = DockStyle.Top;
        input.Margin = Padding.Empty;
        field.Controls.Add(input, 0, 1);

        if (rows == 3)
        {
            field.Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                Font = UITheme.CaptionFont,
                ForeColor = UITheme.MutedTextColor,
                Margin = new Padding(0, UITheme.SpaceXs, 0, 0)
            }, 0, 2);
        }

        return field;
    }

    public static FlowLayoutPanel CreateActionBar(params Control[] actions)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceMd, UITheme.SpaceSm, UITheme.SpaceMd, UITheme.SpaceSm),
            Margin = Padding.Empty
        };

        foreach (var action in actions)
        {
            action.Margin = new Padding(0, 0, UITheme.SpaceSm, 0);
            bar.Controls.Add(action);
        }

        return bar;
    }

    public static TableLayoutPanel CreateEmptyState(string title, string description)
    {
        var state = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UITheme.SpaceXl)
        };
        state.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        state.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        state.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomCenter
        }, 0, 0);
        state.Controls.Add(new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.TopCenter,
            Padding = new Padding(0, UITheme.SpaceSm, 0, 0)
        }, 0, 1);
        return state;
    }
}
