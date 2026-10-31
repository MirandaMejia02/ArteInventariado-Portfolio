using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public sealed class EmployeeInventoryLookupForm : Form
{
    private readonly IEmployeeInventoryLookupService _lookupService;
    private readonly IProductAssetService _assets;
    private readonly ICategoryService? _categoryService;
    private readonly ISubcategoryService? _subcategoryService;
    private readonly BindingSource _binding = new();
    private readonly TextBox _search = new();
    private readonly Button _clear = new();
    private readonly ComboBox _categoryFilter = new();
    private readonly ComboBox _subcategoryFilter = new();
    private readonly ComboBox _imageFilter = new();
    private readonly ComboBox _locationFilter = new();
    private readonly ComboBox _stockFilter = new();
    private readonly ComboBox _sortFilter = new();
    private readonly DataGridView _grid = new();
    private readonly PictureBox _preview = new();
    private readonly Label _previewCaption = new();
    private readonly Label _status = new();
    private LiveSearchController? _liveSearch;
    private CancellationTokenSource? _previewCts;
    private IReadOnlyList<EmployeeInventoryLookupItem> _items = [];
    private IReadOnlyList<CategorySummary> _categories = [];
    private IReadOnlyList<SubcategorySummary> _subcategories = [];
    private bool _loadingFilterOptions;
    private bool _isDisposing;

    public EmployeeInventoryLookupForm(
        IEmployeeInventoryLookupService lookupService,
        IProductAssetService assets,
        ICategoryService? categoryService = null,
        ISubcategoryService? subcategoryService = null,
        bool loadDataOnShow = true)
    {
        _lookupService = lookupService;
        _assets = assets;
        _categoryService = categoryService;
        _subcategoryService = subcategoryService;
        Text = "Consulta de productos e inventario";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 700);
        MinimumSize = new Size(940, 580);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);
        BuildUi();
        _liveSearch = new LiveSearchController(_search, _clear, SearchAsync,
            ex => MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning),
            onClearAdditionalState: ResetCatalogFilters,
            hasAdditionalClearableState: HasNonDefaultCatalogFilters);
        if (loadDataOnShow)
        {
            Load += async (_, _) =>
            {
                await LoadFilterOptionsAsync();
                await _liveSearch.SearchNowAsync();
            };
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposing)
        {
            _isDisposing = true;
            _liveSearch?.Dispose();
            CancelPreview();
            ReplacePreview(null);
            _binding.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 8, 16, 8),
            RowCount = 2,
            ColumnCount = 1
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Consultar inventario",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "Consulta de solo lectura por producto, SKU, variante, categoría, subcategoría o código de barras.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor
        };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);

        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 10, 0, 6)
        };
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        _search.Name = "EmployeeInventorySearchTextBox";
        _search.PlaceholderText = "Buscar producto, SKU, variante, categoría, subcategoría o código de barras...";
        _search.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_search);
        _clear.Text = "×";
        _clear.AccessibleName = "Limpiar búsqueda y filtros";
        _clear.Dock = DockStyle.Fill;
        _clear.Margin = new Padding(6, 0, 0, 0);
        UITheme.StyleSecondaryButton(_clear);
        searchPanel.Controls.Add(_search, 0, 0);
        searchPanel.Controls.Add(_clear, 1, 0);

        var filters = new FlowLayoutPanel
        {
            Name = "EmployeeInventoryFilters",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 2, 0, 8),
            Margin = Padding.Empty
        };
        ConfigureChoiceFilter(_categoryFilter, [(0, "Todas las categorías")]);
        ConfigureChoiceFilter(_subcategoryFilter, [(0, "Todas las subcategorías")]);
        ConfigureChoiceFilter(_imageFilter, [(0, "Todas las imágenes"), (1, "Con imagen"), (2, "Sin imagen")]);
        ConfigureChoiceFilter(_locationFilter,
            [(0, "Todas las ubicaciones"), (1, "En exhibición"), (2, "En bodega"), (3, "Sin asignar")]);
        ConfigureChoiceFilter(_stockFilter,
            [(0, "Todas las existencias"), (1, "Con existencias"), (2, "Sin existencias"), (3, "Bajo mínimo")]);
        ConfigureChoiceFilter(_sortFilter,
            [(0, "Nombre A-Z"), (1, "Nombre Z-A"), (2, "Alta más reciente"),
                (3, "Alta más antigua"), (4, "Mayor existencia"), (5, "Menor existencia")]);
        _categoryFilter.SelectedValueChanged += CategoryFilter_Changed;
        foreach (var combo in new[] { _subcategoryFilter, _imageFilter, _locationFilter, _stockFilter, _sortFilter })
            combo.SelectedValueChanged += Filter_Changed;
        filters.Controls.Add(CreateFilterField("Categoría", _categoryFilter, 185));
        filters.Controls.Add(CreateFilterField("Subcategoría", _subcategoryFilter, 185));
        filters.Controls.Add(CreateFilterField("Imagen", _imageFilter, 165));
        filters.Controls.Add(CreateFilterField("Ubicación", _locationFilter, 180));
        filters.Controls.Add(CreateFilterField("Existencias", _stockFilter, 180));
        filters.Controls.Add(CreateFilterField("Orden", _sortFilter, 180));

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 82));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        ConfigureGrid();
        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12),
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(8, 0, 0, 0)
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.White;
        _previewCaption.Text = "Selecciona un SKU para ver su imagen.";
        _previewCaption.Dock = DockStyle.Fill;
        _previewCaption.TextAlign = ContentAlignment.MiddleCenter;
        _previewCaption.Font = UITheme.NormalBoldFont;
        _previewCaption.ForeColor = UITheme.MutedTextColor;
        previewPanel.Controls.Add(_preview, 0, 0);
        previewPanel.Controls.Add(_previewCaption, 0, 1);
        content.Controls.Add(_grid, 0, 0);
        content.Controls.Add(previewPanel, 1, 0);

        _status.Text = "Preparando catálogo...";
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = UITheme.NormalFont;
        _status.ForeColor = UITheme.MutedTextColor;

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(searchPanel, 0, 1);
        root.Controls.Add(filters, 0, 2);
        root.Controls.Add(content, 0, 3);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        AddColumn(nameof(LookupRow.Producto), "Producto", 220, 26);
        AddColumn(nameof(LookupRow.Sku), "SKU / variante", 185, 23);
        AddColumn(nameof(LookupRow.Categoria), "Categoría", 150, 18);
        AddColumn(nameof(LookupRow.Subcategoria), "Subcategoría", 170, 18);
        AddColumn(nameof(LookupRow.Exhibicion), "Exhibición", 100, 10);
        AddColumn(nameof(LookupRow.Bodega), "Bodega", 100, 10);
        AddColumn(nameof(LookupRow.Reservado), "Reservado", 100, 10);
        AddColumn(nameof(LookupRow.Disponible), "Disponible", 100, 10);
        AddColumn(nameof(LookupRow.PrecioVenta), "Precio de venta", 140, 12);
        _grid.DataSource = _binding;
        _grid.SelectionChanged += async (_, _) => await ShowSelectedPreviewAsync();
    }

    private void AddColumn(string property, string header, int minimumWidth, float weight)
    {
        var headerFont = _grid.ColumnHeadersDefaultCellStyle.Font ?? _grid.Font;
        var requiredHeaderWidth = TextRenderer.MeasureText(header, headerFont).Width + 24;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = weight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            Width = Math.Max(minimumWidth, requiredHeaderWidth)
        });
    }

    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (_isDisposing || IsDisposed || Disposing)
            return;

        _grid.Enabled = false;
        _status.Text = "Buscando...";
        try
        {
            var items = await _lookupService.SearchAsync(
                string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_isDisposing || IsDisposed || Disposing)
                return;

            _items = items;
            if (_categoryService is null)
                _categories = items
                    .Select(item => new CategorySummary(item.CategoriaId, item.CategoriaNombre, null, true))
                    .DistinctBy(item => item.CategoriaId)
                    .ToList();
            if (_subcategoryService is null)
                _subcategories = items.Where(item => item.SubcategoriaId.HasValue)
                    .Select(item => new SubcategorySummary(
                        item.SubcategoriaId!.Value,
                        item.CategoriaId,
                        item.SubcategoriaNombre ?? string.Empty,
                        null,
                        true))
                    .DistinctBy(item => item.SubcategoriaId)
                    .ToList();
            if (_categoryService is null || _subcategoryService is null)
                BindCategoryOptions();
            ApplyFilters();
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && !_isDisposing && !IsDisposed && !Disposing)
                _grid.Enabled = true;
        }
    }

    private async Task LoadFilterOptionsAsync()
    {
        try
        {
            if (_categoryService is not null)
                _categories = await _categoryService.GetAllAsync();
            if (_subcategoryService is not null)
                _subcategories = await _subcategoryService.GetAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Filtros de inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        BindCategoryOptions();
    }

    private void BindCategoryOptions()
    {
        var selectedCategory = (_categoryFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        _loadingFilterOptions = true;
        try
        {
            var options = new[] { new ChoiceOption(0, "Todas las categorías") }
                .Concat(_categories
                    .OrderBy(item => item.Nombre)
                    .Select(item => new ChoiceOption(item.CategoriaId, item.Nombre)))
                .ToList();
            _categoryFilter.DataSource = null;
            _categoryFilter.DisplayMember = nameof(ChoiceOption.Label);
            _categoryFilter.ValueMember = nameof(ChoiceOption.Value);
            _categoryFilter.DataSource = options;
            _categoryFilter.SelectedValue = options.Any(item => item.Value == selectedCategory)
                ? selectedCategory
                : 0;
        }
        finally
        {
            _loadingFilterOptions = false;
        }

        BindSubcategoryOptions();
    }

    private void BindSubcategoryOptions()
    {
        var selectedCategory = (_categoryFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        var selectedSubcategory = (_subcategoryFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        var options = new List<ChoiceOption>
        {
            new(0, "Todas las subcategorías"),
            new(-1, "Sin subcategoría")
        };
        options.AddRange(_subcategories
            .Where(item => selectedCategory == 0 || item.CategoriaId == selectedCategory)
            .OrderBy(item => item.Nombre)
            .Select(item => new ChoiceOption(item.SubcategoriaId, item.Nombre)));

        var wasLoadingFilterOptions = _loadingFilterOptions;
        _loadingFilterOptions = true;
        try
        {
            _subcategoryFilter.DataSource = null;
            _subcategoryFilter.DisplayMember = nameof(ChoiceOption.Label);
            _subcategoryFilter.ValueMember = nameof(ChoiceOption.Value);
            _subcategoryFilter.DataSource = options;
            _subcategoryFilter.SelectedValue = options.Any(item => item.Value == selectedSubcategory)
                ? selectedSubcategory
                : 0;
        }
        finally
        {
            _loadingFilterOptions = wasLoadingFilterOptions;
        }
    }

    private void CategoryFilter_Changed(object? sender, EventArgs e)
    {
        if (_loadingFilterOptions)
            return;
        BindSubcategoryOptions();
        ApplyFilters();
    }

    private void Filter_Changed(object? sender, EventArgs e)
    {
        if (!_loadingFilterOptions)
            ApplyFilters();
    }

    private static void ConfigureChoiceFilter(ComboBox combo, IEnumerable<(int Value, string Label)> choices)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.AccessibleName = "Filtro de inventario";
        UITheme.StyleInput(combo);
        combo.DisplayMember = nameof(ChoiceOption.Label);
        combo.ValueMember = nameof(ChoiceOption.Value);
        combo.DataSource = choices.Select(choice => new ChoiceOption(choice.Value, choice.Label)).ToList();
    }

    private static Control CreateFilterField(string label, Control input, int width)
    {
        var field = VisualLayout.CreateField(label, input);
        field.AutoSize = true;
        field.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        field.Dock = DockStyle.None;
        field.MinimumSize = new Size(width, 62);
        field.MaximumSize = new Size(width, 0);
        field.Width = width;
        return field;
    }

    internal ComboBox CategoryFilterForSmoke => _categoryFilter;
    internal ComboBox SubcategoryFilterForSmoke => _subcategoryFilter;
    internal ComboBox ImageFilterForSmoke => _imageFilter;
    internal ComboBox LocationFilterForSmoke => _locationFilter;
    internal ComboBox StockFilterForSmoke => _stockFilter;
    internal ComboBox SortFilterForSmoke => _sortFilter;
    internal TextBox SearchBoxForSmoke => _search;
    internal Button ClearSearchButtonForSmoke => _clear;
    internal bool ShouldShowClearButtonForSmoke => _liveSearch?.ShouldShowClearButton ?? false;
    internal void ClearSearchForSmoke() => _liveSearch?.Clear();
    internal void CancelPendingSearchForSmoke() => _liveSearch?.CancelPending();
    internal IReadOnlyList<string> VisibleSkuCodesForSmoke =>
        _binding.List.Cast<LookupRow>().Select(row => row.Sku).ToList();

    internal void BindItemsForSmoke(IReadOnlyList<EmployeeInventoryLookupItem> items)
    {
        _items = items;
        _categories = items
            .Select(item => new CategorySummary(item.CategoriaId, item.CategoriaNombre, null, true))
            .DistinctBy(item => item.CategoriaId)
            .ToList();
        _subcategories = items.Where(item => item.SubcategoriaId.HasValue)
            .Select(item => new SubcategorySummary(
                item.SubcategoriaId!.Value,
                item.CategoriaId,
                item.SubcategoriaNombre ?? string.Empty,
                null,
                true))
            .DistinctBy(item => item.SubcategoriaId)
            .ToList();
        BindCategoryOptions();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (_isDisposing || IsDisposed || Disposing)
            return;

        var categoryId = (_categoryFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        var subcategoryId = (_subcategoryFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        IEnumerable<EmployeeInventoryLookupItem> query = _items;
        if (categoryId > 0)
            query = query.Where(item => item.CategoriaId == categoryId);
        if (subcategoryId > 0)
            query = query.Where(item => item.SubcategoriaId == subcategoryId);
        else if (subcategoryId == -1)
            query = query.Where(item => item.SubcategoriaId is null);

        var image = (_imageFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        if (image == 1) query = query.Where(item => item.TieneImagen || item.ImagenOwnerKind.HasValue || !string.IsNullOrWhiteSpace(item.ImagenLocalPath));
        else if (image == 2) query = query.Where(item => !item.TieneImagen && !item.ImagenOwnerKind.HasValue && string.IsNullOrWhiteSpace(item.ImagenLocalPath));

        var location = (_locationFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        query = location switch
        {
            1 => query.Where(item => item.StockFisicoExhibicion > 0),
            2 => query.Where(item => item.StockFisicoBodega > 0),
            3 => query.Where(item => item.StockFisicoSinAsignar > 0),
            _ => query
        };

        var stock = (_stockFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        query = stock switch
        {
            1 => query.Where(item => item.StockFisicoTotal > 0),
            2 => query.Where(item => item.StockFisicoTotal <= 0),
            3 => query.Where(item => item.StockFisicoTotal <= item.StockMinimo),
            _ => query
        };

        var sort = (_sortFilter.SelectedItem as ChoiceOption)?.Value ?? 0;
        var filtered = sort switch
        {
            1 => query.OrderByDescending(item => item.ProductoNombre).ThenBy(item => item.CodigoSku),
            2 => query.OrderByDescending(item => item.FechaCreacion).ThenBy(item => item.ProductoNombre),
            3 => query.OrderBy(item => item.FechaCreacion).ThenBy(item => item.ProductoNombre),
            4 => query.OrderByDescending(item => item.StockFisicoTotal).ThenBy(item => item.ProductoNombre),
            5 => query.OrderBy(item => item.StockFisicoTotal).ThenBy(item => item.ProductoNombre),
            _ => query.OrderBy(item => item.ProductoNombre).ThenBy(item => item.CodigoSku)
        };
        var rows = filtered.Select(LookupRow.From).ToList();
        _binding.DataSource = rows;
        _status.Text = rows.Count == 0
            ? "No se encontraron coincidencias."
            : $"{rows.Count} SKU encontrado{(rows.Count == 1 ? string.Empty : "s")}. Solo lectura.";
        _liveSearch?.RefreshClearButton();
    }

    private bool HasNonDefaultCatalogFilters() =>
        SelectedValue(_categoryFilter) != 0 ||
        SelectedValue(_subcategoryFilter) != 0 ||
        SelectedValue(_imageFilter) != 0 ||
        SelectedValue(_locationFilter) != 0 ||
        SelectedValue(_stockFilter) != 0 ||
        SelectedValue(_sortFilter) != 0;

    private void ResetCatalogFilters()
    {
        var wasLoadingFilterOptions = _loadingFilterOptions;
        _loadingFilterOptions = true;
        try
        {
            _categoryFilter.SelectedValue = 0;
            BindSubcategoryOptions();
            _subcategoryFilter.SelectedValue = 0;
            _imageFilter.SelectedValue = 0;
            _locationFilter.SelectedValue = 0;
            _stockFilter.SelectedValue = 0;
            _sortFilter.SelectedValue = 0;
        }
        finally
        {
            _loadingFilterOptions = wasLoadingFilterOptions;
        }

        ApplyFilters();
    }

    private static int SelectedValue(ComboBox combo) =>
        (combo.SelectedItem as ChoiceOption)?.Value ?? 0;

    private async Task ShowSelectedPreviewAsync()
    {
        if (_isDisposing || IsDisposed || Disposing)
            return;

        CancelPreview();
        _previewCts = new CancellationTokenSource();
        var previewCts = _previewCts;
        var token = _previewCts.Token;
        var row = _grid.CurrentRow?.DataBoundItem as LookupRow;
        ReplacePreview(null);
        if (row is null)
        {
            _previewCaption.Text = "Selecciona un SKU para ver su imagen.";
            return;
        }

        _previewCaption.Text = $"{row.Producto}\n{row.Sku}";
        try
        {
            string? path = null;
            if (row.AssetOwner.HasValue && row.AssetId.HasValue)
                path = await _assets.GetThumbnailPathAsync(row.AssetOwner.Value, row.AssetId.Value, 420, token);
            path ??= row.LegacyImagePath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                token.ThrowIfCancellationRequested();
                using var image = Image.FromStream(stream);
                if (_isDisposing || IsDisposed || Disposing || !ReferenceEquals(_previewCts, previewCts))
                    return;
                ReplacePreview(new Bitmap(image));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            if (!_isDisposing && !IsDisposed && !Disposing)
                _previewCaption.Text += "\nImagen no disponible";
        }
    }

    private void CancelPreview()
    {
        var cts = _previewCts;
        _previewCts = null;
        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Another close/reselection path already disposed this source.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void ReplacePreview(Image? image)
    {
        if (IsDisposed || Disposing)
        {
            image?.Dispose();
            return;
        }

        var previous = _preview.Image;
        _preview.Image = image;
        previous?.Dispose();
    }

    private sealed record LookupRow(
        string Producto,
        string Sku,
        string Categoria,
        string Subcategoria,
        int Exhibicion,
        int Bodega,
        int Reservado,
        int Disponible,
        string PrecioVenta,
        ProductAssetOwnerKind? AssetOwner,
        int? AssetId,
        string? LegacyImagePath)
    {
        internal static LookupRow From(EmployeeInventoryLookupItem item) => new(
            $"{item.CodigoInterno} - {item.ProductoNombre}",
            string.IsNullOrWhiteSpace(item.VarianteNombre)
                ? item.CodigoSku
                : $"{item.CodigoSku} / {item.VarianteNombre}",
            string.IsNullOrWhiteSpace(item.CategoriaNombre)
                ? "No disponible"
                : item.CategoriaNombre,
            item.SubcategoriaNombre ?? "Sin subcategoría",
            item.StockFisicoExhibicion,
            item.StockFisicoBodega,
            item.StockReservadoTotal,
            item.StockDisponibleTotal,
            Money(item.PrecioVentaEfectivoCentavos),
            item.ImagenOwnerKind,
            item.ImagenAssetId,
            item.ImagenLocalPath);
    }

    private static string Money(long cents) => (cents / 100m).ToString("$#,##0.00");

    private sealed record ChoiceOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}
