using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class ReturnForm : Form
{
    private readonly SaleCorrectionSearchResult _sale;
    private readonly IReturnService _returnService;
    private readonly BindingSource _linesBinding = new();
    private readonly List<ReturnLineGridRow> _lineRows;

    private readonly DataGridView _grid = new();
    private readonly Label _lblSelectedProduct = new();
    private readonly Label _lblAvailable = new();
    private readonly NumericUpDown _numQuantity = new();
    private readonly ComboBox _cmbCondition = new();
    private readonly CheckBox _chkReintegrateStock = new();
    private readonly TextBox _txtMotivo = new();
    private readonly TextBox _txtObservaciones = new();
    private readonly CheckBox _chkRefund = new();
    private readonly ComboBox _cmbRefundMethod = new();
    private readonly NumericUpDown _numRefundAmount = new();
    private readonly TextBox _txtRefundReason = new();
    private readonly TextBox _txtRefundReference = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    private bool _loadingSelectedLine;
    private bool _isSaving;
    private bool _saved;
    private readonly Guid _operationId = Guid.NewGuid();

    public ReturnForm(SaleCorrectionSearchResult sale, IReturnService returnService)
    {
        _sale = sale;
        _returnService = returnService;
        _lineRows = sale.Lines
            .Where(l => l.CantidadDisponible > 0)
            .Select(ReturnLineGridRow.FromLine)
            .ToList();

        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Registrar devolución";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1020, 760);
        MinimumSize = new Size(940, 700);
        MaximizeBox = true;
        WindowState = FormWindowState.Maximized;
        UITheme.StyleForm(this);

        var lblTitle = new Label
        {
            Text = $"Registrar devolución - {_sale.NumeroVenta}",
            AutoSize = true,
            Location = new Point(24, 20)
        };
        UITheme.StyleLabelTitle(lblTitle);

        var lblHelp = new Label
        {
            Text = "Seleccione un producto y configure la devolución con los controles inferiores.",
            AutoSize = false,
            Location = new Point(24, 64),
            Size = new Size(940, 28)
        };
        UITheme.StyleLabelNormal(lblHelp);

        ConfigureGrid();
        _grid.Location = new Point(24, 102);
        _grid.Size = new Size(960, 245);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _grid.DataSource = _linesBinding;
        _grid.SelectionChanged += (_, _) => LoadSelectedLineControls();
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
        };
        _linesBinding.DataSource = _lineRows;

        var optionsPanel = new Panel
        {
            Location = new Point(24, 365),
            Size = new Size(960, 110),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14)
        };

        var lblOptions = new Label
        {
            Text = "Opciones de devolución",
            Location = new Point(14, 10),
            AutoSize = true
        };
        UITheme.StyleLabelHeader(lblOptions);

        _lblSelectedProduct.Location = new Point(14, 42);
        _lblSelectedProduct.Size = new Size(280, 24);
        UITheme.StyleLabelNormal(_lblSelectedProduct);

        _lblAvailable.Location = new Point(14, 68);
        _lblAvailable.Size = new Size(280, 24);
        UITheme.StyleLabelNormal(_lblAvailable);

        AddPanelLabel(optionsPanel, "Cantidad a devolver", 360, 18);
        _numQuantity.Location = new Point(360, 46);
        _numQuantity.Size = new Size(110, 30);
        _numQuantity.Minimum = 0;
        _numQuantity.Maximum = 0;
        _numQuantity.ValueChanged += (_, _) => ApplySelectedLineControls();

        AddPanelLabel(optionsPanel, "Condición", 520, 18);
        _cmbCondition.Location = new Point(520, 46);
        _cmbCondition.Size = new Size(160, 30);
        _cmbCondition.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbCondition.DataSource = new[]
        {
            CondicionProductoDevuelto.BUENO,
            CondicionProductoDevuelto.DANADO,
            CondicionProductoDevuelto.NO_APTO,
            CondicionProductoDevuelto.FALTANTE
        };
        _cmbCondition.SelectedIndexChanged += (_, _) => ApplySelectedLineControls();

        _chkReintegrateStock.Text = "Reintegrar al inventario";
        _chkReintegrateStock.Location = new Point(730, 48);
        _chkReintegrateStock.AutoSize = true;
        _chkReintegrateStock.CheckedChanged += (_, _) => ApplySelectedLineControls();

        optionsPanel.Controls.AddRange([
            lblOptions,
            _lblSelectedProduct,
            _lblAvailable,
            _numQuantity,
            _cmbCondition,
            _chkReintegrateStock
        ]);

        AddLabel("Motivo devolución", 24, 480);
        _txtMotivo.Location = new Point(24, 504);
        _txtMotivo.Size = new Size(470, 28);
        UITheme.StyleTextBox(_txtMotivo);

        AddLabel("Observaciones", 520, 480);
        _txtObservaciones.Location = new Point(520, 504);
        _txtObservaciones.Size = new Size(464, 28);
        UITheme.StyleTextBox(_txtObservaciones);

        _chkRefund.Text = "Registrar reembolso";
        _chkRefund.Location = new Point(24, 558);
        _chkRefund.AutoSize = true;
        _chkRefund.CheckedChanged += (_, _) => ToggleRefundControls();

        AddLabel("Metodo", 24, 592);
        _cmbRefundMethod.Location = new Point(24, 616);
        _cmbRefundMethod.Size = new Size(150, 28);
        _cmbRefundMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRefundMethod.DataSource = new[] { MetodoReembolso.EFECTIVO, MetodoReembolso.TRANSFERENCIA, MetodoReembolso.BITCOIN };
        _cmbRefundMethod.Font = UITheme.NormalFont;

        AddLabel("Monto ($)", 190, 592);
        ConfigureMoney(_numRefundAmount, 190, 616);

        AddLabel("Motivo reembolso", 360, 592);
        _txtRefundReason.Location = new Point(360, 616);
        _txtRefundReason.Size = new Size(250, 28);
        UITheme.StyleTextBox(_txtRefundReason);

        AddLabel("Referencia", 630, 592);
        _txtRefundReference.Location = new Point(630, 616);
        _txtRefundReference.Size = new Size(354, 28);
        UITheme.StyleTextBox(_txtRefundReference);

        _btnSave.Text = "Confirmar devolución";
        _btnSave.Location = new Point(650, 690);
        _btnSave.Size = new Size(205, 38);
        _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnSave.Click += BtnSave_Click;
        UITheme.StylePrimaryButton(_btnSave);

        _btnCancel.Text = "Cancelar";
        _btnCancel.Location = new Point(870, 690);
        _btnCancel.Size = new Size(120, 38);
        _btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnCancel.CausesValidation = false;
        _btnCancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        UITheme.StyleSecondaryButton(_btnCancel);

        Controls.AddRange([
            lblTitle,
            lblHelp,
            _grid,
            optionsPanel,
            _txtMotivo,
            _txtObservaciones,
            _chkRefund,
            _cmbRefundMethod,
            _numRefundAmount,
            _txtRefundReason,
            _txtRefundReference,
            _btnSave,
            _btnCancel
        ]);

        ToggleRefundControls();
        if (_lineRows.Count > 0)
        {
            _linesBinding.Position = 0;
            LoadSelectedLineControls();
        }
        else
        {
            _lblSelectedProduct.Text = "No hay lineas disponibles para devolver.";
            _lblAvailable.Text = string.Empty;
            SetLineControlsEnabled(false);
        }
    }

    private void LoadSelectedLineControls()
    {
        if (GetSelectedLine() is not { } row)
        {
            SetLineControlsEnabled(false);
            return;
        }

        _loadingSelectedLine = true;
        try
        {
            SetLineControlsEnabled(true);
            _lblSelectedProduct.Text = $"Producto: {row.Producto}";
            _lblAvailable.Text = $"Disponible: {row.CantidadDisponible}";
            _numQuantity.Maximum = row.CantidadDisponible;
            _numQuantity.Value = Math.Clamp(row.CantidadDevolver, 0, row.CantidadDisponible);
            _cmbCondition.SelectedItem = row.Condicion;
            _chkReintegrateStock.Checked = row.ReintegraStock;
            ApplyConditionRules(row.Condicion);
        }
        finally
        {
            _loadingSelectedLine = false;
        }
    }

    private void ApplySelectedLineControls()
    {
        if (_loadingSelectedLine || GetSelectedLine() is not { } row)
        {
            return;
        }

        var condition = _cmbCondition.SelectedItem is CondicionProductoDevuelto selected
            ? selected
            : CondicionProductoDevuelto.BUENO;

        ApplyConditionRules(condition);

        row.CantidadDevolver = (int)_numQuantity.Value;
        row.Condicion = condition;
        row.ReintegraStock = condition != CondicionProductoDevuelto.BUENO
            ? false
            : _chkReintegrateStock.Checked;

        _linesBinding.ResetBindings(false);
    }

    private ReturnLineGridRow? GetSelectedLine()
    {
        if (_grid.CurrentRow?.DataBoundItem is ReturnLineGridRow gridRow)
        {
            return gridRow;
        }

        return _linesBinding.Current as ReturnLineGridRow;
    }

    private void ApplyConditionRules(CondicionProductoDevuelto condition)
    {
        if (condition != CondicionProductoDevuelto.BUENO)
        {
            _chkReintegrateStock.Checked = false;
            _chkReintegrateStock.Enabled = false;
            return;
        }

        _chkReintegrateStock.Enabled = true;
    }

    private void SetLineControlsEnabled(bool enabled)
    {
        _numQuantity.Enabled = enabled;
        _cmbCondition.Enabled = enabled;
        _chkReintegrateStock.Enabled = enabled;
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_isSaving || _saved)
        {
            return;
        }

        try
        {
            _isSaving = true;
            _btnSave.Enabled = false;
            _btnCancel.Enabled = false;

            var selectedLines = BuildSelectedLines();

            RegisterRefundRequest? refund = null;
            if (_chkRefund.Checked)
            {
                refund = new RegisterRefundRequest(
                    (MetodoReembolso)_cmbRefundMethod.SelectedItem!,
                    DollarsToCentavos(_numRefundAmount.Value),
                    _txtRefundReason.Text,
                    _txtRefundReference.Text);
            }

            var result = await _returnService.RegisterReturnAsync(new RegisterReturnRequest(
                _sale.VentaId,
                _txtMotivo.Text,
                selectedLines,
                refund,
                _txtObservaciones.Text,
                _operationId));

            _saved = true;
            MessageBox.Show(
                $"Devolución {result.NumeroDevolucion} registrada.\nTotal devuelto: {FormatDollars(result.TotalDevueltoCentavos)}\nReembolso: {FormatDollars(result.ReembolsoCentavos)}",
                "Devoluciones",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _btnSave.Enabled = true;
            _btnCancel.Enabled = true;
            _isSaving = false;
        }
    }

    private IReadOnlyList<RegisterReturnLineRequest> BuildSelectedLines()
    {
        if (string.IsNullOrWhiteSpace(_txtMotivo.Text))
        {
            throw new InvalidOperationException("El motivo de la devolución es obligatorio.");
        }

        var selectedLines = new List<RegisterReturnLineRequest>();
        foreach (var line in _lineRows.Where(l => l.CantidadDevolver > 0))
        {
            if (line.CantidadDevolver > line.CantidadDisponible)
            {
                throw new InvalidOperationException("No puede devolver mas unidades que las disponibles.");
            }

            if (line.Condicion != CondicionProductoDevuelto.BUENO && line.ReintegraStock)
            {
                throw new InvalidOperationException("Un producto dañado no puede reintegrarse al stock vendible.");
            }

            selectedLines.Add(new RegisterReturnLineRequest(
                line.DetalleVentaId,
                line.CantidadDevolver,
                line.Condicion,
                line.ReintegraStock));
        }

        if (selectedLines.Count == 0)
        {
            throw new InvalidOperationException("Debe seleccionar al menos una linea para devolver.");
        }

        return selectedLines;
    }

    private void ToggleRefundControls()
    {
        var enabled = _chkRefund.Checked;
        _cmbRefundMethod.Enabled = enabled;
        _numRefundAmount.Enabled = enabled;
        _txtRefundReason.Enabled = enabled;
        _txtRefundReference.Enabled = enabled;
    }

    private void ConfigureGrid()
    {
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UITheme.StyleDataGridView(_grid);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.ColumnHeadersHeight = 50;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        AddTextColumn("Producto", "Producto", 180);
        AddTextColumn("CodigoInterno", "Codigo", 85);
        AddTextColumn("CantidadVendida", "Vendida", 55);
        AddTextColumn("CantidadDevuelta", "Devuelta", 65);
        AddTextColumn("CantidadDisponible", "Disponible", 65);
        AddTextColumn("CantidadDevolver", "Cantidad a devolver", 105);
        AddTextColumn("Condicion", "Condicion", 85);
        AddTextColumn("ReintegraStockTexto", "Reintegra stock", 90);
        AddTextColumn("PrecioUnitario", "Precio de venta", 95);
        AddTextColumn("TotalLinea", "Total linea", 100);
    }

    private void AddTextColumn(string propertyName, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            Width = width,
            MinimumWidth = Math.Min(width, 48),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = width,
            ReadOnly = true
        });
    }

    private void AddLabel(string text, int x, int y)
    {
        var label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
        UITheme.StyleLabelNormal(label);
        Controls.Add(label);
    }

    private static void AddPanelLabel(Control parent, string text, int x, int y)
    {
        var label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
        UITheme.StyleLabelNormal(label);
        parent.Controls.Add(label);
    }

    private static void ConfigureMoney(NumericUpDown control, int x, int y)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(140, 28);
        control.Minimum = 0;
        control.Maximum = 9_999_999.99m;
        control.DecimalPlaces = 2;
        control.ThousandsSeparator = true;
        control.Increment = 0.25m;
    }

    private static long DollarsToCentavos(decimal dollars)
    {
        return decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string FormatDollars(long centavos) => (centavos / 100m).ToString("0.00");

    private sealed class ReturnLineGridRow
    {
        public int DetalleVentaId { get; init; }
        public string Producto { get; init; } = string.Empty;
        public string CodigoInterno { get; init; } = string.Empty;
        public int CantidadVendida { get; init; }
        public int CantidadDevuelta { get; init; }
        public int CantidadDisponible { get; init; }
        public int CantidadDevolver { get; set; }
        public CondicionProductoDevuelto Condicion { get; set; } = CondicionProductoDevuelto.BUENO;
        public bool ReintegraStock { get; set; } = true;
        public string ReintegraStockTexto => ReintegraStock ? "Si" : "No";
        public string PrecioUnitario { get; init; } = string.Empty;
        public string TotalLinea { get; init; } = string.Empty;

        public static ReturnLineGridRow FromLine(SaleCorrectionLine line)
        {
            return new ReturnLineGridRow
            {
                DetalleVentaId = line.DetalleVentaId,
                Producto = line.Producto,
                CodigoInterno = line.CodigoInterno,
                CantidadVendida = line.CantidadVendida,
                CantidadDevuelta = line.CantidadDevuelta,
                CantidadDisponible = line.CantidadDisponible,
                CantidadDevolver = 0,
                PrecioUnitario = FormatDollars(line.PrecioUnitarioCentavos),
                TotalLinea = FormatDollars(line.TotalLineaCentavos)
            };
        }
    }
}
