namespace ArteInventariado.WinForms;

public static class UITheme
{
    // Colors
    public static readonly Color PrimaryColor = Color.FromArgb(203, 43, 116);
    public static readonly Color SecondaryColor = Color.FromArgb(113, 50, 82);
    public static readonly Color BackgroundColor = Color.FromArgb(255, 246, 250);
    public static readonly Color SurfaceColor = Color.White;
    public static readonly Color TextDark = Color.FromArgb(70, 42, 58);
    public static readonly Color TextLight = Color.White;
    public static readonly Color SuccessColor = Color.FromArgb(79, 160, 121);
    public static readonly Color DangerColor = Color.FromArgb(183, 51, 82);
    public static readonly Color SoftPinkColor = Color.FromArgb(255, 225, 238);
    public static readonly Color BorderPinkColor = Color.FromArgb(241, 169, 202);
    public static readonly Color MutedTextColor = Color.FromArgb(129, 92, 111);
    public static readonly Color DashboardBackgroundColor = Color.FromArgb(250, 249, 252);
    public static readonly Color DashboardBorderColor = Color.FromArgb(234, 224, 230);
    public static readonly Color DashboardSidebarColor = Color.FromArgb(82, 30, 57);
    public static readonly Color DashboardSidebarHoverColor = Color.FromArgb(113, 50, 82);
    public static readonly Color DashboardAccentSoftColor = Color.FromArgb(255, 239, 246);
    public static readonly Color WarningColor = Color.FromArgb(205, 139, 29);
    public static readonly Color SurfaceMutedColor = Color.FromArgb(255, 250, 252);
    public static readonly Color NeutralBorderColor = Color.FromArgb(226, 214, 221);
    public static readonly Color DisabledSurfaceColor = Color.FromArgb(244, 239, 242);

    public const int SpaceXs = 4;
    public const int SpaceSm = 8;
    public const int SpaceMd = 12;
    public const int SpaceLg = 20;
    public const int SpaceXl = 24;
    public const int StandardControlHeight = 38;
    public const int CompactControlHeight = 34;

    public const string BusinessName = "ArteInventariado Demo";
    public const string LogoFileName = "ArteInventariado.png";

    // Fonts
    public static Font TitleFont => new("Segoe UI", 20, FontStyle.Bold);
    public static Font HeaderFont => new("Segoe UI", 16, FontStyle.Bold);
    public static Font SubheaderFont => new("Segoe UI", 12, FontStyle.Bold);
    public static Font SectionTitleFont => new("Segoe UI", 13, FontStyle.Bold);
    public static Font CaptionFont => new("Segoe UI", 9, FontStyle.Regular);
    public static Font NormalFont => new("Segoe UI", 10, FontStyle.Regular);
    public static Font NormalBoldFont => new("Segoe UI", 10, FontStyle.Bold);
    public static Font ButtonFont => new("Segoe UI", 11, FontStyle.Bold);
    public static Font LargeButtonFont => new("Segoe UI", 14, FontStyle.Bold);
    public static Font HugeTitleFont => new("Segoe UI", 24, FontStyle.Bold);
    public static Font CartItemFont => new("Segoe UI", 11, FontStyle.Regular);

    // Styling Methods
    public static void StyleForm(Form form, bool applyApplicationIcon = true)
    {
        form.BackColor = BackgroundColor;
        form.ForeColor = TextDark;
        form.Font = NormalFont;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        var excludedForm = form.GetType().Name is
            nameof(SystemConfigForm) or
            nameof(CloudBackupProvisioningForm) or
            nameof(CloudBackupProvisioningAuthorizationForm);
        if (applyApplicationIcon && !excludedForm && form.Icon is null)
        {
            form.Icon = LoadApplicationIcon();
        }
    }

