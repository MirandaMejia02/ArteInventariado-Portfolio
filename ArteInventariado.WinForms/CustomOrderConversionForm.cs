using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CustomOrderConversionForm : Form
{
    private readonly int _orderId;
    private readonly ICustomOrderFinanceService _finance;
    private readonly IPosCatalogService _products;
    private CustomOrderConversionPreview _preview;
    private readonly Label _summary = new() { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UITheme.SurfaceColor };
    private readonly DataGridView _grid = new();
    private readonly NumericUpDown _cash = MoneyInput();
    private readonly NumericUpDown _transfer = MoneyInput();
    private readonly NumericUpDown _bitcoin = MoneyInput();
    private readonly TextBox _transferReference = new() { Dock = DockStyle.Fill };
    private readonly TextBox _bitcoinReference = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _credit = new() { Text = "Dejar saldo restante a credito", AutoSize = true };
    private readonly NumericUpDown _term = new() { Minimum = 1, Maximum = 365, Value = 7, Width = 100 };
    private readonly Button _convert = new() { Text = "Convertir en venta" };
    private bool _processing;

    public CustomOrderConversionForm(
        CustomOrderConversionPreview preview,
        ICustomOrderFinanceService finance,
        IPosCatalogService products)
    {
        _preview = preview;
        _orderId = preview.Finance.PedidoPersonalizadoId;
        _finance = finance;
        _products = products;
        Text = $"Convertir {_preview.Finance.CodigoPedido} en venta";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1240, 820);
        MinimumSize = new Size(960, 640);
        UITheme.StyleForm(this);
        BuildUi();
        Render();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 6 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var title = new Label { Text = "Convertir pedido en venta", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "Revise pagos, reservas, productos y stock antes de confirmar.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor
        };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(_summary, 0, 2);

        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false;
        _grid.AutoGenerateColumns = false; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; UITheme.StyleDataGridView(_grid);
        foreach (var (property, header, weight) in new[] {
            ("Code","Codigo",65),("Description","Producto / descripcion",160),("Quantity","Cantidad",55),
            ("Price","Precio",60),("Reserved","Reservado",65),("Available","Disponible",65),("Status","Estado",85)})
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = header, FillWeight = weight });
        root.Controls.Add(_grid, 0, 3);

        var payment = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(12), ColumnCount = 4, RowCount = 4 };
        payment.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        payment.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        payment.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        payment.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        AddPayment(payment, "Efectivo ($)", _cash, null, 0);
        AddPayment(payment, "Transferencia ($)", _transfer, _transferReference, 1);
        AddPayment(payment, "Bitcoin ($)", _bitcoin, _bitcoinReference, 2);
        var creditPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        creditPanel.Controls.Add(_credit); creditPanel.Controls.Add(new Label { Text = "Plazo (dias)", AutoSize = true, Margin = new Padding(22, 5, 4, 0) }); creditPanel.Controls.Add(_term);
        payment.Controls.Add(creditPanel, 0, 3); payment.SetColumnSpan(creditPanel, 4);
        _credit.CheckedChanged += (_, _) => _term.Enabled = _credit.Checked;
        _term.Enabled = false;
        root.Controls.Add(payment, 0, 4);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 8, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        var cancel = new Button { Text = "Cancelar", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
        var map = new Button { Text = "Vincular productos pendientes", Dock = DockStyle.Fill };
        var pay = new Button { Text = "Registrar pago previo", Dock = DockStyle.Fill };
        _convert.Dock = DockStyle.Fill;
        UITheme.StyleSecondaryButton(cancel); UITheme.StylePrimaryButton(map);
        UITheme.StylePrimaryButton(pay); UITheme.StyleSuccessButton(_convert);
        map.Click += async (_, _) => await MapAsync();
        pay.Click += async (_, _) => await RegisterPriorPaymentAsync();
        _convert.Click += async (_, _) => await ConvertAsync();
        actions.Controls.Add(cancel); actions.Controls.Add(map); actions.Controls.Add(pay); actions.Controls.Add(_convert);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);
    }

    private void Render()
    {
        var finance = _preview.Finance;
        _summary.Text =
            $"Pedido: {finance.CodigoPedido}    Cliente: {finance.Destinatario}\r\n" +
            $"Total: {Money(finance.TotalCentavos)}    Pagado neto: {Money(finance.PagoNetoCentavos)}    " +
            $"Reembolsado: {Money(finance.TotalReembolsadoCentavos)}    Saldo: {Money(finance.SaldoPendienteCentavos)}";
        _grid.DataSource = _preview.Lines.Select(l => new Row(
            l.CodigoSku ?? l.CodigoProducto ?? "-", l.Descripcion, l.Cantidad, Money(l.PrecioUnitarioCentavos),
            l.StockReservadoPedido, l.StockDisponible,
            !l.ProductoId.HasValue ? "Vincular producto" :
            l.StockDisponible < l.Cantidad ? "Stock insuficiente" : "Listo")).ToList();
        _credit.Enabled = _preview.PuedeCredito;
        if (!_credit.Enabled) _credit.Checked = false;
        _convert.Enabled = _preview.EstadoOperativo == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA &&
            _preview.LineasSinProducto == 0 && _preview.Lines.All(l => l.StockDisponible >= l.Cantidad);
    }

    private async Task RefreshPreviewAsync()
    {
        _preview = await _finance.GetConversionPreviewAsync(_orderId);
        Render();
    }

    private async Task MapAsync()
    {
        using var form = new CustomOrderProductMappingForm(_preview.Lines, _finance, _products);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshPreviewAsync();
    }

    private async Task RegisterPriorPaymentAsync()
    {
        using var form = new CustomOrderPaymentForm(_preview.Finance, _finance);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshPreviewAsync();
    }

    private async Task ConvertAsync()
    {
        if (_processing) return;
        if (MessageBox.Show(
            "La conversion creara una venta, aplicara los pagos anteriores y descontara el inventario correspondiente.\r\n\r\nEsta operacion no debe repetirse.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            _processing = true; _convert.Enabled = false;
            var payments = new List<SalePaymentRequest>();
            AddPayment(payments, MetodoPago.EFECTIVO, _cash, null);
            AddPayment(payments, MetodoPago.TRANSFERENCIA, _transfer, _transferReference.Text);
            AddPayment(payments, MetodoPago.BITCOIN, _bitcoin, _bitcoinReference.Text);
            var result = await _finance.ConvertToSaleAsync(new(
                _orderId, Guid.NewGuid(), payments, _credit.Checked,
                _credit.Checked ? (int)_term.Value : 0));
            MessageBox.Show(
                $"Venta {result.NumeroVenta} creada.\r\nTotal: {Money(result.TotalCentavos)}\r\n" +
                $"Pagos previos: {Money(result.PagosPreviosAplicadosCentavos)}\r\n" +
                $"Pago final: {Money(result.PagoFinalCentavos)}\r\nCredito: {Money(result.CreditoCentavos)}\r\nVuelto: {Money(result.CambioCentavos)}",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _processing = false; Render();
        }
    }

    private static void AddPayment(TableLayoutPanel layout, string label, Control amount, Control? reference, int row)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        layout.Controls.Add(amount, 1, row);
        if (reference is not null)
        {
            layout.Controls.Add(new Label { Text = "Referencia", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, row);
            layout.Controls.Add(reference, 3, row);
        }
    }

    private static void AddPayment(List<SalePaymentRequest> payments, MetodoPago method, NumericUpDown input, string? reference)
    {
        var cents = checked((long)(input.Value * 100m));
        if (cents > 0) payments.Add(new(method, cents, reference));
    }

    private static NumericUpDown MoneyInput() => new() { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 1000000 };
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private sealed record Row(string Code, string Description, int Quantity, string Price, int Reserved, int Available, string Status);
}
