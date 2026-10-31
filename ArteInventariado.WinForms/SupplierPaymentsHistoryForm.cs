using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class SupplierPaymentsHistoryForm : Form
{
    private readonly IAccountsPayableQueryService _queryService;
    private readonly int? _supplierId;
    private readonly int? _accountId;
    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbMethod = new();
    private readonly DateTimePicker _dtpFrom = new();
    private readonly DateTimePicker _dtpTo = new();
    private readonly DataGridView _grid = new();
    private readonly Button _btnReceipt = new();

    public SupplierPaymentsHistoryForm(IAccountsPayableQueryService queryService, int? supplierId = null, int? accountId = null)
    {
        _queryService = queryService;
        _supplierId = supplierId;
        _accountId = accountId;
        BuildUi();
        Load += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        Text = "Historial de pagos a proveedores";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 700);
        MinimumSize = new Size(940, 600);
        UITheme.StyleForm(this);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var title = new Label { Text = "Historial de pagos a proveedores", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildFilters(), 0, 1);
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        ConfigureGrid();
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateReceiptState();
        _grid.CellDoubleClick += (_, _) => OpenReceipt();
        root.Controls.Add(_grid, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var close = Button("Cerrar", UITheme.StyleSecondaryButton, (_, _) => Close());
        ConfigureButton(_btnReceipt, "Ver comprobante", UITheme.StylePrimaryButton, (_, _) => OpenReceipt());
        _btnReceipt.Enabled = false;
        actions.Controls.AddRange([close, _btnReceipt]);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), ColumnCount = 5, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        for (var i = 0; i < 5; i++) panel.Controls.Add(new Label { Text = new[] { "Buscar", "Metodo", "Desde", "Hasta", "" }[i], Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont }, i, 0);
        UITheme.StyleTextBox(_txtSearch);
        _txtSearch.Dock = DockStyle.Fill;
        _cmbMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMethod.DataSource = new object[] { "TODOS", MetodoPago.EFECTIVO, MetodoPago.TRANSFERENCIA, MetodoPago.BITCOIN };
        _dtpFrom.Format = _dtpTo.Format = DateTimePickerFormat.Short;
        _dtpFrom.Value = DateTime.Today.AddMonths(-1);
        _dtpTo.Value = DateTime.Today;
        var search = Button("Buscar", UITheme.StylePrimaryButton, async (_, _) => await RefreshAsync());
        panel.Controls.Add(_txtSearch, 0, 1);
        panel.Controls.Add(_cmbMethod, 1, 1);
        panel.Controls.Add(_dtpFrom, 2, 1);
        panel.Controls.Add(_dtpTo, 3, 1);
        panel.Controls.Add(search, 4, 1);
        return panel;
    }

    private async Task RefreshAsync()
    {
        try
        {
            MetodoPago? method = _cmbMethod.SelectedItem is MetodoPago selected ? selected : null;
            var rows = await _queryService.GetPaymentHistoryAsync(new SupplierPaymentHistoryFilter(
                _dtpFrom.Value.Date, _dtpTo.Value.Date, _supplierId, _accountId, null, method, _txtSearch.Text));
            _binding.DataSource = rows.Select(r => new PaymentRow(
                r,
                SaleReceiptViewModel.ToLocal(r.Fecha).ToString("yyyy-MM-dd HH:mm"),
                r.Codigo,
                r.Tipo,
                r.Proveedor,
                r.CodigoCompra,
                r.CodigoCuenta,
                r.MetodoPago.ToString(),
                OriginText(r.OrigenFondos),
                r.ResponsableOrigen ?? "-",
                CashEffectText(r.OrigenFondos, r.CajaId),
                r.Referencia ?? "-",
                SaleReceiptViewModel.FormatDollars(r.MontoCentavos),
                SaleReceiptViewModel.FormatDollars(r.SaldoAnteriorCentavos),
                SaleReceiptViewModel.FormatDollars(r.SaldoNuevoCentavos),
                r.Usuario)).ToList();
            if (_binding.Count == 0)
            {
                _grid.ClearSelection();
            }
            UpdateReceiptState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Historial de pagos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenReceipt()
    {
        if (_binding.Current is not PaymentRow row)
        {
            MessageBox.Show("Seleccione un pago del historial.", "Historial de pagos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new SupplierPaymentReceiptForm(row.Source);
        form.ShowDialog(this);
    }

    private void UpdateReceiptState()
    {
        _btnReceipt.Enabled = _binding.Current is PaymentRow && _grid.SelectedRows.Count > 0;
    }

    private void ConfigureGrid()
    {
        AddColumn(nameof(PaymentRow.Fecha), "Fecha", 130);
        AddColumn(nameof(PaymentRow.Codigo), "Codigo", 125);
        AddColumn(nameof(PaymentRow.Tipo), "Tipo", 105);
        AddColumn(nameof(PaymentRow.Proveedor), "Proveedor", 180);
        AddColumn(nameof(PaymentRow.Compra), "Compra", 120);
        AddColumn(nameof(PaymentRow.Cuenta), "Cuenta", 120);
        AddColumn(nameof(PaymentRow.Metodo), "Metodo", 110);
        AddColumn(nameof(PaymentRow.Origen), "Origen", 180);
        AddColumn(nameof(PaymentRow.Responsable), "Responsable/fuente", 170);
        AddColumn(nameof(PaymentRow.EfectoCaja), "Efecto en caja", 150);
        AddColumn(nameof(PaymentRow.Referencia), "Referencia", 140);
        AddColumn(nameof(PaymentRow.Monto), "Monto", 95, DataGridViewContentAlignment.MiddleRight);
        AddColumn(nameof(PaymentRow.SaldoAnterior), "Saldo anterior", 110, DataGridViewContentAlignment.MiddleRight);
        AddColumn(nameof(PaymentRow.SaldoNuevo), "Saldo nuevo", 110, DataGridViewContentAlignment.MiddleRight);
        AddColumn(nameof(PaymentRow.Usuario), "Usuario", 110);
    }

    private void AddColumn(string property, string header, int width, DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            MinimumWidth = Math.Min(width, 70),
            SortMode = DataGridViewColumnSortMode.Automatic,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private static Button Button(string text, Action<Button> style, EventHandler click)
    {
        var button = new Button();
        ConfigureButton(button, text, style, click);
        return button;
    }

    private static void ConfigureButton(Button button, string text, Action<Button> style, EventHandler click)
    {
        using var font = UITheme.ButtonFont;
        var width = Math.Max(150, TextRenderer.MeasureText(text, font).Width + 32);
        button.Text = text;
        button.Size = new Size(width, 40);
        button.Margin = new Padding(8, 0, 0, 0);
        style(button);
        button.Click += click;
    }

    private sealed record PaymentRow(
        SupplierPaymentHistoryEntry Source,
        string Fecha,
        string Codigo,
        string Tipo,
        string Proveedor,
        string Compra,
        string Cuenta,
        string Metodo,
        string Origen,
        string Responsable,
        string EfectoCaja,
        string Referencia,
        string Monto,
        string SaldoAnterior,
        string SaldoNuevo,
        string Usuario);

    private static string OriginText(OrigenFondos? origin) => origin?.ToString() ?? "No especificado (histórico)";

    private static string CashEffectText(OrigenFondos? origin, int? cajaId) =>
        origin is null ? "No especificado (histórico)" : cajaId.HasValue ? "Reduce caja física" : "No afecta caja física";
}
