using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using System.Globalization;

namespace ArteInventariado.WinForms;

public sealed class MySalesTodayForm : Form
{
    private readonly IMySalesTodayQueryService _queryService;
    private readonly DataGridView _salesGrid = new();
    private readonly DataGridView _linesGrid = new();
    private readonly DataGridView _paymentsGrid = new();
    private readonly Label _detailTitle = new();
    private readonly Label _detailSummary = new();
    private readonly Label _completedCount = new();
    private readonly Label _completedTotal = new();
    private readonly Label _statsNote = new();
    private readonly Button _refresh = new();
    private IReadOnlyList<MySalesTodaySummary> _sales = [];
    private bool _loading;

    public MySalesTodayForm(IMySalesTodayQueryService queryService)
    {
        _queryService = queryService;
        BuildUi();
        Load += async (_, _) => await LoadSalesAsync();
        _salesGrid.SelectionChanged += async (_, _) => await ShowSelectedSaleAsync();
    }

    private void BuildUi()
    {
        Text = "Mis ventas de hoy";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 640);
        ClientSize = new Size(1280, 800);
        WindowState = FormWindowState.Maximized;
        UITheme.StyleForm(this);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Close();
            }
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UITheme.SpaceXl),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, UITheme.SpaceSm)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "Mis ventas de hoy",
            Dock = DockStyle.Fill,
            Font = UITheme.TitleFont,
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(title, 0, 0);
        _refresh.Text = "Actualizar";
        UITheme.StyleSecondaryButton(_refresh);
        UITheme.EnsureButtonContentFits(_refresh, 145, 40, 28);
        _refresh.Dock = DockStyle.Fill;
        _refresh.Margin = Padding.Empty;
        _refresh.Click += async (_, _) => await LoadSalesAsync();
        header.Controls.Add(_refresh, 1, 0);
        header.Controls.Add(new Label
        {
            Text = "Solo tus ventas del día empresarial actual · consulta de solo lectura",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        header.SetColumnSpan(header.GetControlFromPosition(0, 1)!, 2);
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(BuildMetrics(), 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        content.Controls.Add(BuildSalesPanel(), 0, 0);
        content.Controls.Add(BuildDetailPanel(), 1, 0);
        root.Controls.Add(content, 0, 2);

        var close = new Button { Text = "Cerrar", Dock = DockStyle.Right, Width = 120, Margin = Padding.Empty };
        UITheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        root.Controls.Add(close, 0, 3);
        Controls.Add(root);
    }

    private Control BuildMetrics()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, UITheme.SpaceSm)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        for (var index = 0; index < 2; index++)
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.Controls.Add(BuildMetric("Ventas completadas", _completedCount), 0, 0);
        cards.Controls.Add(BuildMetric("Total vendido", _completedTotal), 1, 0);
        panel.Controls.Add(cards, 0, 0);
        _statsNote.Dock = DockStyle.Fill;
        _statsNote.TextAlign = ContentAlignment.MiddleLeft;
        _statsNote.ForeColor = UITheme.MutedTextColor;
        _statsNote.Font = UITheme.CaptionFont;
        panel.Controls.Add(_statsNote, 0, 1);
        return panel;
    }

    private static Control BuildMetric(string title, Label value)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UITheme.SpaceMd, 2, UITheme.SpaceMd, 2),
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, UITheme.SpaceSm, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor
        }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.Font = UITheme.HeaderFont;
        value.ForeColor = UITheme.SecondaryColor;
        value.AutoSize = false;
        value.AutoEllipsis = false;
        value.MinimumSize = new Size(0, 30);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private Control BuildSalesPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(UITheme.SpaceMd), Margin = new Padding(0, 0, UITheme.SpaceSm, 0) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Ventas registradas",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.SecondaryColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        }, 0, 0);

        ConfigureGrid(_salesGrid);
        _salesGrid.MultiSelect = false;
        _salesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "numero", HeaderText = "Venta", Width = 120 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "hora", HeaderText = "Hora", Width = 80 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "estado", HeaderText = "Estado", Width = 90 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "total", HeaderText = "Total", Width = 100 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "metodos", HeaderText = "Método(s)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        layout.Controls.Add(_salesGrid, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildDetailPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(UITheme.SpaceMd), Margin = new Padding(UITheme.SpaceSm, 0, 0, 0) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));

        _detailTitle.Dock = DockStyle.Fill;
        _detailTitle.Font = UITheme.SubheaderFont;
        _detailTitle.ForeColor = UITheme.PrimaryColor;
        _detailTitle.TextAlign = ContentAlignment.MiddleLeft;
        _detailTitle.AutoEllipsis = true;
        _detailTitle.Margin = Padding.Empty;
        layout.Controls.Add(_detailTitle, 0, 0);

        _detailSummary.Dock = DockStyle.Fill;
        _detailSummary.Font = UITheme.NormalBoldFont;
        _detailSummary.ForeColor = UITheme.TextDark;
        _detailSummary.TextAlign = ContentAlignment.MiddleLeft;
        _detailSummary.AutoEllipsis = true;
        _detailSummary.Margin = Padding.Empty;
        layout.Controls.Add(_detailSummary, 0, 1);

        ConfigureGrid(_linesGrid);
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "producto", HeaderText = "Producto", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sku", HeaderText = "SKU", Width = 100 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cantidad", HeaderText = "Cant.", Width = 55 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "precio", HeaderText = "Precio", Width = 90 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "descuento", HeaderText = "Desc.", Width = 85 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "total", HeaderText = "Total", Width = 90 });
        layout.Controls.Add(_linesGrid, 0, 2);

        var paymentsTitle = new Label
        {
            Text = "Métodos de pago",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.SecondaryColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, UITheme.SpaceSm, 0, 0)
        };
        layout.Controls.Add(paymentsTitle, 0, 3);

        ConfigureGrid(_paymentsGrid);
        _paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "metodo", HeaderText = "Método", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "monto", HeaderText = "Monto", Width = 110 });
        layout.Controls.Add(_paymentsGrid, 0, 4);

        panel.Controls.Add(layout);
        return panel;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.Margin = Padding.Empty;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        UITheme.StyleGrid(grid);
    }

    private async Task LoadSalesAsync()
    {
        try
        {
            _loading = true;
            _refresh.Enabled = false;
            _sales = await _queryService.GetSalesAsync();
            var completed = _sales.Where(sale => sale.Estado == EstadoVenta.COMPLETADA).ToList();
            var completedTotal = completed.Sum(sale => sale.TotalCentavos);
            _completedCount.Text = completed.Count.ToString(CultureInfo.InvariantCulture);
            _completedTotal.Text = Money(completedTotal);
            var cancelled = _sales.Count - completed.Count;
            _statsNote.Text = $"Anuladas: {cancelled} · Actualizado: {DateTime.Now:HH:mm}. El resumen solo considera ventas completadas.";
            _salesGrid.Rows.Clear();
            foreach (var sale in _sales)
            {
                _salesGrid.Rows.Add(
                    sale.NumeroVenta,
                    sale.FechaVentaLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
                    sale.Estado == EstadoVenta.COMPLETADA ? "Completada" : "Anulada",
                    Money(sale.TotalCentavos),
                    sale.MetodosPago);
            }

            _detailTitle.Text = _sales.Count == 0 ? "Sin ventas hoy" : "Detalle de venta";
            _detailSummary.Text = _sales.Count == 0
                ? "No hay ventas tuyas registradas en el día empresarial actual."
                : "Selecciona una venta de la lista.";
            if (_sales.Count > 0)
            {
                _salesGrid.Rows[0].Selected = true;
                await ShowSelectedSaleAsync();
            }
            else
            {
                ClearDetail();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Mis ventas de hoy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
        finally
        {
            _loading = false;
            _refresh.Enabled = true;
        }
    }

    private async Task ShowSelectedSaleAsync()
    {
        if (_loading || _salesGrid.CurrentRow?.Index is not int index || index < 0 || index >= _sales.Count)
        {
            return;
        }

        var detail = await _queryService.GetSaleDetailAsync(_sales[index].VentaId);
        if (detail is null)
        {
            ClearDetail();
            return;
        }

        _detailTitle.Text = $"Venta {detail.NumeroVenta}";
        _detailSummary.Text =
            $"{detail.FechaVentaLocal:dd/MM/yyyy HH:mm} · {(detail.Estado == EstadoVenta.COMPLETADA ? "Completada" : "Anulada")} · " +
            $"Subtotal: {Money(detail.SubtotalCentavos)} · Descuentos: {Money(detail.DescuentoCentavos)} · Total: {Money(detail.TotalCentavos)}";
        _linesGrid.Rows.Clear();
        foreach (var line in detail.Lines)
        {
            _linesGrid.Rows.Add(
                line.Producto,
                line.CodigoSku,
                line.Cantidad,
                Money(line.PrecioUnitarioCentavos),
                Money(line.DescuentoCentavos),
                Money(line.TotalCentavos));
        }

        _paymentsGrid.Rows.Clear();
        foreach (var payment in detail.Payments)
        {
            _paymentsGrid.Rows.Add(payment.Metodo, payment.MontoCentavos == 0 && payment.Metodo == "CREDITO"
                ? "—"
                : Money(payment.MontoCentavos));
        }
    }

    private void ClearDetail()
    {
        _linesGrid.Rows.Clear();
        _paymentsGrid.Rows.Clear();
        _detailTitle.Text = "Detalle de venta";
    }

    private static string Money(long cents)
    {
        return $"${cents / 100m:0.00}";
    }
}
