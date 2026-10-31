using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;
using ArteInventariado.WinForms.Helpers;
using System.Drawing.Drawing2D;

namespace ArteInventariado.WinForms;

public class EmployeeDashboardForm : Form
{
    private readonly IAuthService _authService;
    private readonly ICurrentSession _currentSession;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IInstallationContext _installationContext;
    private readonly CashRecoveryCoordinator? _cashRecovery;
    private readonly IStationSettingsService? _stationSettingsService;
    private readonly SidebarNavigationButton _btnPos = new();
    private readonly SidebarNavigationButton _btnQuickPos = new();
    private readonly SidebarNavigationButton _btnReturns = new();
    private readonly SidebarNavigationButton _btnCash = new();
    private readonly SidebarNavigationButton _btnInventory = new();
    private readonly SidebarNavigationButton _btnChangePassword = new();
    private readonly SidebarNavigationButton _btnLogout = new();
    private readonly TableLayoutPanel _shell = new();
    private readonly Panel _sidebar = new();
    private readonly FlowLayoutPanel _sidebarNavigation = new();
    private readonly FlowLayoutPanel _quickActions = new();
    private readonly ToolTip _navigationToolTip = new();
    private readonly List<SidebarNavigationButton> _sidebarButtons = [];
    private readonly List<Label> _sidebarSectionLabels = [];
    private readonly List<SidebarNavigationButton> _dynamicModuleButtons = [];
    private readonly Dictionary<Button, EmployeeQuickAccessCard> _quickAccessCards = [];
    private Label? _modulesSectionLabel;
    private Label? _accountSectionLabel;
    private readonly SemaphoreSlim _permissionRefreshLock = new(1, 1);
    private bool _applicationCloseReviewed;
    private bool _applicationCloseReviewing;

    public EmployeeDashboardForm(
        IAuthService authService,
        ICurrentSession currentSession,
        IServiceProvider serviceProvider,
        IEmployeePermissionService permissionService,
        IInstallationContext installationContext,
        CashRecoveryCoordinator? cashRecovery = null,
        IStationSettingsService? stationSettingsService = null)
    {
        _authService = authService;
        _currentSession = currentSession;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        _installationContext = installationContext;
        _cashRecovery = cashRecovery;
        _stationSettingsService = stationSettingsService;

        BuildUi();
        Load += async (_, _) =>
        {
            KeyDown += Dashboard_KeyDown;
            await ApplyPermissionsAsync();
            if (_cashRecovery is not null)
            {
                await _cashRecovery.EnsureReviewedAsync(this, requireContinuationForOperation: false);
            }
        };
        Activated += async (_, _) => await ApplyPermissionsAsync();
    }

