using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;
using ArteInventariado.WinForms.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace ArteInventariado.WinForms;

public partial class EmployeePOSForm : Form
{
    private const int ProductsPageSize = 60;
    private readonly IProductService _productService;
    private readonly IPosCatalogService _posCatalogService;
    private readonly IBarcodeService _barcodeService;
    private readonly IInventoryService _inventoryService;
    private readonly IInstallationContext _installation;
    private readonly ICategoryService _categoryService;
    private readonly ISubcategoryService? _subcategoryService;
    private readonly ISaleService _saleService;
    private readonly ICashService _cashService;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ICreditService _creditService;
    private readonly ISystemConfigService _systemConfigService;
    private readonly IProductAssetService? _productAssetService;
    private readonly ICustomerQueryService? _customerQueryService;
    private readonly IReceiptDocumentService? _receiptDocuments;
    private readonly IPeripheralOutputCoordinator? _peripheralOutput;
    private readonly CashRecoveryCoordinator? _cashRecovery;
    private readonly PosPresentationMode _presentationMode;
    private readonly SaleDraftCoordinator _draftCoordinator = new();
    private readonly BindingSource _cartBinding = new();

    private readonly TextBox _txtSearch = new();
    private readonly TextBox _txtBarcode = new();
    private readonly Label _lblScannerStatus = new();
    private readonly ComboBox _cmbCategoryFilter = new();
    private readonly ComboBox _cmbSubcategoryFilter = new();
    private readonly ComboBox _cmbStockFilter = new();
    private readonly ComboBox _cmbLocationFilter = new();
    private readonly ComboBox _cmbSort = new();
    private readonly FlowLayoutPanel _productsPanel = new();
    private readonly DataGridView _gridCart = new();
    private readonly NumericUpDown _numQuantity = new();
    private readonly NumericUpDown _numLineDiscount = new();
    private readonly TextBox _txtLineDiscountReason = new();
    private readonly NumericUpDown _numGeneralDiscount = new();
    private readonly TextBox _txtGeneralDiscountReason = new();
    private readonly NumericUpDown _numCashReceived = new();
    private readonly NumericUpDown _numPaymentCashAmount = new();
    private readonly NumericUpDown _numTransferAmount = new();
    private readonly TextBox _txtTransferReference = new();
    private readonly NumericUpDown _numBitcoinAmount = new();
    private readonly TextBox _txtBitcoinReference = new();
    private readonly ComboBox _cmbQuickLineReason = new();
    private readonly ComboBox _cmbQuickGeneralReason = new();
    private readonly Label _lblCashStatus = new();
    private readonly Label _lblCustomer = new();
    private readonly Label _lblSelectedLine = new();
    private readonly Label _lblSubtotal = new();
    private readonly Label _lblDiscounts = new();
    private readonly Label _lblPaid = new();
    private readonly Label _lblMissing = new();
    private readonly Label _lblTotal = new();
    private readonly Label _lblChange = new();
    private readonly Button _btnSearch = new();
    private readonly Button _btnClearSearch = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnUpdateLine = new();
    private readonly Button _btnRemove = new();
    private readonly Button _btnRegister = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnPreviousPage = new();
    private readonly Button _btnNextPage = new();
    private readonly Button _btnDiscounts = new();
    private readonly Button _btnOtherMethods = new();
    private readonly Label _lblQuickCashChange = new();
    private readonly Label _lblPaymentMethodStatus = new();
    private readonly Label _lblPageInfo = new();
    private readonly Label _lblResultsInfo = new();
    private readonly FlowLayoutPanel _draftTabs = new();
    private readonly SemaphoreSlim _productsRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _scannerQueue = new(1, 1);

    private readonly Panel _pnlPaymentOverlay = new();
    private readonly Panel _pnlPaymentModal = new();
    private readonly FlowLayoutPanel _pnlPaymentMethodButtons = new();
    private readonly Panel _pnlPaymentMethodContent = new();
    private readonly Label _lblOverlayTotalToPay = new();
    private readonly Label _lblOverlayPaid = new();
    private readonly Label _lblOverlayMissing = new();
    private readonly Label _lblOverlayChange = new();
    private readonly Button _btnAcceptPayment = new();
    private readonly Button _btnCancelPayment = new();
    private readonly ToolTip _shortcutToolTip = new();

    private List<PosSkuSaleItem> _allProducts = [];
    private List<PosSkuSaleItem> _products = [];
    private List<SaleDraftLine> _cart = [];
    private LiveSearchController? _liveSearch;
    private string _lastRenderedProductsKey = string.Empty;
    private int _currentProductsPage = 1;
    private bool _cashOpen;
    private bool _loadingFilterOptions;
    private string _currentPaymentMethod = "EFECTIVO";
    private long _acceptedCash;
    private long _acceptedTransfer;
    private string _acceptedTransferRef = string.Empty;
    private long _acceptedBitcoin;
    private string _acceptedBitcoinRef = string.Empty;
    private bool _paymentValidated;
    private bool _canRegisterSale;
    private bool _canApplyLineDiscount;
    private bool _canApplyGeneralDiscount;
    private bool _canUseTransfer;
    private bool _canUseBitcoin;
    private bool _canRegisterCredit;
    private bool _canTransferInventory;
    private bool _saleProcessing;
    private bool _settingCashReceived;
    private bool _cashReceivedEditedManually;
    private PosOverlayPanel _activeOverlayPanel = PosOverlayPanel.None;
    private bool _overlayActionProcessing;
    private Button? _activeOverlayApplyButton;
    private Button? _activeOverlayCancelButton;
    private Control? _activeOverlayPrimaryControl;
    private int? _selectedCustomerId;
    private string _selectedCustomerName = "Consumidor final";
    private CreditSaleRequest? _pendingCreditRequest;
    private bool _creditDialogOpen;
    private bool _cartExitConfirmed;
    private bool _cartExitPromptVisible;
    private bool _restoringDraftState;

