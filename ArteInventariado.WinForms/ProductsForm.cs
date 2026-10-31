using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class ProductsForm : Form
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly ISubcategoryService? _subcategoryService;
    private readonly IInventoryService _inventoryService;
    private readonly IProductCatalogService _catalogService;
    private readonly IProductAssetService _assetService;
    private readonly IBarcodeService _barcodeService;
    private readonly ICode128BarcodeRenderer _barcodeRenderer;
    private readonly IQrCodeRenderer? _qrCodeRenderer;
    private readonly IPeripheralOutputCoordinator? _peripheralOutput;
    private readonly IEmployeePermissionService _permissions;
    private readonly IInstallationContext _installation;
    private readonly BindingSource _generalSource = new();
    private readonly BindingSource _exhibitionSource = new();
    private readonly BindingSource _warehouseSource = new();
    private readonly TextBox _txtSearch = new();
    private readonly Button _btnClearSearch = new();
    private readonly ComboBox _cmbState = new();
    private readonly ComboBox _cmbMode = new();
    private readonly ComboBox _cmbBarcodeState = new();
    private readonly ComboBox _cmbCategory = new();
    private readonly ComboBox _cmbSubcategory = new();
    private readonly ComboBox _cmbImage = new();
    private readonly ComboBox _cmbLocation = new();
    private readonly ComboBox _cmbAvailability = new();
    private readonly ComboBox _cmbSort = new();
    private readonly CheckBox _chkLowStock = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnOpen = new();
    private readonly Button _btnActivate = new();
    private readonly Button _btnDeactivate = new();
    private readonly Button _btnDelete = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnGenerateMissingBarcodes = new();
    private readonly Button _btnBatchLabels = new();
    private readonly Button _btnBatchImages = new();
    private readonly DataGridView _generalGrid = CreateGrid();
    private readonly DataGridView _exhibitionGrid = CreateGrid();
    private readonly DataGridView _warehouseGrid = CreateGrid();
    private readonly PictureBox _mainPreview = new();
    private readonly Label _mainPreviewLabel = new();
    private readonly Label _resultsStatus = new();
    private FlowLayoutPanel? _filtersPanel;
    private FlowLayoutPanel? _actionsPanel;
    private TableLayoutPanel? _rootLayout;
    private TabControl? _tabs;
    private LiveSearchController? _liveSearch;
    private CancellationTokenSource? _previewCancellation;
    private List<ProductSummary> _allProducts = [];
    private List<ProductSummary> _visibleProducts = [];
    private List<CategorySummary> _categoryFilterOptions = [];
    private List<SubcategorySummary> _subcategoryFilterOptions = [];
    private bool _loadingFilterOptions;
    private bool _canManage;
    private bool _canManageLabels;
    private bool _canManageImages;
    private bool _canEditPurchaseCost;
    private bool _activationRedrawQueued;

    public ProductsForm(
        IProductService productService,
        ICategoryService categoryService,
        IInventoryService inventoryService,
        IProductCatalogService catalogService,
        IProductAssetService assetService,
        IBarcodeService barcodeService,
        ICode128BarcodeRenderer barcodeRenderer,
        IEmployeePermissionService permissions,
        IInstallationContext installation,
        bool loadDataOnShow = true,
        IQrCodeRenderer? qrCodeRenderer = null,
        IPeripheralOutputCoordinator? peripheralOutput = null,
        ISubcategoryService? subcategoryService = null)
    {
        _productService = productService;
        _categoryService = categoryService;
        _subcategoryService = subcategoryService;
        _inventoryService = inventoryService;
        _catalogService = catalogService;
        _assetService = assetService;
        _barcodeService = barcodeService;
        _barcodeRenderer = barcodeRenderer;
        _qrCodeRenderer = qrCodeRenderer;
        _peripheralOutput = peripheralOutput;
        _permissions = permissions;
        _installation = installation;
        BuildUi();
        _liveSearch = new LiveSearchController(_txtSearch, _btnClearSearch, ApplyLiveFilterAsync,
            ex => MessageBox.Show(ex.Message, "Productos", MessageBoxButtons.OK, MessageBoxIcon.Warning),
            onClearAdditionalState: ResetCatalogFilters,
            hasAdditionalClearableState: HasNonDefaultCatalogFilters);
        if (loadDataOnShow) Load += ProductsForm_Load;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
            CancelAndDispose(Interlocked.Exchange(ref _previewCancellation, null));
            var image = _mainPreview.Image;
            _mainPreview.Image = null;
            image?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        QueueVisibleCatalogRedraw();
    }

    private void QueueVisibleCatalogRedraw()
    {
        if (IsDisposed || Disposing || !IsHandleCreated || _activationRedrawQueued)
        {
            return;
        }

        _activationRedrawQueued = true;
        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                _activationRedrawQueued = false;
                if (IsDisposed || Disposing || !Visible || WindowState == FormWindowState.Minimized)
                {
                    return;
                }

                // Returning from another application can leave child controls with stale pixels.
                // Repaint only the existing view; do not reload data or disturb filters/selection.
                _rootLayout?.PerformLayout();
                _tabs?.PerformLayout();
                _rootLayout?.Invalidate(invalidateChildren: true);
                _tabs?.SelectedTab?.Refresh();
                var activeGrid = _tabs?.SelectedIndex switch
                {
                    1 => _exhibitionGrid,
                    2 => _warehouseGrid,
                    _ => _generalGrid
                };
                if (activeGrid?.Visible == true)
                {
                    activeGrid.PerformLayout();
                    activeGrid.Refresh();
                }
                _resultsStatus.Refresh();
            }));
        }
        catch (InvalidOperationException)
        {
            _activationRedrawQueued = false;
        }
    }

    private async void ProductsForm_Load(object? sender, EventArgs e)
    {
        _canManage = _installation.AllowsOperationalWrites &&
            await _permissions.HasPermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR);
        _canEditPurchaseCost = _installation.AllowsOperationalWrites &&
            await _permissions.HasPermissionAsync(PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR);
        _canManageLabels = _installation.AllowsOperationalWrites &&
            await _permissions.HasAnyPermissionAsync([
                PermisoEmpleado.PRODUCTOS_GESTIONAR,
                PermisoEmpleado.ETIQUETAS_GESTIONAR]);
        _canManageImages = _installation.AllowsOperationalWrites &&
            await _permissions.HasAnyPermissionAsync([
                PermisoEmpleado.PRODUCTOS_GESTIONAR,
                PermisoEmpleado.PRODUCTOS_IMAGENES_GESTIONAR]);
        SetCostVisibility(_canManage);
        await RefreshAsync();
    }

    private void BuildUi()
    {
        Text = "Productos e inventario";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(980, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var modeBadge = BuildModeBadge();
        var pageHeader = VisualLayout.CreatePageHeader(
            "Productos e inventario",
            "Consulta productos, imágenes, SKU y existencias por ubicación.",
            modeBadge);

        var filters = new FlowLayoutPanel
        {
            Name = "productsFilters",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoScroll = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(UITheme.SpaceMd, UITheme.SpaceSm, UITheme.SpaceMd, UITheme.SpaceSm),
            BackColor = UITheme.SurfaceMutedColor,
            Margin = Padding.Empty
        };

        _txtSearch.PlaceholderText = "Buscar por nombre, código, categoría, subcategoría o SKU...";
        UITheme.StyleInput(_txtSearch);
        _btnClearSearch.Text = "X";
        _btnClearSearch.AccessibleName = "Limpiar búsqueda y filtros";
        _btnClearSearch.Width = 42;
        UITheme.StyleOutlineButton(_btnClearSearch);
        _btnClearSearch.Margin = Padding.Empty;
        var searchHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = UITheme.StandardControlHeight,
            MinimumSize = new Size(0, UITheme.StandardControlHeight),
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        searchHost.RowStyles.Clear();
        searchHost.RowStyles.Add(new RowStyle(SizeType.Absolute, UITheme.StandardControlHeight));
        _txtSearch.Dock = DockStyle.Fill;
        _btnClearSearch.Dock = DockStyle.Fill;
        searchHost.Controls.Add(_txtSearch, 0, 0);
        searchHost.Controls.Add(_btnClearSearch, 1, 0);
        ConfigureFilter(_cmbState, ["Activos", "Inactivos", "Todos"], 0);
        ConfigureFilter(_cmbMode, ["Todos los modos", "Simple", "Surtido", "Por variantes"], 0);
        ConfigureFilter(_cmbBarcodeState, ["Todos", "Completo", "Pendiente", "Sin barcode"], 0);
        _cmbState.SelectedIndexChanged += (_, _) => { if (!_loadingFilterOptions) ApplyFilter(); };
        _cmbMode.SelectedIndexChanged += (_, _) => { if (!_loadingFilterOptions) ApplyFilter(); };
        _cmbBarcodeState.SelectedIndexChanged += (_, _) => { if (!_loadingFilterOptions) ApplyFilter(); };
        _chkLowStock.Text = "Solo bajo mínimo";
        _chkLowStock.Dock = DockStyle.Top;
        _chkLowStock.Padding = new Padding(4, 9, 0, 0);
        _chkLowStock.CheckedChanged += (_, _) => { if (!_loadingFilterOptions) ApplyFilter(); };
        _chkLowStock.Font = UITheme.NormalFont;

        ConfigureChoiceFilter(_cmbCategory, [(0, "Todas las categorías")]);
        ConfigureChoiceFilter(_cmbSubcategory, [(0, "Todas las subcategorías")]);
        ConfigureChoiceFilter(_cmbImage, [(0, "Todas las imágenes"), (1, "Con imagen"), (2, "Sin imagen")]);
        ConfigureChoiceFilter(_cmbLocation,
            [(0, "Todas las ubicaciones"), (1, "En exhibición"), (2, "En bodega"), (3, "Sin asignar")]);
        ConfigureChoiceFilter(_cmbAvailability,
            [(0, "Todas las existencias"), (1, "Con existencias"), (2, "Sin existencias")]);
        ConfigureChoiceFilter(_cmbSort,
            [(0, "Nombre A-Z"), (1, "Nombre Z-A"), (2, "Alta más reciente"),
                (3, "Alta más antigua"), (4, "Mayor existencia"), (5, "Menor existencia")]);
        _cmbCategory.SelectedValueChanged += (_, _) =>
        {
            if (_loadingFilterOptions) return;
            LoadSubcategoryFilterOptions();
            ApplyFilter();
        };
        _cmbSubcategory.SelectedValueChanged += (_, _) =>
        {
            if (!_loadingFilterOptions) ApplyFilter();
        };
        foreach (var combo in new[] { _cmbImage, _cmbLocation, _cmbAvailability, _cmbSort })
            combo.SelectedValueChanged += (_, _) =>
            {
                if (!_loadingFilterOptions) ApplyFilter();
            };

        filters.Controls.Add(CreateResponsiveField("Buscar", searchHost, 420));
        filters.Controls.Add(CreateResponsiveField("Estado", _cmbState, 160));
        filters.Controls.Add(CreateResponsiveField("Modo de producto", _cmbMode, 200));
        filters.Controls.Add(CreateResponsiveField("Barcode", _cmbBarcodeState, 160));
        filters.Controls.Add(CreateResponsiveField("Disponibilidad", _chkLowStock, 180));
        filters.Controls.Add(CreateResponsiveField("Categoría", _cmbCategory, 190));
        filters.Controls.Add(CreateResponsiveField("Subcategoría", _cmbSubcategory, 190));
        filters.Controls.Add(CreateResponsiveField("Imagen", _cmbImage, 170));
        filters.Controls.Add(CreateResponsiveField("Ubicación", _cmbLocation, 190));
        filters.Controls.Add(CreateResponsiveField("Existencias", _cmbAvailability, 190));
        filters.Controls.Add(CreateResponsiveField("Orden", _cmbSort, 190));

        var actions = VisualLayout.CreateActionBar();
        actions.Name = "productsActions";
        actions.AutoSize = false;
        actions.AutoScroll = false;
        actions.WrapContents = true;
        actions.FlowDirection = FlowDirection.LeftToRight;
        AddButton(actions, _btnNew, "Nuevo producto", BtnNew_Click, 170, primary: true);
        AddButton(actions, _btnOpen, "Abrir ficha", BtnOpen_Click, 130);
        AddButton(actions, _btnActivate, "Activar", async (_, _) => await SetStateAsync(EstadoProducto.ACTIVO), 105);
        AddButton(actions, _btnDeactivate, "Desactivar", async (_, _) => await SetStateAsync(EstadoProducto.INACTIVO), 115);
        AddButton(actions, _btnDelete, "Eliminar", BtnDelete_Click, 105, danger: true);
        AddButton(actions, _btnGenerateMissingBarcodes, "Generar códigos faltantes", async (_, _) => await OpenGenerateMissingBarcodesAsync(), 270);
        AddButton(actions, _btnBatchLabels, "Etiquetas en lote", async (_, _) => await OpenBatchLabelsAsync(), 175);
        AddButton(actions, _btnBatchImages, "Imágenes en lote", async (_, _) => await OpenBatchImagesAsync(), 175);
        AddButton(actions, _btnRefresh, "Actualizar", async (_, _) => await RefreshAsync(), 115);

        ConfigureGeneralGrid();
        ConfigureLocationGrid(_exhibitionGrid, _exhibitionSource, "Exhibicion");
        ConfigureLocationGrid(_warehouseGrid, _warehouseSource, "Bodega");
        _generalGrid.DoubleClick += BtnOpen_Click;
        _exhibitionGrid.DoubleClick += BtnOpen_Click;
        _warehouseGrid.DoubleClick += BtnOpen_Click;
        _generalGrid.SelectionChanged += async (_, _) => await LoadMainPreviewAsync();
        var tabs = new TabControl { Dock = DockStyle.Fill };
        UITheme.StyleTabControl(tabs);
        tabs.TabPages.Add(CreateTab("Productos generales", BuildGeneralContent()));
        tabs.TabPages.Add(CreateTab("Exhibicion", _exhibitionGrid));
        tabs.TabPages.Add(CreateTab("Bodega", _warehouseGrid));

        UITheme.StyleStatusLabel(_resultsStatus);
        _resultsStatus.Dock = DockStyle.Fill;
        _resultsStatus.Text = "Preparando catálogo...";

        var root = new TableLayoutPanel
        {
            Name = "productsRoot",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(UITheme.SpaceMd, UITheme.SpaceMd, UITheme.SpaceMd, UITheme.SpaceSm),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        pageHeader.Margin = new Padding(0, 0, 0, UITheme.SpaceSm);
        filters.Margin = new Padding(0, 0, 0, UITheme.SpaceSm);
        actions.Margin = new Padding(0, 0, 0, UITheme.SpaceSm);
        root.Controls.Add(pageHeader, 0, 0);
        root.Controls.Add(filters, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(tabs, 0, 3);
        root.Controls.Add(_resultsStatus, 0, 4);
        Controls.Add(root);
        _filtersPanel = filters;
        _actionsPanel = actions;
        _rootLayout = root;
        _tabs = tabs;
        root.Resize += (_, _) => UpdateResponsiveRows(root, filters, actions);
        root.Layout += (_, _) => UpdateResponsiveRows(root, filters, actions);
        filters.SizeChanged += (_, _) => UpdateResponsiveRows(root, filters, actions);
        actions.SizeChanged += (_, _) => UpdateResponsiveRows(root, filters, actions);
        UpdateResponsiveRows(root, filters, actions);
    }

    private Control BuildGeneralContent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(UITheme.SpaceMd),
            BackColor = UITheme.SurfaceColor,
            RowCount = 3,
            ColumnCount = 1
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        previewPanel.Controls.Add(new Label
        {
            Text = "Vista previa",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _mainPreview.Dock = DockStyle.Fill;
        _mainPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _mainPreview.BackColor = Color.White;
        _mainPreview.Paint += DrawImagePlaceholder;
        _mainPreviewLabel.Dock = DockStyle.Fill;
        _mainPreviewLabel.TextAlign = ContentAlignment.MiddleCenter;
        _mainPreviewLabel.Font = UITheme.NormalBoldFont;
        _mainPreviewLabel.ForeColor = UITheme.TextDark;
        previewPanel.Controls.Add(_mainPreview, 0, 1);
        previewPanel.Controls.Add(_mainPreviewLabel, 0, 2);
        layout.Controls.Add(_generalGrid, 0, 0);
        layout.Controls.Add(previewPanel, 1, 0);
        return layout;
    }

    private Control BuildModeBadge() => new Label
    {
        AutoSize = false,
        Size = new Size(170, UITheme.StandardControlHeight),
        MinimumSize = new Size(170, UITheme.StandardControlHeight),
        TextAlign = ContentAlignment.MiddleCenter,
        Text = _installation.Status.Mode == InstallationMode.Consulta ? "Modo Consulta" : "Modo Principal",
        BackColor = _installation.Status.Mode == InstallationMode.Consulta ? Color.FromArgb(255, 245, 224) : Color.FromArgb(235, 248, 239),
        ForeColor = UITheme.TextDark, Font = UITheme.NormalBoldFont
    };

    private void ConfigureGeneralGrid()
    {
        _generalGrid.DataSource = _generalSource;
        _generalGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        AddColumn(_generalGrid, "Codigo", "Codigo", 110);
        AddColumn(_generalGrid, "Nombre", "Producto", 210, fill: true);
        AddColumn(_generalGrid, "Categoria", "Categoria", 145);
        AddColumn(_generalGrid, "Subcategoria", "Subcategoría", 165);
        AddColumn(_generalGrid, "Modo", "Modo", 120);
        AddColumn(_generalGrid, "Precio", "Precio", 95, numeric: true);
        AddColumn(_generalGrid, "Costo", "Costo", 95, numeric: true);
        AddColumn(_generalGrid, "Total", "Total", 75, numeric: true);
        AddColumn(_generalGrid, "Disponible", "Disponible", 125, numeric: true);
        AddColumn(_generalGrid, "Exhibicion", "Exhib.", 82, numeric: true);
        AddColumn(_generalGrid, "Bodega", "Bodega", 90, numeric: true);
        AddColumn(_generalGrid, "SinAsignar", "Sin asignar", 125, numeric: true);
        AddColumn(_generalGrid, "Estado", "Estado", 95);
        AddColumn(_generalGrid, "Imagen", "Imagen", 95);
        SetCostVisibility(false);
    }

    private void SetCostVisibility(bool visible)
    {
        var column = _generalGrid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(item => item.DataPropertyName == "Costo");
        if (column is not null)
        {
            column.Visible = visible;
        }
    }

    private static void ConfigureLocationGrid(DataGridView grid, BindingSource source, string location)
    {
        grid.DataSource = source;
        AddColumn(grid, "Codigo", "Codigo", 120);
        AddColumn(grid, "Nombre", "Producto", 240, fill: true);
        AddColumn(grid, "Modo", "Modo", 130);
        AddColumn(grid, "Ubicacion", location, 110, numeric: true);
        AddColumn(grid, "Reservado", "Reservado", 100, numeric: true);
        AddColumn(grid, "Disponible", "Disponible total", 120, numeric: true);
        AddColumn(grid, "Distribucion", "Distribucion", 260);
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        try
        {
            _allProducts = (await _productService.GetAllAsync(new ProductFilter(SearchText: _txtSearch.Text))).ToList();
            await RefreshFilterOptionsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudieron cargar los productos.\n\n{ex.Message}", "Productos",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task ApplyLiveFilterAsync(CancellationToken cancellationToken)
    {
        _allProducts = (await _productService.GetAllAsync(
            new ProductFilter(SearchText: _txtSearch.Text), cancellationToken)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<ProductSummary> query = _allProducts;
        var state = _cmbState.SelectedIndex switch { 0 => EstadoProducto.ACTIVO, 1 => EstadoProducto.INACTIVO, _ => (EstadoProducto?)null };
        if (state.HasValue) query = query.Where(product => product.Estado == state.Value);
        var mode = _cmbMode.SelectedIndex switch
        {
            1 => ModoProducto.SIMPLE, 2 => ModoProducto.SURTIDO, 3 => ModoProducto.POR_VARIANTES, _ => (ModoProducto?)null
        };
        if (mode.HasValue) query = query.Where(product => product.Modo == mode.Value);
        var categoryId = (_cmbCategory.SelectedItem as ChoiceOption)?.Value ?? 0;
        if (categoryId > 0) query = query.Where(product => product.CategoriaId == categoryId);
        var subcategoryId = (_cmbSubcategory.SelectedItem as ChoiceOption)?.Value ?? 0;
        if (subcategoryId > 0) query = query.Where(product => product.SubcategoriaId == subcategoryId);
        else if (subcategoryId == -1) query = query.Where(product => product.SubcategoriaId is null);
        var barcodeState = _cmbBarcodeState.SelectedIndex switch
        {
            1 => EstadoBarcodeProducto.Completo,
            2 => EstadoBarcodeProducto.Pendiente,
            3 => EstadoBarcodeProducto.SinBarcode,
            _ => EstadoBarcodeProducto.Todos
        };
        if (barcodeState != EstadoBarcodeProducto.Todos)
        {
            query = query.Where(product => product.EstadoBarcode == barcodeState);
        }
        if (_chkLowStock.Checked) query = query.Where(product => product.StockActual <= product.StockMinimo);
        var imageFilter = (_cmbImage.SelectedItem as ChoiceOption)?.Value ?? 0;
        if (imageFilter == 1) query = query.Where(product => product.TieneImagen);
        else if (imageFilter == 2) query = query.Where(product => !product.TieneImagen);
        var locationFilter = (_cmbLocation.SelectedItem as ChoiceOption)?.Value ?? 0;
        query = locationFilter switch
        {
            1 => query.Where(product => product.StockExhibicion > 0),
            2 => query.Where(product => product.StockBodega > 0),
            3 => query.Where(product => product.StockSinAsignar > 0),
            _ => query
        };
        var availabilityFilter = (_cmbAvailability.SelectedItem as ChoiceOption)?.Value ?? 0;
        if (availabilityFilter == 1) query = query.Where(product => product.StockActual > 0);
        else if (availabilityFilter == 2) query = query.Where(product => product.StockActual <= 0);
        var sort = (_cmbSort.SelectedItem as ChoiceOption)?.Value ?? 0;
        _visibleProducts = sort switch
        {
            1 => query.OrderByDescending(product => product.Nombre).ThenBy(product => product.ProductoId).ToList(),
            2 => query.OrderByDescending(product => product.FechaCreacion).ThenBy(product => product.Nombre).ToList(),
            3 => query.OrderBy(product => product.FechaCreacion).ThenBy(product => product.Nombre).ToList(),
            4 => query.OrderByDescending(product => product.StockActual).ThenBy(product => product.Nombre).ToList(),
            5 => query.OrderBy(product => product.StockActual).ThenBy(product => product.Nombre).ToList(),
            _ => query.OrderBy(product => product.Nombre).ThenBy(product => product.ProductoId).ToList()
        };
        _generalSource.DataSource = _visibleProducts.Select(ProductRow.From).ToList();
        BindLocationRows();
        _resultsStatus.Text = _visibleProducts.Count == 0
            ? "No hay productos que coincidan con los filtros actuales."
            : $"{_visibleProducts.Count} producto(s) visibles · {_allProducts.Count} cargados.";
        _liveSearch?.RefreshClearButton();
    }

    private bool HasNonDefaultCatalogFilters() =>
        _cmbState.SelectedIndex != 0 ||
        _cmbMode.SelectedIndex != 0 ||
        _cmbBarcodeState.SelectedIndex != 0 ||
        _chkLowStock.Checked ||
        SelectedValue(_cmbCategory) != 0 ||
        SelectedValue(_cmbSubcategory) != 0 ||
        SelectedValue(_cmbImage) != 0 ||
        SelectedValue(_cmbLocation) != 0 ||
        SelectedValue(_cmbAvailability) != 0 ||
        SelectedValue(_cmbSort) != 0;

    private void ResetCatalogFilters()
    {
        var wasLoadingFilterOptions = _loadingFilterOptions;
        _loadingFilterOptions = true;
        try
        {
            _cmbState.SelectedIndex = 0;
            _cmbMode.SelectedIndex = 0;
            _cmbBarcodeState.SelectedIndex = 0;
            _chkLowStock.Checked = false;
            _cmbCategory.SelectedValue = 0;
            LoadSubcategoryFilterOptions();
            _cmbSubcategory.SelectedValue = 0;
            _cmbImage.SelectedValue = 0;
            _cmbLocation.SelectedValue = 0;
            _cmbAvailability.SelectedValue = 0;
            _cmbSort.SelectedValue = 0;
        }
        finally
        {
            _loadingFilterOptions = wasLoadingFilterOptions;
        }

        ApplyFilter();
    }

    private static int SelectedValue(ComboBox combo) =>
        (combo.SelectedItem as ChoiceOption)?.Value ?? 0;

    private void BindLocationRows()
    {
        _exhibitionSource.DataSource = _visibleProducts.Where(product => product.StockExhibicion > 0)
            .Select(product => LocationRow.From(product, product.StockExhibicion)).ToList();
        _warehouseSource.DataSource = _visibleProducts.Where(product => product.StockBodega > 0)
            .Select(product => LocationRow.From(product, product.StockBodega)).ToList();
    }

    private async Task RefreshFilterOptionsAsync()
    {
        _categoryFilterOptions = (await _categoryService.GetAllAsync()).ToList();
        _subcategoryFilterOptions = _subcategoryService is null
            ? _allProducts.Where(product => product.SubcategoriaId.HasValue)
                .Select(product => new SubcategorySummary(
                    product.SubcategoriaId!.Value,
                    product.CategoriaId,
                    product.SubcategoriaNombre ?? string.Empty,
                    null,
                    true))
                .DistinctBy(item => item.SubcategoriaId)
                .ToList()
            : (await _subcategoryService.GetAllAsync()).ToList();

        BindCategoryFilterOptions();
    }

    private void BindCategoryFilterOptions()
    {
        var selectedCategory = (_cmbCategory.SelectedItem as ChoiceOption)?.Value ?? 0;
        var wasLoadingFilterOptions = _loadingFilterOptions;
        _loadingFilterOptions = true;
        try
        {
            _cmbCategory.DataSource = null;
            _cmbCategory.DisplayMember = nameof(ChoiceOption.Label);
            _cmbCategory.ValueMember = nameof(ChoiceOption.Value);
            _cmbCategory.DataSource = new[] { new ChoiceOption(0, "Todas las categorías") }
                .Concat(_categoryFilterOptions
                    .OrderBy(item => item.Nombre)
                    .Select(item => new ChoiceOption(item.CategoriaId, item.Nombre)))
                .ToList();
            _cmbCategory.SelectedValue = _categoryFilterOptions.Any(item => item.CategoriaId == selectedCategory)
                ? selectedCategory
                : 0;
        }
        finally
        {
            _loadingFilterOptions = wasLoadingFilterOptions;
        }

        LoadSubcategoryFilterOptions();
    }

    private void LoadSubcategoryFilterOptions()
    {
        var selectedCategory = (_cmbCategory.SelectedItem as ChoiceOption)?.Value ?? 0;
        var previousSubcategory = (_cmbSubcategory.SelectedItem as ChoiceOption)?.Value ?? 0;
        var options = new List<ChoiceOption>
        {
            new(0, "Todas las subcategorías"),
            new(-1, "Sin subcategoría")
        };
        options.AddRange(_subcategoryFilterOptions
            .Where(item => selectedCategory == 0 || item.CategoriaId == selectedCategory)
            .OrderBy(item => item.Nombre)
            .Select(item => new ChoiceOption(item.SubcategoriaId, item.Nombre)));

        var wasLoadingFilterOptions = _loadingFilterOptions;
        _loadingFilterOptions = true;
        try
        {
            _cmbSubcategory.DataSource = null;
            _cmbSubcategory.DisplayMember = nameof(ChoiceOption.Label);
            _cmbSubcategory.ValueMember = nameof(ChoiceOption.Value);
            _cmbSubcategory.DataSource = options;
            _cmbSubcategory.SelectedValue = options.Any(item => item.Value == previousSubcategory)
                ? previousSubcategory
                : 0;
        }
        finally
        {
            _loadingFilterOptions = wasLoadingFilterOptions;
        }
    }

    private async Task LoadMainPreviewAsync()
    {
        if (IsDisposed || Disposing) return;
        var cancellation = new CancellationTokenSource();
        CancelAndDispose(Interlocked.Exchange(ref _previewCancellation, cancellation));
        var token = cancellation.Token;
        var current = _mainPreview.Image;
        _mainPreview.Image = null;
        current?.Dispose();
        _mainPreviewLabel.Text = "Sin imagen";
        if (_generalGrid.CurrentRow?.DataBoundItem is not ProductRow row) return;
        var product = _visibleProducts.SingleOrDefault(item => item.ProductoId == row.ProductoId);
        if (product is null) return;
        _mainPreviewLabel.Text = product.Nombre;
        try
        {
            var assets = await _assetService.GetByProductAsync(product.ProductoId, token);
            var asset = assets.FirstOrDefault(item => item.OwnerKind == ProductAssetOwnerKind.Product && item.IsPrimary)
                ?? assets.FirstOrDefault(item => item.OwnerKind == ProductAssetOwnerKind.Sku && item.IsPrimary);
            string? path = asset is null ? product.ImagenLocalPath :
                await _assetService.GetThumbnailPathAsync(asset.OwnerKind, asset.AssetId, 400, token);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            using var stream = new MemoryStream(await File.ReadAllBytesAsync(path, token));
            using var loaded = Image.FromStream(stream);
            if (!token.IsCancellationRequested) _mainPreview.Image = new Bitmap(loaded);
        }
        catch (OperationCanceledException) { }
        catch { _mainPreview.Image = null; }
        finally
        {
            if (!IsDisposed && !Disposing) _mainPreview.Invalidate();
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        cancellation.Dispose();
    }

    private static void DrawImagePlaceholder(object? sender, PaintEventArgs e)
    {
        if (sender is not PictureBox picture || picture.Image is not null) return;
        TextRenderer.DrawText(e.Graphics, "Sin imagen", UITheme.NormalFont, picture.ClientRectangle,
            UITheme.MutedTextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private async void BtnNew_Click(object? sender, EventArgs e)
    {
        if (!EnsureCanManage()) return;
        using var form = new ProductEditForm(
            _productService,
            _categoryService,
            subcategoryService: _subcategoryService,
            canEditPurchaseCost: _canEditPurchaseCost,
            assetService: _assetService,
            canManageImages: _canManageImages);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
    }

    private async void BtnOpen_Click(object? sender, EventArgs e)
    {
        var product = GetSelected();
        if (product is null) return;
        using var form = new ProductWorkspaceForm(product.ProductoId, _productService, _categoryService,
            _catalogService, _assetService, _inventoryService, _barcodeService, _barcodeRenderer,
            _canManage, _canManageLabels, _canManageImages,
            qrCodeRenderer: _qrCodeRenderer, peripheralOutput: _peripheralOutput,
            subcategories: _subcategoryService,
            canEditPurchaseCost: _canEditPurchaseCost);
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (!EnsureCanManage()) return;
        var product = GetSelected();
        if (product is null) return;
        if (MessageBox.Show($"Eliminar '{product.Nombre}'? Si tiene historia solo podra desactivarse.", "Productos",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { await _productService.DeleteAsync(product.ProductoId); await RefreshAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Producto protegido", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task SetStateAsync(EstadoProducto state)
    {
        if (!EnsureCanManage()) return;
        var product = GetSelected();
        if (product is null) return;
        try { await _productService.SetEstadoAsync(product.ProductoId, state); await RefreshAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Productos", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task OpenGenerateMissingBarcodesAsync()
    {
        if (!_canManageLabels)
        {
            MessageBox.Show("No tiene permiso para gestionar etiquetas.", "Etiquetas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var candidates = await _barcodeService.GetSkuCandidatesAsync();
            using var form = new GenerateMissingBarcodesForm(_barcodeService, candidates);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Generar códigos faltantes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task OpenBatchLabelsAsync()
    {
        try
        {
            var candidates = await _barcodeService.GetSkuCandidatesAsync();
            using var selection = new LabelBatchSelectionForm(candidates);
            if (selection.ShowDialog(this) != DialogResult.OK || selection.Batch is null) return;
            using var preview = new LabelPreviewForm(selection.Batch, _barcodeRenderer, peripheralOutput: _peripheralOutput);
            preview.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Etiquetas en lote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task OpenBatchImagesAsync()
    {
        if (!_canManageImages)
        {
            MessageBox.Show("No tiene permiso para gestionar imágenes de productos.", "Imágenes",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_visibleProducts.Count == 0)
        {
            MessageBox.Show("No hay productos visibles para preparar una asignación de imágenes.", "Imágenes en lote",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ProductImageBatchAssignmentForm(_visibleProducts, _catalogService, _assetService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private ProductSummary? GetSelected()
    {
        var productId = _tabs?.SelectedIndex switch
        {
            1 when _exhibitionGrid.CurrentRow?.DataBoundItem is LocationRow row => row.ProductoId,
            2 when _warehouseGrid.CurrentRow?.DataBoundItem is LocationRow row => row.ProductoId,
            _ when _generalGrid.CurrentRow?.DataBoundItem is ProductRow row => row.ProductoId,
            _ => (int?)null
        };
        if (productId.HasValue)
            return _visibleProducts.SingleOrDefault(product => product.ProductoId == productId.Value);
        MessageBox.Show("Seleccione un producto en la pestaña actual.", "Productos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    private bool EnsureCanManage()
    {
        if (_canManage) return true;
        MessageBox.Show("Esta estacion o usuario tiene acceso de solo lectura.", "Productos",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _btnRefresh.Enabled = !busy;
        foreach (var button in new[] { _btnNew, _btnActivate, _btnDeactivate, _btnDelete })
            button.Enabled = !busy && _canManage;
        _btnGenerateMissingBarcodes.Enabled = !busy && _canManageLabels;
        _btnOpen.Enabled = !busy;
        _btnBatchLabels.Enabled = !busy && _canManageLabels;
        _btnBatchImages.Enabled = !busy && _canManageImages;
    }

    private static DataGridView CreateGrid()
    {
        var grid = new BufferedProductGrid
        {
            Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        UITheme.StyleDataGridView(grid);
        return grid;
    }

    private sealed class BufferedProductGrid : DataGridView
    {
        public BufferedProductGrid() => DoubleBuffered = true;
    }

    private static TabPage CreateTab(string title, Control child)
    {
        var page = new TabPage(title) { Padding = new Padding(10), BackColor = UITheme.BackgroundColor };
        page.Controls.Add(child);
        return page;
    }

    private static void ConfigureFilter(ComboBox combo, object[] items, int selected)
    {
        combo.Dock = DockStyle.Fill; combo.DropDownStyle = ComboBoxStyle.DropDownList;
        UITheme.StyleInput(combo); combo.Items.AddRange(items); combo.SelectedIndex = selected;
    }

    private static void ConfigureChoiceFilter(ComboBox combo, IEnumerable<(int Value, string Label)> choices)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        UITheme.StyleInput(combo);
        combo.DisplayMember = nameof(ChoiceOption.Label);
        combo.ValueMember = nameof(ChoiceOption.Value);
        combo.DataSource = choices.Select(choice => new ChoiceOption(choice.Value, choice.Label)).ToList();
    }

    private static void AddButton(FlowLayoutPanel panel, Button button, string text, EventHandler handler, int width,
        bool primary = false, bool danger = false)
    {
        button.Text = text; button.Size = new Size(width, 36); button.Margin = new Padding(0, 0, UITheme.SpaceXs, 0); button.Click += handler;
        if (primary) UITheme.StylePrimaryButton(button);
        else if (danger) UITheme.StyleDangerButton(button);
        else UITheme.StyleSecondaryButton(button);
        panel.Controls.Add(button);
    }

    private static Control CreateResponsiveField(string label, Control input, int width)
    {
        var field = VisualLayout.CreateField(label, input);
        field.AutoSize = true;
        field.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        field.MinimumSize = new Size(width, 64);
        field.MaximumSize = new Size(width, 0);
        field.Width = width;
        return field;
    }

    private static void UpdateResponsiveRows(
        TableLayoutPanel root,
        FlowLayoutPanel filters,
        FlowLayoutPanel actions)
    {
        if (filters.ClientSize.Width <= 0 || actions.ClientSize.Width <= 0) return;

        var filterHeight = WrappedHeight(filters);
        var actionHeight = WrappedHeight(actions);
        if (root.RowStyles[1].Height == filterHeight && root.RowStyles[2].Height == actionHeight) return;

        root.RowStyles[1].Height = filterHeight;
        root.RowStyles[2].Height = actionHeight;
        root.PerformLayout();
    }

    private static int WrappedHeight(FlowLayoutPanel panel)
    {
        var availableWidth = Math.Max(1, panel.ClientSize.Width - panel.Padding.Horizontal);
        var rowWidth = 0;
        var rowHeight = 0;
        var rowsHeight = 0;
        var hasControls = false;

        foreach (Control child in panel.Controls)
        {
            if (!child.Visible) continue;
            hasControls = true;
            var childWidth = child.Width + child.Margin.Horizontal;
            var childHeight = child.Height + child.Margin.Vertical;
            if (rowWidth > 0 && rowWidth + childWidth > availableWidth)
            {
                rowsHeight += rowHeight;
                rowWidth = 0;
                rowHeight = 0;
            }

            rowWidth += childWidth;
            rowHeight = Math.Max(rowHeight, childHeight);
        }

        if (hasControls) rowsHeight += rowHeight;
        return Math.Max(panel.Padding.Vertical + rowsHeight, UITheme.StandardControlHeight + panel.Padding.Vertical);
    }

    private static void AddColumn(
        DataGridView grid,
        string property,
        string title,
        int width,
        bool fill = false,
        bool numeric = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property, HeaderText = title, Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            MinimumWidth = fill ? width : 20
        };
        if (numeric)
        {
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        grid.Columns.Add(column);
    }

    private static string Money(long cents) => (cents / 100m).ToString("C2");
    private static string Mode(ModoProducto mode) => mode switch
    {
        ModoProducto.SIMPLE => "Simple", ModoProducto.SURTIDO => "Surtido", _ => "Por variantes"
    };

    private sealed record ProductRow(int ProductoId, string Codigo, string Nombre, string Categoria, string Subcategoria, string Modo,
        string Precio, string Costo, int Total, int Disponible, int Exhibicion, int Bodega, int SinAsignar,
        string Estado, string Imagen)
    {
        public static ProductRow From(ProductSummary product) => new(product.ProductoId, product.CodigoInterno,
            product.Nombre, product.CategoriaNombre, DisplaySubcategory(product.SubcategoriaNombre), ProductsForm.Mode(product.Modo), Money(product.PrecioVentaCentavos),
            product.CostosVisibles ? Money(product.PrecioCompraCentavos) : string.Empty,
            product.StockActual, product.StockDisponible, product.StockExhibicion,
            product.StockBodega, product.StockSinAsignar, product.Estado.ToString(),
            product.ManagedImageRelativePath is null && product.ImagenLocalPath is null ? "Sin imagen" : "Principal");
    }

    private static string DisplaySubcategory(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Sin subcategoría" : name.Trim();

    private sealed record ChoiceOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    internal DataGridView GeneralGridForSmoke => _generalGrid;
    internal FlowLayoutPanel FiltersForSmoke => _filtersPanel ?? throw new InvalidOperationException("Filtros no inicializados.");
    internal FlowLayoutPanel ActionsForSmoke => _actionsPanel ?? throw new InvalidOperationException("Acciones no inicializadas.");
    internal TableLayoutPanel RootForSmoke => _rootLayout ?? throw new InvalidOperationException("Layout raíz no inicializado.");
    internal TabControl TabsForSmoke => _tabs ?? throw new InvalidOperationException("Pestañas no inicializadas.");
    internal Button ClearSearchButtonForSmoke => _btnClearSearch;
    internal TextBox SearchBoxForSmoke => _txtSearch;
    internal bool ShouldShowClearButtonForSmoke => _liveSearch?.ShouldShowClearButton ?? false;
    internal void ClearSearchForSmoke() => _liveSearch?.Clear();
    internal void CancelPendingSearchForSmoke() => _liveSearch?.CancelPending();
    internal ComboBox StateFilterForSmoke => _cmbState;
    internal ComboBox ModeFilterForSmoke => _cmbMode;
    internal ComboBox BarcodeFilterForSmoke => _cmbBarcodeState;
    internal CheckBox LowStockFilterForSmoke => _chkLowStock;
    internal ComboBox CategoryFilterForSmoke => _cmbCategory;
    internal ComboBox SubcategoryFilterForSmoke => _cmbSubcategory;
    internal ComboBox ImageFilterForSmoke => _cmbImage;
    internal ComboBox LocationFilterForSmoke => _cmbLocation;
    internal ComboBox AvailabilityFilterForSmoke => _cmbAvailability;
    internal ComboBox SortFilterForSmoke => _cmbSort;
    internal DataGridView ExhibitionGridForSmoke => _exhibitionGrid;
    internal DataGridView WarehouseGridForSmoke => _warehouseGrid;
    internal IReadOnlyList<int> VisibleProductIdsForSmoke =>
        _visibleProducts.Select(product => product.ProductoId).ToList();
    internal ProductSummary? SelectedProductForSmoke => GetSelected();

    internal void BindFilterOptionsForSmoke(
        IReadOnlyList<CategorySummary> categories,
        IReadOnlyList<SubcategorySummary> subcategories)
    {
        _categoryFilterOptions = categories.ToList();
        _subcategoryFilterOptions = subcategories.ToList();
        BindCategoryFilterOptions();
    }
    internal TableLayoutPanel SearchHostForSmoke =>
        (TableLayoutPanel)_btnClearSearch.Parent!;
    internal void SetSearchTextForLayoutSmoke(string value)
    {
        _txtSearch.Text = value;
        _liveSearch?.CancelPending();
    }
    internal void BindProductsForSmoke(IReadOnlyList<ProductSummary> products)
    {
        _allProducts = products.ToList();
        ApplyFilter();
        BindLocationRows();
    }

    private sealed record LocationRow(int ProductoId, string Codigo, string Nombre, string Modo, int Ubicacion, int Reservado,
        int Disponible, string Distribucion)
    {
        public static LocationRow From(ProductSummary product, int location) => new(product.ProductoId, product.CodigoInterno, product.Nombre,
            ProductsForm.Mode(product.Modo), location, product.StockReservado, product.StockDisponible,
            $"Exh. {product.StockExhibicion}  |  Bod. {product.StockBodega}  |  Sin asignar {product.StockSinAsignar}");
    }
}