    private void Dashboard_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11)
        {
            FullScreenHelper.ToggleFullScreen(this);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape && FullScreenHelper.IsFullScreen(this))
        {
            FullScreenHelper.ExitFullScreen(this);
            e.Handled = true;
        }
    }

    private void BuildUi()
    {
        Text = "Panel Empleado";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        // Keep the logical minimum below the 1280x720 @ 150% work area.
        // Both columns remain usable because their long content scrolls.
        MinimumSize = new Size(760, 440);
        KeyPreview = true;
        UITheme.StyleForm(this);
        BackColor = UITheme.DashboardBackgroundColor;

        var usuario = _currentSession.UsuarioActual;

        _shell.Dock = DockStyle.Fill;
        _shell.ColumnCount = 2;
        _shell.RowCount = 1;
        _shell.Margin = Padding.Empty;
        _shell.Padding = Padding.Empty;
        _shell.BackColor = UITheme.DashboardBackgroundColor;
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        BuildSidebar(usuario);
        _shell.Controls.Add(_sidebar, 0, 0);
        _shell.Controls.Add(BuildMainArea(usuario), 1, 0);
        Controls.Add(_shell);

        SetAccessVisible(_btnPos, false);
        SetAccessVisible(_btnQuickPos, false);
        SetAccessVisible(_btnReturns, false);
        SetAccessVisible(_btnCash, false);
    }

    private void BuildSidebar(UsuarioSesion? usuario)
    {
        _sidebar.Dock = DockStyle.Fill;
        _sidebar.BackColor = UITheme.DashboardSidebarColor;
        _sidebar.Padding = new Padding(10, 12, 10, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UITheme.DashboardSidebarColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Reserve a little more vertical space for the identity block so the
        // logo, user name and role remain readable before the navigation starts.
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 274));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(BuildSidebarIdentity(usuario), 0, 0);

        _sidebarNavigation.Dock = DockStyle.Fill;
        _sidebarNavigation.FlowDirection = FlowDirection.TopDown;
        _sidebarNavigation.WrapContents = false;
        _sidebarNavigation.AutoScroll = true;
        _sidebarNavigation.BackColor = UITheme.DashboardSidebarColor;
        _sidebarNavigation.Padding = new Padding(0, 4, 0, 8);
        _sidebarNavigation.Margin = Padding.Empty;
        _sidebarNavigation.SizeChanged += (_, _) => ResizeSidebarButtons();

        AddSidebarSection("OPERACIÓN");
        AddSidebarButton(_btnPos, "\uE7BF", "Punto de venta", BtnPos_Click);
        AddSidebarButton(_btnQuickPos, "\uE719", "POS rápido", BtnQuickPos_Click);
        _navigationToolTip.SetToolTip(_btnQuickPos, "POS rápido · Teclado · Touch · Scanner");
        AddSidebarButton(_btnReturns, "\uE777", "Devoluciones", BtnReturns_Click);
        AddSidebarButton(_btnCash, "\uE8C7", "Caja", BtnCash_Click);
        AddSidebarButton(_btnInventory, "\uE7B8", "Consultar inventario", OpenInventory_Click);

        _modulesSectionLabel = AddSidebarSection("MÓDULOS DISPONIBLES");
        _modulesSectionLabel.Visible = false;
        _accountSectionLabel = AddSidebarSection("CUENTA");
        AddSidebarButton(_btnChangePassword, "\uE72E", "Cambiar clave", BtnChangePassword_Click);

        ConfigureSidebarButton(_btnLogout, "\uE8AC", "Cerrar sesión", BtnLogout_Click);
        _btnLogout.Dock = DockStyle.Fill;
        _btnLogout.BackColor = UITheme.DangerColor;
        _btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(204, 61, 93);

        layout.Controls.Add(_sidebarNavigation, 0, 1);
        layout.Controls.Add(_btnLogout, 0, 2);
        _sidebar.Controls.Add(layout);
    }

    private Control BuildSidebarIdentity(UsuarioSesion? usuario)
    {
        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UITheme.DashboardSidebarColor,
            Margin = Padding.Empty,
            Padding = new Padding(10, 4, 10, 10)
        };
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));

        var logo = UITheme.CreateLogoPictureBox(100);
        logo.Anchor = AnchorStyles.None;
        logo.Margin = Padding.Empty;

        var title = new Label
        {
            Text = "Panel Empleado",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };
        var name = new Label
        {
            Text = usuario?.NombreCompleto ?? "Sesión no disponible",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };
        var account = new Label
        {
            Text = usuario is null ? "Usuario: -" : $"Usuario: {usuario.NombreUsuario}",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(245, 215, 231),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };
        var role = new Label
        {
            Text = usuario is null ? "Rol: -" : $"Rol: {usuario.Rol}",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(245, 215, 231),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };

        identity.Controls.Add(logo, 0, 0);
        identity.Controls.Add(title, 0, 1);
        identity.Controls.Add(name, 0, 2);
        identity.Controls.Add(account, 0, 3);
        identity.Controls.Add(role, 0, 4);
        return identity;
    }

    private Control BuildMainArea(UsuarioSesion? usuario)
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.DashboardBackgroundColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.Controls.Add(BuildMainHeader(), 0, 0);
        main.Controls.Add(BuildScrollableHome(usuario), 0, 1);
        return main;
    }

    private Control BuildMainHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(24, 12, 24, 10),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var icon = new Label
        {
            Text = "\uE80F",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            BackColor = UITheme.DashboardAccentSoftColor,
            ForeColor = UITheme.PrimaryColor,
            Font = new Font("Segoe MDL2 Assets", 19f),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        identity.Controls.Add(new Label
        {
            Text = "Inicio del empleado",
            Dock = DockStyle.Fill,
            Font = UITheme.TitleFont,
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true
        }, 0, 0);
        identity.Controls.Add(new Label
        {
            Text = "Accesos rápidos y módulos disponibles para tu jornada.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        }, 0, 1);

        var mode = new Label
        {
            Text = _installationContext.Status.DisplayName,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(18, 0, 0, 0),
            Font = UITheme.NormalBoldFont,
            ForeColor = _installationContext.Status.Mode == InstallationMode.Consulta
                ? UITheme.PrimaryColor
                : UITheme.SuccessColor,
            BackColor = _installationContext.Status.Mode == InstallationMode.Consulta
                ? UITheme.DashboardAccentSoftColor
                : Color.FromArgb(235, 248, 239)
        };

        header.Controls.Add(icon, 0, 0);
        header.Controls.Add(identity, 1, 0);
        header.Controls.Add(mode, 2, 0);
        return header;
    }

    private Control BuildScrollableHome(UsuarioSesion? usuario)
    {
        var scroll = new Panel
        {
            Name = "EmployeeDashboardMainScroll",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UITheme.DashboardBackgroundColor,
            Margin = Padding.Empty
        };
        var content = new TableLayoutPanel
        {
            Name = "EmployeeDashboardHomeContent",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UITheme.DashboardBackgroundColor,
            Padding = new Padding(28, 24, 28, 24),
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        content.Controls.Add(BuildWelcomeCard(usuario), 0, 0);
        content.Controls.Add(BuildQuickAccessHeader(), 0, 1);

        _quickActions.Name = "EmployeeDashboardQuickActions";
        _quickActions.Dock = DockStyle.Fill;
        _quickActions.AutoSize = true;
        _quickActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _quickActions.WrapContents = true;
        _quickActions.FlowDirection = FlowDirection.LeftToRight;
        _quickActions.BackColor = UITheme.DashboardBackgroundColor;
        _quickActions.Margin = Padding.Empty;
        _quickActions.Padding = Padding.Empty;
        AddQuickAction(_btnPos, "\uE7BF", "Punto de venta", "Realiza ventas y gestiona clientes de forma rápida.", BtnPos_Click);
        AddQuickAction(_btnQuickPos, "\uE719", "POS rápido", "Teclado, touch y scanner en un flujo ágil.", BtnQuickPos_Click);
        AddQuickAction(_btnReturns, "\uE777", "Devoluciones", "Consulta y procesa devoluciones autorizadas.", BtnReturns_Click);
        AddQuickAction(_btnCash, "\uE8C7", "Caja", "Apertura, movimientos y cierre de caja.", BtnCash_Click);
        AddQuickAction(_btnInventory, "\uE7B8", "Consultar inventario", "Revisa existencias y detalles de productos.", OpenInventory_Click);
        content.Controls.Add(_quickActions, 0, 2);
        content.Controls.Add(BuildAccessStatusCard(), 0, 4);
        scroll.Controls.Add(content);
        return scroll;
    }

    private Control BuildWelcomeCard(UsuarioSesion? usuario)
    {
        var card = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0),
            BackColor = UITheme.SurfaceColor
        };
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = UITheme.PrimaryColor, Margin = Padding.Empty }, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(28, 18, 28, 16),
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 44));

        var greeting = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        greeting.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        greeting.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        greeting.Controls.Add(new Label
        {
            Text = "\uE77B",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 14, 4),
            BackColor = UITheme.DashboardAccentSoftColor,
            ForeColor = UITheme.PrimaryColor,
            Font = new Font("Segoe MDL2 Assets", 26f),
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);
        var greetingText = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        greetingText.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        greetingText.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        greetingText.Controls.Add(new Label
        {
            Text = $"Bienvenido, {usuario?.NombreCompleto ?? "Empleado"}",
            Dock = DockStyle.Fill,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true
        }, 0, 0);
        greetingText.Controls.Add(new Label
        {
            Text = "Gestiona tus operaciones diarias desde un solo panel de trabajo.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        }, 0, 1);
        greeting.Controls.Add(greetingText, 1, 0);

        var facts = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(0, 8, 0, 0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        facts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        facts.Controls.Add(BuildWelcomeFact("\uE787", "Fecha", DateTime.Now.ToString("dd/MM/yyyy")), 0, 0);
        facts.Controls.Add(BuildWelcomeFact("\uE823", "Sesión", "Activa"), 1, 0);
        facts.Controls.Add(BuildWelcomeFact("\uEA18", "Rol", usuario?.Rol.ToString() ?? "EMPLEADO"), 2, 0);

        body.Controls.Add(greeting, 0, 0);
        body.Controls.Add(facts, 0, 1);
        shell.Controls.Add(body, 1, 0);
        card.Controls.Add(shell);
        return card;
    }

    private static Control BuildWelcomeFact(string iconGlyph, string title, string value)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(4, 2, 4, 2)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var icon = new Label
        {
            Text = iconGlyph,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe MDL2 Assets", 18f),
            ForeColor = UITheme.SuccessColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var text = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        text.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true
        }, 0, 0);
        text.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        }, 0, 1);
        layout.Controls.Add(icon, 0, 0);
        layout.Controls.Add(text, 1, 0);
        return layout;
    }

    private static Control BuildQuickAccessHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 12, 0, 6)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 7));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.PrimaryColor,
            Margin = new Padding(0, 4, 10, 4)
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = "Accesos rápidos",
            Dock = DockStyle.Fill,
            Font = UITheme.SectionTitleFont,
            ForeColor = UITheme.PrimaryColor,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 1, 0);
        header.Controls.Add(new Label
        {
            Text = "Módulos principales disponibles para ti",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true
        }, 2, 0);
        return header;
    }

    private static Control BuildAccessStatusCard()
    {
        var card = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = UITheme.SurfaceColor
        };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(new Label
        {
            Text = "\uE73E",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe MDL2 Assets", 20f),
            ForeColor = UITheme.SuccessColor,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);
        content.Controls.Add(new Label
        {
            Text = "Accesos habilitados\nTus módulos disponibles se muestran en el menú lateral y en los accesos rápidos.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 1, 0);
        card.Controls.Add(content);
        return card;
    }

    private void AddQuickAction(
        Button sourceButton,
        string iconGlyph,
        string title,
        string description,
        EventHandler handler)
    {
        var card = new EmployeeQuickAccessCard(title, iconGlyph, description);
        card.Click += handler;
        _quickActions.Controls.Add(card);
        _quickAccessCards[sourceButton] = card;
    }

    private Label AddSidebarSection(string text)
    {
        var label = new Label
        {
            Text = text,
            Width = 224,
            Height = 28,
            Margin = new Padding(8, 9, 0, 0),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(224, 188, 207),
            TextAlign = ContentAlignment.BottomLeft
        };
        _sidebarSectionLabels.Add(label);
        _sidebarNavigation.Controls.Add(label);
        return label;
    }

    private void AddSidebarButton(
        SidebarNavigationButton button,
        string icon,
        string text,
        EventHandler handler)
    {
        ConfigureSidebarButton(button, icon, text, handler);
        _sidebarNavigation.Controls.Add(button);
    }

    private void ConfigureSidebarButton(
        SidebarNavigationButton button,
        string icon,
        string text,
        EventHandler handler)
    {
        button.IconGlyph = icon;
        button.Text = text;
        button.Height = 43;
        button.Width = 224;
        button.Margin = new Padding(0, 1, 0, 1);
        button.Padding = new Padding(40, 0, 5, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = UITheme.DashboardSidebarHoverColor;
        button.BackColor = UITheme.DashboardSidebarColor;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Cursor = Cursors.Hand;
        button.AutoEllipsis = true;
        button.Click += handler;
        _navigationToolTip.SetToolTip(button, text);
        _sidebarButtons.Add(button);
    }

    private void ResizeSidebarButtons()
    {
        var width = Math.Max(
            48,
            _sidebarNavigation.ClientSize.Width -
            _sidebarNavigation.Padding.Horizontal -
            SystemInformation.VerticalScrollBarWidth - 5);
        foreach (var button in _sidebarButtons.Where(button => button.Parent == _sidebarNavigation))
        {
            button.Width = width;
        }
        foreach (var label in _sidebarSectionLabels)
        {
            label.Width = Math.Max(48, width - label.Margin.Left);
        }
        _sidebarNavigation.HorizontalScroll.Enabled = false;
        _sidebarNavigation.HorizontalScroll.Visible = false;
    }

    private void SetAccessVisible(Button sourceButton, bool visible)
    {
        sourceButton.Visible = visible;
        if (_quickAccessCards.TryGetValue(sourceButton, out var card))
        {
            card.Visible = visible;
        }
    }

    private void SetAccessEnabled(Button sourceButton, bool enabled)
    {
        sourceButton.Enabled = enabled;
        if (_quickAccessCards.TryGetValue(sourceButton, out var card))
        {
            card.Enabled = enabled;
        }
    }

    private void OpenInventory_Click(object? sender, EventArgs e)
    {
        using var form = (EmployeeInventoryLookupForm)_serviceProvider.GetService(typeof(EmployeeInventoryLookupForm))!;
        form.ShowDialog(this);
    }

    private async void BtnPos_Click(object? sender, EventArgs e)
    {
        if (!await EnsurePermissionAsync(PermisoEmpleado.POS_ACCEDER))
        {
            return;
        }

        using var form = (EmployeePOSForm)_serviceProvider.GetService(typeof(EmployeePOSForm))!;
        form.ShowDialog(this);
    }

    private async void BtnQuickPos_Click(object? sender, EventArgs e)
    {
        if (!await EnsurePermissionAsync(PermisoEmpleado.POS_ACCEDER))
        {
            return;
        }

        if (_stationSettingsService is null ||
            !(await _stationSettingsService.GetPosSettingsAsync()).QuickPosEnabled)
        {
            MessageBox.Show("El POS rápido no está habilitado en esta PC.", "Panel empleado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetAccessVisible(_btnQuickPos, false);
            return;
        }

        using var form = (QuickPOSForm)_serviceProvider.GetService(typeof(QuickPOSForm))!;
        form.ShowDialog(this);
    }

    private async void BtnReturns_Click(object? sender, EventArgs e)
    {
        if (!await EnsurePermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER))
        {
            return;
        }

        using var form = (SaleCorrectionForm)_serviceProvider.GetService(typeof(SaleCorrectionForm))!;
        form.ShowDialog(this);
    }

    private async void BtnCash_Click(object? sender, EventArgs e)
    {
        if (!await HasCashPermissionAsync())
        {
            MessageBox.Show("No tiene permiso para acceder a caja.", "Panel empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var cashService = (ICashService)_serviceProvider.GetService(typeof(ICashService))!;
        if (!await HasCashOperationPermissionAsync())
        {
            using var history = new CashHistoryForm(cashService);
            history.ShowDialog(this);
            return;
        }

        var permissionService = (IEmployeePermissionService)_serviceProvider.GetService(typeof(IEmployeePermissionService))!;
        var movementQuery = (ICashMovementQueryService)_serviceProvider.GetService(typeof(ICashMovementQueryService))!;
        var reports = (ICommercialOperationsReportService)_serviceProvider.GetService(typeof(ICommercialOperationsReportService))!;
        using var form = new CashRegisterForm(
            cashService,
            _currentSession,
            permissionService,
            movementQuery,
            reports,
            CashRegisterMode.Employee,
            _installationContext,
            _cashRecovery);
        form.ShowDialog(this);
    }

    private async Task ApplyPermissionsAsync()
    {
        await _permissionRefreshLock.WaitAsync();
        try
        {
            var canUsePos = await _permissionService.HasPermissionAsync(PermisoEmpleado.POS_ACCEDER);
            SetAccessVisible(_btnPos, canUsePos);
            SetAccessVisible(
                _btnQuickPos,
                canUsePos && _stationSettingsService is not null &&
                (await _stationSettingsService.GetPosSettingsAsync()).QuickPosEnabled);
            SetAccessVisible(
                _btnReturns,
                await _permissionService.HasPermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER));
            SetAccessVisible(_btnCash, await HasCashPermissionAsync());
            ApplyInstallationMode();
            await RebuildMoreMenuAsync();
            ResizeSidebarButtons();
        }
        catch
        {
            SetAccessVisible(_btnPos, false);
            SetAccessVisible(_btnQuickPos, false);
            SetAccessVisible(_btnReturns, false);
            SetAccessVisible(_btnCash, false);
            ClearDynamicModuleButtons();
        }
        finally
        {
            _permissionRefreshLock.Release();
        }
    }

    private void ApplyInstallationMode()
    {
        if (_installationContext.AllowsOperationalWrites)
        {
            return;
        }

        const string reason = "Esta estacion esta en Modo Consulta. Las operaciones que escriben datos estan bloqueadas.";
        SetAccessEnabled(_btnPos, false);
        SetAccessEnabled(_btnReturns, false);
        SetAccessEnabled(_btnCash, false);
        SetAccessEnabled(_btnChangePassword, false);
        _navigationToolTip.SetToolTip(_btnPos, reason);
        _navigationToolTip.SetToolTip(_btnReturns, reason);
        _navigationToolTip.SetToolTip(_btnCash, reason);
        _navigationToolTip.SetToolTip(_btnChangePassword, reason);
    }

    private async Task<bool> EnsurePermissionAsync(PermisoEmpleado permiso)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(permiso);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Panel empleado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private Task<bool> HasCashPermissionAsync()
    {
        return _permissionService.HasAnyPermissionAsync([
            PermisoEmpleado.CAJA_ABRIR,
            PermisoEmpleado.CAJA_CERRAR,
            PermisoEmpleado.GASTO_REGISTRAR,
            PermisoEmpleado.RETIRO_REGISTRAR,
            PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER,
            PermisoEmpleado.CAJA_DIFERENCIAS_VER,
            PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER,
            PermisoEmpleado.CAJA_APORTE_REGISTRAR,
            PermisoEmpleado.CAJA_HISTORIAL_VER
        ]);
    }

    private Task<bool> HasCashOperationPermissionAsync()
    {
        return _permissionService.HasAnyPermissionAsync([
            PermisoEmpleado.CAJA_ABRIR,
            PermisoEmpleado.CAJA_CERRAR,
            PermisoEmpleado.GASTO_REGISTRAR,
            PermisoEmpleado.RETIRO_REGISTRAR,
            PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER,
            PermisoEmpleado.CAJA_DIFERENCIAS_VER,
            PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER,
            PermisoEmpleado.CAJA_APORTE_REGISTRAR
        ]);
    }

    private async Task RebuildMoreMenuAsync()
    {
        var permissions = new Dictionary<PermisoEmpleado, bool>
        {
            [PermisoEmpleado.PRODUCTOS_GESTIONAR] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR),
            [PermisoEmpleado.ETIQUETAS_GESTIONAR] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.ETIQUETAS_GESTIONAR),
            [PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR),
            [PermisoEmpleado.CLIENTES_ACCEDER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER),
            [PermisoEmpleado.HISTORIAL_VENTAS_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.HISTORIAL_VENTAS_VER),
            [PermisoEmpleado.VENTAS_PROPIAS_DIA_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.VENTAS_PROPIAS_DIA_VER),
            [PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER),
            [PermisoEmpleado.CUENTAS_COBRAR_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER),
            [PermisoEmpleado.ALERTAS_CREDITO_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.ALERTAS_CREDITO_VER),
            [PermisoEmpleado.PROVEEDORES_ACCEDER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.PROVEEDORES_ACCEDER),
            [PermisoEmpleado.COMPRAS_ACCEDER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPRAS_ACCEDER),
            [PermisoEmpleado.CUENTAS_PAGAR_ACCEDER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER),
            [PermisoEmpleado.COTIZACIONES_ACCEDER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.COTIZACIONES_ACCEDER),
            [PermisoEmpleado.PEDIDOS_ACCEDER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER),
            [PermisoEmpleado.ALERTAS_COMERCIALES_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.ALERTAS_COMERCIALES_VER),
            [PermisoEmpleado.REPORTES_COMERCIALES_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.REPORTES_COMERCIALES_VER),
            [PermisoEmpleado.COSTOS_VER] =
                await _permissionService.HasPermissionAsync(PermisoEmpleado.COSTOS_VER)
        };

        var menuItems = EmployeeDashboardMoreMenuCatalog.Build(permissions);
        if (DynamicModulesMatch(menuItems))
        {
            return;
        }

        var scrollPosition = Math.Abs(_sidebarNavigation.AutoScrollPosition.Y);
        ClearDynamicModuleButtons();
        var insertionIndex = _accountSectionLabel is null
            ? _sidebarNavigation.Controls.Count
            : _sidebarNavigation.Controls.GetChildIndex(_accountSectionLabel);

        foreach (var menuItem in menuItems)
        {
            var button = new SidebarNavigationButton { Tag = menuItem.Action };
            ConfigureSidebarButton(
                button,
                GetMoreActionIcon(menuItem.Action),
                menuItem.Text,
                MoreMenuItem_Click);
            _sidebarNavigation.Controls.Add(button);
            _sidebarNavigation.Controls.SetChildIndex(button, insertionIndex++);
            _dynamicModuleButtons.Add(button);
        }

        if (_modulesSectionLabel is not null)
        {
            _modulesSectionLabel.Visible = menuItems.Count > 0;
        }
        ResizeSidebarButtons();
        _sidebarNavigation.PerformLayout();
        if (scrollPosition > 0)
        {
            _sidebarNavigation.AutoScrollPosition = new Point(0, scrollPosition);
        }
    }

    private async void MoreMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is not Control { Tag: EmployeeDashboardMoreAction action })
        {
            return;
        }

        switch (action)
        {
            case EmployeeDashboardMoreAction.Etiquetas:
                await OpenLabelsAsync();
                break;
            case EmployeeDashboardMoreAction.Clientes:
                await OpenCustomersAsync();
                break;
            case EmployeeDashboardMoreAction.HistorialVentas:
                await OpenSalesHistoryAsync();
                break;
            case EmployeeDashboardMoreAction.MisVentasHoy:
                await OpenMySalesTodayAsync();
                break;
            case EmployeeDashboardMoreAction.HistorialDevoluciones:
                await OpenReturnsHistoryAsync();
                break;
            case EmployeeDashboardMoreAction.CuentasPorCobrar:
                await OpenAccountsReceivableAsync();
                break;
            case EmployeeDashboardMoreAction.AlertasCredito:
                await OpenCreditAlertsAsync();
                break;
            case EmployeeDashboardMoreAction.Proveedores:
                await OpenSuppliersAsync();
                break;
            case EmployeeDashboardMoreAction.Compras:
                await OpenPurchasesAsync();
                break;
            case EmployeeDashboardMoreAction.CuentasPorPagar:
                await OpenAccountsPayableAsync();
                break;
            case EmployeeDashboardMoreAction.Cotizaciones:
                await OpenQuotationsAsync();
                break;
            case EmployeeDashboardMoreAction.PedidosPersonalizados:
                await OpenCustomOrdersAsync();
                break;
            case EmployeeDashboardMoreAction.AlertasComerciales:
                await OpenCommercialAlertsAsync();
                break;
            case EmployeeDashboardMoreAction.ReportesComerciales:
                await OpenCommercialReportsAsync();
                break;
            case EmployeeDashboardMoreAction.RentabilidadProductos:
                await OpenProductProfitabilityAsync();
                break;
        }
    }

    private bool DynamicModulesMatch(IReadOnlyList<EmployeeDashboardMoreMenuItem> menuItems)
    {
        if (_dynamicModuleButtons.Count != menuItems.Count)
        {
            return false;
        }

        for (var index = 0; index < menuItems.Count; index++)
        {
            if (_dynamicModuleButtons[index].Tag is not EmployeeDashboardMoreAction action ||
                action != menuItems[index].Action ||
                !string.Equals(_dynamicModuleButtons[index].Text, menuItems[index].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void ClearDynamicModuleButtons()
    {
        foreach (var button in _dynamicModuleButtons)
        {
            _sidebarButtons.Remove(button);
            _sidebarNavigation.Controls.Remove(button);
            button.Dispose();
        }
        _dynamicModuleButtons.Clear();
        if (_modulesSectionLabel is not null)
        {
            _modulesSectionLabel.Visible = false;
        }
    }

    private static string GetMoreActionIcon(EmployeeDashboardMoreAction action) => action switch
    {
        EmployeeDashboardMoreAction.Etiquetas => "\uE7B8",
        EmployeeDashboardMoreAction.Clientes => "\uE716",
        EmployeeDashboardMoreAction.HistorialVentas => "\uE81C",
        EmployeeDashboardMoreAction.MisVentasHoy => "\uE823",
        EmployeeDashboardMoreAction.HistorialDevoluciones => "\uE777",
        EmployeeDashboardMoreAction.CuentasPorCobrar => "\uE8C7",
        EmployeeDashboardMoreAction.AlertasCredito => "\uE7BA",
        EmployeeDashboardMoreAction.Proveedores => "\uE716",
        EmployeeDashboardMoreAction.Compras => "\uE719",
        EmployeeDashboardMoreAction.CuentasPorPagar => "\uE8C7",
        EmployeeDashboardMoreAction.Cotizaciones => "\uE8A5",
        EmployeeDashboardMoreAction.PedidosPersonalizados => "\uE9F9",
        EmployeeDashboardMoreAction.AlertasComerciales => "\uE7BA",
        EmployeeDashboardMoreAction.ReportesComerciales => "\uE9D2",
        EmployeeDashboardMoreAction.RentabilidadProductos => "\uE9D9",
        _ => "\uE8A5"
    };

    private async Task ShowPermittedDialogAsync<TForm>(PermisoEmpleado permiso)
        where TForm : Form
    {
        if (!await EnsurePermissionAsync(permiso))
        {
            return;
        }

        using var form = (TForm)_serviceProvider.GetService(typeof(TForm))!;
        form.ShowDialog(this);
    }

    private Task OpenCommercialAlertsAsync() =>
        ShowPermittedDialogAsync<CommercialAlertsForm>(PermisoEmpleado.ALERTAS_COMERCIALES_VER);

    private Task OpenCommercialReportsAsync() =>
        ShowPermittedDialogAsync<ReportsForm>(PermisoEmpleado.REPORTES_COMERCIALES_VER);

    private async Task OpenProductProfitabilityAsync()
    {
        if (!await _permissionService.HasPermissionAsync(PermisoEmpleado.REPORTES_COMERCIALES_VER) ||
            !await _permissionService.HasPermissionAsync(PermisoEmpleado.COSTOS_VER))
        {
            MessageBox.Show(
                "No tiene permiso para consultar reportes comerciales con costos.",
                "Panel empleado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var form = (ProductProfitabilityForm)_serviceProvider
            .GetService(typeof(ProductProfitabilityForm))!;
        form.ShowDialog(this);
    }

    private Task OpenCustomersAsync() =>
        ShowPermittedDialogAsync<CustomersForm>(PermisoEmpleado.CLIENTES_ACCEDER);

    private async Task OpenLabelsAsync()
    {
        if (!await _permissionService.HasAnyPermissionAsync([
                PermisoEmpleado.PRODUCTOS_GESTIONAR,
                PermisoEmpleado.ETIQUETAS_GESTIONAR,
                PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR]))
        {
            MessageBox.Show("No tiene permiso para gestionar productos, etiquetas o imágenes.", "Panel empleado",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = (ProductsForm)_serviceProvider.GetService(typeof(ProductsForm))!;
        form.ShowDialog(this);
    }

    private Task OpenSalesHistoryAsync() =>
        ShowPermittedDialogAsync<SalesHistoryForm>(PermisoEmpleado.HISTORIAL_VENTAS_VER);

    private Task OpenMySalesTodayAsync() =>
        ShowPermittedDialogAsync<MySalesTodayForm>(PermisoEmpleado.VENTAS_PROPIAS_DIA_VER);

    private Task OpenReturnsHistoryAsync() =>
        ShowPermittedDialogAsync<ReturnsHistoryForm>(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER);

    private Task OpenAccountsReceivableAsync() =>
        ShowPermittedDialogAsync<AccountsReceivableForm>(PermisoEmpleado.CUENTAS_COBRAR_VER);

    private Task OpenCreditAlertsAsync() =>
        ShowPermittedDialogAsync<CreditAlertsForm>(PermisoEmpleado.ALERTAS_CREDITO_VER);

    private Task OpenSuppliersAsync() =>
        ShowPermittedDialogAsync<SuppliersForm>(PermisoEmpleado.PROVEEDORES_ACCEDER);

    private Task OpenPurchasesAsync() =>
        ShowPermittedDialogAsync<PurchasesForm>(PermisoEmpleado.COMPRAS_ACCEDER);

    private Task OpenAccountsPayableAsync() =>
        ShowPermittedDialogAsync<AccountsPayableForm>(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER);

    private Task OpenQuotationsAsync() =>
        ShowPermittedDialogAsync<QuotationsForm>(PermisoEmpleado.COTIZACIONES_ACCEDER);

    private Task OpenCustomOrdersAsync() =>
        ShowPermittedDialogAsync<CustomOrdersForm>(PermisoEmpleado.PEDIDOS_ACCEDER);

    private void BtnChangePassword_Click(object? sender, EventArgs e)
    {
        var accountService = (IAccountService)_serviceProvider.GetService(typeof(IAccountService))!;
        using var form = new ChangePasswordForm(accountService);
        form.ShowDialog(this);
    }

    private async void BtnLogout_Click(object? sender, EventArgs e)
    {
        _btnLogout.Enabled = false;
        try
        {
            if (_cashRecovery is not null &&
                !await _cashRecovery.ConfirmLogoutWithOpenCashAsync(this))
            {
                return;
            }

            try
            {
                await _authService.LogoutAsync();
            }
            catch (Exception ex)
            {
                TechnicalErrorLogger.Log("employee-logout", ex);
                MessageBox.Show(
                    "La sesion se cerro localmente, pero no se pudo registrar el evento en bitacora. " +
                    "El detalle tecnico fue guardado en el registro de errores.",
                    "Cerrar sesion",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            DialogResult = DialogResult.Retry;
            Close();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("employee-logout-cash-check", ex);
            MessageBox.Show(
                "No se pudo confirmar el estado de la caja. La sesion permanece abierta; revisa la caja e intenta de nuevo.",
                "Cerrar sesion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed && DialogResult != DialogResult.Retry)
            {
                _btnLogout.Enabled = true;
            }
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_applicationCloseReviewed ||
            ControlledShutdownState.IsActive ||
            e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing ||
            DialogResult == DialogResult.Retry)
        {
            base.OnFormClosing(e);
            return;
        }

        if (_applicationCloseReviewing)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _applicationCloseReviewing = true;
        try
        {
            if (_cashRecovery is not null)
            {
                await _cashRecovery.WarnOpenCashWillRemainAsync(this);
            }
        }
        finally
        {
            _applicationCloseReviewing = false;
        }
        _applicationCloseReviewed = true;
        if (!IsDisposed)
        {
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _navigationToolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class EmployeeQuickAccessCard : Button
{
    private const int CornerRadius = 10;
    private readonly Font _iconFont = new("Segoe MDL2 Assets", 25f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _titleFont = new("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _descriptionFont = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _arrowFont = new("Segoe MDL2 Assets", 12f, FontStyle.Regular, GraphicsUnit.Point);
    private bool _hovered;

    internal EmployeeQuickAccessCard(string title, string iconGlyph, string description)
    {
        Title = title;
        IconGlyph = iconGlyph;
        Description = description;
        Text = title;
        AccessibleName = title;
        AccessibleDescription = description;
        // Keep the width unchanged while giving the description its own
        // breathing room between the title and the action affordance.
        Size = new Size(222, 330);
        MinimumSize = new Size(190, 310);
        Margin = new Padding(0, 0, 14, 14);
        Padding = Padding.Empty;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = UITheme.SurfaceColor;
        ForeColor = UITheme.TextDark;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        TabStop = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    internal string Title { get; }
    internal string IconGlyph { get; }
    internal string Description { get; }

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

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var cardPath = CreateRoundedPath(bounds, Logical(CornerRadius));
        var background = !Enabled
            ? UITheme.DisabledSurfaceColor
            : _hovered
                ? UITheme.DashboardAccentSoftColor
                : UITheme.SurfaceColor;
        using var backgroundBrush = new SolidBrush(background);
        using var borderPen = new Pen(_hovered && Enabled ? UITheme.BorderPinkColor : UITheme.DashboardBorderColor);
        e.Graphics.FillPath(backgroundBrush, cardPath);
        e.Graphics.DrawPath(borderPen, cardPath);

        var iconSize = Logical(72);
        var iconBounds = new Rectangle((Width - iconSize) / 2, Logical(22), iconSize, iconSize);
        using var iconBrush = new SolidBrush(Enabled ? UITheme.DashboardAccentSoftColor : UITheme.DisabledSurfaceColor);
        e.Graphics.FillEllipse(iconBrush, iconBounds);
        TextRenderer.DrawText(
            e.Graphics,
            IconGlyph,
            _iconFont,
            iconBounds,
            Enabled ? UITheme.PrimaryColor : UITheme.MutedTextColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix);

        var titleBounds = new Rectangle(
            Logical(14),
            iconBounds.Bottom + Logical(11),
            Math.Max(1, Width - Logical(28)),
            Logical(44));
        TextRenderer.DrawText(
            e.Graphics,
            Title,
            _titleFont,
            titleBounds,
            Enabled ? UITheme.SecondaryColor : UITheme.MutedTextColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            // Let long titles such as "Consultar inventario" wrap instead of
            // replacing the ending with an ellipsis.
            TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix);

        var arrowSize = Logical(32);
        var arrowBounds = new Rectangle(
            (Width - arrowSize) / 2,
            Height - arrowSize - Logical(15),
            arrowSize,
            arrowSize);
        var descriptionTop = titleBounds.Bottom + Logical(2);
        var descriptionBounds = new Rectangle(
            Logical(18),
            descriptionTop,
            Math.Max(1, Width - Logical(36)),
            Math.Max(Logical(26), arrowBounds.Top - descriptionTop - Logical(8)));
        TextRenderer.DrawText(
            e.Graphics,
            Description,
            _descriptionFont,
            descriptionBounds,
            Enabled ? UITheme.MutedTextColor : Color.FromArgb(160, UITheme.MutedTextColor),
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.Top |
            TextFormatFlags.WordBreak |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        using var arrowBrush = new SolidBrush(Enabled ? UITheme.DashboardAccentSoftColor : UITheme.DisabledSurfaceColor);
        e.Graphics.FillEllipse(arrowBrush, arrowBounds);
        TextRenderer.DrawText(
            e.Graphics,
            "\uE72A",
            _arrowFont,
            arrowBounds,
            Enabled ? UITheme.PrimaryColor : UITheme.MutedTextColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(bounds, -Logical(5), -Logical(5));
            ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, ForeColor, background);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
            _titleFont.Dispose();
            _descriptionFont.Dispose();
            _arrowFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Logical(int value) => (int)Math.Round(value * DeviceDpi / 96f, MidpointRounding.AwayFromZero);

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Max(2, radius * 2);
        diameter = Math.Min(diameter, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