    public EmployeePOSForm(
        IProductService productService,
        IPosCatalogService posCatalogService,
        IBarcodeService barcodeService,
        IInventoryService inventoryService,
        IInstallationContext installation,
        ICategoryService categoryService,
        ISaleService saleService,
        ICashService cashService,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        ICreditService creditService,
        ISystemConfigService systemConfigService,
        CashRecoveryCoordinator? cashRecovery = null,
        bool loadDataOnShow = true,
        IProductAssetService? productAssetService = null,
        PosPresentationMode presentationMode = PosPresentationMode.Traditional,
        ICustomerQueryService? customerQueryService = null,
        IReceiptDocumentService? receiptDocuments = null,
        IPeripheralOutputCoordinator? peripheralOutput = null,
        ISubcategoryService? subcategoryService = null)
    {
        _productService = productService;
        _posCatalogService = posCatalogService;
        _barcodeService = barcodeService;
        _inventoryService = inventoryService;
        _installation = installation;
        _categoryService = categoryService;
        _subcategoryService = subcategoryService;
        _saleService = saleService;
        _cashService = cashService;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _creditService = creditService;
        _systemConfigService = systemConfigService;
        _cashRecovery = cashRecovery;
        _productAssetService = productAssetService;
        _customerQueryService = customerQueryService;
        _receiptDocuments = receiptDocuments;
        _peripheralOutput = peripheralOutput;
        _presentationMode = presentationMode;
        LoadActiveDraftFields();

        if (_presentationMode == PosPresentationMode.Quick)
        {
            BuildQuickUi();
        }
        else
        {
            BuildTraditionalUi();
        }
        _cmbCategoryFilter.SelectedIndexChanged += CategoryFilterChanged;
        ApplyActiveDraftToControls();
        RefreshDraftTabs();
        _liveSearch = new LiveSearchController(
            _txtSearch,
            _btnClearSearch,
            cancellationToken => ApplyProductFilterAsync(resetPage: true, cancellationToken: cancellationToken),
            ex => MessageBox.Show(ex.Message, "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning),
            handleEnter: false);
        if (loadDataOnShow)
        {
            Load += EmployeePOSForm_Load;
        }
    }

    private void LoadActiveDraftFields()
    {
        var draft = _draftCoordinator.ActiveDraft;
        _cart = draft.Lines;
        _selectedCustomerId = draft.SelectedCustomerId;
        _selectedCustomerName = draft.SelectedCustomerName;
        _currentPaymentMethod = draft.Checkout.CurrentPaymentMethod;
        _acceptedCash = draft.Checkout.AcceptedCashCentavos;
        _acceptedTransfer = draft.Checkout.AcceptedTransferCentavos;
        _acceptedTransferRef = draft.Checkout.AcceptedTransferReference;
        _acceptedBitcoin = draft.Checkout.AcceptedBitcoinCentavos;
        _acceptedBitcoinRef = draft.Checkout.AcceptedBitcoinReference;
        _pendingCreditRequest = draft.Checkout.PendingCreditRequest;
        _cashReceivedEditedManually = draft.Checkout.CashReceivedEditedManually;
    }

    private void SaveActiveDraftFields()
    {
        var draft = _draftCoordinator.ActiveDraft;
        draft.SelectedCustomerId = _selectedCustomerId;
        draft.SelectedCustomerName = _selectedCustomerName;
        draft.Checkout.CurrentPaymentMethod = _currentPaymentMethod;
        draft.Checkout.GeneralDiscountCentavos = GetMoneyCentavos(_numGeneralDiscount);
        draft.Checkout.GeneralDiscountReason = _txtGeneralDiscountReason.Text;
        draft.Checkout.CashReceivedCentavos = GetMoneyCentavos(_numCashReceived);
        draft.Checkout.AcceptedCashCentavos = _acceptedCash;
        draft.Checkout.AcceptedTransferCentavos = _acceptedTransfer;
        draft.Checkout.AcceptedTransferReference = _acceptedTransferRef;
        draft.Checkout.AcceptedBitcoinCentavos = _acceptedBitcoin;
        draft.Checkout.AcceptedBitcoinReference = _acceptedBitcoinRef;
        draft.Checkout.CashReceivedEditedManually = _cashReceivedEditedManually;
        draft.Checkout.PendingCreditRequest = _pendingCreditRequest;
    }

    private void ApplyActiveDraftToControls()
    {
        _restoringDraftState = true;
        try
        {
            var checkout = _draftCoordinator.ActiveDraft.Checkout;
            _numGeneralDiscount.Value = Math.Clamp(
                CentavosToDollars(checkout.GeneralDiscountCentavos),
                _numGeneralDiscount.Minimum,
                _numGeneralDiscount.Maximum);
            _txtGeneralDiscountReason.Text = checkout.GeneralDiscountReason ?? string.Empty;
            _numCashReceived.Value = Math.Clamp(
                CentavosToDollars(checkout.CashReceivedCentavos),
                _numCashReceived.Minimum,
                _numCashReceived.Maximum);
            _numPaymentCashAmount.Value = Math.Clamp(
                CentavosToDollars(checkout.AcceptedCashCentavos),
                _numPaymentCashAmount.Minimum,
                _numPaymentCashAmount.Maximum);
            _numTransferAmount.Value = Math.Clamp(
                CentavosToDollars(checkout.AcceptedTransferCentavos),
                _numTransferAmount.Minimum,
                _numTransferAmount.Maximum);
            _txtTransferReference.Text = checkout.AcceptedTransferReference;
            _numBitcoinAmount.Value = Math.Clamp(
                CentavosToDollars(checkout.AcceptedBitcoinCentavos),
                _numBitcoinAmount.Minimum,
                _numBitcoinAmount.Maximum);
            _txtBitcoinReference.Text = checkout.AcceptedBitcoinReference;
        }
        finally
        {
            _restoringDraftState = false;
        }

        UpdateCustomerLabel();
        UpdatePaymentReferenceStates();
        ResetLineEditorControls();
        RefreshCart();
        LoadSelectedCartLineControls();
    }

    private void ResetLineEditorControls()
    {
        _numQuantity.Value = Math.Clamp(1, _numQuantity.Minimum, _numQuantity.Maximum);
        _numLineDiscount.Value = 0;
        _txtLineDiscountReason.Clear();
        _cmbQuickLineReason.SelectedIndex = -1;
        _lblSelectedLine.Text = "Seleccione un producto de la venta";
    }

    private Control BuildDraftTabs()
    {
        _draftTabs.Dock = DockStyle.Fill;
        _draftTabs.AutoScroll = true;
        _draftTabs.WrapContents = false;
        _draftTabs.FlowDirection = FlowDirection.LeftToRight;
        _draftTabs.Padding = new Padding(0, 2, 0, 2);
        _draftTabs.Margin = Padding.Empty;
        _draftTabs.BackColor = UITheme.BackgroundColor;
        return _draftTabs;
    }

    private void RefreshDraftTabs()
    {
        if (_draftTabs.IsDisposed)
        {
            return;
        }

        _draftTabs.SuspendLayout();
        try
        {
            foreach (var child in _draftTabs.Controls.Cast<Control>().ToList())
            {
                child.Dispose();
            }

            _draftTabs.Controls.Clear();
            foreach (var draft in _draftCoordinator.Drafts)
            {
                var tab = new Panel
                {
                    Width = 126,
                    Height = 36,
                    Margin = new Padding(0, 0, 6, 0),
                    BackColor = UITheme.SurfaceColor,
                    Tag = draft.Id
                };
                var select = new Button
                {
                    Text = draft.Title,
                    Location = new Point(0, 0),
                    Size = new Size(96, 36),
                    Tag = draft.Id,
                    AccessibleName = $"Activar {draft.Title}"
                };
                if (ReferenceEquals(draft, _draftCoordinator.ActiveDraft))
                {
                    UITheme.StylePrimaryButton(select);
                }
                else
                {
                    UITheme.StyleSecondaryButton(select);
                }

                select.Click += (_, _) => ActivateDraftFromUi(draft.Id);
                var close = new Button
                {
                    Text = "×",
                    Location = new Point(98, 0),
                    Size = new Size(28, 36),
                    Tag = draft.Id,
                    AccessibleName = $"Cerrar {draft.Title}"
                };
                UITheme.StyleDangerButton(close);
                close.Click += (_, _) => CloseDraftFromUi(draft.Id);
                tab.Controls.Add(select);
                tab.Controls.Add(close);
                _draftTabs.Controls.Add(tab);
            }

            var add = new Button
            {
                Text = "+",
                Width = 42,
                Height = 36,
                Margin = new Padding(0, 0, 6, 0),
                AccessibleName = "Nueva venta"
            };
            UITheme.StyleSuccessButton(add);
            add.Click += (_, _) => CreateDraftFromUi();
            _draftTabs.Controls.Add(add);
        }
        finally
        {
            _draftTabs.ResumeLayout();
        }
    }

    private void CreateDraftFromUi()
    {
        if (_saleProcessing)
        {
            return;
        }

        if (!_draftCoordinator.CanCreateDraft)
        {
            MessageBox.Show(
                $"Solo puede mantener {_draftCoordinator.MaximumDrafts} ventas abiertas simultáneamente.",
                "Punto de venta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (_pnlPaymentOverlay.Visible)
        {
            HidePaymentOverlay(save: true);
        }

        SaveActiveDraftFields();
        if (!_draftCoordinator.TryCreateDraft(out _))
        {
            return;
        }

        LoadActiveDraftFields();
        ApplyActiveDraftToControls();
        RefreshDraftTabs();
        FocusBarcodeBox();
    }

    private void ActivateDraftFromUi(Guid draftId)
    {
        if (_saleProcessing || draftId == _draftCoordinator.ActiveDraft.Id)
        {
            return;
        }

        if (_pnlPaymentOverlay.Visible)
        {
            HidePaymentOverlay(save: true);
        }

        SaveActiveDraftFields();
        if (!_draftCoordinator.TryActivate(draftId))
        {
            return;
        }

        LoadActiveDraftFields();
        ApplyActiveDraftToControls();
        RefreshDraftTabs();
        FocusBarcodeBox();
    }

    private void CloseDraftFromUi(Guid draftId)
    {
        if (_saleProcessing)
        {
            return;
        }

        var draft = _draftCoordinator.Drafts.FirstOrDefault(item => item.Id == draftId);
        if (draft is null)
        {
            return;
        }

        if (draftId == _draftCoordinator.ActiveDraft.Id && _pnlPaymentOverlay.Visible)
        {
            HidePaymentOverlay(save: true);
        }

        SaveActiveDraftFields();
        if (!draft.IsEmpty && MessageBox.Show(
                $"¿Cerrar {draft.Title}? Sus productos pendientes se descartarán.",
                "Cerrar venta",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        if (!_draftCoordinator.TryClose(draftId, discardConfirmed: true))
        {
            return;
        }

        LoadActiveDraftFields();
        ApplyActiveDraftToControls();
        RefreshDraftTabs();
        FocusBarcodeBox();
    }

    private void DiscardAllDrafts()
    {
        _draftCoordinator.ResetAll();
        LoadActiveDraftFields();
        RefreshDraftTabs();
    }

    public void SetSelectedCustomer(CustomerSaleSelection customer)
    {
        if (!customer.Activo)
        {
            MessageBox.Show("No se puede asociar un cliente inactivo a una venta nueva.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _selectedCustomerId = customer.ClienteId;
        _selectedCustomerName = customer.NombreCompleto;
        UpdateCustomerLabel();
    }

    private async void EmployeePOSForm_Load(object? sender, EventArgs e)
    {
        if (!await EnsurePosAccessAsync())
        {
            Close();
            return;
        }

        if (_cashRecovery is not null &&
            !await _cashRecovery.EnsureReviewedAsync(
                this,
                requireContinuationForOperation: _installation.AllowsOperationalWrites))
        {
            Close();
            return;
        }

        await LoadPermissionsAsync();
        await LoadFilterOptionsAsync();
        await RefreshCashAndProductsAsync();
        FocusBarcodeBox();
    }

    private async Task<bool> EnsurePosAccessAsync()
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.POS_ACCEDER);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private async Task LoadPermissionsAsync()
    {
        _canRegisterSale = _installation.AllowsOperationalWrites &&
            await _permissionService.HasPermissionAsync(PermisoEmpleado.VENTA_REGISTRAR);
        _canApplyLineDiscount = await _permissionService.HasPermissionAsync(PermisoEmpleado.DESCUENTO_LINEA_APLICAR);
        _canApplyGeneralDiscount = await _permissionService.HasPermissionAsync(PermisoEmpleado.DESCUENTO_GENERAL_APLICAR);
        _canUseTransfer = await _permissionService.HasPermissionAsync(PermisoEmpleado.PAGO_TRANSFERENCIA_USAR);
        _canUseBitcoin = await _permissionService.HasPermissionAsync(PermisoEmpleado.PAGO_BITCOIN_USAR);
        if (_canUseBitcoin)
        {
            _canUseBitcoin = await _systemConfigService.IsBitcoinEnabledAsync();
        }
        _canRegisterCredit = await _permissionService.HasPermissionAsync(PermisoEmpleado.CREDITO_REGISTRAR);
        _canTransferInventory = _installation.AllowsOperationalWrites &&
            await _permissionService.HasPermissionAsync(PermisoEmpleado.INVENTARIO_GESTIONAR);

        _numLineDiscount.Enabled = _canApplyLineDiscount;
        _txtLineDiscountReason.Enabled = _canApplyLineDiscount;
        _cmbQuickLineReason.Enabled = _canApplyLineDiscount;
        _numGeneralDiscount.Enabled = _canApplyGeneralDiscount;
        _txtGeneralDiscountReason.Enabled = _canApplyGeneralDiscount;
        _cmbQuickGeneralReason.Enabled = _canApplyGeneralDiscount;
        _btnDiscounts.Visible = _canApplyLineDiscount || _canApplyGeneralDiscount;
        _btnOtherMethods.Visible = _canUseTransfer || _canUseBitcoin || _canRegisterCredit;
        _btnRegister.Enabled = _canRegisterSale;
        _lblScannerStatus.Text = _installation.AllowsOperationalWrites
            ? "Escáner listo. La búsqueda manual permanece disponible."
            : "Modo Consulta: búsqueda y escáner disponibles; venta y transferencias deshabilitadas.";

        if (!_canUseTransfer)
        {
            _acceptedTransfer = 0;
            _acceptedTransferRef = string.Empty;
            _numTransferAmount.Value = 0;
            _txtTransferReference.Clear();
        }

        if (!_canUseBitcoin)
        {
            _acceptedBitcoin = 0;
            _acceptedBitcoinRef = string.Empty;
            _numBitcoinAmount.Value = 0;
            _txtBitcoinReference.Clear();
        }

        ValidateRegisterButton();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _liveSearch?.Dispose();
        _productsRefreshGate.Dispose();
        _scannerQueue.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cartExitPromptVisible)
        {
            if (ControlledShutdownState.IsActive ||
                e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
            {
                _cartExitConfirmed = true;
            }
            else
            {
                e.Cancel = true;
                return;
            }
        }

        SaveActiveDraftFields();
        var pendingDrafts = _draftCoordinator.Drafts
            .Where(draft => !draft.IsEmpty)
            .ToList();
        var summary = new PosCartExitSummary(
            pendingDrafts
                .SelectMany(draft => draft.Lines)
                .Select(line => line.Product.ProductoSkuId)
                .Distinct()
                .Count(),
            pendingDrafts.Sum(draft => draft.Lines.Sum(line => line.Quantity)));
        var reason = e.CloseReason switch
        {
            CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing =>
                PosExitReason.ApplicationExit,
            _ => PosExitReason.ReturnToDashboard
        };
        var exitAllowed = PosCartExitPolicy.TryExit(
            summary,
            ControlledShutdownState.IsActive ||
                e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing,
            _cartExitConfirmed,
            confirmDiscard: () =>
            {
                _cartExitPromptVisible = true;
                try
                {
                    using var confirmation = new PosCartExitConfirmationForm(summary, reason, pendingDrafts.Count);
                    confirmation.ShowDialog(this);
                    return confirmation.DiscardConfirmed;
                }
                finally
                {
                    _cartExitPromptVisible = false;
                }
            },
            discard: () =>
            {
                _cartExitConfirmed = true;
                DiscardAllDrafts();
            });
        if (!exitAllowed)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private async void PosForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11)
        {
            FullScreenHelper.ToggleFullScreen(this);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (HandleActiveOverlayShortcut(e))
        {
            return;
        }

        if (_presentationMode == PosPresentationMode.Quick && HandleQuickPosShortcut(e))
        {
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            if (_pnlPaymentOverlay.Visible)
            {
                ClosePaymentOverlay(
                    save: false,
                    focusAfterClose: _presentationMode == PosPresentationMode.Quick ? _txtBarcode : _txtSearch);
            }
            else if (_presentationMode == PosPresentationMode.Traditional)
            {
                // Route Escape through the normal close pipeline. OnFormClosing
                // confirms before discarding any non-empty sale draft.
                Close();
            }
            else if (FullScreenHelper.IsFullScreen(this))
            {
                FullScreenHelper.ExitFullScreen(this);
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F6)
        {
            FocusSearchBox();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F7)
        {
            ShowDiscountOverlay();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F8)
        {
            FocusCashReceived();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F9)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            await RegisterExactCashSaleAsync();
        }
        else if (e.KeyCode == Keys.F10)
        {
            ShowOtherMethodsOverlay();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }

    private bool HandleActiveOverlayShortcut(KeyEventArgs e)
    {
        if (!_pnlPaymentOverlay.Visible || _activeOverlayPanel == PosOverlayPanel.None)
        {
            return false;
        }

        if (e.KeyCode == Keys.Tab)
        {
            return false;
        }

        if (e.KeyCode == Keys.Escape)
        {
            ClosePaymentOverlay(
                save: false,
                focusAfterClose: _presentationMode == PosPresentationMode.Quick ? _txtBarcode : _txtSearch);
            MarkShortcutHandled(e);
            return true;
        }

        if (e.KeyCode == Keys.Enter && !e.Control)
        {
            if (_presentationMode == PosPresentationMode.Quick &&
                _activeOverlayPanel == PosOverlayPanel.OtherPayments)
            {
                _ = ConfirmQuickCheckoutAsync();
            }
            MarkShortcutHandled(e);
            return true;
        }

        if (e.Control && e.KeyCode == Keys.Enter)
        {
            if (_activeOverlayPanel == PosOverlayPanel.Discounts)
            {
                ApplyActiveDiscount();
            }
            else if (_activeOverlayPanel == PosOverlayPanel.OtherPayments)
            {
                AcceptActiveOverlayPayment();
            }

            MarkShortcutHandled(e);
            return true;
        }

        if (e.Alt && _activeOverlayPanel == PosOverlayPanel.Discounts)
        {
            if (e.KeyCode == Keys.L)
            {
                ShowLineDiscountEditor();
                MarkShortcutHandled(e);
                return true;
            }

            if (e.KeyCode == Keys.G)
            {
                ShowGeneralDiscountEditor();
                MarkShortcutHandled(e);
                return true;
            }
        }

        if (e.Alt && _activeOverlayPanel == PosOverlayPanel.OtherPayments)
        {
            var methodKey = e.KeyCode switch
            {
                Keys.T => "TRANSFERENCIA",
                Keys.B => "BITCOIN",
                Keys.M => "MIXTO",
                Keys.E => "EFECTIVO",
                Keys.C => "CREDITO",
                _ => null
            };

            if (methodKey is not null)
            {
                SelectPaymentMethodByKey(methodKey);
                MarkShortcutHandled(e);
                return true;
            }
        }

        if (e.KeyCode is Keys.F7 or Keys.F8 or Keys.F9 or Keys.F10)
        {
            MarkShortcutHandled(e);
            return true;
        }

        return false;
    }

    private static void MarkShortcutHandled(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private void BuildTraditionalUi()
    {
        Text = "Punto de venta";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1024, 700);
        KeyPreview = true;
        KeyDown += PosForm_KeyDown;
        UITheme.StyleForm(this);
        BackColor = UITheme.BackgroundColor;

        // 1. HEADER PANEL
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(20, 10, 24, 10)
        };

        var logo = UITheme.CreateLogoPictureBox(64);
        logo.Dock = DockStyle.Fill;

        var lblTitle = new Label
        {
            Text = "Punto de Venta",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false
        };
        UITheme.StyleLabelTitle(lblTitle);

        _lblCustomer.Text = "Cliente: Consumidor final";
        _lblCustomer.AutoSize = false;
        _lblCustomer.Dock = DockStyle.Fill;
        _lblCustomer.TextAlign = ContentAlignment.TopLeft;
        _lblCustomer.ForeColor = UITheme.MutedTextColor;
        _lblCustomer.Font = UITheme.NormalBoldFont;
        _lblCustomer.AutoEllipsis = true;

        var titleLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor
        };
        titleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        titleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        titleLayout.Controls.Add(lblTitle, 0, 0);
        titleLayout.Controls.Add(_lblCustomer, 0, 1);

        var usuario = _currentSession.UsuarioActual;
        var lblUser = new Label
        {
            Text = usuario is null ? string.Empty : usuario.NombreCompleto,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = UITheme.TextDark,
            Font = UITheme.NormalBoldFont,
            AutoEllipsis = true,
            Padding = new Padding(0, 0, 12, 0)
        };

        _lblCashStatus.AutoSize = false;
        _lblCashStatus.Dock = DockStyle.Fill;
        _lblCashStatus.TextAlign = ContentAlignment.MiddleCenter;
        _lblCashStatus.AutoEllipsis = false;
        _lblCashStatus.Margin = Padding.Empty;
        UITheme.StyleLabelHeader(_lblCashStatus);

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        headerLayout.Controls.Add(logo, 0, 0);
        headerLayout.Controls.Add(titleLayout, 1, 0);
        headerLayout.Controls.Add(lblUser, 2, 0);
        headerLayout.Controls.Add(_lblCashStatus, 3, 0);
        pnlHeader.Controls.Add(headerLayout);

        // 2. RIGHT PANEL (Cart & Checkout)
        var pnlRight = new Panel
        {
            Dock = DockStyle.Right,
            Width = 540,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(12)
        };

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = UITheme.BackgroundColor
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        var lblCartTitle = new Label
        {
            Text = "Venta actual",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.TextDark
        };

        ConfigureCartGrid();
        _gridCart.Dock = DockStyle.Fill;
        _gridCart.Margin = new Padding(0, 0, 0, 8);

        SetupQuickReasons(_cmbQuickLineReason, _txtLineDiscountReason);
        SetupQuickReasons(_cmbQuickGeneralReason, _txtGeneralDiscountReason);

        var linePanel = CreateSurfacePanel();
        var lineLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(8)
        };
        lineLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        lineLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        lineLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        lineLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        lineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        lineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        lineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        lineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblSelectedLine.Text = "Seleccione un producto de la venta";
        _lblSelectedLine.Dock = DockStyle.Fill;
        _lblSelectedLine.TextAlign = ContentAlignment.MiddleLeft;
        _lblSelectedLine.Font = UITheme.NormalBoldFont;
        _lblSelectedLine.ForeColor = UITheme.TextDark;
        _lblSelectedLine.AutoEllipsis = true;
        lineLayout.Controls.Add(_lblSelectedLine, 0, 0);
        lineLayout.SetColumnSpan(_lblSelectedLine, 4);

        lineLayout.Controls.Add(CreateFieldLabel("Cant."), 0, 1);
        lineLayout.Controls.Add(CreateFieldLabel("Desc. linea ($)"), 1, 1);
        lineLayout.Controls.Add(CreateFieldLabel("Motivo rapido"), 2, 1);
        lineLayout.Controls.Add(CreateFieldLabel("Motivo manual"), 3, 1);

        ConfigureInteger(_numQuantity, 0, 0);
        _numQuantity.Minimum = 1;
        _numQuantity.Value = 1;
        DockInput(_numQuantity);
        ConfigureMoney(_numLineDiscount, 0, 0);
        DockInput(_numLineDiscount);
        DockCombo(_cmbQuickLineReason);
        UITheme.StyleTextBox(_txtLineDiscountReason);
        DockInput(_txtLineDiscountReason);

        lineLayout.Controls.Add(_numQuantity, 0, 2);
        lineLayout.Controls.Add(_numLineDiscount, 1, 2);
        lineLayout.Controls.Add(_cmbQuickLineReason, 2, 2);
        lineLayout.Controls.Add(_txtLineDiscountReason, 3, 2);

        ConfigureButton(_btnUpdateLine, "Actualizar linea", 0, 0, BtnUpdateLine_Click);
        UITheme.StyleSecondaryButton(_btnUpdateLine);
        _btnUpdateLine.Dock = DockStyle.Fill;
        _btnUpdateLine.Margin = new Padding(0, 8, 5, 0);
        ConfigureButton(_btnRemove, "Quitar seleccionado", 0, 0, BtnRemove_Click);
        UITheme.StyleDangerButton(_btnRemove);
        _btnRemove.Dock = DockStyle.Fill;
        _btnRemove.Margin = new Padding(5, 8, 0, 0);

        lineLayout.Controls.Add(_btnUpdateLine, 0, 3);
        lineLayout.SetColumnSpan(_btnUpdateLine, 2);
        lineLayout.Controls.Add(_btnRemove, 2, 3);
        lineLayout.SetColumnSpan(_btnRemove, 2);
        linePanel.Controls.Add(lineLayout);

        var paySummaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor
        };
        paySummaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        paySummaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        paySummaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var paymentsPanel = CreateSurfacePanel();
        paymentsPanel.Margin = new Padding(0, 0, 8, 8);
        var paymentsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10)
        };
        paymentsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        paymentsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paymentsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        for (var i = 1; i < 8; i++)
        {
            paymentsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        }

        var lblPaymentsTitle = CreateSectionTitle("Descuento y pago");
        paymentsLayout.Controls.Add(lblPaymentsTitle, 0, 0);
        paymentsLayout.SetColumnSpan(lblPaymentsTitle, 2);

        ConfigureMoney(_numGeneralDiscount, 0, 0);
        _numGeneralDiscount.ValueChanged += (_, _) =>
        {
            if (_restoringDraftState)
            {
                return;
            }

            InvalidateAcceptedPayment();
            RecalculateTotals();
        };
        DockCombo(_cmbQuickGeneralReason);
        UITheme.StyleTextBox(_txtGeneralDiscountReason);
        DockInput(_txtGeneralDiscountReason);
        var discountReasonLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0)
        };
        discountReasonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        discountReasonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        discountReasonLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _cmbQuickGeneralReason.Margin = new Padding(0, 0, 4, 0);
        _txtGeneralDiscountReason.Margin = new Padding(4, 0, 0, 0);
        discountReasonLayout.Controls.Add(_cmbQuickGeneralReason, 0, 0);
        discountReasonLayout.Controls.Add(_txtGeneralDiscountReason, 1, 0);
        AddPaymentField(paymentsLayout, 1, "Desc. gen.", _numGeneralDiscount);
        AddPaymentField(paymentsLayout, 2, "Motivo", discountReasonLayout);

        ConfigureMoney(_numCashReceived, 0, 0);
        _numCashReceived.ValueChanged += (_, _) => QuickCashPaymentChanged();
        _numCashReceived.TextChanged += (_, _) => QuickCashPaymentChanged();
        _numCashReceived.KeyDown += NumCashReceived_KeyDown;
        ConfigureMoney(_numPaymentCashAmount, 0, 0);
        SelectNumericTextOnFocus(_numPaymentCashAmount);
        _numPaymentCashAmount.ValueChanged += (_, _) => UpdateOverlayTotals();
        _numPaymentCashAmount.TextChanged += (_, _) => UpdateOverlayTotals();
        ConfigureMoney(_numTransferAmount, 0, 0);
        SelectNumericTextOnFocus(_numTransferAmount);
        _numTransferAmount.ValueChanged += (_, _) => PaymentAmountChanged();
        _numTransferAmount.TextChanged += (_, _) => PaymentAmountChanged();
        UITheme.StyleTextBox(_txtTransferReference);
        ConfigureMoney(_numBitcoinAmount, 0, 0);
        SelectNumericTextOnFocus(_numBitcoinAmount);
        _numBitcoinAmount.ValueChanged += (_, _) => PaymentAmountChanged();
        _numBitcoinAmount.TextChanged += (_, _) => PaymentAmountChanged();
        UITheme.StyleTextBox(_txtBitcoinReference);
        paymentsPanel.Controls.Add(paymentsLayout);

