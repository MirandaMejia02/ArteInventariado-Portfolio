using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

internal sealed class ProductQuickPreviewForm : Form
{
    private readonly PosSkuSaleItem _item;
    private readonly IProductAssetService? _assets;
    private readonly CancellationTokenSource _imageLoadCancellation = new();
    private readonly PictureBox _picture = new();
    private readonly Label _imageMessage = new();
    private readonly Button _close = new();

    public ProductQuickPreviewForm(PosSkuSaleItem item, IProductAssetService? assets)
    {
        _item = item;
        _assets = assets;

        Text = "Detalles del producto";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(850, 540);
        KeyPreview = true;
        BackColor = UITheme.BackgroundColor;
        UITheme.StyleForm(this);

        BuildUi();
        Shown += async (_, _) => await LoadImageAsync(_imageLoadCancellation.Token);
    }

    internal bool HasImageForSmoke => _picture.Image is not null;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageLoadCancellation.Cancel();
            _imageLoadCancellation.Dispose();
            var image = _picture.Image;
            _picture.Image = null;
            image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12, 7, 12, 5),
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 0, 10)
        };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var productName = string.IsNullOrWhiteSpace(_item.VarianteNombre)
            ? _item.ProductoNombre
            : $"{_item.ProductoNombre} · {_item.VarianteNombre}";
        header.Controls.Add(new Label
        {
            Text = productName,
            Dock = DockStyle.Fill,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            AccessibleName = "Nombre del producto"
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = $"Código: {_item.CodigoInterno}   ·   SKU: {_item.CodigoSku}",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        content.Controls.Add(BuildImagePanel(), 0, 0);
        content.Controls.Add(BuildDetailsPanel(), 1, 0);
        root.Controls.Add(content, 0, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        footer.Controls.Add(new Label
        {
            Text = "La venta usa el disponible de Exhibición; Bodega y Sin asignar no se venden directamente.",
            Dock = DockStyle.Fill,
            ForeColor = UITheme.MutedTextColor,
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);

        _close.Text = "Cerrar · Esc";
        _close.DialogResult = DialogResult.Cancel;
        _close.Dock = DockStyle.Fill;
        _close.Margin = new Padding(8, 0, 0, 0);
        UITheme.StyleSecondaryButton(_close);
        footer.Controls.Add(_close, 1, 0);
        root.Controls.Add(footer, 0, 2);

        CancelButton = _close;
        Controls.Add(root);
    }

    private Control BuildImagePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 8, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.Controls.Add(new Label
        {
            Text = "Imagen del producto",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var imageHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SoftPinkColor,
            Margin = Padding.Empty
        };
        _picture.Dock = DockStyle.Fill;
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        _picture.BackColor = UITheme.SoftPinkColor;
        _imageMessage.Text = "Cargando imagen...";
        _imageMessage.Dock = DockStyle.Fill;
        _imageMessage.TextAlign = ContentAlignment.MiddleCenter;
        _imageMessage.ForeColor = UITheme.MutedTextColor;
        _imageMessage.BackColor = UITheme.SoftPinkColor;
        imageHost.Controls.Add(_picture);
        imageHost.Controls.Add(_imageMessage);
        panel.Controls.Add(imageHost, 0, 1);
        panel.Controls.Add(new Label
        {
            Text = "Vista ampliada",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 2);
        return panel;
    }

    private Control BuildDetailsPanel()
    {
        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16, 10, 12, 10),
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 0, 0)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        for (var row = 1; row < details.RowCount; row++)
        {
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 1));
        }

        details.Controls.Add(new Label
        {
            Text = "Detalles",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        details.SetColumnSpan(details.Controls[0], 2);

        AddDetail(details, 1, "Categoría", _item.CategoriaNombre);
        AddDetail(details, 2, "Subcategoría", _item.SubcategoriaNombre ?? "Sin subcategoría");
        AddDetail(details, 3, "Modo", FormatMode(_item.Modo));
        AddDetail(details, 4, "Precio de venta", $"${_item.PrecioVentaEfectivoCentavos / 100m:N2}", emphasize: true);
        AddDetail(details, 5, "Exhibición", FormatStock(
            _item.StockFisicoExhibicion,
            _item.StockReservadoExhibicion,
            _item.StockDisponibleExhibicion));
        AddDetail(details, 6, "Bodega", FormatStock(
            _item.StockFisicoBodega,
            _item.StockReservadoBodega,
            _item.StockDisponibleBodega));
        AddDetail(details, 7, "Sin asignar", FormatStock(
            _item.StockFisicoSinAsignar,
            _item.StockReservadoSinAsignar,
            _item.StockDisponibleSinAsignar));
        AddDetail(details, 8, "Existencia física", _item.StockFisicoTotal.ToString("N0"));
        AddDetail(details, 9, "Disponible total", _item.StockDisponibleTotal.ToString("N0"), emphasize: true);
        return details;
    }

    private static void AddDetail(
        TableLayoutPanel table,
        int row,
        string label,
        string value,
        bool emphasize = false)
    {
        table.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, row);
        table.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = emphasize ? UITheme.NormalBoldFont : UITheme.NormalFont,
            ForeColor = emphasize ? UITheme.PrimaryColor : UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 1, row);
    }

    private static string FormatStock(int physical, int reserved, int available) =>
        $"Físico {physical:N0} · Reserv. {reserved:N0} · Disp. {available:N0}";

    private static string FormatMode(ModoProducto mode) => mode switch
    {
        ModoProducto.SIMPLE => "Simple",
        ModoProducto.SURTIDO => "Surtido",
        ModoProducto.POR_VARIANTES => "Por variantes",
        _ => mode.ToString()
    };

    private async Task LoadImageAsync(CancellationToken cancellationToken)
    {
        Image? image = null;
        try
        {
            var path = await ResolveImagePathAsync(cancellationToken);
            image = await TryLoadImageAsync(path, cancellationToken);
            if (image is null && !string.IsNullOrWhiteSpace(_item.ImagenLocalPath) &&
                !string.Equals(path, _item.ImagenLocalPath, StringComparison.OrdinalIgnoreCase))
            {
                image = await TryLoadImageAsync(_item.ImagenLocalPath, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested || IsDisposed || Disposing)
            {
                image?.Dispose();
                return;
            }

            _picture.Image = image;
            image = null;
            _imageMessage.Visible = _picture.Image is null;
            _imageMessage.Text = "Sin imagen disponible";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            image?.Dispose();
        }
        catch
        {
            image?.Dispose();
            if (!IsDisposed && !Disposing)
            {
                _imageMessage.Text = "No se pudo cargar la imagen";
                _imageMessage.Visible = true;
            }
        }
    }

    private async Task<string?> ResolveImagePathAsync(CancellationToken cancellationToken)
    {
        if (_assets is not null && _item.ImagenOwnerKind.HasValue && _item.ImagenAssetId.HasValue)
        {
            try
            {
                var thumbnail = await _assets.GetThumbnailPathAsync(
                    _item.ImagenOwnerKind.Value,
                    _item.ImagenAssetId.Value,
                    720,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(thumbnail))
                {
                    return thumbnail;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Fall through to the existing local path if a thumbnail is unavailable.
            }
        }

        if (!string.IsNullOrWhiteSpace(_item.ImagenRutaRelativa) &&
            Path.IsPathRooted(_item.ImagenRutaRelativa))
        {
            return _item.ImagenRutaRelativa;
        }

        return _item.ImagenLocalPath;
    }

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
