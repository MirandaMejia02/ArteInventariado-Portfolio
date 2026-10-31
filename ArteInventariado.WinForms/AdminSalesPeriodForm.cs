using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using System.Globalization;

namespace ArteInventariado.WinForms;

public sealed class AdminSalesPeriodForm : Form
{
    private readonly ISalesQueryService _salesQueryService;
    private readonly ICurrentSession _currentSession;
    private readonly ComboBox _period = new();
    private readonly Button _refresh = new();
    private readonly DataGridView _salesGrid = new();
    private readonly DataGridView _linesGrid = new();
    private readonly DataGridView _paymentsGrid = new();
    private readonly Label _title = new();
    private readonly Label _completedCount = new();
    private readonly Label _completedTotal = new();
    private readonly Label _statsNote = new();
    private readonly Label _detailTitle = new();
    private readonly Label _detailSummary = new();
    private IReadOnlyList<SalesHistorySummary> _sales = [];
    private bool _ready;
    private bool _loading;
    private bool _suppressSelectionEvent;

    public AdminSalesPeriodForm(ISalesQueryService salesQueryService, ICurrentSession currentSession)
    {
        _salesQueryService = salesQueryService;
        _currentSession = currentSession;
        BuildUi();
        Load += async (_, _) =>
        {
            if (!EnsureAdministrator())
            {
                Close();
                return;
            }

            _ready = true;
            await LoadSalesAsync();
        };
        _period.SelectedIndexChanged += async (_, _) =>
        {
            if (_ready)
                await LoadSalesAsync();
        };
        _salesGrid.SelectionChanged += async (_, _) => await ShowSelectedSaleAsync();
    }

