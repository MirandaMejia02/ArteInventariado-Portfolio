using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public class ProductCardControl : UserControl
{
    private ProductSummary _product;
    private readonly PictureBox _picture = new();
    private readonly Label _lblName = new();
    private readonly Label _lblPrice = new();
    private readonly Label _lblStock = new();
    private readonly Label _lblLowStock = new();
    private readonly Label _lblPlaceholder = new();
    private readonly Button _btnAdd = new();

    public ProductCardControl(ProductSummary product)
    {
        _product = product;
        BuildUi();
        LoadImage();
    }

    public int ProductId => _product.ProductoId;

    public event EventHandler<ProductSummary>? AddRequested;

    public void UpdateProduct(ProductSummary product)
    {
        if (product.ProductoId != _product.ProductoId)
        {
            return;
        }

        var imageChanged = !string.Equals(_product.ImagenLocalPath, product.ImagenLocalPath, StringComparison.OrdinalIgnoreCase);
        _product = product;
        _lblName.Text = _product.Nombre;
        _lblPrice.Text = $"${FormatDollars(_product.PrecioVentaCentavos)}";
        _lblStock.Text = $"Stock: {_product.StockActual}";
        _lblLowStock.Text = _product.StockActual <= _product.StockMinimo ? "BAJO STOCK" : string.Empty;
        _btnAdd.Enabled = _product.StockActual > 0;

        if (imageChanged)
        {
            var previous = _picture.Image;
            _picture.Image = null;
            previous?.Dispose();
            LoadImage();
        }
    }

    private void BuildUi()
    {
        Size = new Size(182, 268);
        Margin = new Padding(10);
        BorderStyle = BorderStyle.None;
        BackColor = UITheme.SurfaceColor;
        Padding = new Padding(8);
        Cursor = Cursors.Hand;

        _picture.Location = new Point(12, 12);
        _picture.Size = new Size(158, 90);
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        _picture.BackColor = UITheme.SoftPinkColor;

        _lblPlaceholder.Text = "Sin imagen";
        _lblPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
        _lblPlaceholder.Location = _picture.Location;
        _lblPlaceholder.Size = _picture.Size;
        _lblPlaceholder.ForeColor = UITheme.MutedTextColor;
        _lblPlaceholder.BackColor = UITheme.SoftPinkColor;
        _lblPlaceholder.Font = new Font("Segoe UI", 9, FontStyle.Bold);

        _lblName.Text = _product.Nombre;
        _lblName.Location = new Point(12, 112);
        _lblName.Size = new Size(158, 42);
        _lblName.Font = UITheme.NormalBoldFont;
        _lblName.ForeColor = UITheme.TextDark;
        _lblName.TextAlign = ContentAlignment.TopCenter;

        _lblPrice.Text = $"${FormatDollars(_product.PrecioVentaCentavos)}";
        _lblPrice.Location = new Point(12, 158);
        _lblPrice.Size = new Size(158, 34);
        _lblPrice.Font = UITheme.HeaderFont;
        _lblPrice.ForeColor = UITheme.PrimaryColor;
        _lblPrice.TextAlign = ContentAlignment.MiddleCenter;

        _lblStock.Text = $"Stock: {_product.StockActual}";
        _lblStock.Location = new Point(12, 196);
        _lblStock.Size = new Size(72, 22);
        _lblStock.Font = new Font("Segoe UI", 9, FontStyle.Regular);
        _lblStock.ForeColor = UITheme.TextDark;
        _lblStock.TextAlign = ContentAlignment.MiddleLeft;

        _lblLowStock.Text = _product.StockActual <= _product.StockMinimo ? "BAJO STOCK" : string.Empty;
        _lblLowStock.Location = new Point(84, 196);
        _lblLowStock.Size = new Size(86, 22);
        _lblLowStock.Font = new Font("Segoe UI", 7, FontStyle.Bold);
        _lblLowStock.ForeColor = UITheme.DangerColor;
        _lblLowStock.TextAlign = ContentAlignment.MiddleRight;

        _btnAdd.Text = "AGREGAR  +";
        _btnAdd.Location = new Point(12, 226);
        _btnAdd.Size = new Size(158, 34);
        _btnAdd.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _btnAdd.Enabled = _product.StockActual > 0;
        _btnAdd.Cursor = Cursors.Hand;
        _btnAdd.Click += (_, _) => AddRequested?.Invoke(this, _product);
        UITheme.StylePrimaryButton(_btnAdd);
        _btnAdd.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _btnAdd.TextAlign = ContentAlignment.MiddleCenter;

        Controls.AddRange([
            _picture,
            _lblPlaceholder,
            _lblName,
            _lblPrice,
            _lblStock,
            _lblLowStock,
            _btnAdd
        ]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(UITheme.NeutralBorderColor, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private void LoadImage()
    {
        _picture.Image = ProductCardImageCache.GetImageCopy(_product.ImagenLocalPath);
        _lblPlaceholder.Visible = _picture.Image is null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = _picture.Image;
            _picture.Image = null;
            image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string FormatDollars(long centavos)
    {
        return (centavos / 100m).ToString("0.00");
    }

    private static class ProductCardImageCache
    {
        private static readonly Dictionary<string, Image> Images = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object SyncRoot = new();

        public static Image? GetImageCopy(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(imagePath.Trim());
            if (!File.Exists(fullPath))
            {
                return null;
            }

            lock (SyncRoot)
            {
                if (!Images.TryGetValue(fullPath, out var cachedImage))
                {
                    cachedImage = TryLoadDetachedImage(fullPath);
                    if (cachedImage is null)
                    {
                        return null;
                    }

                    Images[fullPath] = cachedImage;
                }

                return (Image)cachedImage.Clone();
            }
        }

        private static Image? TryLoadDetachedImage(string fullPath)
        {
            try
            {
                using var stream = new MemoryStream(File.ReadAllBytes(fullPath));
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }
    }
}
