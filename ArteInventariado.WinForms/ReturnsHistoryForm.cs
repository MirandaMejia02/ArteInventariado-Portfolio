using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class ReturnsHistoryForm : Form
{
    private readonly ISaleCorrectionQueryService _queryService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly BindingSource _returnsBinding = new();
    private readonly BindingSource _linesBinding = new();
    private readonly BindingSource _refundsBinding = new();

    private readonly DateTimePicker _dtpDesde = new();
    private readonly DateTimePicker _dtpHasta = new();
    private readonly TextBox _txtNumeroVenta = new();
    private readonly TextBox _txtNumeroDevolucion = new();
    private readonly Button _btnBuscar = new();
    private readonly DataGridView _gridReturns = new();
    private readonly DataGridView _gridLines = new();
    private readonly DataGridView _gridRefunds = new();

    public ReturnsHistoryForm(
        ISaleCorrectionQueryService queryService,
        IEmployeePermissionService permissionService)
    {
        _queryService = queryService;
        _permissionService = permissionService;

        BuildUi();
        Load += ReturnsHistoryForm_Load;
    }

    private async void ReturnsHistoryForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Historial de devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Historial de devoluciones";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(1060, 640);
        AutoScroll = true;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24, 18, 24, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var lblTitle = new Label
        {
            Text = "Historial de devoluciones",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14)
        };
        UITheme.StyleLabelTitle(lblTitle);

        var filterPanel = CreateFilterPanel();

        ConfigureGrid(_gridReturns);
        _gridReturns.DataSource = _returnsBinding;
        _gridReturns.SelectionChanged += async (_, _) => await LoadSelectedReturnDetailAsync();
        AddGridColumn(_gridReturns, "Fecha", "Fecha", 132, 13);
        AddGridColumn(_gridReturns, "NumeroDevolucion", "Operación", 150, 16);
        AddGridColumn(_gridReturns, "NumeroVenta", "Venta", 142, 14);
        AddGridColumn(_gridReturns, "Usuario", "Usuario", 120, 13);
        AddGridColumn(_gridReturns, "Tipo", "Tipo", 95, 9);
        AddGridColumn(_gridReturns, "TotalDevuelto", "Total devuelto", 116, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridReturns, "TotalReembolsado", "Reembolsado", 112, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridReturns, "Motivo", "Motivo", 260, 35);

        ConfigureGrid(_gridLines);
        _gridLines.DataSource = _linesBinding;
        AddGridColumn(_gridLines, "Producto", "Producto", 220, 36);
        AddGridColumn(_gridLines, "Codigo", "Codigo", 100, 13);
        AddGridColumn(_gridLines, "Cantidad", "Cantidad", 76, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridLines, "Condicion", "Condicion", 95, 14);
        AddGridColumn(_gridLines, "ReintegraStock", "Stock", 70, 10);
        AddGridColumn(_gridLines, "Subtotal", "Subtotal", 92, 12, DataGridViewContentAlignment.MiddleRight);

        ConfigureGrid(_gridRefunds);
        _gridRefunds.DataSource = _refundsBinding;
        AddGridColumn(_gridRefunds, "Metodo", "Metodo", 96, 18);
        AddGridColumn(_gridRefunds, "Monto", "Monto", 86, 14, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_gridRefunds, "Usuario", "Usuario", 96, 18);
        AddGridColumn(_gridRefunds, "Referencia", "Referencia", 180, 50);

        var lowerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        lowerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        lowerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        lowerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lowerLayout.Controls.Add(CreateSection("Detalle", _gridLines), 0, 0);
        lowerLayout.Controls.Add(CreateSection("Reembolsos", _gridRefunds), 1, 0);

        root.Controls.Add(lblTitle, 0, 0);
        root.Controls.Add(filterPanel, 0, 1);
        root.Controls.Add(Header("Devoluciones"), 0, 2);
        root.Controls.Add(_gridReturns, 0, 3);
        root.Controls.Add(lowerLayout, 0, 4);
        Controls.Add(root);
    }

    private Control CreateFilterPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 14)
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0)
        };

        ConfigureDate(_dtpDesde, DateTime.Today.AddDays(-30));
        ConfigureDate(_dtpHasta, DateTime.Today);
        ConfigureTextBox(_txtNumeroVenta, 160);
        ConfigureTextBox(_txtNumeroDevolucion, 180);
        ConfigureButton(_btnBuscar, "Buscar", 108, async (_, _) => await RefreshAsync());
        UITheme.StylePrimaryButton(_btnBuscar);

        flow.Controls.AddRange([
            CreateField("Desde", _dtpDesde, 116),
            CreateField("Hasta", _dtpHasta, 116),
            CreateField("Venta", _txtNumeroVenta, 166),
            CreateField("Devolución / operación", _txtNumeroDevolucion, 190),
            CreateButtonField(_btnBuscar)
        ]);
        panel.Controls.Add(flow);

        return panel;
    }

    private async Task RefreshAsync()
    {
        try
        {
            _btnBuscar.Enabled = false;
            var filter = new ReturnHistoryFilter(
                DateOnly.FromDateTime(_dtpDesde.Value.Date),
                DateOnly.FromDateTime(_dtpHasta.Value.Date),
                _txtNumeroVenta.Text,
                _txtNumeroDevolucion.Text);

            var returns = await _queryService.GetReturnHistoryAsync(filter);
            _returnsBinding.DataSource = returns.Select(ReturnGridRow.FromSummary).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Historial de devoluciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnBuscar.Enabled = true;
        }
    }

    private async Task LoadSelectedReturnDetailAsync()
    {
        if (_gridReturns.CurrentRow?.DataBoundItem is not ReturnGridRow row)
        {
            return;
        }

        var detail = await _queryService.GetReturnDetailAsync(row.DevolucionId);
        if (detail is null)
        {
            _linesBinding.DataSource = Array.Empty<ReturnLineGridRow>();
            _refundsBinding.DataSource = Array.Empty<RefundGridRow>();
            return;
        }

        _linesBinding.DataSource = detail.Lines
            .Select(ReturnLineGridRow.FromLine)
            .Concat(detail.ChangeLines.Select(ReturnLineGridRow.FromChangeLine))
            .ToList();
        _refundsBinding.DataSource = detail.Refunds.Select(RefundGridRow.FromRefund).ToList();
    }

    private static Label Header(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 6)
        };
        UITheme.StyleLabelHeader(label);
        return label;
    }

    private static Control CreateSection(string title, Control content)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(Header(title), 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private static Control CreateField(string labelText, Control input, int width)
    {
        var panel = new TableLayoutPanel
        {
            Width = width,
            Height = 56,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 6)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label { Text = labelText, AutoSize = true, Dock = DockStyle.Top };
        UITheme.StyleLabelNormal(label);
        input.Dock = DockStyle.Top;
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private static Control CreateButtonField(Button button)
    {
        var panel = new Panel
        {
            Width = button.Width + 4,
            Height = 56,
            Margin = new Padding(0, 22, 10, 6)
        };
        button.Dock = DockStyle.Top;
        panel.Controls.Add(button);
        return panel;
    }

    private static void ConfigureDate(DateTimePicker picker, DateTime value)
    {
        picker.Size = new Size(110, 28);
        picker.Format = DateTimePickerFormat.Short;
        picker.Value = value;
    }

    private static void ConfigureTextBox(TextBox textBox, int width)
    {
        textBox.Size = new Size(width, 28);
        UITheme.StyleTextBox(textBox);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Size = new Size(width, 32);
        button.Click += handler;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ScrollBars = ScrollBars.Both;
        grid.ShowCellToolTips = true;
        grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        UITheme.StyleDataGridView(grid);
    }

    private static void AddGridColumn(
        DataGridView grid,
        string propertyName,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = { Alignment = alignment }
        });
    }

    private static void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        e.ToolTipText = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
    }

    private static string FormatDollars(long centavos) => (centavos / 100m).ToString("0.00");

    private sealed record ReturnGridRow(
        int DevolucionId,
        string Fecha,
        string NumeroDevolucion,
        string NumeroVenta,
        string Usuario,
        string Tipo,
        string TotalDevuelto,
        string TotalReembolsado,
        string Motivo)
    {
        public static ReturnGridRow FromSummary(ReturnHistorySummary summary)
        {
            return new ReturnGridRow(
                summary.DevolucionId,
                summary.FechaDevolucion.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                summary.NumeroDevolucion,
                summary.NumeroVenta,
                summary.Usuario,
                summary.TipoCorreccion.ToString(),
                FormatDollars(summary.TotalDevueltoCentavos),
                FormatDollars(summary.TotalReembolsadoCentavos),
                summary.Motivo);
        }
    }

    private sealed record ReturnLineGridRow(
        string Producto,
        string Codigo,
        int Cantidad,
        string Condicion,
        string ReintegraStock,
        string Subtotal)
    {
        public static ReturnLineGridRow FromLine(ReturnHistoryLine line)
        {
            return new ReturnLineGridRow(
                line.Producto,
                line.CodigoInterno,
                line.Cantidad,
                line.CondicionProducto.ToString(),
                line.ReintegraStock ? "Si" : "No",
                FormatDollars(line.SubtotalDevueltoCentavos));
        }

        public static ReturnLineGridRow FromChangeLine(ReturnHistoryChangeLine line)
        {
            return new ReturnLineGridRow(
                $"Entregado: {line.ProductoNuevo}",
                line.CodigoInterno,
                line.Cantidad,
                "CAMBIO",
                "Salida",
                FormatDollars(line.TotalCentavos));
        }
    }

    private sealed record RefundGridRow(string Metodo, string Monto, string Usuario, string? Referencia)
    {
        public static RefundGridRow FromRefund(ReturnHistoryRefund refund)
        {
            return new RefundGridRow(
                refund.MetodoReembolso.ToString(),
                FormatDollars(refund.MontoCentavos),
                refund.Usuario,
                refund.Referencia);
        }
    }
}
