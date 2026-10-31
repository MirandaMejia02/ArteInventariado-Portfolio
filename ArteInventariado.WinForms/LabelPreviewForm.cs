using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

/// <summary>
/// Preview lógico y gráfico. Solo renderiza la etiqueta visible, incluso para
/// lotes grandes, para no reservar cientos de bitmaps en memoria.
/// </summary>
internal sealed class LabelPreviewForm : Form
{
    private readonly LabelBatchPreview _batch;
    private readonly ICode128BarcodeRenderer _renderer;
    private readonly IQrCodeRenderer? _qrRenderer;
    private readonly IPeripheralOutputCoordinator? _peripheralOutput;
    private readonly PictureBox _barcodeImage = new();
    private readonly Label _name = new();
    private readonly Label _variant = new();
    private readonly Label _barcodeText = new();
    private readonly Label _price = new();
    private readonly Label _status = new();
    private readonly Button _previous = new();
    private readonly Button _next = new();
    private readonly Button _print = new();
    private int _copyIndex;

    public LabelPreviewForm(
        LabelData label,
        ICode128BarcodeRenderer renderer,
        ProductLabelProfile profile = ProductLabelProfile.Mm40x30,
        IQrCodeRenderer? qrRenderer = null,
        IPeripheralOutputCoordinator? peripheralOutput = null)
        : this(new LabelBatchPreview(profile, [new LabelBatchItem(label)]), renderer, qrRenderer, peripheralOutput)
    {
    }

    public LabelPreviewForm(
        LabelBatchPreview batch,
        ICode128BarcodeRenderer renderer,
        IQrCodeRenderer? qrRenderer = null,
        IPeripheralOutputCoordinator? peripheralOutput = null)
    {
        _batch = batch ?? throw new ArgumentNullException(nameof(batch));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _qrRenderer = qrRenderer;
        _peripheralOutput = peripheralOutput;
        if (_batch.Items.Count == 0 || _batch.TotalCopies <= 0)
            throw new ArgumentException("El lote de etiquetas no contiene copias.", nameof(batch));
        BuildUi();
        RenderCurrent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = _barcodeImage.Image;
            _barcodeImage.Image = null;
            image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Text = "Vista previa de etiquetas";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 610);
        MinimumSize = new Size(640, 520);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        root.Controls.Add(new Label
        {
            Text = "Vista previa de etiquetas",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.TextDark
        }, 0, 0);

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(22),
            Margin = new Padding(68, 4, 68, 10),
            ColumnCount = 1,
            RowCount = 5
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.Paint += (_, args) => ControlPaint.DrawBorder(
            args.Graphics, card.ClientRectangle, UITheme.DashboardBorderColor, ButtonBorderStyle.Solid);

        ConfigureText(_name, UITheme.SubheaderFont, ContentAlignment.MiddleCenter);
        ConfigureText(_variant, UITheme.NormalFont, ContentAlignment.MiddleCenter, UITheme.MutedTextColor);
        _barcodeImage.Dock = DockStyle.Fill;
        _barcodeImage.SizeMode = PictureBoxSizeMode.CenterImage;
        _barcodeImage.BackColor = Color.White;
        ConfigureText(_barcodeText, new Font(UITheme.NormalFont.FontFamily, 10, FontStyle.Bold), ContentAlignment.MiddleCenter);
        ConfigureText(_price, UITheme.NormalBoldFont, ContentAlignment.MiddleCenter, UITheme.PrimaryColor);
        card.Controls.Add(_name, 0, 0);
        card.Controls.Add(_variant, 0, 1);
        card.Controls.Add(_barcodeImage, 0, 2);
        card.Controls.Add(_barcodeText, 0, 3);
        card.Controls.Add(_price, 0, 4);
        root.Controls.Add(card, 0, 1);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Font = UITheme.NormalFont;
        _status.ForeColor = UITheme.MutedTextColor;
        root.Controls.Add(_status, 0, 2);

