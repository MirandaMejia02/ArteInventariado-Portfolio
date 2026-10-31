using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

internal sealed class ProductImageBatchAssignmentForm : Form
{
    private readonly IProductCatalogService _catalog;
    private readonly IProductAssetService _assets;
    private readonly List<ProductChoiceRow> _products;
    private readonly Dictionary<int, List<ProductImageTarget>> _skuTargetsByProduct = [];
    private readonly BindingSource _productSource = new();
    private readonly BindingSource _imageSource = new();
    private readonly DataGridView _productGrid = CreateGrid();
    private readonly DataGridView _imageGrid = CreateGrid();
    private readonly DataGridViewComboBoxColumn _targetColumn;
    private readonly PictureBox _currentPreview = new();
    private readonly PictureBox _newPreview = new();
    private readonly Label _currentPreviewCaption = new();
    private readonly Label _newPreviewCaption = new();
    private readonly Label _previewTargetCaption = new();
    private readonly Label _status = new();
    private readonly TextBox _productSearch = new();
    private readonly ComboBox _productImageFilter = new();
    private readonly ComboBox _productSelectionFilter = new();
    private readonly Button _btnSelectAll = new();
    private readonly Button _btnClearSelection = new();
    private readonly Button _btnChooseFolder = new();
    private readonly Button _btnLoadSkus = new();
    private readonly Button _btnApply = new();
    private readonly Button _btnClose = new();
    private readonly List<ProductImageTarget> _targets = [];
    private readonly Dictionary<int, IReadOnlyList<ProductAssetSummary>> _assetsByProduct = [];
    private IReadOnlyList<ProductChoiceRow> _visibleProducts = [];
    private int _successfulImports;
    private int _previewRequestVersion;
    private bool _suppressRowEvents;
    private bool _busy;

