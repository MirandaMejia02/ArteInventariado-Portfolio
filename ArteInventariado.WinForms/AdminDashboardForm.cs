using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.WinForms.Controls;
using ArteInventariado.WinForms.Helpers;

namespace ArteInventariado.WinForms;

public class AdminDashboardForm : Form
{
    private readonly IAuthService _authService;
    private readonly ICurrentSession _currentSession;
    private readonly ICreditAlertQueryService _creditAlertService;
    private readonly ICommercialAlertQueryService _commercialAlertService;
    private readonly IDashboardQueryService _dashboardQueryService;
    private readonly IInstallationContext _installationContext;
    private readonly IInstallationManagementService _installationManagement;
    private readonly CashRecoveryCoordinator? _cashRecovery;
    private readonly IStationSettingsService? _stationSettingsService;
    private readonly IServiceProvider _serviceProvider;
    private readonly AdminNavigationCoordinator _navigation;
    private readonly TableLayoutPanel _shell = new();
    private readonly Panel _sidebar = new();
    private TableLayoutPanel? _sidebarLayout;
    private TableLayoutPanel? _sidebarBrandLayout;
    private FlowLayoutPanel? _sidebarNavigation;
    private PictureBox? _sidebarLogo;
    private readonly SidebarNavigationButton _btnHome = new();
    private readonly Button _btnCollapseMenu = new();
    private readonly ToolTip _navigationToolTip = new();
    private readonly List<Label> _sidebarSectionLabels = [];
    private readonly List<SidebarNavigationButton> _sidebarButtons = [];
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 30_000 };
    private DashboardHomeControl? _home;
    private Label? _brandLabel;
    private Label? _footerClock;
    private bool _sidebarCollapsed;
    private bool _sidebarPreferenceSet;
    private readonly SidebarNavigationButton _btnSales = new();
    private readonly SidebarNavigationButton _btnQuickSales = new();
    private readonly SidebarNavigationButton _btnReturns = new();
    private readonly SidebarNavigationButton _btnReturnsHistory = new();
    private readonly SidebarNavigationButton _btnProducts = new();
    private readonly SidebarNavigationButton _btnCategories = new();
    private readonly SidebarNavigationButton _btnSubcategories = new();
    private readonly SidebarNavigationButton _btnUsers = new();
    private readonly SidebarNavigationButton _btnEmployees = new();
    private readonly SidebarNavigationButton _btnEmployeePermissions = new();
    private readonly SidebarNavigationButton _btnCustomers = new();
    private readonly SidebarNavigationButton _btnSuppliers = new();
    private readonly SidebarNavigationButton _btnPurchases = new();
    private readonly SidebarNavigationButton _btnQuotations = new();
    private readonly SidebarNavigationButton _btnCustomOrders = new();
    private readonly SidebarNavigationButton _btnAccountsReceivable = new();
    private readonly SidebarNavigationButton _btnAccountsPayable = new();
    private readonly SidebarNavigationButton _btnCash = new();
    private readonly SidebarNavigationButton _btnAnalytics = new();
    private readonly SidebarNavigationButton _btnReports = new();
    private readonly SidebarNavigationButton _btnExcelImport = new();
    private readonly SidebarNavigationButton _btnAuditLog = new();
    private readonly SidebarNavigationButton _btnSettings = new();
    private readonly SidebarNavigationButton _btnStation = new();
    private readonly SidebarNavigationButton _btnChangePassword = new();
    private readonly SidebarNavigationButton _btnLogout = new();
    private readonly Button _btnCreditAlerts = new();
    private readonly Label _lblCreditAlertBadge = new();
    private readonly ContextMenuStrip _creditAlertsMenu = new();
    private readonly ToolTip _creditAlertsToolTip = new();
    private bool _initialPasswordChecked;
    private bool _refreshingCreditAlerts;
    private bool _applicationCloseReviewed;
    private bool _applicationCloseReviewing;
    private bool _quickPosEnabled;
    private bool _quickPosOpening;
    private readonly Label _installationModeLabel = new();

    public AdminDashboardForm(
        IAuthService authService,
        ICurrentSession currentSession,
        ICreditAlertQueryService creditAlertService,
        ICommercialAlertQueryService commercialAlertService,
        IDashboardQueryService dashboardQueryService,
        IServiceProvider serviceProvider,
        IInstallationContext installationContext,
        IInstallationManagementService installationManagement,
        CashRecoveryCoordinator? cashRecovery = null,
        IStationSettingsService? stationSettingsService = null)
    {
        _authService = authService;
        _currentSession = currentSession;
        _creditAlertService = creditAlertService;
        _commercialAlertService = commercialAlertService;
        _dashboardQueryService = dashboardQueryService;
        _serviceProvider = serviceProvider;
        _installationContext = installationContext;
        _installationManagement = installationManagement;
        _cashRecovery = cashRecovery;
        _stationSettingsService = stationSettingsService;
        _navigation = new AdminNavigationCoordinator(serviceProvider, currentSession, installationContext);
        _navigation.ModuleClosed += (_, _) => _home?.RefreshDashboard();

        BuildUi();
    }

    internal IReadOnlyList<SidebarNavigationButton> SidebarButtonsForSmoke => _sidebarButtons;

    internal void SetSidebarCollapsedForSmoke(bool collapsed) => SetSidebarCollapsed(collapsed);

    internal string GetSidebarTooltipForSmoke(SidebarNavigationButton button) =>
        _navigationToolTip.GetToolTip(button) ?? string.Empty;

    private void BuildUi()
    {
        Text = "ArteInventariado - Panel Administrador";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(800, 480);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += Dashboard_KeyDown;
        UITheme.StyleForm(this);
        BackColor = UITheme.DashboardBackgroundColor;

        var usuario = _currentSession.UsuarioActual;
        _shell.Dock = DockStyle.Fill;
        _shell.ColumnCount = 2;
        _shell.RowCount = 1;
        _shell.Margin = Padding.Empty;
        _shell.Padding = Padding.Empty;
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 246));
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        BuildSidebar();
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UITheme.DashboardBackgroundColor
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        _home = new DashboardHomeControl(
            _dashboardQueryService,
            usuario?.NombreCompleto ?? "Administrador")
        {
            Dock = DockStyle.Fill
        };
        WireQuickActions(_home);

        main.Controls.Add(BuildHeader(usuario), 0, 0);
        main.Controls.Add(_home, 0, 1);
        main.Controls.Add(BuildFooter(usuario), 0, 2);
        _shell.Controls.Add(_sidebar, 0, 0);
        _shell.Controls.Add(main, 1, 0);
        Controls.Add(_shell);

        Resize += (_, _) => ApplyAutomaticSidebarMode();
        _clockTimer.Tick += (_, _) => UpdateFooterClock();
        _clockTimer.Start();
        UpdateFooterClock();
    }

    private void BuildSidebar()
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
        _sidebarLayout = layout;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        _sidebarBrandLayout = brand;
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        var logo = UITheme.CreateLogoPictureBox(44);
        _sidebarLogo = logo;
        logo.Anchor = AnchorStyles.Left;
        _brandLabel = new Label
        {
            Text = "ArteInventariado\nGestion comercial",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _btnCollapseMenu.Text = "\uE700";
        _btnCollapseMenu.Font = new Font("Segoe MDL2 Assets", 12);
        _btnCollapseMenu.Dock = DockStyle.Fill;
        _btnCollapseMenu.FlatStyle = FlatStyle.Flat;
        _btnCollapseMenu.FlatAppearance.BorderSize = 0;
        _btnCollapseMenu.BackColor = Color.Transparent;
        _btnCollapseMenu.ForeColor = Color.White;
        _btnCollapseMenu.Cursor = Cursors.Hand;
        _btnCollapseMenu.Click += (_, _) =>
        {
            _sidebarPreferenceSet = true;
            SetSidebarCollapsed(!_sidebarCollapsed);
        };
        _navigationToolTip.SetToolTip(_btnCollapseMenu, "Contraer o expandir menu");
        brand.Controls.Add(logo, 0, 0);
        brand.Controls.Add(_brandLabel, 1, 0);
        brand.Controls.Add(_btnCollapseMenu, 2, 0);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UITheme.DashboardSidebarColor,
            Padding = new Padding(0, 2, 0, 4),
            Margin = Padding.Empty
        };
        _sidebarNavigation = navigation;
        navigation.SizeChanged += (_, _) => ResizeSidebarButtons(navigation);

        AddSidebarButton(navigation, _btnHome, "\uE80F", "Inicio", (_, _) => _home?.RefreshDashboard(), active: true);
        AddSidebarSection(navigation, "OPERACIONES");
        AddSidebarButton(navigation, _btnSales, "\uE7BF", "Ventas", BtnSales_Click);
        AddSidebarButton(navigation, _btnQuickSales, "\uE719", "POS rápido", BtnQuickSales_Click);
        _navigationToolTip.SetToolTip(_btnQuickSales, "F8 · Abrir POS rápido");
        _btnQuickSales.Visible = false;
        AddSidebarButton(navigation, _btnReturns, "\uE777", "Devoluciones", BtnReturns_Click);
        AddSidebarButton(navigation, _btnReturnsHistory, "\uE81C", "Historial devoluciones", BtnReturnsHistory_Click);
        AddSidebarButton(navigation, _btnProducts, "\uE7B8", "Productos", BtnProducts_Click);
        AddSidebarButton(navigation, _btnCategories, "\uE8EC", "Categorias", BtnCategories_Click);
        AddSidebarButton(navigation, _btnSubcategories, "\uE8EC", "Subcategorías", BtnSubcategories_Click);
        AddSidebarButton(navigation, _btnCustomers, "\uE716", "Clientes", BtnCustomers_Click);
        AddSidebarSection(navigation, "ABASTECIMIENTO");
        AddSidebarButton(navigation, _btnSuppliers, "\uE716", "Proveedores y artesanos", BtnSuppliers_Click);
        AddSidebarButton(navigation, _btnPurchases, "\uE719", "Compras", BtnPurchases_Click);
        AddSidebarButton(navigation, _btnQuotations, "\uE8A5", "Cotizaciones", BtnQuotations_Click);
        AddSidebarButton(navigation, _btnCustomOrders, "\uE9F9", "Pedidos personalizados", BtnCustomOrders_Click);
        AddSidebarButton(navigation, _btnAccountsReceivable, "\uE8C7", "Cuentas por cobrar", BtnAccountsReceivable_Click);
        AddSidebarButton(navigation, _btnAccountsPayable, "\uE8C7", "Cuentas por pagar", BtnAccountsPayable_Click);
        AddSidebarButton(navigation, _btnCash, "\uE8C7", "Caja", BtnCash_Click);
        AddSidebarSection(navigation, "ANALITICA");
        AddSidebarButton(navigation, _btnAnalytics, "\uE9D2", "Analisis", BtnAnalytics_Click);
        AddSidebarButton(navigation, _btnReports, "\uE9D2", "Reportes", BtnReports_Click);
        AddSidebarSection(navigation, "ADMINISTRACION");
        AddSidebarButton(navigation, _btnUsers, "\uE716", "Usuarios", BtnUsers_Click);
        AddSidebarButton(navigation, _btnEmployees, "\uE77B", "Empleados", BtnEmployees_Click);
        AddSidebarButton(navigation, _btnEmployeePermissions, "\uEA18", "Permisos empleados", BtnEmployeePermissions_Click);
        AddSidebarButton(navigation, _btnExcelImport, "\uE8B5", "Importar Excel", BtnExcelImport_Click);
        AddSidebarButton(navigation, _btnAuditLog, "\uE81C", "Bitacora", BtnAuditLog_Click);
        AddSidebarButton(navigation, _btnSettings, "\uE713", "Configuracion", BtnSettings_Click);
        if (CanManageStationIdentity())
        {
            AddSidebarButton(navigation, _btnStation, "\uE7F8", "Identidad de estacion", BtnStation_Click);
        }
        else
        {
            _btnStation.Visible = false;
        }
        AddSidebarButton(navigation, _btnChangePassword, "\uE72E", "Cambiar clave", BtnChangePassword_Click);

        ConfigureSidebarButton(_btnLogout, "\uE8AC", "Cerrar sesion", BtnLogout_Click);
        _btnLogout.Dock = DockStyle.Fill;
        _btnLogout.BackColor = UITheme.DangerColor;
        _btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(204, 61, 93);

        layout.Controls.Add(brand, 0, 0);
        layout.Controls.Add(navigation, 0, 1);
        layout.Controls.Add(_btnLogout, 0, 2);
        _sidebar.Controls.Add(layout);
    }

    private Control BuildHeader(UsuarioSesion? usuario)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 10, 18, 9),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        header.Controls.Add(UITheme.CreateLogoPictureBox(50), 0, 0);

        var identity = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        identity.Controls.Add(new Label
        {
            Text = "ArteInventariado",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        identity.Controls.Add(new Label
        {
            Text = "Gestion de inventario y ventas",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        header.Controls.Add(identity, 1, 0);

        var user = new Label
        {
            Text = usuario is null ? "Sesion no disponible" : $"{usuario.NombreCompleto}\nADMIN",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(10, 0, 16, 0)
        };
        ConfigureInstallationModeLabel();
        header.Controls.Add(_installationModeLabel, 2, 0);
        header.Controls.Add(user, 3, 0);

        var alertHost = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor };
        ConfigureCreditAlertBell(alertHost);
        alertHost.Controls.AddRange([_btnCreditAlerts, _lblCreditAlertBadge]);
        header.Controls.Add(alertHost, 4, 0);
        return header;
    }

    private Control BuildFooter(UsuarioSesion? usuario)
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.SecondaryColor,
            Padding = new Padding(14, 0, 14, 0),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(new Label
        {
            Text = $"Sistema: ArteInventariado  |  Usuario: {usuario?.NombreUsuario ?? "-"}  |  Rol: ADMIN",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _footerClock = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleRight
        };
        footer.Controls.Add(_footerClock, 1, 0);
        return footer;
    }

    private void AddSidebarSection(FlowLayoutPanel navigation, string text)
    {
        var label = new Label
        {
            Text = text,
            Width = 210,
            Height = 27,
            Margin = new Padding(8, 8, 0, 0),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(224, 188, 207),
            TextAlign = ContentAlignment.BottomLeft
        };
        _sidebarSectionLabels.Add(label);
        navigation.Controls.Add(label);
    }

    private void AddSidebarButton(
        FlowLayoutPanel navigation,
        SidebarNavigationButton button,
        string icon,
        string text,
        EventHandler handler,
        bool active = false)
    {
        ConfigureSidebarButton(button, icon, text, handler);
        if (active)
        {
            button.BackColor = UITheme.PrimaryColor;
        }
        navigation.Controls.Add(button);
    }

    private void ConfigureSidebarButton(SidebarNavigationButton button, string icon, string text, EventHandler handler)
    {
        button.Tag = new SidebarItemPresentation(text);
        button.IconGlyph = icon;
        button.Text = text;
        button.Height = 40;
        button.Width = 210;
        button.Margin = new Padding(0, 1, 0, 1);
        button.Padding = new Padding(40, 0, 4, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = UITheme.DashboardSidebarHoverColor;
        button.BackColor = UITheme.DashboardSidebarColor;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Cursor = Cursors.Hand;
        button.AutoEllipsis = true;
        button.Click += handler;
        _navigationToolTip.SetToolTip(button, text);
        _sidebarButtons.Add(button);
    }

    private void ResizeSidebarButtons(FlowLayoutPanel navigation)
    {
        var width = Math.Max(
            42,
            navigation.ClientSize.Width - navigation.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (var button in _sidebarButtons.Where(b => b.Parent == navigation))
        {
            button.Width = width;
        }
        foreach (var label in _sidebarSectionLabels)
        {
            label.Width = Math.Max(42, width - label.Margin.Left);
        }
        navigation.HorizontalScroll.Enabled = false;
        navigation.HorizontalScroll.Visible = false;
    }

    private void SetSidebarCollapsed(bool collapsed)
    {
        _sidebarCollapsed = collapsed;
        _shell.SuspendLayout();
        _sidebar.SuspendLayout();
        _sidebarBrandLayout?.SuspendLayout();
        _sidebarNavigation?.SuspendLayout();
        _shell.ColumnStyles[0].Width = ScaleLogicalPixels(collapsed ? 92 : 246);
        if (_sidebarBrandLayout is not null)
        {
            var logoColumn = _sidebarBrandLayout.ColumnStyles[0];
            var textColumn = _sidebarBrandLayout.ColumnStyles[1];
            var toggleColumn = _sidebarBrandLayout.ColumnStyles[2];

            logoColumn.SizeType = collapsed ? SizeType.Percent : SizeType.Absolute;
            logoColumn.Width = collapsed ? 100 : ScaleLogicalPixels(48);
            textColumn.SizeType = collapsed ? SizeType.Absolute : SizeType.Percent;
            textColumn.Width = collapsed ? 0 : 100;
            toggleColumn.SizeType = SizeType.Absolute;
            toggleColumn.Width = ScaleLogicalPixels(32);
        }
        if (_sidebarLogo is not null)
        {
            var logoSize = ScaleLogicalPixels(collapsed ? 36 : 44);
            _sidebarLogo.Size = new Size(logoSize, logoSize);
            _sidebarLogo.Anchor = collapsed ? AnchorStyles.None : AnchorStyles.Left;
            _sidebarLogo.Visible = true;
        }
        if (_brandLabel is not null)
        {
            _brandLabel.Visible = !collapsed;
        }
        foreach (var label in _sidebarSectionLabels)
        {
            label.Visible = !collapsed;
        }
        foreach (var button in _sidebarButtons)
        {
            if (button.Tag is not SidebarItemPresentation presentation)
            {
                continue;
            }
            button.Visible = button != _btnQuickSales || _quickPosEnabled;
            button.Text = collapsed ? string.Empty : presentation.Text;
            button.CenterIcon = collapsed;
            button.TextAlign = collapsed ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            button.Padding = collapsed
                ? Padding.Empty
                : new Padding(ScaleLogicalPixels(40), 0, ScaleLogicalPixels(4), 0);
            button.Invalidate();
        }
        _btnCollapseMenu.Text = collapsed ? "\uE76C" : "\uE700";
        _btnCollapseMenu.Visible = true;
        _sidebarNavigation?.SetAutoScrollMargin(0, 0);
        if (_sidebarNavigation is not null)
        {
            _sidebarNavigation.AutoScrollPosition = Point.Empty;
        }
        _shell.ResumeLayout(true);
        _sidebar.ResumeLayout(true);
        _sidebarBrandLayout?.ResumeLayout(true);
        _sidebarNavigation?.ResumeLayout(true);
        _shell.PerformLayout();
        _sidebar.PerformLayout();
        _sidebarLayout?.PerformLayout();
        _sidebarBrandLayout?.PerformLayout();
        if (_sidebarNavigation is not null)
        {
            ResizeSidebarButtons(_sidebarNavigation);
            _sidebarNavigation.PerformLayout();
        }
    }

    private int ScaleLogicalPixels(int value)
    {
        var dpiScale = DeviceDpi / 96f;
        var appliedLayoutScale = _sidebar.Padding.Left > 0 ? _sidebar.Padding.Left / 10f : 1f;
        return (int)Math.Round(value * Math.Max(dpiScale, appliedLayoutScale), MidpointRounding.AwayFromZero);
    }

    private void ApplyAutomaticSidebarMode()
    {
        if (_sidebarPreferenceSet)
        {
            return;
        }
        SetSidebarCollapsed(ClientSize.Width < 1200);
    }

    private void WireQuickActions(DashboardHomeControl home)
    {
        home.NewSaleRequested += (_, _) => ExecuteWriteNavigation(() => _navigation.OpenNewSale(this));
        home.NewProductRequested += (_, _) => ExecuteWriteNavigation(() => _navigation.OpenNewProduct(this));
        home.NewPurchaseRequested += (_, _) => ExecuteWriteNavigation(() => _navigation.OpenNewPurchase(this));
        home.NewQuotationRequested += (_, _) => ExecuteWriteNavigation(() => _navigation.OpenNewQuotation(this));
        home.CashRequested += (_, _) => OpenModule(AdminModule.Cash);
        home.ReportsRequested += (_, _) => OpenModule(AdminModule.Reports);
        home.ExcelImportRequested += (_, _) => OpenModule(AdminModule.ExcelImport);
    }

    private void OpenModule(AdminModule module) => ExecuteNavigation(() => _navigation.Open(module, this));

    private static void ExecuteNavigation(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Panel Administrador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExecuteWriteNavigation(Action action)
    {
        try
        {
            _installationContext.EnsureOperationalWriteAllowed("Accion operativa");
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Panel Administrador", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void UpdateFooterClock()
    {
        if (_footerClock is not null)
        {
            _footerClock.Text = DateTime.Now.ToString("dd/MM/yyyy  hh:mm tt");
        }
    }

    private sealed record SidebarItemPresentation(string Text);

    private void Dashboard_KeyDown(object? sender, KeyEventArgs e)
        => HandleDashboardKeyDown(e, LaunchQuickPos);

    private void HandleDashboardKeyDown(KeyEventArgs e, Action launchQuickPos)
    {
        if (e.KeyCode == Keys.F8)
        {
            OpenQuickPos(launchQuickPos);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F11)
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

    private bool OpenQuickPos(Action launchQuickPos)
    {
        if (!_quickPosEnabled || _quickPosOpening)
        {
            return false;
        }

        _quickPosOpening = true;
        try
        {
            launchQuickPos();
            return true;
        }
        finally
        {
            _quickPosOpening = false;
        }
    }

    internal (bool Handled, bool SuppressKeyPress) SimulateDashboardKeyDownForSmoke(
        Keys keyCode,
        Action launchQuickPos)
    {
        var args = new KeyEventArgs(keyCode);
        HandleDashboardKeyDown(args, launchQuickPos);
        return (args.Handled, args.SuppressKeyPress);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshCreditAlertsAsync();
        await RefreshQuickPosAccessAsync();

        if (_initialPasswordChecked)
        {
            return;
        }

        _initialPasswordChecked = true;
        var installationProfile = await _installationManagement.GetSetupProfileAsync();
        if (!IsDisposed && DialogResult != DialogResult.Retry)
        {
            await EnsureStationConfigurationAsync(installationProfile);
        }

        if (!IsDisposed && DialogResult != DialogResult.Retry)
        {
            if (_cashRecovery is not null)
            {
                await _cashRecovery.EnsureReviewedAsync(this, requireContinuationForOperation: false);
            }
        }
    }

    protected override async void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        await RefreshCreditAlertsAsync();
        await RefreshQuickPosAccessAsync();
    }

    private void BtnProducts_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Products);

    private void BtnSales_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Sales);

    private void BtnQuickSales_Click(object? sender, EventArgs e)
        => OpenQuickPos(LaunchQuickPos);

    private void LaunchQuickPos()
        => ExecuteNavigation(() => _navigation.OpenQuickSale(this));

    private async Task RefreshQuickPosAccessAsync()
    {
        try
        {
            _quickPosEnabled = _stationSettingsService is not null &&
                (await _stationSettingsService.GetPosSettingsAsync()).QuickPosEnabled;
            _btnQuickSales.Visible = _quickPosEnabled;
        }
        catch
        {
            _quickPosEnabled = false;
            _btnQuickSales.Visible = false;
        }
    }

    private void BtnReturns_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Returns);

    private void BtnReturnsHistory_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.ReturnsHistory);

    private void BtnCategories_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Categories);

    private void BtnSubcategories_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Subcategories);

    private void BtnUsers_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Users);
    private void BtnEmployees_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Employees);

    private void BtnEmployeePermissions_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.EmployeePermissions);

    private void BtnCustomers_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Customers);

    private void BtnSuppliers_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Suppliers);

    private void BtnPurchases_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Purchases);

    private void BtnQuotations_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Quotations);

    private void BtnCustomOrders_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.CustomOrders);

    private void BtnAccountsReceivable_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.AccountsReceivable);

    private void BtnAccountsPayable_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.AccountsPayable);

    private void BtnCash_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Cash);

    private void BtnReports_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Reports);

    private void BtnAnalytics_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Analytics);

    private async void BtnCreditAlerts_Click(object? sender, EventArgs e)
    {
        try
        {
            var summary = await _creditAlertService.GetSummaryAsync();
            var commercial = await _commercialAlertService.GetSummaryAsync();
            BuildCreditAlertsMenu(summary, commercial);
            _creditAlertsMenu.Show(_btnCreditAlerts, new Point(0, _btnCreditAlerts.Height));
        }
        catch (Exception ex)
        {
            LogCreditAlertError(ex);
            MessageBox.Show(
                "No se pudieron actualizar las alertas de credito.",
                "Alertas de credito",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void BtnExcelImport_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.ExcelImport);

    private void BtnAuditLog_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.AuditLog);

    private void BtnSettings_Click(object? sender, EventArgs e)
        => OpenModule(AdminModule.Settings);

    private async void BtnStation_Click(object? sender, EventArgs e)
    {
        if (!EnsureCanManageStationIdentity())
        {
            return;
        }

        await ShowStationConfigurationAsync(required: false);
    }

    private void BtnChangePassword_Click(object? sender, EventArgs e)
        => ExecuteNavigation(() => _navigation.OpenChangePassword(this));

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

            await LogoutAndReturnToLoginAsync("admin-logout");
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("admin-logout-cash-check", ex);
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

    private void ConfigureCreditAlertBell(Panel header)
    {
        _btnCreditAlerts.Text = "!";
        _btnCreditAlerts.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        _btnCreditAlerts.Size = new Size(54, 42);
        _btnCreditAlerts.BackColor = Color.FromArgb(255, 245, 250);
        _btnCreditAlerts.ForeColor = UITheme.SecondaryColor;
        _btnCreditAlerts.FlatStyle = FlatStyle.Flat;
        _btnCreditAlerts.FlatAppearance.BorderColor = UITheme.SoftPinkColor;
        _btnCreditAlerts.FlatAppearance.BorderSize = 1;
        _btnCreditAlerts.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnCreditAlerts.Click += BtnCreditAlerts_Click;
        _creditAlertsToolTip.SetToolTip(_btnCreditAlerts, "Alertas del sistema");

        _lblCreditAlertBadge.AutoSize = false;
        _lblCreditAlertBadge.Size = new Size(34, 22);
        _lblCreditAlertBadge.BackColor = Color.FromArgb(196, 35, 70);
        _lblCreditAlertBadge.ForeColor = Color.White;
        _lblCreditAlertBadge.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _lblCreditAlertBadge.TextAlign = ContentAlignment.MiddleCenter;
        _lblCreditAlertBadge.Visible = false;
        _lblCreditAlertBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        header.Resize += (_, _) => PositionCreditAlertBell(header);
        PositionCreditAlertBell(header);
    }

    private void PositionCreditAlertBell(Panel header)
    {
        var x = Math.Max(20, header.ClientSize.Width - 92);
        var y = Math.Max(4, (header.ClientSize.Height - _btnCreditAlerts.Height) / 2);
        _btnCreditAlerts.Location = new Point(x, y);
        _lblCreditAlertBadge.Location = new Point(x + 34, Math.Max(0, y - 7));
    }

    private async Task RefreshCreditAlertsAsync()
    {
        if (_refreshingCreditAlerts || IsDisposed)
        {
            return;
        }

        _refreshingCreditAlerts = true;
        try
        {
            var summary = await _creditAlertService.GetSummaryAsync();
            var commercial = await _commercialAlertService.GetSummaryAsync();
            var total = summary.TotalActive + commercial.Total;
            _lblCreditAlertBadge.Visible = total > 0;
            _lblCreditAlertBadge.Text = total > 99 ? "99+" : total.ToString();
            _creditAlertsToolTip.SetToolTip(_btnCreditAlerts, "Alertas de credito y operaciones comerciales");
        }
        catch (Exception ex)
        {
            LogCreditAlertError(ex);
            _lblCreditAlertBadge.Visible = false;
            _creditAlertsToolTip.SetToolTip(_btnCreditAlerts, "No se pudieron actualizar las alertas.");
        }
        finally
        {
            _refreshingCreditAlerts = false;
        }
    }

    private void BuildCreditAlertsMenu(CreditAlertSummary summary, CommercialAlertSummary commercial)
    {
        _creditAlertsMenu.Items.Clear();
        if (summary.TotalActive == 0)
        {
            _creditAlertsMenu.Items.Add(new ToolStripMenuItem("No hay alertas de credito pendientes.") { Enabled = false });
        }
        else
        {
            AddSummaryMenuItem($"Morosas: {summary.Moroso}");
            AddSummaryMenuItem($"Vencidas: {summary.Overdue}");
            AddSummaryMenuItem($"Vencen hoy: {summary.DueToday}");
            AddSummaryMenuItem($"Proximas a vencer: {summary.UpcomingDue}");
            AddSummaryMenuItem($"Clientes cerca del limite: {summary.NearLimit + summary.LimitReached}");
        }

        _creditAlertsMenu.Items.Add(new ToolStripSeparator());
        var viewAll = new ToolStripMenuItem("Ver todas las alertas");
        viewAll.Click += (_, _) => OpenCreditAlertsForm();
        _creditAlertsMenu.Items.Add(viewAll);

        _creditAlertsMenu.Items.Add(new ToolStripSeparator());
        AddSummaryMenuItem($"Cotizaciones vencidas: {commercial.CotizacionesVencidas}");
        AddSummaryMenuItem($"Pedidos atrasados: {commercial.PedidosAtrasados}");
        AddSummaryMenuItem($"Listos para entrega: {commercial.ListosParaEntrega}");
        var viewCommercial = new ToolStripMenuItem("Ver alertas comerciales");
        viewCommercial.Click += (_, _) => OpenCommercialAlertsForm();
        _creditAlertsMenu.Items.Add(viewCommercial);
    }

    private void OpenCommercialAlertsForm()
        => OpenModule(AdminModule.CommercialAlerts);

    private void AddSummaryMenuItem(string text)
    {
        _creditAlertsMenu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });
    }

    private void OpenCreditAlertsForm()
        => OpenModule(AdminModule.CreditAlerts);

    private static void LogCreditAlertError(Exception ex)
    {
        try
        {
            var logDirectory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "credit-alerts-ui.log"),
                $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Could not write credit alerts UI log.");
        }
    }

    private void ConfigureInstallationModeLabel()
    {
        _installationModeLabel.Text = _installationContext.Status.DisplayName;
        _installationModeLabel.AutoSize = true;
        _installationModeLabel.Anchor = AnchorStyles.Right;
        _installationModeLabel.Padding = new Padding(10, 6, 10, 6);
        _installationModeLabel.Margin = new Padding(8, 0, 8, 0);
        _installationModeLabel.Font = UITheme.NormalBoldFont;
        _installationModeLabel.ForeColor = _installationContext.Status.Mode == InstallationMode.Consulta
            ? Color.FromArgb(120, 67, 10)
            : Color.White;
        _installationModeLabel.BackColor = _installationContext.Status.Mode == InstallationMode.Consulta
            ? Color.FromArgb(255, 235, 156)
            : _installationContext.Status.AllowsOperationalWrites
                ? UITheme.SuccessColor
                : UITheme.DangerColor;
    }

    private bool CanManageStationIdentity() =>
        ProtectedAdministrator.IsProtectedAdministrator(_currentSession.UsuarioActual);

    private bool EnsureCanManageStationIdentity()
    {
        if (CanManageStationIdentity())
        {
            return true;
        }

        MessageBox.Show(
            "Solo el administrador principal 'admin' puede acceder a la identidad de esta instalacion.",
            "Identidad de estacion",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private async Task EnsureStationConfigurationAsync(InstallationSetupProfile? profile = null)
    {
        profile ??= await _installationManagement.GetSetupProfileAsync();
        if (profile.Kind == InstallationSetupKind.AlreadyConfigured)
        {
            return;
        }

        if (!profile.IsProtectedAdministrator)
        {
            MessageBox.Show(
                "La identidad de esta estacion debe ser configurada por el administrador principal 'admin'.",
                "Configuracion de estacion requerida",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            await LogoutAndReturnToLoginAsync("station-setup-requires-admin");
            return;
        }

        await ShowStationConfigurationAsync(required: true, profile);
    }

    private async Task ShowStationConfigurationAsync(
        bool required,
        InstallationSetupProfile? profile = null)
    {
        if (!EnsureCanManageStationIdentity())
        {
            return;
        }

        profile ??= await _installationManagement.GetSetupProfileAsync();
        if (!profile.IsProtectedAdministrator)
        {
            MessageBox.Show(
                "Solo el administrador principal 'admin' puede cambiar la identidad de esta instalacion.",
                "Identidad de estacion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var form = new StationConfigurationForm(_installationManagement, profile, required);
        var result = form.ShowDialog(this);
        if (result == DialogResult.OK && form.RestartRequired)
        {
            ControlledShutdownState.Begin();
            System.Windows.Forms.Application.Restart();
            Close();
            return;
        }

        if (required)
        {
            await LogoutAndReturnToLoginAsync("station-setup-cancelled");
        }
    }

    private async Task LogoutAndReturnToLoginAsync(string logArea)
    {
        try
        {
            await _authService.LogoutAsync();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log(logArea, ex);
            MessageBox.Show(
                "La sesion se cerro localmente, pero no se pudo registrar el evento en bitacora. " +
                "El detalle tecnico fue guardado en el registro de errores.",
                "Cerrar sesion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            DialogResult = DialogResult.Retry;
            Close();
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
            _clockTimer.Stop();
            _clockTimer.Dispose();
            _creditAlertsMenu.Dispose();
            _creditAlertsToolTip.Dispose();
            _navigationToolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class SidebarNavigationButton : Button
{
    private readonly Font _iconFont = new("Segoe MDL2 Assets", 12f, FontStyle.Regular, GraphicsUnit.Point);

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string IconGlyph { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool CenterIcon { get; set; }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);
        if (string.IsNullOrEmpty(IconGlyph))
        {
            return;
        }

        var iconBounds = CenterIcon
            ? ClientRectangle
            : new Rectangle(LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(32), ClientSize.Height);
        TextRenderer.DrawText(
            pevent.Graphics,
            IconGlyph,
            _iconFont,
            iconBounds,
            ForeColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix);
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
