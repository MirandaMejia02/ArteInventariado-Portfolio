using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public sealed class ProductWorkspaceForm : Form
{
    private const int ActionBarHeight = 68;

    private readonly int _productId;
    private readonly IProductService _products;
    private readonly ICategoryService _categories;
    private readonly ISubcategoryService? _subcategories;
    private readonly IProductCatalogService _catalog;
    private readonly IProductAssetService _assets;
    private readonly IInventoryService _inventory;
    private readonly IBarcodeService _barcodes;
    private readonly ICode128BarcodeRenderer _barcodeRenderer;
    private readonly IQrCodeRenderer? _qrCodeRenderer;
    private readonly IPeripheralOutputCoordinator? _peripheralOutput;
    private readonly bool _canManage;
    private readonly bool _canManageLabels;
    private readonly bool _canManageImages;
    private readonly bool _canEditPurchaseCost;
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _generalInfo = new();
    private readonly BindingSource _skuSource = new();
    private readonly BindingSource _inventorySource = new();
    private readonly BindingSource _assetSource = new();
    private readonly BindingSource _barcodeSource = new();
    private readonly DataGridView _skuGrid = CreateGrid();
    private readonly DataGridView _inventoryGrid = CreateGrid();
    private readonly DataGridView _assetGrid = CreateGrid();
    private readonly DataGridView _barcodeGrid = CreateGrid();
    private readonly ComboBox _barcodeSku = new();
    private readonly ComboBox _assetOwner = new();
    private readonly PictureBox _preview = new();
    private readonly Label _previewCaption = new();
    private readonly Button _btnEditGeneral = new();
    private readonly List<Button> _writeButtons = [];
    private readonly List<Button> _labelButtons = [];
    private readonly List<Button> _imageButtons = [];
    private readonly ToolTip _toolTips = new()
    {
        AutoPopDelay = 8000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true
    };
    private ProductCatalogDetail? _detail;
    private List<ProductAssetSummary> _productAssets = [];
    private List<ProductSkuBarcodeSummary> _skuBarcodes = [];
    private List<InventoryBalanceSummary> _inventoryBalances = [];

    public ProductWorkspaceForm(int productId, IProductService products, ICategoryService categories,
        IProductCatalogService catalog, IProductAssetService assets, IInventoryService inventory,
        IBarcodeService barcodes, ICode128BarcodeRenderer barcodeRenderer, bool canManage,
        bool canManageLabels = false,
        bool canManageImages = false,
        bool loadDataOnShow = true,
        IQrCodeRenderer? qrCodeRenderer = null,
        IPeripheralOutputCoordinator? peripheralOutput = null,
        ISubcategoryService? subcategories = null,
        bool canEditPurchaseCost = true)
    {
        _productId = productId;
        _products = products;
        _categories = categories;
        _subcategories = subcategories;
        _catalog = catalog;
        _assets = assets;
        _inventory = inventory;
        _barcodes = barcodes;
        _barcodeRenderer = barcodeRenderer;
        _qrCodeRenderer = qrCodeRenderer;
        _peripheralOutput = peripheralOutput;
        _canManage = canManage;
        _canManageLabels = canManage || canManageLabels;
        _canManageImages = canManage || canManageImages;
        _canEditPurchaseCost = canEditPurchaseCost;
        BuildUi();
        if (loadDataOnShow) Load += async (_, _) => await RefreshAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = _preview.Image;
            _preview.Image = null;
            image?.Dispose();
            _toolTips.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Text = "Ficha de producto";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1400, 760);
        MinimumSize = new Size(960, 640);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var accessBadge = new Label
        {
            Text = _canManage ? "Edición habilitada" : _canManageImages ? "Imágenes habilitadas" : _canManageLabels ? "Etiquetas habilitadas" : "Solo lectura",
            AutoSize = false,
            Size = new Size(170, 38),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UITheme.NormalBoldFont,
            BackColor = _canManage || _canManageLabels || _canManageImages ? UITheme.DashboardAccentSoftColor : UITheme.DisabledSurfaceColor,
            ForeColor = _canManage || _canManageLabels || _canManageImages ? UITheme.PrimaryColor : UITheme.MutedTextColor
        };
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceMd, UITheme.SpaceLg, UITheme.SpaceMd),
            BackColor = UITheme.SurfaceColor,
            ColumnCount = 2,
            RowCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        _title.Dock = DockStyle.Fill;
        _title.Font = UITheme.TitleFont;
        _title.ForeColor = UITheme.TextDark;
        _title.TextAlign = ContentAlignment.BottomLeft;
        _subtitle.Dock = DockStyle.Fill;
        _subtitle.Font = UITheme.NormalFont;
        _subtitle.ForeColor = UITheme.MutedTextColor;
        _subtitle.TextAlign = ContentAlignment.TopLeft;
        header.Controls.Add(_title, 0, 0);
        header.Controls.Add(_subtitle, 0, 1);
        header.Controls.Add(accessBadge, 1, 0);
        header.SetRowSpan(accessBadge, 2);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        UITheme.StyleTabControl(tabs);
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildSkuTab());
        tabs.TabPages.Add(BuildInventoryTab());
        tabs.TabPages.Add(BuildBarcodeTab());
        tabs.TabPages.Add(BuildImagesTab());
        Controls.Add(tabs);
        Controls.Add(header);
        foreach (var button in _writeButtons) button.Enabled = _canManage;
        foreach (var button in _labelButtons) button.Enabled = _canManageLabels;
        foreach (var button in _imageButtons) button.Enabled = _canManageImages;
    }

    private TabPage BuildGeneralTab()
    {
        var page = Page("General");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(UITheme.SpaceLg) };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionBarHeight));
        _generalInfo.Dock = DockStyle.Fill; _generalInfo.Font = UITheme.SubheaderFont; _generalInfo.ForeColor = UITheme.TextDark;
        _generalInfo.TextAlign = ContentAlignment.TopLeft;

        var summaryHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor
        };
        summaryHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        summaryHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        var summary = VisualLayout.CreateSection(
            "Resumen del producto",
            "Información comercial e inventario consolidado de todos sus SKU.",
            _generalInfo);
        summary.Margin = new Padding(0, 0, UITheme.SpaceMd, 0);
        var inventoryNote = new Label
        {
            Text = "Inventario canónico\n\nLas existencias se muestran por SKU y ubicación. Reservado reduce la disponibilidad, pero no es una ubicación física.\n\nUsa la pestaña Inventario para transferir, clasificar o registrar merma.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark,
            BackColor = UITheme.DashboardAccentSoftColor,
            Padding = new Padding(UITheme.SpaceLg),
            TextAlign = ContentAlignment.TopLeft
        };
        summaryHost.Controls.Add(summary, 0, 0);
        summaryHost.Controls.Add(inventoryNote, 1, 0);

        _btnEditGeneral.Text = "Editar datos generales"; _btnEditGeneral.Size = new Size(220, 42);
        _btnEditGeneral.Click += async (_, _) => await EditGeneralAsync(); UITheme.StylePrimaryButton(_btnEditGeneral);
        _writeButtons.Add(_btnEditGeneral);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        actions.Controls.Add(_btnEditGeneral);
        panel.Controls.Add(summaryHost, 0, 0); panel.Controls.Add(actions, 0, 1);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildSkuTab()
    {
        var page = Page("Variantes / SKU");
        _skuGrid.DataSource = _skuSource;
        AddColumn(_skuGrid, "Codigo", "Código estable", 140);
        AddColumn(_skuGrid, "Variante", "Nombre / diseño", 200, true);
        AddColumn(_skuGrid, "Activo", "Activo", 75);
        AddColumn(_skuGrid, "Principal", "Principal", 85);
        AddColumn(_skuGrid, "Precio", "Precio efectivo", 115, numeric: true);
        if (_canManage)
        {
            AddColumn(_skuGrid, "Costo", "Costo efectivo", 115, numeric: true);
        }
        AddColumn(_skuGrid, "Minimo", "Mín. exhib.", 95, numeric: true);
        AddColumn(_skuGrid, "Stock", "Stock", 75, numeric: true);
        var actions = ActionBar();
        AddAction(actions, "Crear nuevo SKU", async (_, _) => await CreateSkuAsync(), 165, primary: true,
            toolTip: "Crea una variante identificable de este producto.");
        AddAction(actions, "Editar SKU seleccionado", async (_, _) => await EditSkuAsync(), 205,
            toolTip: "Modifica el nombre, precio, costo o mínimo del SKU seleccionado.");
        AddAction(actions, "Activar o desactivar SKU", async (_, _) => await ToggleSkuAsync(), 215,
            toolTip: "Cambia la disponibilidad del SKU seleccionado sin eliminar su historial.");
        AddAction(actions, "Eliminar SKU sin historial", async (_, _) => await DeleteSkuAsync(), 220, danger: true,
            toolTip: "Solo elimina un SKU que todavía no tenga movimientos registrados.");
        page.Controls.Add(_skuGrid); page.Controls.Add(actions);
        return page;
    }

    private TabPage BuildInventoryTab()
    {
        var page = Page("Inventario");
        _inventoryGrid.DataSource = _inventorySource;
        AddColumn(_inventoryGrid, "Codigo", "SKU", 140);
        AddColumn(_inventoryGrid, "Variante", "Variante", 180, true);
        AddColumn(_inventoryGrid, "Exhibicion", "Exhibición", 110, numeric: true);
        AddColumn(_inventoryGrid, "Bodega", "Bodega", 105, numeric: true);
        AddColumn(_inventoryGrid, "SinAsignar", "Sin asignar", 120, numeric: true);
        AddColumn(_inventoryGrid, "Reservado", "Reservado", 110, numeric: true);
        AddColumn(_inventoryGrid, "Disponible", "Disponible", 110, numeric: true);
        AddColumn(_inventoryGrid, "Total", "Físico", 95, numeric: true);
        var actions = ActionBar();
        actions.FlowDirection = FlowDirection.RightToLeft;
        AddAction(actions, "Configurar mínimo de exhibición", async (_, _) => await MinimumAsync(), 250,
            toolTip: "Define la cantidad mínima que debe mantenerse en exhibición.");
        AddAction(actions, "Registrar merma", async (_, _) => await WasteAsync(), 165, danger: true,
            toolTip: "Registra una pérdida o daño con su motivo correspondiente.");
        AddAction(actions, "Ajustar existencias", async (_, _) => await AdjustInventoryAsync(), 205,
            toolTip: "Corrige un conteo físico exacto por SKU y ubicación. Requiere ADMIN y reautenticación.");
        AddAction(actions, "Asignar a una ubicación", async (_, _) => await TransferAsync(true), 215, primary: true,
            toolTip: "Distribuye existencias pendientes de 'Sin asignar' hacia exhibición o bodega.");
        AddAction(actions, "Transferir entre ubicaciones", async (_, _) => await TransferAsync(false), 235,
            toolTip: "Mueve existencias entre exhibición, bodega y sin asignar.");
        page.Controls.Add(_inventoryGrid); page.Controls.Add(actions);
        return page;
    }

    private TabPage BuildBarcodeTab()
    {
        var page = Page("Códigos de barras");
        var selector = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(8),
            WrapContents = false
        };
        selector.Controls.Add(new Label
        {
            Text = "SKU:",
            AutoSize = true,
            Margin = new Padding(4, 9, 8, 0),
            Font = UITheme.NormalBoldFont
        });
        _barcodeSku.Width = 430;
        _barcodeSku.DropDownStyle = ComboBoxStyle.DropDownList;
        UITheme.StyleInput(_barcodeSku);
        _barcodeSku.SelectedIndexChanged += async (_, _) => await RefreshBarcodesAsync();
        selector.Controls.Add(_barcodeSku);
        var previewLabels = new Button
        {
            Text = "Vista previa de etiquetas",
            Size = new Size(230, 36),
            Margin = new Padding(16, 1, 0, 0),
            TextAlign = ContentAlignment.MiddleCenter
        };
        previewLabels.Click += (_, _) => PreviewLabels();
        UITheme.StyleSecondaryButton(previewLabels);
        UITheme.EnsureButtonContentFits(previewLabels, 230, 36, 36);
        _toolTips.SetToolTip(previewLabels,
            "Prepara etiquetas del SKU seleccionado sin requerir impresora física.");
        selector.Controls.Add(previewLabels);

        _barcodeGrid.DataSource = _barcodeSource;
        AddColumn(_barcodeGrid, "Codigo", "Código", 280, true);
        AddColumn(_barcodeGrid, "Tipo", "Tipo", 110);
        AddColumn(_barcodeGrid, "Activo", "Estado", 110);
        AddColumn(_barcodeGrid, "Principal", "Principal", 110);

        var actions = ActionBar();
        AddAction(actions, "Asignar código", async (_, _) => await AssignBarcodeAsync(), 165, primary: true,
            toolTip: "Asocia un código leído o escrito al SKU seleccionado.");
        AddAction(actions, "Generar código interno", async (_, _) => await GenerateBarcodeAsync(), 205,
            labelsOnly: true,
            toolTip: "Genera un identificador interno seguro y compatible con Code 128.");
        AddAction(actions, "Generar QR", async (_, _) => await GenerateQrAsync(), 145,
            labelsOnly: true,
            toolTip: "Genera el payload QR estable del SKU sin cambiar códigos existentes.");
        AddAction(actions, "Marcar como principal", async (_, _) => await SetPrimaryBarcodeAsync(), 205,
            toolTip: "Define el código activo preferido para este SKU.");
        AddAction(actions, "Activar o desactivar", async (_, _) => await ToggleBarcodeAsync(), 190,
            toolTip: "Conserva el código histórico y cambia si puede utilizarse en el POS.");
        page.Controls.Add(_barcodeGrid);
        page.Controls.Add(actions);
        page.Controls.Add(selector);
        return page;
    }

    private TabPage BuildImagesTab()
    {
        var page = Page("Imágenes");
        var ownerPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8), WrapContents = false };
        ownerPanel.Controls.Add(new Label { Text = "Galería de:", AutoSize = true, Margin = new Padding(4, 9, 8, 0), Font = UITheme.NormalBoldFont });
        _assetOwner.Width = 360; _assetOwner.DropDownStyle = ComboBoxStyle.DropDownList; UITheme.StyleInput(_assetOwner);
        _assetOwner.SelectedIndexChanged += (_, _) => ApplyAssetOwnerFilter(); ownerPanel.Controls.Add(_assetOwner);

        var gallery = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor
        };
        gallery.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        gallery.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        gallery.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _assetGrid.DataSource = _assetSource;
        _assetGrid.Margin = new Padding(0, 0, 10, 0);
        AddColumn(_assetGrid, "Owner", "Propietario", 145);
        AddColumn(_assetGrid, "File", "Archivo original", 160, true);
        AddColumn(_assetGrid, "Dimensions", "Dimensiones", 110);
        AddColumn(_assetGrid, "Size", "Tamaño", 80);
        AddColumn(_assetGrid, "Primary", "Principal", 80);
        _assetGrid.SelectionChanged += async (_, _) => await LoadPreviewAsync();

        var previewCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(10, 0, 0, 0),
            Padding = new Padding(12),
            RowCount = 3,
            ColumnCount = 1,
            BackColor = UITheme.SurfaceColor,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        previewCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var previewTitle = new Label
        {
            Text = "Vista previa",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            Padding = new Padding(6, 0, 0, 0)
        };
        _preview.Dock = DockStyle.Fill; _preview.Margin = new Padding(8); _preview.SizeMode = PictureBoxSizeMode.Zoom; _preview.BackColor = Color.White;
        _preview.Paint += (_, e) =>
        {
            if (_preview.Image is null)
                TextRenderer.DrawText(e.Graphics, "Selecciona una imagen\npara verla aquí", UITheme.NormalFont, _preview.ClientRectangle,
                    UITheme.MutedTextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        _previewCaption.Text = "Aquí verás el archivo seleccionado y sus dimensiones.";
        _previewCaption.Dock = DockStyle.Fill;
        _previewCaption.TextAlign = ContentAlignment.MiddleLeft;
        _previewCaption.Font = UITheme.NormalFont;
        _previewCaption.ForeColor = UITheme.MutedTextColor;
        _previewCaption.Padding = new Padding(6, 0, 6, 0);
        _previewCaption.AutoEllipsis = true;
        previewCard.Controls.Add(previewTitle, 0, 0);
        previewCard.Controls.Add(_preview, 0, 1);
        previewCard.Controls.Add(_previewCaption, 0, 2);
        gallery.Controls.Add(_assetGrid, 0, 0);
        gallery.Controls.Add(previewCard, 1, 0);

        var actions = ActionBar();
        AddAction(actions, "Importar imagen", async (_, _) => await ImportAssetAsync(), 170, primary: true, imagesOnly: true,
            toolTip: "Agrega un archivo JPG o PNG a la galería seleccionada.");
        AddAction(actions, "Usar como imagen principal", async (_, _) => await SetPrimaryAsync(), 230, imagesOnly: true,
            toolTip: "Convierte la imagen seleccionada en la principal del producto o SKU.");
        AddAction(actions, "Eliminar imagen", async (_, _) => await DeleteAssetAsync(), 165, danger: true, imagesOnly: true,
            toolTip: "Elimina de forma permanente la imagen seleccionada.");
        page.Controls.Add(gallery); page.Controls.Add(actions); page.Controls.Add(ownerPanel);
        return page;
    }

    private async Task RefreshAsync()
    {
        try
        {
            _detail = await _catalog.GetDetailAsync(_productId)
                ?? throw new InvalidOperationException("El producto ya no existe.");
            _productAssets = (await _assets.GetByProductAsync(_productId)).ToList();
            _inventoryBalances = (await _inventory.GetBalancesByProductAsync(_productId)).ToList();
            var product = _detail.Product;
            _title.Text = product.Nombre;
            _subtitle.Text = $"{product.CodigoInterno}  |  {ModeText(product.Modo)}  |  {product.Estado}";
            RenderGeneralSummary(product);
            var rows = _detail.Skus.Select(SkuRow.From).ToList();
            _skuSource.DataSource = rows;
            _inventorySource.DataSource = rows;
            var selectedBarcodeSkuId = (_barcodeSku.SelectedItem as BarcodeSkuChoice)?.ProductoSkuId;
            var barcodeChoices = _detail.Skus
                .Select(sku => new BarcodeSkuChoice(
                    sku.ProductoSkuId,
                    $"{sku.CodigoSku} · {sku.NombreVariante ?? (sku.EsPrincipal ? "Principal" : "Sin nombre")}"))
                .ToList();
            _barcodeSku.DataSource = barcodeChoices;
            _barcodeSku.DisplayMember = nameof(BarcodeSkuChoice.Text);
            if (selectedBarcodeSkuId.HasValue)
            {
                _barcodeSku.SelectedItem = barcodeChoices.FirstOrDefault(choice =>
                    choice.ProductoSkuId == selectedBarcodeSkuId.Value);
            }
            await RefreshBarcodesAsync();
            var selected = _assetOwner.SelectedItem as OwnerChoice;
            _assetOwner.DataSource = new[] { new OwnerChoice(ProductAssetOwnerKind.Product, _productId, "Producto (galeria general)") }
                .Concat(_detail.Skus.Select(sku => new OwnerChoice(ProductAssetOwnerKind.Sku, sku.ProductoSkuId,
                    $"SKU {sku.CodigoSku} - {sku.NombreVariante ?? "Principal"}"))).ToList();
            _assetOwner.DisplayMember = nameof(OwnerChoice.Text);
            if (selected is not null)
                _assetOwner.SelectedItem = ((List<OwnerChoice>)_assetOwner.DataSource).FirstOrDefault(choice => choice.Kind == selected.Kind && choice.Id == selected.Id);
            ApplyAssetOwnerFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ficha de producto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RenderGeneralSummary(ProductSummary product)
    {
        var commercialSummary = $"Precio base  {Money(product.PrecioVentaCentavos)}";
        if (_canManage && product.CostosVisibles)
        {
            commercialSummary += $"     Costo base  {Money(product.PrecioCompraCentavos)}";
        }

        _generalInfo.Text = $"Categoría\n{product.CategoriaNombre}\n\nSubcategoría\n{DisplaySubcategory(product.SubcategoriaNombre)}\n\n" +
            $"Descripción\n{product.Descripcion ?? "Sin descripción"}\n\n" +
            commercialSummary + "\n\n" +
            $"Físico  {product.StockActual}     Disponible  {product.StockDisponible}     Reservado  {product.StockReservado}\n\n" +
            $"Exhibición  {product.StockExhibicion}     Bodega  {product.StockBodega}     Sin asignar  {product.StockSinAsignar}\n\n" +
            $"Mínimo general  {product.StockMinimo}";
    }

    internal Label GeneralInfoForSmoke => _generalInfo;
    internal void ShowProductForSmoke(ProductCatalogDetail detail)
    {
        _detail = detail;
        _title.Text = detail.Product.Nombre;
        _subtitle.Text = $"{detail.Product.CodigoInterno}  |  {ModeText(detail.Product.Modo)}  |  {detail.Product.Estado}";
        RenderGeneralSummary(detail.Product);
        var rows = detail.Skus.Select(SkuRow.From).ToList();
        _skuSource.DataSource = rows;
        _inventorySource.DataSource = rows;
    }

    private async Task EditGeneralAsync()
    {
        if (_detail is null || !EnsureManage()) return;
        using var form = new ProductEditForm(
            _products,
            _categories,
            _detail.Product,
            subcategoryService: _subcategories,
            canEditPurchaseCost: _canEditPurchaseCost,
            assetService: _assets,
            canManageImages: _canManageImages);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
    }

    private async Task CreateSkuAsync()
    {
        if (_detail is null || !EnsureManage()) return;
        if (_detail.Product.Modo != ModoProducto.POR_VARIANTES)
        {
            MessageBox.Show("Cambie primero el modo del producto a POR_VARIANTES desde General.", "SKU",
                MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        using var dialog = new ProductSkuEditDialog(
            _productId,
            canEditPurchaseCost: _canEditPurchaseCost);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CreateRequest is null) return;
        await RunMutationAsync(() => _catalog.CreateSkuAsync(dialog.CreateRequest), "SKU creado.");
    }

    private async Task EditSkuAsync()
    {
        if (!EnsureManage() || GetSelectedSku(_skuGrid) is not { } sku) return;
        using var dialog = new ProductSkuEditDialog(_productId, sku, _canEditPurchaseCost);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.UpdateRequest is null) return;
        await RunMutationAsync(() => _catalog.UpdateSkuAsync(dialog.UpdateRequest), "SKU actualizado.");
    }

    private async Task ToggleSkuAsync()
    {
        if (!EnsureManage() || GetSelectedSku(_skuGrid) is not { } sku) return;
        await RunMutationAsync(async () => { await _catalog.SetSkuActiveAsync(sku.ProductoSkuId, !sku.Activo); return true; },
            sku.Activo ? "SKU desactivado." : "SKU activado.");
    }

    private async Task DeleteSkuAsync()
    {
        if (!EnsureManage() || GetSelectedSku(_skuGrid) is not { } sku) return;
        if (MessageBox.Show($"Eliminar el SKU '{sku.CodigoSku}'?", "SKU", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        await RunMutationAsync(async () => { await _catalog.DeleteSkuAsync(sku.ProductoSkuId); return true; }, "SKU eliminado.");
    }

    private async Task RefreshBarcodesAsync()
    {
        if (_barcodeSku.SelectedItem is not BarcodeSkuChoice selected || _barcodes is null)
        {
            _skuBarcodes = [];
            _barcodeSource.DataSource = Array.Empty<BarcodeRow>();
            return;
        }

        try
        {
            _skuBarcodes = (await _barcodes.GetBySkuAsync(selected.ProductoSkuId)).ToList();
            _barcodeSource.DataSource = _skuBarcodes.Select(BarcodeRow.From).ToList();
        }
        catch (Exception ex)
        {
            _skuBarcodes = [];
            _barcodeSource.DataSource = Array.Empty<BarcodeRow>();
            MessageBox.Show(ex.Message, "Códigos de barras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PreviewLabels()
    {
        if (_detail is null || _barcodeSku.SelectedItem is not BarcodeSkuChoice selected)
        {
            MessageBox.Show("Seleccione un SKU para preparar sus etiquetas.", "Etiquetas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sku = _detail.Skus.SingleOrDefault(item => item.ProductoSkuId == selected.ProductoSkuId);
        if (sku is null)
        {
            MessageBox.Show("El SKU seleccionado ya no esta disponible.", "Etiquetas",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var barcode = GetSelectedBarcode() ?? _skuBarcodes
            .Where(item => item.Activo)
            .OrderByDescending(item => item.EsPrincipal)
            .ThenBy(item => item.Codigo, StringComparer.Ordinal)
            .FirstOrDefault();
        if (barcode is null)
        {
            MessageBox.Show("Este SKU no tiene un código activo para imprimir.", "Etiquetas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var quantityDialog = new LabelQuantityDialog(barcode.Symbology == BarcodeSymbology.Qr);
        if (quantityDialog.ShowDialog(this) != DialogResult.OK) return;
        var label = LabelDataFactory.Create(
            _detail.Product,
            sku,
            barcode,
            quantityDialog.Quantity,
            quantityDialog.IncludePrice);
        using var preview = new LabelPreviewForm(
            label, _barcodeRenderer, quantityDialog.Profile, _qrCodeRenderer, _peripheralOutput);
        preview.ShowDialog(this);
    }

    private async Task AssignBarcodeAsync()
    {
        if (!EnsureManage() || _barcodeSku.SelectedItem is not BarcodeSkuChoice selected) return;
        using var dialog = new BarcodeEditDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Request is null) return;
        await RunBarcodeMutationAsync(
            () => _barcodes.AssignAsync(new AssignProductSkuBarcodeRequest(
                selected.ProductoSkuId, dialog.Request.Codigo, dialog.Request.EsPrincipal, dialog.Request.Symbology)),
            "Código de barras asignado.");
    }

    private async Task GenerateBarcodeAsync()
    {
        if (!EnsureLabelsManage() || _barcodeSku.SelectedItem is not BarcodeSkuChoice selected) return;
        if (MessageBox.Show(
                "¿Generar un código interno seguro para este SKU? La impresión de etiquetas se realizará en otro bloque.",
                "Generar código interno",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        await RunBarcodeMutationAsync(
            () => _barcodes.GenerateInternalAsync(new GenerateProductSkuBarcodeRequest(
                selected.ProductoSkuId, EsPrincipal: _skuBarcodes.All(item => !item.EsPrincipal))),
            "Código interno generado.");
    }

    private async Task GenerateQrAsync()
    {
        if (!EnsureLabelsManage() || _barcodeSku.SelectedItem is not BarcodeSkuChoice selected) return;
        await RunBarcodeMutationAsync(
            () => _barcodes.GenerateQrAsync(new GenerateProductSkuQrRequest(
                selected.ProductoSkuId, EsPrincipal: _skuBarcodes.All(item => !item.EsPrincipal))),
            "Código QR generado.");
    }

    private async Task SetPrimaryBarcodeAsync()
    {
        if (!EnsureManage() || GetSelectedBarcode() is not { } barcode) return;
        await RunBarcodeMutationAsync(async () =>
        {
            await _barcodes.SetPrincipalAsync(barcode.ProductoSkuBarcodeId);
            return barcode;
        }, "Código principal actualizado.");
    }

    private async Task ToggleBarcodeAsync()
    {
        if (!EnsureManage() || GetSelectedBarcode() is not { } barcode) return;
        await RunBarcodeMutationAsync(async () =>
        {
            await _barcodes.SetActiveAsync(barcode.ProductoSkuBarcodeId, !barcode.Activo);
            return barcode;
        }, barcode.Activo ? "Código desactivado." : "Código activado.");
    }

    private async Task RunBarcodeMutationAsync<T>(Func<Task<T>> action, string success)
    {
        try
        {
            await action();
            await RefreshBarcodesAsync();
            MessageBox.Show(success, "Códigos de barras", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Códigos de barras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task TransferAsync(bool classify)
    {
        if (!EnsureManage() || GetSelectedSku(_inventoryGrid) is not { } sku) return;
        var available = GetAvailableByLocation(sku.ProductoSkuId);
        if (classify && available.GetValueOrDefault(UbicacionInventario.SIN_ASIGNAR) <= 0)
        {
            MessageBox.Show("No hay inventario sin asignar para clasificar.", "Inventario",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new InventoryTransferDialog(sku, classify, available);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Request is null) return;
        await RunMutationAsync(() => _inventory.TransferAsync(dialog.Request), "Inventario transferido.");
    }

    private async Task WasteAsync()
    {
        if (!EnsureManage() || GetSelectedSku(_inventoryGrid) is not { } sku) return;
        using var dialog = new InventoryWasteDialog(sku, GetAvailableByLocation(sku.ProductoSkuId));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Request is null) return;
        await RunMutationAsync(() => _inventory.RegisterWasteAsync(dialog.Request), "Merma registrada.");
    }

    private async Task AdjustInventoryAsync()
    {
        if (!EnsureManage() || GetSelectedSku(_inventoryGrid) is not { } sku)
        {
            return;
        }

        var balances = _inventoryBalances
            .Where(item => item.ProductoSkuId == sku.ProductoSkuId)
            .ToList();
        using var dialog = new StockAdjustmentForm(_inventory, sku, balances);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private async Task MinimumAsync()
    {
        if (!EnsureManage() || GetSelectedSku(_inventoryGrid) is not { } sku) return;
        using var dialog = new ExhibitionMinimumDialog(sku);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunMutationAsync(async () =>
        {
            await _inventory.SetExhibitionMinimumAsync(new SetExhibitionMinimumRequest(sku.ProductoSkuId, dialog.Value));
            return true;
        }, "Minimo actualizado.");
    }

    private IReadOnlyDictionary<UbicacionInventario, int> GetAvailableByLocation(int productoSkuId) =>
        _inventoryBalances
            .Where(item => item.ProductoSkuId == productoSkuId)
            .ToDictionary(item => item.Ubicacion, item => item.Disponible);

    private async Task ImportAssetAsync()
    {
        if (!EnsureImagesManage() || _assetOwner.SelectedItem is not OwnerChoice owner) return;
        using var dialog = new OpenFileDialog { Title = "Importar imagen administrada", Filter = "Imagen JPEG o PNG|*.jpg;*.jpeg;*.png", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var request = new ImportProductAssetRequest(_productId, dialog.FileName,
            owner.Kind == ProductAssetOwnerKind.Sku ? owner.Id : null, SetAsPrimary: false);
        await RunMutationAsync(() => _assets.ImportAsync(request), "Imagen importada al almacenamiento administrado.");
    }

    private async Task SetPrimaryAsync()
    {
        if (!EnsureImagesManage() || GetSelectedAsset() is not { } asset) return;
        await RunMutationAsync(async () => { await _assets.SetPrimaryAsync(asset.OwnerKind, asset.AssetId); return true; },
            "Imagen principal actualizada.");
    }

    private async Task DeleteAssetAsync()
    {
        if (!EnsureImagesManage() || GetSelectedAsset() is not { } asset) return;
        if (MessageBox.Show("Eliminar esta imagen administrada?", "Imagenes", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        await RunMutationAsync(async () => { await _assets.DeleteAsync(asset.OwnerKind, asset.AssetId); return true; },
            "Imagen eliminada.");
    }

    private void ApplyAssetOwnerFilter()
    {
        if (_assetOwner.SelectedItem is not OwnerChoice owner) return;
        _assetSource.DataSource = _productAssets.Where(asset => asset.OwnerKind == owner.Kind && asset.OwnerId == owner.Id)
            .Select(asset => AssetRow.From(asset, owner.Text)).ToList();
        if (_assetGrid.Rows.Count == 0)
        {
            var current = _preview.Image;
            _preview.Image = null;
            current?.Dispose();
            _previewCaption.Text = "Esta galería todavía no contiene imágenes.";
            _preview.Invalidate();
        }
    }

    private async Task LoadPreviewAsync()
    {
        var current = _preview.Image; _preview.Image = null; current?.Dispose();
        var asset = GetSelectedAsset();
        if (asset is null)
        {
            _previewCaption.Text = "Selecciona una imagen de la tabla para ver su vista previa.";
            _preview.Invalidate();
            return;
        }
        _previewCaption.Text = $"{asset.OriginalFileName}  ·  {asset.Width} × {asset.Height} px  ·  {(asset.IsPrimary ? "Principal" : "Secundaria")}";
        try
        {
            var path = await _assets.GetThumbnailPathAsync(asset.OwnerKind, asset.AssetId, 480);
            if (path is null || !File.Exists(path)) return;
            using var stream = new MemoryStream(await File.ReadAllBytesAsync(path));
            using var image = Image.FromStream(stream);
            _preview.Image = new Bitmap(image);
        }
        catch
        {
            _preview.Image = null;
            _previewCaption.Text = "No fue posible preparar la vista previa de esta imagen.";
        }
        finally { _preview.Invalidate(); }
    }

    private ProductSkuSummary? GetSelectedSku(DataGridView grid)
    {
        if (grid.CurrentRow?.DataBoundItem is SkuRow row)
            return _detail?.Skus.SingleOrDefault(sku => sku.ProductoSkuId == row.ProductoSkuId);
        MessageBox.Show("Selecciona un SKU.", "Productos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    private ProductAssetSummary? GetSelectedAsset()
    {
        if (_assetGrid.CurrentRow?.DataBoundItem is AssetRow row)
            return _productAssets.SingleOrDefault(asset => asset.OwnerKind == row.OwnerKind && asset.AssetId == row.AssetId);
        return null;
    }

    private ProductSkuBarcodeSummary? GetSelectedBarcode()
    {
        if (_barcodeGrid.CurrentRow?.DataBoundItem is BarcodeRow row)
        {
            return _skuBarcodes.SingleOrDefault(item =>
                item.ProductoSkuBarcodeId == row.ProductoSkuBarcodeId);
        }

        MessageBox.Show("Seleccione un código de barras.", "Códigos de barras",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    private async Task RunMutationAsync<T>(Func<Task<T>> operation, string success)
    {
        try { await operation(); await RefreshAsync(); MessageBox.Show(success, "Productos", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Operacion no completada", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private bool EnsureManage()
    {
        if (_canManage) return true;
        MessageBox.Show("La ficha esta en modo de solo lectura.", "Productos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private bool EnsureLabelsManage()
    {
        if (_canManageLabels) return true;
        MessageBox.Show("No tiene permiso para gestionar etiquetas.", "Etiquetas",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private bool EnsureImagesManage()
    {
        if (_canManageImages) return true;
        MessageBox.Show("No tiene permiso para gestionar imágenes de productos.", "Imágenes",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private static TabPage Page(string title) => new(title) { Padding = new Padding(10), BackColor = UITheme.BackgroundColor };
    private FlowLayoutPanel ActionBar() => new()
    {
        Dock = DockStyle.Bottom,
        Height = ActionBarHeight,
        MinimumSize = new Size(0, ActionBarHeight),
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(10, 12, 10, 10),
        WrapContents = true,
        FlowDirection = FlowDirection.LeftToRight,
        BackColor = UITheme.SurfaceColor
    };
    private void AddAction(FlowLayoutPanel bar, string text, EventHandler click, int width = 140,
        bool primary = false, bool danger = false, bool labelsOnly = false, bool imagesOnly = false,
        string? toolTip = null)
    {
        var button = new Button { Text = text, Margin = new Padding(0, 0, 10, 0), AutoEllipsis = false };
        button.Click += click;
        if (danger) UITheme.StyleDangerButton(button);
        else if (primary) UITheme.StylePrimaryButton(button);
        else UITheme.StyleSecondaryButton(button);
        UITheme.EnsureButtonContentFits(button, width, 44, 36);
        button.Size = button.MinimumSize;
        button.TextAlign = ContentAlignment.MiddleCenter;
        if (!string.IsNullOrWhiteSpace(toolTip))
        {
            button.AccessibleDescription = toolTip;
            _toolTips.SetToolTip(button, toolTip);
        }
        button.Enabled = labelsOnly ? _canManageLabels : imagesOnly ? _canManageImages : _canManage;
        (labelsOnly ? _labelButtons : imagesOnly ? _imageButtons : _writeButtons).Add(button);
        bar.Controls.Add(button);
    }
    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        UITheme.StyleDataGridView(grid);
        grid.ColumnHeadersHeight = 48;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        return grid;
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
            DataPropertyName = property,
            HeaderText = title,
            Width = width,
            MinimumWidth = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
        };
        if (numeric)
        {
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        grid.Columns.Add(column);
    }
    private static string Money(long cents) => (cents / 100m).ToString("C2");
    private static string DisplaySubcategory(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Sin subcategoría" : name.Trim();
    private static string ModeText(ModoProducto mode) => mode switch { ModoProducto.SIMPLE => "Simple", ModoProducto.SURTIDO => "Surtido", _ => "Por variantes" };

    private sealed record OwnerChoice(ProductAssetOwnerKind Kind, int Id, string Text);
    private sealed record BarcodeSkuChoice(int ProductoSkuId, string Text);
    private sealed record BarcodeRow(int ProductoSkuBarcodeId, string Codigo, string Tipo, string Activo, string Principal)
    {
        public static BarcodeRow From(ProductSkuBarcodeSummary barcode) => new(
            barcode.ProductoSkuBarcodeId,
            barcode.Codigo,
            barcode.Symbology == BarcodeSymbology.Qr ? "QR" : "Code 128",
            barcode.Activo ? "Activo" : "Inactivo",
            barcode.EsPrincipal ? "Sí" : "No");
    }
    private sealed record SkuRow(int ProductoSkuId, string Codigo, string Variante, string Activo, string Principal,
        string Precio, string Costo, string Minimo, int Exhibicion, int Bodega, int SinAsignar, int Reservado,
        int Disponible, int Total, int Stock)
    {
        public static SkuRow From(ProductSkuSummary sku) => new(sku.ProductoSkuId, sku.CodigoSku,
            sku.NombreVariante ?? (sku.EsPrincipal ? "Principal" : "Sin nombre"), sku.Activo ? "Sí" : "No", sku.EsPrincipal ? "Sí" : "No",
            Money(sku.PrecioVentaEfectivoCentavos), sku.CostosVisibles ? Money(sku.CostoEfectivoCentavos) : string.Empty,
            sku.MinimoExhibicionOverride?.ToString() ?? "Producto",
            sku.StockExhibicion, sku.StockBodega, sku.StockSinAsignar, sku.StockReservado, sku.Disponible, sku.StockTotal, sku.StockTotal);
    }
    private sealed record AssetRow(int AssetId, ProductAssetOwnerKind OwnerKind, string Owner, string File,
        string Dimensions, string Size, string Primary)
    {
        public static AssetRow From(ProductAssetSummary asset, string owner) => new(asset.AssetId, asset.OwnerKind,
            owner, asset.OriginalFileName, $"{asset.Width} × {asset.Height}", $"{asset.SizeBytes / 1024d:0.#} KB", asset.IsPrimary ? "Sí" : "No");
    }
}

internal sealed class BarcodeEditDialog : Form
{
    private readonly TextBox _code = new();
    private readonly ComboBox _symbology = new();
    private readonly CheckBox _primary = new();

    public BarcodeEditDialog()
    {
        Text = "Asignar código de barras";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 250);
        MinimumSize = new Size(500, 240);
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "Código leído o escrito",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _code.Dock = DockStyle.Fill;
        _code.MaxLength = 128;
        _code.Font = UITheme.SubheaderFont;
        UITheme.StyleTextBox(_code);
        root.Controls.Add(_code, 0, 1);
        _symbology.DropDownStyle = ComboBoxStyle.DropDownList;
        _symbology.Items.AddRange(["Code 128", "QR"]);
        _symbology.SelectedIndex = 0;
        _symbology.Dock = DockStyle.Fill;
        UITheme.StyleInput(_symbology);
        root.Controls.Add(_symbology, 0, 2);
        _primary.Text = "Usar como código principal de este SKU";
        _primary.AutoSize = true;
        _primary.Font = UITheme.NormalFont;
        _primary.ForeColor = UITheme.TextDark;
        _primary.Margin = new Padding(0, 10, 0, 0);
        root.Controls.Add(_primary, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var save = new Button { Text = "Asignar código", Size = new Size(150, 38) };
        UITheme.StylePrimaryButton(save);
        save.Click += (_, _) => Save();
        var cancel = new Button { Text = "Cancelar", Size = new Size(120, 38), DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => _code.Focus();
    }

    public AssignProductSkuBarcodeRequest? Request { get; private set; }

    private void Save()
    {
        var code = _code.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Ingrese o escanee un código.", "Código de barras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _code.Focus();
            return;
        }

        Request = new AssignProductSkuBarcodeRequest(0, code, _primary.Checked,
            _symbology.SelectedIndex == 1 ? BarcodeSymbology.Qr : BarcodeSymbology.Code128);
        DialogResult = DialogResult.OK;
    }
}

internal sealed class ProductSkuEditDialog : Form
{
    private readonly int _productId;
    private readonly ProductSkuSummary? _sku;
    private readonly bool _canEditPurchaseCost;
    private readonly TextBox _code = new();
    private readonly TextBox _name = new();
    private readonly CheckBox _usePrice = new();
    private readonly CheckBox _useCost = new();
    private readonly CheckBox _useMinimum = new();
    private readonly NumericUpDown _price = MoneyInput();
    private readonly NumericUpDown _cost = MoneyInput();
    private readonly NumericUpDown _minimum = IntegerInput();
    public CreateProductSkuRequest? CreateRequest { get; private set; }
    public UpdateProductSkuRequest? UpdateRequest { get; private set; }

    public ProductSkuEditDialog(
        int productId,
        ProductSkuSummary? sku = null,
        bool canEditPurchaseCost = true)
    {
        _productId = productId; _sku = sku; _canEditPurchaseCost = canEditPurchaseCost;
        Text = sku is null ? "Crear SKU" : "Editar SKU";
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(520, 390); MaximizeBox = false; MinimizeBox = false; AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this); Build();
    }

    private void Build()
    {
        AddLabel("Codigo estable", 24, 22); _code.SetBounds(24, 48, 472, 32); UITheme.StyleTextBox(_code);
        AddLabel("Nombre / diseno", 24, 92); _name.SetBounds(24, 118, 472, 32); UITheme.StyleTextBox(_name);
        SetupOverride(_usePrice, _price, "Precio override", 24, 170);
        SetupOverride(_useCost, _cost, "Costo override", 264, 170, _canEditPurchaseCost);
        SetupOverride(_useMinimum, _minimum, "Minimo exhibicion", 24, 250);
        var save = new Button { Text = "Guardar", Bounds = new Rectangle(270, 330, 105, 38) };
        var cancel = new Button { Text = "Cancelar", Bounds = new Rectangle(391, 330, 105, 38), DialogResult = DialogResult.Cancel };
        UITheme.StylePrimaryButton(save); UITheme.StyleSecondaryButton(cancel); save.Click += Save;
        Controls.AddRange([_code, _name, _usePrice, _price, _useCost, _cost, _useMinimum, _minimum, save, cancel]);
        AcceptButton = save; CancelButton = cancel;
        if (_sku is null) return;
        _code.Text = _sku.CodigoSku; _code.ReadOnly = true; _name.Text = _sku.NombreVariante;
        SetOverride(_usePrice, _price, _sku.PrecioVentaOverrideCentavos, value => value / 100m);
        SetOverride(_useCost, _cost, _sku.CostoOverrideCentavos, value => value / 100m);
        SetOverride(_useMinimum, _minimum, _sku.MinimoExhibicionOverride, value => value);
    }

    private void Save(object? sender, EventArgs e)
    {
        try
        {
            if (_sku is null)
                CreateRequest = new CreateProductSkuRequest(_productId, _code.Text, _name.Text,
                    _usePrice.Checked ? ToCents(_price.Value) : null, _useCost.Checked ? ToCents(_cost.Value) : null,
                    _useMinimum.Checked ? (int)_minimum.Value : null);
            else
                UpdateRequest = new UpdateProductSkuRequest(_sku.ProductoSkuId, _name.Text,
                    _usePrice.Checked ? ToCents(_price.Value) : null, _useCost.Checked ? ToCents(_cost.Value) : null,
                    _useMinimum.Checked ? (int)_minimum.Value : null);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "SKU", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void SetupOverride(CheckBox check, NumericUpDown value, string text, int x, int y, bool canEdit = true)
    { check.Text = text; check.SetBounds(x, y, 190, 24); check.Enabled = canEdit;
      check.CheckedChanged += (_, _) => value.Enabled = canEdit && check.Checked;
      value.SetBounds(x, y + 28, 210, 32); value.Enabled = false; }
    private static void SetOverride<T>(CheckBox check, NumericUpDown input, T? value, Func<T, decimal> convert) where T : struct
    { check.Checked = value.HasValue; if (value.HasValue) input.Value = convert(value.Value); }
    private void AddLabel(string text, int x, int y) { var label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) }; UITheme.StyleLabelNormal(label); Controls.Add(label); }
    private static NumericUpDown MoneyInput() => new() { Maximum = 9_999_999, DecimalPlaces = 2, ThousandsSeparator = true, Font = UITheme.NormalFont };
    private static NumericUpDown IntegerInput() => new() { Maximum = 1_000_000, ThousandsSeparator = true, Font = UITheme.NormalFont };
    private static long ToCents(decimal value) => decimal.ToInt64(decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
}

internal static class InventoryDialogValidator
{
    public static string? ValidateSelectedSku(ProductSkuSummary? sku) =>
        sku is null ? "Selecciona un SKU." : null;

    public static string? ValidateTransfer(
        ProductSkuSummary? sku,
        UbicacionInventario? origin,
        UbicacionInventario? destination,
        int quantity,
        string? reason,
        int available,
        bool classify)
    {
        var skuError = ValidateSelectedSku(sku);
        if (skuError is not null) return skuError;
        if (!origin.HasValue) return "Selecciona una ubicación de origen.";
        if (!destination.HasValue) return "Selecciona una ubicación de destino.";
        if (origin == destination) return "La ubicación de origen y destino deben ser diferentes.";
        if (classify && origin == UbicacionInventario.SIN_ASIGNAR && available <= 0)
            return "No hay inventario sin asignar para clasificar.";
        if (quantity <= 0) return "Ingresa una cantidad mayor que cero.";
        if (quantity > available)
            return $"Solo hay {available} unidades disponibles en {LocationName(origin.Value)}.";
        if (string.IsNullOrWhiteSpace(reason))
            return classify ? "Ingresa el motivo de la clasificación." : "Ingresa el motivo de la transferencia.";
        return null;
    }

    public static string? ValidateWaste(
        ProductSkuSummary? sku,
        UbicacionInventario? location,
        int quantity,
        string? reason,
        int available)
    {
        var skuError = ValidateSelectedSku(sku);
        if (skuError is not null) return skuError;
        if (!location.HasValue) return "Selecciona una ubicación.";
        if (quantity <= 0) return "Ingresa una cantidad mayor que cero.";
        if (quantity > available)
            return $"Solo hay {available} unidades disponibles en {LocationName(location.Value)}.";
        if (string.IsNullOrWhiteSpace(reason)) return "Ingresa el motivo de la merma.";
        return null;
    }

    public static string? ValidateMinimum(ProductSkuSummary? sku, bool inherit, int minimum)
    {
        var skuError = ValidateSelectedSku(sku);
        if (skuError is not null) return skuError;
        if (!inherit && minimum < 0) return "Ingresa un mínimo de exhibición válido.";
        return null;
    }

    public static string LocationName(UbicacionInventario location) => location switch
    {
        UbicacionInventario.EXHIBICION => "Exhibición",
        UbicacionInventario.BODEGA => "Bodega",
        UbicacionInventario.SIN_ASIGNAR => "Sin asignar",
        _ => location.ToString()
    };
}

internal sealed class InventoryTransferDialog : Form
{
    private readonly ProductSkuSummary _sku;
    private readonly bool _classify;
    private readonly IReadOnlyDictionary<UbicacionInventario, int> _available;
    private readonly ComboBox _from = new();
    private readonly ComboBox _to = new();
    private readonly NumericUpDown _quantity = new();
    private readonly TextBox _reason = new();
    private readonly Label _availableLabel = new();

    public TransferInventoryRequest? Request { get; private set; }

    public InventoryTransferDialog(
        ProductSkuSummary sku,
        bool classify,
        IReadOnlyDictionary<UbicacionInventario, int>? available = null)
    {
        _sku = sku;
        _classify = classify;
        _available = available ?? new Dictionary<UbicacionInventario, int>
        {
            [UbicacionInventario.EXHIBICION] = sku.StockExhibicion,
            [UbicacionInventario.BODEGA] = sku.StockBodega,
            [UbicacionInventario.SIN_ASIGNAR] = sku.StockSinAsignar
        };
        Text = classify ? "Clasificar inventario sin asignar" : "Transferir entre ubicaciones";
        BuildDialog(this, new Size(600, 390), new Size(560, 410));
        BuildUi();
    }

    private void BuildUi()
    {
        var root = CreateRoot(2, 7);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var summary = BuildSummary(_classify
            ? $"SKU {_sku.CodigoSku} · Origen fijo: SIN_ASIGNAR"
            : $"SKU {_sku.CodigoSku} · El total físico no cambia al transferir.");
        root.Controls.Add(summary, 0, 0);
        root.SetColumnSpan(summary, 2);
        root.Controls.Add(BuildLabel("Ubicación de origen"), 0, 1);
        root.Controls.Add(BuildLabel("Ubicación de destino"), 1, 1);

        var transferLocations = new[] { UbicacionInventario.BODEGA, UbicacionInventario.EXHIBICION };
        ConfigureLocation(_from, _classify ? [UbicacionInventario.SIN_ASIGNAR] : transferLocations,
            "Ubicación de origen");
        ConfigureLocation(_to, transferLocations, "Ubicación de destino");
        root.Controls.Add(_from, 0, 2);
        root.Controls.Add(_to, 1, 2);

        _availableLabel.Dock = DockStyle.Fill;
        _availableLabel.TextAlign = ContentAlignment.MiddleLeft;
        _availableLabel.Font = UITheme.NormalBoldFont;
        _availableLabel.ForeColor = UITheme.PrimaryColor;
        root.Controls.Add(_availableLabel, 0, 3);
        root.SetColumnSpan(_availableLabel, 2);

        root.Controls.Add(BuildLabel("Cantidad"), 0, 4);
        root.Controls.Add(BuildLabel("Motivo obligatorio"), 1, 4);
        ConfigureQuantity(_quantity, "Cantidad a transferir");
        _reason.Dock = DockStyle.Fill;
        _reason.AccessibleName = "Motivo de la transferencia";
        UITheme.StyleTextBox(_reason);
        root.Controls.Add(_quantity, 0, 5);
        root.Controls.Add(_reason, 1, 5);

        var actions = BuildActions(_classify ? "Clasificar inventario" : "Confirmar transferencia", Save);
        root.Controls.Add(actions, 0, 6);
        root.SetColumnSpan(actions, 2);
        Controls.Add(root);

        if (_classify)
        {
            _from.SelectedIndex = 0;
            _from.Enabled = false;
            _to.SelectedIndex = -1;
        }
        else if (AvailableAt(UbicacionInventario.BODEGA) > 0)
        {
            SelectLocation(_from, UbicacionInventario.BODEGA);
            SelectLocation(_to, UbicacionInventario.EXHIBICION);
        }
        else
        {
            SelectLocation(_from, UbicacionInventario.EXHIBICION);
            SelectLocation(_to, UbicacionInventario.BODEGA);
        }

        _from.SelectedIndexChanged += (_, _) => UpdateAvailable();
        UpdateAvailable();
        Shown += (_, _) => (_classify ? (Control)_to : _quantity).Focus();
    }

    private void Save(object? sender, EventArgs e)
    {
        var origin = SelectedLocation(_from);
        var destination = SelectedLocation(_to);
        var available = origin.HasValue ? AvailableAt(origin.Value) : 0;
        var error = InventoryDialogValidator.ValidateTransfer(
            _sku, origin, destination, (int)_quantity.Value, _reason.Text, available, _classify);
        if (ShowValidation(error)) return;

        Request = new TransferInventoryRequest(Guid.NewGuid(), _sku.ProductoSkuId,
            origin!.Value, destination!.Value, (int)_quantity.Value, _reason.Text.Trim());
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateAvailable()
    {
        var origin = SelectedLocation(_from);
        _availableLabel.Text = origin.HasValue
            ? $"Disponible en {InventoryDialogValidator.LocationName(origin.Value)}: {AvailableAt(origin.Value)} unidades."
            : "Selecciona el origen para consultar su disponibilidad.";
    }

    private int AvailableAt(UbicacionInventario location) => _available.GetValueOrDefault(location);

    internal static void BuildDialog(Form form, Size clientSize, Size minimumSize)
    {
        form.StartPosition = FormStartPosition.CenterParent;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.ClientSize = clientSize;
        form.MinimumSize = minimumSize;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(form);
    }

    internal static TableLayoutPanel CreateRoot(int columns, int rows) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = columns,
        RowCount = rows,
        Padding = new Padding(22),
        BackColor = UITheme.BackgroundColor
    };

    internal static Label BuildSummary(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = UITheme.NormalBoldFont,
        ForeColor = UITheme.TextDark,
        BackColor = UITheme.SoftPinkColor,
        Padding = new Padding(12, 0, 12, 0),
        AutoEllipsis = true
    };

    internal static Label BuildLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        Font = UITheme.NormalBoldFont,
        ForeColor = UITheme.TextDark
    };

    internal static void ConfigureLocation(
        ComboBox combo,
        IEnumerable<UbicacionInventario> locations,
        string accessibleName)
    {
        combo.Dock = DockStyle.Fill;
        combo.Margin = new Padding(0, 2, 12, 4);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Font = UITheme.NormalFont;
        combo.AccessibleName = accessibleName;
        combo.Items.AddRange(locations.Select(location => new LocationChoice(
            location, InventoryDialogValidator.LocationName(location))).Cast<object>().ToArray());
        combo.DisplayMember = nameof(LocationChoice.Text);
        combo.SelectedIndex = -1;
    }

    internal static void ConfigureQuantity(NumericUpDown quantity, string accessibleName)
    {
        quantity.Dock = DockStyle.Fill;
        quantity.Margin = new Padding(0, 2, 12, 4);
        quantity.Minimum = 0;
        quantity.Maximum = 1_000_000;
        quantity.ThousandsSeparator = true;
        quantity.Font = UITheme.NormalFont;
        quantity.AccessibleName = accessibleName;
    }

    internal static FlowLayoutPanel BuildActions(string saveText, EventHandler saveHandler)
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0)
        };
        var save = new Button { Text = saveText, Margin = new Padding(10, 0, 0, 0) };
        UITheme.StylePrimaryButton(save);
        UITheme.EnsureButtonContentFits(save, 190, 42, 38);
        save.Size = save.MinimumSize;
        save.TextAlign = ContentAlignment.MiddleCenter;
        save.Click += saveHandler;
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.EnsureButtonContentFits(cancel, 120, 42, 38);
        cancel.Size = cancel.MinimumSize;
        cancel.TextAlign = ContentAlignment.MiddleCenter;
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        return actions;
    }

    internal bool ShowValidation(string? error)
    {
        if (error is null) return false;
        MessageBox.Show(error, "Inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    internal static UbicacionInventario? SelectedLocation(ComboBox combo) =>
        (combo.SelectedItem as LocationChoice)?.Value;

    private static void SelectLocation(ComboBox combo, UbicacionInventario location)
    {
        var choice = combo.Items.Cast<LocationChoice>().First(item => item.Value == location);
        combo.SelectedItem = choice;
    }

    private sealed record LocationChoice(UbicacionInventario Value, string Text);
}

internal sealed class InventoryWasteDialog : Form
{
    private readonly ProductSkuSummary _sku;
    private readonly IReadOnlyDictionary<UbicacionInventario, int> _available;
    private readonly ComboBox _location = new();
    private readonly NumericUpDown _quantity = new();
    private readonly TextBox _reason = new();
    private readonly Label _availableLabel = new();

    public RegisterInventoryWasteRequest? Request { get; private set; }

    public InventoryWasteDialog(
        ProductSkuSummary sku,
        IReadOnlyDictionary<UbicacionInventario, int>? available = null)
    {
        _sku = sku;
        _available = available ?? new Dictionary<UbicacionInventario, int>
        {
            [UbicacionInventario.EXHIBICION] = sku.StockExhibicion,
            [UbicacionInventario.BODEGA] = sku.StockBodega,
            [UbicacionInventario.SIN_ASIGNAR] = sku.StockSinAsignar
        };
        Text = "Registrar merma";
        InventoryTransferDialog.BuildDialog(this, new Size(540, 420), new Size(500, 440));
        BuildUi();
    }

    private void BuildUi()
    {
        var root = InventoryTransferDialog.CreateRoot(1, 9);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(InventoryTransferDialog.BuildSummary(
            $"SKU {_sku.CodigoSku} · La merma reduce el inventario físico."), 0, 0);
        root.Controls.Add(InventoryTransferDialog.BuildLabel("Ubicación"), 0, 1);
        InventoryTransferDialog.ConfigureLocation(_location, Enum.GetValues<UbicacionInventario>(),
            "Ubicación de la merma");
        root.Controls.Add(_location, 0, 2);
        _availableLabel.Dock = DockStyle.Fill;
        _availableLabel.TextAlign = ContentAlignment.MiddleLeft;
        _availableLabel.Font = UITheme.NormalBoldFont;
        _availableLabel.ForeColor = UITheme.PrimaryColor;
        root.Controls.Add(_availableLabel, 0, 3);
        root.Controls.Add(InventoryTransferDialog.BuildLabel("Cantidad"), 0, 4);
        InventoryTransferDialog.ConfigureQuantity(_quantity, "Cantidad de la merma");
        root.Controls.Add(_quantity, 0, 5);
        root.Controls.Add(InventoryTransferDialog.BuildLabel("Motivo obligatorio"), 0, 6);
        _reason.Dock = DockStyle.Fill;
        _reason.AccessibleName = "Motivo obligatorio de la merma";
        UITheme.StyleTextBox(_reason);
        root.Controls.Add(_reason, 0, 7);
        var actions = InventoryTransferDialog.BuildActions("Registrar merma", Save);
        root.Controls.Add(actions, 0, 8);
        Controls.Add(root);

        var initial = _available.GetValueOrDefault(UbicacionInventario.BODEGA) > 0
            ? UbicacionInventario.BODEGA
            : _available.OrderByDescending(item => item.Value).Select(item => item.Key).FirstOrDefault();
        _location.SelectedIndex = Enum.GetValues<UbicacionInventario>().ToList().IndexOf(initial);
        _location.SelectedIndexChanged += (_, _) => UpdateAvailable();
        UpdateAvailable();
        Shown += (_, _) => _quantity.Focus();
    }

    private void Save(object? sender, EventArgs e)
    {
        var location = InventoryTransferDialog.SelectedLocation(_location);
        var available = location.HasValue ? _available.GetValueOrDefault(location.Value) : 0;
        var error = InventoryDialogValidator.ValidateWaste(
            _sku, location, (int)_quantity.Value, _reason.Text, available);
        if (ShowValidation(error)) return;

        Request = new RegisterInventoryWasteRequest(Guid.NewGuid(), _sku.ProductoSkuId,
            location!.Value, (int)_quantity.Value, _reason.Text.Trim());
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateAvailable()
    {
        var location = InventoryTransferDialog.SelectedLocation(_location);
        _availableLabel.Text = location.HasValue
            ? $"Disponible en {InventoryDialogValidator.LocationName(location.Value)}: {_available.GetValueOrDefault(location.Value)} unidades."
            : "Selecciona una ubicación para consultar su disponibilidad.";
    }

    private static bool ShowValidation(string? error)
    {
        if (error is null) return false;
        MessageBox.Show(error, "Merma", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }
}

internal sealed class ExhibitionMinimumDialog : Form
{
    private readonly ProductSkuSummary _sku;
    private readonly CheckBox _inherit = new();
    private readonly NumericUpDown _value = new();
    public int? Value => _inherit.Checked ? null : (int)_value.Value;

    public ExhibitionMinimumDialog(ProductSkuSummary sku)
    {
        _sku = sku;
        Text = "Configurar mínimo de exhibición";
        InventoryTransferDialog.BuildDialog(this, new Size(500, 330), new Size(460, 350));
        BuildUi();
    }

    private void BuildUi()
    {
        var root = InventoryTransferDialog.CreateRoot(1, 6);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.Controls.Add(InventoryTransferDialog.BuildSummary(
            $"SKU {_sku.CodigoSku} · Define la alerta de reposición para exhibición."), 0, 0);
        _inherit.Text = "Usar el mínimo general del producto";
        _inherit.Dock = DockStyle.Fill;
        _inherit.Font = UITheme.NormalFont;
        _inherit.ForeColor = UITheme.TextDark;
        _inherit.AccessibleName = "Usar mínimo general del producto";
        _inherit.CheckedChanged += (_, _) => _value.Enabled = !_inherit.Checked;
        root.Controls.Add(_inherit, 0, 1);
        root.Controls.Add(InventoryTransferDialog.BuildLabel("Mínimo específico"), 0, 2);
        InventoryTransferDialog.ConfigureQuantity(_value, "Mínimo específico de exhibición");
        root.Controls.Add(_value, 0, 3);
        var help = new Label
        {
            Text = "Usa cero si no deseas exigir unidades mínimas para este SKU.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor
        };
        root.Controls.Add(help, 0, 4);
        var actions = InventoryTransferDialog.BuildActions("Guardar mínimo", Save);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);

        _inherit.Checked = !_sku.MinimoExhibicionOverride.HasValue;
        if (_sku.MinimoExhibicionOverride.HasValue) _value.Value = _sku.MinimoExhibicionOverride.Value;
        _value.Enabled = !_inherit.Checked;
    }

    private void Save(object? sender, EventArgs e)
    {
        var error = InventoryDialogValidator.ValidateMinimum(_sku, _inherit.Checked, (int)_value.Value);
        if (error is not null)
        {
            MessageBox.Show(error, "Inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