        // Sin padding vertical: en 125 % el redondeo DPI de una fila absoluta
        // podía dejar el borde inferior de los botones fuera por un píxel.
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        ConfigureAction(_previous, "Anterior", UITheme.StyleSecondaryButton);
        ConfigureAction(_next, "Siguiente", UITheme.StyleSecondaryButton);
        _print.Text = "Imprimir etiquetas";
        _print.Dock = DockStyle.Fill;
        _print.Enabled = _peripheralOutput is not null;
        _print.Margin = new Padding(10, 0, 0, 1);
        UITheme.StyleSecondaryButton(_print);
        var close = new Button { Text = "Cerrar", Dock = DockStyle.Fill, DialogResult = DialogResult.OK, Margin = new Padding(10, 0, 0, 1) };
        UITheme.StylePrimaryButton(close);
        _previous.Click += (_, _) => { if (_copyIndex > 0) { _copyIndex--; RenderCurrent(); } };
        _next.Click += (_, _) => { if (_copyIndex + 1 < _batch.TotalCopies) { _copyIndex++; RenderCurrent(); } };
        _print.Click += async (_, _) => await PrintCurrentAsync();
        actions.Controls.Add(_previous, 0, 0);
        actions.Controls.Add(_next, 1, 0);
        actions.Controls.Add(_print, 2, 0);
        actions.Controls.Add(close, 3, 0);
        root.Controls.Add(actions, 0, 3);
        AcceptButton = close;
        Controls.Add(root);
    }

    private void RenderCurrent()
    {
        var definition = ProductLabelProfiles.Get(_batch.Profile);
        var label = GetLabelAt(_copyIndex);
        _name.Text = VisualEllipsis(label.ProductName, _batch.Profile);
        _variant.Text = string.IsNullOrWhiteSpace(label.VariantName) || _batch.Profile == ProductLabelProfile.Mm30x20
            ? string.Empty
            : VisualEllipsis(label.VariantName, _batch.Profile);
        _price.Text = label.PriceCents.HasValue
            ? $"Precio: {ReceiptTextRenderer.Money(label.PriceCents.Value)}"
            : string.Empty;
        _previous.Enabled = _copyIndex > 0;
        _next.Enabled = _copyIndex + 1 < _batch.TotalCopies;

        var previous = _barcodeImage.Image;
        _barcodeImage.Image = null;
        previous?.Dispose();
        if (string.IsNullOrWhiteSpace(label.Barcode))
        {
            _barcodeText.Text = "Este SKU no tiene código de barras.";
            _status.Text = "No se generó un código durante la impresión. Use “Generar códigos faltantes” como acción explícita.";
            return;
        }

        if (label.Symbology == BarcodeSymbology.Qr && !ProductLabelProfiles.SupportsQr(_batch.Profile))
        {
            _barcodeText.Text = "QR no permitido en 25 × 15 mm.";
            _status.Text = "Seleccione un perfil QR compatible.";
            return;
        }
        var render = label.Symbology == BarcodeSymbology.Qr
            ? (_qrRenderer ?? throw new InvalidOperationException("El renderizador QR no está disponible.")).Render(label.Barcode, _batch.Profile)
            : _renderer.Render(label.Barcode, _batch.Profile);
        using var stream = new MemoryStream(render.PngBytes, writable: false);
        using var loaded = Image.FromStream(stream);
        _barcodeImage.Image = new Bitmap(loaded);
        _barcodeText.Text = render.Value;
        _status.Text = $"Etiqueta {_copyIndex + 1} de {_batch.TotalCopies} · {definition.DisplayName} · {(label.Symbology == BarcodeSymbology.Qr ? "QR" : "Code 128")}";
    }

    private LabelData GetLabelAt(int copyIndex)
    {
        var remaining = copyIndex;
        foreach (var item in _batch.Items)
        {
            if (remaining < item.Label.Quantity) return item.Label;
            remaining -= item.Label.Quantity;
        }
        throw new InvalidOperationException("El índice de etiqueta no es válido.");
    }

    private async Task PrintCurrentAsync()
    {
        if (_peripheralOutput is null) return;
        var result = await _peripheralOutput.TryPrintLabelsAsync(GetLabelAt(_copyIndex));
        _status.Text = result.Message;
        MessageBox.Show(result.Message, "Etiquetas", MessageBoxButtons.OK,
            result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private static void ConfigureText(Label label, Font font, ContentAlignment alignment, Color? color = null)
    {
        label.Dock = DockStyle.Fill;
        label.TextAlign = alignment;
        label.Font = font;
        label.ForeColor = color ?? UITheme.TextDark;
        label.AutoEllipsis = false;
    }

    private static void ConfigureAction(Button button, string text, Action<Button> style)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        // Un píxel inferior de margen absorbe el redondeo WinForms a 125 %
        // y 150 %, sin modificar el tamaño táctil ni cortar el contenido.
        button.Margin = new Padding(0, 0, 6, 1);
        style(button);
    }

    private static string VisualEllipsis(string value, ProductLabelProfile profile)
    {
        var maximum = profile switch
        {
            ProductLabelProfile.Mm30x20 => 22,
            ProductLabelProfile.Mm40x30 => 32,
            _ => 46
        };
        return value.Length <= maximum ? value : value[..(maximum - 1)] + "…";
    }
}

