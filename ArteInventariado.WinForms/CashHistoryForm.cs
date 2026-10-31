using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CashHistoryForm : Form
{
    private readonly ICashService _cashService;
    private readonly bool _showAdminCashSummary;
    private readonly BindingSource _binding = new();
    private readonly BindingSource _dailyBinding = new();
    private readonly ComboBox _period = new();
    private readonly DateTimePicker _from = new();
    private readonly DateTimePicker _to = new();
    private readonly Button _search = new();
    private readonly Button _detail = new();
    private readonly Button _close = new();
    private readonly Label _status = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _dailyGrid = new();
    private readonly TabControl _tabs = new();
    private readonly Label _yesterdayExpectedAmount = new() { Text = "—" };
    private readonly Label _yesterdayExpectedContext = new();
    private readonly Label _todayExpectedAmount = new() { Text = "—" };
    private readonly Label _todayExpectedContext = new();

    public CashHistoryForm(ICashService cashService, bool showAdminCashSummary = false)
    {
        _cashService = cashService;
        _showAdminCashSummary = showAdminCashSummary;
        Text = "Historial de caja";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1480, 740);
        MinimumSize = new Size(1120, 620);
        MaximizeBox = true;
        UITheme.StyleForm(this);
        BuildUi();
        Load += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, _showAdminCashSummary ? 108 : 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildFilters(), 0, 1);
        root.Controls.Add(BuildGrid(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = _showAdminCashSummary ? 3 : 1,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = _showAdminCashSummary
                ? new Padding(14, 0, 12, 0)
                : new Padding(14, 4, 12, 4)
        };

        if (_showAdminCashSummary)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        }
        else
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }

        panel.Controls.Add(BuildHeaderText(), 0, 0);
        if (_showAdminCashSummary)
        {
            panel.Controls.Add(BuildExpectedCashCard(
                "Último cierre contado de ayer",
                _yesterdayExpectedAmount,
                _yesterdayExpectedContext,
                UITheme.PrimaryColor), 1, 0);
            panel.Controls.Add(BuildExpectedCashCard(
                "Efectivo teórico hoy",
                _todayExpectedAmount,
                _todayExpectedContext,
                UITheme.SuccessColor), 2, 0);
        }

        return panel;
    }

    private static Control BuildHeaderText()
    {
        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "Historial de cierres de caja",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "Consulta de solo lectura. Los movimientos se acumulan; cada cierre físico queda registrado por sesión.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.SubheaderFont,
            AutoEllipsis = true,
            Margin = Padding.Empty
        };
        heading.Controls.Add(title, 0, 0);
        heading.Controls.Add(subtitle, 0, 1);
        return heading;
    }

    private static Control BuildExpectedCashCard(
        string title,
        Label amount,
        Label context,
        Color valueColor)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UITheme.SurfaceMutedColor,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(4, 0, 4, 0),
            AccessibleName = title
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true,
            Margin = Padding.Empty
        }, 0, 0);

        amount.Dock = DockStyle.Fill;
        amount.TextAlign = ContentAlignment.MiddleLeft;
        amount.Font = UITheme.HeaderFont;
        amount.ForeColor = valueColor;
        amount.AccessibleName = $"{title}: monto";
        amount.Margin = Padding.Empty;
        card.Controls.Add(amount, 0, 1);

        context.Dock = DockStyle.Fill;
        context.TextAlign = ContentAlignment.MiddleLeft;
        context.Font = UITheme.CaptionFont;
        context.ForeColor = UITheme.MutedTextColor;
        context.AutoEllipsis = true;
        context.Margin = Padding.Empty;
        card.Controls.Add(context, 0, 2);
        return card;
    }

    private Control BuildFilters()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12, 8, 12, 6),
            Margin = new Padding(0, 8, 0, 8)
        };
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            AutoScroll = false
        };
        _period.DropDownStyle = ComboBoxStyle.DropDownList;
        _period.Width = 190;
        _period.Items.AddRange([
            new PeriodOption(7, "Últimos 7 días"),
            new PeriodOption(15, "Últimos 15 días"),
            new PeriodOption(30, "Últimos 30 días"),
            new PeriodOption(null, "Fechas personalizadas")
        ]);
        _period.SelectedIndexChanged += (_, _) => ApplyPeriodSelection();
        _period.SelectedIndex = 0;
        ConfigureDatePicker(_from);
        ConfigureDatePicker(_to);
        filters.Controls.Add(BuildFilter("Período", _period));
        filters.Controls.Add(BuildFilter("Desde", _from));
        filters.Controls.Add(BuildFilter("Hasta", _to));

        _search.Text = "Buscar";
        _search.Size = new Size(122, 36);
        _search.Margin = new Padding(10, 20, 4, 0);
        _search.Click += async (_, _) => await RefreshAsync();
        UITheme.StylePrimaryButton(_search);
        filters.Controls.Add(_search);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Text = "El efectivo contado corresponde al monto registrado al cerrar cada sesión.";
        container.Controls.Add(filters, 0, 0);
        container.Controls.Add(_status, 0, 1);
        return container;
    }

    private Control BuildGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ColumnHeadersHeight = 50;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateDetailButton();
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) ShowSelectedDetail();
        };
        _grid.ShowCellToolTips = true;
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                e.ToolTipText = _grid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? string.Empty;
            }
        };

        AddColumn(nameof(CashHistoryRow.Fecha), "Fecha", 100);
        AddColumn(nameof(CashHistoryRow.Caja), "Caja", 65);
        AddColumn(nameof(CashHistoryRow.Estado), "Estado", 90);
        AddColumn(nameof(CashHistoryRow.Apertura), "Apertura", 105);
        AddColumn(nameof(CashHistoryRow.Entradas), "Entradas", 105);
        AddColumn(nameof(CashHistoryRow.Salidas), "Salidas", 105);
        AddColumn(nameof(CashHistoryRow.Esperado), "Efectivo esperado", 125);
        AddColumn(nameof(CashHistoryRow.Contado), "Efectivo contado", 125);
        AddColumn(nameof(CashHistoryRow.Diferencia), "Diferencia", 105);
        AddColumn(nameof(CashHistoryRow.HoraApertura), "Hora apertura", 130);
        AddColumn(nameof(CashHistoryRow.HoraCierre), "Hora cierre", 130);
        AddColumn(nameof(CashHistoryRow.UsuarioApertura), "Abrió", 125);
        AddColumn(nameof(CashHistoryRow.UsuarioCierre), "Cerró", 125);
        ConfigureDailyGrid();
        var sessionsPage = new TabPage("Sesiones") { BackColor = UITheme.BackgroundColor, Padding = new Padding(4) };
        sessionsPage.Controls.Add(_grid);
        var dailyPage = new TabPage("Resumen por día") { BackColor = UITheme.BackgroundColor, Padding = new Padding(4) };
        dailyPage.Controls.Add(_dailyGrid);
        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.AddRange([sessionsPage, dailyPage]);
        _tabs.SelectedIndexChanged += (_, _) => UpdateDetailButton();
        _dailyGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _dailyGrid.Rows[e.RowIndex].DataBoundItem is CashDailySummaryRow row)
            {
                _period.SelectedIndex = 3;
                _from.Value = row.Summary.FechaOperacion.ToDateTime(TimeOnly.MinValue);
                _to.Value = _from.Value;
                await RefreshAsync();
                _tabs.SelectedIndex = 0;
                if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
            }
        };
        return _tabs;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _detail.Text = "Ver detalle";
        _detail.Size = new Size(140, 36);
        _detail.Enabled = false;
        _detail.Click += (_, _) => ShowSelectedDetail();
        _detail.AccessibleName = "Ver detalle de la sesión seleccionada";
        _detail.AccessibleDescription = "Muestra los importes registrados de la sesión en modo de solo lectura.";
        UITheme.StylePrimaryButton(_detail);
        _close.Text = "Cerrar";
        _close.Size = new Size(120, 36);
        _close.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(_close);
        actions.Controls.AddRange([_close, _detail]);
        footer.Controls.Add(new Label
        {
            Text = "Sesiones conserva cada cierre. El resumen diario acumula movimientos y ajustes de reapertura, pero muestra solo el último cierre contado.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor
        }, 0, 0);
        footer.Controls.Add(actions, 1, 0);
        return footer;
    }

    private CashHistoryRow? GetSelectedSession() =>
        _grid.CurrentRow?.DataBoundItem as CashHistoryRow;

    private void ShowSelectedDetail()
    {
        var session = GetSelectedSession();
        if (session is null) return;

        using var detail = new CashSessionDetailForm(session.Summary);
        detail.ShowDialog(this);
    }

    private async Task RefreshAsync()
    {
        var fromDate = DateOnly.FromDateTime(_from.Value.Date);
        var toDate = DateOnly.FromDateTime(_to.Value.Date);
        if (fromDate > toDate)
        {
            MessageBox.Show(this, "La fecha inicial no puede ser posterior a la fecha final.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var sessions = await _cashService.GetSessionHistoryAsync(fromDate, toDate);
            _binding.DataSource = sessions.Select(CashHistoryRow.From).ToList();
            _dailyBinding.DataSource = CashDailySummaryCalculator.Aggregate(sessions)
                .Select(CashDailySummaryRow.From)
                .ToList();

            if (_showAdminCashSummary)
            {
                var todayDate = DateOnly.FromDateTime(DateTime.Today);
                var yesterdayDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
                var yesterdaySessions = fromDate <= yesterdayDate && yesterdayDate <= toDate
                    ? sessions.Where(session => session.FechaOperacion == yesterdayDate).ToList()
                    : await _cashService.GetSessionHistoryAsync(yesterdayDate, yesterdayDate);
                var yesterday = CashDailySummaryCalculator.Aggregate(yesterdaySessions).SingleOrDefault();
                var todaySessions = fromDate <= todayDate && todayDate <= toDate
                    ? sessions.Where(session => session.FechaOperacion == todayDate).ToList()
                    : await _cashService.GetSessionHistoryAsync(todayDate, todayDate);
                var today = CashDailySummaryCalculator.Aggregate(todaySessions).SingleOrDefault();
                UpdateAdminCashSummary(yesterday, today, yesterdayDate);
            }

            _status.Text = $"{sessions.Count} sesión(es) entre {fromDate:dd/MM/yyyy} y {toDate:dd/MM/yyyy}. " +
                           "El saldo de arrastre usa el último conteo físico; los conteos de sesión no se suman entre sí.";
        }
        catch (InvalidOperationException ex)
        {
            TechnicalErrorLogger.Log("cash-history", ex);
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("cash-history", ex);
            MessageBox.Show(this, $"No se pudo consultar el historial de caja:\r\n\r\n{ex.Message}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateAdminCashSummary(CashDailySummary? yesterday, CashDailySummary? today, DateOnly yesterdayDate)
    {
        UpdateLastPhysicalCloseCard(yesterday, _yesterdayExpectedAmount, _yesterdayExpectedContext,
            $"Sin sesiones · {yesterdayDate:dd/MM/yyyy}");
        UpdateTodayExpectedCashCard(today, _todayExpectedAmount, _todayExpectedContext,
            $"Sin sesiones · {DateOnly.FromDateTime(DateTime.Today):dd/MM/yyyy}");
    }

    private static void UpdateLastPhysicalCloseCard(
        CashDailySummary? daily,
        Label amount,
        Label context,
        string emptyContext)
    {
        if (daily is null)
        {
            amount.Text = "—";
            context.Text = emptyContext;
            return;
        }

        if (!daily.UltimoCierreContadoCentavos.HasValue)
        {
            amount.Text = "Pendiente";
            context.Text = $"{daily.CantidadSesiones} sesiones · falta el conteo final.";
            return;
        }

        amount.Text = Money(daily.UltimoCierreContadoCentavos.Value);
        context.Text = $"Último conteo físico · {daily.CantidadSesiones} sesión(es)";
    }

    private static void UpdateTodayExpectedCashCard(
        CashDailySummary? daily,
        Label amount,
        Label context,
        string emptyContext)
    {
        if (daily is null)
        {
            amount.Text = "—";
            context.Text = emptyContext;
            return;
        }

        if (daily.TieneCierreIntermedioSinConteo || !daily.EfectivoEsperadoAcumuladoCentavos.HasValue)
        {
            amount.Text = "No calculable";
            context.Text = "Falta un conteo intermedio.";
            return;
        }

        amount.Text = Money(daily.EfectivoEsperadoAcumuladoCentavos.Value);
        context.Text = $"Saldo teórico acumulado · {daily.CantidadSesiones} sesión(es)";
    }

    private void ApplyPeriodSelection()
    {
        if (_period.SelectedItem is not PeriodOption option) return;
        var isCustom = !option.Days.HasValue;
        _from.Enabled = isCustom;
        _to.Enabled = isCustom;
        if (isCustom) return;

        var today = DateTime.Today;
        _to.Value = today;
        _from.Value = today.AddDays(1 - option.Days!.Value);
    }

    private void SetBusy(bool busy)
    {
        _search.Enabled = !busy;
        _period.Enabled = !busy;
        _from.Enabled = !busy && _period.SelectedItem is PeriodOption { Days: null };
        _to.Enabled = !busy && _period.SelectedItem is PeriodOption { Days: null };
        UseWaitCursor = busy;
    }

    private void ConfigureDailyGrid()
    {
        UITheme.StyleDataGridView(_dailyGrid);
        _dailyGrid.Dock = DockStyle.Fill;
        _dailyGrid.AutoGenerateColumns = false;
        _dailyGrid.AllowUserToAddRows = false;
        _dailyGrid.AllowUserToDeleteRows = false;
        _dailyGrid.AllowUserToResizeRows = false;
        _dailyGrid.ReadOnly = true;
        _dailyGrid.RowHeadersVisible = false;
        _dailyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dailyGrid.MultiSelect = false;
        _dailyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dailyGrid.ColumnHeadersHeight = 50;
        _dailyGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _dailyGrid.DataSource = _dailyBinding;
        AddDailyColumn(nameof(CashDailySummaryRow.Fecha), "Fecha operativa", 105);
        AddDailyColumn(nameof(CashDailySummaryRow.Sesiones), "Sesiones", 70);
        AddDailyColumn(nameof(CashDailySummaryRow.AperturaInicial), "Apertura inicial", 115);
        AddDailyColumn(nameof(CashDailySummaryRow.Entradas), "Entradas físicas", 115);
        AddDailyColumn(nameof(CashDailySummaryRow.Salidas), "Salidas físicas", 115);
        AddDailyColumn(nameof(CashDailySummaryRow.Ajustes), "Ajustes de reapertura", 135);
        AddDailyColumn(nameof(CashDailySummaryRow.Esperado), "Esperado acumulado", 135);
        AddDailyColumn(nameof(CashDailySummaryRow.SumaSaldos), "Suma saldos sesión (ref.)", 145);
        AddDailyColumn(nameof(CashDailySummaryRow.UltimoCierre), "Último cierre contado", 140);
        AddDailyColumn(nameof(CashDailySummaryRow.Diferencia), "Diferencia final", 115);
        _dailyGrid.ShowCellToolTips = true;
        _dailyGrid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                e.ToolTipText = _dailyGrid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? string.Empty;
        };
    }

    private void AddDailyColumn(string property, string header, int width) =>
        _dailyGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            FillWeight = width,
            MinimumWidth = Math.Min(width, 70),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private void UpdateDetailButton() =>
        _detail.Enabled = _tabs.SelectedIndex == 0 && GetSelectedSession() is not null;

    private void AddColumn(string property, string header, int width) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            FillWeight = width,
            MinimumWidth = Math.Min(width, 70),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private static Control BuildFilter(string title, Control control)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 14, 0)
        };
        group.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        group.Controls.Add(control);
        return group;
    }

    private static void ConfigureDatePicker(DateTimePicker picker)
    {
        picker.Width = 150;
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "dd/MM/yyyy";
        picker.Value = DateTime.Today;
    }

    private static string Money(long centavos) => $"$ {(centavos / 100m):0.00}";
    private static string OptionalMoney(long? centavos) => centavos.HasValue ? Money(centavos.Value) : "—";
    private static string Time(DateTime? timestamp)
    {
        if (!timestamp.HasValue) return "—";
        var local = timestamp.Value.Kind == DateTimeKind.Utc ? timestamp.Value.ToLocalTime() : timestamp.Value;
        return local.ToString("dd/MM/yyyy HH:mm");
    }

    private sealed record PeriodOption(int? Days, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record CashHistoryRow(
        string Fecha,
        string Caja,
        string Estado,
        string Apertura,
        string Entradas,
        string Salidas,
        string Esperado,
        string Contado,
        string Diferencia,
        string HoraApertura,
        string HoraCierre,
        string UsuarioApertura,
        string UsuarioCierre,
        CashSummary Summary)
    {
        public static CashHistoryRow From(CashSummary summary)
        {
            var entradas = summary.VentasEfectivoCentavos +
                           summary.AbonosClientesEfectivoCentavos +
                           summary.AnticiposPedidosEfectivoCentavos +
                           summary.AbonosPedidosEfectivoCentavos +
                           summary.PagosFinalesPedidosEfectivoCentavos +
                           summary.TotalAportesCajaCentavos;
            var salidas = summary.TotalReembolsosCentavos +
                          summary.TotalPagosProveedoresCentavos +
                          summary.TotalPagosEmpleadosCentavos +
                          summary.TotalGastosCentavos +
                          summary.TotalRetirosCentavos;

            return new CashHistoryRow(
                summary.FechaOperacion.ToString("dd/MM/yyyy"),
                summary.CajaId?.ToString() ?? "—",
                summary.Estado?.ToString() ?? "—",
                Money(summary.MontoInicialCentavos),
                Money(entradas),
                Money(salidas),
                Money(summary.TotalEsperadoCentavos),
                OptionalMoney(summary.MontoCierreCentavos),
                OptionalMoney(summary.DiferenciaCentavos),
                Time(summary.FechaApertura),
                Time(summary.FechaCierre),
                summary.UsuarioApertura ?? "—",
                summary.UsuarioCierre ?? "—",
                summary);
        }
    }

    private sealed record CashDailySummaryRow(
        string Fecha,
        int Sesiones,
        string AperturaInicial,
        string Entradas,
        string Salidas,
        string Ajustes,
        string Esperado,
        string SumaSaldos,
        string UltimoCierre,
        string Diferencia,
        CashDailySummary Summary)
    {
        public static CashDailySummaryRow From(CashDailySummary summary) => new(
            summary.FechaOperacion.ToString("dd/MM/yyyy"),
            summary.CantidadSesiones,
            Money(summary.AperturaInicialCentavos),
            Money(summary.EntradasFisicasCentavos),
            Money(summary.SalidasFisicasCentavos),
            summary.AjustesEntreSesionesCentavos.HasValue
                ? Money(summary.AjustesEntreSesionesCentavos.Value)
                : "No calculable",
            summary.EfectivoEsperadoAcumuladoCentavos.HasValue
                ? Money(summary.EfectivoEsperadoAcumuladoCentavos.Value)
                : "No calculable",
            OptionalMoney(summary.SumaSaldosPorSesionCentavos),
            OptionalMoney(summary.UltimoCierreContadoCentavos),
            OptionalMoney(summary.DiferenciaFinalCentavos),
            summary);
    }

}
