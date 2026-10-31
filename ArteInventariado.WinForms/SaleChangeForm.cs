using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SaleChangeForm : Form
{
    private readonly SaleCorrectionSearchResult _sale;
    private readonly IPosCatalogService _productService;
    private readonly ISaleChangeService _saleChangeService;

    private readonly ComboBox _cmbLine = new();
    private readonly NumericUpDown _numQuantity = new();
    private readonly ComboBox _cmbNewProduct = new();
    private readonly ComboBox _cmbCondition = new();
    private readonly CheckBox _chkReintegrate = new();
    private readonly ComboBox _cmbPaymentMethod = new();
    private readonly Label _lblPaymentMethod = new();
    private readonly Label _lblAmount = new();
    private readonly Label _lblReference = new();
    private readonly TextBox _txtReference = new();
    private readonly NumericUpDown _numAmount = new();
    private readonly TextBox _txtMotivo = new();
    private readonly TextBox _txtObservaciones = new();
    private readonly Label _lblDifference = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    private IReadOnlyList<ProductOption> _products = [];
    private bool _saving;
    private MoneyDifferenceDirection _moneyDifferenceDirection;
    private readonly Guid _operationId = Guid.NewGuid();

    public SaleChangeForm(
        SaleCorrectionSearchResult sale,
        IPosCatalogService productService,
        ISaleChangeService saleChangeService)
    {
        _sale = sale;
        _productService = productService;
        _saleChangeService = saleChangeService;

        BuildUi();
        Load += async (_, _) => await LoadProductsAsync();
    }

    private void BuildUi()
    {
        Text = "Registrar cambio";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(840, 540);
        MinimumSize = new Size(820, 520);
        UITheme.StyleForm(this);

        var title = new Label
        {
            Text = $"Cambio de producto - {_sale.NumeroVenta}",
            AutoSize = true,
            Location = new Point(24, 20)
        };
        UITheme.StyleLabelTitle(title);

        AddLabel("Producto vendido", 24, 78);
        _cmbLine.Location = new Point(24, 104);
        _cmbLine.Size = new Size(500, 30);
        _cmbLine.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbLine.DataSource = _sale.Lines
            .Where(l => l.CantidadDisponible > 0)
            .Select(LineOption.FromLine)
            .ToList();
        _cmbLine.SelectedIndexChanged += (_, _) => SyncSelectedLine();

        AddLabel("Cantidad", 548, 78);
        ConfigureInteger(_numQuantity, 548, 104);
        _numQuantity.ValueChanged += (_, _) => RecalculateDifference();

        AddLabel("Producto nuevo", 24, 154);
        _cmbNewProduct.Location = new Point(24, 180);
        _cmbNewProduct.Size = new Size(500, 30);
        _cmbNewProduct.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbNewProduct.SelectedIndexChanged += (_, _) => RecalculateDifference();

        AddLabel("Condicion devuelta", 548, 154);
        _cmbCondition.Location = new Point(548, 180);
        _cmbCondition.Size = new Size(160, 30);
        _cmbCondition.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbCondition.DataSource = new[]
        {
            CondicionProductoDevuelto.BUENO,
            CondicionProductoDevuelto.DANADO,
            CondicionProductoDevuelto.NO_APTO,
            CondicionProductoDevuelto.FALTANTE
        };
        _cmbCondition.SelectedIndexChanged += (_, _) => ApplyConditionRules();

        _chkReintegrate.Text = "Reintegrar stock vendible";
        _chkReintegrate.Location = new Point(24, 226);
        _chkReintegrate.AutoSize = true;
        _chkReintegrate.Checked = true;

        _lblDifference.Location = new Point(24, 260);
        _lblDifference.Size = new Size(690, 30);
        UITheme.StyleLabelHeader(_lblDifference);

        ConfigureLabel(_lblPaymentMethod, "Método cobro/reembolso", 24, 308);
        _cmbPaymentMethod.Location = new Point(24, 334);
        _cmbPaymentMethod.Size = new Size(170, 30);
        _cmbPaymentMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbPaymentMethod.FormattingEnabled = true;
        _cmbPaymentMethod.Format += (_, e) => e.Value = FormatPaymentMethod(e.ListItem);

        ConfigureLabel(_lblAmount, "Monto ($)", 214, 308);
        ConfigureMoney(_numAmount, 214, 334);

        ConfigureLabel(_lblReference, "Referencia", 384, 308);
        _txtReference.Location = new Point(384, 334);
        _txtReference.Size = new Size(330, 30);
        UITheme.StyleTextBox(_txtReference);

        AddLabel("Motivo", 24, 386);
        _txtMotivo.Location = new Point(24, 412);
        _txtMotivo.Size = new Size(330, 30);
        UITheme.StyleTextBox(_txtMotivo);

        AddLabel("Observaciones", 384, 386);
        _txtObservaciones.Location = new Point(384, 412);
        _txtObservaciones.Size = new Size(330, 30);
        UITheme.StyleTextBox(_txtObservaciones);

        _btnSave.Text = "Confirmar cambio de producto";
        _btnSave.Location = new Point(394, 482);
        _btnSave.Size = new Size(300, 38);
        _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnSave.Click += BtnSave_Click;
        UITheme.StylePrimaryButton(_btnSave);

        _btnCancel.Text = "Cancelar";
        _btnCancel.Location = new Point(706, 482);
        _btnCancel.Size = new Size(110, 38);
        _btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnCancel.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(_btnCancel);

        Controls.AddRange([
            title,
            _cmbLine,
            _numQuantity,
            _cmbNewProduct,
            _cmbCondition,
            _chkReintegrate,
            _lblDifference,
            _lblPaymentMethod,
            _cmbPaymentMethod,
            _lblAmount,
            _numAmount,
            _lblReference,
            _txtReference,
            _txtMotivo,
            _txtObservaciones,
            _btnSave,
            _btnCancel
        ]);

        SyncSelectedLine();
        ApplyConditionRules();
    }

    private async Task LoadProductsAsync()
    {
        var products = await _productService.SearchAsync(new PosSkuCatalogFilter(
            Estado: EstadoProducto.ACTIVO, SkuActivo: true));
        _products = products.Select(ProductOption.FromProduct).ToList();
        _cmbNewProduct.DataSource = _products;
        RecalculateDifference();
    }

    private void SyncSelectedLine()
    {
        if (_cmbLine.SelectedItem is not LineOption line)
        {
            _numQuantity.Maximum = 0;
            _numQuantity.Value = 0;
            return;
        }

        _numQuantity.Maximum = line.CantidadDisponible;
        _numQuantity.Value = line.CantidadDisponible > 0 ? 1 : 0;
        RecalculateDifference();
    }

    private void ApplyConditionRules()
    {
        var condition = _cmbCondition.SelectedItem is CondicionProductoDevuelto selected
            ? selected
            : CondicionProductoDevuelto.BUENO;

        if (condition == CondicionProductoDevuelto.BUENO)
        {
            _chkReintegrate.Enabled = true;
            if (!_chkReintegrate.Checked)
            {
                _chkReintegrate.Checked = true;
            }
            return;
        }

        _chkReintegrate.Checked = false;
        _chkReintegrate.Enabled = false;
    }

    private void RecalculateDifference()
    {
        if (_cmbLine.SelectedItem is not LineOption line || _cmbNewProduct.SelectedItem is not ProductOption product)
        {
            _lblDifference.Text = "Seleccione producto vendido y producto nuevo.";
            ConfigurePaymentControls(MoneyDifferenceDirection.None);
            _numAmount.Value = 0;
            return;
        }

        var quantity = (int)_numQuantity.Value;
        var returnedValue = Proportional(line.TotalLineaCentavos, line.CantidadVendida, quantity);
        var newValue = product.PrecioVentaCentavos * quantity;
        var difference = newValue - returnedValue;

        if (difference > 0)
        {
            _lblDifference.Text = $"Diferencia a cobrar: {FormatDollars(difference)}";
            ConfigurePaymentControls(MoneyDifferenceDirection.Charge);
            _numAmount.Value = CentavosToDollars(difference);
            return;
        }

        if (difference < 0)
        {
            _lblDifference.Text = $"Diferencia a devolver: {FormatDollars(-difference)}";
            ConfigurePaymentControls(MoneyDifferenceDirection.Refund);
            _numAmount.Value = CentavosToDollars(-difference);
            return;
        }

        _lblDifference.Text = "Sin diferencia de dinero.";
        ConfigurePaymentControls(MoneyDifferenceDirection.None);
        _numAmount.Value = 0;
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_saving)
        {
            return;
        }

        try
        {
            _saving = true;
            _btnSave.Enabled = false;

            if (_cmbLine.SelectedItem is not LineOption line || _cmbNewProduct.SelectedItem is not ProductOption product)
            {
                throw new InvalidOperationException("Seleccione la linea vendida y el producto nuevo.");
            }

            var quantity = (int)_numQuantity.Value;
            var returnedValue = Proportional(line.TotalLineaCentavos, line.CantidadVendida, quantity);
            var newValue = product.PrecioVentaCentavos * quantity;
            var difference = newValue - returnedValue;
            var amount = DollarsToCentavos(_numAmount.Value);

            RegisterCorrectionPaymentRequest? payment = null;
            RegisterRefundRequest? refund = null;
            if (difference > 0)
            {
                if (_cmbPaymentMethod.SelectedItem is not MetodoPago paymentMethod)
                {
                    throw new InvalidOperationException("Seleccione el método para cobrar la diferencia.");
                }

                payment = new RegisterCorrectionPaymentRequest(
                    paymentMethod,
                    amount,
                    _txtMotivo.Text,
                    _txtReference.Text);
            }
            else if (difference < 0)
            {
                if (_cmbPaymentMethod.SelectedItem is not MetodoReembolso refundMethod)
                {
                    throw new InvalidOperationException("Seleccione el método para devolver la diferencia.");
                }

                refund = new RegisterRefundRequest(
                    refundMethod,
                    amount,
                    _txtMotivo.Text,
                    _txtReference.Text);
            }

            await _saleChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
                _sale.VentaId,
                line.DetalleVentaId,
                quantity,
                product.ProductoId,
                (CondicionProductoDevuelto)_cmbCondition.SelectedItem!,
                _chkReintegrate.Checked,
                _txtMotivo.Text,
                payment,
                refund,
                _txtObservaciones.Text,
                ProductoNuevoSkuId: product.ProductoSkuId,
                OperacionId: _operationId));

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cambio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _btnSave.Enabled = true;
            _saving = false;
        }
    }

    private void AddLabel(string text, int x, int y)
    {
        var label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
        UITheme.StyleLabelNormal(label);
        Controls.Add(label);
    }

    private static void ConfigureLabel(Label label, string text, int x, int y)
    {
        label.Text = text;
        label.AutoSize = true;
        label.Location = new Point(x, y);
        UITheme.StyleLabelNormal(label);
    }

    private void ConfigurePaymentControls(MoneyDifferenceDirection direction)
    {
        var directionChanged = _moneyDifferenceDirection != direction;
        _moneyDifferenceDirection = direction;
        var visible = direction != MoneyDifferenceDirection.None;
        _lblPaymentMethod.Visible = visible;
        _cmbPaymentMethod.Visible = visible;
        _lblAmount.Visible = visible;
        _numAmount.Visible = visible;
        _lblReference.Visible = visible;
        _txtReference.Visible = visible;

        if (direction == MoneyDifferenceDirection.None)
        {
            _cmbPaymentMethod.DataSource = null;
            _txtReference.Clear();
            return;
        }

        var hasExpectedSelection = direction switch
        {
            MoneyDifferenceDirection.Charge => _cmbPaymentMethod.SelectedItem is MetodoPago,
            MoneyDifferenceDirection.Refund => _cmbPaymentMethod.SelectedItem is MetodoReembolso,
            _ => false
        };
        if (!directionChanged && hasExpectedSelection)
        {
            return;
        }

        _txtReference.Clear();
        if (direction == MoneyDifferenceDirection.Charge)
        {
            _cmbPaymentMethod.DataSource = new[]
            {
                MetodoPago.EFECTIVO,
                MetodoPago.TRANSFERENCIA,
                MetodoPago.BITCOIN
            };
            _cmbPaymentMethod.SelectedItem = MetodoPago.EFECTIVO;
            return;
        }

        _cmbPaymentMethod.DataSource = new[]
        {
            MetodoReembolso.EFECTIVO,
            MetodoReembolso.TRANSFERENCIA,
            MetodoReembolso.BITCOIN
        };
        _cmbPaymentMethod.SelectedItem = GetDefaultRefundMethod();
    }

    private static string FormatPaymentMethod(object? item) => item?.ToString() switch
    {
        "EFECTIVO" => "Efectivo",
        "TRANSFERENCIA" => "Transferencia",
        "BITCOIN" => "Bitcoin",
        _ => item?.ToString() ?? string.Empty
    };

    private static void ConfigureInteger(NumericUpDown control, int x, int y)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(120, 30);
        control.Minimum = 1;
        control.Maximum = 1;
    }

    private static void ConfigureMoney(NumericUpDown control, int x, int y)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(140, 30);
        control.Minimum = 0;
        control.Maximum = 9_999_999.99m;
        control.DecimalPlaces = 2;
        control.ThousandsSeparator = true;
        control.Increment = 0.25m;
    }

    private static long Proportional(long amount, int originalQuantity, int quantity)
    {
        return decimal.ToInt64(decimal.Round(amount * quantity / (decimal)originalQuantity, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal CentavosToDollars(long centavos) => centavos / 100m;

    private static long DollarsToCentavos(decimal dollars)
    {
        return decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string FormatDollars(long centavos) => (centavos / 100m).ToString("0.00");

    private enum MoneyDifferenceDirection
    {
        None,
        Charge,
        Refund
    }

    private MetodoReembolso GetDefaultRefundMethod()
    {
        var distinctMethods = _sale.Payments
            .Select(p => p.MetodoPago)
            .Distinct()
            .ToList();

        if (distinctMethods.Count != 1)
        {
            return MetodoReembolso.EFECTIVO;
        }

        return distinctMethods[0] switch
        {
            MetodoPago.TRANSFERENCIA => MetodoReembolso.TRANSFERENCIA,
            MetodoPago.BITCOIN => MetodoReembolso.BITCOIN,
            _ => MetodoReembolso.EFECTIVO
        };
    }

    private sealed record LineOption(
        int DetalleVentaId,
        string Display,
        int CantidadVendida,
        int CantidadDisponible,
        long TotalLineaCentavos)
    {
        public override string ToString() => Display;

        public static LineOption FromLine(SaleCorrectionLine line)
        {
            return new LineOption(
                line.DetalleVentaId,
                $"{line.Producto} | Disponible: {line.CantidadDisponible} | Total linea: {FormatDollars(line.TotalLineaCentavos)}",
                line.CantidadVendida,
                line.CantidadDisponible,
                line.TotalLineaCentavos);
        }
    }

    private sealed record ProductOption(
        int ProductoId,
        int ProductoSkuId,
        string Display,
        long PrecioVentaCentavos)
    {
        public override string ToString() => Display;

        public static ProductOption FromProduct(PosSkuSaleItem product)
        {
            return new ProductOption(
                product.ProductoId,
                product.ProductoSkuId,
                $"{product.CodigoSku} - {product.ProductoNombre}{(string.IsNullOrWhiteSpace(product.VarianteNombre) ? string.Empty : $" / {product.VarianteNombre}")} | Disponible: {product.StockDisponibleTotal} | Precio: {FormatDollars(product.PrecioVentaEfectivoCentavos)}",
                product.PrecioVentaEfectivoCentavos);
        }
    }
}