internal sealed class LabelQuantityDialog : Form
{
    private readonly NumericUpDown _quantity = new() { Minimum = 1, Maximum = ProductLabelProfiles.MaxCopiesPerSku, Value = 1 };
    private readonly CheckBox _includePrice = new() { Text = "Incluir precio", Checked = true, AutoSize = true };
    private readonly ComboBox _profile = new();

    public LabelQuantityDialog(bool forQr = false)
    {
        Text = "Preparar etiquetas";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 278);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 2,
            RowCount = 5,
            BackColor = UITheme.BackgroundColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(FieldLabel("Cantidad"), 0, 0);
        _quantity.Dock = DockStyle.Fill; _quantity.Font = UITheme.NormalFont; root.Controls.Add(_quantity, 1, 0);
        root.Controls.Add(FieldLabel("Perfil"), 0, 1);
        _profile.DataSource = ProductLabelProfiles.All
            .Where(profile => !forQr || ProductLabelProfiles.SupportsQr(profile.Profile))
            .ToList();
        _profile.DisplayMember = nameof(ProductLabelProfileDefinition.DisplayName);
        _profile.ValueMember = nameof(ProductLabelProfileDefinition.Profile);
        _profile.SelectedIndex = _profile.Items.Count > 1 ? 1 : _profile.Items.Count == 1 ? 0 : -1;
        _profile.Dock = DockStyle.Fill;
        UITheme.StyleInput(_profile);
        root.Controls.Add(_profile, 1, 1);
        _includePrice.Dock = DockStyle.Fill; _includePrice.Font = UITheme.NormalFont; root.Controls.Add(_includePrice, 1, 2);
        var note = new Label
        {
            Text = forQr
                ? "La vista previa genera un código QR; no se enviará nada a hardware."
                : "La vista previa genera barras Code 128; no se enviará nada a hardware.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor
        };
        root.Controls.Add(note, 0, 3); root.SetColumnSpan(note, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var accept = new Button { Text = "Ver vista previa", Size = new Size(160, 40), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancelar", Size = new Size(120, 40), DialogResult = DialogResult.Cancel };
        UITheme.StylePrimaryButton(accept); UITheme.StyleSecondaryButton(cancel);
        actions.Controls.AddRange([accept, cancel]); root.Controls.Add(actions, 0, 4); root.SetColumnSpan(actions, 2);
        AcceptButton = accept; CancelButton = cancel; Controls.Add(root);
    }

    public int Quantity => (int)_quantity.Value;
    public bool IncludePrice => _includePrice.Checked;
    public ProductLabelProfile Profile =>
        (_profile.SelectedItem as ProductLabelProfileDefinition ??
            ProductLabelProfiles.Get(ProductLabelProfile.Mm40x30)).Profile;

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = UITheme.NormalBoldFont
    };
}