    public static Icon? LoadApplicationIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ArteInventariado.ico");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var icon = new Icon(stream);
            return new Icon(icon, icon.Size);
        }
        catch
        {
            return null;
        }
    }

    public static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(219, 74, 139);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(176, 34, 98);
        button.BackColor = PrimaryColor;
        button.ForeColor = TextLight;
        button.Font = ButtonFont;
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.MinimumSize = new Size(0, StandardControlHeight);
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleLargePrimaryButton(Button button)
    {
        StylePrimaryButton(button);
        button.Font = LargeButtonFont;
    }

    public static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(134, 66, 100);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(95, 38, 68);
        button.BackColor = SecondaryColor;
        button.ForeColor = TextLight;
        button.Font = ButtonFont;
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.MinimumSize = new Size(0, StandardControlHeight);
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleLargeSecondaryButton(Button button)
    {
        StyleSecondaryButton(button);
        button.Font = LargeButtonFont;
    }

    public static void StyleDangerButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(201, 68, 99);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(150, 38, 64);
        button.BackColor = DangerColor;
        button.ForeColor = TextLight;
        button.Font = ButtonFont;
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.MinimumSize = new Size(0, StandardControlHeight);
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleSuccessButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = SuccessColor;
        button.ForeColor = TextLight;
        button.Font = ButtonFont;
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 143, 103);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(45, 122, 85);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.MinimumSize = new Size(0, StandardControlHeight);
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleOutlineButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = BorderPinkColor;
        button.FlatAppearance.MouseOverBackColor = DashboardAccentSoftColor;
        button.FlatAppearance.MouseDownBackColor = SoftPinkColor;
        button.BackColor = SurfaceColor;
        button.ForeColor = SecondaryColor;
        button.Font = ButtonFont;
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.MinimumSize = new Size(0, StandardControlHeight);
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleHugeSuccessButton(Button button)
    {
        StyleSuccessButton(button);
        button.Font = HugeTitleFont;
    }

    public static void EnsureButtonContentFits(
        Button button,
        int minimumWidth,
        int height,
        int horizontalPadding = 40)
    {
        var measured = TextRenderer.MeasureText(
            button.Text,
            button.Font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        button.MinimumSize = new Size(
            Math.Max(minimumWidth, measured.Width + horizontalPadding),
            height);
        button.AutoEllipsis = false;
        button.TextAlign = ContentAlignment.MiddleCenter;
    }

    public static void StyleLabelTitle(Label label)
    {
        label.Font = TitleFont;
        label.ForeColor = PrimaryColor;
    }

    public static void StyleLabelHeader(Label label)
    {
        label.Font = HeaderFont;
        label.ForeColor = TextDark;
    }

    public static void StyleLabelNormal(Label label)
    {
        label.Font = NormalFont;
        label.ForeColor = TextDark;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = SurfaceColor;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SecondaryColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextLight;
        grid.ColumnHeadersDefaultCellStyle.Font = NormalBoldFont;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SecondaryColor;
        grid.ColumnHeadersHeight = 40;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle.BackColor = SurfaceColor;
        grid.DefaultCellStyle.ForeColor = TextDark;
        grid.DefaultCellStyle.SelectionBackColor = SoftPinkColor;
        grid.DefaultCellStyle.SelectionForeColor = TextDark;
        grid.AlternatingRowsDefaultCellStyle.BackColor = BackgroundColor;
        grid.RowTemplate.Height = 34;
        grid.GridColor = BorderPinkColor;
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.Font = NormalFont;
        textBox.BackColor = SurfaceColor;
        textBox.ForeColor = TextDark;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void StyleInput(Control control)
    {
        control.Font = NormalFont;
        control.BackColor = SurfaceColor;
        control.ForeColor = TextDark;
        control.MinimumSize = new Size(control.MinimumSize.Width, CompactControlHeight);

        switch (control)
        {
            case TextBox textBox:
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.IntegralHeight = false;
                comboBox.DropDownHeight = 260;
                break;
            case NumericUpDown numeric:
                numeric.BorderStyle = BorderStyle.FixedSingle;
                numeric.TextAlign = HorizontalAlignment.Right;
                break;
            case DateTimePicker datePicker:
                datePicker.CalendarForeColor = TextDark;
                datePicker.CalendarMonthBackground = SurfaceColor;
                break;
        }
    }

    public static void StyleTabControl(TabControl tabs)
    {
        tabs.Font = NormalBoldFont;
        tabs.Padding = new Point(16, 8);
        tabs.ItemSize = new Size(0, 36);
        tabs.SizeMode = TabSizeMode.Normal;
    }

    public static void StyleSurface(Control control, bool muted = false)
    {
        control.BackColor = muted ? SurfaceMutedColor : SurfaceColor;
        control.ForeColor = TextDark;
        control.Padding = new Padding(SpaceMd);
    }

    public static void StyleStatusLabel(Label label, Color? foreground = null)
    {
        label.Font = NormalBoldFont;
        label.ForeColor = foreground ?? MutedTextColor;
        label.BackColor = SurfaceMutedColor;
        label.Padding = new Padding(SpaceMd, SpaceSm, SpaceMd, SpaceSm);
        label.TextAlign = ContentAlignment.MiddleLeft;
    }

    public static void StyleEmptyStateLabel(Label label)
    {
        label.Font = SubheaderFont;
        label.ForeColor = MutedTextColor;
        label.BackColor = SurfaceColor;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Padding = new Padding(SpaceXl);
    }

    public static PictureBox CreateLogoPictureBox(int size)
    {
        return new PictureBox
        {
            Image = LoadLogoImage(),
            Size = new Size(size, size),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
    }

    public static Image? LoadLogoImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", LogoFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    public static void StyleDataGridView(DataGridView grid)
    {
        grid.BackgroundColor = SurfaceColor;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;

        grid.ColumnHeadersDefaultCellStyle.BackColor = SecondaryColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextLight;
        grid.ColumnHeadersDefaultCellStyle.Font = NormalBoldFont;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SecondaryColor;
        grid.ColumnHeadersHeight = 40;

        grid.DefaultCellStyle.BackColor = SurfaceColor;
        grid.DefaultCellStyle.ForeColor = TextDark;
        grid.DefaultCellStyle.SelectionBackColor = SoftPinkColor;
        grid.DefaultCellStyle.SelectionForeColor = TextDark;
        grid.DefaultCellStyle.Font = NormalFont;

        grid.RowTemplate.Height = 35;
        grid.GridColor = NeutralBorderColor;
        grid.RowHeadersVisible = false;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle.Padding = new Padding(SpaceXs, 0, SpaceXs, 0);
    }

    public static void StyleCartGrid(DataGridView grid)
    {
        grid.BackgroundColor = SurfaceColor;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = BorderPinkColor;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;

        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
        grid.ColumnHeadersDefaultCellStyle.Font = NormalBoldFont;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceColor;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersHeight = 34;

        grid.DefaultCellStyle.BackColor = SurfaceColor;
        grid.DefaultCellStyle.ForeColor = TextDark;
        grid.DefaultCellStyle.SelectionBackColor = SoftPinkColor;
        grid.DefaultCellStyle.SelectionForeColor = TextDark;
        grid.DefaultCellStyle.Font = CartItemFont;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.Padding = new Padding(5);

        grid.RowTemplate.Height = 42;
    }
}