    private void BuildUi()
    {
        Text = "Ventas por período";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 660);
        ClientSize = new Size(1280, 820);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UITheme.SpaceXl),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMetrics(), 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        content.Controls.Add(BuildSalesPanel(), 0, 0);
        content.Controls.Add(BuildDetailPanel(), 1, 0);
        root.Controls.Add(content, 0, 2);

        var close = new Button { Text = "Cerrar", Dock = DockStyle.Right, Width = 120, Margin = Padding.Empty };
        UITheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        root.Controls.Add(close, 0, 3);
        Controls.Add(root);
        AcceptButton = _refresh;
    }

    private Control BuildHeader()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, UITheme.SpaceSm)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _title.Text = "Ventas de hoy";
        _title.Dock = DockStyle.Fill;
        _title.Font = UITheme.TitleFont;
        _title.ForeColor = UITheme.PrimaryColor;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_title, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _refresh.Text = "Actualizar";
        _refresh.Size = new Size(132, 40);
        UITheme.StyleSecondaryButton(_refresh);
        _refresh.Click += async (_, _) => await LoadSalesAsync();
        _period.DropDownStyle = ComboBoxStyle.DropDownList;
        _period.Font = UITheme.NormalFont;
        _period.Width = 176;
        _period.Items.AddRange([
            new PeriodOption(SalesPeriodPreset.Today, "Hoy"),
            new PeriodOption(SalesPeriodPreset.LastSevenDays, "Últimos 7 días"),
            new PeriodOption(SalesPeriodPreset.LastThirtyDays, "Últimos 30 días")
        ]);
        _period.SelectedIndex = 0;
        var periodLabel = new Label
        {
            Text = "Período",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.SecondaryColor,
            Margin = new Padding(0, 8, UITheme.SpaceSm, 0)
        };
        actions.Controls.Add(_refresh);
        actions.Controls.Add(_period);
        actions.Controls.Add(periodLabel);
        layout.Controls.Add(actions, 1, 0);

        var subtitle = new Label
        {
            Text = "Consulta administrativa de solo lectura · incluye ventas de todos los usuarios",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(subtitle, 0, 1);
        layout.SetColumnSpan(subtitle, 2);
        return layout;
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
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
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
        value.MinimumSize = new Size(0, 30);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private Control BuildSalesPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceMd),
            Margin = new Padding(0, 0, UITheme.SpaceSm, 0)
        };
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
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "numero", HeaderText = "Venta", Width = 150 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "fecha", HeaderText = "Fecha / hora", Width = 132 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "usuario", HeaderText = "Usuario", Width = 132 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "estado", HeaderText = "Estado", Width = 100 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "total", HeaderText = "Total", Width = 100 });
        _salesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "metodos", HeaderText = "Método(s)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        layout.Controls.Add(_salesGrid, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildDetailPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceMd),
            Margin = new Padding(UITheme.SpaceSm, 0, 0, 0)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

        _detailTitle.Dock = DockStyle.Fill;
        _detailTitle.Font = UITheme.SubheaderFont;
        _detailTitle.ForeColor = UITheme.PrimaryColor;
        _detailTitle.TextAlign = ContentAlignment.MiddleLeft;
        _detailTitle.AutoEllipsis = true;
        _detailTitle.Margin = Padding.Empty;
        layout.Controls.Add(_detailTitle, 0, 0);

        _detailSummary.Dock = DockStyle.Fill;
        _detailSummary.Font = UITheme.NormalFont;
        _detailSummary.ForeColor = UITheme.TextDark;
        _detailSummary.TextAlign = ContentAlignment.MiddleLeft;
        _detailSummary.AutoEllipsis = true;
        _detailSummary.Margin = Padding.Empty;
        layout.Controls.Add(_detailSummary, 0, 1);

        ConfigureGrid(_linesGrid);
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "producto", HeaderText = "Producto", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 120 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sku", HeaderText = "Código / SKU", Width = 110 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cantidad", HeaderText = "Cant.", Width = 60 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "precio", HeaderText = "Precio", Width = 82 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "descuento", HeaderText = "Desc.", Width = 82 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "total", HeaderText = "Total", Width = 82 });
        layout.Controls.Add(_linesGrid, 0, 2);

        layout.Controls.Add(new Label
        {
            Text = "Métodos de pago",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.SecondaryColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, UITheme.SpaceSm, 0, 0)
        }, 0, 3);

        ConfigureGrid(_paymentsGrid);
        _paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "metodo", HeaderText = "Método", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "monto", HeaderText = "Monto", Width = 120 });
        _paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "referencia", HeaderText = "Referencia", Width = 170 });
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
        if (!EnsureAdministrator())
        {
            Close();
            return;
        }

        if (_period.SelectedItem is not PeriodOption selected)
            return;

        try
        {
            _loading = true;
            _refresh.Enabled = false;
            _period.Enabled = false;
            var (from, through, description) = GetDateRange(selected.Preset, DateOnly.FromDateTime(DateTime.Today));
            _salesGrid.Rows.Clear();
            ClearDetail("Cargando ventas", $"Consultando {description.ToLowerInvariant()}...");
            _completedCount.Text = "—";
            _completedTotal.Text = "—";
            _statsNote.Text = $"Consultando {description.ToLowerInvariant()}...";
            var sales = await _salesQueryService.GetSalesAsync(new SalesHistoryFilter(from, through));
            _title.Text = description switch
            {
                "Hoy" => "Ventas de hoy",
                "Últimos 7 días" => "Ventas de los últimos 7 días",
                _ => "Ventas de los últimos 30 días"
            };
            Text = _title.Text;
            _sales = sales;

            var completed = _sales.Where(sale => sale.Estado == EstadoVenta.COMPLETADA).ToList();
            _completedCount.Text = completed.Count.ToString(CultureInfo.InvariantCulture);
            _completedTotal.Text = Money(completed.Sum(sale => sale.TotalCentavos));
            var cancelled = _sales.Count - completed.Count;
            _statsNote.Text = $"Anuladas: {cancelled} · {description} · Actualizado: {DateTime.Now:HH:mm}. El total solo suma ventas completadas.";

            _salesGrid.Rows.Clear();
            foreach (var sale in _sales)
            {
                var index = _salesGrid.Rows.Add(
                    sale.NumeroVenta,
                    ToLocal(sale.FechaVenta).ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture),
                    sale.Usuario,
                    sale.Estado == EstadoVenta.COMPLETADA ? "Completada" : "Anulada",
                    Money(sale.TotalCentavos),
                    sale.MetodosPago);
                _salesGrid.Rows[index].Tag = sale;
            }

            _detailTitle.Text = _sales.Count == 0 ? "Sin ventas en este período" : "Detalle de venta";
            _detailSummary.Text = _sales.Count == 0 ? "No se encontraron ventas para el período seleccionado." : "Selecciona una venta de la lista.";
            if (_salesGrid.Rows.Count > 0)
            {
                _loading = false;
                _suppressSelectionEvent = true;
                _salesGrid.ClearSelection();
                _salesGrid.Rows[0].Selected = true;
                _salesGrid.CurrentCell = _salesGrid.Rows[0].Cells[0];
                _suppressSelectionEvent = false;
                await ShowSelectedSaleAsync();
            }
            else
            {
                ClearDetail("Sin ventas en este período", "No se encontraron ventas para el período seleccionado.");
            }
        }
        catch (Exception ex)
        {
            _statsNote.Text = "No se pudo cargar el período seleccionado. Actualiza para volver a intentarlo.";
            MessageBox.Show(ex.Message, "Ventas por período", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _loading = false;
            _refresh.Enabled = true;
            _period.Enabled = true;
        }
    }

    private async Task ShowSelectedSaleAsync()
    {
        if (_loading || _suppressSelectionEvent || !EnsureAdministrator() || _salesGrid.CurrentRow?.Tag is not SalesHistorySummary sale)
            return;

        try
        {
            var detail = await _salesQueryService.GetSaleDetailAsync(sale.VentaId);
            if (detail is null)
            {
                ClearDetail();
                return;
            }

            _detailTitle.Text = $"Venta {detail.NumeroVenta}";
            _detailSummary.Text =
                $"{ToLocal(detail.FechaVenta):dd/MM/yyyy HH:mm} · {(sale.Estado == EstadoVenta.COMPLETADA ? "Completada" : "Anulada")} · " +
                $"Usuario: {detail.Usuario} · Cliente: {detail.Cliente}{Environment.NewLine}" +
                $"Subtotal: {Money(detail.SubtotalCentavos)} · Descuento: {Money(detail.DescuentoCentavos)} · Total: {Money(detail.TotalCentavos)}";
            _linesGrid.Rows.Clear();
            foreach (var line in detail.Lines)
            {
                _linesGrid.Rows.Add(
                    line.Producto,
                    line.Codigo,
                    line.Cantidad,
                    Money(line.PrecioUnitarioCentavos),
                    Money(line.DescuentoCentavos),
                    Money(line.TotalCentavos));
            }

            _paymentsGrid.Rows.Clear();
            foreach (var payment in detail.Payments)
            {
                _paymentsGrid.Rows.Add(
                    payment.MetodoPago.ToString(),
                    Money(payment.MontoCentavos),
                    payment.Referencia ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Detalle de venta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool EnsureAdministrator()
    {
        if (_currentSession.EsAdmin)
            return true;

        MessageBox.Show("Esta consulta está disponible solo para administradores.", "Ventas por período",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void ClearDetail(string title = "Detalle de venta", string summary = "")
    {
        _linesGrid.Rows.Clear();
        _paymentsGrid.Rows.Clear();
        _detailTitle.Text = title;
        _detailSummary.Text = summary;
    }

    private static (DateOnly From, DateOnly Through, string Description) GetDateRange(
        SalesPeriodPreset preset,
        DateOnly today)
    {
        return preset switch
        {
            SalesPeriodPreset.Today => (today, today, "Hoy"),
            SalesPeriodPreset.LastSevenDays => (today.AddDays(-6), today, "Últimos 7 días"),
            SalesPeriodPreset.LastThirtyDays => (today.AddDays(-29), today, "Últimos 30 días"),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
    }

    private static DateTime ToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => value,
            DateTimeKind.Utc => value.ToLocalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
        };
    }

    private static string Money(long cents) => $"${cents / 100m:0.00}";

    private enum SalesPeriodPreset
    {
        Today,
        LastSevenDays,
        LastThirtyDays
    }

    private sealed record PeriodOption(SalesPeriodPreset Preset, string Label)
    {
        public override string ToString() => Label;
    }
}
