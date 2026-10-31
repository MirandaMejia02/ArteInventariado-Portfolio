using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class PosSkuCardControl : UserControl
{
    private PosSkuSaleItem _item;
    private readonly bool _canTransfer;
    private readonly bool _compactTouchMode;
    private readonly IProductAssetService? _assets;
    private CancellationTokenSource? _imageLoadCts;
    private readonly PictureBox _picture = new();
    private readonly Label _placeholder = new();
    private readonly Label _name = new();
    private readonly Label _category = new();
    private readonly Label _sku = new();
    private readonly Label _price = new();
    private readonly Label _stock = new();
    private readonly Button _action = new();
    private readonly Button _preview = new();
    private readonly ToolTip _previewToolTip = new();
    private Font? _previewFont;

    public PosSkuCardControl(
        PosSkuSaleItem item,
        bool canTransfer,
        IProductAssetService? assets = null,
        bool compactTouchMode = false)
    {
        _item = item;
        _canTransfer = canTransfer;
        _assets = assets;
        _compactTouchMode = compactTouchMode;
        BuildUi();
        Render();
        HandleCreated += (_, _) => QueueImageLoad();
    }

    public int ProductoSkuId => _item.ProductoSkuId;

    internal bool HasImageForSmoke => _picture.Image is not null;

    internal bool ShowsPlaceholderForSmoke => _placeholder.Visible;

    internal Button PreviewButtonForSmoke => _preview;

    internal PictureBox PictureForSmoke => _picture;

    internal Label PlaceholderForSmoke => _placeholder;

    internal Control PreviewSurfaceForSmoke => _picture.Image is null ? _placeholder : _picture;

    internal bool CompactTouchModeForSmoke => _compactTouchMode;

    internal void ActivateForSmoke() => ActivateCard();

    public event EventHandler<PosSkuSaleItem>? AddRequested;

    public event EventHandler<PosSkuSaleItem>? MoveRequested;

    public event EventHandler<PosSkuSaleItem>? PreviewRequested;

    public void UpdateItem(PosSkuSaleItem item)
    {
        if (item.ProductoSkuId != _item.ProductoSkuId)
        {
            return;
        }

        var imageChanged = !string.Equals(ImageKey(_item), ImageKey(item),
            StringComparison.OrdinalIgnoreCase);
        _item = item;
        Render();
        if (imageChanged)
        {
            QueueImageLoad();
        }
    }

    private void BuildUi()
    {
        Size = _compactTouchMode ? new Size(178, 264) : new Size(190, 306);
        Margin = _compactTouchMode ? new Padding(6) : new Padding(9);
        BackColor = UITheme.SurfaceColor;
        Padding = new Padding(8);
        Cursor = Cursors.Hand;

        _picture.SetBounds(12, 12, _compactTouchMode ? 154 : 166, _compactTouchMode ? 96 : 86);
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        _picture.BackColor = UITheme.SoftPinkColor;

        _placeholder.SetBounds(12, 12, _compactTouchMode ? 154 : 166, _compactTouchMode ? 96 : 86);
        _placeholder.Text = "Sin imagen";
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = UITheme.MutedTextColor;
        _placeholder.BackColor = UITheme.SoftPinkColor;
        _placeholder.Font = new Font("Segoe UI", 9, FontStyle.Bold);

        _name.SetBounds(12, _compactTouchMode ? 112 : 104, _compactTouchMode ? 154 : 166, 36);
        _name.TextAlign = ContentAlignment.TopCenter;
        _name.Font = UITheme.NormalBoldFont;
        _name.ForeColor = UITheme.TextDark;
        _name.AutoEllipsis = true;

        _category.SetBounds(12, _compactTouchMode ? 148 : 140, _compactTouchMode ? 154 : 166, 30);
        _category.TextAlign = ContentAlignment.MiddleCenter;
        _category.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        _category.ForeColor = UITheme.MutedTextColor;
        _category.AutoEllipsis = false;

        _sku.SetBounds(12, _compactTouchMode ? 178 : 170, _compactTouchMode ? 154 : 166, 20);
        _sku.TextAlign = ContentAlignment.MiddleCenter;
        _sku.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _sku.ForeColor = UITheme.MutedTextColor;
        _sku.AutoEllipsis = true;

        _price.SetBounds(12, _compactTouchMode ? 198 : 190, _compactTouchMode ? 154 : 166, 30);
        _price.TextAlign = ContentAlignment.MiddleCenter;
        _price.Font = UITheme.HeaderFont;
        _price.ForeColor = UITheme.PrimaryColor;

        _stock.SetBounds(12, _compactTouchMode ? 228 : 220, _compactTouchMode ? 154 : 166, _compactTouchMode ? 24 : 30);
        _stock.TextAlign = ContentAlignment.MiddleCenter;
        _stock.Font = new Font("Segoe UI", 8, FontStyle.Regular);
        _stock.ForeColor = UITheme.TextDark;

        _action.SetBounds(12, 258, 166, 34);
        _action.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _action.TextAlign = ContentAlignment.MiddleCenter;
        _action.Click += (_, _) =>
        {
            if (_item.DisponibleParaVenta)
            {
                AddRequested?.Invoke(this, _item);
            }
            else if (_canTransfer && _item.StockDisponibleBodega > 0)
            {
                MoveRequested?.Invoke(this, _item);
            }
        };

        Controls.AddRange([_picture, _placeholder, _name, _category, _sku, _price, _stock, _action]);

        _preview.SetBounds(_placeholder.Width - 34, 2, 32, 32);
        _preview.Text = "\uE890";
        _previewFont = new Font("Segoe MDL2 Assets", 12f, FontStyle.Regular);
        _preview.Font = _previewFont;
        _preview.UseVisualStyleBackColor = false;
        _preview.BackColor = UITheme.PrimaryColor;
        _preview.ForeColor = Color.White;
        _preview.FlatStyle = FlatStyle.Flat;
        _preview.FlatAppearance.BorderColor = UITheme.SecondaryColor;
        _preview.FlatAppearance.BorderSize = 1;
        _preview.FlatAppearance.MouseOverBackColor = UITheme.SecondaryColor;
        _preview.Cursor = Cursors.Hand;
        _preview.TabStop = false;
        _preview.AccessibleName = "Ver producto";
        _preview.AccessibleDescription = "Abrir imagen y detalles ampliados del producto";
        _previewToolTip.SetToolTip(_preview, "Ver imagen y detalles · Esc para cerrar");
        _preview.Click += (_, _) => PreviewRequested?.Invoke(this, _item);
        _placeholder.Controls.Add(_preview);
        _preview.BringToFront();

        if (_compactTouchMode)
        {
            _action.Visible = false;
            foreach (var control in new Control[] { this, _picture, _placeholder, _name, _category, _sku, _price, _stock })
            {
                control.Cursor = Cursors.Hand;
                control.Click += (_, _) => ActivateCard();
            }
        }
    }

    private void ActivateCard()
    {
        if (_compactTouchMode)
        {
            AddRequested?.Invoke(this, _item);
            return;
        }

        if (_item.DisponibleParaVenta)
        {
            AddRequested?.Invoke(this, _item);
        }
        else if (_canTransfer && _item.StockDisponibleBodega > 0)
        {
            MoveRequested?.Invoke(this, _item);
        }
    }

    private void Render()
    {
        var productText = string.IsNullOrWhiteSpace(_item.VarianteNombre)
            ? _item.ProductoNombre
            : $"{_item.ProductoNombre} · {_item.VarianteNombre}";
        var categoryText = string.IsNullOrWhiteSpace(_item.CategoriaNombre)
            ? null
            : $"{_item.CategoriaNombre} · {_item.SubcategoriaNombre ?? "Sin subcategoría"}";
        _name.Text = productText;
        _category.Text = categoryText is null
            ? "No disponible"
            : $"{_item.CategoriaNombre}{Environment.NewLine}{_item.SubcategoriaNombre ?? "Sin subcategoría"}";
        _sku.Text = $"SKU: {_item.CodigoSku}";
        _price.Text = $"${_item.PrecioVentaEfectivoCentavos / 100m:0.00}";
        _stock.Text = _compactTouchMode
            ? $"Disp. {_item.StockDisponibleExhibicion}"
            : $"Exhibición: {_item.StockDisponibleExhibicion}  ·  Bodega: {_item.StockDisponibleBodega}";

        if (_item.DisponibleParaVenta)
        {
            _action.Text = "AGREGAR  +";
            _action.Enabled = true;
            UITheme.StylePrimaryButton(_action);
        }
        else if (_item.StockDisponibleBodega > 0)
        {
            _action.Text = _canTransfer ? "MOVER A EXHIBICIÓN" : $"EN BODEGA: {_item.StockDisponibleBodega}";
            _action.Enabled = _canTransfer;
            UITheme.StyleSecondaryButton(_action);
        }
        else
        {
            _action.Text = "SIN STOCK EN EXHIBICIÓN";
            _action.Enabled = false;
            UITheme.StyleSecondaryButton(_action);
        }

    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(UITheme.NeutralBorderColor, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            _imageLoadCts = null;
            _previewToolTip.Dispose();
            _previewFont?.Dispose();
            var image = _picture.Image;
            _picture.Image = null;
            image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void QueueImageLoad()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        _imageLoadCts?.Cancel();
        _imageLoadCts?.Dispose();
        _imageLoadCts = new CancellationTokenSource();
        _ = LoadImageAsync(_item, _imageLoadCts.Token);
    }

    private async Task LoadImageAsync(PosSkuSaleItem item, CancellationToken cancellationToken)
    {
        Image? loaded = null;
        try
        {
            var path = await ResolveImagePathAsync(item, cancellationToken);
            loaded = await TryLoadImageAsync(path, cancellationToken);
            if (cancellationToken.IsCancellationRequested || IsDisposed || Disposing ||
                !string.Equals(ImageKey(item), ImageKey(_item), StringComparison.OrdinalIgnoreCase))
            {
                loaded?.Dispose();
                return;
            }

            ReplaceImage(loaded);
            loaded = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            loaded?.Dispose();
        }
        catch
        {
            loaded?.Dispose();
            if (!cancellationToken.IsCancellationRequested && !IsDisposed && !Disposing)
            {
                ReplaceImage(null);
            }
        }
    }

    private async Task<string?> ResolveImagePathAsync(
        PosSkuSaleItem item,
        CancellationToken cancellationToken)
    {
        if (_assets is not null && item.ImagenOwnerKind.HasValue && item.ImagenAssetId.HasValue)
        {
            var thumbnail = await _assets.GetThumbnailPathAsync(
                item.ImagenOwnerKind.Value,
                item.ImagenAssetId.Value,
                240,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(thumbnail))
            {
                return thumbnail;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.ImagenRutaRelativa) &&
            Path.IsPathRooted(item.ImagenRutaRelativa))
        {
            return item.ImagenRutaRelativa;
        }

        return item.ImagenLocalPath;
    }

    private void ReplaceImage(Image? image)
    {
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => ReplaceImage(image)));
            }
            catch (InvalidOperationException)
            {
                image?.Dispose();
            }
            return;
        }

        var previous = _picture.Image;
        _picture.Image = image;
        _placeholder.Visible = image is null;
        var visibleSurface = image is null ? (Control)_placeholder : _picture;
        if (_preview.Parent != visibleSurface)
        {
            _preview.Parent?.Controls.Remove(_preview);
            _preview.SetBounds(visibleSurface.ClientSize.Width - 34, 2, 32, 32);
            visibleSurface.Controls.Add(_preview);
        }

        _preview.BringToFront();

        previous?.Dispose();
    }

    private static string ImageKey(PosSkuSaleItem item) =>
        $"{item.ImagenOwnerKind}:{item.ImagenAssetId}:{item.ImagenRutaRelativa}:{item.ImagenLocalPath}";

    private static async Task<Image?> TryLoadImageAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
