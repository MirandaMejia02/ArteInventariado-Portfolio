using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class AccountPayableDetailForm : Form
{
    private readonly AccountPayableDetail _detail;
    private readonly IAccountsPayableQueryService? _queryService;

    public AccountPayableDetailForm(AccountPayableDetail detail, IAccountsPayableQueryService? queryService = null)
    {
        _detail = detail;
        _queryService = queryService;
        BuildUi();
    }

    private void BuildUi()
    {
        var detail = _detail;
        Text = $"Cuenta {detail.Summary.CodigoCuenta}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1050, 720);
        MinimumSize = new Size(900, 620);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var title = new Label { Text = $"Cuenta por pagar {detail.Summary.CodigoCuenta}", Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildSummary(detail), 0, 1);
        root.Controls.Add(BuildTabs(detail), 0, 2);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var close = new Button { Text = "Cerrar", Size = new Size(120, 40), Margin = new Padding(8, 0, 0, 0) };
        UITheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        var history = new Button { Text = "Historial de pagos", Size = new Size(185, 40), Margin = new Padding(8, 0, 0, 0), Visible = _queryService is not null };
        UITheme.StylePrimaryButton(history);
        history.Click += (_, _) => OpenHistory();
        actions.Controls.AddRange([close, history]);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    private void OpenHistory()
    {
        if (_queryService is null)
        {
            return;
        }

        using var form = new SupplierPaymentsHistoryForm(_queryService, accountId: _detail.Summary.CuentaPorPagarId);
        form.ShowDialog(this);
    }

    private static Control BuildSummary(AccountPayableDetail detail)
    {
        var s = detail.Summary;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), ColumnCount = 4, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        for (var i = 0; i < 4; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        AddCell(panel, "Compra", s.CodigoCompra, 0, 0);
        AddCell(panel, "Proveedor", s.Proveedor, 1, 0);
        AddCell(panel, "Estado", s.Estado.ToString(), 2, 0);
        AddCell(panel, "Vencimiento", s.FechaVencimiento.ToString("yyyy-MM-dd"), 3, 0);
        AddCell(panel, "Total", SaleReceiptViewModel.FormatDollars(s.TotalOriginalCentavos), 0, 1);
        AddCell(panel, "Pagado", SaleReceiptViewModel.FormatDollars(s.TotalPagadoCentavos), 1, 1);
        AddCell(panel, "Saldo", SaleReceiptViewModel.FormatDollars(s.SaldoPendienteCentavos), 2, 1);
        AddCell(panel, "Estado compra", detail.EstadoFinancieroCompra.ToString(), 3, 1);
        return panel;
    }

    private static Control BuildTabs(AccountPayableDetail detail)
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = UITheme.NormalFont };
        tabs.TabPages.Add(BuildTab("Pagos iniciales", detail.PagosIniciales.Select(p => new
        {
            p.CodigoPago,
            Fecha = SaleReceiptViewModel.ToLocal(p.FechaPago),
            Metodo = p.MetodoPago.ToString(),
            Origen = PaymentOriginText(p.OrigenFondos),
            Responsable = p.ResponsableOrigen ?? "-",
            EfectoCaja = CashEffectText(p.OrigenFondos, p.CajaId),
            Monto = SaleReceiptViewModel.FormatDollars(p.MontoCentavos),
            p.Referencia,
            p.Usuario,
            p.CajaId
        }).ToList()));
        tabs.TabPages.Add(BuildTab("Abonos", detail.Abonos.Select(a => new
        {
            a.CodigoAbono,
            Fecha = SaleReceiptViewModel.ToLocal(a.FechaAbono),
            Metodo = a.MetodoPago.ToString(),
            Origen = PaymentOriginText(a.OrigenFondos),
            Responsable = a.ResponsableOrigen ?? "-",
            EfectoCaja = CashEffectText(a.OrigenFondos, a.CajaId),
            Monto = SaleReceiptViewModel.FormatDollars(a.MontoCentavos),
            SaldoAnterior = SaleReceiptViewModel.FormatDollars(a.SaldoAnteriorCentavos),
            SaldoNuevo = SaleReceiptViewModel.FormatDollars(a.SaldoNuevoCentavos),
            a.Referencia,
            a.Usuario
        }).ToList()));
        var notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = detail.Summary.Observaciones ?? "Sin observaciones.", BackColor = UITheme.SurfaceColor, Font = UITheme.NormalFont };
        tabs.TabPages.Add(new TabPage("Observaciones") { BackColor = UITheme.SurfaceColor, Padding = new Padding(12), Controls = { notes } });
        return tabs;
    }

    private static string PaymentOriginText(OrigenFondos? origin) =>
        origin?.ToString() ?? "No especificado (histórico)";

    private static string CashEffectText(OrigenFondos? origin, int? cajaId) =>
        origin is null ? "No especificado (histórico)" : cajaId.HasValue ? "Reduce caja física" : "No afecta caja física";

    private static TabPage BuildTab<T>(string title, IReadOnlyList<T> rows)
    {
        var tab = new TabPage(title) { BackColor = UITheme.SurfaceColor, Padding = new Padding(12) };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, DataSource = rows };
        UITheme.StyleDataGridView(grid);
        tab.Controls.Add(grid);
        return tab;
    }

    private static void AddCell(TableLayoutPanel panel, string caption, string value, int column, int row)
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(5) };
        host.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor }, 0, 0);
        host.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, Font = UITheme.HeaderFont, AutoEllipsis = true }, 0, 1);
        panel.Controls.Add(host, column, row);
    }
}
