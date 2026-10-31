using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class AccountsReceivableForm : Form
{
    private static readonly Color CardPendingBackColor = Color.FromArgb(255, 225, 238);
    private static readonly Color CardCurrentBackColor = Color.FromArgb(255, 246, 210);
    private static readonly Color CardOverdueBackColor = Color.FromArgb(255, 224, 224);
    private static readonly Color CardMorosoBackColor = Color.FromArgb(245, 196, 205);
    private static readonly Color PaidBackColor = Color.FromArgb(219, 244, 230);
    private static readonly Color PaidForeColor = Color.FromArgb(30, 108, 74);
    private static readonly Color CancelledBackColor = Color.FromArgb(235, 235, 238);
    private static readonly Color CancelledForeColor = Color.FromArgb(84, 84, 92);
    private static readonly Color OverdueForeColor = Color.FromArgb(155, 40, 55);
    private static readonly Color MorosoForeColor = Color.FromArgb(112, 31, 50);

    private readonly ICreditService _creditService;
    private readonly IAccountPaymentService _accountPaymentService;
    private readonly IAccountPaymentQueryService _accountPaymentQueryService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ICustomerQueryService _customerQueryService;
    private readonly ICustomerService _customerService;
    private readonly ISalesQueryService _salesQueryService;
    private readonly ICurrentSession _currentSession;
    private readonly IServiceProvider _serviceProvider;

    private readonly BindingSource _binding = new();
    private readonly ToolTip _toolTip = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbSituation = new();
    private readonly CheckBox _chkIncludeClosed = new();
    private readonly Button _btnClearSearch = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnDetail = new();
    private readonly Button _btnRegisterPayment = new();
    private readonly Button _btnPaymentsHistory = new();
    private readonly Button _btnOpenAlerts = new();
    private readonly Button _btnOpenCustomer = new();
    private readonly Button _btnOpenSale = new();
    private readonly Button _btnClose = new();
    private readonly DataGridView _grid = new();
    private readonly Panel _gridHost = new();
    private readonly Panel _emptyPanel = new();
    private readonly Label _lblFooter = new();
    private readonly Label _lblDetail = new();

    private readonly Label _lblPendingAmount = new();
    private readonly Label _lblOpenAccounts = new();
    private readonly Label _lblOverdueAccounts = new();
    private readonly Label _lblMorosoAccounts = new();

    private IReadOnlyList<AccountRow> _currentRows = [];
    private bool _canOpenCustomer;
    private bool _canOpenSale;
    private bool _canRegisterPayment;
    private bool _canViewAlerts;
    private LiveSearchController? _liveSearch;
    private bool _suppressFilterRefresh;

    public AccountsReceivableForm(
        ICreditService creditService,
        IAccountPaymentService accountPaymentService,
        IAccountPaymentQueryService accountPaymentQueryService,
        IEmployeePermissionService permissionService,
        ICustomerQueryService customerQueryService,
        ICustomerService customerService,
        ISalesQueryService salesQueryService,
        ICurrentSession currentSession,
        IServiceProvider serviceProvider)
    {
        _creditService = creditService;
        _accountPaymentService = accountPaymentService;
        _accountPaymentQueryService = accountPaymentQueryService;
        _permissionService = permissionService;
        _customerQueryService = customerQueryService;
        _customerService = customerService;
        _salesQueryService = salesQueryService;
        _currentSession = currentSession;
        _serviceProvider = serviceProvider;

        BuildUi();
        _liveSearch = new LiveSearchController(
            _txtSearch,
            _btnClearSearch,
            RefreshAsync,
            ShowSearchError);
        Load += AccountsReceivableForm_Load;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void AccountsReceivableForm_Load(object? sender, EventArgs e)
    {
        try
        {
            _canOpenCustomer = await _permissionService.HasPermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER);
            _canOpenSale = await _permissionService.HasPermissionAsync(PermisoEmpleado.HISTORIAL_VENTAS_VER);
            _canRegisterPayment = await _permissionService.HasPermissionAsync(PermisoEmpleado.ABONO_REGISTRAR);
            _canViewAlerts = await _permissionService.HasPermissionAsync(PermisoEmpleado.ALERTAS_CREDITO_VER);
            _btnOpenCustomer.Visible = _canOpenCustomer;
            _btnOpenSale.Visible = _canOpenSale;
            _btnRegisterPayment.Visible = _canRegisterPayment;
            _btnOpenAlerts.Visible = _canViewAlerts;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cuentas por cobrar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Cuentas por cobrar";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1280, 740);
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 700);
        UITheme.StyleForm(this);
        BackColor = Color.FromArgb(253, 245, 247);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20, 14, 20, 14),
            BackColor = Color.FromArgb(253, 245, 247)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSummaryCards(), 0, 1);
        root.Controls.Add(BuildFilters(), 0, 2);
        root.Controls.Add(BuildGridHost(), 0, 3);
        root.Controls.Add(BuildSelectionDetail(), 0, 4);
        root.Controls.Add(BuildFooter(), 0, 5);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 60),
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6),
            BackColor = Color.Transparent
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconBox = new Panel
        {
            Size = new Size(46, 46),
            Anchor = AnchorStyles.Left | AnchorStyles.None,
            BackColor = Color.FromArgb(247, 221, 230),
            Margin = new Padding(0, 0, 8, 0)
        };
        var iconLabel = new Label
        {
            Text = "\uE8C7",
            Font = new Font("Segoe MDL2 Assets", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(107, 9, 59),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        iconBox.Controls.Add(iconLabel);

        var textPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        textPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Cuentas por cobrar",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(107, 9, 59),
            Margin = new Padding(0, 0, 0, 3)
        };

        var subtitle = new Label
        {
            Text = "Consulte y supervise créditos, saldos y vencimientos de clientes.",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(115, 115, 122),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Margin = Padding.Empty
        };

        textPanel.Controls.Add(title, 0, 0);
        textPanel.Controls.Add(subtitle, 0, 1);

        panel.Controls.Add(iconBox, 0, 0);
        panel.Controls.Add(textPanel, 1, 0);
        return panel;
    }

    private Control BuildSummaryCards()
    {
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 104),
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent
        };
        for (var i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        cards.Controls.Add(BuildSummaryCard("Saldo pendiente", _lblPendingAmount, CardPendingBackColor, Color.FromArgb(122, 12, 62), "$", Color.FromArgb(245, 194, 214)), 0, 0);
        cards.Controls.Add(BuildSummaryCard("Cuentas abiertas", _lblOpenAccounts, CardCurrentBackColor, Color.FromArgb(107, 82, 0), "●", Color.FromArgb(240, 225, 176)), 1, 0);
        cards.Controls.Add(BuildSummaryCard("Vencidas", _lblOverdueAccounts, CardOverdueBackColor, Color.FromArgb(158, 27, 42), "!", Color.FromArgb(245, 181, 181)), 2, 0);
        cards.Controls.Add(BuildSummaryCard("Morosas", _lblMorosoAccounts, CardMorosoBackColor, Color.FromArgb(143, 29, 56), "▲", Color.FromArgb(247, 181, 194)), 3, 0);
        return cards;
    }

    private Control BuildSummaryCard(string title, Label value, Color backColor, Color foreColor, string icon, Color borderColor)
    {
        var cardHost = new Panel
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 104),
            BackColor = backColor,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(12, 8, 12, 8)
        };
        cardHost.Paint += (s, e) =>
        {
            using var pen = new Pen(borderColor, 1);
            var rect = cardHost.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleWrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };

        var titleFlow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };

        var iconLabel = new Label
        {
            Text = icon,
            Size = new Size(24, 24),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Symbol", 11, FontStyle.Bold),
            ForeColor = foreColor,
            BackColor = Color.FromArgb(65, Color.White),
            Margin = new Padding(0, 0, 6, 0)
        };

        var lblTitle = new Label
        {
            Text = title,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = foreColor,
            Margin = new Padding(0, 2, 0, 0)
        };

        titleFlow.Controls.Add(iconLabel);
        titleFlow.Controls.Add(lblTitle);
        titleWrapper.Controls.Add(titleFlow, 0, 0);

        value.Text = "-";
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleCenter;
        value.Font = new Font("Segoe UI", 22, FontStyle.Bold);
        value.ForeColor = foreColor;
        value.AutoEllipsis = false;
        value.Margin = Padding.Empty;
        value.MinimumSize = new Size(0, 46);

        panel.Controls.Add(titleWrapper, 0, 0);
        panel.Controls.Add(value, 0, 1);

        cardHost.Controls.Add(panel);
        return cardHost;
    }

    private Control BuildFilters()
    {
        var cardHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 76),
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(12, 6, 12, 6)
        };
        cardHost.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(229, 203, 213), 1);
            var rect = cardHost.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "Buscar por cuenta, venta o cliente";
        UITheme.StyleInput(_txtSearch);
        ConfigureClearSearchButton(_btnClearSearch);

        _cmbSituation.Dock = DockStyle.Fill;
        _cmbSituation.DropDownStyle = ComboBoxStyle.DropDownList;
        UITheme.StyleInput(_cmbSituation);
        _cmbSituation.Items.Clear();
        _cmbSituation.Items.AddRange([
            new SituationFilterItem("Todas", AccountSituationFilter.All),
            new SituationFilterItem("Saldo vigente", AccountSituationFilter.Current),
            new SituationFilterItem("Vencida", AccountSituationFilter.Overdue),
            new SituationFilterItem("Morosa", AccountSituationFilter.Moroso),
            new SituationFilterItem("Pagada", AccountSituationFilter.Paid),
            new SituationFilterItem("Anulada", AccountSituationFilter.Cancelled)
        ]);
        _cmbSituation.SelectedIndex = 0;
        _cmbSituation.SelectedIndexChanged += FilterChanged;

        _chkIncludeClosed.Text = "Incluir cerradas";
        _chkIncludeClosed.AutoSize = true;
        _chkIncludeClosed.Anchor = AnchorStyles.Left;
        _chkIncludeClosed.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _chkIncludeClosed.TextAlign = ContentAlignment.MiddleLeft;
        _chkIncludeClosed.CheckedChanged += FilterChanged;

        _btnClear.Text = "Limpiar";
        _btnClear.Dock = DockStyle.Fill;
        _btnClear.Click += async (_, _) => await ClearFiltersAsync();
        StyleWineOutlineButton(_btnClear);
        UITheme.EnsureButtonContentFits(_btnClear, 100, 36, 26);

        var chkHost = BuildFilterActionHost("Opciones", _chkIncludeClosed);
        var btnHost = BuildFilterActionHost("Acciones", _btnClear);

        panel.Controls.Add(BuildField("Buscar por cuenta, venta o cliente", BuildSearchInput()), 0, 0);
        panel.Controls.Add(BuildField("Situación", _cmbSituation), 1, 0);
        panel.Controls.Add(chkHost, 2, 0);
        panel.Controls.Add(btnHost, 3, 0);

        cardHost.Controls.Add(panel);
        return cardHost;
    }

    private Control BuildGridHost()
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(1)
        };
        container.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(229, 203, 213), 1);
            var rect = container.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
        };

        _gridHost.Dock = DockStyle.Fill;
        _gridHost.BackColor = UITheme.SurfaceColor;
        _gridHost.Margin = Padding.Empty;
        _gridHost.Padding = Padding.Empty;

        ConfigureGrid();
        _emptyPanel.Dock = DockStyle.Fill;
        _emptyPanel.BackColor = UITheme.SurfaceColor;
        _emptyPanel.Visible = false;

        var emptyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var emptyTitle = new Label
        {
            Text = "No hay cuentas por cobrar registradas.",
            Dock = DockStyle.Bottom,
            Height = 34,
            TextAlign = ContentAlignment.BottomCenter,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark
        };
        var emptySubtitle = new Label
        {
            Text = "Las ventas a crédito aparecerán aquí.",
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.TopCenter,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor
        };

        emptyLayout.Controls.Add(emptyTitle, 0, 0);
        emptyLayout.Controls.Add(emptySubtitle, 0, 1);
        _emptyPanel.Controls.Add(emptyLayout);
        _gridHost.Controls.Add(_grid);
        _gridHost.Controls.Add(_emptyPanel);
        container.Controls.Add(_gridHost);
        return container;
    }

    private Control BuildSelectionDetail()
    {
        var cardHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(14, 8, 14, 8)
        };
        cardHost.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(229, 203, 213), 1);
            var rect = cardHost.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
        };

        var rootTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        rootTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        rootTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
        rootTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var detailContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        detailContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        detailContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconBox = new Panel
        {
            Size = new Size(42, 42),
            Anchor = AnchorStyles.Left | AnchorStyles.None,
            BackColor = Color.FromArgb(255, 230, 240),
            Margin = new Padding(0, 0, 6, 0)
        };
        var iconLabel = new Label
        {
            Text = "\uE8A1",
            Font = new Font("Segoe MDL2 Assets", 15, FontStyle.Bold),
            ForeColor = Color.FromArgb(107, 9, 59),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        iconBox.Controls.Add(iconLabel);

        _lblDetail.Text = "Seleccione una cuenta para ver el resumen.";
        _lblDetail.Dock = DockStyle.Fill;
        _lblDetail.TextAlign = ContentAlignment.MiddleLeft;
        _lblDetail.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        _lblDetail.ForeColor = UITheme.TextDark;
        _lblDetail.BackColor = Color.Transparent;
        _lblDetail.Padding = new Padding(4, 0, 4, 0);

        detailContainer.Controls.Add(iconBox, 0, 0);
        detailContainer.Controls.Add(_lblDetail, 1, 0);

        var separator = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 220, 225),
            Margin = new Padding(7, 4, 8, 4),
            Width = 1
        };

        var buttonMatrix = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        buttonMatrix.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttonMatrix.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var topButtonsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 2)
        };

        var bottomButtonsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 2)
        };

        ConfigureActionButton(_btnOpenSale, "Ver venta", 116, async (_, _) => await OpenSelectedSaleAsync());
        ConfigureActionButton(_btnOpenCustomer, "Abrir cliente", 132, async (_, _) => await OpenSelectedCustomerAsync());
        ConfigureActionButton(_btnOpenAlerts, "Ver alertas", 116, (_, _) => OpenCreditAlerts());
        ConfigureActionButton(_btnPaymentsHistory, "Historial", 110, async (_, _) => await OpenPaymentsHistoryAsync());

        StyleWineOutlineButton(_btnOpenSale);
        StyleWineOutlineButton(_btnOpenCustomer);
        StyleWineOutlineButton(_btnOpenAlerts);
        StyleWineOutlineButton(_btnPaymentsHistory);

        topButtonsRow.Controls.AddRange([_btnOpenSale, _btnOpenCustomer, _btnOpenAlerts, _btnPaymentsHistory]);

        ConfigureActionButton(_btnClose, "Cerrar", 106, (_, _) => Close());
        ConfigureActionButton(_btnRegisterPayment, "Registrar", 120, async (_, _) => await OpenRegisterPaymentAsync());
        ConfigureActionButton(_btnDetail, "Ver detalle", 126, async (_, _) => await OpenDetailAsync());

        StyleNeutralOutlineButton(_btnClose);
        StyleGreenButton(_btnRegisterPayment);
        StyleWineFilledButton(_btnDetail);

        bottomButtonsRow.Controls.AddRange([_btnClose, _btnRegisterPayment, _btnDetail]);

        buttonMatrix.Controls.Add(topButtonsRow, 0, 0);
        buttonMatrix.Controls.Add(bottomButtonsRow, 0, 1);

        rootTable.Controls.Add(detailContainer, 0, 0);
        rootTable.Controls.Add(separator, 1, 0);
        rootTable.Controls.Add(buttonMatrix, 2, 0);

        cardHost.Controls.Add(rootTable);
        return cardHost;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(2, 2, 2, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lblFooter.Text = "0 cuentas mostradas";
        _lblFooter.Dock = DockStyle.Fill;
        _lblFooter.TextAlign = ContentAlignment.MiddleLeft;
        _lblFooter.ForeColor = UITheme.MutedTextColor;
        _lblFooter.Font = UITheme.NormalBoldFont;

        _btnClose.Visible = true;
        footer.Controls.Add(_lblFooter, 0, 0);
        return footer;
    }

    private Control BuildField(string labelText, Control control)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 56),
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = Color.Transparent
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 42, 58),
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        control.Dock = DockStyle.Top;
        control.MinimumSize = new Size(control.MinimumSize.Width, 34);
        wrapper.Controls.Add(label, 0, 0);
        wrapper.Controls.Add(control, 0, 1);
        return wrapper;
    }

    private static Control BuildFilterActionHost(string labelText, Control control)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 56),
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = Color.Transparent
        };
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        host.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 42, 58),
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);

        control.MinimumSize = new Size(control.MinimumSize.Width, 34);
        if (control is CheckBox)
        {
            control.Anchor = AnchorStyles.Left;
            control.Margin = new Padding(2, 6, 0, 0);
        }
        else
        {
            control.Dock = DockStyle.Top;
            control.Margin = Padding.Empty;
        }

        host.Controls.Add(control, 0, 1);
        return host;
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.ScrollBars = ScrollBars.Vertical;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ColumnHeadersHeight = 36;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(92, 6, 50);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _grid.EnableHeadersVisualStyles = false;
        _grid.RowTemplate.Height = 34;
        _grid.DataSource = _binding;
        _grid.CellDoubleClick += async (_, _) => await OpenDetailAsync();
        _grid.SelectionChanged += (_, _) => UpdateSelectedDetail();
        _grid.DataBindingComplete += (_, _) => ApplyRowStyles();
        _grid.CellFormatting += Grid_CellFormatting;

        _grid.Columns.Clear();
        AddColumn("Codigo", "Cuenta", 112, 16);
        AddColumn("Venta", "Venta", 112, 16);
        AddColumn("Cliente", "Cliente", 150, 24);
        AddColumn("Fecha", "Fecha", 82, 9, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Capital", "Capital", 68, 8, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Interes", "Interes", 68, 8, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Total", "Total", 68, 8, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Pagado", "Pagado", 68, 8, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Saldo", "Saldo", 68, 8, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Vencimiento", "Vencimiento", 88, 10, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("SituacionTexto", "Situación", 112, 13, DataGridViewContentAlignment.MiddleCenter);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var selectedFilter = CurrentFilter();
            var forceClosed = selectedFilter is AccountSituationFilter.Paid or AccountSituationFilter.Cancelled;
            var accounts = await _creditService.GetAccountsAsync(new AccountReceivableFilter(
                Estado: PersistentStateForFilter(selectedFilter),
                Search: _txtSearch.Text,
                IncludeClosed: _chkIncludeClosed.Checked || forceClosed), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _currentRows = accounts
                .Select(AccountRow.FromSummary)
                .Where(row => MatchesSituation(row, selectedFilter))
                .ToList();

            _binding.DataSource = _currentRows;
            _emptyPanel.Visible = _currentRows.Count == 0;
            if (_emptyPanel.Visible)
            {
                _emptyPanel.BringToFront();
            }

            UpdateSummaryCards();
            UpdateFooter();
            UpdateSelectedDetail();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cuentas por cobrar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ClearFiltersAsync()
    {
        _suppressFilterRefresh = true;
        try
        {
            _txtSearch.Clear();
            _cmbSituation.SelectedIndex = 0;
            _chkIncludeClosed.Checked = false;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        if (_liveSearch is not null)
        {
            await _liveSearch.SearchNowAsync();
        }
        else
        {
            await RefreshAsync();
        }
    }

    private async void FilterChanged(object? sender, EventArgs e)
    {
        if (!_suppressFilterRefresh && _liveSearch is not null)
        {
            await _liveSearch.SearchNowAsync();
        }
    }

    private Control BuildSearchInput()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 34),
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        host.Controls.Add(_txtSearch, 0, 0);
        host.Controls.Add(_btnClearSearch, 1, 0);
        return host;
    }

    private static void ConfigureClearSearchButton(Button button)
    {
        button.Text = "X";
        button.AccessibleName = "Limpiar búsqueda";
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 0, 0, 0);
        UITheme.StyleSecondaryButton(button);
    }

    private void ShowSearchError(Exception ex)
    {
        if (!IsDisposed)
        {
            _lblFooter.ForeColor = UITheme.DangerColor;
            _lblFooter.Text = $"No se pudo actualizar la búsqueda: {ex.Message}";
        }
    }

    private async Task OpenDetailAsync()
    {
        if (_binding.Current is not AccountRow row)
        {
            return;
        }

        using var form = new AccountReceivableDetailForm(
            row.CuentaPorCobrarId,
            _creditService,
            _accountPaymentQueryService,
            _accountPaymentService,
            _permissionService);
        form.ShowDialog(this);
        await TryRefreshVisualAsync();
    }

    private async Task OpenRegisterPaymentAsync()
    {
        if (!_canRegisterPayment || _binding.Current is not AccountRow row || !CanRegisterPayment(row))
        {
            return;
        }

        await OpenRegisterPaymentLoopAsync(row.CuentaPorCobrarId);
    }

    private async Task OpenRegisterPaymentLoopAsync(int cuentaPorCobrarId)
    {
        var keepRegistering = true;
        while (keepRegistering)
        {
            keepRegistering = false;
            using var form = new AccountPaymentForm(
                cuentaPorCobrarId,
                _creditService,
                _accountPaymentService,
                _accountPaymentQueryService,
                _permissionService);
            var result = form.ShowDialog(this);
            if (result is DialogResult.OK or DialogResult.Retry)
            {
                await TryRefreshAfterConfirmedPaymentAsync();
            }

            if (result == DialogResult.Retry && await CanRegisterPaymentAsync(cuentaPorCobrarId))
            {
                keepRegistering = true;
            }
        }
    }

    private async Task OpenPaymentsHistoryAsync()
    {
        var filter = _binding.Current is AccountRow row
            ? new AccountPaymentHistoryFilter(CuentaPorCobrarId: row.CuentaPorCobrarId)
            : new AccountPaymentHistoryFilter();

        using var form = new AccountPaymentsHistoryForm(
            _accountPaymentQueryService,
            _permissionService,
            filter);
        form.ShowDialog(this);
        await TryRefreshVisualAsync();
    }

    private async Task OpenSelectedCustomerAsync()
    {
        if (!_canOpenCustomer || _binding.Current is not AccountRow row)
        {
            return;
        }

        using var form = new CustomerProfileForm(
            row.ClienteId,
            _customerQueryService,
            _customerService,
            _creditService,
            _permissionService,
            _currentSession,
            _serviceProvider,
            _accountPaymentService,
            _accountPaymentQueryService);
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task OpenSelectedSaleAsync()
    {
        if (!_canOpenSale || _binding.Current is not AccountRow row)
        {
            return;
        }

        var detail = await _salesQueryService.GetSaleDetailAsync(row.VentaId);
        if (detail is null)
        {
            MessageBox.Show("La venta seleccionada ya no existe.", "Cuentas por cobrar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshAsync();
            return;
        }

        using var receipt = new SaleReceiptForm(
            SaleReceiptViewModel.FromSalesHistoryDetail(detail),
            isCompletedSale: false,
            primaryButtonText: "Cerrar");
        receipt.ShowDialog(this);
    }

    private void OpenCreditAlerts()
    {
        if (!_canViewAlerts)
        {
            return;
        }

        using var form = (CreditAlertsForm)_serviceProvider.GetService(typeof(CreditAlertsForm))!;
        if (_binding.Current is AccountRow row)
        {
            form.SetInitialFilter(new CreditAlertFilter(Search: row.Codigo));
        }

        form.ShowDialog(this);
    }

    private void UpdateSummaryCards()
    {
        var pending = _currentRows
            .Where(row => row.Estado == EstadoCuentaPorCobrar.ABIERTA)
            .Sum(row => row.SaldoCentavos);
        _lblPendingAmount.Text = SaleReceiptViewModel.FormatDollars(pending);
        _lblOpenAccounts.Text = _currentRows.Count(row => row.Estado == EstadoCuentaPorCobrar.ABIERTA).ToString();
        _lblOverdueAccounts.Text = _currentRows.Count(row => row.Situacion == SituacionCrediticia.VENCIDO).ToString();
        _lblMorosoAccounts.Text = _currentRows.Count(row => row.Situacion == SituacionCrediticia.MOROSO).ToString();
    }

    private void UpdateFooter()
    {
        var count = _currentRows.Count;
        var filteredBalance = _currentRows.Sum(row => row.SaldoCentavos);
        var plural = count == 1 ? "cuenta mostrada" : "cuentas mostradas";
        _lblFooter.Text = $"{count} {plural} - Saldo filtrado: {SaleReceiptViewModel.FormatDollars(filteredBalance)}";
    }

    private void UpdateSelectedDetail()
    {
        if (_binding.Current is not AccountRow row)
        {
            _btnDetail.Enabled = false;
            _btnOpenCustomer.Enabled = false;
            _btnOpenSale.Enabled = false;
            _btnOpenAlerts.Enabled = _canViewAlerts;
            _btnRegisterPayment.Enabled = false;
            _btnPaymentsHistory.Enabled = false;
            _lblDetail.Text = "Seleccione una cuenta para ver el resumen.";
            return;
        }

        _btnDetail.Enabled = true;
        _btnOpenCustomer.Enabled = _canOpenCustomer;
        _btnOpenSale.Enabled = _canOpenSale;
        _btnOpenAlerts.Enabled = _canViewAlerts;
        _btnRegisterPayment.Enabled = _canRegisterPayment && CanRegisterPayment(row);
        _btnPaymentsHistory.Enabled = true;

        _lblDetail.Text =
            $"Cuenta {row.Codigo} · Cliente: {row.Cliente} · Venta: {row.Venta}\n" +
            $"Capital {row.Capital} · Interés {row.Interes} · Total {row.Total} · Pagado {row.Pagado} · Saldo {row.Saldo}\n" +
            $"Vencimiento {row.Vencimiento} · {row.SituacionTexto} · {BuildDaysText(row)}";
        _toolTip.SetToolTip(_lblDetail, _lblDetail.Text);
    }

    private void ApplyRowStyles()
    {
        foreach (DataGridViewRow gridRow in _grid.Rows)
        {
            if (gridRow.DataBoundItem is not AccountRow row)
            {
                continue;
            }

            var (backColor, foreColor) = ColorsForRow(row);
            gridRow.DefaultCellStyle.BackColor = backColor;
            gridRow.DefaultCellStyle.ForeColor = foreColor;
            gridRow.DefaultCellStyle.SelectionBackColor = UITheme.SoftPinkColor;
            gridRow.DefaultCellStyle.SelectionForeColor = UITheme.TextDark;
            foreach (DataGridViewCell cell in gridRow.Cells)
            {
                cell.ToolTipText = $"{row.Codigo} - {row.Cliente} - {row.SituacionTexto}";
            }
        }
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not AccountRow row)
        {
            return;
        }

        var column = _grid.Columns[e.ColumnIndex];
        if (column.DataPropertyName is "Codigo" or "Venta" or "Cliente" or "SituacionTexto")
        {
            _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = e.Value?.ToString();
        }

        if (column.DataPropertyName == "SituacionTexto")
        {
            var (backColor, foreColor) = ColorsForSituation(row);
            e.CellStyle.BackColor = backColor;
            e.CellStyle.ForeColor = foreColor;
            e.CellStyle.SelectionBackColor = UITheme.SoftPinkColor;
            e.CellStyle.SelectionForeColor = UITheme.TextDark;
            e.CellStyle.Font = UITheme.NormalBoldFont;
        }
    }

    private static bool MatchesSituation(AccountRow row, AccountSituationFilter filter)
    {
        return filter switch
        {
            AccountSituationFilter.Current => row.Estado == EstadoCuentaPorCobrar.ABIERTA && row.Situacion == SituacionCrediticia.SALDO_VIGENTE,
            AccountSituationFilter.Overdue => row.Estado == EstadoCuentaPorCobrar.ABIERTA && row.Situacion == SituacionCrediticia.VENCIDO,
            AccountSituationFilter.Moroso => row.Estado == EstadoCuentaPorCobrar.ABIERTA && row.Situacion == SituacionCrediticia.MOROSO,
            AccountSituationFilter.Paid => row.Estado == EstadoCuentaPorCobrar.PAGADA,
            AccountSituationFilter.Cancelled => row.Estado == EstadoCuentaPorCobrar.ANULADA,
            _ => true
        };
    }

    private AccountSituationFilter CurrentFilter()
    {
        return _cmbSituation.SelectedItem is SituationFilterItem item
            ? item.Filter
            : AccountSituationFilter.All;
    }

    private static EstadoCuentaPorCobrar? PersistentStateForFilter(AccountSituationFilter filter)
    {
        return filter switch
        {
            AccountSituationFilter.Paid => EstadoCuentaPorCobrar.PAGADA,
            AccountSituationFilter.Cancelled => EstadoCuentaPorCobrar.ANULADA,
            _ => null
        };
    }

    private static (Color BackColor, Color ForeColor) ColorsForRow(AccountRow row)
    {
        if (row.Estado == EstadoCuentaPorCobrar.PAGADA)
        {
            return (Color.FromArgb(247, 253, 249), UITheme.TextDark);
        }

        if (row.Estado == EstadoCuentaPorCobrar.ANULADA)
        {
            return (Color.FromArgb(250, 250, 251), UITheme.TextDark);
        }

        return row.Situacion switch
        {
            SituacionCrediticia.SALDO_VIGENTE => (Color.FromArgb(255, 252, 238), UITheme.TextDark),
            SituacionCrediticia.VENCIDO => (Color.FromArgb(255, 245, 245), UITheme.TextDark),
            SituacionCrediticia.MOROSO => (Color.FromArgb(255, 237, 241), UITheme.TextDark),
            _ => (UITheme.SurfaceColor, UITheme.TextDark)
        };
    }

    private static (Color BackColor, Color ForeColor) ColorsForSituation(AccountRow row)
    {
        if (row.Estado == EstadoCuentaPorCobrar.PAGADA)
        {
            return (PaidBackColor, PaidForeColor);
        }

        if (row.Estado == EstadoCuentaPorCobrar.ANULADA)
        {
            return (CancelledBackColor, CancelledForeColor);
        }

        return row.Situacion switch
        {
            SituacionCrediticia.SALDO_VIGENTE => (CardCurrentBackColor, UITheme.TextDark),
            SituacionCrediticia.VENCIDO => (CardOverdueBackColor, OverdueForeColor),
            SituacionCrediticia.MOROSO => (CardMorosoBackColor, MorosoForeColor),
            _ => (UITheme.SurfaceColor, UITheme.TextDark)
        };
    }

    private static string BuildDaysText(AccountRow row)
    {
        if (row.Estado is EstadoCuentaPorCobrar.PAGADA or EstadoCuentaPorCobrar.ANULADA)
        {
            return "Cuenta cerrada";
        }

        var today = DateTime.Today;
        var due = SaleReceiptViewModel.ToLocal(row.FechaVencimientoValue).Date;
        var days = (due - today).Days;
        return days switch
        {
            > 0 => $"Vence en {days} día(s)",
            0 => "Vence hoy",
            _ => $"{Math.Abs(days)} día(s) de atraso"
        };
    }

    private static bool CanRegisterPayment(AccountRow row)
    {
        return row.Estado == EstadoCuentaPorCobrar.ABIERTA && row.SaldoCentavos > 0;
    }

    private async Task<bool> CanRegisterPaymentAsync(int cuentaPorCobrarId)
    {
        var detail = await _creditService.GetAccountDetailAsync(cuentaPorCobrarId);
        return detail is not null &&
            detail.Summary.Estado == EstadoCuentaPorCobrar.ABIERTA &&
            detail.Summary.SaldoCentavos > 0 &&
            _canRegisterPayment;
    }

    private async Task TryRefreshAfterConfirmedPaymentAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
            MessageBox.Show(
                "El abono fue registrado, pero no se pudo actualizar una pantalla. Vuelva a abrir el módulo.",
                "Cuentas por cobrar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private async Task TryRefreshVisualAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LogTechnicalError(ex);
            MessageBox.Show(
                "No se pudo actualizar la pantalla. Vuelva a abrir el módulo.",
                "Cuentas por cobrar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static void LogTechnicalError(Exception ex)
    {
        try
        {
            var logDirectory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "account-payments-ui.log"),
                $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Could not write account payment UI log.");
        }
    }

    private void AddColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private static void ConfigureActionButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 38;
        button.Margin = new Padding(6, 0, 0, 0);
        button.Click += handler;
        UITheme.EnsureButtonContentFits(button, width, 38, 24);
    }

    private static void StyleWineOutlineButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(107, 9, 59);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(253, 238, 243);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(247, 215, 226);
        button.BackColor = Color.White;
        button.ForeColor = Color.FromArgb(107, 9, 59);
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleWineFilledButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(203, 43, 116);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(150, 20, 80);
        button.BackColor = Color.FromArgb(184, 37, 95);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleGreenButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 136, 56);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(24, 100, 40);
        button.BackColor = Color.FromArgb(40, 167, 69);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleNeutralOutlineButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(107, 9, 59);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 245, 245);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(230, 230, 230);
        button.BackColor = Color.White;
        button.ForeColor = Color.FromArgb(107, 9, 59);
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private enum AccountSituationFilter
    {
        All,
        Current,
        Overdue,
        Moroso,
        Paid,
        Cancelled
    }

    private sealed record SituationFilterItem(string Text, AccountSituationFilter Filter)
    {
        public override string ToString() => Text;
    }

    private sealed record AccountRow(
        int CuentaPorCobrarId,
        string Codigo,
        int VentaId,
        string Venta,
        int ClienteId,
        string Cliente,
        string Fecha,
        string Capital,
        string Interes,
        string Total,
        string Pagado,
        string Saldo,
        long SaldoCentavos,
        string Vencimiento,
        DateTime FechaVencimientoValue,
        EstadoCuentaPorCobrar Estado,
        SituacionCrediticia Situacion,
        string SituacionTexto)
    {
        public static AccountRow FromSummary(AccountReceivableSummary summary)
        {
            return new AccountRow(
                summary.CuentaPorCobrarId,
                summary.CodigoCuenta,
                summary.VentaId,
                summary.NumeroVenta,
                summary.ClienteId,
                summary.Cliente,
                SaleReceiptViewModel.ToLocal(summary.FechaCreacion).ToString("yyyy-MM-dd"),
                SaleReceiptViewModel.FormatDollars(summary.CapitalCentavos),
                SaleReceiptViewModel.FormatDollars(summary.InteresCentavos),
                SaleReceiptViewModel.FormatDollars(summary.TotalOriginalCentavos),
                SaleReceiptViewModel.FormatDollars(summary.TotalPagadoCentavos),
                SaleReceiptViewModel.FormatDollars(summary.SaldoCentavos),
                summary.SaldoCentavos,
                SaleReceiptViewModel.ToLocal(summary.FechaVencimiento).ToString("yyyy-MM-dd"),
                summary.FechaVencimiento,
                summary.Estado,
                summary.Situacion,
                FriendlySituation(summary.Estado, summary.Situacion));
        }

        private static string FriendlySituation(EstadoCuentaPorCobrar estado, SituacionCrediticia situacion)
        {
            return estado switch
            {
                EstadoCuentaPorCobrar.PAGADA => "Pagada",
                EstadoCuentaPorCobrar.ANULADA => "Anulada",
                _ => situacion switch
                {
                    SituacionCrediticia.SALDO_VIGENTE => "Saldo vigente",
                    SituacionCrediticia.VENCIDO => "Vencida",
                    SituacionCrediticia.MOROSO => "Morosa",
                    _ => "Al día"
                }
            };
        }
    }
}
