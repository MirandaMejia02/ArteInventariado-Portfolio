using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class ProductEditForm : Form
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly ISubcategoryService? _subcategoryService;
    private readonly IProductAssetService? _assetService;
    private readonly ProductSummary? _product;
    private readonly bool _canEditPurchaseCost;
    private readonly bool _canManageImages;

    private readonly TextBox _txtCodigo = new();
    private readonly TextBox _txtNombre = new();
    private readonly TextBox _txtDescripcion = new();
    private readonly ComboBox _cmbCategoria = new();
    private readonly ComboBox _cmbSubcategoria = new();
    private readonly ComboBox _cmbModo = new();
    private readonly ComboBox _cmbUbicacionInicial = new();
    private readonly Label _lblUbicacionInicial = new();
    private readonly NumericUpDown _numPrecioCompra = new();
    private readonly NumericUpDown _numPrecioVenta = new();
    private readonly NumericUpDown _numStockActual = new();
    private readonly NumericUpDown _numStockMinimo = new();
    private readonly Button _btnGuardar = new();
    private readonly Button _btnCancelar = new();
    private readonly Button _btnChooseImage = new();
    private readonly Button _btnClearImage = new();
    private readonly CheckBox _chkPrimaryImage = new();
    private readonly PictureBox _imagePreview = new();
    private readonly Label _imageCaption = new();
    private string? _pendingImagePath;
    private readonly ToolTip _toolTip = new();
    private bool _precioVentaCapturado;
    private bool _suppressCategoryChange;
    private int _subcategoryLoadVersion;

    public ProductEditForm(
        IProductService productService,
        ICategoryService categoryService,
        ProductSummary? product = null,
        bool loadDataOnShow = true,
        ISubcategoryService? subcategoryService = null,
        bool canEditPurchaseCost = true,
        IProductAssetService? assetService = null,
        bool canManageImages = false)
    {
        _productService = productService;
        _categoryService = categoryService;
        _subcategoryService = subcategoryService;
        _product = product;
        _canEditPurchaseCost = canEditPurchaseCost;
        _assetService = assetService;
        _canManageImages = canManageImages && assetService is not null;

        BuildUi();
        _precioVentaCapturado = EsEdicion;
        if (loadDataOnShow) Shown += ProductEditForm_Shown;
    }

    public bool EsEdicion => _product is not null;

    private async void ProductEditForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            _suppressCategoryChange = true;
            await LoadCategoriesAsync();
            await LoadSubcategoriesAsync(GetSelectedCategoryId(), _product?.SubcategoriaId);
            LoadProductValues();
            _suppressCategoryChange = false;
            if (!EsEdicion)
            {
                FocusNameField();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al cargar el formulario de producto:\n\n{ex.Message}",
                "Productos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
        finally
        {
            _suppressCategoryChange = false;
        }
    }

    private void BuildUi()
    {
        Text = EsEdicion ? "Editar producto" : "Crear producto";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        var formHeight = EsEdicion ? 580 : 620;
        var formWidth = _canManageImages ? 1010 : 760;
        ClientSize = new Size(formWidth, formHeight);
        MinimumSize = new Size(formWidth, formHeight);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        AddLabel("Codigo interno", 30, 30);
        _txtCodigo.Location = new Point(30, 55);
        _txtCodigo.Size = new Size(250, 32);
        _txtCodigo.PlaceholderText = EsEdicion ? string.Empty : "Opcional; se genera automatico";
        UITheme.StyleTextBox(_txtCodigo);

        AddLabel("Nombre", 310, 30);
        _txtNombre.Location = new Point(310, 55);
        _txtNombre.Size = new Size(420, 32);
        UITheme.StyleTextBox(_txtNombre);

        AddLabel("Descripcion", 30, 100);
        _txtDescripcion.Location = new Point(30, 125);
        _txtDescripcion.Size = new Size(700, 80);
        _txtDescripcion.Multiline = true;
        UITheme.StyleTextBox(_txtDescripcion);

        AddLabel("Categoria", 30, 220);
        _cmbCategoria.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbCategoria.Location = new Point(30, 245);
        _cmbCategoria.Size = new Size(210, 32);
        _cmbCategoria.Font = UITheme.NormalFont;
        _cmbCategoria.SelectedIndexChanged += CmbCategoria_SelectedIndexChanged;

        AddLabel("Subcategoria (opcional)", 260, 220);
        _cmbSubcategoria.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSubcategoria.Location = new Point(260, 245);
        _cmbSubcategoria.Size = new Size(300, 32);
        _cmbSubcategoria.DropDownWidth = 420;
        _cmbSubcategoria.Font = UITheme.NormalFont;

        AddLabel("Modo de producto", 580, 220);
        _cmbModo.Location = new Point(580, 245);
        _cmbModo.Size = new Size(150, 32);
        _cmbModo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbModo.DataSource = Enum.GetValues<ModoProducto>();
        _cmbModo.Font = UITheme.NormalFont;

        ConfigureMoney(_numPrecioCompra, 30, 335);
        AddLabel("Precio compra ($)", 30, 310);
        _numPrecioCompra.Enabled = _canEditPurchaseCost;
        _numPrecioCompra.TabStop = _canEditPurchaseCost;
        _numPrecioCompra.AccessibleDescription = _canEditPurchaseCost
            ? "Costo de compra del producto. Puede ser cero."
            : "Solo un administrador o un usuario con permiso puede editar el costo de compra.";
        _toolTip.SetToolTip(_numPrecioCompra, _numPrecioCompra.AccessibleDescription);

        ConfigureMoney(_numPrecioVenta, 390, 335);
        AddLabel("Precio venta ($)", 390, 310);
        _numPrecioVenta.ValueChanged += (_, _) => _precioVentaCapturado = true;
        _numPrecioVenta.TextChanged += (_, _) =>
        {
            if (_numPrecioVenta.Focused)
            {
                _precioVentaCapturado = true;
            }
        };

        ConfigureStock(_numStockActual, 30, 425);
        AddLabel("Stock actual", 30, 400);
        _numStockActual.Enabled = !EsEdicion;

        ConfigureStock(_numStockMinimo, 390, 425);
        AddLabel("Stock minimo", 390, 400);

        _lblUbicacionInicial.Text = "Ubicación de la cantidad inicial";
        _lblUbicacionInicial.AutoSize = true;
        _lblUbicacionInicial.Location = new Point(30, 465);
        _lblUbicacionInicial.Visible = !EsEdicion;
        UITheme.StyleLabelNormal(_lblUbicacionInicial);

        _cmbUbicacionInicial.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbUbicacionInicial.Location = new Point(30, 490);
        _cmbUbicacionInicial.Size = new Size(250, 32);
        _cmbUbicacionInicial.Font = UITheme.NormalFont;
        _cmbUbicacionInicial.Visible = !EsEdicion;
        _cmbUbicacionInicial.Enabled = !EsEdicion;
        _cmbUbicacionInicial.TabStop = !EsEdicion;
        _cmbUbicacionInicial.AccessibleName = "Ubicación de la cantidad inicial";
        _cmbUbicacionInicial.DisplayMember = nameof(InitialLocationChoice.Text);
        foreach (var choice in InitialLocationChoices)
        {
            _cmbUbicacionInicial.Items.Add(choice);
        }
        _cmbUbicacionInicial.SelectedIndex = 0;
        _toolTip.SetToolTip(
            _cmbUbicacionInicial,
            "Puedes cambiar la ubicación posteriormente desde Inventario.");

        var actionY = EsEdicion ? 510 : 555;

        _btnGuardar.Text = "Guardar";
        _btnGuardar.Location = new Point(500, actionY);
        _btnGuardar.Size = new Size(110, 40);
        _btnGuardar.Click += BtnGuardar_Click;
        UITheme.StylePrimaryButton(_btnGuardar);

        _btnCancelar.Text = "Cancelar";
        _btnCancelar.Location = new Point(630, actionY);
        _btnCancelar.Size = new Size(110, 40);
        _btnCancelar.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(_btnCancelar);

        if (_canManageImages)
        {
            BuildImagePicker();
        }

        Controls.AddRange([
            _txtCodigo,
            _txtNombre,
            _txtDescripcion,
            _cmbCategoria,
            _cmbSubcategoria,
            _cmbModo,
            _lblUbicacionInicial,
            _cmbUbicacionInicial,
            _numPrecioCompra,
            _numPrecioVenta,
            _numStockActual,
            _numStockMinimo,
            _btnGuardar,
            _btnCancelar
        ]);
        CancelButton = _btnCancelar;

        ConfigureTabOrder();
    }

    private void BuildImagePicker()
    {
        var panel = new Panel
        {
            Location = new Point(770, 30),
            Size = new Size(210, 470),
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10)
        };
        var heading = new Label
        {
            Text = "Imagen (opcional)",
            Location = new Point(10, 10),
            Size = new Size(190, 28),
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark
        };
        _imagePreview.Location = new Point(10, 44);
        _imagePreview.Size = new Size(190, 205);
        _imagePreview.SizeMode = PictureBoxSizeMode.Zoom;
        _imagePreview.BackColor = Color.White;
        _imagePreview.Paint += DrawImagePlaceholder;
        _imageCaption.Text = "Sin imagen seleccionada";
        _imageCaption.Location = new Point(10, 255);
        _imageCaption.Size = new Size(190, 44);
        _imageCaption.Font = UITheme.CaptionFont;
        _imageCaption.ForeColor = UITheme.MutedTextColor;
        _imageCaption.TextAlign = ContentAlignment.MiddleCenter;
        _btnChooseImage.Text = "Elegir imagen...";
        _btnChooseImage.Location = new Point(10, 305);
        _btnChooseImage.Size = new Size(190, 36);
        _btnChooseImage.Click += async (_, _) => await ChooseImageAsync();
        UITheme.StyleSecondaryButton(_btnChooseImage);
        _btnClearImage.Text = "Quitar selección";
        _btnClearImage.Location = new Point(10, 348);
        _btnClearImage.Size = new Size(190, 32);
        _btnClearImage.Click += (_, _) => ClearImageSelection();
        _btnClearImage.Enabled = false;
        UITheme.StyleSecondaryButton(_btnClearImage);
        _chkPrimaryImage.Text = "Marcar como principal";
        _chkPrimaryImage.Location = new Point(10, 389);
        _chkPrimaryImage.Size = new Size(190, 28);
        _chkPrimaryImage.Font = UITheme.CaptionFont;
        _chkPrimaryImage.Checked = !EsEdicion;
        _chkPrimaryImage.AccessibleDescription = "Si se marca, la imagen seleccionada será la principal al importarse.";
        _toolTip.SetToolTip(_chkPrimaryImage, _chkPrimaryImage.AccessibleDescription);
        var help = new Label
        {
            Text = "JPG o PNG hasta 12 MB. Se importa al guardar.",
            Location = new Point(10, 419),
            Size = new Size(190, 42),
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor
        };

        panel.Controls.AddRange([heading, _imagePreview, _imageCaption, _btnChooseImage, _btnClearImage, _chkPrimaryImage, help]);
        Controls.Add(panel);
    }

    private async Task ChooseImageAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Elegir imagen de producto",
            Filter = "Imagen JPEG o PNG|*.jpg;*.jpeg;*.png",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await SetPendingImageAsync(dialog.FileName);
    }

    private async Task SetPendingImageAsync(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException("La imagen seleccionada ya no existe.", path);
            }
            if (file.Length is <= 0 or > 12L * 1024 * 1024)
            {
                throw new InvalidDataException("La imagen debe pesar como máximo 12 MB.");
            }

            var bytes = await File.ReadAllBytesAsync(path);
            using var stream = new MemoryStream(bytes, writable: false);
            using var loaded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (loaded.RawFormat.Guid != System.Drawing.Imaging.ImageFormat.Jpeg.Guid &&
                loaded.RawFormat.Guid != System.Drawing.Imaging.ImageFormat.Png.Guid)
            {
                throw new InvalidDataException("Solo se permiten imágenes JPEG o PNG reales.");
            }

            var preview = CreatePreviewBitmap(loaded, maximumPixels: 800);
            var previous = _imagePreview.Image;
            _imagePreview.Image = preview;
            previous?.Dispose();
            _pendingImagePath = path;
            _imageCaption.Text = Path.GetFileName(path);
            _btnClearImage.Enabled = true;
            _imagePreview.Invalidate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vista previa de imagen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearImageSelection()
    {
        _pendingImagePath = null;
        var image = _imagePreview.Image;
        _imagePreview.Image = null;
        image?.Dispose();
        _imageCaption.Text = "Sin imagen seleccionada";
        _btnClearImage.Enabled = false;
        _imagePreview.Invalidate();
    }

    internal string? PendingImagePathForSmoke => _pendingImagePath;
    internal PictureBox ImagePreviewForSmoke => _imagePreview;
    internal Task SelectImageForSmokeAsync(string path) => SetPendingImageAsync(path);

    private static void DrawImagePlaceholder(object? sender, PaintEventArgs e)
    {
        if (sender is not PictureBox picture || picture.Image is not null)
        {
            return;
        }

        TextRenderer.DrawText(e.Graphics, "Vista previa", UITheme.NormalFont, picture.ClientRectangle,
            UITheme.MutedTextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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

    private async Task LoadCategoriesAsync()
    {
        var categories = EsEdicion
            ? await _categoryService.GetAllAsync()
            : await _categoryService.GetActiveAsync();
        categories ??= [];
        if (!EsEdicion && categories.Count == 0)
        {
            categories = [await _categoryService.GetDefaultCategoryAsync()];
        }

        var choices = categories
            .Select(category => new CategoryChoice(
                category.CategoriaId,
                category.Activa || _product?.CategoriaId != category.CategoriaId
                    ? category.Nombre
                    : $"{category.Nombre} (inactiva - clasificación actual)",
                category.Activa,
                EsCategoriaSinCategoria(category.Nombre)))
            .ToList();

        if (EsEdicion && _product is not null && choices.All(choice => choice.Id != _product.CategoriaId))
        {
            choices.Insert(0, new CategoryChoice(
                _product.CategoriaId,
                $"Categoría no disponible (ID {_product.CategoriaId})",
                false,
                false));
        }

        _cmbCategoria.DataSource = choices;
        _cmbCategoria.DisplayMember = nameof(CategoryChoice.Text);
        _cmbCategoria.ValueMember = nameof(CategoryChoice.Id);
        if (_product is not null)
        {
            _cmbCategoria.SelectedValue = _product.CategoriaId;
        }
    }

    private async void CmbCategoria_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressCategoryChange || IsDisposed)
        {
            return;
        }

        try
        {
            await LoadSubcategoriesAsync(GetSelectedCategoryId(), preferredSubcategoryId: null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Subcategorías",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            BindSubcategoryChoices([new SubcategoryChoice(null, "Sin subcategoría", IsHistoricalInactive: false)]);
        }
    }

    private async Task LoadSubcategoriesAsync(int? categoryId, int? preferredSubcategoryId)
    {
        var loadVersion = ++_subcategoryLoadVersion;
        var choices = new List<SubcategoryChoice>
        {
            new(null, "Sin subcategoría", IsHistoricalInactive: false)
        };

        var selectedCategory = _cmbCategoria.SelectedItem as CategoryChoice;
        if (!categoryId.HasValue || selectedCategory?.EsSinCategoria == true || _subcategoryService is null)
        {
            if (loadVersion != _subcategoryLoadVersion)
            {
                return;
            }
            BindSubcategoryChoices(choices);
            return;
        }

        var active = (await _subcategoryService.GetByCategoryAsync(
                categoryId.Value,
                soloActivas: true))
            ?? [];
        choices.AddRange(active
            .Where(item => item.Activa)
            .OrderBy(item => item.Nombre)
            .Select(item => new SubcategoryChoice(item.SubcategoriaId, item.Nombre, IsHistoricalInactive: false)));

        if (loadVersion != _subcategoryLoadVersion)
        {
            return;
        }

        if (preferredSubcategoryId.HasValue && choices.All(choice => choice.Id != preferredSubcategoryId))
        {
            var current = await _subcategoryService.GetByIdAsync(preferredSubcategoryId.Value);
            if (current is not null && current.CategoriaId == categoryId.Value)
            {
                var suffix = current.Activa
                    ? " (no asignable - asignación actual)"
                    : " (inactiva - asignación actual)";
                choices.Add(new SubcategoryChoice(
                    current.SubcategoriaId,
                    current.Nombre + suffix,
                    IsHistoricalInactive: true));
            }
        }

        if (loadVersion != _subcategoryLoadVersion)
        {
            return;
        }

        BindSubcategoryChoices(choices);
        var selected = choices.FirstOrDefault(choice => choice.Id == preferredSubcategoryId)
            ?? choices[0];
        _cmbSubcategoria.SelectedItem = selected;
    }

    private void BindSubcategoryChoices(IReadOnlyList<SubcategoryChoice> choices)
    {
        _cmbSubcategoria.DataSource = null;
        _cmbSubcategoria.DisplayMember = nameof(SubcategoryChoice.Text);
        _cmbSubcategoria.DataSource = choices.ToList();
        _cmbSubcategoria.Enabled = (_cmbCategoria.SelectedItem as CategoryChoice)?.EsSinCategoria != true;
        if (choices.Count > 0)
        {
            _cmbSubcategoria.SelectedIndex = 0;
        }
    }

    private int? GetSelectedCategoryId() =>
        (_cmbCategoria.SelectedItem as CategoryChoice)?.Id;

    private int? GetSelectedSubcategoryId() =>
        (_cmbSubcategoria.SelectedItem as SubcategoryChoice)?.Id;

    private static bool EsCategoriaSinCategoria(string nombre) =>
        string.Equals(nombre.Trim(), ArteInventariado.Infrastructure.Services.CategoryService.DefaultCategoryName,
            StringComparison.OrdinalIgnoreCase);

    private void LoadProductValues()
    {
        if (_product is null)
        {
            return;
        }

        _txtCodigo.Text = _product.CodigoInterno;
        _txtNombre.Text = _product.Nombre;
        _txtDescripcion.Text = _product.Descripcion;
        _cmbModo.SelectedItem = _product.Modo;
        _numPrecioCompra.Value = CentavosToDollars(_product.PrecioCompraCentavos);
        _numPrecioVenta.Value = CentavosToDollars(_product.PrecioVentaCentavos);
        _numStockActual.Value = _product.StockActual;
        _numStockMinimo.Value = _product.StockMinimo;
        _cmbCategoria.SelectedValue = _product.CategoriaId;
    }

    private async void BtnGuardar_Click(object? sender, EventArgs e)
    {
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (!ValidateProductFields())
        {
            return;
        }

        if (RequiresUnassignedInitialStockConfirmation() && !ConfirmUnassignedInitialStock())
        {
            // No means "go back and choose the physical location", not cancel
            // the dialog or silently accept SIN_ASIGNAR.
            _cmbUbicacionInicial.Focus();
            return;
        }

        _btnGuardar.Enabled = false;

        try
        {
            var categoriaId = _cmbCategoria.SelectedValue is int selectedId
                ? selectedId
                : throw new InvalidOperationException("Debe seleccionar una categoria.");
            var subcategoriaId = GetSelectedSubcategoryId();

            if (_product is null)
            {
                var created = await _productService.CreateAsync(new CreateProductRequest(
                    CodigoInterno: _txtCodigo.Text,
                    Nombre: _txtNombre.Text,
                    Descripcion: _txtDescripcion.Text,
                    CategoriaId: categoriaId,
                    ImagenLocalPath: null,
                    PrecioCompraCentavos: DollarsToCentavos(_numPrecioCompra.Value),
                    PrecioVentaCentavos: DollarsToCentavos(_numPrecioVenta.Value),
                    StockActual: (int)_numStockActual.Value,
                    StockMinimo: (int)_numStockMinimo.Value,
                    Modo: (ModoProducto)_cmbModo.SelectedItem!,
                    SubcategoriaId: subcategoriaId,
                    UbicacionInicial: GetSelectedInitialLocation()));
                await ImportPendingImageAsync(created.ProductoId);
            }
            else
            {
                var updated = await _productService.UpdateAsync(new UpdateProductRequest(
                    ProductoId: _product.ProductoId,
                    CodigoInterno: _txtCodigo.Text,
                    Nombre: _txtNombre.Text,
                    Descripcion: _txtDescripcion.Text,
                    CategoriaId: categoriaId,
                    ImagenLocalPath: _product.ImagenLocalPath,
                    PrecioCompraCentavos: DollarsToCentavos(_numPrecioCompra.Value),
                    PrecioVentaCentavos: DollarsToCentavos(_numPrecioVenta.Value),
                    StockMinimo: (int)_numStockMinimo.Value,
                    Modo: (ModoProducto)_cmbModo.SelectedItem!,
                    SubcategoriaId: subcategoriaId));
                await ImportPendingImageAsync(updated.ProductoId);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Validacion de producto",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnGuardar.Enabled = true;
            }
        }
    }

    private async Task ImportPendingImageAsync(int productoId)
    {
        if (string.IsNullOrWhiteSpace(_pendingImagePath) || _assetService is null)
        {
            return;
        }

        try
        {
            await _assetService.ImportAsync(new ImportProductAssetRequest(
                productoId,
                _pendingImagePath,
                SetAsPrimary: _chkPrimaryImage.Checked));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Los datos del producto se guardaron, pero la imagen no se pudo importar. Puedes intentarlo después desde la ficha del producto, pestaña Imágenes.\n\nDetalle: {ex.Message}",
                "Producto guardado; imagen pendiente",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private bool ValidateProductName()
    {
        var normalizedName = _txtNombre.Text.Trim();
        if (normalizedName.Length == 0)
        {
            MessageBox.Show(
                "El nombre del producto es obligatorio.",
                "Validacion de producto",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            FocusNameField();
            return false;
        }

        _txtNombre.Text = normalizedName;
        return true;
    }

    private bool ValidateProductFields()
    {
        if (!ValidateProductName())
        {
            return false;
        }

        if (_product is null && !_precioVentaCapturado)
        {
            return ShowValidationError("El precio de venta es obligatorio.", _numPrecioVenta);
        }

        if (_numPrecioCompra.Value < 0)
        {
            return ShowValidationError("El precio de compra no puede ser negativo.", _numPrecioCompra);
        }

        if (_numPrecioVenta.Value < 0)
        {
            return ShowValidationError("El precio de venta no puede ser negativo.", _numPrecioVenta);
        }

        if (_numStockActual.Value < 0 || _numStockActual.Value != decimal.Truncate(_numStockActual.Value))
        {
            return ShowValidationError("La cantidad inicial debe ser un entero no negativo.", _numStockActual);
        }

        if (_numStockActual.Value > int.MaxValue || _numStockMinimo.Value > int.MaxValue)
        {
            return ShowValidationError("La cantidad de inventario supera el límite permitido.", _numStockActual.Value > int.MaxValue
                ? _numStockActual
                : _numStockMinimo);
        }

        if (_numStockMinimo.Value < 0 || _numStockMinimo.Value != decimal.Truncate(_numStockMinimo.Value))
        {
            return ShowValidationError("El stock mínimo debe ser un entero no negativo.", _numStockMinimo);
        }

        if (_cmbCategoria.SelectedItem is not CategoryChoice)
        {
            return ShowValidationError("Debe seleccionar una categoría.", _cmbCategoria);
        }

        if (_cmbSubcategoria.SelectedItem is not SubcategoryChoice)
        {
            return ShowValidationError("Seleccione una subcategoría o 'Sin subcategoría'.", _cmbSubcategoria);
        }

        if (_cmbModo.SelectedItem is not ModoProducto mode || !Enum.IsDefined(mode))
        {
            return ShowValidationError("Debe seleccionar un modo de producto válido.", _cmbModo);
        }

        return true;
    }

    private static bool ShowValidationError(string message, Control control)
    {
        MessageBox.Show(
            message,
            "Validación de producto",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        control.Focus();
        return false;
    }

    private void FocusNameField()
    {
        ActiveControl = _txtNombre;
        _txtNombre.Focus();
        _txtNombre.SelectAll();
    }

    private void ConfigureTabOrder()
    {
        _txtNombre.TabIndex = 0;
        _txtCodigo.TabIndex = 1;
        _cmbCategoria.TabIndex = 2;
        _cmbSubcategoria.TabIndex = 3;
        _txtDescripcion.TabIndex = 4;
        _cmbModo.TabIndex = 5;
        _numPrecioCompra.TabIndex = 6;
        _numPrecioVenta.TabIndex = 7;
        _numStockActual.TabIndex = 8;
        _cmbUbicacionInicial.TabIndex = 9;
        _numStockMinimo.TabIndex = 10;
        _btnGuardar.TabIndex = 11;
        _btnCancelar.TabIndex = 12;
    }

    private void AddLabel(string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y)
        };
        UITheme.StyleLabelNormal(lbl);
        Controls.Add(lbl);
    }

    private static void ConfigureMoney(NumericUpDown control, int x, int y)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(250, 32);
        control.Maximum = 9_999_999.99m;
        control.Minimum = 0;
        control.DecimalPlaces = 2;
        control.ThousandsSeparator = true;
        control.Increment = 0.25m;
        control.Font = UITheme.NormalFont;
    }

    private static void ConfigureStock(NumericUpDown control, int x, int y)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(250, 32);
        control.Maximum = 1_000_000;
        control.Minimum = 0;
        control.DecimalPlaces = 0;
        control.ThousandsSeparator = true;
        control.Font = UITheme.NormalFont;
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

    private UbicacionInventario GetSelectedInitialLocation() =>
        _cmbUbicacionInicial.SelectedItem is InitialLocationChoice choice
            ? choice.Value
            : UbicacionInventario.SIN_ASIGNAR;

    private bool RequiresUnassignedInitialStockConfirmation() =>
        _product is null &&
        _numStockActual.Value > 0 &&
        GetSelectedInitialLocation() == UbicacionInventario.SIN_ASIGNAR;

    private bool ConfirmUnassignedInitialStock()
    {
        if (ConfirmUnassignedInitialStockForSmoke is not null)
        {
            return ConfirmUnassignedInitialStockForSmoke();
        }

        var quantity = decimal.ToInt32(_numStockActual.Value);
        return MessageBox.Show(
            this,
            $"Vas a registrar {quantity} unidad(es) en «Sin asignar». Esas unidades seguirán en el inventario, pero quedarán pendientes de clasificar en Exhibición o Bodega.\n\n¿Deseas guardar el producto así?\n\nSí: confirmar «Sin asignar».\nNo: volver y elegir Exhibición o Bodega.",
            "Confirmar ubicación inicial",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;
    }

    internal ComboBox CategorySelectorForSmoke => _cmbCategoria;
    internal ComboBox SubcategorySelectorForSmoke => _cmbSubcategoria;
    internal TextBox NameInputForSmoke => _txtNombre;
    internal Button SaveButtonForSmoke => _btnGuardar;
    internal Button CancelButtonForSmoke => _btnCancelar;
    internal IButtonControl? EscapeCancelButtonForSmoke => CancelButton;
    internal ComboBox InitialLocationSelectorForSmoke => _cmbUbicacionInicial;
    internal Func<bool>? ConfirmUnassignedInitialStockForSmoke;
    internal IReadOnlyList<string> InitialLocationOptionsForSmoke =>
        _cmbUbicacionInicial.Items.Cast<InitialLocationChoice>().Select(choice => choice.Text).ToList();
    internal UbicacionInventario SelectedInitialLocationForSmoke => GetSelectedInitialLocation();
    internal int? SelectedSubcategoryIdForSmoke => GetSelectedSubcategoryId();
    internal IReadOnlyList<string> SubcategoryOptionsForSmoke =>
        _cmbSubcategoria.Items.Cast<SubcategoryChoice>().Select(choice => choice.Text).ToList();

    internal async Task SelectCategoryForSmokeAsync(int categoryId)
    {
        _suppressCategoryChange = true;
        try
        {
            _cmbCategoria.SelectedValue = categoryId;
        }
        finally
        {
            _suppressCategoryChange = false;
        }

        await LoadSubcategoriesAsync(categoryId, preferredSubcategoryId: null);
    }

    internal Task SaveForSmokeAsync() => SaveAsync();

    internal void SetRequiredValuesForSmoke(int initialStock = 0)
    {
        _numPrecioCompra.Value = 1;
        _numPrecioVenta.Value = 2;
        _numStockActual.Value = initialStock;
    }

    internal Task LoadForSmokeAsync()
    {
        _suppressCategoryChange = true;
        return LoadForSmokeCoreAsync();
    }

    private async Task LoadForSmokeCoreAsync()
    {
        try
        {
            await LoadCategoriesAsync();
            await LoadSubcategoriesAsync(GetSelectedCategoryId(), _product?.SubcategoriaId);
            LoadProductValues();
        }
        finally
        {
            _suppressCategoryChange = false;
        }
    }

    private sealed record CategoryChoice(int Id, string Text, bool Activa, bool EsSinCategoria);

    private sealed record InitialLocationChoice(UbicacionInventario Value, string Text);

    private static readonly InitialLocationChoice[] InitialLocationChoices =
    [
        new(UbicacionInventario.SIN_ASIGNAR, "Sin asignar"),
        new(UbicacionInventario.EXHIBICION, "Exhibición"),
        new(UbicacionInventario.BODEGA, "Bodega")
    ];

    private sealed record SubcategoryChoice(int? Id, string Text, bool IsHistoricalInactive);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = _imagePreview.Image;
            _imagePreview.Image = null;
            image?.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}
