using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SupplierProfileForm : Form
{
    private readonly int _supplierId;
    private readonly ISupplierQueryService _supplierQueryService;
    private readonly ISupplierService _supplierService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IProductService _productService;
    private readonly IPurchaseQueryService _purchaseQueryService;
    private readonly IPurchaseReceptionService _purchaseReceptionService;
    private readonly IAccountsPayableQueryService? _accountsPayableQueryService;
    private readonly IAccountsPayableService? _accountsPayableService;
    private readonly BindingSource _productsBinding = new();
    private readonly BindingSource _historyBinding = new();
    private readonly BindingSource _purchasesBinding = new();
    private readonly BindingSource _accountsBinding = new();
    private readonly Dictionary<string, Label> _values = new();
    private readonly DataGridView _productsGrid = new();
    private readonly DataGridView _historyGrid = new();
    private readonly DataGridView _purchasesGrid = new();
    private readonly DataGridView _accountsGrid = new();
    private readonly Label _lblSupplierBalance = new();
    private readonly Label _lblSupplierOpenAccounts = new();
    private readonly Label _lblSupplierOverdueAccounts = new();
    private readonly Button _btnSupplierPayment = new();
    private readonly Button _btnSupplierPaymentHistory = new();
    private readonly Button _btnEdit = new();
    private readonly Button _btnAssociateProduct = new();
    private readonly Button _btnSetMain = new();
    private readonly Button _btnDeactivateProduct = new();
    private readonly Button _btnViewPurchase = new();
    private readonly Button _btnReceivePurchase = new();
    private readonly Button _btnClose = new();
    private readonly Label _lblStatus = new();
    private bool _canEdit;
    private bool _canManageProducts;
    private bool _canViewHistory;
    private bool _canAccessPurchases;
    private bool _canReceivePurchases;
    private bool _canViewFinances;
    private bool _canRegisterSupplierPayment;
    private bool _canViewSupplierPaymentHistory;
    private SupplierDetail? _detail;

    public SupplierProfileForm(
        int supplierId,
        ISupplierQueryService supplierQueryService,
        ISupplierService supplierService,
        IEmployeePermissionService permissionService,
        IProductService productService,
        IPurchaseQueryService purchaseQueryService,
        IPurchaseReceptionService purchaseReceptionService,
        IAccountsPayableQueryService? accountsPayableQueryService = null,
        IAccountsPayableService? accountsPayableService = null)
    {
        _supplierId = supplierId;
        _supplierQueryService = supplierQueryService;
        _supplierService = supplierService;
        _permissionService = permissionService;
        _productService = productService;
        _purchaseQueryService = purchaseQueryService;
        _purchaseReceptionService = purchaseReceptionService;
        _accountsPayableQueryService = accountsPayableQueryService;
        _accountsPayableService = accountsPayableService;

        BuildUi();
        Load += SupplierProfileForm_Load;
    }

    private async void SupplierProfileForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDORES_ACCEDER);
            await LoadPermissionsAsync();
            await RefreshDetailAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Perfil de proveedor";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1160, 740);
        MinimumSize = new Size(980, 640);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildTabs(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 12, 18, 12),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Proveedor o artesano",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Perfil operativo, productos asociados e historial.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalBoldFont,
            Margin = new Padding(0)
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 4,
            RowCount = 3
        };

        for (var i = 0; i < 4; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));

        AddSummaryCell(panel, "Codigo", "Codigo", 0, 0);
        AddSummaryCell(panel, "Tipo", "Tipo", 1, 0);
        AddSummaryCell(panel, "Estado", "Estado", 2, 0);
        AddSummaryCell(panel, "Productos", "Productos", 3, 0);
        AddSummaryCell(panel, "Nombre", "Nombre", 0, 1);
        AddSummaryCell(panel, "Contacto", "Contacto", 1, 1);
        AddSummaryCell(panel, "Telefono", "Telefono", 2, 1);
        AddSummaryCell(panel, "WhatsApp", "WhatsApp", 3, 1);
        AddSummaryCell(panel, "Correo", "Correo", 0, 2);
        AddSummaryCell(panel, "Documento", "DUI/documento", 1, 2);
        AddSummaryCell(panel, "Fiscal", "NIT/documento fiscal", 2, 2);
        AddSummaryCell(panel, "Actualizado", "Actualizado", 3, 2);
        return panel;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont
        };

        var productsTab = new TabPage("Productos asociados")
        {
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };

        ConfigureProductsGrid();
        _productsGrid.DataSource = _productsBinding;
        _productsGrid.SelectionChanged += (_, _) => UpdateActionState();
        productsTab.Controls.Add(_productsGrid);

        var historyTab = new TabPage("Historial")
        {
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };

        ConfigureHistoryGrid();
        _historyGrid.DataSource = _historyBinding;
        historyTab.Controls.Add(_historyGrid);

        var purchasesTab = new TabPage("Compras")
        {
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };

        purchasesTab.Controls.Add(BuildPurchasesSection());

        tabs.TabPages.Add(productsTab);
        tabs.TabPages.Add(purchasesTab);
        tabs.TabPages.Add(historyTab);
        tabs.TabPages.Add(BuildFinancesTab());
        return tabs;
    }

    private TabPage BuildFinancesTab()
    {
        var tab = new TabPage("Finanzas") { BackColor = UITheme.SurfaceColor, Padding = new Padding(12) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (var i = 0; i < 3; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        stats.Controls.Add(BuildFinancialCard("Saldo total", _lblSupplierBalance), 0, 0);
        stats.Controls.Add(BuildFinancialCard("Cuentas abiertas", _lblSupplierOpenAccounts), 1, 0);
        stats.Controls.Add(BuildFinancialCard("Cuentas vencidas", _lblSupplierOverdueAccounts), 2, 0);
        UITheme.StyleDataGridView(_accountsGrid);
        _accountsGrid.Dock = DockStyle.Fill;
        _accountsGrid.ReadOnly = true;
        _accountsGrid.AllowUserToAddRows = false;
        _accountsGrid.AutoGenerateColumns = true;
        _accountsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _accountsGrid.DataSource = _accountsBinding;
        _accountsGrid.SelectionChanged += (_, _) => UpdateFinanceActions();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        ConfigureButton(_btnSupplierPaymentHistory, "Historial de pagos", 160, (_, _) => OpenSupplierPaymentHistory());
        UITheme.StyleSecondaryButton(_btnSupplierPaymentHistory);
        ConfigureButton(_btnSupplierPayment, "Registrar abono", 150, async (_, _) => await RegisterSupplierPaymentAsync());
        UITheme.StyleSuccessButton(_btnSupplierPayment);
        actions.Controls.AddRange([_btnSupplierPaymentHistory, _btnSupplierPayment]);
        root.Controls.Add(stats, 0, 0);
        root.Controls.Add(_accountsGrid, 0, 1);
        root.Controls.Add(actions, 0, 2);
        tab.Controls.Add(root);
        return tab;
    }

    private Control BuildPurchasesSection()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        ConfigurePurchasesGrid();
        _purchasesGrid.DataSource = _purchasesBinding;
        _purchasesGrid.SelectionChanged += (_, _) => UpdatePurchaseActionState();
        _purchasesGrid.CellDoubleClick += async (_, _) => await ViewSelectedPurchaseAsync();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        ConfigureButton(_btnReceivePurchase, "Registrar recepcion", 170, async (_, _) => await ReceiveSelectedPurchaseAsync());
        UITheme.StyleSuccessButton(_btnReceivePurchase);
        ConfigureButton(_btnViewPurchase, "Ver compra", 120, async (_, _) => await ViewSelectedPurchaseAsync());
        UITheme.StyleSecondaryButton(_btnViewPurchase);
        actions.Controls.AddRange([_btnReceivePurchase, _btnViewPurchase]);

        panel.Controls.Add(_purchasesGrid, 0, 0);
        panel.Controls.Add(actions, 0, 1);
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = UITheme.NormalBoldFont;
        _lblStatus.ForeColor = UITheme.MutedTextColor;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true
        };

        ConfigureButton(_btnClose, "Cerrar", 102, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnClose);
        ConfigureButton(_btnDeactivateProduct, "Desasociar producto", 178, async (_, _) => await DeactivateProductAsync());
        UITheme.StyleDangerButton(_btnDeactivateProduct);
        ConfigureButton(_btnSetMain, "Marcar principal", 150, async (_, _) => await SetMainProductAsync());
        UITheme.StyleSecondaryButton(_btnSetMain);
        ConfigureButton(_btnAssociateProduct, "Asociar producto", 154, async (_, _) => await AssociateProductAsync());
        UITheme.StylePrimaryButton(_btnAssociateProduct);
        ConfigureButton(_btnEdit, "Editar", 96, async (_, _) => await EditSupplierAsync());
        UITheme.StyleSecondaryButton(_btnEdit);

        actions.Controls.AddRange([_btnClose, _btnDeactivateProduct, _btnSetMain, _btnAssociateProduct, _btnEdit]);
        panel.Controls.Add(_lblStatus, 0, 0);
        panel.Controls.Add(actions, 1, 0);
        return panel;
    }

    private async Task LoadPermissionsAsync()
    {
        _canEdit = await _permissionService.HasPermissionAsync(PermisoEmpleado.PROVEEDOR_EDITAR);
        _canManageProducts = await _permissionService.HasPermissionAsync(PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR);
        _canViewHistory = await _permissionService.HasPermissionAsync(PermisoEmpleado.PROVEEDOR_HISTORIAL_VER);
        _canAccessPurchases = await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPRAS_ACCEDER);
        _canReceivePurchases = await _permissionService.HasPermissionAsync(PermisoEmpleado.COMPRA_RECEPCIONAR);
        _canViewFinances = _accountsPayableQueryService is not null && await _permissionService.HasPermissionAsync(PermisoEmpleado.FINANZAS_PROVEEDOR_VER);
        _canRegisterSupplierPayment = _accountsPayableService is not null && await _permissionService.HasPermissionAsync(PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR);
        _canViewSupplierPaymentHistory = _accountsPayableQueryService is not null &&
            await _permissionService.HasPermissionAsync(PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER);

        _btnEdit.Visible = _canEdit;
        _btnAssociateProduct.Visible = _canManageProducts;
        _btnSetMain.Visible = _canManageProducts;
        _btnDeactivateProduct.Visible = _canManageProducts;
        _btnViewPurchase.Visible = _canAccessPurchases;
        _btnReceivePurchase.Visible = _canReceivePurchases;
        _btnSupplierPayment.Visible = _canRegisterSupplierPayment;
        _btnSupplierPaymentHistory.Visible = _canViewSupplierPaymentHistory;
    }

    private async Task RefreshDetailAsync()
    {
        _lblStatus.Text = "Cargando proveedor...";
        _detail = await _supplierQueryService.GetByIdAsync(_supplierId);
        if (_detail is null)
        {
            MessageBox.Show("El proveedor seleccionado ya no existe.", "Proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        Text = $"Proveedor {_detail.CodigoProveedor}";
        SetValue("Codigo", _detail.CodigoProveedor);
        SetValue("Tipo", FormatSupplierType(_detail.Tipo));
        SetValue("Estado", _detail.Estado.ToString());
        SetValue("Productos", _detail.Productos.Count(p => p.Activo).ToString());
        SetValue("Nombre", _detail.NombreRazonSocial);
        SetValue("Contacto", Empty(_detail.NombreContacto));
        SetValue("Telefono", Empty(_detail.Telefono));
        SetValue("WhatsApp", Empty(_detail.WhatsApp));
        SetValue("Correo", Empty(_detail.Correo));
        SetValue("Documento", Empty(_detail.DocumentoIdentidad));
        SetValue("Fiscal", Empty(_detail.DocumentoFiscal));
        SetValue("Actualizado", SaleReceiptViewModel.ToLocal(_detail.FechaActualizacion).ToString("yyyy-MM-dd HH:mm"));

        _productsBinding.DataSource = _detail.Productos.Select(SupplierProductRow.FromSummary).ToList();
        if (_canViewHistory)
        {
            var history = _detail.Historial.Count > 0
                ? _detail.Historial
                : await _supplierQueryService.GetHistoryAsync(_supplierId);
            _historyBinding.DataSource = history.Select(SupplierHistoryRow.FromEntry).ToList();
        }
        else
        {
            _historyBinding.DataSource = new List<SupplierHistoryRow>
            {
                new("-", "Sin permiso para ver historial.", string.Empty, string.Empty)
            };
        }

        await RefreshPurchasesSectionAsync();
        await RefreshFinancesAsync();

        _lblStatus.Text = $"{_detail.CodigoProveedor} - {_detail.NombreRazonSocial}";
        UpdateActionState();
    }

    private async Task RefreshFinancesAsync()
    {
        if (!_canViewFinances || _accountsPayableQueryService is null)
        {
            _lblSupplierBalance.Text = "Sin permiso";
            _lblSupplierOpenAccounts.Text = "-";
            _lblSupplierOverdueAccounts.Text = "-";
            _accountsBinding.DataSource = Array.Empty<object>();
            UpdateFinanceActions();
            return;
        }
        var summary = await _accountsPayableQueryService.GetSupplierSummaryAsync(_supplierId);
        _lblSupplierBalance.Text = SaleReceiptViewModel.FormatDollars(summary.SaldoTotalCentavos);
        _lblSupplierOpenAccounts.Text = summary.CuentasAbiertas.ToString();
        _lblSupplierOverdueAccounts.Text = summary.CuentasVencidas.ToString();
        _accountsBinding.DataSource = summary.CuentasRecientes.Select(a => new SupplierAccountRow(
            a, a.CodigoCuenta, a.CodigoCompra, a.FechaVencimiento.ToString("yyyy-MM-dd"),
            SaleReceiptViewModel.FormatDollars(a.TotalOriginalCentavos),
            SaleReceiptViewModel.FormatDollars(a.TotalPagadoCentavos),
            SaleReceiptViewModel.FormatDollars(a.SaldoPendienteCentavos), a.Estado.ToString())).ToList();
        UpdateFinanceActions();
    }

    private async Task RegisterSupplierPaymentAsync()
    {
        if (_accountsPayableService is null || _accountsPayableQueryService is null || _accountsBinding.Current is not SupplierAccountRow row || row.Source.SaldoPendienteCentavos <= 0) return;
        var detail = await _accountsPayableQueryService.GetDetailAsync(row.Source.CuentaPorPagarId);
        if (detail is null) return;
        using var form = new SupplierPaymentForm(detail, _accountsPayableService);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshFinancesAsync();
    }

    private void OpenSupplierPaymentHistory()
    {
        if (_accountsPayableQueryService is null) return;
        using var form = new SupplierPaymentsHistoryForm(_accountsPayableQueryService, supplierId: _supplierId);
        form.ShowDialog(this);
    }

    private void UpdateFinanceActions()
    {
        _btnSupplierPayment.Enabled = _canRegisterSupplierPayment && _accountsBinding.Current is SupplierAccountRow { Source.SaldoPendienteCentavos: > 0 };
        _btnSupplierPaymentHistory.Enabled = _canViewSupplierPaymentHistory;
    }

    private static Control BuildFinancialCard(string caption, Label value)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = UITheme.BackgroundColor, Padding = new Padding(12), Margin = new Padding(5) };
        card.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor }, 0, 0);
        value.Text = "-"; value.Dock = DockStyle.Fill; value.Font = UITheme.HeaderFont; value.ForeColor = UITheme.PrimaryColor;
        card.Controls.Add(value, 0, 1);
        return card;
    }

    private async Task RefreshPurchasesSectionAsync()
    {
        if (!_canAccessPurchases)
        {
            _purchasesBinding.DataSource = new List<SupplierPurchaseRow>
            {
                new(0, "-", "-", "Sin permiso para ver compras.", "-", "-", 0, 0)
            };
            UpdatePurchaseActionState();
            return;
        }

        var purchases = await _purchaseQueryService.GetBySupplierAsync(_supplierId);
        _purchasesBinding.DataSource = purchases.Select(SupplierPurchaseRow.FromSummary).ToList();
        UpdatePurchaseActionState();
    }

    private async Task EditSupplierAsync()
    {
        if (_detail is null)
        {
            return;
        }

        using var form = new SupplierEditForm(_supplierService, _detail);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshDetailAsync();
        }
    }

    private async Task AssociateProductAsync()
    {
        using var form = new SupplierProductAssociationForm(_supplierId, _supplierService, _productService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshDetailAsync();
        }
    }

    private async Task DeactivateProductAsync()
    {
        if (CurrentProduct() is not { } row)
        {
            return;
        }

        var motivo = PromptForText("Desasociar producto", "Motivo obligatorio:");
        if (string.IsNullOrWhiteSpace(motivo))
        {
            return;
        }

        try
        {
            await _supplierService.DeactivateProductAssociationAsync(row.ProductoProveedorId, motivo);
            await RefreshDetailAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Productos asociados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task SetMainProductAsync()
    {
        if (CurrentProduct() is not { } row)
        {
            return;
        }

        try
        {
            await _supplierService.SetMainProductSupplierAsync(new SupplierProductMainRequest(
                row.ProductoProveedorId,
                !row.EsProveedorPrincipal));
            await RefreshDetailAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Productos asociados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private SupplierProductRow? CurrentProduct()
    {
        return _productsBinding.Current as SupplierProductRow;
    }

    private void UpdateActionState()
    {
        var row = CurrentProduct();
        var hasProduct = row is not null;
        _btnEdit.Enabled = _detail is not null && _canEdit;
        _btnAssociateProduct.Enabled = _detail is not null && _canManageProducts && _detail.Estado == EstadoProveedor.ACTIVO;
        _btnDeactivateProduct.Enabled = hasProduct && _canManageProducts && row!.Activo;
        _btnSetMain.Enabled = hasProduct && _canManageProducts && row!.Activo;
        if (row is not null)
        {
            _btnSetMain.Text = row.EsProveedorPrincipal ? "Quitar principal" : "Marcar principal";
        }
    }

    private void UpdatePurchaseActionState()
    {
        var row = CurrentPurchase();
        var hasPurchase = row is not null && row.CompraId > 0;
        _btnViewPurchase.Enabled = hasPurchase && _canAccessPurchases;
        _btnReceivePurchase.Enabled = hasPurchase && _canReceivePurchases && row!.Pendiente > 0;
    }

    private async Task ViewSelectedPurchaseAsync()
    {
        if (CurrentPurchase() is not { CompraId: > 0 } row)
        {
            return;
        }

        try
        {
            var detail = await _purchaseQueryService.GetByIdAsync(row.CompraId);
            if (detail is null)
            {
                MessageBox.Show("La compra seleccionada ya no existe.", "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await RefreshPurchasesSectionAsync();
                return;
            }

            using var form = new PurchaseDetailForm(detail);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ReceiveSelectedPurchaseAsync()
    {
        if (CurrentPurchase() is not { CompraId: > 0 } row)
        {
            return;
        }

        try
        {
            var detail = await _purchaseQueryService.GetByIdAsync(row.CompraId);
            if (detail is null)
            {
                MessageBox.Show("La compra seleccionada ya no existe.", "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await RefreshPurchasesSectionAsync();
                return;
            }

            using var form = new PurchaseReceptionForm(detail, _purchaseReceptionService);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                await RefreshPurchasesSectionAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Recepcion de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private SupplierPurchaseRow? CurrentPurchase()
    {
        return _purchasesBinding.Current as SupplierPurchaseRow;
    }

    private void ConfigureProductsGrid()
    {
        UITheme.StyleDataGridView(_productsGrid);
        _productsGrid.Dock = DockStyle.Fill;
        _productsGrid.ReadOnly = true;
        _productsGrid.AllowUserToAddRows = false;
        _productsGrid.AllowUserToDeleteRows = false;
        _productsGrid.AllowUserToResizeRows = false;
        _productsGrid.AutoGenerateColumns = false;
        _productsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _productsGrid.ScrollBars = ScrollBars.Both;
        _productsGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _productsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _productsGrid.MultiSelect = false;
        _productsGrid.RowHeadersVisible = false;
        _productsGrid.ShowCellToolTips = true;

        AddGridColumn(_productsGrid, "CodigoProducto", "Código", 110, 10);
        AddGridColumn(_productsGrid, "Producto", "Producto", 280, 28);
        AddGridColumn(_productsGrid, "Categoria", "Categoría", 150, 14);
        AddGridColumn(_productsGrid, "Subcategoria", "Subcategoría", 170, 16);
        AddGridColumn(_productsGrid, "CodigoProductoProveedor", "Código proveedor", 150, 14);
        AddGridColumn(_productsGrid, "Principal", "Principal", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_productsGrid, "UltimoCosto", "Último costo", 120, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_productsGrid, "Activo", "Activo", 80, 7, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_productsGrid, "Notas", "Notas", 160, 14);
    }

    private void ConfigureHistoryGrid()
    {
        UITheme.StyleDataGridView(_historyGrid);
        _historyGrid.Dock = DockStyle.Fill;
        _historyGrid.ReadOnly = true;
        _historyGrid.AllowUserToAddRows = false;
        _historyGrid.AllowUserToDeleteRows = false;
        _historyGrid.AllowUserToResizeRows = false;
        _historyGrid.AutoGenerateColumns = false;
        _historyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _historyGrid.MultiSelect = false;
        _historyGrid.RowHeadersVisible = false;
        _historyGrid.ShowCellToolTips = true;

        AddGridColumn(_historyGrid, "Fecha", "Fecha", 150, 14);
        AddGridColumn(_historyGrid, "Tipo", "Tipo", 160, 16);
        AddGridColumn(_historyGrid, "Usuario", "Usuario", 130, 12);
        AddGridColumn(_historyGrid, "Descripcion", "Descripcion", 380, 58);
    }

    private void ConfigurePurchasesGrid()
    {
        UITheme.StyleDataGridView(_purchasesGrid);
        _purchasesGrid.Dock = DockStyle.Fill;
        _purchasesGrid.ReadOnly = true;
        _purchasesGrid.AllowUserToAddRows = false;
        _purchasesGrid.AllowUserToDeleteRows = false;
        _purchasesGrid.AllowUserToResizeRows = false;
        _purchasesGrid.AutoGenerateColumns = false;
        _purchasesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _purchasesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _purchasesGrid.MultiSelect = false;
        _purchasesGrid.RowHeadersVisible = false;
        _purchasesGrid.ShowCellToolTips = true;

        AddGridColumn(_purchasesGrid, "CodigoCompra", "Compra", 120, 12);
        AddGridColumn(_purchasesGrid, "Fecha", "Fecha", 120, 10);
        AddGridColumn(_purchasesGrid, "Estado", "Estado", 120, 10, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_purchasesGrid, "Total", "Total", 120, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_purchasesGrid, "Comprado", "Comprado", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_purchasesGrid, "Recibido", "Recibido", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_purchasesGrid, "Pendiente", "Pendiente", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_purchasesGrid, "UsuarioRegistro", "Usuario", 140, 12);
    }

    private void AddSummaryCell(TableLayoutPanel panel, string key, string labelText, int column, int row)
    {
        var cell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 18, 10)
        };
        cell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(0, 0, 0, 4)
        };
        var value = new Label
        {
            Text = "-",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        cell.Controls.Add(label, 0, 0);
        cell.Controls.Add(value, 0, 1);
        panel.Controls.Add(cell, column, row);
        _values[key] = value;
    }

    private void SetValue(string key, string value)
    {
        if (!_values.TryGetValue(key, out var label))
        {
            return;
        }

        label.Text = value;
        label.Tag = value;
        var toolTip = new ToolTip();
        toolTip.SetToolTip(label, value);
    }

    private static void AddGridColumn(
        DataGridView grid,
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft,
        DataGridViewAutoSizeColumnMode? autoSizeMode = null)
    {
        var mode = autoSizeMode ?? (grid.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.None
            ? DataGridViewAutoSizeColumnMode.None
            : DataGridViewAutoSizeColumnMode.Fill);
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = mode,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        };
        if (mode == DataGridViewAutoSizeColumnMode.None)
        {
            column.Width = Math.Max(
                minimumWidth,
                TextRenderer.MeasureText(
                    header,
                    grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 24);
        }

        grid.Columns.Add(column);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        using var font = UITheme.ButtonFont;
        width = Math.Max(width, TextRenderer.MeasureText(text, font).Width + 32);
        button.Text = text;
        button.Width = width;
        button.MinimumSize = new Size(width, 36);
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private static string? PromptForText(string title, string labelText)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(460, 180),
            MinimumSize = new Size(420, 170),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        UITheme.StyleForm(form);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var label = new Label { Text = labelText, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont };
        var textBox = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        UITheme.StyleTextBox(textBox);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        var ok = new Button { Text = "Aceptar", Width = 100, Height = 34, DialogResult = DialogResult.OK };
        UITheme.StylePrimaryButton(ok);
        var cancel = new Button { Text = "Cancelar", Width = 100, Height = 34, DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        actions.Controls.AddRange([cancel, ok]);

        root.Controls.Add(label, 0, 0);
        root.Controls.Add(textBox, 0, 1);
        root.Controls.Add(actions, 0, 2);
        form.Controls.Add(root);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
    }

    private static string FormatSupplierType(TipoProveedor tipo)
    {
        return tipo switch
        {
            TipoProveedor.PROVEEDOR => "Proveedor",
            TipoProveedor.ARTESANO => "Artesano",
            TipoProveedor.AMBOS => "Proveedor y artesano",
            _ => tipo.ToString()
        };
    }

    private static string Empty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private sealed record SupplierProductRow(
        int ProductoProveedorId,
        int ProductoId,
        string CodigoProducto,
        string Producto,
        string Categoria,
        string Subcategoria,
        string CodigoProductoProveedor,
        bool EsProveedorPrincipal,
        string Principal,
        string UltimoCosto,
        bool Activo,
        string Notas)
    {
        public static SupplierProductRow FromSummary(SupplierProductSummary summary)
        {
            return new SupplierProductRow(
                summary.ProductoProveedorId,
                summary.ProductoId,
                summary.CodigoProducto,
                summary.Producto,
                summary.Categoria,
                string.IsNullOrWhiteSpace(summary.Subcategoria)
                    ? "Sin subcategoría"
                    : summary.Subcategoria,
                summary.CodigoProductoProveedor ?? string.Empty,
                summary.EsProveedorPrincipal,
                summary.EsProveedorPrincipal ? "Si" : "No",
                summary.UltimoCostoCentavos.HasValue
                    ? SaleReceiptViewModel.FormatDollars(summary.UltimoCostoCentavos.Value)
                    : "-",
                summary.Activo,
                summary.Notas ?? string.Empty);
        }
    }

    private sealed record SupplierHistoryRow(
        string Fecha,
        string Tipo,
        string Usuario,
        string Descripcion)
    {
        public static SupplierHistoryRow FromEntry(SupplierHistoryEntry entry)
        {
            return new SupplierHistoryRow(
                SaleReceiptViewModel.ToLocal(entry.Fecha).ToString("yyyy-MM-dd HH:mm"),
                entry.Tipo,
                entry.Usuario,
                entry.Descripcion);
        }
    }

    private sealed record SupplierPurchaseRow(
        int CompraId,
        string CodigoCompra,
        string Fecha,
        string Estado,
        string Total,
        string UsuarioRegistro,
        int Comprado,
        int Recibido)
    {
        public int Pendiente => Comprado - Recibido;

        public static SupplierPurchaseRow FromSummary(PurchaseSummary summary)
        {
            return new SupplierPurchaseRow(
                summary.CompraId,
                summary.CodigoCompra,
                summary.FechaCompra.ToString("yyyy-MM-dd"),
                summary.EstadoRecepcion.ToString(),
                SaleReceiptViewModel.FormatDollars(summary.TotalCentavos),
                summary.UsuarioRegistro,
                summary.CantidadComprada,
                summary.CantidadRecibida);
        }
    }

    private sealed record SupplierAccountRow(
        AccountPayableSummary Source,
        string Cuenta,
        string Compra,
        string Vencimiento,
        string Total,
        string Pagado,
        string Saldo,
        string Estado);
}
