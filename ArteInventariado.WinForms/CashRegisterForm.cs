using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class CashRegisterForm : Form
{
    private readonly ICashService _cashService;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ICashMovementQueryService _cashMovementQueryService;
    private readonly ICommercialOperationsReportService _commercialReports;
    private readonly CashRegisterMode _mode;
    private readonly IInstallationContext? _installationContext;
    private readonly CashRecoveryCoordinator? _cashRecovery;
    private readonly BindingSource _withdrawalsBinding = new();
    private TableLayoutPanel? _operationsPanel;
    private TableLayoutPanel? _openingActionPanel;
    private TableLayoutPanel? _previousCloseReferencePanel;
    private Control? _closingActionPanel;
    private Control? _outputActionPanel;
    private Control? _cashPositionCard;

    private readonly Label _lblCashPosition = new();
    private readonly Label _lblTheoreticalCashAmount = new();
    private readonly Label _lblTheoreticalCashContext = new();
    private readonly Label _lblPhysicalInputs = new();
    private readonly Label _lblPhysicalOutputs = new();
    private readonly Label _lblElectronic = new();
    private readonly Label _lblDailyCashSummary = new();
    private readonly Label _lblPreviousClose = new();
    private readonly Label _lblMontoCierre = new();
    private readonly NumericUpDown _numMontoInicial = new();
    private readonly NumericUpDown _numMontoCierre = new();
    private readonly NumericUpDown _numRetiro = new();
    private readonly CheckBox _chkUsePreviousClose = new();
    private readonly ComboBox _cmbSalidaTipo = new();
    private readonly TextBox _txtMotivoRetiro = new();
    private readonly Button _btnAbrir = new();
    private readonly Button _btnCerrar = new();
    private readonly Button _btnRegularizarCierre = new();
    private readonly Button _btnRetiro = new();
    private readonly Button _btnRefrescar = new();
    private readonly Button _btnMovimientos = new();
    private readonly Button _btnHistorial = new();
    private readonly Button _btnAporte = new();
    private readonly DataGridView _gridRetiros = new();

    private CashSummary? _summary;
    private CashDailySummary? _todayCashDailySummary;
    private CashOpeningReference? _previousCashOpeningReference;
    private decimal _manualOpeningAmount;
    private bool _updatingOpeningAmount;
    private bool _canOpenCash;
    private bool _canCloseCash;
    private bool _canRegisterExpense;
    private bool _canRegisterWithdrawal;
    private bool _canRegisterCashContribution;
    private bool _canViewCashHistory;
    private bool _canViewExpectedTotal;
    private bool _canViewMovementDetail;
    private bool _actionsEnabled;

    public CashRegisterForm(
        ICashService cashService,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        ICashMovementQueryService cashMovementQueryService,
        ICommercialOperationsReportService commercialReports)
        : this(
            cashService,
            currentSession,
            permissionService,
            cashMovementQueryService,
            commercialReports,
            CashRegisterMode.Admin)
    {
    }

    public CashRegisterForm(
        ICashService cashService,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        ICashMovementQueryService cashMovementQueryService,
        ICommercialOperationsReportService commercialReports,
        IInstallationContext installationContext,
        CashRecoveryCoordinator? cashRecovery = null)
        : this(
            cashService,
            currentSession,
            permissionService,
            cashMovementQueryService,
            commercialReports,
            CashRegisterMode.Admin,
            installationContext,
            cashRecovery)
    {
    }

    public CashRegisterForm(
        ICashService cashService,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        ICashMovementQueryService cashMovementQueryService,
        ICommercialOperationsReportService commercialReports,
        CashRegisterMode mode,
        IInstallationContext? installationContext = null,
        CashRecoveryCoordinator? cashRecovery = null)
    {
        _cashService = cashService;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _cashMovementQueryService = cashMovementQueryService;
        _commercialReports = commercialReports;
        _mode = mode;
        _installationContext = installationContext;
        _cashRecovery = cashRecovery;

        BuildUi();
        Load += CashRegisterForm_Load;
    }

    private async void CashRegisterForm_Load(object? sender, EventArgs e)
    {
        if (!await ApplyPermissionVisibilityAsync())
        {
            Close();
            return;
        }

        if (_installationContext is { AllowsOperationalWrites: false })
        {
            await RefreshAsync();
        }
        else
        {
            await TryAutoOpenAndRefreshAsync();
        }
    }

    private async Task<bool> ApplyPermissionVisibilityAsync()
    {
        if (_mode == CashRegisterMode.Admin)
        {
            _canOpenCash = true;
            _canCloseCash = true;
            _canRegisterExpense = true;
            _canRegisterWithdrawal = true;
            _canRegisterCashContribution = true;
            _canViewCashHistory = true;
            _canViewExpectedTotal = true;
            _canViewMovementDetail = true;
        }
        else
        {
            _canOpenCash = await _permissionService.HasPermissionAsync(PermisoEmpleado.CAJA_ABRIR);
            _canCloseCash = await _permissionService.HasPermissionAsync(PermisoEmpleado.CAJA_CERRAR);
            _canRegisterExpense = await _permissionService.HasPermissionAsync(PermisoEmpleado.GASTO_REGISTRAR);
            _canRegisterWithdrawal = await _permissionService.HasPermissionAsync(PermisoEmpleado.RETIRO_REGISTRAR);
            _canRegisterCashContribution = await _permissionService.HasPermissionAsync(PermisoEmpleado.CAJA_APORTE_REGISTRAR);
            _canViewCashHistory = await _permissionService.HasPermissionAsync(PermisoEmpleado.CAJA_HISTORIAL_VER);
            _canViewExpectedTotal = await _permissionService.HasPermissionAsync(PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER);
            _canViewMovementDetail = await _permissionService.HasPermissionAsync(PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER);

            var canAccess = _canOpenCash || _canCloseCash || _canRegisterExpense ||
                _canRegisterWithdrawal || _canRegisterCashContribution || _canViewExpectedTotal || _canViewMovementDetail ||
                _canViewCashHistory ||
                await _permissionService.HasPermissionAsync(PermisoEmpleado.CAJA_DIFERENCIAS_VER);
            if (!canAccess)
            {
                MessageBox.Show("No tiene permiso para acceder a caja.", "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        if (_cashRecovery is not null &&
            !await _cashRecovery.EnsureReviewedAsync(
                this,
                requireContinuationForOperation: _installationContext?.AllowsOperationalWrites != false))
        {
            return false;
        }

        if (_installationContext is { AllowsOperationalWrites: false })
        {
            _canOpenCash = false;
            _canCloseCash = false;
            _canRegisterExpense = false;
            _canRegisterWithdrawal = false;
            _canRegisterCashContribution = false;
            Text += " - Modo Consulta";
        }

        SetOperationVisibility();
        return true;
    }

    private void SetOperationVisibility()
    {
        if (_openingActionPanel is not null) _openingActionPanel.Visible = _canOpenCash;
        if (_closingActionPanel is not null) _closingActionPanel.Visible = _canCloseCash;

        SetFieldVisibility(_numMontoInicial, _canOpenCash);
        _btnAbrir.Visible = _canOpenCash;
        _numMontoInicial.Enabled = _canOpenCash && !_chkUsePreviousClose.Checked;
        _chkUsePreviousClose.Enabled = _canOpenCash &&
            _previousCashOpeningReference is not null;
        SetClosingActionState();

        var canRegisterOutput = _canRegisterExpense || _canRegisterWithdrawal;
        if (_outputActionPanel is not null) _outputActionPanel.Visible = canRegisterOutput;
        _cmbSalidaTipo.Visible = canRegisterOutput;
        _numRetiro.Visible = canRegisterOutput;
        _txtMotivoRetiro.Visible = canRegisterOutput;
        _btnRetiro.Visible = canRegisterOutput;
        _btnMovimientos.Visible = _canViewMovementDetail;
        _btnHistorial.Visible = _mode == CashRegisterMode.Admin || _canViewCashHistory;
        _lblDailyCashSummary.Visible = _mode == CashRegisterMode.Admin || _canViewCashHistory;
        UpdateSummaryCardLayout();
        _btnAporte.Visible = _canRegisterCashContribution;

        if (_canRegisterExpense && !_canRegisterWithdrawal)
        {
            _cmbSalidaTipo.Items.Clear();
            _cmbSalidaTipo.Items.Add("Gasto");
            _cmbSalidaTipo.SelectedIndex = 0;
        }
        else if (!_canRegisterExpense && _canRegisterWithdrawal)
        {
            _cmbSalidaTipo.Items.Clear();
            _cmbSalidaTipo.Items.Add("Retiro");
            _cmbSalidaTipo.SelectedIndex = 0;
        }

        UpdateOperationsLayout();
    }

    private void BuildUi()
    {
        Text = _mode == CashRegisterMode.Admin ? "Caja diaria" : "Caja diaria - Empleado";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 780);
        MinimumSize = new Size(980, 680);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 205));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildTitleBar(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildOperations(), 0, 2);

        root.Controls.Add(BuildOutputHeader(), 0, 3);
        root.Controls.Add(BuildOutputGrid(), 0, 4);
        Controls.Add(root);
        WindowState = FormWindowState.Maximized;
    }

    private Control BuildOutputHeader()
    {
        var header = new TableLayoutPanel
        {
            Name = "CashOutputHeaderPanel",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Salidas y reembolsos registrados",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelHeader(title);

        _previousCloseReferencePanel = new TableLayoutPanel
        {
            Name = "CashPreviousCloseReferencePanel",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(8, 2, 8, 2),
            BackColor = UITheme.SurfaceMutedColor,
            ColumnCount = 2,
            RowCount = 1,
            Visible = false
        };
        _previousCloseReferencePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        _previousCloseReferencePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _previousCloseReferencePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblPreviousClose.Name = "CashOpeningPreviousCloseLabel";
        _lblPreviousClose.Dock = DockStyle.Fill;
        _lblPreviousClose.Margin = Padding.Empty;
        _lblPreviousClose.TextAlign = ContentAlignment.MiddleLeft;
        _lblPreviousClose.AutoEllipsis = true;
        _lblPreviousClose.AccessibleDescription =
            "Muestra el último conteo físico que se usará como referencia para proponer el efectivo inicial. No reemplaza el conteo físico de apertura.";
        _lblPreviousClose.ForeColor = UITheme.TextDark;
        _lblPreviousClose.BackColor = UITheme.SurfaceMutedColor;

        _chkUsePreviousClose.Name = "CashOpeningUsePreviousCloseCheckBox";
        _chkUsePreviousClose.Visible = true;
        _chkUsePreviousClose.Text = "Usar referencia";
        _chkUsePreviousClose.Dock = DockStyle.Fill;
        _chkUsePreviousClose.Margin = Padding.Empty;
        _chkUsePreviousClose.AutoSize = false;
        _chkUsePreviousClose.TextAlign = ContentAlignment.MiddleLeft;
        _chkUsePreviousClose.BackColor = UITheme.SurfaceMutedColor;
        _chkUsePreviousClose.CheckedChanged += PreviousClose_CheckedChanged;

        _previousCloseReferencePanel.Controls.Add(_lblPreviousClose, 0, 0);
        _previousCloseReferencePanel.Controls.Add(_chkUsePreviousClose, 1, 0);
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_previousCloseReferencePanel, 1, 0);
        return header;
    }

    private Control BuildTitleBar()
    {
        var titleBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label { Text = Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelTitle(title);
        ConfigureButton(_btnHistorial, "Historial", (_, _) => OpenHistory());
        _btnHistorial.Visible = _mode == CashRegisterMode.Admin;
        ConfigureButton(_btnMovimientos, "Movimientos", (_, _) => OpenMovements());
        ConfigureButton(_btnRefrescar, "Refrescar", async (_, _) => await RefreshAsync());
        ConfigureButton(_btnAporte, "Aportar efectivo", BtnAporte_Click);
        UITheme.StyleSecondaryButton(_btnHistorial);
        UITheme.StyleSecondaryButton(_btnMovimientos);
        UITheme.StyleSecondaryButton(_btnRefrescar);
        UITheme.StylePrimaryButton(_btnAporte);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        SizeHeaderButton(_btnRefrescar, 120);
        SizeHeaderButton(_btnMovimientos, 150);
        SizeHeaderButton(_btnHistorial, 130);
        SizeHeaderButton(_btnAporte, 160);
        actions.Controls.AddRange([_btnRefrescar, _btnMovimientos, _btnHistorial, _btnAporte]);
        titleBar.Controls.Add(title, 0, 0);
        titleBar.Controls.Add(actions, 1, 0);
        return titleBar;
    }

    private static void SizeHeaderButton(Button button, int width)
    {
        button.Dock = DockStyle.None;
        button.AutoSize = false;
        button.Size = new Size(width, 42);
        button.Margin = new Padding(4, 3, 0, 3);
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel
        {
            Name = "CashOperationsPanel",
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(0, 6, 0, 8)
        };
        for (var index = 0; index < 5; index++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        var cards = new Control[]
        {
            BuildCashPositionCard(),
            _lblPhysicalInputs,
            _lblPhysicalOutputs,
            _lblElectronic,
            _lblDailyCashSummary
        };
        foreach (var card in cards)
        {
            if (card is Label label)
            {
                ConfigureSummaryCard(label);
            }

            panel.Controls.Add(card);
        }

        _lblDailyCashSummary.Name = "CashDailySummaryCard";
        _lblDailyCashSummary.Visible = false;
        _lblDailyCashSummary.Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Regular);
        _lblDailyCashSummary.AutoEllipsis = true;

        return panel;
    }

    private Control BuildCashPositionCard()
    {
        var card = new TableLayoutPanel
        {
            Name = "CashPositionCard",
            Dock = DockStyle.Fill,
            Margin = new Padding(5),
            Padding = new Padding(13, 8, 9, 7),
            BackColor = UITheme.SurfaceColor,
            ColumnCount = 1,
            RowCount = 4
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Name = "CashPositionTitle",
            Text = "EFECTIVO TEÓRICO HOY",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            BackColor = UITheme.SurfaceColor,
            Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold),
            AutoEllipsis = true
        };
        _lblTheoreticalCashAmount.Name = "CashTheoreticalAmount";
        _lblTheoreticalCashAmount.Dock = DockStyle.Fill;
        _lblTheoreticalCashAmount.Margin = Padding.Empty;
        _lblTheoreticalCashAmount.TextAlign = ContentAlignment.MiddleLeft;
        _lblTheoreticalCashAmount.ForeColor = UITheme.PrimaryColor;
        _lblTheoreticalCashAmount.BackColor = UITheme.SurfaceColor;
        _lblTheoreticalCashAmount.Font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold);
        _lblTheoreticalCashAmount.AutoEllipsis = true;
        _lblTheoreticalCashContext.Name = "CashTheoreticalContext";
        _lblTheoreticalCashContext.Dock = DockStyle.Fill;
        _lblTheoreticalCashContext.Margin = Padding.Empty;
        _lblTheoreticalCashContext.TextAlign = ContentAlignment.MiddleLeft;
        _lblTheoreticalCashContext.ForeColor = UITheme.MutedTextColor;
        _lblTheoreticalCashContext.BackColor = UITheme.SurfaceColor;
        _lblTheoreticalCashContext.Font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Regular);
        _lblTheoreticalCashContext.AutoEllipsis = true;

        _lblCashPosition.Name = "CashPositionDetails";
        _lblCashPosition.Dock = DockStyle.Fill;
        _lblCashPosition.Margin = new Padding(0, 5, 0, 0);
        _lblCashPosition.TextAlign = ContentAlignment.TopLeft;
        _lblCashPosition.ForeColor = UITheme.TextDark;
        _lblCashPosition.BackColor = UITheme.SurfaceColor;
        _lblCashPosition.Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Regular);
        _lblCashPosition.AutoEllipsis = true;

        card.Controls.Add(title, 0, 0);
        card.Controls.Add(_lblTheoreticalCashAmount, 0, 1);
        card.Controls.Add(_lblTheoreticalCashContext, 0, 2);
        card.Controls.Add(_lblCashPosition, 0, 3);
        _cashPositionCard = card;
        return card;
    }

    private Control BuildOperations()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14, 10, 14, 10),
            ColumnCount = 3,
            RowCount = 1
        };
        _operationsPanel = panel;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.Controls.Add(BuildOpenCashAction(), 0, 0);
        _numMontoCierre.Name = "CashClosingAmount";
        _closingActionPanel = BuildCashClosingAction();
        _closingActionPanel.Name = "CashClosingActionPanel";
        panel.Controls.Add(_closingActionPanel, 1, 0);
        _outputActionPanel = BuildOutputAction();
        _outputActionPanel.Name = "CashOutputActionPanel";
        panel.Controls.Add(_outputActionPanel, 2, 0);
        return panel;
    }

    private void UpdateOperationsLayout()
    {
        if (_operationsPanel is null ||
            _openingActionPanel is null ||
            _closingActionPanel is null ||
            _outputActionPanel is null)
        {
            return;
        }

        var actions = new List<(Control Control, float Weight)>();
        if (_canOpenCash) actions.Add((_openingActionPanel, 25f));
        if (_canCloseCash) actions.Add((_closingActionPanel, 25f));
        if (_canRegisterExpense || _canRegisterWithdrawal) actions.Add((_outputActionPanel, 50f));

        var totalWeight = actions.Sum(action => action.Weight);
        _operationsPanel.SuspendLayout();
        try
        {
            _operationsPanel.ColumnStyles.Clear();
            for (var index = 0; index < 3; index++)
            {
                var weight = totalWeight <= 0
                    ? (index == 0 ? 100f : 0f)
                    : index < actions.Count
                        ? actions[index].Weight / totalWeight * 100f
                        : 0f;
                _operationsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weight));
            }

            for (var index = 0; index < actions.Count; index++)
            {
                _operationsPanel.SetColumn(actions[index].Control, index);
                _operationsPanel.SetColumnSpan(actions[index].Control, 1);
            }
        }
        finally
        {
            _operationsPanel.ResumeLayout(performLayout: true);
        }
    }

    private void UpdateSummaryCardLayout()
    {
        if (_cashPositionCard?.Parent is not TableLayoutPanel panel) return;
        panel.ColumnStyles.Clear();
        var visibleCards = _lblDailyCashSummary.Visible ? 5 : 4;
        for (var index = 0; index < visibleCards; index++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / visibleCards));
        if (visibleCards == 4)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 0));
    }

    private Control BuildOpenCashAction()
    {
        _openingActionPanel = new TableLayoutPanel
        {
            Name = "CashOpeningActionPanel",
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Padding = new Padding(8),
            RowCount = 3,
            ColumnCount = 1
        };
        _openingActionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _openingActionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _openingActionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var openingLabel = NewFieldLabel("Monto inicial ($)");
        openingLabel.Margin = Padding.Empty;
        ConfigureMoney(_numMontoInicial);
        _numMontoInicial.Name = "CashOpeningAmount";
        _numMontoInicial.Dock = DockStyle.Fill;
        _numMontoInicial.Margin = Padding.Empty;
        _numMontoInicial.ValueChanged += (_, _) =>
        {
            if (!_updatingOpeningAmount && !_chkUsePreviousClose.Checked)
            {
                _manualOpeningAmount = _numMontoInicial.Value;
            }
        };

        ConfigureButton(_btnAbrir, "Abrir caja", BtnAbrir_Click);
        _btnAbrir.Name = "CashOpenButton";
        UITheme.StylePrimaryButton(_btnAbrir);

        _openingActionPanel.Controls.Add(openingLabel, 0, 0);
        _openingActionPanel.Controls.Add(_numMontoInicial, 0, 1);
        _openingActionPanel.Controls.Add(_btnAbrir, 0, 2);
        return _openingActionPanel;
    }

    private Control BuildCashClosingAction()
    {
        var panel = new TableLayoutPanel
        {
            Name = "CashClosingActionPanel",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            RowCount = 3,
            ColumnCount = 1
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _lblMontoCierre.Text = "Monto cierre ($)";
        _lblMontoCierre.Dock = DockStyle.Fill;
        _lblMontoCierre.Margin = Padding.Empty;
        _lblMontoCierre.TextAlign = ContentAlignment.MiddleLeft;
        UITheme.StyleLabelNormal(_lblMontoCierre);
        panel.Controls.Add(_lblMontoCierre, 0, 0);

        ConfigureMoney(_numMontoCierre);
        _numMontoCierre.Name = "CashClosingAmount";
        _numMontoCierre.Dock = DockStyle.Fill;
        _numMontoCierre.Margin = Padding.Empty;
        panel.Controls.Add(_numMontoCierre, 0, 1);

        ConfigureButton(_btnCerrar, "Cerrar caja", BtnCerrar_Click);
        UITheme.StylePrimaryButton(_btnCerrar);
        ConfigureButton(_btnRegularizarCierre, "Regularizar cierre", BtnRegularizarCierre_Click);
        UITheme.StyleDangerButton(_btnRegularizarCierre);
        _btnRegularizarCierre.Visible = false;
        panel.Controls.Add(_btnCerrar, 0, 2);
        panel.Controls.Add(_btnRegularizarCierre, 0, 2);
        return panel;
    }

    private Control BuildMoneyAction(
        string labelText,
        NumericUpDown amount,
        Button button,
        string buttonText,
        EventHandler handler)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), RowCount = 3, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var label = NewFieldLabel(labelText);
        panel.Controls.Add(label, 0, 0);
        ConfigureMoney(amount);
        amount.Dock = DockStyle.Fill;
        amount.Margin = Padding.Empty;
        ConfigureButton(button, buttonText, handler);
        UITheme.StylePrimaryButton(button);
        panel.Controls.Add(amount, 0, 1);
        panel.Controls.Add(button, 0, 2);
        return panel;
    }

    private Control BuildOutputAction()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), RowCount = 3, ColumnCount = 4 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.Controls.Add(NewFieldLabel("Tipo"), 0, 0);
        panel.Controls.Add(NewFieldLabel("Monto ($)"), 1, 0);
        panel.Controls.Add(NewFieldLabel("Motivo / concepto"), 2, 0);
        panel.SetColumnSpan(panel.GetControlFromPosition(2, 0)!, 2);

        _cmbSalidaTipo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSalidaTipo.Items.AddRange(["Retiro", "Gasto"]);
        _cmbSalidaTipo.SelectedIndex = 0;
        _cmbSalidaTipo.Dock = DockStyle.Fill;
        _cmbSalidaTipo.Margin = Padding.Empty;
        _cmbSalidaTipo.SelectedIndexChanged += (_, _) => UpdateOutputActionEnabled();
        ConfigureMoney(_numRetiro);
        _numRetiro.Name = "CashOutputAmount";
        _numRetiro.Dock = DockStyle.Fill;
        _numRetiro.Margin = Padding.Empty;
        _txtMotivoRetiro.Dock = DockStyle.Fill;
        _txtMotivoRetiro.Margin = Padding.Empty;
        ConfigureButton(_btnRetiro, "Registrar salida", BtnRetiro_Click);
        UITheme.StylePrimaryButton(_btnRetiro);
        panel.Controls.Add(_cmbSalidaTipo, 0, 1);
        panel.Controls.Add(_numRetiro, 1, 1);
        panel.Controls.Add(_txtMotivoRetiro, 2, 1);
        panel.SetColumnSpan(_txtMotivoRetiro, 2);
        panel.Controls.Add(_btnRetiro, 2, 2);
        panel.SetColumnSpan(_btnRetiro, 2);
        return panel;
    }

    private Control BuildOutputGrid()
    {
        _gridRetiros.Dock = DockStyle.Fill;
        _gridRetiros.AllowUserToAddRows = false;
        _gridRetiros.AllowUserToDeleteRows = false;
        _gridRetiros.AllowUserToResizeRows = false;
        _gridRetiros.AutoGenerateColumns = false;
        _gridRetiros.MultiSelect = false;
        _gridRetiros.ReadOnly = true;
        _gridRetiros.RowHeadersVisible = false;
        _gridRetiros.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridRetiros.DataSource = _withdrawalsBinding;
        AddGridColumn("Tipo", "Tipo", 90);
        AddGridColumn("Fecha", "Fecha", 145);
        AddGridColumn("Monto", "Monto", 90);
        AddGridColumn("Motivo", "Motivo / concepto", 330);
        AddGridColumn("OrigenMetodo", "Origen / método / efecto", 300, DataGridViewAutoSizeColumnMode.Fill);
        AddGridColumn("Usuario", "Usuario", 120);
        UITheme.StyleGrid(_gridRetiros);
        return _gridRetiros;
    }

    private async Task TryAutoOpenAndRefreshAsync()
    {
        try
        {
            if (_mode == CashRegisterMode.Admin) await _cashService.OpenCashAutomaticallyIfEnabledAsync();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-auto-open", ex);
            MessageBox.Show($"No se pudo evaluar la apertura automatica:\n\n{ex.Message}", "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            SetActionsEnabled(false);
            var currentSummary = await _cashService.GetTodaySummaryAsync();
            _summary = currentSummary;
            await RefreshPreviousDayCashOpeningReferenceAsync(currentSummary);
            var withdrawals = await _cashService.GetTodayWithdrawalsAsync();
            var expenses = await _cashService.GetTodayExpensesAsync();
            var refunds = await _cashService.GetTodayRefundsAsync();
            IEnumerable<CashOutputGridRow> outputRows = withdrawals.Select(CashOutputGridRow.FromWithdrawal)
                .Concat(expenses.Select(CashOutputGridRow.FromExpense))
                .Concat(refunds.Select(CashOutputGridRow.FromRefund))
                .OrderByDescending(row => row.FechaOrden);
            if (_mode == CashRegisterMode.Employee && _currentSession.UsuarioActual is not null)
            {
                var currentUserId = _currentSession.UsuarioActual.UsuarioId;
                outputRows = outputRows.Where(row => row.UsuarioId == currentUserId);
            }

            _withdrawalsBinding.DataSource = outputRows.ToList();

            CashExpectedBalanceSummary? expectedBalance = null;
            if (_mode == CashRegisterMode.Admin || _canViewCashHistory)
            {
                var sessions = await _cashService.GetSessionHistoryAsync(
                    currentSummary.FechaOperacion,
                    currentSummary.FechaOperacion);
                var daily = CashDailySummaryCalculator.Aggregate(sessions).FirstOrDefault();
                _todayCashDailySummary = daily;
                _lblDailyCashSummary.Text = daily is null
                    ? "RESUMEN ACUMULADO DEL DÍA\nSin sesiones registradas hoy."
                    : FormatDailyCashSummary(daily, _canViewExpectedTotal);
                expectedBalance = daily is null
                    ? new CashExpectedBalanceSummary(currentSummary.FechaOperacion, 0, null, false)
                    : new CashExpectedBalanceSummary(
                        daily.FechaOperacion,
                        daily.CantidadSesiones,
                        daily.EfectivoEsperadoAcumuladoCentavos,
                        daily.TieneCierreIntermedioSinConteo);
            }
            else
            {
                _todayCashDailySummary = null;
            }

            if (_canViewExpectedTotal && expectedBalance is null)
            {
                expectedBalance = await _cashService.GetTodayExpectedCashBalanceAsync();
            }

            RenderTheoreticalCashBalance(expectedBalance, _canViewExpectedTotal);

            RenderSummary(currentSummary);
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-refresh", ex);
            MessageBox.Show(ex.Message, "Caja", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async Task RefreshPreviousDayCashOpeningReferenceAsync(CashSummary summary)
    {
        if (!_canOpenCash || _installationContext is { AllowsOperationalWrites: false })
        {
            UpdatePreviousCashOpeningReference(null);
            return;
        }

        try
        {
            UpdatePreviousCashOpeningReference(await _cashService.GetPreviousDayCashOpeningReferenceAsync());
        }
        catch (Exception ex)
        {
            UpdatePreviousCashOpeningReference(null);
            TechnicalErrorLogger.Log("cash-previous-day-reference", ex);
        }
    }

    internal void UpdatePreviousCashOpeningReference(CashOpeningReference? reference)
    {
        _updatingOpeningAmount = true;
        try
        {
            if (_chkUsePreviousClose.Checked)
            {
                _chkUsePreviousClose.Checked = false;
                _numMontoInicial.Value = ClampToNumeric(_manualOpeningAmount, _numMontoInicial);
            }

            _previousCashOpeningReference = reference;
            var openingReference = _previousCashOpeningReference;
            var visible = openingReference is not null;
            _lblPreviousClose.Text = openingReference is null
                ? string.Empty
                : $"{DescribeCashOpeningReference(openingReference)}: ${FormatDollars(openingReference.MontoCierreCentavos)}";
            if (_previousCloseReferencePanel is not null)
            {
                _previousCloseReferencePanel.Visible = visible;
            }
            _chkUsePreviousClose.AccessibleDescription =
                "Marque solo si confirma que el monto de la referencia coincide con el efectivo disponible físicamente. Si cuenta una cantidad distinta, debe explicar la variación.";
            _numMontoInicial.Enabled = _canOpenCash;
            _chkUsePreviousClose.Enabled = _canOpenCash &&
                visible &&
                _actionsEnabled;
        }
        finally
        {
            _updatingOpeningAmount = false;
        }
    }

    private void PreviousClose_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingOpeningAmount)
        {
            return;
        }

        var reference = _previousCashOpeningReference;
        if (reference is null)
        {
            _updatingOpeningAmount = true;
            _chkUsePreviousClose.Checked = false;
            _updatingOpeningAmount = false;
            return;
        }

        _updatingOpeningAmount = true;
        try
        {
            if (_chkUsePreviousClose.Checked)
            {
                _manualOpeningAmount = _numMontoInicial.Value;
                _numMontoInicial.Value = ClampToNumeric(
                    CentavosToDollars(reference.MontoCierreCentavos),
                    _numMontoInicial);
                _numMontoInicial.Enabled = false;
            }
            else
            {
                _numMontoInicial.Value = ClampToNumeric(_manualOpeningAmount, _numMontoInicial);
                _numMontoInicial.Enabled = _canOpenCash && _actionsEnabled;
            }
        }
        finally
        {
            _updatingOpeningAmount = false;
        }
    }

    private async void BtnAbrir_Click(object? sender, EventArgs e)
    {
        try
        {
            var latestClose = _previousCashOpeningReference;
            if (_chkUsePreviousClose.Checked && latestClose is null)
            {
                throw new InvalidOperationException("Actualice la caja y cuente el efectivo antes de abrir.");
            }

            var openingAmount = DollarsToCentavos(_numMontoInicial.Value);
            string? reason = null;
            if (latestClose is not null &&
                openingAmount == latestClose.MontoCierreCentavos &&
                !_chkUsePreviousClose.Checked)
            {
                var confirmManualCount = MessageBox.Show(
                    this,
                    $"La referencia de apertura ({DescribeCashOpeningReference(latestClose)}) es ${FormatDollars(latestClose.MontoCierreCentavos)}. " +
                    "Ingresaste la misma cantidad, pero no marcaste «Usar referencia».\r\n\r\n" +
                    "¿Contaste físicamente el efectivo ahora?\r\n\r\n" +
                    "Sí: se registrará como un conteo manual.\r\n" +
                    "No: vuelve a revisar el efectivo antes de abrir.",
                    "Confirmar conteo de apertura",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (confirmManualCount != DialogResult.Yes)
                {
                    return;
                }
            }

            if (latestClose is not null && openingAmount != latestClose.MontoCierreCentavos)
            {
                if (_chkUsePreviousClose.Checked)
                {
                    throw new InvalidOperationException("Quite la opción de usar la referencia sin cambio y registre el motivo del nuevo conteo.");
                }

                using var difference = new CashOpeningDifferenceForm(latestClose, openingAmount);
                if (difference.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                reason = difference.Reason;
            }

            await _cashService.OpenCashAsync(new OpenCashRequest(
                openingAmount,
                CajaCierreAnteriorId: latestClose?.CajaId,
                ConfirmoEfectivoIntacto: _chkUsePreviousClose.Checked,
                MotivoVariacionCierreAnterior: reason));
            MessageBox.Show("Caja abierta.", "Caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-open", ex);
            MessageBox.Show(ex.Message, "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void BtnCerrar_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Cerrar la caja del dia?", "Caja", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            var countedCash = DollarsToCentavos(_numMontoCierre.Value);
            string? incidentReason = null;
            var currentCash = await _cashService.GetTodaySummaryAsync();
            if (_currentSession.EsAdmin &&
                currentCash.Estado == EstadoCaja.ABIERTA &&
                countedCash != currentCash.TotalEsperadoCentavos)
            {
                var difference = countedCash - currentCash.TotalEsperadoCentavos;
                var addIncidentNote = MessageBox.Show(
                    this,
                    $"Efectivo esperado: ${FormatDollars(currentCash.TotalEsperadoCentavos)}\r\n" +
                    $"Efectivo contado: ${FormatDollars(countedCash)}\r\n" +
                    $"Diferencia: ${FormatDollars(difference)}\r\n\r\n" +
                    "¿Desea dejar una nota administrativa por un incidente imprevisto?\r\n" +
                    "La nota solo se guardará en la bitácora: no crea ventas, gastos ni movimientos, y no cambia los totales o reportes financieros.",
                    "Incidente imprevisto de caja",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (addIncidentNote == DialogResult.Yes)
                {
                    using var incident = new CashIncidentNoteForm(currentCash.TotalEsperadoCentavos, countedCash);
                    if (incident.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    incidentReason = incident.Reason;
                }
            }

            await _cashService.CloseCashAsync(new CloseCashRequest(
                countedCash,
                MotivoImprevisto: incidentReason));
            MessageBox.Show("Caja cerrada.", "Caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-close", ex);
            MessageBox.Show(ex.Message, "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void BtnRetiro_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_cmbSalidaTipo.SelectedItem?.ToString() == "Gasto")
            {
                var categories = await _cashService.GetExpenseCategoriesAsync();
                using var expense = new ExpenseCaptureForm(categories, _txtMotivoRetiro.Text);
                if (expense.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                await _cashService.RegisterExpenseAsync(new RegisterExpenseRequest(
                    DollarsToCentavos(_numRetiro.Value),
                    expense.Concept,
                    expense.Notes,
                    expense.CategoryId,
                    expense.PaymentMethod,
                    expense.Reference,
                    expense.FundingOrigin,
                    expense.FundingResponsible,
                    expense.OperationId));
            }
            else
            {
                await _cashService.RegisterWithdrawalAsync(new RegisterWithdrawalRequest(DollarsToCentavos(_numRetiro.Value), _txtMotivoRetiro.Text));
            }
            MessageBox.Show("Salida registrada.", "Caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _numRetiro.Value = 0;
            _txtMotivoRetiro.Clear();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-output", ex);
            MessageBox.Show(ex.Message, "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RenderSummary(CashSummary summary)
    {
        var expected = _canViewExpectedTotal
            ? $"Esperado de sesión: ${FormatDollars(summary.TotalEsperadoCentavos)}"
            : "Esperado de sesión: restringido";
        var counted = summary.MontoCierreCentavos.HasValue
            ? $"Último efectivo contado: ${FormatDollars(summary.MontoCierreCentavos.Value)}"
            : "Efectivo contado: pendiente";
        var difference = summary.DiferenciaCentavos.HasValue
            ? $"Diferencia de sesión: ${FormatDollars(summary.DiferenciaCentavos.Value)}"
            : "Diferencia de sesión: pendiente";
        _lblCashPosition.Text = $"Estado: {summary.Estado?.ToString() ?? "SIN CAJA"}\n" +
                                $"Apertura sesión: ${FormatDollars(summary.MontoInicialCentavos)}\n" +
                                $"{expected}\n{counted}\n{difference}";
        _lblPhysicalInputs.Text = $"ENTRADAS FISICAS\n\nVentas: ${FormatDollars(summary.VentasEfectivoCentavos)}\nAbonos clientes: ${FormatDollars(summary.AbonosClientesEfectivoCentavos)}\nAnticipos pedidos: ${FormatDollars(summary.AnticiposPedidosEfectivoCentavos)}\nAbonos pedidos: ${FormatDollars(summary.AbonosPedidosEfectivoCentavos)}\nPagos finales: ${FormatDollars(summary.PagosFinalesPedidosEfectivoCentavos)}\nAportes para cambio: ${FormatDollars(summary.TotalAportesCajaCentavos)}";
        _lblPhysicalOutputs.Text = $"SALIDAS FISICAS\n\nReembolsos ventas: ${FormatDollars(summary.ReembolsosVentasEfectivoCentavos)}\nReembolsos pedidos: ${FormatDollars(summary.ReembolsosPedidosEfectivoCentavos)}\nPagos compras: ${FormatDollars(summary.PagosComprasEfectivoCentavos)}\nAbonos proveedores: ${FormatDollars(summary.AbonosProveedoresEfectivoCentavos)}\nPagos empleados: ${FormatDollars(summary.TotalPagosEmpleadosCentavos)}\nGastos / retiros: ${FormatDollars(summary.TotalGastosCentavos)} / ${FormatDollars(summary.TotalRetirosCentavos)}";
        _lblElectronic.Text = $"METODOS NO FISICOS\n\nTransferencias recibidas: ${FormatDollars(summary.TransferenciasRecibidasCentavos)}\nBitcoin recibido: ${FormatDollars(summary.BitcoinRecibidoCentavos)}\nTransferencias pagadas: ${FormatDollars(summary.TransferenciasPagadasCentavos)}\nBitcoin pagado: ${FormatDollars(summary.BitcoinPagadoCentavos)}";
        if (summary.Estado == EstadoCaja.ABIERTA)
            _numMontoCierre.Value = ClampToNumeric(CentavosToDollars(summary.TotalEsperadoCentavos), _numMontoCierre);
    }

    private void SetActionsEnabled(bool enabled)
    {
        _actionsEnabled = enabled;
        var cashOpen = _summary?.Estado == EstadoCaja.ABIERTA;
        _btnAbrir.Enabled = enabled && _canOpenCash && !cashOpen;
        _numMontoInicial.Enabled = enabled && _canOpenCash && !_chkUsePreviousClose.Checked;
        _chkUsePreviousClose.Enabled = enabled &&
            _canOpenCash &&
            _previousCashOpeningReference is not null &&
            !cashOpen;
        _btnCerrar.Enabled = enabled && _canCloseCash && cashOpen;
        SetClosingActionState();
        _btnRegularizarCierre.Enabled = enabled && _canCloseCash && _currentSession.EsAdmin &&
            _installationContext?.AllowsOperationalWrites != false &&
            !cashOpen && _todayCashDailySummary is { CantidadSesiones: > 0, TieneCierreIntermedioSinConteo: false };
        _cmbSalidaTipo.Enabled = enabled;
        _numRetiro.Enabled = enabled;
        _txtMotivoRetiro.Enabled = enabled;
        UpdateOutputActionEnabled();
        _btnRefrescar.Enabled = enabled;
        _btnMovimientos.Enabled = enabled && _canViewMovementDetail;
        _btnAporte.Enabled = enabled &&
            _canRegisterCashContribution &&
            cashOpen &&
            _installationContext?.AllowsOperationalWrites != false;
    }

    private void UpdateOutputActionEnabled()
    {
        var selectedIsExpense = _cmbSalidaTipo.SelectedItem?.ToString() == "Gasto";
        var canRegister = selectedIsExpense
            ? _canRegisterExpense
            : _canRegisterWithdrawal && _summary?.Estado == EstadoCaja.ABIERTA;
        _btnRetiro.Enabled = _actionsEnabled && canRegister;
    }

    private void OpenMovements()
    {
        if (!_canViewMovementDetail) return;
        using var form = new CashMovementsDetailForm(_cashMovementQueryService, _commercialReports, _permissionService);
        form.ShowDialog(this);
    }

    private void SetClosingActionState()
    {
        var cashOpen = _summary?.Estado == EstadoCaja.ABIERTA;
        var hasClosedDay = !cashOpen && _summary?.CajaId.HasValue == true &&
            _todayCashDailySummary is { CantidadSesiones: > 0, TieneCierreIntermedioSinConteo: false };
        _lblMontoCierre.Text = cashOpen ? "Monto cierre ($)" : "Último cierre contado ($)";
        SetFieldVisibility(_numMontoCierre, _canCloseCash && (cashOpen || hasClosedDay));
        _btnCerrar.Visible = _canCloseCash && cashOpen;
        _btnRegularizarCierre.Visible = _mode == CashRegisterMode.Admin && _canCloseCash && hasClosedDay;
        _numMontoCierre.Enabled = _actionsEnabled && cashOpen && _canCloseCash;
        if (hasClosedDay && _todayCashDailySummary?.UltimoCierreContadoCentavos is long lastClose)
        {
            _numMontoCierre.Value = ClampToNumeric(CentavosToDollars(lastClose), _numMontoCierre);
            _numMontoCierre.ReadOnly = true;
        }
        else
        {
            _numMontoCierre.ReadOnly = false;
        }
    }

    private async void BtnRegularizarCierre_Click(object? sender, EventArgs e)
    {
        if (!_currentSession.EsAdmin || !_canCloseCash ||
            _installationContext?.AllowsOperationalWrites == false ||
            _summary?.Estado != EstadoCaja.CERRADA)
        {
            return;
        }

        try
        {
            var date = _summary.FechaOperacion;
            var sessions = await _cashService.GetSessionHistoryAsync(date, date);
            var daily = CashDailySummaryCalculator.Aggregate(sessions).FirstOrDefault();
            if (daily is null || daily.TieneCierreIntermedioSinConteo || !daily.UltimoCierreContadoCentavos.HasValue)
            {
                MessageBox.Show(this, "No se puede regularizar: falta un cierre intermedio o un conteo final.",
                    "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var form = new CashDayRegularizationForm(daily);
            if (form.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await _cashService.RegularizeClosedCashDayAsync(new RegularizeClosedCashDayRequest(
                date,
                form.CountedCashCentavos,
                form.Reason,
                form.Password,
                Guid.NewGuid()));
            MessageBox.Show(this, "El cierre consolidado del día quedó regularizado y auditado.",
                "Caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-closed-day-regularize", ex);
            MessageBox.Show(this, ex.Message, "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenHistory()
    {
        if (_mode != CashRegisterMode.Admin && !_canViewCashHistory) return;
        using var form = new CashHistoryForm(
            _cashService,
            showAdminCashSummary: _mode == CashRegisterMode.Admin);
        form.ShowDialog(this);
    }

    private void RenderTheoreticalCashBalance(CashExpectedBalanceSummary? summary, bool canView)
    {
        if (!canView)
        {
            _lblTheoreticalCashAmount.Text = "Restringido";
            _lblTheoreticalCashContext.Text = "No disponible para este usuario";
            return;
        }

        if (summary is null || summary.CantidadSesiones == 0)
        {
            _lblTheoreticalCashAmount.Text = "—";
            _lblTheoreticalCashContext.Text = "Sin sesiones registradas hoy";
            return;
        }

        if (summary.TieneCierreIntermedioSinConteo || !summary.EfectivoEsperadoCentavos.HasValue)
        {
            _lblTheoreticalCashAmount.Text = "No calculable";
            _lblTheoreticalCashContext.Text = "Falta un conteo intermedio";
            return;
        }

        _lblTheoreticalCashAmount.Text = $"${FormatDollars(summary.EfectivoEsperadoCentavos.Value)}";
        _lblTheoreticalCashContext.Text =
            $"Saldo teórico acumulado · {summary.CantidadSesiones} sesión(es)";
    }

    private static string FormatDailyCashSummary(CashDailySummary summary, bool canViewExpectedTotal)
    {
        var adjustments = summary.AjustesEntreSesionesCentavos.HasValue
            ? $"${FormatDollars(summary.AjustesEntreSesionesCentavos.Value)}"
            : "No calculable";
        var expected = canViewExpectedTotal && summary.EfectivoEsperadoAcumuladoCentavos.HasValue
            ? $"${FormatDollars(summary.EfectivoEsperadoAcumuladoCentavos.Value)}"
            : canViewExpectedTotal ? "No calculable" : "Restringido";
        var sessionBalancesSum = summary.SumaSaldosPorSesionCentavos.HasValue
            ? $"${FormatDollars(summary.SumaSaldosPorSesionCentavos.Value)} (referencia)"
            : "Pendiente";
        var finalCount = summary.UltimoCierreContadoCentavos.HasValue
            ? $"${FormatDollars(summary.UltimoCierreContadoCentavos.Value)}"
            : "Pendiente";
        var finalDifference = summary.DiferenciaFinalCentavos.HasValue
            ? $"${FormatDollars(summary.DiferenciaFinalCentavos.Value)}"
            : "Pendiente";
        return $"RESUMEN ACUMULADO DEL DÍA\nSesiones: {summary.CantidadSesiones} · Apertura inicial: ${FormatDollars(summary.AperturaInicialCentavos)}\n" +
               $"Entradas físicas: ${FormatDollars(summary.EntradasFisicasCentavos)}\n" +
               $"Salidas físicas: ${FormatDollars(summary.SalidasFisicasCentavos)}\n" +
               $"Ajustes de reapertura: {adjustments}\n" +
               $"Esperado acumulado: {expected}\n" +
               $"Suma de saldos por sesión: {sessionBalancesSum}\n" +
               $"Último efectivo físico contado: {finalCount}\nDiferencia final: {finalDifference}" +
               (summary.TieneCierreIntermedioSinConteo ? "\nFalta un conteo intermedio." : string.Empty);
    }

    private async void BtnAporte_Click(object? sender, EventArgs e)
    {
        if (!_canRegisterCashContribution || _summary?.Estado != EstadoCaja.ABIERTA)
        {
            return;
        }

        using var form = new CashContributionForm();
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetActionsEnabled(false);
        try
        {
            await _cashService.RegisterCashContributionAsync(new RegisterCashContributionRequest(
                DollarsToCentavos(form.Amount),
                form.Reason));
            MessageBox.Show(this, "Aporte de efectivo registrado en la caja.", "Caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-contribution", ex);
            MessageBox.Show(this, ex.Message, "Caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            await RefreshAsync();
        }
    }

    private static void SetFieldVisibility(Control control, bool visible)
    {
        control.Visible = visible;
        if (control.Parent is TableLayoutPanel panel)
        {
            var position = panel.GetPositionFromControl(control);
            var label = panel.GetControlFromPosition(position.Column, Math.Max(0, position.Row - 1));
            if (label is not null) label.Visible = visible;
        }
    }

    private static Label NewFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void ConfigureSummaryCard(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(5);
        label.Padding = new Padding(14, 10, 10, 8);
        label.BackColor = UITheme.SurfaceColor;
        label.ForeColor = UITheme.TextDark;
        label.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Regular);
    }

    private static void ConfigureMoney(NumericUpDown control)
    {
        control.Minimum = 0;
        control.Maximum = 9_999_999.99m;
        control.DecimalPlaces = 2;
        control.ThousandsSeparator = true;
        control.Increment = 0.25m;
    }

    private static void ConfigureButton(Button button, string text, EventHandler clickHandler)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 2, 6, 2);
        button.Click += clickHandler;
    }

    private void AddGridColumn(string propertyName, string headerText, int width, DataGridViewAutoSizeColumnMode autoSize = DataGridViewAutoSizeColumnMode.None)
    {
        _gridRetiros.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            AutoSizeMode = autoSize
        });
    }

    private static decimal ClampToNumeric(decimal value, NumericUpDown control) =>
        Math.Min(control.Maximum, Math.Max(control.Minimum, value));

    private static string FormatDollars(long centavos) => (centavos / 100m).ToString("0.00");

    private static string DescribeCashOpeningReference(CashOpeningReference reference) =>
        $"Último conteo · {reference.FechaOperacion:dd/MM}";
    private static decimal CentavosToDollars(long centavos) => centavos / 100m;

    private static long DollarsToCentavos(decimal dollars)
    {
        if (dollars < 0) throw new ArgumentException("El monto no puede ser negativo.", nameof(dollars));
        return decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private sealed record CashOutputGridRow(
        string Tipo,
        DateTime FechaOrden,
        string Fecha,
        int UsuarioId,
        string Monto,
        string Motivo,
        string OrigenMetodo,
        string Usuario)
    {
        public static CashOutputGridRow FromWithdrawal(CashWithdrawalSummary item) =>
            new("Retiro", item.FechaRetiro, item.FechaRetiro.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), item.UsuarioId,
                FormatDollars(item.MontoCentavos), item.Motivo, "Caja física | efectivo | afecta caja", item.Usuario);

        public static CashOutputGridRow FromExpense(CashExpenseSummary item)
        {
            var fundingOrigin = item.OrigenFondos switch
            {
                OrigenFondos.CAJA_FISICA => "Caja física",
                OrigenFondos.FONDOS_EXTERNOS_DUENO => "Fondos externos del dueño",
                OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA => "Fondos del negocio fuera de caja",
                _ => "No especificado (histórico)"
            };
            var method = item.MetodoPago?.ToString() ?? "Método no especificado";
            var effect = item.AfectaCaja ? "sí afecta caja" : "no afecta caja";
            var responsible = string.IsNullOrWhiteSpace(item.ResponsableOrigen) ? string.Empty : $" | Fuente: {item.ResponsableOrigen}";
            return new("Gasto", item.FechaGasto, item.FechaGasto.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), item.UsuarioId,
                FormatDollars(item.MontoCentavos), item.Concepto, $"{fundingOrigin} | {method} | {effect}{responsible}", item.Usuario);
        }

        public static CashOutputGridRow FromRefund(CashRefundSummary item) =>
            new("Reembolso", item.FechaReembolso, item.FechaReembolso.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), item.UsuarioId,
                FormatDollars(item.MontoCentavos), $"{item.Motivo} ({item.MetodoReembolso})", item.CajaId.HasValue ? "Caja física | afecta caja" : "Fuera de caja", item.Usuario);
    }
}
