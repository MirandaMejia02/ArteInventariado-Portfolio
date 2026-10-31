using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public class PurchaseDetailForm : Form
{
    private readonly PurchaseDetail _purchase;
    private readonly BindingSource _linesBinding = new();
    private readonly BindingSource _receptionsBinding = new();
    private readonly BindingSource _costsBinding = new();
    private readonly Dictionary<string, Label> _values = new();
    private readonly DataGridView _linesGrid = new();
    private readonly DataGridView _receptionsGrid = new();
    private readonly DataGridView _costsGrid = new();
    private readonly Button _btnClose = new();
    private readonly Button _btnPayment = new();
    private readonly Button _btnAccount = new();
    private readonly Button _btnHistory = new();
    private readonly IPurchasePaymentService? _purchasePaymentService;
    private readonly IAccountsPayableQueryService? _accountsPayableQueryService;
    private readonly IAccountsPayableService? _accountsPayableService;

    public PurchaseDetailForm(
        PurchaseDetail purchase,
        IPurchasePaymentService? purchasePaymentService = null,
        IAccountsPayableQueryService? accountsPayableQueryService = null,
        IAccountsPayableService? accountsPayableService = null)
    {
        _purchase = purchase;
        _purchasePaymentService = purchasePaymentService;
        _accountsPayableQueryService = accountsPayableQueryService;
        _accountsPayableService = accountsPayableService;
        BuildUi();
        Load += (_, _) => LoadData();
    }

    private void BuildUi()
    {
        Text = $"Detalle de compra {_purchase.CodigoCompra}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(980, 640);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildTabs(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = $"Compra {_purchase.CodigoCompra}",
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = $"{_purchase.CodigoProveedor} - {_purchase.Proveedor}",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 18, 18, 20),
            ColumnCount = 4,
            RowCount = 3,
            MinimumSize = new Size(0, 252),
            Margin = new Padding(0, 0, 0, 12)
        };
        for (var i = 0; i < 4; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddSummaryCell(panel, "Estado", "Estado", 0, 0);
        AddSummaryCell(panel, "Fecha", "Fecha compra", 1, 0);
        AddSummaryCell(panel, "Registrada", "Fecha registro", 2, 0);
        AddSummaryCell(panel, "Usuario", "Usuario", 3, 0);
        AddSummaryCell(panel, "Subtotal", "Subtotal", 0, 1);
        AddSummaryCell(panel, "Descuento", "Descuento", 1, 1);
        AddSummaryCell(panel, "Total", "Total", 2, 1);
        AddSummaryCell(panel, "Recepciones", "Recepciones", 3, 1);
        AddSummaryCell(panel, "Observaciones", "Observaciones", 0, 2);
        panel.SetColumnSpan(_values["Observaciones"].Parent!, 4);
        return panel;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont
        };

        var linesTab = new TabPage("Detalle de productos")
        {
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };
        ConfigureLinesGrid();
        _linesGrid.DataSource = _linesBinding;
        linesTab.Controls.Add(_linesGrid);

        var receptionsTab = new TabPage("Recepciones")
        {
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };
        ConfigureReceptionsGrid();
        _receptionsGrid.DataSource = _receptionsBinding;
        receptionsTab.Controls.Add(_receptionsGrid);

        var costsTab = new TabPage("Historial de costos")
        {
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };
        ConfigureCostsGrid();
        _costsGrid.DataSource = _costsBinding;
        costsTab.Controls.Add(_costsGrid);

        tabs.TabPages.Add(linesTab);
        tabs.TabPages.Add(receptionsTab);
        tabs.TabPages.Add(costsTab);
        tabs.TabPages.Add(BuildFinancialTab());
        return tabs;
    }

    private TabPage BuildFinancialTab()
    {
        var tab = new TabPage("Pagos y saldo") { BackColor = UITheme.SurfaceColor, Padding = new Padding(18) };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        for (var i = 0; i < 3; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddFinancialValue(panel, "Estado financiero", _purchase.EstadoFinanciero.ToString(), 0);
        AddFinancialValue(panel, "Pagado", SaleReceiptViewModel.FormatDollars(_purchase.TotalPagadoCentavos), 1);
        AddFinancialValue(panel, "Saldo pendiente", SaleReceiptViewModel.FormatDollars(_purchase.SaldoPendienteCentavos), 2);
        var note = new Label
        {
            Text = _purchase.CuentaPorPagarId.HasValue
                ? $"Cuenta relacionada: CPP-{_purchase.CompraId:000000}. Use las acciones inferiores para consultar pagos o abonar."
                : "La compra no tiene una cuenta por pagar relacionada.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(8, 18, 8, 8)
        };
        panel.Controls.Add(note, 0, 1);
        panel.SetColumnSpan(note, 3);
        tab.Controls.Add(panel);
        return tab;
    }

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        _btnClose.Text = "Cerrar";
        _btnClose.Width = 110;
        _btnClose.Height = 36;
        _btnClose.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(_btnClose);
        ConfigureAction(_btnHistory, "Historial pagos", 140, (_, _) => OpenHistory());
        UITheme.StyleSecondaryButton(_btnHistory);
        ConfigureAction(_btnAccount, "Cuenta por pagar", 190, async (_, _) => await OpenAccountAsync());
        UITheme.StyleSecondaryButton(_btnAccount);
        ConfigureAction(_btnPayment, "Registrar pago", 140, async (_, _) => await RegisterPaymentAsync());
        UITheme.StyleSuccessButton(_btnPayment);
        _btnPayment.Visible = _purchasePaymentService is not null && _purchase.TotalPagadoCentavos == 0 && _purchase.SaldoPendienteCentavos > 0;
        _btnAccount.Visible = _accountsPayableQueryService is not null && _purchase.CuentaPorPagarId.HasValue;
        _btnHistory.Visible = _accountsPayableQueryService is not null && _purchase.CuentaPorPagarId.HasValue;
        panel.Controls.AddRange([_btnClose, _btnHistory, _btnAccount, _btnPayment]);
        return panel;
    }

    private async Task RegisterPaymentAsync()
    {
        if (_purchasePaymentService is null) return;
        using var form = new PurchasePaymentForm(_purchase, _purchasePaymentService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private async Task OpenAccountAsync()
    {
        if (_accountsPayableQueryService is null || !_purchase.CuentaPorPagarId.HasValue) return;
        var detail = await _accountsPayableQueryService.GetDetailAsync(_purchase.CuentaPorPagarId.Value);
        if (detail is null) return;
        using var form = new AccountPayableDetailForm(detail, _accountsPayableQueryService);
        form.ShowDialog(this);
    }

    private void OpenHistory()
    {
        if (_accountsPayableQueryService is null || !_purchase.CuentaPorPagarId.HasValue) return;
        using var form = new SupplierPaymentsHistoryForm(_accountsPayableQueryService, accountId: _purchase.CuentaPorPagarId.Value);
        form.ShowDialog(this);
    }

    private static void ConfigureAction(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Size = new Size(width, 36);
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private static void AddFinancialValue(TableLayoutPanel panel, string caption, string value, int column)
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = UITheme.BackgroundColor, Padding = new Padding(14), Margin = new Padding(5) };
        host.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor }, 0, 0);
        host.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, Font = UITheme.HeaderFont, ForeColor = UITheme.PrimaryColor }, 0, 1);
        panel.Controls.Add(host, column, 0);
    }

    private void LoadData()
    {
        SetValue("Estado", _purchase.EstadoRecepcion.ToString());
        SetValue("Fecha", _purchase.FechaCompra.ToString("yyyy-MM-dd"));
        SetValue("Registrada", SaleReceiptViewModel.ToLocal(_purchase.FechaRegistro).ToString("yyyy-MM-dd HH:mm"));
        SetValue("Usuario", _purchase.UsuarioRegistro);
        SetValue("Subtotal", SaleReceiptViewModel.FormatDollars(_purchase.SubtotalCentavos));
        SetValue("Descuento", SaleReceiptViewModel.FormatDollars(_purchase.DescuentoCentavos));
        SetValue("Total", SaleReceiptViewModel.FormatDollars(_purchase.TotalCentavos));
        SetValue("Recepciones", _purchase.Recepciones.Count.ToString());
        SetValue("Observaciones", string.IsNullOrWhiteSpace(_purchase.Observaciones) ? "-" : _purchase.Observaciones);

        _linesBinding.DataSource = _purchase.Detalles.Select(PurchaseLineRow.FromSummary).ToList();
        _receptionsBinding.DataSource = _purchase.Recepciones
            .SelectMany(reception => reception.Detalles.Select(line => PurchaseReceptionRow.FromSummary(reception, line)))
            .ToList();
        _costsBinding.DataSource = _purchase.HistorialCostos.Select(CostHistoryGridRow.FromSummary).ToList();
    }

    private void ConfigureLinesGrid()
    {
        ConfigureBaseGrid(_linesGrid);
        AddGridColumn(_linesGrid, "Codigo", "Codigo", 110, 10);
        AddGridColumn(_linesGrid, "Producto", "Producto", 260, 32);
        AddGridColumn(_linesGrid, "Comprado", "Comprado", 90, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_linesGrid, "Recibido", "Recibido", 90, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_linesGrid, "Pendiente", "Pendiente", 90, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_linesGrid, "Costo", "Costo", 110, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_linesGrid, "Descuento", "Descuento", 110, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_linesGrid, "Subtotal", "Subtotal", 110, 10, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_linesGrid, "Observaciones", "Observaciones", 180, 18);
    }

    private void ConfigureReceptionsGrid()
    {
        ConfigureBaseGrid(_receptionsGrid);
        AddGridColumn(_receptionsGrid, "CodigoRecepcion", "Recepcion", 120, 10);
        AddGridColumn(_receptionsGrid, "Fecha", "Fecha", 140, 12);
        AddGridColumn(_receptionsGrid, "Usuario", "Usuario", 130, 10);
        AddGridColumn(_receptionsGrid, "Codigo", "Codigo", 110, 10);
        AddGridColumn(_receptionsGrid, "Producto", "Producto", 240, 26);
        AddGridColumn(_receptionsGrid, "Cantidad", "Cantidad", 90, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_receptionsGrid, "StockAnterior", "Stock ant.", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_receptionsGrid, "StockNuevo", "Stock nuevo", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_receptionsGrid, "Costo", "Costo", 110, 10, DataGridViewContentAlignment.MiddleRight);
    }

    private void ConfigureCostsGrid()
    {
        ConfigureBaseGrid(_costsGrid);
        AddGridColumn(_costsGrid, "Fecha", "Fecha", 140, 12);
        AddGridColumn(_costsGrid, "Codigo", "Codigo", 110, 10);
        AddGridColumn(_costsGrid, "Producto", "Producto", 260, 28);
        AddGridColumn(_costsGrid, "CostoAnterior", "Costo anterior", 125, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_costsGrid, "CostoRecibido", "Costo recibido", 125, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_costsGrid, "CostoNuevo", "Costo promedio", 125, 12, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_costsGrid, "StockAnterior", "Stock ant.", 90, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_costsGrid, "Cantidad", "Cantidad", 90, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_costsGrid, "StockNuevo", "Stock nuevo", 95, 8, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_costsGrid, "Usuario", "Usuario", 120, 10);
    }

    private static void ConfigureBaseGrid(DataGridView grid)
    {
        UITheme.StyleDataGridView(grid);
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.ShowCellToolTips = true;
    }

    private static void AddGridColumn(
        DataGridView grid,
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private void AddSummaryCell(TableLayoutPanel panel, string key, string title, int column, int row)
    {
        var cell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 16, 10),
            MinimumSize = row == 2 ? new Size(0, 92) : new Size(0, 56)
        };
        cell.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        cell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var valueLabel = new Label
        {
            Text = "-",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.TopLeft
        };
        cell.Controls.Add(titleLabel, 0, 0);
        cell.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(cell, column, row);
        _values[key] = valueLabel;
    }

    private void SetValue(string key, string value)
    {
        if (_values.TryGetValue(key, out var label))
        {
            label.Text = value;
        }
    }

    private sealed record PurchaseLineRow(
        string Codigo,
        string Producto,
        int Comprado,
        int Recibido,
        int Pendiente,
        string Costo,
        string Descuento,
        string Subtotal,
        string Observaciones)
    {
        public static PurchaseLineRow FromSummary(PurchaseLineSummary summary)
        {
            return new PurchaseLineRow(
                summary.CodigoProducto,
                summary.Producto,
                summary.CantidadComprada,
                summary.CantidadRecibida,
                summary.CantidadPendiente,
                SaleReceiptViewModel.FormatDollars(summary.CostoUnitarioCentavos),
                SaleReceiptViewModel.FormatDollars(summary.DescuentoLineaCentavos),
                SaleReceiptViewModel.FormatDollars(summary.SubtotalCentavos),
                summary.Observaciones ?? string.Empty);
        }
    }

    private sealed record PurchaseReceptionRow(
        string CodigoRecepcion,
        string Fecha,
        string Usuario,
        string Codigo,
        string Producto,
        int Cantidad,
        int StockAnterior,
        int StockNuevo,
        string Costo)
    {
        public static PurchaseReceptionRow FromSummary(
            PurchaseReceptionSummary reception,
            PurchaseReceptionLineSummary line)
        {
            return new PurchaseReceptionRow(
                reception.CodigoRecepcion,
                SaleReceiptViewModel.ToLocal(reception.FechaRecepcion).ToString("yyyy-MM-dd HH:mm"),
                reception.UsuarioRecepcion,
                line.CodigoProducto,
                line.Producto,
                line.CantidadRecibida,
                line.StockAnterior,
                line.StockNuevo,
                SaleReceiptViewModel.FormatDollars(line.CostoUnitarioCentavos));
        }
    }

    private sealed record CostHistoryGridRow(
        string Fecha,
        string Codigo,
        string Producto,
        string CostoAnterior,
        string CostoRecibido,
        string CostoNuevo,
        int StockAnterior,
        int Cantidad,
        int StockNuevo,
        string Usuario)
    {
        public static CostHistoryGridRow FromSummary(CostHistoryRow row)
        {
            return new CostHistoryGridRow(
                SaleReceiptViewModel.ToLocal(row.Fecha).ToString("yyyy-MM-dd HH:mm"),
                row.CodigoProducto,
                row.Producto,
                SaleReceiptViewModel.FormatDollars(row.CostoAnteriorCentavos),
                SaleReceiptViewModel.FormatDollars(row.CostoRecibidoCentavos),
                SaleReceiptViewModel.FormatDollars(row.CostoPromedioNuevoCentavos),
                row.StockAnterior,
                row.CantidadRecibida,
                row.StockNuevo,
                row.Usuario);
        }
    }
}