        var summaryPanel = CreateSurfacePanel();
        summaryPanel.Margin = new Padding(8, 0, 0, 8);
        var summaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12, 10, 12, 10)
        };
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        summaryLayout.Controls.Add(CreateSectionTitle("Resumen"), 0, 0);

        var summaryDetailsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0),
            BackColor = UITheme.SurfaceColor
        };
        summaryDetailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summaryDetailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summaryDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));
        summaryDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        summaryDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

        ConfigureSummaryLabel(_lblSubtotal, UITheme.TextDark, UITheme.NormalBoldFont);
        ConfigureSummaryLabel(_lblDiscounts, UITheme.DangerColor, UITheme.NormalBoldFont);
        ConfigureSummaryLabel(_lblPaid, UITheme.TextDark, UITheme.NormalBoldFont);
        ConfigureSummaryLabel(_lblMissing, UITheme.DangerColor, UITheme.NormalBoldFont);
        ConfigureSummaryLabel(_lblChange, UITheme.TextDark, UITheme.NormalBoldFont);
        // Mantiene el total dominante sin forzar AutoEllipsis en 1280x720 ni con DPI alto.
        ConfigureSummaryLabel(_lblTotal, UITheme.SuccessColor, new Font("Segoe UI", 21, FontStyle.Bold));

        summaryDetailsLayout.Controls.Add(_lblSubtotal, 0, 0);
        summaryDetailsLayout.Controls.Add(_lblDiscounts, 1, 0);
        summaryDetailsLayout.Controls.Add(_lblPaid, 0, 1);
        summaryDetailsLayout.Controls.Add(_lblChange, 1, 1);
        summaryDetailsLayout.Controls.Add(_lblMissing, 0, 2);
        summaryDetailsLayout.SetColumnSpan(_lblMissing, 2);

        summaryLayout.Controls.Add(summaryDetailsLayout, 0, 1);
        summaryLayout.Controls.Add(_lblTotal, 0, 2);
        summaryPanel.Controls.Add(summaryLayout);

        paySummaryLayout.Controls.Add(paymentsPanel, 0, 0);
        paySummaryLayout.Controls.Add(summaryPanel, 1, 0);

        var quickCashPanel = CreateSurfacePanel();
        quickCashPanel.Margin = new Padding(0, 0, 0, 8);
        quickCashPanel.BackColor = Color.FromArgb(248, 255, 251);
        var quickCashLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            BackColor = quickCashPanel.BackColor,
            Padding = new Padding(14, 8, 14, 8)
        };
        quickCashLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        quickCashLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        quickCashLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        quickCashLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        quickCashLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        quickCashLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var lblQuickCashTitle = CreateSectionTitle("Cobro rápido (Efectivo)");
        lblQuickCashTitle.ForeColor = UITheme.SuccessColor;
        quickCashLayout.Controls.Add(lblQuickCashTitle, 0, 0);
        quickCashLayout.SetColumnSpan(lblQuickCashTitle, 3);
        quickCashLayout.Controls.Add(CreateFieldLabel("Efectivo recibido"), 0, 1);
        quickCashLayout.Controls.Add(CreateFieldLabel("Vuelto"), 1, 1);
        quickCashLayout.Controls.Add(CreateFieldLabel("Método actual"), 2, 1);
        DockInput(_numCashReceived);
        quickCashLayout.Controls.Add(_numCashReceived, 0, 2);

        _lblQuickCashChange.Dock = DockStyle.Fill;
        _lblQuickCashChange.TextAlign = ContentAlignment.MiddleCenter;
        _lblQuickCashChange.Font = new Font("Segoe UI", 14, FontStyle.Bold);
        _lblQuickCashChange.ForeColor = UITheme.SuccessColor;
        quickCashLayout.Controls.Add(_lblQuickCashChange, 1, 2);

        _lblPaymentMethodStatus.Dock = DockStyle.Fill;
        _lblPaymentMethodStatus.TextAlign = ContentAlignment.MiddleCenter;
        _lblPaymentMethodStatus.Font = UITheme.NormalBoldFont;
        _lblPaymentMethodStatus.ForeColor = UITheme.SuccessColor;
        _lblPaymentMethodStatus.BackColor = Color.FromArgb(230, 245, 236);
        quickCashLayout.Controls.Add(_lblPaymentMethodStatus, 2, 2);
        quickCashPanel.Controls.Add(quickCashLayout);

        var methodShortcutsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor
        };
        methodShortcutsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        methodShortcutsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        methodShortcutsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _btnDiscounts.Text = "DESCUENTOS";
        _btnDiscounts.Click += (_, _) => ShowDiscountOverlay();
        UITheme.StylePrimaryButton(_btnDiscounts);
        _btnDiscounts.Dock = DockStyle.Fill;
        _btnDiscounts.Margin = new Padding(0, 0, 6, 8);
        _btnDiscounts.Font = UITheme.LargeButtonFont;

        _btnOtherMethods.Text = "OTROS MÉTODOS";
        _btnOtherMethods.Click += (_, _) => ShowOtherMethodsOverlay();
        UITheme.StyleSecondaryButton(_btnOtherMethods);
        _btnOtherMethods.Dock = DockStyle.Fill;
        _btnOtherMethods.Margin = new Padding(6, 0, 0, 8);
        _btnOtherMethods.Font = UITheme.LargeButtonFont;

        methodShortcutsPanel.Controls.Add(_btnDiscounts, 0, 0);
        methodShortcutsPanel.Controls.Add(_btnOtherMethods, 1, 0);

        var pnlCheckoutActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(0, 8, 0, 0)
        };
        pnlCheckoutActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        pnlCheckoutActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlCheckoutActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _btnClear.Text = "Limpiar";
        _btnClear.Click += (_, _) => ClearCart();
        UITheme.StyleDangerButton(_btnClear);
        _btnClear.Dock = DockStyle.Fill;
        _btnClear.Margin = new Padding(0, 0, 10, 0);

        _btnRegister.Text = "REGISTRAR VENTA";
        _btnRegister.Click += BtnRegister_Click;
        UITheme.StyleSuccessButton(_btnRegister);
        _btnRegister.Dock = DockStyle.Fill;
        _btnRegister.Margin = new Padding(0);
        _btnRegister.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        _btnRegister.ForeColor = Color.White;
        _btnRegister.TextAlign = ContentAlignment.MiddleCenter;
        _btnRegister.UseVisualStyleBackColor = false;

        pnlCheckoutActions.Controls.Add(_btnClear, 0, 0);
        pnlCheckoutActions.Controls.Add(_btnRegister, 1, 0);

        rightLayout.Controls.Add(lblCartTitle, 0, 0);
        rightLayout.Controls.Add(BuildDraftTabs(), 0, 1);
        rightLayout.Controls.Add(_gridCart, 0, 2);
        rightLayout.Controls.Add(summaryPanel, 0, 3);
        rightLayout.Controls.Add(quickCashPanel, 0, 4);
        rightLayout.Controls.Add(methodShortcutsPanel, 0, 5);
        rightLayout.Controls.Add(pnlCheckoutActions, 0, 6);
        pnlRight.Controls.Add(rightLayout);

        // 3. LEFT PANEL (Search & Products)
        var pnlLeft = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(20)
        };

        var pnlSearch = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 220,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        pnlSearch.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        pnlSearch.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        pnlSearch.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        pnlSearch.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        var searchLabel = CreateLabel("Buscar producto:", 0, 0);
        searchLabel.Dock = DockStyle.Fill;
        searchLabel.TextAlign = ContentAlignment.MiddleLeft;
        searchRow.Controls.Add(searchLabel, 0, 0);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.Margin = new Padding(2, 5, 4, 5);
        _txtSearch.PlaceholderText = "Nombre, código, SKU, categoría o subcategoría";
        UITheme.StyleInput(_txtSearch);
        _txtSearch.Font = UITheme.SubheaderFont;
        _txtSearch.KeyDown += TxtSearch_KeyDown;
        searchRow.Controls.Add(_txtSearch, 1, 0);
        _btnClearSearch.Text = "X";
        _btnClearSearch.AccessibleName = "Limpiar búsqueda";
        _btnClearSearch.Dock = DockStyle.Fill;
        _btnClearSearch.Margin = new Padding(0, 5, 4, 5);
        UITheme.StyleOutlineButton(_btnClearSearch);
        searchRow.Controls.Add(_btnClearSearch, 2, 0);
        ConfigureButton(_btnSearch, "Buscar", 0, 0, async (_, _) =>
        {
            if (_liveSearch is not null)
            {
                await _liveSearch.ExecuteNowAsync(token =>
                    ApplyProductFilterAsync(forceRender: true, resetPage: true, cancellationToken: token));
            }
            else
            {
                await ApplyProductFilterAsync(forceRender: true, resetPage: true);
            }
        });
        UITheme.StylePrimaryButton(_btnSearch);
        _btnSearch.Dock = DockStyle.Fill;
        _btnSearch.Margin = new Padding(2, 5, 4, 5);
        searchRow.Controls.Add(_btnSearch, 3, 0);
        ConfigureButton(_btnRefresh, "Refrescar", 0, 0, async (_, _) => await RefreshCashAndProductsAsync());
        UITheme.StyleSecondaryButton(_btnRefresh);
        _btnRefresh.Dock = DockStyle.Fill;
        _btnRefresh.Margin = new Padding(2, 5, 0, 5);
        searchRow.Controls.Add(_btnRefresh, 4, 0);
        pnlSearch.Controls.Add(searchRow, 0, 0);

        var filterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        filterRow.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        filterRow.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        TableLayoutPanel CreateFilterField(string caption, ComboBox combo)
        {
            ConfigureFilterCombo(combo, 0, 0, 160);
            DockCombo(combo);
            combo.Margin = new Padding(4, 2, 4, 4);
            var field = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                BackColor = UITheme.BackgroundColor
            };
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = UITheme.MutedTextColor,
                Margin = new Padding(4, 0, 4, 0)
            };
            field.Controls.Add(label, 0, 0);
            field.Controls.Add(combo, 0, 1);
            return field;
        }

        filterRow.Controls.Add(CreateFilterField("Categoría:", _cmbCategoryFilter), 0, 0);
        filterRow.Controls.Add(CreateFilterField("Subcategoría:", _cmbSubcategoryFilter), 1, 0);
        filterRow.Controls.Add(CreateFilterField("Stock:", _cmbStockFilter), 0, 1);
        var locationAndSort = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        locationAndSort.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        locationAndSort.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        locationAndSort.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        locationAndSort.Controls.Add(CreateFilterField("Ubicación:", _cmbLocationFilter), 0, 0);
        locationAndSort.Controls.Add(CreateFilterField("Orden:", _cmbSort), 1, 0);
        filterRow.Controls.Add(locationAndSort, 1, 1);
        pnlSearch.Controls.Add(filterRow, 0, 1);

        var barcodeRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        barcodeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        barcodeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        barcodeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        var barcodeLabel = CreateLabel("Escáner / barcode:", 0, 0);
        barcodeLabel.Dock = DockStyle.Fill;
        barcodeLabel.TextAlign = ContentAlignment.MiddleLeft;
        barcodeRow.Controls.Add(barcodeLabel, 0, 0);
        _txtBarcode.Dock = DockStyle.Fill;
        _txtBarcode.Margin = new Padding(2, 4, 4, 4);
        _txtBarcode.Font = UITheme.SubheaderFont;
        _txtBarcode.MaxLength = 128;
        _txtBarcode.AccessibleName = "Entrada de escáner de código de barras";
        _txtBarcode.PlaceholderText = "Escanear o escribir código y presionar Enter";
        UITheme.StyleInput(_txtBarcode);
        _txtBarcode.KeyDown += TxtBarcode_KeyDown;
        barcodeRow.Controls.Add(_txtBarcode, 1, 0);
        _lblScannerStatus.Dock = DockStyle.Fill;
        _lblScannerStatus.Margin = new Padding(4, 3, 0, 3);
        _lblScannerStatus.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _lblScannerStatus.ForeColor = UITheme.MutedTextColor;
        _lblScannerStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblScannerStatus.AutoEllipsis = true;
        barcodeRow.Controls.Add(_lblScannerStatus, 2, 0);
        pnlSearch.Controls.Add(barcodeRow, 0, 2);

        var paginationRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = UITheme.BackgroundColor
        };
        paginationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        paginationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        paginationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        paginationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        ConfigureButton(_btnPreviousPage, "Anterior", 0, 0, (_, _) => ChangeProductsPage(-1));
        UITheme.StyleSecondaryButton(_btnPreviousPage);
        _btnPreviousPage.Dock = DockStyle.Fill;
        _btnPreviousPage.Margin = new Padding(0, 4, 4, 4);
        paginationRow.Controls.Add(_btnPreviousPage, 0, 0);
        _lblPageInfo.Dock = DockStyle.Fill;
        _lblPageInfo.TextAlign = ContentAlignment.MiddleLeft;
        UITheme.StyleLabelNormal(_lblPageInfo);
        paginationRow.Controls.Add(_lblPageInfo, 1, 0);
        ConfigureButton(_btnNextPage, "Siguiente", 0, 0, (_, _) => ChangeProductsPage(1));
        UITheme.StyleSecondaryButton(_btnNextPage);
        _btnNextPage.Dock = DockStyle.Fill;
        _btnNextPage.Margin = new Padding(2, 4, 4, 4);
        paginationRow.Controls.Add(_btnNextPage, 2, 0);
        _lblResultsInfo.Dock = DockStyle.Fill;
        _lblResultsInfo.TextAlign = ContentAlignment.MiddleLeft;
        UITheme.StyleLabelNormal(_lblResultsInfo);
        paginationRow.Controls.Add(_lblResultsInfo, 3, 0);
        pnlSearch.Controls.Add(paginationRow, 0, 3);

        ConfigureProductsPanel();
        _productsPanel.Dock = DockStyle.Fill;
        _productsPanel.Padding = new Padding(0, 10, 0, 0);

        pnlLeft.Controls.Add(_productsPanel);
        pnlLeft.Controls.Add(pnlSearch);

        Controls.Add(pnlLeft);
        Controls.Add(pnlRight);
        Controls.Add(pnlHeader);
        BuildPaymentOverlay();

        UpdatePaymentReferenceStates();
        RecalculateTotals();
    }

    private void BuildPaymentOverlay()
    {
        _pnlPaymentOverlay.Dock = _presentationMode == PosPresentationMode.Quick
            ? DockStyle.None
            : DockStyle.Fill;
        if (_presentationMode == PosPresentationMode.Quick)
        {
            _pnlPaymentOverlay.Bounds = ClientRectangle;
            _pnlPaymentOverlay.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        }
        _pnlPaymentOverlay.BackColor = UITheme.BackgroundColor;
        _pnlPaymentOverlay.Visible = false;

        _pnlPaymentModal.Size = _presentationMode == PosPresentationMode.Quick
            ? new Size(900, 660)
            : new Size(1100, 720);
        _pnlPaymentModal.BackColor = UITheme.SurfaceColor;
        _pnlPaymentModal.Padding = new Padding(32);

        var overlayLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UITheme.SurfaceColor
        };
        overlayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        overlayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _presentationMode == PosPresentationMode.Quick ? 72 : 128));
        overlayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _presentationMode == PosPresentationMode.Quick ? 166 : 158));
        overlayLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        overlayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var title = new Label
        {
            Text = _presentationMode == PosPresentationMode.Quick ? "Cobro rápido" : "Seleccionar método de pago",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = UITheme.PrimaryColor
        };
        overlayLayout.Controls.Add(title, 0, 0);

        _pnlPaymentMethodButtons.Dock = DockStyle.Fill;
        _pnlPaymentMethodButtons.FlowDirection = FlowDirection.LeftToRight;
        _pnlPaymentMethodButtons.WrapContents = _presentationMode != PosPresentationMode.Quick;
        _pnlPaymentMethodButtons.AutoScroll = false;
        _pnlPaymentMethodButtons.Padding = new Padding(0, 8, 0, 8);
        AddPaymentMethodButtons(includeCashButton: true);
        overlayLayout.Controls.Add(_pnlPaymentMethodButtons, 0, 1);

        var totalsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0, 12, 0, 12),
            BackColor = UITheme.SoftPinkColor,
            Padding = new Padding(18, 10, 18, 10)
        };
        totalsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        totalsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        for (var i = 0; i < 4; i++)
        {
            totalsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }

        ConfigureOverlayAmountLabel(_lblOverlayTotalToPay, UITheme.TextDark, new Font("Segoe UI", 15, FontStyle.Bold));
        ConfigureOverlayAmountLabel(_lblOverlayPaid, UITheme.TextDark, new Font("Segoe UI", 13, FontStyle.Bold));
        ConfigureOverlayAmountLabel(_lblOverlayMissing, UITheme.DangerColor, new Font("Segoe UI", 13, FontStyle.Bold));
        ConfigureOverlayAmountLabel(_lblOverlayChange, UITheme.SuccessColor, new Font("Segoe UI", 13, FontStyle.Bold));

        AddOverlayTotalRow(totalsPanel, 0, "TOTAL A PAGAR:", _lblOverlayTotalToPay, new Font("Segoe UI", 12, FontStyle.Bold));
        AddOverlayTotalRow(totalsPanel, 1, "TOTAL PAGADO:", _lblOverlayPaid, UITheme.NormalBoldFont);
        AddOverlayTotalRow(totalsPanel, 2, "FALTANTE:", _lblOverlayMissing, UITheme.NormalBoldFont);
        AddOverlayTotalRow(totalsPanel, 3, "VUELTO:", _lblOverlayChange, UITheme.NormalBoldFont);
        overlayLayout.Controls.Add(totalsPanel, 0, 2);

        _pnlPaymentMethodContent.Dock = DockStyle.Fill;
        overlayLayout.Controls.Add(_pnlPaymentMethodContent, 0, 3);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _btnCancelPayment.Text = "Cancelar";
        _btnCancelPayment.Click += (_, _) => ClosePaymentOverlay(
            save: false,
            focusAfterClose: _presentationMode == PosPresentationMode.Quick ? _txtBarcode : _txtSearch);
        _shortcutToolTip.SetToolTip(_btnCancelPayment, "Esc");
        UITheme.StyleSecondaryButton(_btnCancelPayment);
        _btnCancelPayment.Dock = DockStyle.Fill;
        _btnCancelPayment.Margin = new Padding(0, 0, 10, 0);

        _btnAcceptPayment.Text = _presentationMode == PosPresentationMode.Quick
            ? "CONFIRMAR VENTA · ENTER"
            : "ACEPTAR PAGO";
        _btnAcceptPayment.Click += async (_, _) =>
        {
            if (_presentationMode == PosPresentationMode.Quick)
            {
                await ConfirmQuickCheckoutAsync();
            }
            else
            {
                AcceptActiveOverlayPayment();
            }
        };
        _shortcutToolTip.SetToolTip(
            _btnAcceptPayment,
            _presentationMode == PosPresentationMode.Quick ? "Enter" : "Ctrl+Enter");
        UITheme.StyleSuccessButton(_btnAcceptPayment);
        _btnAcceptPayment.Dock = DockStyle.Fill;
        _btnAcceptPayment.Margin = new Padding(10, 0, 0, 0);
        _btnAcceptPayment.Font = new Font(
            "Segoe UI",
            _presentationMode == PosPresentationMode.Quick ? 12 : 14,
            FontStyle.Bold);

        actions.Controls.Add(_btnCancelPayment, 0, 0);
        actions.Controls.Add(_btnAcceptPayment, 1, 0);
        overlayLayout.Controls.Add(actions, 0, 4);

        _pnlPaymentModal.Controls.Add(overlayLayout);
        _pnlPaymentOverlay.Controls.Add(_pnlPaymentModal);
        _pnlPaymentOverlay.Resize += (_, _) => CenterPaymentModal();

        Controls.Add(_pnlPaymentOverlay);
        _pnlPaymentOverlay.BringToFront();
    }

    private void CenterPaymentModal()
    {
        if (_presentationMode != PosPresentationMode.Quick && _pnlPaymentOverlay.ClientSize.Width > 0)
        {
            _pnlPaymentModal.Width = Math.Min(1100, _pnlPaymentOverlay.ClientSize.Width);
            _pnlPaymentModal.Height = Math.Min(720, _pnlPaymentOverlay.ClientSize.Height);
        }

        _pnlPaymentModal.Left = Math.Max(0, (_pnlPaymentOverlay.Width - _pnlPaymentModal.Width) / 2);
        _pnlPaymentModal.Top = Math.Max(0, (_pnlPaymentOverlay.Height - _pnlPaymentModal.Height) / 2);
    }

    private void SetOverlayTitle(string title)
    {
        if (_pnlPaymentModal.Controls.OfType<TableLayoutPanel>().FirstOrDefault()?.GetControlFromPosition(0, 0) is Label label)
        {
            label.Text = title;
        }
    }

    private void SetOverlayActionsVisible(bool visible)
    {
        var overlayLayout = _pnlPaymentModal.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (overlayLayout?.GetControlFromPosition(0, 4) is Control actions)
        {
            actions.Visible = visible;
        }

        if (overlayLayout is not null && overlayLayout.RowStyles.Count > 4)
        {
            // Descuentos no usa la fila de acciones del cobro; colapsarla
            // evita que el editor quede recortado dentro del mismo overlay.
            overlayLayout.RowStyles[4] = new RowStyle(
                SizeType.Absolute,
                visible ? 62 : 0);
        }
    }

    private void UpdatePaymentMethodDisplay()
    {
        _lblPaymentMethodStatus.Text = _currentPaymentMethod switch
        {
            "TRANSFERENCIA" => "TRANSFERENCIA",
            "BITCOIN" => "BITCOIN",
            "MIXTO" => "PAGO MIXTO",
            "CREDITO" => "CREDITO",
            _ => "EFECTIVO"
        };

        _numCashReceived.Enabled = _currentPaymentMethod == "EFECTIVO";
        _lblPaymentMethodStatus.ForeColor = _currentPaymentMethod == "EFECTIVO"
            ? UITheme.SuccessColor
            : UITheme.SecondaryColor;
    }

    private Button CreatePaymentMethodButton(string text, string methodKey, string? shortcut = null)
    {
        var button = new Button
        {
            Text = text,
            Width = _presentationMode == PosPresentationMode.Quick ? 155 : 180,
            Height = 56,
            Margin = new Padding(5, 0, 5, 0),
            Font = UITheme.ButtonFont,
            Tag = methodKey,
            AutoSize = false,
            AutoEllipsis = false,
            Padding = new Padding(4, 0, 4, 0),
            TextAlign = ContentAlignment.MiddleCenter
        };
        UITheme.StyleSecondaryButton(button);
        button.Click += (_, _) => SelectPaymentMethod(methodKey, button);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            _shortcutToolTip.SetToolTip(button, shortcut);
        }

        return button;
    }

    private void AddPaymentMethodButtons(bool includeCashButton)
    {
        _pnlPaymentMethodButtons.Controls.Clear();
        var buttons = new List<Button>();

        if (includeCashButton)
        {
            buttons.Add(CreatePaymentMethodButton("Efectivo", "EFECTIVO", "Alt+E"));
        }

        if (_canUseTransfer)
        {
            buttons.Add(CreatePaymentMethodButton("Transferencia", "TRANSFERENCIA", "Alt+T"));
        }

        if (_canUseBitcoin)
        {
            buttons.Add(CreatePaymentMethodButton("Bitcoin", "BITCOIN", "Alt+B"));
        }

        if (_canUseTransfer || _canUseBitcoin)
        {
            buttons.Add(CreatePaymentMethodButton("Pago mixto", "MIXTO", "Alt+M"));
        }

        if (_canRegisterCredit)
        {
            buttons.Add(CreatePaymentMethodButton("Credito", "CREDITO", "Alt+C"));
        }

        if (!includeCashButton)
        {
            buttons.Add(CreatePaymentMethodButton("Volver a efectivo", "EFECTIVO", "Alt+E"));
        }

        _pnlPaymentMethodButtons.Controls.AddRange(buttons.Cast<Control>().ToArray());
    }

    private void SelectPaymentMethod(string methodKey, Button selectedButton)
    {
        if (!IsPaymentMethodAllowed(methodKey))
        {
            MessageBox.Show("No tiene permiso para usar este metodo de pago.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (methodKey == "EFECTIVO")
        {
            if (_presentationMode == PosPresentationMode.Quick)
            {
                SelectQuickCashPayment(selectedButton);
                return;
            }
            ReturnToDefaultCashPayment();
            ClosePaymentOverlay(save: false, focusAfterClose: _numCashReceived);
            return;
        }

        if (methodKey == "CREDITO")
        {
            OpenCreditSaleDialogFromOverlay();
            return;
        }

        ResetActiveOverlayActions();
        var methodChanged = _currentPaymentMethod != methodKey;
        _currentPaymentMethod = methodKey;
        if (methodChanged)
        {
            _paymentValidated = false;
            _acceptedCash = 0;
            _acceptedTransfer = 0;
            _acceptedTransferRef = string.Empty;
            _acceptedBitcoin = 0;
            _acceptedBitcoinRef = string.Empty;
        }

        foreach (Control control in _pnlPaymentMethodButtons.Controls)
        {
            if (control is Button button)
            {
                UITheme.StyleSecondaryButton(button);
            }
        }

        UITheme.StylePrimaryButton(selectedButton);
        _pnlPaymentMethodContent.Controls.Clear();

        var rowCount = methodKey == "MIXTO"
            ? 5
            : methodKey is "TRANSFERENCIA" or "BITCOIN" ? 2 : 1;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rowCount,
            Padding = new Padding(10, 12, 10, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rowCount; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));
        }

        switch (methodKey)
        {
            case "TRANSFERENCIA":
                if (!_canUseTransfer)
                {
                    return;
                }

                AddPaymentField(layout, 0, "Transferencia", _numTransferAmount);
                AddPaymentField(layout, 1, "Referencia", _txtTransferReference);
                _numPaymentCashAmount.Value = 0;
                _numBitcoinAmount.Value = 0;
                if (methodChanged)
                {
                    _numTransferAmount.Value = 0;
                    _txtTransferReference.Clear();
                }
                _activeOverlayPrimaryControl = _numTransferAmount;
                break;
            case "BITCOIN":
                if (!_canUseBitcoin)
                {
                    return;
                }

                AddPaymentField(layout, 0, "Bitcoin", _numBitcoinAmount);
                AddPaymentField(layout, 1, "Referencia", _txtBitcoinReference);
                _numPaymentCashAmount.Value = 0;
                _numTransferAmount.Value = 0;
                if (methodChanged)
                {
                    _numBitcoinAmount.Value = 0;
                    _txtBitcoinReference.Clear();
                }
                _activeOverlayPrimaryControl = _numBitcoinAmount;
                break;
            case "MIXTO":
                AddPaymentField(layout, 0, "Efectivo", _numPaymentCashAmount);
                if (_canUseTransfer)
                {
                    AddPaymentField(layout, 1, "Transferencia", _numTransferAmount);
                    AddPaymentField(layout, 2, "Ref. Transf.", _txtTransferReference);
                }

                if (_canUseBitcoin)
                {
                    AddPaymentField(layout, 3, "Bitcoin", _numBitcoinAmount);
                    AddPaymentField(layout, 4, "Ref. Bitcoin", _txtBitcoinReference);
                }

                if (methodChanged)
                {
                    _numPaymentCashAmount.Value = 0;
                    _numTransferAmount.Value = 0;
                    _txtTransferReference.Clear();
                    _numBitcoinAmount.Value = 0;
                    _txtBitcoinReference.Clear();
                }
                _activeOverlayPrimaryControl = _numPaymentCashAmount;
                break;
        }

        UpdatePaymentReferenceStates();
        _pnlPaymentMethodContent.Controls.Add(layout);
        UpdateOverlayTotals();
        RecalculateTotals();
        FocusOverlayPrimaryControl();
    }

    private void SelectPaymentMethodByKey(string methodKey)
    {
        foreach (Control control in _pnlPaymentMethodButtons.Controls)
        {
            if (control is Button button && button.Tag is string key && key == methodKey)
            {
                SelectPaymentMethod(methodKey, button);
                return;
            }
        }

        if (methodKey == "EFECTIVO")
        {
            ReturnToDefaultCashPayment();
            ClosePaymentOverlay(save: false, focusAfterClose: _numCashReceived);
        }
    }

    private async void OpenCreditSaleDialogFromOverlay()
    {
        if (_saleProcessing || _creditDialogOpen)
        {
            return;
        }

        if (!_canRegisterCredit)
        {
            MessageBox.Show("No tiene permiso para registrar ventas a credito.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_selectedCustomerId is null)
        {
            MessageBox.Show("Seleccione un cliente real antes de vender a credito. Consumidor final no puede recibir credito.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_cart.Count == 0)
        {
            MessageBox.Show("El carrito esta vacio.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _creditDialogOpen = true;
        try
        {
            CommitMoneyInput(_numPaymentCashAmount);
            CommitMoneyInput(_numTransferAmount);
            CommitMoneyInput(_numBitcoinAmount);

            var initialPayments = BuildInitialCreditPaymentsFromOverlay();
            using var dialog = new CreditSaleDialog(
                _creditService,
                _selectedCustomerId.Value,
                _selectedCustomerName,
                BuildCreditDialogLines(),
                GetCurrentTotalCentavos(),
                initialPayments,
                _canUseTransfer,
                _canUseBitcoin);

            if (dialog.ShowDialog(this) != DialogResult.OK ||
                (dialog.CreditRequest is null && !dialog.ContinueAsNormalSale))
            {
                return;
            }

            var creditRequest = dialog.CreditRequest;
            _pendingCreditRequest = creditRequest;
            _acceptedCash = dialog.InitialPayments.Where(p => p.MetodoPago == MetodoPago.EFECTIVO).Sum(p => p.MontoCentavos);
            _acceptedTransfer = dialog.InitialPayments.Where(p => p.MetodoPago == MetodoPago.TRANSFERENCIA).Sum(p => p.MontoCentavos);
            _acceptedTransferRef = dialog.InitialPayments.FirstOrDefault(p => p.MetodoPago == MetodoPago.TRANSFERENCIA)?.Referencia ?? string.Empty;
            _acceptedBitcoin = dialog.InitialPayments.Where(p => p.MetodoPago == MetodoPago.BITCOIN).Sum(p => p.MontoCentavos);
            _acceptedBitcoinRef = dialog.InitialPayments.FirstOrDefault(p => p.MetodoPago == MetodoPago.BITCOIN)?.Referencia ?? string.Empty;
            _currentPaymentMethod = creditRequest is not null
                ? "CREDITO"
                : ResolveAcceptedPaymentMethod(dialog.InitialPayments);
            _paymentValidated = true;
            RefreshPaymentState();
            ClosePaymentOverlay(save: false, focusAfterClose: _btnRegister);

            if (_presentationMode == PosPresentationMode.Quick && creditRequest is not null)
            {
                await RegisterCurrentSaleAsync();
            }
        }
        finally
        {
            _creditDialogOpen = false;
        }
    }

    private static string ResolveAcceptedPaymentMethod(IReadOnlyList<SalePaymentRequest> payments)
    {
        var methods = payments.Select(p => p.MetodoPago).Distinct().ToList();
        return methods.Count == 1
            ? methods[0].ToString()
            : "MIXTO";
    }

    private IReadOnlyList<SalePaymentRequest> BuildInitialCreditPaymentsFromOverlay()
    {
        var payments = new List<SalePaymentRequest>();
        var cash = GetMoneyCentavos(_numPaymentCashAmount);
        var transfer = GetMoneyCentavos(_numTransferAmount);
        var bitcoin = GetMoneyCentavos(_numBitcoinAmount);

        if (cash > 0)
        {
            payments.Add(new SalePaymentRequest(MetodoPago.EFECTIVO, cash));
        }

        if (transfer > 0)
        {
            payments.Add(new SalePaymentRequest(MetodoPago.TRANSFERENCIA, transfer, _txtTransferReference.Text.Trim()));
        }

        if (bitcoin > 0)
        {
            payments.Add(new SalePaymentRequest(MetodoPago.BITCOIN, bitcoin, _txtBitcoinReference.Text.Trim()));
        }

        return payments;
    }

    private IReadOnlyList<CreditSaleDialogLine> BuildCreditDialogLines()
    {
        var generalDiscount = GetMoneyCentavos(_numGeneralDiscount);
        var allocations = AllocateAmountAcrossBases(generalDiscount, _cart.Select(c => c.TotalCentavos).ToList());
        return _cart
            .Select((line, index) => new CreditSaleDialogLine(
                line.Product.ProductoId,
                line.Product.ProductoNombre,
                line.Product.CodigoSku,
                line.Quantity,
                line.Product.PrecioVentaEfectivoCentavos,
                line.SubtotalCentavos,
                line.DiscountCentavos,
                allocations[index],
                Math.Max(0, line.TotalCentavos - allocations[index]),
                line.Product.ProductoSkuId))
            .ToList();
    }

    private void ShowOtherMethodsOverlay()
    {
        if (_saleProcessing)
        {
            return;
        }

        if (_pnlPaymentOverlay.Visible && _activeOverlayPanel != PosOverlayPanel.OtherPayments)
        {
            return;
        }

        if (_cart.Count == 0)
        {
            MessageBox.Show("El carrito esta vacio.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetOverlayTitle("Otros métodos de pago");
        SetOverlayActionsVisible(true);
        if (!_canUseTransfer && !_canUseBitcoin && !_canRegisterCredit)
        {
            MessageBox.Show("No tiene permisos para otros metodos de pago.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _activeOverlayPanel = PosOverlayPanel.OtherPayments;
        ResetActiveOverlayActions();
        AddPaymentMethodButtons(includeCashButton: false);

        _numPaymentCashAmount.Value = CentavosToDollars(_acceptedCash);
        _numTransferAmount.Value = CentavosToDollars(_acceptedTransfer);
        _txtTransferReference.Text = _acceptedTransferRef;
        _numBitcoinAmount.Value = CentavosToDollars(_acceptedBitcoin);
        _txtBitcoinReference.Text = _acceptedBitcoinRef;

        _pnlPaymentMethodContent.Controls.Clear();
        foreach (Control control in _pnlPaymentMethodButtons.Controls)
        {
            if (control is Button button)
            {
                UITheme.StyleSecondaryButton(button);
            }
        }

        _pnlPaymentOverlay.Visible = true;
        _pnlPaymentOverlay.BringToFront();
        CenterPaymentModal();
        UpdateOverlayTotals();

        var selectedMethod = _currentPaymentMethod is "TRANSFERENCIA" or "BITCOIN" or "MIXTO" or "CREDITO"
            ? _currentPaymentMethod
            : "TRANSFERENCIA";
        if (!IsPaymentMethodAllowed(selectedMethod))
        {
            selectedMethod = _canUseTransfer
                ? "TRANSFERENCIA"
                : _canUseBitcoin ? "BITCOIN" : "CREDITO";
        }

        foreach (Control control in _pnlPaymentMethodButtons.Controls)
        {
            if (control is Button button && button.Tag is string methodKey && methodKey == selectedMethod)
            {
                SelectPaymentMethod(methodKey, button);
                break;
            }
        }
    }

    private void ShowDiscountOverlay()
    {
        if (_saleProcessing)
        {
            return;
        }

        if (_pnlPaymentOverlay.Visible && _activeOverlayPanel != PosOverlayPanel.Discounts)
        {
            return;
        }

        if (_cart.Count == 0)
        {
            MessageBox.Show("El carrito esta vacio.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetOverlayTitle("Descuentos");
        SetOverlayActionsVisible(false);
        if (!_canApplyLineDiscount && !_canApplyGeneralDiscount)
        {
            MessageBox.Show("No tiene permisos para aplicar descuentos.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _activeOverlayPanel = PosOverlayPanel.Discounts;
        ResetActiveOverlayActions();
        _numPaymentCashAmount.Value = CentavosToDollars(_acceptedCash);
        _numTransferAmount.Value = CentavosToDollars(_acceptedTransfer);
        _numBitcoinAmount.Value = CentavosToDollars(_acceptedBitcoin);
        _pnlPaymentMethodButtons.Controls.Clear();
        var discountButtons = new List<Control>();
        if (_canApplyLineDiscount)
        {
            discountButtons.Add(CreateOverlayCommandButton("Por producto", (_, _) => ShowLineDiscountEditor(), "Alt+L"));
        }

        if (_canApplyGeneralDiscount)
        {
            discountButtons.Add(CreateOverlayCommandButton("General", (_, _) => ShowGeneralDiscountEditor(), "Alt+G"));
        }

        _pnlPaymentMethodButtons.Controls.AddRange(discountButtons.ToArray());
        _pnlPaymentMethodContent.Controls.Clear();
        _pnlPaymentOverlay.Visible = true;
        _pnlPaymentOverlay.BringToFront();
        CenterPaymentModal();
        UpdateOverlayTotals();
        if (_canApplyGeneralDiscount)
        {
            ShowGeneralDiscountEditor();
        }
        else
        {
            ShowLineDiscountEditor();
        }
    }

    private Button CreateOverlayCommandButton(string text, EventHandler clickHandler, string? shortcut = null)
    {
        var button = new Button
        {
            Text = text,
            Width = 220,
            Height = 56,
            Margin = new Padding(5, 0, 5, 0),
            Font = UITheme.ButtonFont
        };
        UITheme.StyleSecondaryButton(button);
        button.Click += clickHandler;
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            _shortcutToolTip.SetToolTip(button, shortcut);
        }

        return button;
    }

    private void ShowLineDiscountEditor()
    {
        if (!_canApplyLineDiscount)
        {
            MessageBox.Show("No tiene permiso para aplicar descuentos por producto.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var line = GetSelectedCartLine(showMessage: false);
        if (line is null)
        {
            MessageBox.Show("Seleccione un producto de la venta para aplicar el descuento.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResetActiveOverlayActions();
        var amount = new NumericUpDown();
        ConfigureMoney(amount, 0, 0);
        amount.Value = CentavosToDollars(line.DiscountCentavos);
        _activeOverlayPrimaryControl = amount;

        var quickReason = new ComboBox();
        var manualReason = new TextBox();
        SetupQuickReasons(quickReason, manualReason);
        UITheme.StyleTextBox(manualReason);
        manualReason.Text = line.DiscountReason ?? string.Empty;

        var newSubtotal = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };

        void RefreshLineTotal()
        {
            var discount = DollarsToCentavos(amount.Value);
            var total = Math.Max(0, line.SubtotalCentavos - discount);
            newSubtotal.Text = $"Nuevo subtotal: ${FormatDollars(total)}";
        }

        amount.ValueChanged += (_, _) => RefreshLineTotal();
        RefreshLineTotal();

        var layout = CreateOverlayEditorLayout(rowCount: 7, fitCompactOverlay: true);
        layout.Controls.Add(CreateFieldLabel("Producto"), 0, 0);
        layout.Controls.Add(CreateReadOnlyValue(
            string.IsNullOrWhiteSpace(line.Product.VarianteNombre)
                ? line.Product.ProductoNombre
                : $"{line.Product.ProductoNombre} · {line.Product.VarianteNombre}"), 1, 0);
        layout.Controls.Add(CreateFieldLabel("Precio"), 0, 1);
        layout.Controls.Add(CreateReadOnlyValue($"${FormatDollars(line.Product.PrecioVentaEfectivoCentavos)}"), 1, 1);
        AddPaymentField(layout, 2, "Descuento", amount);
        AddPaymentField(layout, 3, "Motivo rapido", quickReason);
        AddPaymentField(layout, 4, "Motivo manual", manualReason);
        layout.Controls.Add(newSubtotal, 1, 5);

        var actions = CreateEditorActions(
            apply: (_, _) =>
            {
                var discount = DollarsToCentavos(amount.Value);
                var reason = manualReason.Text.Trim();
                if (discount > line.SubtotalCentavos)
                {
                    MessageBox.Show("El descuento no puede superar el subtotal del producto.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (discount > 0 && string.IsNullOrWhiteSpace(reason))
                {
                    MessageBox.Show("Ingrese un motivo para aplicar descuento.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                line.DiscountCentavos = discount;
                line.DiscountReason = reason;
                InvalidateAcceptedPayment();
                RefreshCart();
                ClosePaymentOverlay(
                    save: false,
                    focusAfterClose: _presentationMode == PosPresentationMode.Quick ? _txtBarcode : _numCashReceived);
            },
            cancel: (_, _) => ClosePaymentOverlay(
                save: false,
                focusAfterClose: _presentationMode == PosPresentationMode.Quick ? _txtBarcode : _txtSearch));
        layout.Controls.Add(actions, 0, 6);
        layout.SetColumnSpan(actions, 2);

        _pnlPaymentMethodContent.Controls.Clear();
        _pnlPaymentMethodContent.Controls.Add(layout);
        FocusOverlayPrimaryControl();
    }

    private void ShowGeneralDiscountEditor()
    {
        if (!_canApplyGeneralDiscount)
        {
            MessageBox.Show("No tiene permiso para aplicar descuento general.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ResetActiveOverlayActions();
        var amount = new NumericUpDown();
        ConfigureMoney(amount, 0, 0);
        amount.Value = _numGeneralDiscount.Value;
        _activeOverlayPrimaryControl = amount;

        var quickReason = new ComboBox();
        var manualReason = new TextBox();
        SetupQuickReasons(quickReason, manualReason);
        UITheme.StyleTextBox(manualReason);
        manualReason.Text = _txtGeneralDiscountReason.Text;

        var totalAfterDiscount = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };

        void RefreshGeneralTotal()
        {
            var subtotal = _cart.Sum(c => c.SubtotalCentavos);
            var lineDiscounts = _cart.Sum(c => c.DiscountCentavos);
            var generalDiscount = DollarsToCentavos(amount.Value);
            var total = CalculateFinalTotalCentavos(subtotal, lineDiscounts, generalDiscount);
            totalAfterDiscount.Text = $"Total con descuento: ${FormatDollars(total)}";
        }

        amount.ValueChanged += (_, _) => RefreshGeneralTotal();
        RefreshGeneralTotal();

        var layout = CreateOverlayEditorLayout(rowCount: 6);
        layout.Controls.Add(CreateFieldLabel("Subtotal"), 0, 0);
        layout.Controls.Add(CreateReadOnlyValue($"${FormatDollars(_cart.Sum(c => c.SubtotalCentavos))}"), 1, 0);
        AddPaymentField(layout, 1, "Descuento general", amount);
        AddPaymentField(layout, 2, "Motivo rapido", quickReason);
        AddPaymentField(layout, 3, "Motivo manual", manualReason);
        layout.Controls.Add(totalAfterDiscount, 1, 4);

        var actions = CreateEditorActions(
            apply: (_, _) =>
            {
                var discount = DollarsToCentavos(amount.Value);
                var reason = manualReason.Text.Trim();
                var subtotal = _cart.Sum(c => c.SubtotalCentavos);
                var lineDiscounts = _cart.Sum(c => c.DiscountCentavos);
                var maxGeneralDiscount = Math.Max(0, subtotal - lineDiscounts);
                if (discount > maxGeneralDiscount)
                {
                    MessageBox.Show("El descuento general no puede superar el total disponible.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (discount > 0 && string.IsNullOrWhiteSpace(reason))
                {
                    MessageBox.Show("Ingrese un motivo para aplicar descuento.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _numGeneralDiscount.Value = amount.Value;
                _txtGeneralDiscountReason.Text = reason;
                InvalidateAcceptedPayment();
                RecalculateTotals();
                ClosePaymentOverlay(
                    save: false,
                    focusAfterClose: _presentationMode == PosPresentationMode.Quick ? _txtBarcode : _numCashReceived);
            },
            cancel: (_, _) => ClosePaymentOverlay(
                save: false,
                focusAfterClose: _presentationMode == PosPresentationMode.Quick ? _txtBarcode : _txtSearch));
        layout.Controls.Add(actions, 0, 5);
        layout.SetColumnSpan(actions, 2);

        _pnlPaymentMethodContent.Controls.Clear();
        _pnlPaymentMethodContent.Controls.Add(layout);
        FocusOverlayPrimaryControl();
    }

    private void HidePaymentOverlay(bool save)
    {
        _pnlPaymentOverlay.Visible = false;
        ResetOverlayContext();
        if (!save)
        {
            return;
        }

        _acceptedCash = GetMoneyCentavos(_numPaymentCashAmount);
        _acceptedTransfer = GetMoneyCentavos(_numTransferAmount);
        _acceptedTransferRef = _acceptedTransfer > 0 ? _txtTransferReference.Text.Trim() : string.Empty;
        _acceptedBitcoin = GetMoneyCentavos(_numBitcoinAmount);
        _acceptedBitcoinRef = _acceptedBitcoin > 0 ? _txtBitcoinReference.Text.Trim() : string.Empty;
        _paymentValidated = true;
        RefreshPaymentState();
    }

    private void ClosePaymentOverlay(bool save, Control? focusAfterClose)
    {
        HidePaymentOverlay(save);
        focusAfterClose?.BeginInvoke(new Action(() =>
        {
            if (!focusAfterClose.IsDisposed && focusAfterClose.CanFocus)
            {
                focusAfterClose.Focus();
            }
        }));
    }

    private void ValidateAndAcceptPayment()
    {
        CommitMoneyInput(_numPaymentCashAmount);
        CommitMoneyInput(_numTransferAmount);
        CommitMoneyInput(_numBitcoinAmount);

        var cash = GetMoneyCentavos(_numPaymentCashAmount);
        var transfer = GetMoneyCentavos(_numTransferAmount);
        var bitcoin = GetMoneyCentavos(_numBitcoinAmount);
        var totalFinal = GetCurrentTotalCentavos();
        var totalPaid = cash + transfer + bitcoin;
        var missing = Math.Max(0, totalFinal - totalPaid);
        var overpay = Math.Max(0, totalPaid - totalFinal);

        if (totalFinal <= 0)
        {
            MessageBox.Show("El total de la venta debe ser mayor que cero.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (totalPaid <= 0)
        {
            MessageBox.Show("Ingrese un monto de pago.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (missing > 0)
        {
            MessageBox.Show("El monto ingresado no cubre el total de la venta.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (overpay > cash)
        {
            MessageBox.Show("Solo el pago en efectivo puede generar vuelto.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (transfer > 0 && !_canUseTransfer)
        {
            MessageBox.Show("No tiene permiso para usar transferencia.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (transfer > 0 && string.IsNullOrWhiteSpace(_txtTransferReference.Text))
        {
            MessageBox.Show("Ingrese la referencia de transferencia.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtTransferReference.Focus();
            return;
        }

        if (bitcoin > 0 && !_canUseBitcoin)
        {
            MessageBox.Show("No tiene permiso para usar Bitcoin.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (bitcoin > 0 && string.IsNullOrWhiteSpace(_txtBitcoinReference.Text))
        {
            MessageBox.Show("Ingrese la referencia de Bitcoin.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtBitcoinReference.Focus();
            return;
        }

        ClosePaymentOverlay(save: true, focusAfterClose: _btnRegister);
    }

    private void UpdateOverlayTotals()
    {
        var totalFinal = GetCurrentTotalCentavos();
        var cash = GetMoneyCentavos(_numPaymentCashAmount);
        var transfer = GetMoneyCentavos(_numTransferAmount);
        var bitcoin = GetMoneyCentavos(_numBitcoinAmount);
        var totalPaid = cash + transfer + bitcoin;
        var missing = Math.Max(0, totalFinal - totalPaid);
        var overpay = Math.Max(0, totalPaid - totalFinal);
        var invalidChange = overpay > cash;
        var change = invalidChange ? 0 : overpay;

        _lblOverlayTotalToPay.Text = $"${FormatDollars(totalFinal)}";
        _lblOverlayPaid.Text = $"${FormatDollars(totalPaid)}";
        _lblOverlayMissing.Text = $"${FormatDollars(missing)}";
        _lblOverlayChange.Text = invalidChange ? "No valido" : $"${FormatDollars(change)}";
        _lblOverlayChange.ForeColor = invalidChange ? UITheme.DangerColor : UITheme.SuccessColor;
        _lblQuickCheckoutChange.Text = invalidChange ? "No válido" : $"${FormatDollars(change)}";
        _lblQuickCheckoutChange.ForeColor = invalidChange ? UITheme.DangerColor : UITheme.SuccessColor;
    }

    private void ApplyActiveDiscount()
    {
        if (_overlayActionProcessing || _activeOverlayApplyButton is null)
        {
            return;
        }

        _activeOverlayApplyButton.PerformClick();
    }

    private void AcceptActiveOverlayPayment()
    {
        if (_overlayActionProcessing)
        {
            return;
        }

        RunSingleOverlayAction(ValidateAndAcceptPayment);
    }

    private void RunSingleOverlayAction(Action action)
    {
        if (_overlayActionProcessing)
        {
            return;
        }

        _overlayActionProcessing = true;
        var applyWasEnabled = _activeOverlayApplyButton?.Enabled;
        var acceptWasEnabled = _btnAcceptPayment.Enabled;
        try
        {
            if (_activeOverlayApplyButton is not null)
            {
                _activeOverlayApplyButton.Enabled = false;
            }

            _btnAcceptPayment.Enabled = false;
            action();
        }
        finally
        {
            _overlayActionProcessing = false;
            if (_activeOverlayApplyButton is not null && applyWasEnabled.HasValue)
            {
                _activeOverlayApplyButton.Enabled = applyWasEnabled.Value;
            }

            if (!_btnAcceptPayment.IsDisposed)
            {
                _btnAcceptPayment.Enabled = acceptWasEnabled;
            }
        }
    }

    private void ResetActiveOverlayActions()
    {
        _activeOverlayApplyButton = null;
        _activeOverlayCancelButton = null;
        _activeOverlayPrimaryControl = null;
    }

    private void ResetOverlayContext()
    {
        _activeOverlayPanel = PosOverlayPanel.None;
        _overlayActionProcessing = false;
        ResetActiveOverlayActions();
    }

    private void FocusOverlayPrimaryControl()
    {
        var control = _activeOverlayPrimaryControl;
        if (control is null || control.IsDisposed)
        {
            return;
        }

        control.BeginInvoke(new Action(() =>
        {
            if (!control.IsDisposed && control.CanFocus)
            {
                control.Focus();
            }
        }));
    }

    private async Task LoadFilterOptionsAsync()
    {
        _loadingFilterOptions = true;
        try
        {
            var categories = await _categoryService.GetActiveAsync() ?? [];
            _cmbCategoryFilter.DisplayMember = nameof(CategoryFilterOption.Name);
            _cmbCategoryFilter.ValueMember = nameof(CategoryFilterOption.CategoriaId);
            _cmbCategoryFilter.DataSource = categories
                .Select(c => new CategoryFilterOption(
                    c.CategoriaId,
                    c.Nombre,
                    IsProtectedCategory(c.Nombre)))
                .Prepend(new CategoryFilterOption(null, "Todas", false))
                .ToList();

            _cmbStockFilter.DisplayMember = nameof(StockFilterOption.Name);
            _cmbStockFilter.ValueMember = nameof(StockFilterOption.Filter);
            _cmbStockFilter.DataSource = new List<StockFilterOption>
            {
                new StockFilterOption(ProductStockFilter.All, "Todos"),
                new StockFilterOption(ProductStockFilter.InStock, "Con stock"),
                new StockFilterOption(ProductStockFilter.LowStock, "Bajo stock"),
                new StockFilterOption(ProductStockFilter.NoStock, "Sin stock")
            };

            _cmbLocationFilter.DisplayMember = nameof(LocationFilterOption.Name);
            _cmbLocationFilter.ValueMember = nameof(LocationFilterOption.Filter);
            _cmbLocationFilter.DataSource = new List<LocationFilterOption>
            {
                new(ProductLocationFilter.All, "Todos los productos"),
                new(ProductLocationFilter.Exhibition, "Productos en exhibición"),
                new(ProductLocationFilter.Warehouse, "Productos en bodega")
            };

            _cmbSort.DisplayMember = nameof(ProductSortOption.Name);
            _cmbSort.ValueMember = nameof(ProductSortOption.Sort);
            _cmbSort.DataSource = new List<ProductSortOption>
            {
                new ProductSortOption(ProductSort.NameAscending, "Nombre A-Z"),
                new ProductSortOption(ProductSort.PriceAscending, "Precio menor a mayor"),
                new ProductSortOption(ProductSort.PriceDescending, "Precio mayor a menor"),
                new ProductSortOption(ProductSort.StockAscending, "Stock menor a mayor")
            };

            await LoadSubcategoryOptionsAsync(GetSelectedCategoryId());
        }
        finally
        {
            _loadingFilterOptions = false;
        }
    }

    private async void CategoryFilterChanged(object? sender, EventArgs e)
    {
        if (_loadingFilterOptions)
        {
            return;
        }

        await LoadSubcategoryOptionsAsync(GetSelectedCategoryId());
        RestartSearchDebounce();
    }

    private async Task LoadSubcategoryOptionsAsync(int? categoryId)
    {
        _loadingFilterOptions = true;
        try
        {
            var category = _cmbCategoryFilter.SelectedItem as CategoryFilterOption;
            var enabled = categoryId.HasValue && category?.IsProtected != true;
            IReadOnlyList<SubcategorySummary> subcategories = [];
            if (enabled && _subcategoryService is not null)
            {
                subcategories = await _subcategoryService.GetByCategoryAsync(
                    categoryId!.Value,
                    soloActivas: true);
            }

            var options = new List<SubcategoryFilterOption>
            {
                new(PosSubcategoryFilter.Todas, "Todas las subcategorías")
            };
            if (enabled)
            {
                options.AddRange(subcategories.Select(subcategory =>
                    new SubcategoryFilterOption(
                        PosSubcategoryFilter.Concreta(subcategory.SubcategoriaId),
                        subcategory.Nombre)));
                options.Add(new SubcategoryFilterOption(
                    PosSubcategoryFilter.SinSubcategoria,
                    "Sin subcategoría"));
            }

            _cmbSubcategoryFilter.DisplayMember = nameof(SubcategoryFilterOption.Name);
            _cmbSubcategoryFilter.ValueMember = nameof(SubcategoryFilterOption.Filter);
            _cmbSubcategoryFilter.DataSource = options;
            _cmbSubcategoryFilter.Enabled = enabled;
            if (_cmbSubcategoryFilter.Items.Count > 0)
            {
                _cmbSubcategoryFilter.SelectedIndex = 0;
            }
        }
        finally
        {
            _loadingFilterOptions = false;
        }
    }

    private async Task RefreshCashAndProductsAsync()
    {
        await RefreshCashStatusAsync();
        await RefreshProductsAsync();
    }

    private async Task RefreshCashStatusAsync()
    {
        var summary = await _cashService.GetTodaySummaryAsync();
        _cashOpen = summary.Estado == EstadoCaja.ABIERTA;
        _lblCashStatus.Text = _cashOpen
            ? "Caja: ABIERTA"
            : "Caja: CERRADA";

        _lblCashStatus.BackColor = _cashOpen ? UITheme.SuccessColor : UITheme.DangerColor;
        _lblCashStatus.ForeColor = Color.White;
        _lblCashStatus.Padding = new Padding(10, 5, 10, 5);

        ValidateRegisterButton();
    }

    private async Task RefreshProductsAsync(
        bool renderCatalog = true,
        bool forceRender = true,
        bool resetPage = true)
    {
        await _productsRefreshGate.WaitAsync();
        try
        {
            _allProducts = (await _posCatalogService.SearchAsync(new PosSkuCatalogFilter(
                Estado: EstadoProducto.ACTIVO,
                SkuActivo: true))).ToList();
            SyncCartWithLatestProducts();
            SyncAllDraftsWithLatestProducts();

            if (renderCatalog)
            {
                await ApplyProductFilterAsync(forceRender: forceRender, resetPage: resetPage);
            }
        }
        finally
        {
            _productsRefreshGate.Release();
        }
    }

    private void SyncCartWithLatestProducts()
    {
        if (_cart.Count == 0 || _allProducts.Count == 0)
        {
            return;
        }

        var productsById = _allProducts.ToDictionary(p => p.ProductoSkuId);
        foreach (var line in _cart)
        {
            if (productsById.TryGetValue(line.Product.ProductoSkuId, out var latestProduct))
            {
                line.Product = latestProduct;
            }
        }

        RecalculateTotals();
    }

    private void SyncAllDraftsWithLatestProducts()
    {
        if (_allProducts.Count == 0)
        {
            return;
        }

        var productsById = _allProducts.ToDictionary(product => product.ProductoSkuId);
        foreach (var draft in _draftCoordinator.Drafts)
        {
            foreach (var line in draft.Lines)
            {
                if (productsById.TryGetValue(line.Product.ProductoSkuId, out var latestProduct))
                {
                    line.Product = latestProduct;
                }
            }
        }
    }

    private bool ValidateCartStock(bool showMessage)
    {
        if (_cart.Count == 0)
        {
            return false;
        }

        var productsById = _allProducts.ToDictionary(p => p.ProductoSkuId);
        foreach (var line in _cart)
        {
            if (!productsById.TryGetValue(line.Product.ProductoSkuId, out var latestProduct) ||
                latestProduct.Estado != EstadoProducto.ACTIVO ||
                !latestProduct.SkuActivo ||
                latestProduct.StockDisponibleExhibicion <= 0)
            {
                if (showMessage)
                {
                    MessageBox.Show(
                        $"El SKU '{line.Product.CodigoSku}' ya no tiene stock disponible en Exhibición. Refresque el catálogo.",
                        "Punto de venta",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return false;
            }

            line.Product = latestProduct;
            if (line.Quantity > latestProduct.StockDisponibleExhibicion)
            {
                if (showMessage)
                {
                    MessageBox.Show(
                        $"Stock insuficiente en Exhibición para '{latestProduct.ProductoNombre}' ({latestProduct.CodigoSku}). " +
                        $"Disponible: {latestProduct.StockDisponibleExhibicion}. Bodega: {latestProduct.StockDisponibleBodega}.",
                        "Punto de venta",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return false;
            }
        }

        return true;
    }

    private void ApplySoldStockToVisibleCatalog(SaleResult sale)
    {
        var soldQuantities = sale.Lines
            .Where(line => line.ProductoSkuId.HasValue)
            .GroupBy(l => l.ProductoSkuId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Cantidad));

        if (soldQuantities.Count == 0)
        {
            return;
        }

        _allProducts = ApplyStockDelta(_allProducts, soldQuantities);
        _products = ApplyStockDelta(_products, soldQuantities);
        SyncAllDraftsWithLatestProducts();
        UpdateVisibleProductCards(soldQuantities.Keys);
        _lastRenderedProductsKey = BuildRenderKey(GetCurrentPageProducts());
        UpdatePaginationControls();
    }

    private static List<PosSkuSaleItem> ApplyStockDelta(
        IReadOnlyList<PosSkuSaleItem> products,
        IReadOnlyDictionary<int, int> soldQuantities)
    {
        return products
            .Select(product => soldQuantities.TryGetValue(product.ProductoSkuId, out var soldQuantity)
                ? product with
                {
                    StockFisicoExhibicion = Math.Max(0, product.StockFisicoExhibicion - soldQuantity)
                }
                : product)
            .ToList();
    }

    private void UpdateVisibleProductCards(IEnumerable<int> skuIds)
    {
        var skuIdSet = skuIds.ToHashSet();
        if (skuIdSet.Count == 0)
        {
            return;
        }

        var currentProductsById = _products.ToDictionary(p => p.ProductoSkuId);
        foreach (var card in _productsPanel.Controls.OfType<PosSkuCardControl>())
        {
            if (skuIdSet.Contains(card.ProductoSkuId) &&
                currentProductsById.TryGetValue(card.ProductoSkuId, out var product))
            {
                card.UpdateItem(product);
            }
        }
    }

    private async Task RefreshCatalogAfterCompletedSaleAsync(string numeroVenta)
    {
        var refreshWatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _lblResultsInfo.Text = "Actualizando inventario...";
            await RefreshCashStatusAsync();
            await RefreshProductsAsync(renderCatalog: true, forceRender: false, resetPage: false);
            LogPosPerformance($"Venta {numeroVenta}: background-catalog-refresh={refreshWatch.ElapsedMilliseconds}ms.");
        }
        catch (Exception ex)
        {
            LogPosPerformance($"Venta {numeroVenta}: background-catalog-refresh failed after {refreshWatch.ElapsedMilliseconds}ms. {ex}");
        }
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        var product = _products.FirstOrDefault();
        if (product is null)
        {
            MessageBox.Show("No hay producto seleccionado.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AddProductToCart(product);
    }

    private bool AddProductToCart(PosSkuSaleItem product, bool showMessage = true)
    {
        var existing = _cart.FirstOrDefault(c => c.Product.ProductoSkuId == product.ProductoSkuId);
        if (existing is not null)
        {
            existing.Product = product;
            if (existing.Quantity + 1 > product.StockDisponibleExhibicion)
            {
                if (showMessage)
                {
                    MessageBox.Show("No hay stock suficiente para agregar otra unidad.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }

            existing.Quantity++;
        }
        else
        {
            if (!product.DisponibleParaVenta)
            {
                var message = product.StockDisponibleBodega > 0
                    ? $"El SKU no tiene stock disponible en Exhibición. Hay {product.StockDisponibleBodega} en Bodega; debe transferirse antes de vender."
                    : "El SKU no tiene stock disponible en Exhibición.";
                if (showMessage)
                {
                    MessageBox.Show(message, "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }

            _cart.Add(new SaleDraftLine(product));
        }

        InvalidateAcceptedPayment();
        RefreshCart();
        return true;
    }

    private void BtnUpdateLine_Click(object? sender, EventArgs e)
    {
        var line = GetSelectedCartLine();
        if (line is null)
        {
            return;
        }

        var quantity = (int)_numQuantity.Value;
        if (quantity > line.Product.StockDisponibleExhibicion)
        {
            MessageBox.Show("La cantidad supera el stock actual.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_numLineDiscount.Value > 0 && !_canApplyLineDiscount)
        {
            MessageBox.Show("No tiene permiso para aplicar descuentos por producto.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        line.Quantity = quantity;
        line.DiscountCentavos = DollarsToCentavos(_numLineDiscount.Value);
        line.DiscountReason = _txtLineDiscountReason.Text;

        InvalidateAcceptedPayment();
        RefreshCart();
    }

    private void BtnRemove_Click(object? sender, EventArgs e)
    {
        var line = GetSelectedCartLine();
        if (line is null)
        {
            return;
        }

        _cart.Remove(line);
        InvalidateAcceptedPayment();
        RefreshCart();
    }

    private async void BtnRegister_Click(object? sender, EventArgs e)
    {
        await RegisterCurrentSaleAsync();
    }

    private async Task RegisterCurrentSaleAsync()
    {
        SaleResult? result = null;
        var totalWatch = System.Diagnostics.Stopwatch.StartNew();
        var stockRefreshMs = 0L;
        var saveMs = 0L;
        var cleanupMs = 0L;
        var localStockUpdateMs = 0L;
        var receiptBuildMs = 0L;
        var saleSaved = false;

        if (_saleProcessing)
        {
            return;
        }

        _saleProcessing = true;
        SetSaleProcessingState(processing: true);

        try
        {
            try
            {
                await RefreshCashStatusAsync();
                if (!_cashOpen)
                {
                    MessageBox.Show("No hay caja abierta. Abra caja antes de registrar ventas.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_cart.Count == 0)
                {
                    MessageBox.Show("El carrito esta vacio.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var stockRefreshWatch = System.Diagnostics.Stopwatch.StartNew();
                await RefreshProductsAsync(renderCatalog: false);
                stockRefreshMs = stockRefreshWatch.ElapsedMilliseconds;
                if (!ValidateCartStock(showMessage: true))
                {
                    RecalculateTotals();
                    return;
                }

                var paymentValidationMessage = "Complete un pago valido antes de registrar la venta.";
                if (!AcceptedPaymentCoversCurrentTotal(out paymentValidationMessage))
                {
                    _paymentValidated = false;
                    RecalculateTotals();
                    MessageBox.Show(paymentValidationMessage, "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var payments = BuildSalePayments();
                var request = new RegisterSaleRequest(
                    Lines: _cart.Select(c => new SaleLineRequest(
                        c.Product.ProductoId,
                        c.Quantity,
                        c.DiscountCentavos,
                        c.DiscountReason,
                        c.Product.ProductoSkuId)).ToList(),
                    Payments: payments,
                    DescuentoGeneralCentavos: GetMoneyCentavos(_numGeneralDiscount),
                    DescuentoGeneralMotivo: _txtGeneralDiscountReason.Text,
                    ClienteId: _selectedCustomerId,
                    Credit: _pendingCreditRequest);

                var saveWatch = System.Diagnostics.Stopwatch.StartNew();
                result = await _saleService.RegisterSaleAsync(request);
                saleSaved = true;
                saveMs = saveWatch.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                LogPosSaleFailure(ex);
                var message = ex is DbUpdateException or DbUpdateConcurrencyException
                    ? "No se pudo registrar la venta. No se realizaron cambios. Cierre y vuelva a abrir el punto de venta antes de intentarlo nuevamente."
                    : ex.Message;
                MessageBox.Show(message, "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var receiptBuildWatch = System.Diagnostics.Stopwatch.StartNew();
            var cashierName = _currentSession.UsuarioActual?.NombreCompleto
                ?? _currentSession.UsuarioActual?.NombreUsuario
                ?? $"Usuario {result.UsuarioId}";
            var canonicalReceipt = ReceiptDataFactory.FromSaleResult(
                    result,
                    new ReceiptBusinessData(UITheme.BusinessName),
                    cashierName);
            if (_receiptDocuments is not null)
            {
                try
                {
                    canonicalReceipt = await _receiptDocuments.BuildCurrentAsync(result, cashierName);
                }
                catch (Exception ex)
                {
                    LogPosPerformance(
                        $"Venta {result.NumeroVenta}: no se pudo completar el perfil del ticket; se uso fallback local. {ex.GetType().Name}: {ex.Message}");
                }
            }
            var receipt = SaleReceiptViewModel.FromReceiptData(canonicalReceipt);
            receiptBuildMs = receiptBuildWatch.ElapsedMilliseconds;

            var cleanupWatch = System.Diagnostics.Stopwatch.StartNew();
            ClearCart();
            cleanupMs = cleanupWatch.ElapsedMilliseconds;

            var localStockWatch = System.Diagnostics.Stopwatch.StartNew();
            ApplySoldStockToVisibleCatalog(result);
            localStockUpdateMs = localStockWatch.ElapsedMilliseconds;

            if (_peripheralOutput is not null)
            {
                try
                {
                    var ticketOutput = await _peripheralOutput.TryPrintReceiptAsync(canonicalReceipt);
                    if (ticketOutput.Status.Availability == PeripheralAvailability.Unavailable)
                        MessageBox.Show(ticketOutput.Message, "Venta completada", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    if (result.Payments.Any(payment => payment.MetodoPago == MetodoPago.EFECTIVO))
                    {
                        var drawerOutput = await _peripheralOutput.TryOpenCashDrawerAsync();
                        if (drawerOutput.Status.Availability == PeripheralAvailability.Unavailable)
                            MessageBox.Show(drawerOutput.Message, "Venta completada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    LogPosPerformance(
                        $"Venta {result.NumeroVenta}: salida de perifericos contenida. {ex.GetType().Name}: {ex.Message}");
                    MessageBox.Show(
                        "La venta fue registrada correctamente, pero no se pudo completar una salida de periférico.",
                        "Venta completada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            var receiptOpenDelayMs = totalWatch.ElapsedMilliseconds;

            try
            {
                var receiptFormBuildWatch = System.Diagnostics.Stopwatch.StartNew();
                using var receiptForm = new SaleReceiptForm(
                    receipt,
                    isCompletedSale: true,
                    primaryButtonText: "Nueva venta");
                var receiptFormBuildMs = receiptFormBuildWatch.ElapsedMilliseconds;
                LogPosPerformance(
                    $"Venta {result.NumeroVenta}: stock-check-refresh={stockRefreshMs}ms, save={saveMs}ms, snapshot={receiptBuildMs}ms, clear-pos={cleanupMs}ms, local-stock-update={localStockUpdateMs}ms, receipt-form-build={receiptFormBuildMs}ms, receipt-open-delay={receiptOpenDelayMs + receiptFormBuildMs}ms.");
                receiptForm.Shown += (_, _) => _ = RefreshCatalogAfterCompletedSaleAsync(result.NumeroVenta);
                receiptForm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Venta {result.NumeroVenta} registrada correctamente, pero no se pudo abrir Venta completada.\n\nPuede consultar o reimprimir el ticket desde Historial de ventas.\n\n{ex.Message}",
                    "Venta completada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _ = RefreshCatalogAfterCompletedSaleAsync(result.NumeroVenta);
            }
        }
        finally
        {
            _saleProcessing = false;
            SetSaleProcessingState(processing: false);
            RefreshPaymentState();
            if (saleSaved)
            {
                FocusBarcodeBox();
            }
        }
    }

    private IReadOnlyList<SalePaymentRequest> BuildSalePayments()
    {
        if (!AcceptedPaymentCoversCurrentTotal(out var paymentValidationMessage))
        {
            throw new InvalidOperationException(paymentValidationMessage);
        }

        var payments = new List<SalePaymentRequest>();
        var payment = GetCurrentPaymentSnapshot();
        var cash = payment.CashCentavos;
        var transfer = payment.TransferCentavos;
        var bitcoin = payment.BitcoinCentavos;

        if (cash > 0)
        {
            payments.Add(new SalePaymentRequest(MetodoPago.EFECTIVO, cash));
        }

        if (transfer > 0)
        {
            payments.Add(new SalePaymentRequest(MetodoPago.TRANSFERENCIA, transfer, payment.TransferReference));
        }

        if (bitcoin > 0)
        {
            payments.Add(new SalePaymentRequest(MetodoPago.BITCOIN, bitcoin, payment.BitcoinReference));
        }

        if (payments.Count == 0 && _pendingCreditRequest is null)
        {
            throw new InvalidOperationException("Debe registrar al menos un pago mayor que cero.");
        }

        return payments;
    }

    private void RefreshCart()
    {
        _cartBinding.DataSource = _cart
            .Select(CartRow.FromLine)
            .ToList();
        RefreshCartStockIndicators();
        if (_cart.Count == 0)
        {
            _lblSelectedLine.Text = "Seleccione un producto de la venta";
        }

        RecalculateTotals();
    }

    private void RefreshCartStockIndicators()
    {
        var productsById = _allProducts.ToDictionary(product => product.ProductoSkuId);
        foreach (DataGridViewRow row in _gridCart.Rows)
        {
            if (row.DataBoundItem is not CartRow cartRow)
            {
                continue;
            }

            var insufficient = !productsById.TryGetValue(cartRow.ProductoSkuId, out var product) ||
                product.Estado != EstadoProducto.ACTIVO ||
                !product.SkuActivo ||
                cartRow.Cantidad > product.StockDisponibleExhibicion;
            row.ErrorText = insufficient
                ? "Stock insuficiente en Exhibición. Ajuste la cantidad antes de cobrar."
                : string.Empty;
            row.DefaultCellStyle.BackColor = insufficient
                ? UITheme.SoftPinkColor
                : UITheme.SurfaceColor;
            row.DefaultCellStyle.ForeColor = insufficient
                ? UITheme.DangerColor
                : UITheme.TextDark;
        }
    }

    private void RecalculateTotals()
    {
        RefreshPaymentState();
    }

    private void RefreshPaymentState()
    {
        var subtotal = _cart.Sum(c => c.SubtotalCentavos);
        var lineDiscounts = _cart.Sum(c => c.DiscountCentavos);
        var generalDiscount = GetMoneyCentavos(_numGeneralDiscount);
        var discounts = lineDiscounts + generalDiscount;
        var totalFinal = CalculateFinalTotalCentavos(subtotal, lineDiscounts, generalDiscount);

        ApplyDefaultExactCashIfNeeded(totalFinal);

        var payment = GetCurrentPaymentSnapshot(totalFinal);
        if (_currentPaymentMethod == "EFECTIVO")
        {
            _acceptedCash = payment.CashCentavos;
        }

        _lblSubtotal.Text = $"Subtotal: ${FormatDollars(subtotal)}";
        _lblDiscounts.Text = $"Desc.: ${FormatDollars(discounts)}";
        _lblPaid.Text = $"Pagado: ${FormatDollars(payment.TotalPaidCentavos)}";
        _lblTotal.Text = $"TOTAL A PAGAR: ${FormatDollars(totalFinal)}";
        _lblMissing.Text = $"Faltante: ${FormatDollars(payment.MissingCentavos)}";
        _lblChange.Text = payment.InvalidChange
            ? "Vuelto: no valido"
            : $"Vuelto: ${FormatDollars(payment.ChangeCentavos)}";

        _paymentValidated = _pendingCreditRequest is not null
            ? payment.TotalCentavos > 0 && payment.TotalPaidCentavos <= payment.TotalCentavos
            : payment.IsComplete;

        _lblQuickCashChange.Text = $"${FormatDollars(payment.ChangeCentavos)}";
        UpdatePaymentMethodDisplay();
        ValidateRegisterButton();
    }

    private void InvalidateAcceptedPayment()
    {
        if (_currentPaymentMethod == "EFECTIVO")
        {
            _pendingCreditRequest = null;
            RefreshPaymentState();
            return;
        }

        if (!_paymentValidated &&
            _acceptedCash == 0 &&
            _acceptedTransfer == 0 &&
            _acceptedBitcoin == 0)
        {
            return;
        }

        _paymentValidated = false;
        _acceptedCash = 0;
        _acceptedTransfer = 0;
        _acceptedTransferRef = string.Empty;
        _acceptedBitcoin = 0;
        _acceptedBitcoinRef = string.Empty;
        _pendingCreditRequest = null;
        UpdatePaymentMethodDisplay();
        ValidateRegisterButton();
    }

    private void QuickCashPaymentChanged()
    {
        if (_settingCashReceived || _restoringDraftState)
        {
            return;
        }

        _cashReceivedEditedManually = true;
        ReturnToDefaultCashPayment();
    }

    private void ReturnToDefaultCashPayment()
    {
        _currentPaymentMethod = "EFECTIVO";
        _pendingCreditRequest = null;
        _acceptedTransfer = 0;
        _acceptedTransferRef = string.Empty;
        _acceptedBitcoin = 0;
        _acceptedBitcoinRef = string.Empty;
        _numTransferAmount.Value = 0;
        _txtTransferReference.Clear();
        _numBitcoinAmount.Value = 0;
        _txtBitcoinReference.Clear();
        RefreshPaymentState();
    }

    private void ApplyDefaultExactCashIfNeeded(long totalFinal)
    {
        if (_currentPaymentMethod != "EFECTIVO" ||
            totalFinal <= 0 ||
            _cashReceivedEditedManually)
        {
            return;
        }

        SetCashReceivedCentavos(totalFinal, markAsManual: false);
    }

    private void SetCashReceivedCentavos(long centavos, bool markAsManual)
    {
        var dollars = Math.Clamp(
            CentavosToDollars(Math.Max(0, centavos)),
            _numCashReceived.Minimum,
            _numCashReceived.Maximum);

        _settingCashReceived = true;
        try
        {
            _numCashReceived.Value = dollars;
        }
        finally
        {
            _settingCashReceived = false;
        }

        _cashReceivedEditedManually = markAsManual;
        _acceptedCash = DollarsToCentavos(dollars);
        UpdatePaymentMethodDisplay();
    }

    private void ValidateRegisterButton()
    {
        _btnRegister.Enabled =
            !_saleProcessing &&
            _canRegisterSale &&
            _cashOpen &&
            _cart.Count > 0 &&
            ValidateCartStock(showMessage: false) &&
            GetCurrentTotalCentavos() > 0 &&
            AcceptedPaymentCoversCurrentTotal(out _);
    }

    private bool AcceptedPaymentCoversCurrentTotal(out string message)
    {
        var payment = GetCurrentPaymentSnapshot();

        if (payment.TotalCentavos <= 0)
        {
            message = "El total de la venta debe ser mayor que cero.";
            return false;
        }

        if (payment.TotalPaidCentavos <= 0)
        {
            if (_pendingCreditRequest is null)
            {
                message = "Complete un pago valido antes de registrar la venta.";
                return false;
            }
        }

        if (_pendingCreditRequest is not null)
        {
            if (payment.TotalPaidCentavos > payment.TotalCentavos)
            {
                message = "El pago inicial no puede superar el total de la venta.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        if (payment.MissingCentavos > 0)
        {
            message = "El pago aceptado ya no cubre el total actual de la venta.";
            return false;
        }

        if (payment.InvalidChange)
        {
            message = "El vuelto solo puede generarse desde un pago en efectivo.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool IsPaymentMethodAllowed(string methodKey)
    {
        return methodKey switch
        {
            "TRANSFERENCIA" => _canUseTransfer,
            "BITCOIN" => _canUseBitcoin,
            "MIXTO" => _canUseTransfer || _canUseBitcoin,
            "CREDITO" => _canRegisterCredit,
            _ => true
        };
    }

    private PaymentSnapshot GetCurrentPaymentSnapshot(long? totalCentavosOverride = null)
    {
        var totalFinal = totalCentavosOverride ?? GetCurrentTotalCentavos();
        var cash = _currentPaymentMethod == "EFECTIVO"
            ? GetMoneyCentavos(_numCashReceived)
            : _acceptedCash;
        var transfer = _currentPaymentMethod == "EFECTIVO" ? 0 : _acceptedTransfer;
        var bitcoin = _currentPaymentMethod == "EFECTIVO" ? 0 : _acceptedBitcoin;
        var transferReference = _currentPaymentMethod == "EFECTIVO" ? string.Empty : _acceptedTransferRef;
        var bitcoinReference = _currentPaymentMethod == "EFECTIVO" ? string.Empty : _acceptedBitcoinRef;
        var totalPaid = cash + transfer + bitcoin;
        var missing = Math.Max(0, totalFinal - totalPaid);
        var overpay = Math.Max(0, totalPaid - totalFinal);
        var invalidChange = overpay > cash;
        var change = invalidChange ? 0 : overpay;

        return new PaymentSnapshot(
            totalFinal,
            cash,
            transfer,
            transferReference,
            bitcoin,
            bitcoinReference,
            totalPaid,
            missing,
            invalidChange,
            change,
            totalFinal > 0 && totalPaid > 0 && missing == 0 && !invalidChange);
    }

    private void PaymentAmountChanged()
    {
        UpdatePaymentReferenceStates();
        UpdateOverlayTotals();
    }

    private void UpdatePaymentReferenceStates()
    {
        UpdatePaymentReferenceState(_numTransferAmount, _txtTransferReference);
        UpdatePaymentReferenceState(_numBitcoinAmount, _txtBitcoinReference);
    }

    private static void UpdatePaymentReferenceState(NumericUpDown amountControl, TextBox referenceTextBox)
    {
        var enabled = amountControl.Value > 0;
        if (!enabled)
        {
            referenceTextBox.Clear();
        }

        referenceTextBox.Enabled = enabled;
    }

    private static long CalculateFinalTotalCentavos(long subtotal, long lineDiscounts, long generalDiscount)
    {
        return Math.Max(0, subtotal - lineDiscounts - generalDiscount);
    }

    private static IReadOnlyList<long> AllocateAmountAcrossBases(long amount, IReadOnlyList<long> bases)
    {
        var allocations = new long[bases.Count];
        if (amount <= 0 || bases.Count == 0)
        {
            return allocations;
        }

        var totalBase = bases.Sum();
        if (totalBase <= 0)
        {
            return allocations;
        }

        var allocated = 0L;
        var remainders = new List<(int Index, long Remainder)>();
        for (var i = 0; i < bases.Count; i++)
        {
            var product = amount * bases[i];
            allocations[i] = product / totalBase;
            allocated += allocations[i];
            remainders.Add((i, product % totalBase));
        }

        var remaining = amount - allocated;
        foreach (var remainder in remainders
            .OrderByDescending(r => r.Remainder)
            .ThenBy(r => r.Index)
            .Take((int)remaining))
        {
            allocations[remainder.Index]++;
        }

        return allocations;
    }

    private long GetCurrentTotalCentavos()
    {
        var subtotal = _cart.Sum(c => c.SubtotalCentavos);
        var lineDiscounts = _cart.Sum(c => c.DiscountCentavos);
        var generalDiscount = GetMoneyCentavos(_numGeneralDiscount);
        return CalculateFinalTotalCentavos(subtotal, lineDiscounts, generalDiscount);
    }

    private void ClearCart()
    {
        _draftCoordinator.ActiveDraft.Reset();
        _cart = _draftCoordinator.ActiveDraft.Lines;
        _cart.Clear();
        _numQuantity.Value = 1;
        _numLineDiscount.Value = 0;
        _txtLineDiscountReason.Clear();
        _cmbQuickLineReason.SelectedIndex = -1;
        _numGeneralDiscount.Value = 0;
        _txtGeneralDiscountReason.Clear();
        _cmbQuickGeneralReason.SelectedIndex = -1;
        SetCashReceivedCentavos(0, markAsManual: false);
        _numTransferAmount.Value = 0;
        _txtTransferReference.Clear();
        _numBitcoinAmount.Value = 0;
        _txtBitcoinReference.Clear();
        _acceptedCash = 0;
        _acceptedTransfer = 0;
        _acceptedTransferRef = string.Empty;
        _acceptedBitcoin = 0;
        _acceptedBitcoinRef = string.Empty;
        _currentPaymentMethod = "EFECTIVO";
        _pendingCreditRequest = null;
        _paymentValidated = false;
        _cashReceivedEditedManually = false;
        _selectedCustomerId = null;
        _selectedCustomerName = "Consumidor final";
        UpdateCustomerLabel();
        UpdatePaymentMethodDisplay();
        RefreshCart();
        RefreshDraftTabs();
        FocusBarcodeBox();
    }

    private void SetSaleProcessingState(bool processing)
    {
        _btnRegister.Enabled = !processing && _canRegisterSale;
        _btnRegister.Text = processing
            ? "REGISTRANDO..."
            : _presentationMode == PosPresentationMode.Quick ? "COBRAR  F8" : "REGISTRAR VENTA";
        _btnClear.Enabled = !processing;
        _btnDiscounts.Enabled = !processing;
        _txtBarcode.Enabled = !processing;
        _btnOtherMethods.Enabled = !processing;
        _btnAdd.Enabled = !processing;
        _btnSearch.Enabled = !processing;
        _btnRefresh.Enabled = !processing;
    }

    private async void NumCashReceived_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
        await ConfirmQuickCashAndRegisterAsync();
    }

    private async Task ConfirmQuickCashAndRegisterAsync()
    {
        if (_saleProcessing || _pnlPaymentOverlay.Visible)
        {
            return;
        }

        CommitCashReceivedInput();
        if (_currentPaymentMethod == "EFECTIVO" &&
            GetCurrentTotalCentavos() > 0 &&
            GetMoneyCentavos(_numCashReceived) <= 0)
        {
            PrepareExactCashPayment();
        }

        RefreshPaymentState();
        if (!AcceptedPaymentCoversCurrentTotal(out var message))
        {
            MessageBox.Show(message, "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await RegisterCurrentSaleAsync();
    }

    private async Task RegisterExactCashSaleAsync()
    {
        if (_saleProcessing || _pnlPaymentOverlay.Visible)
        {
            return;
        }

        PrepareExactCashPayment();
        await RegisterCurrentSaleAsync();
    }

    private void PrepareExactCashPayment()
    {
        _currentPaymentMethod = "EFECTIVO";
        _pendingCreditRequest = null;
        _acceptedTransfer = 0;
        _acceptedTransferRef = string.Empty;
        _acceptedBitcoin = 0;
        _acceptedBitcoinRef = string.Empty;
        _numTransferAmount.Value = 0;
        _txtTransferReference.Clear();
        _numBitcoinAmount.Value = 0;
        _txtBitcoinReference.Clear();
        _cashReceivedEditedManually = false;
        SetCashReceivedCentavos(GetCurrentTotalCentavos(), markAsManual: false);
        RefreshPaymentState();
    }

    private void CommitCashReceivedInput()
    {
        var centavos = GetMoneyCentavos(_numCashReceived);
        SetCashReceivedCentavos(centavos, markAsManual: _cashReceivedEditedManually);
        RefreshPaymentState();
    }

    private void FocusSearchBox()
    {
        if (IsDisposed || !Visible)
        {
            return;
        }

        _txtSearch.Focus();
        _txtSearch.SelectAll();
    }

    private void FocusBarcodeBox()
    {
        if (IsDisposed || !Visible || !_txtBarcode.Enabled)
        {
            return;
        }

        _txtBarcode.Focus();
        _txtBarcode.SelectAll();
    }

    private void FocusCashReceived()
    {
        if (IsDisposed || !Visible || !_numCashReceived.Enabled)
        {
            return;
        }

        _numCashReceived.Focus();
        _numCashReceived.Select(0, _numCashReceived.Text.Length);
    }

    private void UpdateCustomerLabel()
    {
        _lblCustomer.Text = $"Cliente: {_selectedCustomerName}";
        _lblQuickCartCustomer.Text = $"Cliente: {_selectedCustomerName}";
    }

    private SaleDraftLine? GetSelectedCartLine(bool showMessage = true)
    {
        if (_gridCart.CurrentRow?.DataBoundItem is not CartRow row)
        {
            if (showMessage)
            {
                MessageBox.Show("Seleccione una linea del carrito.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return null;
        }

        return _cart.FirstOrDefault(c => c.Product.ProductoSkuId == row.ProductoSkuId);
    }

    private async void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
        await AddProductFromSearchAsync();
    }

    private async void TxtBarcode_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
        await HandleBarcodeEnterAsync();
    }

    private Task HandleBarcodeEnterAsync(CancellationToken cancellationToken = default)
    {
        var code = _txtBarcode.Text;
        _txtBarcode.Clear();
        return ProcessBarcodeAsync(code, cancellationToken);
    }

    internal Task SimulateBarcodeEnterAsync(string code, CancellationToken cancellationToken = default)
    {
        _txtBarcode.Text = code;
        return HandleBarcodeEnterAsync(cancellationToken);
    }

    internal void AddManualSelectionForSmoke(PosSkuSaleItem item) => AddProductToCart(item);

    internal int CartLineCountForSmoke => _cart.Count;

    internal int CartUnitCountForSmoke => _cart.Sum(line => line.Quantity);

    internal string ScannerStatusForSmoke => _lblScannerStatus.Text;

    internal bool RegisterEnabledForSmoke => _btnRegister.Enabled;

    internal bool IsQuickPosForSmoke => _presentationMode == PosPresentationMode.Quick;

    internal bool BarcodeHasFocusForSmoke => _txtBarcode.Focused;

    internal bool SearchHasFocusForSmoke => _txtSearch.Focused;

    internal bool QuantityHasFocusForSmoke => _numQuantity.Focused;

    internal string QuickCartCustomerForSmoke => _lblQuickCartCustomer.Text;

    internal string CurrentPaymentMethodForSmoke => _currentPaymentMethod;

    internal bool PendingCreditRequestForSmoke => _pendingCreditRequest is not null;

    internal bool SaleProcessingForSmoke => _saleProcessing;

    internal bool PaymentOverlayVisibleForSmoke => _pnlPaymentOverlay.Visible;

    internal void SimulateEscapeForSmoke() =>
        PosForm_KeyDown(this, new KeyEventArgs(Keys.Escape));

    internal Rectangle PaymentOverlayBoundsForSmoke => _pnlPaymentOverlay.Bounds;

    internal int PaymentOverlayZIndexForSmoke => Controls.GetChildIndex(_pnlPaymentOverlay);

    internal string QuickCheckoutChangeForSmoke => _lblQuickCheckoutChange.Text;

    internal void ShowQuickCheckoutForSmoke() => ShowQuickCheckoutOverlay();

    internal bool SimulateQuickShortcutForSmoke(Keys key)
    {
        var args = new KeyEventArgs(key);
        return HandleQuickPosShortcut(args) && args.Handled && args.SuppressKeyPress;
    }

    internal void SetQuickCheckoutCashForSmoke(decimal amount)
    {
        _numPaymentCashAmount.Value = Math.Clamp(amount, _numPaymentCashAmount.Minimum, _numPaymentCashAmount.Maximum);
        UpdateOverlayTotals();
    }

    internal void ShowMixedPaymentForSmoke()
    {
        _canUseTransfer = true;
        _canUseBitcoin = true;
        _pnlPaymentOverlay.Visible = true;
        _pnlPaymentOverlay.BringToFront();
        _activeOverlayPanel = PosOverlayPanel.OtherPayments;
        CenterPaymentModal();
        SelectPaymentMethod("MIXTO", new Button());
    }

    internal bool FocusPaymentAmountForSmoke(string method)
    {
        var amount = method switch
        {
            "Efectivo" => _numPaymentCashAmount,
            "Transferencia" => _numTransferAmount,
            "Bitcoin" => _numBitcoinAmount,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Método de pago desconocido.")
        };

        var intermediate = method == "Bitcoin" ? _txtTransferReference : _txtBitcoinReference;
        intermediate.Focus();
        System.Windows.Forms.Application.DoEvents();
        amount.Focus();
        System.Windows.Forms.Application.DoEvents();
        var editor = amount.Controls.OfType<TextBox>().FirstOrDefault();
        return editor is not null && editor.TextLength > 0 && editor.SelectionLength == editor.TextLength;
    }

    internal void ClosePaymentOverlayForSmoke() =>
        ClosePaymentOverlay(save: false, focusAfterClose: _txtBarcode);

    internal void CancelQuickCheckoutForSmoke() => ClosePaymentOverlay(save: false, focusAfterClose: _txtBarcode);

    internal void SavePaymentOverlayForSmoke(string path)
    {
        using var bitmap = new Bitmap(_pnlPaymentOverlay.Width, _pnlPaymentOverlay.Height);
        _pnlPaymentOverlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    internal void ClickFirstCartActionForSmoke(string columnName)
    {
        if (_gridCart.Rows.Count == 0 || !_gridCart.Columns.Contains(columnName))
        {
            throw new InvalidOperationException($"No se puede ejecutar la acción de carrito '{columnName}'.");
        }

        _gridCart.CurrentCell = _gridCart.Rows[0].Cells[0];
        var column = _gridCart.Columns[columnName]
            ?? throw new InvalidOperationException($"No se encontró la acción de carrito '{columnName}'.");
        GridCart_CellContentClick(
            _gridCart,
            new DataGridViewCellEventArgs(column.Index, 0));
    }

    internal void SearchProductsForSmoke(string text)
    {
        _txtSearch.Text = text;
        _liveSearch?.CancelPending();
        _products = FilterAndSortProducts(
            _allProducts.ToList(),
            _txtSearch.Text,
            GetSelectedCategoryId(),
            GetSelectedSubcategoryFilter(),
            GetSelectedStockFilter(),
            GetSelectedLocationFilter(),
            GetSelectedSort(),
            CancellationToken.None);
        _currentProductsPage = 1;
        RenderProductCards(forceRender: true);
    }

    internal string SelectedLocationFilterForSmoke =>
        _cmbLocationFilter.SelectedItem is LocationFilterOption option ? option.Name : string.Empty;

    internal IReadOnlyList<string> LocationFilterOptionsForSmoke =>
        _cmbLocationFilter.Items.Cast<object>()
            .Select(item => _cmbLocationFilter.GetItemText(item) ?? string.Empty)
            .ToArray();

    internal IReadOnlyList<int> FilterProductIdsForLocationForSmoke(
        IReadOnlyList<PosSkuSaleItem> products,
        string locationName)
    {
        var location = _cmbLocationFilter.Items
            .Cast<LocationFilterOption>()
            .Single(option => string.Equals(option.Name, locationName, StringComparison.Ordinal));
        return FilterAndSortProducts(
                products,
                null,
                null,
                PosSubcategoryFilter.Todas,
                ProductStockFilter.All,
                location.Filter,
                ProductSort.NameAscending,
                CancellationToken.None)
            .Select(product => product.ProductoSkuId)
            .ToArray();
    }

    internal string PaginationInfoForSmoke => _lblPageInfo.Text;

    internal bool PreviousPageEnabledForSmoke => _btnPreviousPage.Enabled;

    internal bool NextPageEnabledForSmoke => _btnNextPage.Enabled;

    internal void SetPaginationScenarioForSmoke(IReadOnlyList<PosSkuSaleItem> products)
    {
        _products = products.ToList();
        _currentProductsPage = 1;
        UpdatePaginationControls();
    }

    internal void ClickNextPageForSmoke() => _btnNextPage.PerformClick();

    internal async Task ProcessBarcodeAsync(string? code, CancellationToken cancellationToken = default)
    {
        var captured = code?.Trim();
        if (string.IsNullOrWhiteSpace(captured) || _saleProcessing)
        {
            FocusBarcodeBox();
            return;
        }

        await _scannerQueue.WaitAsync(cancellationToken);
        try
        {
            SetScannerStatus($"Leyendo {captured}...", UITheme.MutedTextColor);
            var lookup = await _barcodeService.LookupExactAsync(captured, cancellationToken);
            if (lookup is null)
            {
                SetScannerStatus($"Código no encontrado: {captured}", UITheme.DangerColor);
                return;
            }

            var item = await _posCatalogService.GetBySkuIdAsync(lookup.ProductoSkuId, cancellationToken);
            if (item is null || item.Estado != EstadoProducto.ACTIVO || !item.SkuActivo)
            {
                SetScannerStatus($"SKU inactivo o no disponible: {lookup.CodigoSku}", UITheme.DangerColor);
                return;
            }

            if (item.StockDisponibleExhibicion <= 0)
            {
                SetScannerStatus(
                    item.StockDisponibleBodega > 0
                        ? $"Sin stock en Exhibición. Hay {item.StockDisponibleBodega} en Bodega."
                        : $"Sin stock disponible en Exhibición: {item.CodigoSku}",
                    UITheme.DangerColor);
                return;
            }

            if (!AddProductToCart(item, showMessage: false))
            {
                SetScannerStatus(
                    $"No se agregó {item.CodigoSku}: el carrito ya contiene toda la disponibilidad en Exhibición.",
                    UITheme.DangerColor);
                return;
            }
            SetScannerStatus(
                $"Agregado: {item.ProductoNombre} · {item.VarianteNombre ?? item.CodigoSku}",
                UITheme.SuccessColor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetScannerStatus(ex.Message, UITheme.DangerColor);
        }
        finally
        {
            _scannerQueue.Release();
            FocusBarcodeBox();
        }
    }

    private async Task MoveToExhibitionAsync(PosSkuSaleItem item)
    {
        if (!_canTransferInventory)
        {
            MessageBox.Show(
                "No tiene permiso para transferir inventario o esta estación está en modo Consulta.",
                "Mover a Exhibición",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (item.StockDisponibleBodega <= 0)
        {
            MessageBox.Show("Bodega no tiene disponibilidad para este SKU.", "Mover a Exhibición",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                $"¿Mover 1 unidad de '{item.ProductoNombre}' ({item.CodigoSku}) de Bodega a Exhibición?",
                "Mover a Exhibición",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _inventoryService.TransferAsync(new TransferInventoryRequest(
                Guid.NewGuid(),
                item.ProductoSkuId,
                UbicacionInventario.BODEGA,
                UbicacionInventario.EXHIBICION,
                1,
                "Reposición inmediata solicitada desde POS",
                "Movimiento confirmado por el usuario antes de agregar al carrito."));
            await RefreshProductsAsync(forceRender: true, resetPage: false);
            SetScannerStatus($"1 unidad de {item.CodigoSku} movida a Exhibición.", UITheme.SuccessColor);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Mover a Exhibición", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetScannerStatus(string text, Color color)
    {
        _lblScannerStatus.Text = text;
        _lblScannerStatus.ForeColor = color;
    }

    private async Task AddProductFromSearchAsync()
    {
        if (_saleProcessing)
        {
            return;
        }

        if (_liveSearch is not null)
        {
            await _liveSearch.ExecuteNowAsync(token =>
                ApplyProductFilterAsync(forceRender: true, resetPage: true, cancellationToken: token));
        }
        else
        {
            await ApplyProductFilterAsync(forceRender: true, resetPage: true);
        }
        var product = _products.FirstOrDefault();
        if (product is null)
        {
            MessageBox.Show("No se encontro un producto para agregar.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            FocusSearchBox();
            return;
        }

        AddProductToCart(product);
        _txtSearch.Clear();
        if (_presentationMode == PosPresentationMode.Quick)
        {
            FocusBarcodeBox();
        }
        else
        {
            FocusSearchBox();
        }
    }

    private void ConfigureProductsPanel()
    {
        _productsPanel.AutoScroll = true;
        _productsPanel.BorderStyle = BorderStyle.None;
        _productsPanel.BackColor = UITheme.BackgroundColor;
    }

    private void ConfigureCartGrid()
    {
        _gridCart.AllowUserToAddRows = false;
        _gridCart.AllowUserToDeleteRows = false;
        _gridCart.AutoGenerateColumns = false;
        _gridCart.MultiSelect = false;
        _gridCart.ReadOnly = true;
        _gridCart.RowHeadersVisible = false;
        _gridCart.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridCart.DataSource = _cartBinding;
        _gridCart.SelectionChanged += (_, _) => LoadSelectedCartLineControls();
        _gridCart.CellContentClick += GridCart_CellContentClick;

        UITheme.StyleCartGrid(_gridCart);

        var productColumn = AddGridColumn(_gridCart, "Nombre", "Producto", 180);
        productColumn.MinimumWidth = _presentationMode == PosPresentationMode.Quick ? 110 : 150;
        productColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        AddGridColumn(_gridCart, "Cantidad", "Cant.", 62, numeric: true);
        AddGridColumn(_gridCart, "PrecioUnitario", "Precio", _presentationMode == PosPresentationMode.Quick ? 72 : 80, numeric: true);
        AddGridColumn(_gridCart, "Total", "Total", _presentationMode == PosPresentationMode.Quick ? 72 : 80, numeric: true);
        AddCartButtonColumn("Increase", "+", 32);
        AddCartButtonColumn("Decrease", "-", 32);
        AddCartButtonColumn("Remove", "X", 32);
    }

    private void RestartSearchDebounce()
    {
        if (_loadingFilterOptions)
        {
            return;
        }

        _ = _liveSearch?.SearchNowAsync();
    }

    private async Task ApplyProductFilterAsync(
        bool forceRender = false,
        bool resetPage = true,
        CancellationToken cancellationToken = default)
    {
        var searchText = _txtSearch.Text;
        var categoryId = GetSelectedCategoryId();
        var subcategoryFilter = GetSelectedSubcategoryFilter();
        var stockFilter = GetSelectedStockFilter();
        var locationFilter = GetSelectedLocationFilter();
        var sort = GetSelectedSort();
        var productsSnapshot = _allProducts.ToList();

        try
        {
            var filteredProducts = await Task.Run(
                () => FilterAndSortProducts(
                    productsSnapshot,
                    searchText,
                    categoryId,
                    subcategoryFilter,
                    stockFilter,
                    locationFilter,
                    sort,
                    cancellationToken),
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            _products = filteredProducts;
            _currentProductsPage = resetPage
                ? 1
                : Math.Clamp(_currentProductsPage, 1, GetTotalProductPages());
            RenderProductCards(forceRender);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private void RenderProductCards(bool forceRender)
    {
        var pageProducts = GetCurrentPageProducts();
        var renderKey = BuildRenderKey(pageProducts);
        if (!forceRender && renderKey == _lastRenderedProductsKey)
        {
            UpdatePaginationControls();
            return;
        }

        _lastRenderedProductsKey = renderKey;
        _productsPanel.SuspendLayout();

        try
        {
            foreach (var control in _productsPanel.Controls.OfType<Control>().ToList())
            {
                control.Dispose();
            }

            _productsPanel.Controls.Clear();

            foreach (var product in pageProducts)
            {
                var card = new PosSkuCardControl(
                    product,
                    _canTransferInventory,
                    _productAssetService,
                    compactTouchMode: _presentationMode == PosPresentationMode.Quick);
                card.AddRequested += (_, selectedProduct) =>
                {
                    AddProductToCart(selectedProduct);
                    if (_presentationMode == PosPresentationMode.Quick)
                    {
                        FocusBarcodeBox();
                    }
                };
                card.PreviewRequested += (_, selectedProduct) => ShowProductPreview(selectedProduct);
                card.MoveRequested += async (_, selectedProduct) => await MoveToExhibitionAsync(selectedProduct);
                _productsPanel.Controls.Add(card);
            }

            UpdatePaginationControls();
        }
        finally
        {
            _productsPanel.ResumeLayout();
        }
    }

    private void ChangeProductsPage(int delta)
    {
        var totalPages = GetTotalProductPages();
        if (totalPages == 0)
        {
            return;
        }

        var nextPage = Math.Clamp(_currentProductsPage + delta, 1, totalPages);
        if (nextPage == _currentProductsPage)
        {
            return;
        }

        _currentProductsPage = nextPage;
        RenderProductCards(forceRender: true);
    }

    private List<PosSkuSaleItem> GetCurrentPageProducts()
    {
        if (_products.Count == 0)
        {
            return [];
        }

        var totalPages = GetTotalProductPages();
        _currentProductsPage = Math.Clamp(_currentProductsPage, 1, totalPages);

        return _products
            .Skip((_currentProductsPage - 1) * ProductsPageSize)
            .Take(ProductsPageSize)
            .ToList();
    }

    private int GetTotalProductPages()
    {
        return _products.Count == 0
            ? 0
            : (int)Math.Ceiling(_products.Count / (double)ProductsPageSize);
    }

    private void UpdatePaginationControls()
    {
        var totalPages = GetTotalProductPages();
        var currentPage = totalPages == 0 ? 0 : _currentProductsPage;
        var firstItem = _products.Count == 0 ? 0 : ((_currentProductsPage - 1) * ProductsPageSize) + 1;
        var lastItem = _products.Count == 0 ? 0 : Math.Min(_currentProductsPage * ProductsPageSize, _products.Count);

        _lblPageInfo.Text = $"Página {currentPage} de {totalPages}";
        _lblResultsInfo.Text = _products.Count == 0
            ? "Sin productos para mostrar"
            : $"Mostrando {firstItem}-{lastItem} de {_products.Count} productos";
        _btnPreviousPage.Enabled = totalPages > 1 && _currentProductsPage > 1;
        _btnNextPage.Enabled = totalPages > 1 && _currentProductsPage < totalPages;
    }

    private static string BuildRenderKey(IEnumerable<PosSkuSaleItem> products)
    {
        return string.Join(
            "|",
            products.Select(p => $"{p.ProductoSkuId}:{p.StockFisicoExhibicion}:{p.StockDisponibleExhibicion}:" +
                $"{p.StockFisicoBodega}:{p.StockDisponibleBodega}:" +
                $"{p.PrecioVentaEfectivoCentavos}:{p.ImagenOwnerKind}:{p.ImagenAssetId}:" +
                $"{p.ImagenRutaRelativa ?? p.ImagenLocalPath}"));
    }

    private static List<PosSkuSaleItem> FilterAndSortProducts(
        IReadOnlyList<PosSkuSaleItem> products,
        string? searchText,
        int? categoryId,
        PosSubcategoryFilter subcategoryFilter,
        ProductStockFilter stockFilter,
        ProductLocationFilter locationFilter,
        ProductSort sort,
        CancellationToken cancellationToken)
    {
        IEnumerable<PosSkuSaleItem> query = products;

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoriaId == categoryId.Value);
        }

        query = subcategoryFilter.Kind switch
        {
            PosSubcategoryFilterKind.Todas => query,
            PosSubcategoryFilterKind.SinSubcategoria => query.Where(p => p.SubcategoriaId is null),
            PosSubcategoryFilterKind.Concreta when subcategoryFilter.SubcategoriaId.HasValue =>
                query.Where(p => p.SubcategoriaId == subcategoryFilter.SubcategoriaId.Value),
            _ => throw new ArgumentException(
                "El filtro de subcategoría concreta requiere un identificador válido.",
                nameof(subcategoryFilter))
        };

        query = stockFilter switch
        {
            ProductStockFilter.InStock => query.Where(p => p.StockDisponibleExhibicion > 0),
            ProductStockFilter.LowStock => query.Where(p =>
                p.StockDisponibleExhibicion > 0 && p.StockDisponibleExhibicion <= p.MinimoExhibicion),
            ProductStockFilter.NoStock => query.Where(p => p.StockDisponibleExhibicion <= 0),
            _ => query
        };

        query = locationFilter switch
        {
            ProductLocationFilter.Exhibition => query.Where(p => p.StockFisicoExhibicion > 0),
            ProductLocationFilter.Warehouse => query.Where(p => p.StockFisicoBodega > 0),
            _ => query
        };

        query = sort switch
        {
            ProductSort.PriceAscending => query.OrderBy(p => p.PrecioVentaEfectivoCentavos).ThenBy(p => p.ProductoNombre),
            ProductSort.PriceDescending => query.OrderByDescending(p => p.PrecioVentaEfectivoCentavos).ThenBy(p => p.ProductoNombre),
            ProductSort.StockAscending => query.OrderBy(p => p.StockDisponibleExhibicion).ThenBy(p => p.ProductoNombre),
            _ => query.OrderBy(p => p.ProductoNombre).ThenBy(p => p.VarianteNombre)
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim();
            query = query.Where(item =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return item.CodigoInterno.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.CodigoSku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.ProductoNombre.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.CategoriaNombre.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (item.SubcategoriaNombre?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.VarianteNombre?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
            });
        }

        return query.ToList();
    }

    private int? GetSelectedCategoryId()
    {
        return _cmbCategoryFilter.SelectedItem is CategoryFilterOption option
            ? option.CategoriaId
            : null;
    }

    private PosSubcategoryFilter GetSelectedSubcategoryFilter()
    {
        return _cmbSubcategoryFilter.SelectedItem is SubcategoryFilterOption option
            ? option.Filter
            : PosSubcategoryFilter.Todas;
    }

    private static bool IsProtectedCategory(string? name)
    {
        return string.Equals(
            name?.Trim(),
            "Sin categoria",
            StringComparison.OrdinalIgnoreCase);
    }

    private ProductStockFilter GetSelectedStockFilter()
    {
        return _cmbStockFilter.SelectedItem is StockFilterOption option
            ? option.Filter
            : ProductStockFilter.All;
    }

    private ProductLocationFilter GetSelectedLocationFilter()
    {
        return _cmbLocationFilter.SelectedItem is LocationFilterOption option
            ? option.Filter
            : ProductLocationFilter.All;
    }

    private ProductSort GetSelectedSort()
    {
        return _cmbSort.SelectedItem is ProductSortOption option
            ? option.Sort
            : ProductSort.NameAscending;
    }

    private void GridCart_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _gridCart.Rows[e.RowIndex].DataBoundItem is not CartRow row)
        {
            return;
        }

        var line = _cart.FirstOrDefault(c => c.Product.ProductoSkuId == row.ProductoSkuId);
        if (line is null)
        {
            return;
        }

        var columnName = _gridCart.Columns[e.ColumnIndex].Name;
        if (columnName == "Increase")
        {
            if (line.Quantity + 1 > line.Product.StockDisponibleExhibicion)
            {
                MessageBox.Show("La cantidad supera el stock actual.", "Punto de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            line.Quantity++;
        }
        else if (columnName == "Decrease")
        {
            if (line.Quantity > 1)
            {
                line.Quantity--;
            }
        }
        else if (columnName == "Remove")
        {
            _cart.Remove(line);
        }

        InvalidateAcceptedPayment();
        RefreshCart();
    }

    private void LoadSelectedCartLineControls()
    {
        if (_gridCart.CurrentRow?.DataBoundItem is not CartRow row)
        {
            return;
        }

        var line = _cart.FirstOrDefault(c => c.Product.ProductoSkuId == row.ProductoSkuId);
        if (line is null)
        {
            return;
        }

        _numQuantity.Value = Math.Clamp(line.Quantity, (int)_numQuantity.Minimum, (int)_numQuantity.Maximum);
        _numLineDiscount.Value = CentavosToDollars(line.DiscountCentavos);
        _txtLineDiscountReason.Text = line.DiscountReason;
        _lblSelectedLine.Text = string.IsNullOrWhiteSpace(line.Product.VarianteNombre)
            ? $"{line.Product.ProductoNombre} · {line.Product.CodigoSku}"
            : $"{line.Product.ProductoNombre} · {line.Product.VarianteNombre} · {line.Product.CodigoSku}";
    }

    private Label CreateLabel(string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y)
        };
        UITheme.StyleLabelNormal(lbl);
        return lbl;
    }

    private static Panel CreateSurfacePanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };
    }

    private static void DockInput(Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 6, 0);
    }

    private static void DockCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.Margin = new Padding(0, 0, 6, 0);
        UITheme.StyleInput(combo);
    }

    private static void AddPaymentField(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        var label = CreateFieldLabel(labelText);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = false;
        label.Margin = Padding.Empty;
        label.Padding = new Padding(0, 0, 4, 0);
        layout.Controls.Add(label, 0, row);
        control.Font = UITheme.NormalFont;
        DockInput(control);
        control.Margin = new Padding(0, 4, 6, 4);
        layout.Controls.Add(control, 1, row);
    }

    private static TableLayoutPanel CreateOverlayEditorLayout(int rowCount, bool fitCompactOverlay = false)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rowCount,
            Padding = new Padding(10, fitCompactOverlay ? 8 : 12, 10, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // El editor por producto tiene una fila adicional; su presupuesto
        // compacto mantiene visibles Cancelar y Aplicar en ambos overlays.
        var rowHeight = fitCompactOverlay ? 40 : 42;
        var actionRowHeight = fitCompactOverlay ? 54 : 58;
        for (var i = 0; i < rowCount; i++)
        {
            layout.RowStyles.Add(new RowStyle(
                SizeType.Absolute,
                i == rowCount - 1 ? actionRowHeight : rowHeight));
        }

        return layout;
    }

    private static Label CreateReadOnlyValue(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = true
        };
    }

    private TableLayoutPanel CreateEditorActions(EventHandler apply, EventHandler cancel)
    {
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(0, 0, 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        // Both actions intentionally use the same outer margin and row height so
        // the primary/secondary styles do not create an uneven baseline.
        var buttonMargin = new Padding(4, 0, 4, 0);
        var btnCancel = new Button
        {
            Text = "Cancelar",
            Dock = DockStyle.Fill,
            Margin = buttonMargin,
            AutoSize = false
        };
        UITheme.StyleSecondaryButton(btnCancel);
        btnCancel.Click += cancel;
        _shortcutToolTip.SetToolTip(btnCancel, "Esc");

        var btnApply = new Button
        {
            Text = "Aplicar",
            Dock = DockStyle.Fill,
            Margin = buttonMargin,
            AutoSize = false
        };
        UITheme.StyleSuccessButton(btnApply);
        btnApply.Click += (_, _) => RunSingleOverlayAction(() => apply(btnApply, EventArgs.Empty));
        _shortcutToolTip.SetToolTip(btnApply, "Ctrl+Enter");
        _activeOverlayCancelButton = btnCancel;
        _activeOverlayApplyButton = btnApply;

        actions.Controls.Add(btnCancel, 0, 0);
        actions.Controls.Add(btnApply, 1, 0);
        return actions;
    }

    private static void AddOverlayTotalRow(TableLayoutPanel layout, int row, string text, Label valueLabel, Font font)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = font,
            ForeColor = UITheme.TextDark,
            AutoEllipsis = false,
            Padding = new Padding(12, 0, 0, 0)
        };

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private static void ConfigureOverlayAmountLabel(Label label, Color color, Font font)
    {
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleRight;
        label.ForeColor = color;
        label.Font = font;
        label.AutoEllipsis = false;
        label.Padding = new Padding(0, 0, 10, 0);
    }

    private static void ConfigureSummaryLabel(Label label, Color color, Font font)
    {
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.ForeColor = color;
        label.Font = font;
        label.AutoEllipsis = true;
    }

    private static DataGridViewTextBoxColumn AddGridColumn(
        DataGridView grid,
        string propertyName,
        string headerText,
        int width,
        bool numeric = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width
        };
        if (numeric)
        {
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        grid.Columns.Add(column);
        return column;
    }

    private void AddCartButtonColumn(string name, string text, int width)
    {
        var col = new DataGridViewButtonColumn
        {
            Name = name,
            HeaderText = string.Empty,
            Text = text,
            UseColumnTextForButtonValue = true,
            Width = width,
            FlatStyle = FlatStyle.Flat
        };
        _gridCart.Columns.Add(col);
    }

    private static void ConfigureButton(Button button, string text, int x, int y, EventHandler clickHandler)
    {
        button.Text = text;
        button.Location = new Point(x, y);
        button.Size = new Size(text.Length > 12 ? 132 : 90, 32);
        button.Click += clickHandler;
    }

    private static void ConfigureMoney(NumericUpDown control, int x, int y)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(110, 35);
        control.Minimum = 0;
        control.Maximum = 9_999_999.99m;
        control.DecimalPlaces = 2;
        control.ThousandsSeparator = true;
        control.Increment = 0.25m;
        UITheme.StyleInput(control);
    }

    private static void SelectNumericTextOnFocus(NumericUpDown control)
    {
        void QueueSelectAll()
        {
            if (control.IsDisposed || !control.IsHandleCreated)
            {
                return;
            }

            control.BeginInvoke(new Action(() =>
            {
                if (control.IsDisposed)
                {
                    return;
                }

                control.Controls.OfType<TextBox>().FirstOrDefault()?.SelectAll();
            }));
        }

        void AttachEditorFocusHandler(Control child)
        {
            if (child is TextBox editor)
            {
                editor.Enter += (_, _) => QueueSelectAll();
            }
        }

        control.Enter += (_, _) => QueueSelectAll();
        control.ControlAdded += (_, args) =>
        {
            if (args.Control is { } child)
            {
                AttachEditorFocusHandler(child);
            }
        };
        foreach (Control child in control.Controls)
        {
            AttachEditorFocusHandler(child);
        }
    }

    private static void CommitMoneyInput(NumericUpDown control)
    {
        control.Value = GetMoneyValue(control);
    }

    private static long GetMoneyCentavos(NumericUpDown control)
    {
        return DollarsToCentavos(GetMoneyValue(control));
    }

    private static decimal GetMoneyValue(NumericUpDown control)
    {
        var text = control.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) &&
            !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            value = control.Value;
        }

        return Math.Clamp(value, control.Minimum, control.Maximum);
    }

    private static void ConfigureInteger(NumericUpDown control, int x, int y)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(100, 35);
        control.Minimum = 0;
        control.Maximum = 1_000_000;
        control.DecimalPlaces = 0;
        control.ThousandsSeparator = true;
        UITheme.StyleInput(control);
    }

    private void ConfigureFilterCombo(ComboBox combo, int x, int y, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Location = new Point(x, y);
        combo.Size = new Size(width, 35);
        UITheme.StyleInput(combo);
        combo.SelectedIndexChanged += (_, _) => RestartSearchDebounce();
    }

    private static decimal CentavosToDollars(long centavos)
    {
        return centavos / 100m;
    }

    private static long DollarsToCentavos(decimal dollars)
    {
        if (dollars < 0)
        {
            throw new ArgumentException("El monto no puede ser negativo.", nameof(dollars));
        }

        return decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string FormatDollars(long centavos)
    {
        return (centavos / 100m).ToString("0.00");
    }

    private static void LogPosPerformance(string message)
    {
        var logMessage = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
        System.Diagnostics.Trace.TraceInformation(logMessage);

        try
        {
            var logDirectory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "pos-performance.log"), logMessage);
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Could not write POS performance log.");
        }
    }

    private void LogPosSaleFailure(Exception exception)
    {
        var user = _currentSession.UsuarioActual;
        var logMessage = string.Join(
            Environment.NewLine,
            $"[{DateTime.Now:O}] POS sale registration failed.",
            $"Version={System.Windows.Forms.Application.ProductVersion}; Form={nameof(EmployeePOSForm)}; Operation=RegisterSale; " +
            $"UserId={user?.UsuarioId.ToString() ?? "<none>"}; User={user?.NombreUsuario ?? "<none>"}; Role={user?.Rol.ToString() ?? "<none>"}",
            exception.ToString(),
            new string('-', 80),
            string.Empty);

        System.Diagnostics.Trace.TraceError(logMessage);

        try
        {
            var logDirectory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "application-errors.log"), logMessage);
        }
        catch
        {
            System.Diagnostics.Trace.TraceError("Could not write POS sale technical log.");
        }
    }

    private static void SetupQuickReasons(ComboBox cmb, TextBox txt)
    {
        cmb.DropDownStyle = ComboBoxStyle.DropDownList;
        cmb.Font = UITheme.NormalFont;
        cmb.Items.AddRange([
            "",
            "Compra grande / mayoreo",
            "Amistad o familiar",
            "Cliente frecuente",
            "Rebaja autorizada",
            "Promoción especial",
            "Producto con detalle",
            "Otro"
        ]);

        cmb.SelectedIndexChanged += (_, _) =>
        {
            var val = cmb.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(val))
            {
                txt.Text = "";
            }
            else if (val == "Otro")
            {
                txt.Text = "";
                txt.Focus();
            }
            else
            {
                txt.Text = val;
            }
        };
    }

    private sealed record CategoryFilterOption(int? CategoriaId, string Name, bool IsProtected);

    private sealed record SubcategoryFilterOption(PosSubcategoryFilter Filter, string Name);

    private sealed record StockFilterOption(ProductStockFilter Filter, string Name);

    private sealed record LocationFilterOption(ProductLocationFilter Filter, string Name);

    private sealed record ProductSortOption(ProductSort Sort, string Name);

    private sealed record PaymentSnapshot(
        long TotalCentavos,
        long CashCentavos,
        long TransferCentavos,
        string TransferReference,
        long BitcoinCentavos,
        string BitcoinReference,
        long TotalPaidCentavos,
        long MissingCentavos,
        bool InvalidChange,
        long ChangeCentavos,
        bool IsComplete);

    private enum ProductStockFilter
    {
        All,
        InStock,
        LowStock,
        NoStock
    }

    private enum ProductLocationFilter
    {
        All,
        Exhibition,
        Warehouse
    }

    private enum ProductSort
    {
        NameAscending,
        PriceAscending,
        PriceDescending,
        StockAscending
    }

    private enum PosOverlayPanel
    {
        None,
        Discounts,
        OtherPayments
    }

    private sealed record CartRow(
        int ProductoSkuId,
        int ProductoId,
        string CodigoInterno,
        string Nombre,
        int Cantidad,
        string PrecioUnitario,
        string Descuento,
        string Total)
    {
        public static CartRow FromLine(SaleDraftLine line)
        {
            return new CartRow(
                line.Product.ProductoSkuId,
                line.Product.ProductoId,
                line.Product.CodigoSku,
                string.IsNullOrWhiteSpace(line.Product.VarianteNombre)
                    ? line.Product.ProductoNombre
                    : $"{line.Product.VarianteNombre} · {line.Product.ProductoNombre}",
                line.Quantity,
                FormatDollars(line.Product.PrecioVentaEfectivoCentavos),
                FormatDollars(line.DiscountCentavos),
                FormatDollars(line.TotalCentavos));
        }
    }
}