    public ProductImageBatchAssignmentForm(
        IReadOnlyList<ProductSummary> products,
        IProductCatalogService catalog,
        IProductAssetService assets)
    {
        _catalog = catalog;
        _assets = assets;
        _products = products
            .DistinctBy(product => product.ProductoId)
            .OrderBy(product => product.CodigoInterno, StringComparer.OrdinalIgnoreCase)
            .Select(product => new ProductChoiceRow(
                product.ProductoId,
                product.CodigoInterno,
                product.Nombre,
                HasProductImage(product),
                product.ImagenLocalPath,
                product.ManagedImageRelativePath))
            .ToList();

        _targetColumn = new DataGridViewComboBoxColumn
        {
            Name = "Target",
            HeaderText = "Producto o SKU destino",
            DataPropertyName = nameof(ImageBatchRow.TargetKey),
            DisplayMember = nameof(TargetChoice.Text),
            ValueMember = nameof(TargetChoice.Key),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 210,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };

        BuildUi();
        BindProducts();
        UpdateTargetChoices(autoMatch: false);
        UpdateStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposePreview(_currentPreview);
            DisposePreview(_newPreview);
        }

        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_successfulImports > 0 && DialogResult != DialogResult.OK)
        {
            DialogResult = DialogResult.OK;
        }

        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        Text = "Asignación de imágenes en lote";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(UITheme.SpaceMd),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(VisualLayout.CreatePageHeader(
            "Imágenes en lote",
            "Compara la actual con la nueva; la anterior se conserva salvo que marques eliminar."), 0, 0);

        var productActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        ConfigureButton(_btnSelectAll, "Seleccionar visibles", 200, SelectAllProducts);
        ConfigureButton(_btnClearSelection, "Quitar selección", 190, ClearProductSelection);
        ConfigureButton(_btnLoadSkus, "Cargar SKU", 135, async () => await LoadSkuTargetsAsync());
        productActions.Controls.AddRange([_btnSelectAll, _btnClearSelection, _btnLoadSkus]);
        root.Controls.Add(productActions, 0, 1);

        var productFilters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 2, 0)
        };
        productFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        productFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        productFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        productFilters.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        productFilters.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        productFilters.Controls.Add(CreateFilterLabel("Buscar"), 0, 0);
        _productSearch.Dock = DockStyle.Fill;
        _productSearch.Margin = new Padding(6, 2, 12, 3);
        _productSearch.PlaceholderText = "Nombre o código interno";
        _productSearch.TextChanged += (_, _) => ApplyProductFilters();
        productFilters.Controls.Add(_productSearch, 0, 1);
        productFilters.Controls.Add(CreateFilterLabel("Imagen del producto"), 1, 0);
        _productImageFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _productImageFilter.Dock = DockStyle.Fill;
        _productImageFilter.Margin = new Padding(6, 2, 4, 3);
        _productImageFilter.Items.AddRange(["Todas", "Con imagen", "Sin imagen"]);
        _productImageFilter.SelectedIndex = 0;
        _productImageFilter.SelectedIndexChanged += (_, _) => ApplyProductFilters();
        productFilters.Controls.Add(_productImageFilter, 1, 1);
        productFilters.Controls.Add(CreateFilterLabel("Ver selección"), 2, 0);
        _productSelectionFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _productSelectionFilter.Dock = DockStyle.Fill;
        _productSelectionFilter.Margin = new Padding(6, 2, 4, 3);
        _productSelectionFilter.Items.AddRange(["Todos", "Solo seleccionados"]);
        _productSelectionFilter.SelectedIndex = 0;
        _productSelectionFilter.SelectedIndexChanged += (_, _) => ApplyProductFilters();
        productFilters.Controls.Add(_productSelectionFilter, 2, 1);
        root.Controls.Add(productFilters, 0, 2);

        ConfigureProductsGrid();
        root.Controls.Add(_productGrid, 0, 3);

        var imageActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        ConfigureButton(_btnChooseFolder, "Elegir carpeta...", 190, async () => await ChooseFolderAsync(), primary: true);
        imageActions.Controls.Add(_btnChooseFolder);
        imageActions.Controls.Add(new Label
        {
            Text = "JPG/PNG de esta carpeta, sin subcarpetas. Código exacto o destino manual.",
            AutoSize = true,
            Padding = new Padding(8, 10, 0, 0),
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor
        });
        root.Controls.Add(imageActions, 0, 4);

        ConfigureImagesGrid();
        var imageReview = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        imageReview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        imageReview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        imageReview.Controls.Add(_imageGrid, 0, 0);

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UITheme.SpaceMd),
            BackColor = UITheme.SurfaceColor,
            MinimumSize = new Size(240, 0)
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        previewPanel.Controls.Add(new Label
        {
            Text = "Comparación de imágenes",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _previewTargetCaption.Dock = DockStyle.Fill;
        _previewTargetCaption.Font = UITheme.CaptionFont;
        _previewTargetCaption.ForeColor = UITheme.MutedTextColor;
        _previewTargetCaption.TextAlign = ContentAlignment.MiddleLeft;
        _previewTargetCaption.AutoEllipsis = true;
        previewPanel.Controls.Add(_previewTargetCaption, 0, 1);
        var comparison = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        comparison.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        comparison.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        comparison.Controls.Add(CreatePreviewTitle("Actual"), 0, 0);
        comparison.Controls.Add(CreatePreviewTitle("Nueva"), 1, 0);
        ConfigurePreview(_currentPreview, "Imagen actual");
        ConfigurePreview(_newPreview, "Imagen nueva");
        comparison.Controls.Add(_currentPreview, 0, 1);
        comparison.Controls.Add(_newPreview, 1, 1);
        previewPanel.Controls.Add(comparison, 0, 2);
        ConfigurePreviewCaption(_currentPreviewCaption);
        ConfigurePreviewCaption(_newPreviewCaption);
        var captions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        captions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        captions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        captions.Controls.Add(_currentPreviewCaption, 0, 0);
        captions.Controls.Add(_newPreviewCaption, 1, 0);
        previewPanel.Controls.Add(captions, 0, 3);
        imageReview.Controls.Add(previewPanel, 1, 0);
        root.Controls.Add(imageReview, 0, 5);

        _status.Dock = DockStyle.Fill;
        _status.Font = UITheme.CaptionFont;
        _status.ForeColor = UITheme.MutedTextColor;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_status, 0, 6);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _btnApply.Text = "Importar lote revisado";
        _btnApply.Size = new Size(235, 38);
        _btnApply.Click += async (_, _) => await ApplyAssignmentsAsync();
        UITheme.StylePrimaryButton(_btnApply);
        _btnClose.Text = "Cerrar";
        _btnClose.Size = new Size(110, 38);
        _btnClose.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(_btnClose);
        footer.Controls.AddRange([_btnApply, _btnClose]);
        root.Controls.Add(footer, 0, 7);

        Controls.Add(root);
    }

    private void ConfigureProductsGrid()
    {
        _productGrid.DataSource = _productSource;
        _productGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Selected",
            HeaderText = "Incluir",
            DataPropertyName = nameof(ProductChoiceRow.Selected),
            Width = 75,
            MinimumWidth = 75,
            ToolTipText = "Marca para incluir este producto; desmárcalo para quitar solo esta fila de la selección."
        });
        AddTextColumn(_productGrid, nameof(ProductChoiceRow.Code), "Código interno", 190);
        AddTextColumn(_productGrid, nameof(ProductChoiceRow.Name), "Producto", 420, fill: true);
        AddTextColumn(_productGrid, nameof(ProductChoiceRow.ImageStatus), "Imagen", 135);
        _productGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_productGrid.IsCurrentCellDirty)
            {
                _productGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _productGrid.CellValueChanged += (_, e) =>
        {
            if (!_suppressRowEvents && e.RowIndex >= 0 && e.ColumnIndex == 0)
            {
                var changedProduct = _productGrid.Rows[e.RowIndex].DataBoundItem as ProductChoiceRow;
                UpdateTargetChoices(autoMatch: true);
                if (_productSelectionFilter.SelectedIndex == 1 && changedProduct is { Selected: false })
                {
                    ApplyProductFilters();
                }
                else
                {
                    UpdateStatus();
                }
            }
        };
        _productGrid.DataError += (_, e) => e.ThrowException = false;
    }

    private void ConfigureImagesGrid()
    {
        _imageGrid.DataSource = _imageSource;
        AddTextColumn(_imageGrid, nameof(ImageBatchRow.FileName), "Archivo", 170);
        _imageGrid.Columns.Add(_targetColumn);
        AddTextColumn(_imageGrid, nameof(ImageBatchRow.ExistingImageText), "Imagen actual", 165);
        _imageGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Primary",
            HeaderText = "Hacer principal",
            DataPropertyName = nameof(ImageBatchRow.SetAsPrimary),
            Width = 105,
            MinimumWidth = 95,
            ToolTipText = "Marca la nueva imagen como principal. La anterior se conserva salvo que marques su eliminación."
        });
        _imageGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "DeletePreviousPrimary",
            HeaderText = "Eliminar anterior",
            DataPropertyName = nameof(ImageBatchRow.DeletePreviousPrimary),
            Width = 110,
            MinimumWidth = 100,
            ToolTipText = "Elimina solo la imagen principal administrada anterior, después de importar la nueva."
        });
        AddTextColumn(_imageGrid, nameof(ImageBatchRow.StatusText), "Estado", 145);
        _imageGrid.SelectionChanged += async (_, _) => await LoadSelectedPreviewAsync();
        _imageGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_imageGrid.IsCurrentCellDirty)
            {
                _imageGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _imageGrid.CellValueChanged += async (_, e) =>
        {
            if (_suppressRowEvents || e.RowIndex < 0 || e.ColumnIndex < 0 ||
                _imageGrid.Rows[e.RowIndex].DataBoundItem is not ImageBatchRow row)
            {
                return;
            }

            var columnName = _imageGrid.Columns[e.ColumnIndex].Name;
            if (columnName == "Target")
            {
                ResetRowImageDecision(row);
                row.ExistingImageText = "Revisando destino...";
                await LoadSelectedPreviewAsync();
            }
            else if (columnName == "DeletePreviousPrimary" && row.DeletePreviousPrimary)
            {
                row.SetAsPrimary = true;
                _imageGrid.Refresh();
            }
            else if (columnName == "Primary" && !row.SetAsPrimary)
            {
                row.DeletePreviousPrimary = false;
                _imageGrid.Refresh();
            }
            UpdateStatus();
        };
        _imageGrid.DataError += (_, e) => e.ThrowException = false;
        _imageGrid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewRow gridRow in _imageGrid.Rows)
            {
                if (gridRow.DataBoundItem is ImageBatchRow row)
                {
                    UpdateDeleteCellAvailability(row);
                }
            }
        };
        _imageGrid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _imageGrid.Rows[e.RowIndex].DataBoundItem is ImageBatchRow row &&
                (row.Imported || (_imageGrid.Columns[e.ColumnIndex].Name == "DeletePreviousPrimary" && !row.CanDeletePreviousPrimary)))
            {
                e.Cancel = true;
            }
        };
    }

    private void BindProducts()
    {
        ApplyProductFilters();
    }

    private void SelectAllProducts()
    {
        foreach (var product in _visibleProducts)
        {
            product.Selected = true;
        }
        _productGrid.Refresh();
        UpdateTargetChoices(autoMatch: true);
        UpdateStatus();
    }

    internal void SelectAllProductsForSmoke() => SelectAllProducts();
    internal void ClearProductSelectionForSmoke() => ClearProductSelection();

    internal TextBox SearchBoxForSmoke => _productSearch;
    internal ComboBox ImageFilterForSmoke => _productImageFilter;
    internal ComboBox SelectionFilterForSmoke => _productSelectionFilter;
    internal IReadOnlyList<string> VisibleProductCodesForSmoke => _visibleProducts.Select(product => product.Code).ToList();
    internal IReadOnlyList<string> SelectedProductCodesForSmoke => _products.Where(product => product.Selected)
        .Select(product => product.Code).ToList();

    internal void SelectProductForSmoke(int productId, bool selected)
    {
        var product = _products.Single(item => item.ProductId == productId);
        product.Selected = selected;
        _productGrid.Refresh();
        UpdateTargetChoices(autoMatch: true);
        if (_productSelectionFilter.SelectedIndex == 1)
        {
            ApplyProductFilters();
        }
        else
        {
            UpdateStatus();
        }
    }

    internal async Task SelectFirstImageRowForSmokeAsync()
    {
        if (_imageGrid.Rows.Count == 0) return;
        _imageGrid.CurrentCell = _imageGrid.Rows[0].Cells[0];
        await LoadSelectedPreviewAsync();
    }

    internal async Task ProcessImageRowForSmokeAsync(int rowIndex)
    {
        var rows = _imageSource.DataSource as List<ImageBatchRow> ?? [];
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        var row = rows[rowIndex];
        var target = _targets.FirstOrDefault(item => item.Key == row.TargetKey)
            ?? throw new InvalidOperationException("Smoke: el destino de imagen no existe.");
        await ImportOneAsync(row, target);
    }

    internal void SetImageDecisionForSmoke(int rowIndex, bool makePrimary, bool deletePrevious)
    {
        var rows = _imageSource.DataSource as List<ImageBatchRow> ?? [];
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        var row = rows[rowIndex];
        row.SetAsPrimary = makePrimary || deletePrevious;
        row.DeletePreviousPrimary = deletePrevious;
    }

    internal async Task ReviewImageRowForSmokeAsync(int rowIndex)
    {
        var rows = _imageSource.DataSource as List<ImageBatchRow> ?? [];
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        var row = rows[rowIndex];
        var target = _targets.FirstOrDefault(item => item.Key == row.TargetKey)
            ?? throw new InvalidOperationException("Smoke: el destino de imagen no existe.");
        ApplyImageState(row, await GetTargetImageStateAsync(target, forceRefresh: true));
    }

    internal string ImageStatusForSmoke(int rowIndex) =>
        (_imageSource.DataSource as List<ImageBatchRow> ?? []).ElementAtOrDefault(rowIndex)?.StatusText ?? string.Empty;

    internal int? PreviousPrimaryAssetIdForSmoke(int rowIndex) =>
        (_imageSource.DataSource as List<ImageBatchRow> ?? []).ElementAtOrDefault(rowIndex)?.PreviousPrimaryAssetId;

    internal bool HasBothPreviewImagesForSmoke =>
        _currentPreview.Image is not null && _newPreview.Image is not null;
    internal int ImageFileCountForSmoke =>
        (_imageSource.DataSource as List<ImageBatchRow> ?? []).Count;
    internal string PreviewDebugForSmoke =>
        $"current={_currentPreview.Image?.Size.ToString() ?? "vacía"} ({_currentPreviewCaption.Text}); " +
        $"new={_newPreview.Image?.Size.ToString() ?? "vacía"} ({_newPreviewCaption.Text}); " +
        $"target={_previewTargetCaption.Text}; rows={_imageGrid.Rows.Count}; files={(_imageSource.DataSource as List<ImageBatchRow>)?.Count ?? 0}; key={ImageTargetKeyForSmoke(0)}";

    internal string ImageTargetCellTextForSmoke(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _imageGrid.Rows.Count
            ? _imageGrid.Rows[rowIndex].Cells["Target"].FormattedValue?.ToString() ?? string.Empty
            : string.Empty;

    internal string ImageTargetKeyForSmoke(int rowIndex) =>
        (_imageSource.DataSource as List<ImageBatchRow> ?? []).ElementAtOrDefault(rowIndex)?.TargetKey ?? string.Empty;

    internal string ImageTargetDebugForSmoke(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _imageGrid.Rows.Count) return "sin fila";
        var cell = _imageGrid.Rows[rowIndex].Cells["Target"];
        var values = (_targetColumn.DataSource as IEnumerable<TargetChoice> ?? [])
            .Select(choice => $"{choice.Key}={choice.Text}");
        return $"Key='{ImageTargetKeyForSmoke(rowIndex)}'; value='{cell.Value}'; formatted='{cell.FormattedValue}'; member='{_targetColumn.ValueMember}/{_targetColumn.DisplayMember}'; choices=[{string.Join("; ", values)}]";
    }

    internal void LoadFilesForSmoke(IEnumerable<string> paths)
    {
        _imageSource.DataSource = paths
            .Select(path => new ImageBatchRow(path, Path.GetFileName(path)))
            .ToList();
        UpdateTargetChoices(autoMatch: true);
        UpdateStatus();
    }

    internal void AddSkuTargetForSmoke(int productId, int skuId, string code)
    {
        var product = _products.Single(item => item.ProductId == productId);
        _skuTargetsByProduct[productId] =
        [
            new ProductImageTarget(ProductAssetOwnerKind.Sku, skuId, productId, code, $"SKU {code} · {product.Name}")
        ];
        UpdateTargetChoices(autoMatch: true);
    }

    internal IReadOnlyList<(string FileName, string TargetKey)> FileTargetsForSmoke =>
        (_imageSource.DataSource as List<ImageBatchRow> ?? [])
            .Select(row => (row.FileName, row.TargetKey))
            .ToList();

    private void ClearProductSelection()
    {
        foreach (var product in _products)
        {
            product.Selected = false;
        }
        UpdateTargetChoices(autoMatch: false);
        if (_productSelectionFilter.SelectedIndex == 1)
        {
            _productSelectionFilter.SelectedIndex = 0;
        }
        else
        {
            _productGrid.Refresh();
            UpdateStatus();
        }
    }

    private void ApplyProductFilters()
    {
        var search = _productSearch.Text.Trim();
        var imageFilter = _productImageFilter.SelectedIndex;
        var selectionFilter = _productSelectionFilter.SelectedIndex;
        _visibleProducts = _products.Where(product =>
                (search.Length == 0 || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 product.Code.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (imageFilter == 0 || (imageFilter == 1 && product.HasImage) || (imageFilter == 2 && !product.HasImage)) &&
                (selectionFilter != 1 || product.Selected))
            .ToList();
        _productSource.DataSource = _visibleProducts;
        UpdateStatus();
    }

    private async Task LoadSkuTargetsAsync()
    {
        var selected = SelectedProducts();
        if (selected.Count == 0)
        {
            MessageBox.Show("Selecciona primero los productos cuyos SKU quieres incluir.", "Códigos SKU",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        try
        {
            using var gate = new SemaphoreSlim(4, 4);
            var details = await Task.WhenAll(selected.Select(async product =>
            {
                await gate.WaitAsync();
                try
                {
                    return await _catalog.GetDetailAsync(product.ProductId);
                }
                finally
                {
                    gate.Release();
                }
            }));

            foreach (var detail in details.Where(detail => detail is not null))
            {
                _skuTargetsByProduct[detail!.Product.ProductoId] = detail.Skus
                    .Where(sku => sku.Activo)
                    .Select(sku => new ProductImageTarget(
                        ProductAssetOwnerKind.Sku,
                        sku.ProductoSkuId,
                        detail.Product.ProductoId,
                        sku.CodigoSku,
                        $"SKU {sku.CodigoSku} · {detail.Product.Nombre}"))
                    .ToList();
            }

            UpdateTargetChoices(autoMatch: true);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudieron cargar los SKU: {ex.Message}", "Códigos SKU",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ChooseFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecciona la carpeta que contiene las imágenes JPG o PNG de estos productos.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(dialog.SelectedPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedImageExtension)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Select(path => new ImageBatchRow(path, Path.GetFileName(path)))
                .ToList();
            if (files.Count == 0)
            {
                MessageBox.Show("La carpeta no contiene imágenes JPG o PNG.", "Imágenes en lote",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _imageSource.DataSource = files;
            UpdateTargetChoices(autoMatch: true);
            UpdateStatus();
            await Task.Yield();
            await LoadSelectedPreviewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo leer la carpeta: {ex.Message}", "Imágenes en lote",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateTargetChoices(bool autoMatch)
    {
        var selected = SelectedProducts();
        _targets.Clear();
        _targets.AddRange(selected.Select(product => new ProductImageTarget(
            ProductAssetOwnerKind.Product,
            product.ProductId,
            product.ProductId,
            product.Code,
            $"Producto {product.Code} · {product.Name}")));
        _targets.AddRange(selected
            .SelectMany(product => _skuTargetsByProduct.GetValueOrDefault(product.ProductId) ?? [])
            .DistinctBy(target => target.Key));

        var choices = new List<TargetChoice> { new(string.Empty, "— Sin asignar —") };
        choices.AddRange(_targets.Select(target => new TargetChoice(target.Key, target.Text)));
        _targetColumn.DataSource = null;
        _targetColumn.DataSource = choices;
        _targetColumn.ValueMember = nameof(TargetChoice.Key);
        _targetColumn.DisplayMember = nameof(TargetChoice.Text);

        var rows = _imageSource.DataSource as List<ImageBatchRow>;
        if (rows is null)
        {
            return;
        }

        var validKeys = _targets.Select(target => target.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!validKeys.Contains(row.TargetKey))
            {
                row.TargetKey = string.Empty;
                ResetRowImageDecision(row);
            }
        }

        if (autoMatch)
        {
            AutoMatchRows(rows);
        }

        _imageGrid.Refresh();
        _ = LoadSelectedPreviewAsync();
    }

    private void AutoMatchRows(IReadOnlyList<ImageBatchRow> rows)
    {
        var codeMatches = _targets
            .GroupBy(target => target.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var filesByBaseName = rows
            .GroupBy(row => Path.GetFileNameWithoutExtension(row.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Where(row => !row.Imported && string.IsNullOrWhiteSpace(row.TargetKey)))
        {
            var baseName = Path.GetFileNameWithoutExtension(row.FileName);
            if (filesByBaseName.GetValueOrDefault(baseName)?.Count != 1 ||
                codeMatches.GetValueOrDefault(baseName)?.Count != 1)
            {
                continue;
            }

            row.TargetKey = codeMatches[baseName][0].Key;
            ResetRowImageDecision(row);
        }
    }

    private async Task LoadSelectedPreviewAsync()
    {
        var requestVersion = ++_previewRequestVersion;
        DisposePreview(_currentPreview);
        DisposePreview(_newPreview);
        if (_imageGrid.CurrentRow?.DataBoundItem is not ImageBatchRow row)
        {
            _previewTargetCaption.Text = "Selecciona una imagen para revisar.";
            _currentPreviewCaption.Text = "Imagen actual";
            _newPreviewCaption.Text = "Nueva imagen";
            return;
        }

        LoadNewImagePreview(row);
        var target = _targets.FirstOrDefault(item => item.Key == row.TargetKey);
        if (target is null)
        {
            _previewTargetCaption.Text = "Asigna un destino para comparar la imagen actual.";
            _currentPreviewCaption.Text = "Sin destino seleccionado";
            return;
        }

        _previewTargetCaption.Text = target.Text;
        _currentPreviewCaption.Text = "Consultando imagen actual...";
        try
        {
            var state = await GetTargetImageStateAsync(target);
            if (requestVersion != _previewRequestVersion)
            {
                return;
            }

            ApplyImageState(row, state);
            _imageGrid.Refresh();
            UpdateStatus();
            if (state.AssetToShow is not null)
            {
                var thumbnailPath = await _assets.GetThumbnailPathAsync(
                    state.AssetToShow.OwnerKind, state.AssetToShow.AssetId, 800);
                if (requestVersion != _previewRequestVersion) return;
                if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    _currentPreview.Image = LoadPreviewBitmap(thumbnailPath);
                }
            }
            else if (!string.IsNullOrWhiteSpace(state.LocalPath) && File.Exists(state.LocalPath))
            {
                _currentPreview.Image = LoadPreviewBitmap(state.LocalPath);
            }

            _currentPreviewCaption.Text = state.PreviewCaption;
        }
        catch (Exception ex)
        {
            row.ExistingImageState = null;
            row.CanDeletePreviousPrimary = false;
            row.DeletePreviousPrimary = false;
            row.PreviousPrimaryAssetId = null;
            row.ExistingImageText = "No se pudo revisar la actual";
            _currentPreviewCaption.Text = "No se pudo revisar la imagen actual.";
            _previewTargetCaption.Text = $"{target.Text} · Importar como principal quedará bloqueado hasta revisar.";
            UpdateDeleteCellAvailability(row);
            _imageGrid.Refresh();
            UpdateStatus();
            if (!string.IsNullOrWhiteSpace(ex.Message))
            {
                _status.Text = "No se pudo leer la imagen actual del destino. Revisa conexión y vuelve a intentarlo.";
            }
        }

        if (_newPreview.Image is null && string.IsNullOrWhiteSpace(_newPreviewCaption.Text))
        {
            _newPreviewCaption.Text = row.FileName;
        }
    }

    private void LoadNewImagePreview(ImageBatchRow row)
    {
        _newPreviewCaption.Text = row.FileName;
        try
        {
            var file = new FileInfo(row.Path);
            if (!file.Exists || file.Length is <= 0 or > 12L * 1024 * 1024)
            {
                _newPreviewCaption.Text = $"{row.FileName}\nTamaño no válido (máximo 12 MB).";
                return;
            }

            var preview = LoadPreviewBitmap(row.Path);
            _newPreview.Image = preview;
            _newPreviewCaption.Text = $"{row.FileName}\n{preview.Width} × {preview.Height} px · {file.Length / 1024d:0.#} KB";
        }
        catch
        {
            _newPreviewCaption.Text = $"{row.FileName}\nNo se pudo previsualizar; se validará al importar.";
        }
    }

    private async Task ApplyAssignmentsAsync()
    {
        if (_busy)
        {
            return;
        }

        _imageGrid.EndEdit();
        var rows = _imageSource.DataSource as List<ImageBatchRow> ?? [];
        var pending = rows.Where(row => !row.Imported).ToList();
        if (pending.Count == 0)
        {
            MessageBox.Show("No hay imágenes pendientes.", "Imágenes en lote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var unassigned = pending.Where(row => string.IsNullOrWhiteSpace(row.TargetKey)).ToList();
        if (unassigned.Count > 0)
        {
            MessageBox.Show($"Asigna un producto o SKU a cada archivo antes de importar. Faltan {unassigned.Count}.",
                "Revisión requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var duplicateTargets = pending
            .GroupBy(row => row.TargetKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => _targets.FirstOrDefault(target => target.Key == group.Key)?.Text ?? group.Key)
            .ToList();
        if (duplicateTargets.Count > 0)
        {
            MessageBox.Show(
                "Este lote permite una imagen por producto o SKU. Hay destinos repetidos:\n\n" +
                string.Join("\n", duplicateTargets),
                "Revisa los destinos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var invalidDeletes = pending.Where(row => row.DeletePreviousPrimary &&
            (!row.SetAsPrimary || !row.CanDeletePreviousPrimary || row.PreviousPrimaryAssetId is null)).ToList();
        if (invalidDeletes.Count > 0)
        {
            MessageBox.Show(
                "Solo puedes eliminar una imagen anterior administrada cuando existe una imagen principal y la nueva está marcada como principal. Revisa las filas señaladas.",
                "Revisión requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (pending.Any(row => row.ExistingImageState is null))
        {
            SetBusy(true);
            try
            {
                await RefreshTargetStatesForReviewAsync(pending);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"No se pudo comprobar el estado actual de las imágenes. No se importó nada.\n\n{ShortError(ex.Message)}",
                    "No se pudo revisar el lote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            await LoadSelectedPreviewAsync();
            MessageBox.Show(
                "Ya cargué el estado actual de todos los destinos. Revisa la columna «Imagen actual» y, al seleccionar cada fila, compara la imagen actual con la nueva. Luego decide si conservar o eliminar la principal anterior y vuelve a pulsar Importar.",
                "Revisión de imágenes", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        try
        {
            await RefreshTargetStatesForReviewAsync(pending);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo comprobar el estado actual de las imágenes. No se importó nada.\n\n{ShortError(ex.Message)}",
                "No se pudo revisar el lote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        var staleDecisions = pending.Where(row => row.ReviewedPrimaryAssetIdBeforeRefresh != row.PreviousPrimaryAssetId &&
            (row.SetAsPrimary || row.DeletePreviousPrimary)).ToList();
        if (staleDecisions.Count > 0)
        {
            foreach (var row in staleDecisions)
            {
                row.DeletePreviousPrimary = false;
                row.SetAsPrimary = false;
                row.StatusText = "La imagen actual cambió; revisa otra vez";
            }
            _imageGrid.Refresh();
            UpdateStatus();
            var staleRowIndex = _imageGrid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(gridRow => ReferenceEquals(gridRow.DataBoundItem, staleDecisions[0]))?.Index ?? -1;
            if (staleRowIndex >= 0)
            {
                _imageGrid.CurrentCell = _imageGrid.Rows[staleRowIndex].Cells[0];
                await LoadSelectedPreviewAsync();
            }
            MessageBox.Show(
                "La imagen principal de uno o más destinos cambió desde que abriste la comparación. Se actualizó la revisión y no se importó nada. Confirma de nuevo los destinos y las acciones.",
                "Revisión actualizada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var deleteRows = pending.Where(row => row.DeletePreviousPrimary).ToList();
        var changePrimaryCount = pending.Count(row => row.SetAsPrimary ||
            row.ExistingImageState?.TargetOwnedAssets.Count == 0);
        var reviewMessage = $"Se importarán {pending.Count} imagen(es).\n" +
                            $"Nuevas imágenes principales: {changePrimaryCount}.\n" +
                            $"Destinos con imagen actual: {pending.Count(row => row.ExistingImageState?.HasAnyVisibleImage == true)}.\n" +
                            $"Imágenes principales anteriores que se eliminarán después de importar: {deleteRows.Count}.\n\n" +
                            "Revisa en la tabla el archivo, el destino, la imagen actual y las casillas de cada fila. ¿Continuar?";
        if (deleteRows.Count > 0)
        {
            reviewMessage += "\n\nSe eliminarán únicamente las imágenes principales anteriores de estas filas:\n" +
                             string.Join("\n", deleteRows.Select(row =>
                             {
                                 var target = _targets.First(item => item.Key == row.TargetKey);
                                 var oldName = row.ExistingImageState?.PreviousPrimaryAsset?.OriginalFileName ?? "imagen actual";
                                 return $"• {target.Text}: {oldName} → {row.FileName}";
                             }));
        }

        if (MessageBox.Show(reviewMessage, "Revisión final del lote",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        try
        {
            foreach (var row in pending)
            {
                var target = _targets.FirstOrDefault(item => item.Key == row.TargetKey);
                if (target is null)
                {
                    row.StatusText = "Destino no disponible";
                    continue;
                }

                await ImportOneAsync(row, target);
                _imageGrid.Refresh();
                UpdateStatus();
                if (ReferenceEquals(_imageGrid.CurrentRow?.DataBoundItem, row))
                {
                    await LoadSelectedPreviewAsync();
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        var failed = rows.Count(row => !row.Imported);
        if (failed == 0)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        var firstError = rows.FirstOrDefault(row => !row.Imported)?.StatusText ?? "Revisa los destinos pendientes.";
        MessageBox.Show(
            $"Se importaron {_successfulImports} imagen(es); {failed} quedó(aron) pendiente(s). Las importadas están marcadas y no se repetirán si vuelves a intentar.\n\n{firstError}",
            "Resultado de importación",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        if (_successfulImports > 0)
        {
            DialogResult = DialogResult.OK;
        }
    }

    private async Task RefreshTargetStatesForReviewAsync(IReadOnlyList<ImageBatchRow> rows)
    {
        var previouslyReviewed = rows.ToDictionary(
            row => row,
            row => row.ExistingImageState is null ? (int?)null : row.PreviousPrimaryAssetId);
        var productIds = rows
            .Select(row => _targets.First(target => target.Key == row.TargetKey).ProductId)
            .Distinct()
            .ToList();
        using var gate = new SemaphoreSlim(4, 4);
        var refreshedAssets = await Task.WhenAll(productIds.Select(async productId =>
        {
            await gate.WaitAsync();
            try
            {
                return (ProductId: productId, Assets: await _assets.GetByProductAsync(productId));
            }
            finally
            {
                gate.Release();
            }
        }));
        foreach (var (productId, assets) in refreshedAssets)
        {
            _assetsByProduct[productId] = assets;
        }

        foreach (var row in rows)
        {
            var target = _targets.First(item => item.Key == row.TargetKey);
            row.ReviewedPrimaryAssetIdBeforeRefresh = previouslyReviewed[row];
            ApplyImageState(row, CreateTargetImageState(target, _assetsByProduct[target.ProductId]));
        }
        _imageGrid.Refresh();
        UpdateStatus();
    }

    private async Task ImportOneAsync(ImageBatchRow row, ProductImageTarget target)
    {
        var oldPrimaryId = row.DeletePreviousPrimary ? row.PreviousPrimaryAssetId : null;
        if (oldPrimaryId.HasValue)
        {
            try
            {
                var currentState = await GetTargetImageStateAsync(target, forceRefresh: true);
                if (currentState.PreviousPrimaryAsset?.AssetId != oldPrimaryId.Value)
                {
                    ApplyImageState(row, currentState);
                    row.SetAsPrimary = false;
                    row.DeletePreviousPrimary = false;
                    row.StatusText = "No importada; cambió la imagen actual. Revisa de nuevo.";
                    return;
                }
            }
            catch (Exception ex)
            {
                row.StatusText = "No importada; no se pudo revalidar la imagen actual.";
                _status.Text = ShortError(ex.Message);
                return;
            }
        }

        ProductAssetSummary imported;
        try
        {
            imported = await _assets.ImportAsync(new ImportProductAssetRequest(
                target.ProductId,
                row.Path,
                target.OwnerKind == ProductAssetOwnerKind.Sku ? target.OwnerId : null,
                row.SetAsPrimary));
            row.Imported = true;
            _successfulImports++;
            _assetsByProduct.Remove(target.ProductId);
        }
        catch (Exception ex)
        {
            row.StatusText = "Error: " + ShortError(ex.Message);
            return;
        }

        if (!oldPrimaryId.HasValue)
        {
            row.StatusText = imported.IsPrimary ? "Importada como principal" : "Importada; anterior conservada";
            return;
        }

        try
        {
            var afterImport = await _assets.GetByProductAsync(target.ProductId);
            _assetsByProduct[target.ProductId] = afterImport;
            var previous = afterImport.FirstOrDefault(asset =>
                asset.OwnerKind == target.OwnerKind && asset.AssetId == oldPrimaryId.Value);
            var replacement = afterImport.FirstOrDefault(asset => asset.AssetId == imported.AssetId);
            if (previous is null)
            {
                row.StatusText = "Importada; la anterior ya no existía";
                return;
            }

            if (replacement?.IsPrimary != true || previous.IsPrimary)
            {
                row.StatusText = "Importada; no se eliminó la anterior porque cambió el estado";
                return;
            }

            await _assets.DeleteAsync(target.OwnerKind, oldPrimaryId.Value);
            _assetsByProduct.Remove(target.ProductId);
            row.StatusText = "Importada; anterior eliminada";
        }
        catch (Exception ex)
        {
            row.StatusText = "Importada; se conservó la anterior: " + ShortError(ex.Message);
        }
    }

    private async Task<TargetImageState> GetTargetImageStateAsync(
        ProductImageTarget target,
        bool forceRefresh = false)
    {
        IReadOnlyList<ProductAssetSummary> assets;
        if (forceRefresh || !_assetsByProduct.TryGetValue(target.ProductId, out assets!))
        {
            assets = await _assets.GetByProductAsync(target.ProductId);
            _assetsByProduct[target.ProductId] = assets;
        }

        return CreateTargetImageState(target, assets);
    }

    private TargetImageState CreateTargetImageState(
        ProductImageTarget target,
        IReadOnlyList<ProductAssetSummary> assets)
    {
        var targetAssets = assets
            .Where(asset => asset.OwnerKind == target.OwnerKind && asset.OwnerId == target.OwnerId)
            .OrderByDescending(asset => asset.IsPrimary)
            .ThenBy(asset => asset.Order)
            .ToList();
        var targetPrimary = targetAssets.FirstOrDefault(asset => asset.IsPrimary);
        var displayAsset = targetPrimary ?? targetAssets.FirstOrDefault();
        if (displayAsset is not null)
        {
            var summary = targetPrimary is not null
                ? $"Principal: {displayAsset.OriginalFileName}"
                : $"Imagen guardada: {displayAsset.OriginalFileName}";
            var caption = targetPrimary is not null
                ? $"Principal actual\n{displayAsset.OriginalFileName}"
                : $"Imagen existente (sin principal)\n{displayAsset.OriginalFileName}";
            return new TargetImageState(
                targetAssets,
                displayAsset,
                targetPrimary,
                null,
                summary,
                caption,
                HasAnyVisibleImage: true);
        }

        if (target.OwnerKind == ProductAssetOwnerKind.Product)
        {
            var product = _products.First(item => item.ProductId == target.ProductId);
            var effectiveManagedPath = NormalizeRelativePath(product.ManagedImageRelativePath);
            var fallbackAsset = assets.FirstOrDefault(asset =>
                string.Equals(NormalizeRelativePath(asset.RelativePath), effectiveManagedPath, StringComparison.OrdinalIgnoreCase));
            if (fallbackAsset is not null)
            {
                return new TargetImageState(
                    targetAssets,
                    fallbackAsset,
                    null,
                    null,
                    $"Visible desde SKU: {fallbackAsset.OriginalFileName}",
                    $"Imagen visible de un SKU\n{fallbackAsset.OriginalFileName}\nNo se elimina desde el destino Producto.",
                    HasAnyVisibleImage: true);
            }

            if (!string.IsNullOrWhiteSpace(product.LegacyImagePath))
            {
                var exists = File.Exists(product.LegacyImagePath);
                return new TargetImageState(
                    targetAssets,
                    null,
                    null,
                    exists ? product.LegacyImagePath : null,
                    exists ? "Imagen anterior local" : "Imagen anterior no disponible",
                    exists
                        ? "Imagen anterior local\nSe conservará; no se elimina desde el lote."
                        : "La ruta de la imagen anterior ya no está disponible.",
                    HasAnyVisibleImage: true);
            }

            if (product.HasImage)
            {
                return new TargetImageState(
                    targetAssets,
                    null,
                    null,
                    null,
                    "Imagen registrada; vista previa no disponible",
                    "El catálogo indica que hay imagen, pero no se pudo resolver su archivo.",
                    HasAnyVisibleImage: true);
            }
        }

        return new TargetImageState(
            targetAssets,
            null,
            null,
            null,
            "Sin imagen actual",
            target.OwnerKind == ProductAssetOwnerKind.Sku
                ? "Este SKU aún no tiene imagen."
                : "Este producto aún no tiene imagen propia.",
            HasAnyVisibleImage: false);
    }

    private void ApplyImageState(ImageBatchRow row, TargetImageState state)
    {
        _suppressRowEvents = true;
        try
        {
            row.ExistingImageState = state;
            row.ExistingImageText = state.Summary;
            row.PreviousPrimaryAssetId = state.PreviousPrimaryAsset?.AssetId;
            row.CanDeletePreviousPrimary = state.PreviousPrimaryAsset is not null;
            if (!row.CanDeletePreviousPrimary)
            {
                row.DeletePreviousPrimary = false;
            }
            UpdateDeleteCellAvailability(row);
        }
        finally
        {
            _suppressRowEvents = false;
        }
    }

    private void UpdateDeleteCellAvailability(ImageBatchRow row)
    {
        foreach (DataGridViewRow gridRow in _imageGrid.Rows)
        {
            if (!ReferenceEquals(gridRow.DataBoundItem, row)) continue;
            var cell = gridRow.Cells["DeletePreviousPrimary"];
            cell.ReadOnly = row.Imported || !row.CanDeletePreviousPrimary;
            cell.Style.ForeColor = cell.ReadOnly ? UITheme.MutedTextColor : UITheme.TextDark;
            break;
        }
    }

    private void ResetRowImageDecision(ImageBatchRow row)
    {
        row.SetAsPrimary = false;
        row.DeletePreviousPrimary = false;
        row.PreviousPrimaryAssetId = null;
        row.CanDeletePreviousPrimary = false;
        row.ExistingImageState = null;
        row.ExistingImageText = string.IsNullOrWhiteSpace(row.TargetKey) ? "Sin destino" : "Pendiente de revisión";
        UpdateDeleteCellAvailability(row);
    }

    private static string NormalizeRelativePath(string? path) =>
        (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static bool HasProductImage(ProductSummary product)
    {
        return product.TieneImagen ||
               !string.IsNullOrWhiteSpace(product.ImagenLocalPath) ||
               !string.IsNullOrWhiteSpace(product.ManagedImageRelativePath);
    }

    private IReadOnlyList<ProductChoiceRow> SelectedProducts() =>
        _products.Where(product => product.Selected).ToList();

    private void UpdateStatus()
    {
        var selectedCount = _products.Count(product => product.Selected);
        var rows = _imageSource.DataSource as List<ImageBatchRow> ?? [];
        var mappedCount = rows.Count(row => !row.Imported && !string.IsNullOrWhiteSpace(row.TargetKey));
        var hasExisting = rows.Count(row => !row.Imported && row.ExistingImageState?.HasAnyVisibleImage == true);
        var deleting = rows.Count(row => !row.Imported && row.DeletePreviousPrimary);
        _status.Text = $"Seleccionados: {selectedCount} · Resultados: {_visibleProducts.Count}/{_products.Count} · Archivos: {rows.Count} · Asignados: {mappedCount} · Con imagen actual: {hasExisting} · Eliminar anterior: {deleting} · Importadas: {_successfulImports}.";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _btnSelectAll.Enabled = !busy;
        _btnClearSelection.Enabled = !busy;
        _btnChooseFolder.Enabled = !busy;
        _btnLoadSkus.Enabled = !busy;
        _btnApply.Enabled = !busy;
        _btnClose.Enabled = !busy;
        _productSearch.Enabled = !busy;
        _productImageFilter.Enabled = !busy;
        _productSelectionFilter.Enabled = !busy;
        _productGrid.Enabled = !busy;
        _imageGrid.Enabled = !busy;
    }

    private static string ShortError(string message)
    {
        var line = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "No se pudo importar.";
        return line.Length <= 80 ? line : line[..77] + "...";
    }

    private static bool IsSupportedImageExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = false,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeight = 42,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        };
        UITheme.StyleDataGridView(grid);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        return grid;
    }

    private static void AddTextColumn(DataGridView grid, string property, string title, int width, bool fill = false)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = title,
            Width = width,
            MinimumWidth = Math.Min(width, 90),
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            ReadOnly = true
        });
    }

    private static void ConfigureButton(Button button, string text, int width, Action click, bool primary = false)
    {
        button.Text = text;
        button.Size = new Size(width, 34);
        button.Click += (_, _) => click();
        if (primary) UITheme.StylePrimaryButton(button);
        else UITheme.StyleSecondaryButton(button);
    }

    private static Label CreateFilterLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Font = UITheme.CaptionFont,
        ForeColor = UITheme.TextDark,
        Margin = new Padding(4, 4, 2, 4)
    };

    private static Label CreatePreviewTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = UITheme.CaptionFont,
        ForeColor = UITheme.TextDark,
        TextAlign = ContentAlignment.MiddleCenter
    };

    private static void ConfigurePreview(PictureBox picture, string placeholder)
    {
        picture.Dock = DockStyle.Fill;
        picture.Margin = new Padding(3);
        picture.BorderStyle = BorderStyle.FixedSingle;
        picture.BackColor = UITheme.BackgroundColor;
        picture.SizeMode = PictureBoxSizeMode.Zoom;
        picture.AccessibleName = placeholder;
        picture.Paint += DrawImagePlaceholder;
    }

    private static void ConfigurePreviewCaption(Label caption)
    {
        caption.Dock = DockStyle.Fill;
        caption.Font = UITheme.CaptionFont;
        caption.ForeColor = UITheme.MutedTextColor;
        caption.TextAlign = ContentAlignment.MiddleCenter;
        caption.AutoEllipsis = true;
    }

    private static void DrawImagePlaceholder(object? sender, PaintEventArgs e)
    {
        if (sender is not PictureBox picture || picture.Image is not null)
        {
            return;
        }

        TextRenderer.DrawText(e.Graphics, picture.AccessibleName ?? "Vista previa", UITheme.NormalFont, picture.ClientRectangle,
            UITheme.MutedTextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static Bitmap LoadPreviewBitmap(string path)
    {
        using var source = Image.FromFile(path);
        if ((long)source.Width * source.Height > 100_000_000)
        {
            throw new InvalidDataException("La imagen supera el tamaño seguro para vista previa.");
        }

        return CreatePreviewBitmap(source, 800);
    }

    private static void DisposePreview(PictureBox picture)
    {
        var image = picture.Image;
        picture.Image = null;
        image?.Dispose();
        picture.Invalidate();
    }

    private static Bitmap CreatePreviewBitmap(Image source, int maximumPixels)
    {
        var scale = Math.Min(1d, maximumPixels / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return bitmap;
    }

    private sealed class ProductChoiceRow(
        int productId,
        string code,
        string name,
        bool hasImage,
        string? legacyImagePath,
        string? managedImageRelativePath)
    {
        public bool Selected { get; set; }
        public int ProductId { get; } = productId;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public bool HasImage { get; } = hasImage;
        public string? LegacyImagePath { get; } = legacyImagePath;
        public string? ManagedImageRelativePath { get; } = managedImageRelativePath;
        public string ImageStatus => HasImage ? "Con imagen" : "Sin imagen";
    }

    private sealed class ImageBatchRow(string path, string fileName)
    {
        public string Path { get; } = path;
        public string FileName { get; } = fileName;
        public string TargetKey { get; set; } = string.Empty;
        public bool SetAsPrimary { get; set; }
        public bool DeletePreviousPrimary { get; set; }
        public bool CanDeletePreviousPrimary { get; set; }
        public int? PreviousPrimaryAssetId { get; set; }
        public int? ReviewedPrimaryAssetIdBeforeRefresh { get; set; }
        public string ExistingImageText { get; set; } = "Pendiente de revisión";
        public TargetImageState? ExistingImageState { get; set; }
        public string StatusText { get; set; } = "Pendiente";
        public bool Imported { get; set; }
    }

    private sealed record TargetImageState(
        IReadOnlyList<ProductAssetSummary> TargetOwnedAssets,
        ProductAssetSummary? AssetToShow,
        ProductAssetSummary? PreviousPrimaryAsset,
        string? LocalPath,
        string Summary,
        string PreviewCaption,
        bool HasAnyVisibleImage);

    private sealed record ProductImageTarget(
        ProductAssetOwnerKind OwnerKind,
        int OwnerId,
        int ProductId,
        string Code,
        string Text)
    {
        public string Key => $"{(OwnerKind == ProductAssetOwnerKind.Product ? "P" : "S")}:{OwnerId}";
    }

    public sealed record TargetChoice(string Key, string Text)
    {
        public override string ToString() => Text;
    }
}
