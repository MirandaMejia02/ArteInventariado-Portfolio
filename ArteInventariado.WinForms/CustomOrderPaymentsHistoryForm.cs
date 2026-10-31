using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public sealed class CustomOrderPaymentsHistoryForm : Form
{
    private readonly int _orderId;
    private readonly string _orderCode;
    private readonly ICustomOrderFinanceQueryService _query;
    private readonly ICustomOrderFinanceDocumentService _documents;
    private readonly DataGridView _grid = new();
    private List<CustomOrderFinancialHistoryRow> _rows = [];

    public CustomOrderPaymentsHistoryForm(
        int orderId,
        string orderCode,
        ICustomOrderFinanceQueryService query,
        ICustomOrderFinanceDocumentService documents)
    {
        _orderId = orderId;
        _orderCode = orderCode;
        _query = query;
        _documents = documents;
        Text = $"Historial financiero - {orderCode}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1150, 650);
        MinimumSize = new Size(950, 560);
        UITheme.StyleForm(this);
        BuildUi();
        Load += async (_, _) => await LoadRowsAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var title = new Label { Text = $"Pagos y reembolsos de {_orderCode}", Dock = DockStyle.Fill };
        UITheme.StyleLabelTitle(title); root.Controls.Add(title);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false;
        _grid.AutoGenerateColumns = false; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        UITheme.StyleDataGridView(_grid);
        foreach (var (property, titleText, weight) in new[] {
            ("Date","Fecha",85),("Code","Codigo",90),("Type","Tipo",75),("Method","Metodo",70),
            ("Reference","Referencia",90),("Amount","Monto",65),("Before","Saldo anterior",75),
            ("After","Saldo nuevo",75),("User","Usuario",75),("Sale","Venta",65),("Status","Estado",90)})
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = titleText, FillWeight = weight });
        root.Controls.Add(_grid);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var close = new Button { Text = "Cerrar", Size = new Size(120, 40) };
        var receipt = new Button { Text = "Generar comprobante", Size = new Size(190, 40) };
        UITheme.StyleSecondaryButton(close); UITheme.StylePrimaryButton(receipt);
        close.Click += (_, _) => Close(); receipt.Click += async (_, _) => await GenerateAsync();
        actions.Controls.Add(close); actions.Controls.Add(receipt); root.Controls.Add(actions);
        Controls.Add(root);
    }

    private async Task LoadRowsAsync()
    {
        try
        {
            _rows = (await _query.GetHistoryAsync(_orderId)).ToList();
            _grid.DataSource = _rows.Select(r => new Row(
                Local(r.Fecha).ToString("yyyy-MM-dd HH:mm"), r.Codigo, r.Tipo,
                r.MetodoPago?.ToString() ?? "-", r.Referencia ?? "-", Money(r.MontoCentavos),
                Money(r.SaldoAnteriorCentavos), Money(r.SaldoNuevoCentavos), r.Usuario,
                r.VentaId?.ToString() ?? "-", r.Estado, r.EsReembolso, r.MovimientoId)).ToList();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task GenerateAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Row row)
        {
            MessageBox.Show("Seleccione un movimiento.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var path = row.IsRefund
                ? await _documents.GenerateRefundPdfAsync(row.Id)
                : await _documents.GeneratePaymentPdfAsync(row.Id);
            using var form = new GeneratedFileForm("Comprobante generado", path);
            form.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private sealed record Row(string Date, string Code, string Type, string Method, string Reference,
        string Amount, string Before, string After, string User, string Sale, string Status, bool IsRefund, int Id);
}
