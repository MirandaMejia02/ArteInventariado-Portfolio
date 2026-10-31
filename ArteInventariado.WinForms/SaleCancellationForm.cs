using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SaleCancellationForm : Form
{
    private readonly SaleCorrectionSearchResult _sale;
    private readonly ISaleCancellationService _saleCancellationService;
    private readonly BindingSource _linesBinding = new();

    private readonly DataGridView _gridLines = new();
    private readonly ComboBox _cmbCondition = new();
    private readonly CheckBox _chkReintegrate = new();
    private readonly CheckBox _chkRefund = new();
    private readonly CheckBox _chkNoRealOperation = new();
    private readonly Label _lblNoRealOperationNote = new();
    private readonly ComboBox _cmbRefundMethod = new();
    private readonly NumericUpDown _numRefundAmount = new();
    private readonly TextBox _txtRefundReference = new();
    private readonly TextBox _txtMotivo = new();
    private readonly TextBox _txtObservaciones = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    private bool _saving;
    private readonly Guid _operationId = Guid.NewGuid();

    public SaleCancellationForm(
        SaleCorrectionSearchResult sale,
        ISaleCancellationService saleCancellationService)
    {
        _sale = sale;
        _saleCancellationService = saleCancellationService;

        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Anular venta";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 700);
        MinimumSize = new Size(760, 660);
        UITheme.StyleForm(this);

        var title = new Label
        {
            Text = $"Anular venta - {_sale.NumeroVenta}",
            AutoSize = true,
            Location = new Point(24, 20)
        };
        UITheme.StyleLabelTitle(title);

        var help = new Label
        {
            Text = "La anulacion procesa todo el remanente disponible. Para corregir una parte use devolucion o cambio.",
            Location = new Point(24, 64),
            Size = new Size(760, 34)
        };
        UITheme.StyleLabelNormal(help);

        ConfigureGrid();
        _gridLines.Location = new Point(24, 112);
        _gridLines.Size = new Size(760, 180);
        _gridLines.DataSource = _linesBinding;
        _linesBinding.DataSource = _sale.Lines
            .Where(l => l.CantidadDisponible > 0)
            .Select(CancellationLineRow.FromLine)
            .ToList();

        AddLabel("Condicion para remanente", 24, 316);
        _cmbCondition.Location = new Point(24, 342);
        _cmbCondition.Size = new Size(180, 30);
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
        _chkReintegrate.Location = new Point(230, 346);
        _chkReintegrate.AutoSize = true;
        _chkReintegrate.Checked = true;

        _chkNoRealOperation.Text = "Error de registro: no hubo venta, cobro, pago ni entrega real";
        _chkNoRealOperation.Location = new Point(24, 394);
        _chkNoRealOperation.AutoSize = true;
        _chkNoRealOperation.CheckedChanged += (_, _) => ApplyNoRealOperationRules();

        _lblNoRealOperationNote.Text = "Al confirmarlo, se excluye el pago original de caja y reportes; el stock vuelve a Exhibición. No crea reembolso.";
        _lblNoRealOperationNote.Location = new Point(24, 422);
        _lblNoRealOperationNote.Size = new Size(760, 32);
        UITheme.StyleLabelNormal(_lblNoRealOperationNote);

        _chkRefund.Text = "Registrar reembolso";
        _chkRefund.Location = new Point(24, 458);
        _chkRefund.AutoSize = true;
        _chkRefund.CheckedChanged += (_, _) => ToggleRefundControls();

        AddLabel("Metodo", 24, 486);
        _cmbRefundMethod.Location = new Point(24, 512);
        _cmbRefundMethod.Size = new Size(160, 30);
        _cmbRefundMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRefundMethod.DataSource = new[] { MetodoReembolso.EFECTIVO, MetodoReembolso.TRANSFERENCIA, MetodoReembolso.BITCOIN };
        _cmbRefundMethod.SelectedItem = GetDefaultRefundMethod();

        AddLabel("Monto ($)", 204, 486);
        ConfigureMoney(_numRefundAmount, 204, 512);

        AddLabel("Referencia", 374, 486);
        _txtRefundReference.Location = new Point(374, 512);
        _txtRefundReference.Size = new Size(300, 30);
        UITheme.StyleTextBox(_txtRefundReference);

        AddLabel("Motivo", 24, 554);
        _txtMotivo.Location = new Point(24, 580);
        _txtMotivo.Size = new Size(320, 30);
        UITheme.StyleTextBox(_txtMotivo);

        AddLabel("Observaciones", 374, 554);
        _txtObservaciones.Location = new Point(374, 580);
        _txtObservaciones.Size = new Size(300, 30);
        UITheme.StyleTextBox(_txtObservaciones);

        _btnSave.Text = "Guardar";
        _btnSave.Location = new Point(560, 642);
        _btnSave.Size = new Size(100, 34);
        _btnSave.Click += BtnSave_Click;
        UITheme.StyleDangerButton(_btnSave);

        _btnCancel.Text = "Cancelar";
        _btnCancel.Location = new Point(680, 642);
        _btnCancel.Size = new Size(100, 34);
        _btnCancel.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(_btnCancel);

        Controls.AddRange([
            title,
            help,
            _gridLines,
            _cmbCondition,
            _chkReintegrate,
            _chkRefund,
            _chkNoRealOperation,
            _lblNoRealOperationNote,
            _cmbRefundMethod,
            _numRefundAmount,
            _txtRefundReference,
            _txtMotivo,
            _txtObservaciones,
            _btnSave,
            _btnCancel
        ]);

        _numRefundAmount.Value = CentavosToDollars(_sale.Lines.Where(l => l.CantidadDisponible > 0).Sum(l => Proportional(l.TotalLineaCentavos, l.CantidadVendida, l.CantidadDisponible)));
        ToggleRefundControls();
    }

    private void ApplyConditionRules()
    {
        var condition = _cmbCondition.SelectedItem is CondicionProductoDevuelto selected
            ? selected
            : CondicionProductoDevuelto.BUENO;

        if (condition == CondicionProductoDevuelto.BUENO)
        {
            _chkReintegrate.Enabled = true;
            _chkReintegrate.Checked = true;
            return;
        }

        _chkReintegrate.Checked = false;
        _chkReintegrate.Enabled = false;
    }

    private void ToggleRefundControls()
    {
        var enabled = _chkRefund.Checked;
        _cmbRefundMethod.Enabled = enabled;
        _numRefundAmount.Enabled = enabled;
        _txtRefundReference.Enabled = enabled;
    }

    private void ApplyNoRealOperationRules()
    {
        if (_chkNoRealOperation.Checked)
        {
            _chkRefund.Checked = false;
            _cmbCondition.SelectedItem = CondicionProductoDevuelto.BUENO;
            _chkReintegrate.Checked = true;
            _cmbCondition.Enabled = false;
            _chkReintegrate.Enabled = false;
        }
        else
        {
            _cmbCondition.Enabled = true;
            ApplyConditionRules();
        }

        _chkRefund.Enabled = !_chkNoRealOperation.Checked;
        ToggleRefundControls();
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

            var condition = (CondicionProductoDevuelto)_cmbCondition.SelectedItem!;
            var lines = _sale.Lines
                .Where(l => l.CantidadDisponible > 0)
                .Select(l => new RegisterSaleCancellationLineRequest(
                    l.DetalleVentaId,
                    l.CantidadDisponible,
                    condition,
                    _chkReintegrate.Checked))
                .ToList();

            if (_chkNoRealOperation.Checked && MessageBox.Show(
                    "Confirma que no hubo una venta, cobro, pago ni entrega real. Se excluirán los pagos originales de caja y reportes, y los productos volverán a Exhibición. Esta acción quedará auditada y no registra reembolso.",
                    "Confirmar error de registro",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                _btnSave.Enabled = true;
                _saving = false;
                return;
            }

            RegisterRefundRequest? refund = null;
            if (_chkRefund.Checked)
            {
                refund = new RegisterRefundRequest(
                    (MetodoReembolso)_cmbRefundMethod.SelectedItem!,
                    DollarsToCentavos(_numRefundAmount.Value),
                    _txtMotivo.Text,
                    _txtRefundReference.Text);
            }

            await _saleCancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
                _sale.VentaId,
                _txtMotivo.Text,
                lines,
                refund,
                _txtObservaciones.Text,
                _operationId,
                _chkNoRealOperation.Checked));

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Anulacion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _btnSave.Enabled = true;
            _saving = false;
        }
    }

    private void ConfigureGrid()
    {
        _gridLines.AllowUserToAddRows = false;
        _gridLines.AllowUserToDeleteRows = false;
        _gridLines.AutoGenerateColumns = false;
        _gridLines.ReadOnly = true;
        _gridLines.RowHeadersVisible = false;
        _gridLines.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UITheme.StyleDataGridView(_gridLines);

        AddGridColumn("Producto", "Producto", 260);
        AddGridColumn("Codigo", "Codigo", 120);
        AddGridColumn("CantidadVendida", "Vendida", 90);
        AddGridColumn("CantidadDisponible", "Remanente", 100);
        AddGridColumn("Total", "Total", 100);
    }

    private void AddGridColumn(string propertyName, string header, int width)
    {
        _gridLines.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            Width = width
        });
    }

    private void AddLabel(string text, int x, int y)
    {
        var label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
        UITheme.StyleLabelNormal(label);
        Controls.Add(label);
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

    private static decimal CentavosToDollars(long centavos) => centavos / 100m;

    private static long DollarsToCentavos(decimal dollars)
    {
        return decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static long Proportional(long amount, int originalQuantity, int quantity)
    {
        return decimal.ToInt64(decimal.Round(amount * quantity / (decimal)originalQuantity, 0, MidpointRounding.AwayFromZero));
    }

    private static string FormatDollars(long centavos) => (centavos / 100m).ToString("0.00");

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

    private sealed record CancellationLineRow(
        string Producto,
        string Codigo,
        int CantidadVendida,
        int CantidadDisponible,
        string Total)
    {
        public static CancellationLineRow FromLine(SaleCorrectionLine line)
        {
            var total = Proportional(line.TotalLineaCentavos, line.CantidadVendida, line.CantidadDisponible);
            return new CancellationLineRow(
                line.Producto,
                line.CodigoInterno,
                line.CantidadVendida,
                line.CantidadDisponible,
                FormatDollars(total));
        }
    }
}
