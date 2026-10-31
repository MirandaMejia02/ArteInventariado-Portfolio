using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms.Controls;

internal sealed class AdvancedReportsControl : UserControl
{
    private const int ReportPageSize = 200;
    private readonly IAdvancedReportQueryService _query;
    private readonly IAdvancedReportExportService _export;
    private readonly IAdvancedReportFilterOptionsService _filterOptions;
    private readonly ICurrentSession _session;
    private readonly AnalyticsPeriodSelectorControl _period = new();
    private readonly ComboBox _group = new();
    private readonly ComboBox _report = new();
    private readonly Label _description = new();
    private readonly TextBox _search = new();
    private readonly Button _clearSearch = new();
    private readonly ComboBox _state = new();
    private readonly ComboBox _paymentMethod = new();
    private readonly ComboBox _movementType = new();
    private readonly ComboBox _specificFilter = new();
    private readonly ComboBox _specificValue = new();
    private readonly FlowLayoutPanel _summary = new();
    private readonly DataGridView _grid = new();
    private readonly Label _emptyState = new();
    private readonly Label _status = new();
    private readonly Button _load = new();
    private readonly Button _cancel = new();
    private readonly Button _exportExcel = new();
    private readonly Button _print = new();
    private readonly Button _previous = new();
    private readonly Button _next = new();
    private readonly Control _stateField;
    private readonly Control _paymentField;
    private readonly Control _movementField;
    private readonly Control _specificField;
    private readonly System.Windows.Forms.Timer _optionSearchTimer = new() { Interval = 300 };
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _optionCts;
    private AdvancedReportResult? _current;
    private int _page = 1;
    private bool _initialized;
    private bool _bindingFilters;
    private LiveSearchController? _liveSearch;

    public AdvancedReportsControl(
        IAdvancedReportQueryService query,
        IAdvancedReportExportService export,
        IAdvancedReportFilterOptionsService filterOptions,
        ICurrentSession session)
    {
        _query = query;
        _export = export;
        _filterOptions = filterOptions;
        _session = session;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Dock = DockStyle.Fill;
        BackColor = UITheme.DashboardBackgroundColor;
        Padding = new Padding(8);
        _stateField = Field("Estado", _state, 150);
        _paymentField = Field("Método de pago", _paymentMethod, 145);
        _movementField = Field("Tipo movimiento", _movementType, 150);
        _specificField = BuildSpecificField();
        BuildUi();
        BindCatalog();
        _liveSearch = new LiveSearchController(
            _search,
            _clearSearch,
            RefreshSearchAsync,
            ShowLiveSearchError);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _optionCts?.Cancel();
            _optionCts?.Dispose();
            _optionSearchTimer.Dispose();
            _liveSearch?.Dispose();
        }
        base.Dispose(disposing);
    }

    public async Task InitializeAsync()
    {
        if (_initialized || !_session.EsAdmin)
        {
            return;
        }
        _initialized = true;
        await RefreshAsync(1);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = UITheme.DashboardBackgroundColor,
            Margin = Padding.Empty,
            MinimumSize = Size.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        // Summary cards wrap according to the available width.  Let the row
        // grow with the wrapped content so a valuation report with several
        // metrics never clips the last card behind a fixed-height row.
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // The action buttons reserve 44 logical pixels plus the row's top
        // padding.  A 46px row left their lower glyphs outside the
        // FlowLayoutPanel at 100% DPI, so keep a small responsive gutter.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var catalog = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10, 6, 10, 5)
        };
        catalog.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        catalog.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        catalog.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        catalog.Controls.Add(Field("Grupo", _group, 205), 0, 0);
        catalog.Controls.Add(Field("Reporte", _report, 265), 1, 0);
        _description.Dock = DockStyle.Fill;
        _description.TextAlign = ContentAlignment.MiddleLeft;
        _description.Font = new Font("Segoe UI", 9f);
        _description.ForeColor = UITheme.MutedTextColor;
        _description.Padding = new Padding(10, 17, 4, 0);
        _description.AutoEllipsis = true;
        catalog.Controls.Add(_description, 2, 0);

        _period.Dock = DockStyle.Fill;
        _period.RefreshRequested += async (_, _) => await RefreshFromUserAsync(1);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoSize = false,
            AutoScroll = false,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10, 2, 10, 2)
        };
        _search.PlaceholderText = "Buscar en los campos del reporte";
        UITheme.StyleTextBox(_search);
        _search.TextChanged += (_, _) => MarkFiltersChanged();
        ConfigureClearSearchButton(_clearSearch);
        var searchField = BuildSearchField();
        filters.Controls.Add(searchField);
        filters.Controls.Add(_stateField);
        filters.Controls.Add(_paymentField);
        filters.Controls.Add(_movementField);
        filters.Controls.Add(_specificField);
        _load.Text = "Preparar reporte";
        ConfigureActionButton(_load, 184);
        _load.Margin = new Padding(8, 0, 6, 0);
        UITheme.StylePrimaryButton(_load);
        _load.Click += async (_, _) => await RefreshFromUserAsync(1);
        _cancel.Text = "Cancelar";
        ConfigureActionButton(_cancel, 124);
        _cancel.Margin = new Padding(0, 0, 0, 0);
        _cancel.Enabled = false;
        UITheme.StyleDangerButton(_cancel);
        _cancel.Click += (_, _) => _operationCts?.Cancel();
        filters.Controls.AddRange([_load, _cancel]);
        filters.SetFlowBreak(_specificField, true);
        filters.SetFlowBreak(_cancel, true);

        _summary.Dock = DockStyle.Fill;
        _summary.WrapContents = true;
        _summary.AutoSize = true;
        _summary.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _summary.AutoScroll = false;
        _summary.BackColor = UITheme.DashboardAccentSoftColor;
        _summary.Padding = new Padding(8, 2, 8, 0);
        _summary.Resize += (_, _) => ArrangeSummaryCards();

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Font = new Font("Segoe UI", 8.8f);
        _status.Padding = new Padding(8, 0, 0, 0);

        ConfigureGrid();
        var tableHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(1),
            MinimumSize = Size.Empty
        };
        _emptyState.Name = "AdvancedReportsEmptyState";
        _emptyState.Text = "Seleccione filtros y prepare el reporte.";
        _emptyState.Dock = DockStyle.Fill;
        _emptyState.TextAlign = ContentAlignment.MiddleCenter;
        _emptyState.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _emptyState.ForeColor = UITheme.MutedTextColor;
        _emptyState.BackColor = UITheme.SurfaceColor;
        tableHost.Controls.Add(_grid);
        tableHost.Controls.Add(_emptyState);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 5, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _exportExcel.Text = "Exportar Excel";
        _print.Text = "Vista previa / Imprimir";
        ConfigureActionButton(_exportExcel, 164);
        ConfigureActionButton(_print, 218);
        UITheme.StylePrimaryButton(_exportExcel);
        UITheme.StyleSecondaryButton(_print);
        _exportExcel.Click += async (_, _) => await ExportAsync();
        _print.Click += async (_, _) => await PrintAsync();
        left.Controls.AddRange([_exportExcel, _print]);
        var paging = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        _previous.Text = "← Anterior";
        _next.Text = "Siguiente →";
        ConfigureActionButton(_previous, 128);
        ConfigureActionButton(_next, 128);
        UITheme.StyleSecondaryButton(_previous);
        UITheme.StyleSecondaryButton(_next);
        _previous.Click += async (_, _) => await RefreshFromUserAsync(Math.Max(1, _page - 1));
        _next.Click += async (_, _) => await RefreshFromUserAsync(_page + 1);
        paging.Controls.AddRange([_previous, _next]);
        actions.Controls.Add(left, 0, 0);
        actions.Controls.Add(paging, 1, 0);

        root.Controls.Add(catalog, 0, 0);
        root.Controls.Add(_period, 0, 1);
        root.Controls.Add(filters, 0, 2);
        root.Controls.Add(_summary, 0, 3);
        root.Controls.Add(_status, 0, 4);
        root.Controls.Add(tableHost, 0, 5);
        root.Controls.Add(actions, 0, 6);
        Controls.Add(root);
        SetBusy(false);
    }

    private void BindCatalog()
    {
        _group.DropDownStyle = ComboBoxStyle.DropDownList;
        _report.DropDownStyle = ComboBoxStyle.DropDownList;
        _group.DataSource = Enum.GetValues<AdvancedReportGroup>()
            .Select(value => new GroupOption(value, GroupName(value))).ToList();
        _group.DisplayMember = nameof(GroupOption.Name);
        _group.ValueMember = nameof(GroupOption.Value);
        _group.SelectedValueChanged += (_, _) => BindReports();
        _report.SelectedValueChanged += (_, _) => ConfigureDefinition();
        _specificFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _specificFilter.SelectedValueChanged += async (_, _) =>
        {
            MarkFiltersChanged();
            await LoadSpecificOptionsAsync();
        };
        _specificValue.DropDownStyle = ComboBoxStyle.DropDown;
        _specificValue.AutoCompleteMode = AutoCompleteMode.None;
        _specificValue.TextUpdate += (_, _) =>
        {
            MarkFiltersChanged();
            _optionSearchTimer.Stop();
            _optionSearchTimer.Start();
        };
        _specificValue.SelectedValueChanged += (_, _) => MarkFiltersChanged();
        _optionSearchTimer.Tick += async (_, _) =>
        {
            _optionSearchTimer.Stop();
            await LoadSpecificOptionsAsync(_specificValue.Text);
        };
        ConfigureSimpleCombo(_paymentMethod, ["Todos", .. Enum.GetNames<MetodoPago>()]);
        ConfigureSimpleCombo(_movementType, ["Todos", .. Enum.GetNames<TipoMovimientoInventario>()]);
        _state.SelectedValueChanged += (_, _) => MarkFiltersChanged();
        _paymentMethod.SelectedValueChanged += (_, _) => MarkFiltersChanged();
        _movementType.SelectedValueChanged += (_, _) => MarkFiltersChanged();
        BindReports();
    }

    private void BindReports()
    {
        if (_group.SelectedValue is not AdvancedReportGroup group)
        {
            return;
        }
        var reports = AdvancedReportCatalog.All.Where(item => item.Group == group)
            .Select(item => new ReportOption(item.Kind, item.Name)).ToList();
        _report.DataSource = reports;
        _report.DisplayMember = nameof(ReportOption.Name);
        _report.ValueMember = nameof(ReportOption.Value);
        ConfigureDefinition();
    }

    private void ConfigureDefinition()
    {
        if (_report.SelectedValue is not AdvancedReportKind kind)
        {
            return;
        }
        var definition = AdvancedReportCatalog.Get(kind);
        _bindingFilters = true;
        if (kind == AdvancedReportKind.CurrentInventoryValue &&
            _period.SelectedKind != AnalyticsPeriodKind.AllHistory)
        {
            _period.SetKind(AnalyticsPeriodKind.AllHistory);
        }
        _description.Text = definition.Description;
        _search.Enabled = definition.Filters.Contains(AdvancedReportFilterKind.Search);
        _search.Text = string.Empty;
        _search.PlaceholderText = definition.SearchableFields.Count == 0
            ? "Este reporte no requiere búsqueda textual"
            : $"Buscar por {string.Join(", ", definition.SearchableFields).ToLowerInvariant()}";
        _stateField.Visible = definition.Filters.Contains(AdvancedReportFilterKind.State);
        _paymentField.Visible = definition.Filters.Contains(AdvancedReportFilterKind.PaymentMethod);
        _movementField.Visible = definition.Filters.Contains(AdvancedReportFilterKind.MovementType);
        var specific = definition.Filters.Where(filter => filter is AdvancedReportFilterKind.Customer or
            AdvancedReportFilterKind.Product or AdvancedReportFilterKind.Category or AdvancedReportFilterKind.Subcategory or AdvancedReportFilterKind.Supplier or
            AdvancedReportFilterKind.User or AdvancedReportFilterKind.CashRegister)
            .Select(filter => new FilterOption(filter, FilterName(filter))).ToList();
        _specificField.Visible = specific.Count > 0;
        _specificFilter.DataSource = specific;
        _specificFilter.DisplayMember = nameof(FilterOption.Name);
        _specificFilter.ValueMember = nameof(FilterOption.Value);
        _specificValue.DataSource = null;
        _specificValue.Text = string.Empty;
        ConfigureSimpleCombo(_state, ["Todos", .. StateNames(kind)]);
        _bindingFilters = false;
        MarkFiltersChanged();
        _ = LoadSpecificOptionsAsync();
    }

    private async Task RefreshAsync(int page, CancellationToken cancellationToken = default)
    {
        if (_report.SelectedValue is not AdvancedReportKind)
        {
            return;
        }
        BeginOperation("Preparando reporte...", cancellationToken);
        try
        {
            var token = _operationCts!.Token;
            var result = await _query.QueryAsync(BuildRequest(page), token);
            if (page > 1 && result.TotalRows > 0 && result.Rows.Count == 0)
            {
                page = 1;
                result = await _query.QueryAsync(BuildRequest(page), token);
            }
            token.ThrowIfCancellationRequested();
            _page = page;
            _current = result;
            Render(result);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Operación cancelada.";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo preparar el reporte: {ex.Message}";
            _status.ForeColor = UITheme.DangerColor;
        }
        finally
        {
            EndOperation();
        }
    }

    private void Render(AdvancedReportResult report)
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        foreach (var column in report.Columns)
        {
            var gridColumn = new DataGridViewTextBoxColumn
            {
                Name = column.Key,
                HeaderText = column.Title,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MinimumWidth = 72
            };
            gridColumn.DefaultCellStyle.Format = column.ValueKind switch
            {
                AdvancedReportValueKind.Money => "$#,##0.00",
                AdvancedReportValueKind.Percentage => "0.00'%'",
                AdvancedReportValueKind.Integer => "N0",
                AdvancedReportValueKind.Date => "dd/MM/yyyy",
                AdvancedReportValueKind.DateTime => "dd/MM/yyyy HH:mm",
                _ => null
            };
            ConfigureColumnLayout(gridColumn, column);
            _grid.Columns.Add(gridColumn);
        }
        ConfigureSubcategoryColumnLayout(_grid);
        foreach (var row in report.Rows)
        {
            _grid.Rows.Add(report.Columns
                .Select(column => DisplayValue(row[column.Key], column.ValueKind) ?? string.Empty)
                .Cast<object>()
                .ToArray());
        }
        _emptyState.Text = report.EmptyMessage;
        _emptyState.Visible = report.Rows.Count == 0;
        _grid.Visible = report.Rows.Count > 0;
        if (_grid.Visible) _grid.BringToFront(); else _emptyState.BringToFront();

        _summary.Controls.Clear();
        foreach (var item in report.Summary.Take(6))
        {
            _summary.Controls.Add(SummaryCard(item.Label, DisplayValue(item.Value, item.ValueKind)?.ToString() ?? "—"));
        }
        if (!string.IsNullOrWhiteSpace(report.CoverageNote))
        {
            _summary.Controls.Add(SummaryCard("Cobertura / alcance", report.CoverageNote));
        }
        if (_summary.Controls.Count == 0)
        {
            _summary.Controls.Add(SummaryCard("Registros", report.TotalRows.ToString("N0")));
        }
        ArrangeSummaryCards();

        var from = report.TotalRows == 0 ? 0 : (_page - 1) * ReportPageSize + 1;
        var to = Math.Min(report.TotalRows, from + report.Rows.Count - 1);
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Text = report.TotalRows == 0
            ? report.EmptyMessage
            : $"Reporte listo · Mostrando {from:N0}-{to:N0} de {report.TotalRows:N0} · {report.Period.DisplayName}";
        _previous.Enabled = _page > 1;
        _next.Enabled = to < report.TotalRows;
        _exportExcel.Enabled = true;
        _print.Enabled = true;
    }

    private async Task ExportAsync()
    {
        _liveSearch?.CancelPending();
        BeginOperation("Exportando...");
        try
        {
            // Let the status label repaint before query/workbook work starts.
            await Task.Yield();
            var result = await _export.ExportExcelAsync(BuildRequest(1), _operationCts!.Token);
            _status.Text = $"Exportación completada · {result.ExportedRows:N0} filas exportadas.";
            using var actions = new GeneratedFileActionsForm("Reporte avanzado generado", result.FilePath);
            actions.ShowDialog(FindForm());
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Exportación cancelada.";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo exportar: {ex.Message}";
            _status.ForeColor = UITheme.DangerColor;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task PrintAsync()
    {
        _liveSearch?.CancelPending();
        BeginOperation("Generando vista previa · Cargando filas...");
        try
        {
            var token = _operationCts!.Token;
            var loadProgress = new Progress<AdvancedReportLoadProgress>(value =>
                _status.Text = $"Generando vista previa · Cargando {value.LoadedRows:N0} de {value.TotalRows:N0} filas...");
            var printable = await AdvancedReportDataLoader.LoadAllAsync(
                _query,
                BuildRequest(1),
                loadProgress,
                token);
            using var document = new AdvancedReportPrintDocument(
                printable,
                _session.UsuarioActual?.NombreUsuario ?? "ADMIN",
                DateTime.Now);
            _status.Text = "Calculando paginación real de la vista previa...";
            var pageProgress = new Progress<(int Page, int? TotalPages)>(value =>
                _status.Text = value.TotalPages.HasValue
                    ? $"Preparando página {value.Page:N0} de {value.TotalPages.Value:N0}..."
                    : $"Preparando página {value.Page:N0}...");
            await Task.Run(() => document.Prepare(token, pageProgress), token);
            token.ThrowIfCancellationRequested();
            using var preview = new PrintPreviewDialog
            {
                Document = document,
                Width = 1200,
                Height = 800,
                StartPosition = FormStartPosition.CenterParent
            };
            _status.Text = $"Reporte listo · {printable.Rows.Count:N0} filas · {document.TotalPages:N0} páginas.";
            EndOperation();
            preview.ShowDialog(FindForm());
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Vista previa cancelada.";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo preparar la impresión: {ex.Message}";
            _status.ForeColor = UITheme.DangerColor;
        }
        finally
        {
            EndOperation();
        }
    }

    internal AdvancedReportRequest BuildRequest(int page)
    {
        var kind = _report.SelectedValue is AdvancedReportKind value ? value : AdvancedReportKind.Sales;
        var selectedOption = _specificValue.SelectedItem as AdvancedReportFilterOption;
        var specificId = selectedOption?.Id;
        var specific = _specificFilter.SelectedValue is AdvancedReportFilterKind filter ? filter : (AdvancedReportFilterKind?)null;
        var subcategoryFilter = specific == AdvancedReportFilterKind.Subcategory
            ? AnalyticsSubcategoryFilterOptions.FromOptionId(specificId ?? 0)
            : AnalyticsSubcategoryFilter.All;
        return new AdvancedReportRequest(
            kind,
            _period.BuildRequest(),
            Search: CurrentDefinition()?.Filters.Contains(AdvancedReportFilterKind.Search) == true &&
                !string.IsNullOrWhiteSpace(_search.Text)
                    ? _search.Text.Trim()
                    : null,
            CustomerId: specific == AdvancedReportFilterKind.Customer ? specificId : null,
            ProductId: specific == AdvancedReportFilterKind.Product ? specificId : null,
            CategoryId: specific == AdvancedReportFilterKind.Category ? specificId : null,
            SubcategoryId: specific == AdvancedReportFilterKind.Subcategory &&
                subcategoryFilter == AnalyticsSubcategoryFilter.All ? specificId : null,
            SupplierId: specific == AdvancedReportFilterKind.Supplier ? specificId : null,
            UserId: specific == AdvancedReportFilterKind.User ? specificId : null,
            State: SelectedFilter(_stateField.Visible, _state),
            PaymentMethod: SelectedFilter(_paymentField.Visible, _paymentMethod),
            CashRegisterId: specific == AdvancedReportFilterKind.CashRegister ? specificId : null,
            MovementType: SelectedFilter(_movementField.Visible, _movementType),
            SelectedEntityDisplay: selectedOption?.DisplayText,
            Page: page,
            PageSize: ReportPageSize,
            SubcategoryFilter: subcategoryFilter);
    }

    private void BeginOperation(string status, CancellationToken cancellationToken = default)
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Text = status;
        SetBusy(true);
    }

    private void EndOperation() => SetBusy(false);

    private void SetBusy(bool busy)
    {
        _period.SetBusy(busy);
        _group.Enabled = !busy;
        _report.Enabled = !busy;
        _search.Enabled = CurrentDefinition()?.Filters.Contains(AdvancedReportFilterKind.Search) == true;
        _state.Enabled = !busy;
        _paymentMethod.Enabled = !busy;
        _movementType.Enabled = !busy;
        _specificFilter.Enabled = !busy;
        _specificValue.Enabled = !busy;
        _load.Enabled = !busy;
        _cancel.Enabled = busy;
        _exportExcel.Enabled = !busy && _current is not null;
        _print.Enabled = !busy && _current is not null;
        _previous.Enabled = !busy && _page > 1;
        _next.Enabled = !busy && _current is not null && _page * ReportPageSize < _current.TotalRows;
    }

    private AdvancedReportDefinition? CurrentDefinition() =>
        _report.SelectedValue is AdvancedReportKind kind ? AdvancedReportCatalog.Get(kind) : null;

    private async Task RefreshSearchAsync(CancellationToken cancellationToken)
    {
        if (_bindingFilters || !_initialized || !_search.Enabled)
        {
            return;
        }

        await RefreshAsync(1, cancellationToken);
    }

    private async Task RefreshFromUserAsync(int page)
    {
        if (_liveSearch is not null)
        {
            await _liveSearch.ExecuteNowAsync(token => RefreshAsync(page, token));
        }
        else
        {
            await RefreshAsync(page);
        }
    }

    private Control BuildSearchField()
    {
        var panel = new TableLayoutPanel { Width = 300, Height = 58, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var input = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        _search.Dock = DockStyle.Fill;
        input.Controls.Add(_search, 0, 0);
        input.Controls.Add(_clearSearch, 1, 0);
        panel.Controls.Add(FilterLabel("Búsqueda"), 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private static void ConfigureClearSearchButton(Button button)
    {
        button.Text = "X";
        button.AccessibleName = "Limpiar búsqueda";
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 0, 0, 0);
        UITheme.StyleSecondaryButton(button);
    }

    private void ShowLiveSearchError(Exception ex)
    {
        if (!IsDisposed)
        {
            _status.ForeColor = UITheme.DangerColor;
            _status.Text = $"No se pudo actualizar la búsqueda: {ex.Message}";
        }
    }

    private void ConfigureGrid()
    {
        _grid.Name = "AdvancedReportsGrid";
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ScrollBars = ScrollBars.Both;
        UITheme.StyleDataGridView(_grid);
    }

    private Control BuildSpecificField()
    {
        var filterWidth = MeasureComboMinimumWidth(_specificFilter, "Subcategoría");
        var panel = new TableLayoutPanel
        {
            Width = Math.Max(390, filterWidth + 252),
            Height = 58,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, filterWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.Controls.Add(FilterLabel("Filtro específico"), 0, 0);
        panel.SetColumnSpan(panel.Controls[^1], 2);
        _specificFilter.Name = "AdvancedReportsSpecificFilter";
        _specificFilter.MinimumSize = new Size(filterWidth, 0);
        _specificFilter.Dock = DockStyle.Fill;
        _specificValue.Dock = DockStyle.Fill;
        _specificValue.AccessibleName = "Valor del filtro específico";
        panel.Controls.Add(_specificFilter, 0, 1);
        panel.Controls.Add(_specificValue, 1, 1);
        return panel;
    }

    private async Task LoadSpecificOptionsAsync(string? search = null)
    {
        if (_bindingFilters ||
            _report.SelectedValue is not AdvancedReportKind reportKind ||
            _specificFilter.SelectedValue is not AdvancedReportFilterKind filterKind ||
            !_specificField.Visible)
        {
            return;
        }

        _optionCts?.Cancel();
        _optionCts?.Dispose();
        _optionCts = new CancellationTokenSource();
        var token = _optionCts.Token;
        var enteredText = search?.Trim() ?? string.Empty;
        try
        {
            var options = await _filterOptions.GetOptionsAsync(
                reportKind,
                filterKind,
                string.IsNullOrWhiteSpace(enteredText) ? null : enteredText,
                cancellationToken: token);
            token.ThrowIfCancellationRequested();

            _bindingFilters = true;
            _specificValue.BeginUpdate();
            _specificValue.DataSource = options.ToList();
            _specificValue.DisplayMember = nameof(AdvancedReportFilterOption.DisplayText);
            _specificValue.ValueMember = nameof(AdvancedReportFilterOption.Id);
            _specificValue.SelectedIndex = -1;
            if (!string.IsNullOrWhiteSpace(enteredText))
            {
                _specificValue.Text = enteredText;
                _specificValue.SelectionStart = _specificValue.Text.Length;
                _specificValue.DroppedDown = options.Count > 0 && _specificValue.Focused;
            }
            _specificValue.EndUpdate();
            _bindingFilters = false;
        }
        catch (OperationCanceledException)
        {
            // Una búsqueda más reciente reemplazó esta carga acotada de opciones.
        }
        catch (Exception ex)
        {
            _bindingFilters = false;
            _status.ForeColor = UITheme.DangerColor;
            _status.Text = $"No se pudieron cargar las opciones del filtro: {ex.Message}";
        }
    }

    private void MarkFiltersChanged()
    {
        if (_bindingFilters || !_initialized)
        {
            return;
        }

        _page = 1;
        _current = null;
        _previous.Enabled = false;
        _next.Enabled = false;
        _exportExcel.Enabled = false;
        _print.Enabled = false;
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Text = "Filtros modificados · Prepare el reporte para recalcular resultados.";
    }

    private static void ConfigureColumnLayout(
        DataGridViewColumn gridColumn,
        AdvancedReportColumn definition)
    {
        gridColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        switch (definition.ValueKind)
        {
            case AdvancedReportValueKind.Text:
                gridColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                gridColumn.FillWeight = definition.Key.Contains("description", StringComparison.OrdinalIgnoreCase) ||
                    definition.Key.Contains("product", StringComparison.OrdinalIgnoreCase) ||
                    definition.Key.Contains("customer", StringComparison.OrdinalIgnoreCase) ||
                    definition.Key.Contains("supplier", StringComparison.OrdinalIgnoreCase)
                    ? 175f
                    : 110f;
                gridColumn.MinimumWidth = 100;
                break;
            case AdvancedReportValueKind.DateTime:
                gridColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                gridColumn.Width = 142;
                gridColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                break;
            case AdvancedReportValueKind.Date:
                gridColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                gridColumn.Width = 108;
                gridColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                break;
            case AdvancedReportValueKind.Boolean:
                gridColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                gridColumn.Width = 92;
                gridColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                break;
            default:
                gridColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                gridColumn.Width = definition.ValueKind == AdvancedReportValueKind.Money ? 124 : 106;
                gridColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                break;
        }
    }

    private static void ConfigureActionButton(Button button, int minimumWidth)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(minimumWidth, 44);
        button.Padding = new Padding(12, 4, 12, 5);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.AutoEllipsis = false;
    }

    private static Control Field(string label, Control control, int width)
    {
        var panel = new TableLayoutPanel { Width = width, Height = 58, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(FilterLabel(label), 0, 0);
        panel.Controls.Add(control, 0, 1);
        return panel;
    }

    private static int MeasureComboMinimumWidth(ComboBox combo, string text)
    {
        var measured = TextRenderer.MeasureText(
            text,
            combo.Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var arrowAndBorder = SystemInformation.VerticalScrollBarWidth + 16;
        return Math.Max(150, measured.Width + combo.Padding.Horizontal + arrowAndBorder);
    }

    private static void ConfigureSubcategoryColumnLayout(DataGridView grid)
    {
        var column = grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(item => string.Equals(item.Name, "subcategory", StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            return;
        }

        var headerFont = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
        var cellFont = grid.DefaultCellStyle.Font ?? grid.Font;
        var headerWidth = TextRenderer.MeasureText(
            column.HeaderText,
            headerFont,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var valueWidth = TextRenderer.MeasureText(
            "No disponible (histórico)",
            cellFont,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var padding = grid.DefaultCellStyle.Padding.Horizontal + 24;
        var readableWidth = Math.Clamp(Math.Max(headerWidth, valueWidth) + padding, 160, 320);

        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        column.MinimumWidth = readableWidth;
        column.Width = readableWidth;
        column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
    }

    private static Label FilterLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        ForeColor = UITheme.MutedTextColor,
        TextAlign = ContentAlignment.BottomLeft
    };

    private static Control SummaryCard(string title, string value)
    {
        var isCoverage = string.Equals(title, "Cobertura / alcance", StringComparison.Ordinal);
        var valueFont = new Font("Segoe UI", 9.2f, FontStyle.Bold);
        var textFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var measured = TextRenderer.MeasureText(value, valueFont, Size.Empty, textFlags);
        var cardWidth = isCoverage
            ? Math.Clamp(measured.Width + 20, 210, 320)
            : 210;
        var valueWidth = Math.Max(1, cardWidth - 18);
        var wrappedSize = isCoverage
            ? TextRenderer.MeasureText(
                value,
                valueFont,
                new Size(valueWidth, 1000),
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak)
            : measured;
        var cardHeight = isCoverage
            ? Math.Clamp(Math.Max(58, wrappedSize.Height + 36), 58, 82)
            : 58;
        var card = new DashboardSurfacePanel
        {
            Width = cardWidth,
            Height = cardHeight,
            Margin = new Padding(0, 0, 8, 4),
            Padding = new Padding(9, 5, 9, 5),
            Name = isCoverage ? "AdvancedReportCoverageSummaryCard" : string.Empty,
            Tag = isCoverage ? "coverage-summary-card" : null
        };
        var valueLabel = new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = valueFont,
            ForeColor = UITheme.TextDark,
            TextAlign = isCoverage ? ContentAlignment.MiddleLeft : ContentAlignment.BottomLeft,
            AutoEllipsis = isCoverage ? false : true,
            AutoSize = false,
            UseCompatibleTextRendering = isCoverage,
            Name = isCoverage ? "AdvancedReportCoverageValue" : string.Empty
        };
        card.Controls.Add(valueLabel);
        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font("Segoe UI", isCoverage ? 8f : 7.5f, FontStyle.Bold),
            ForeColor = UITheme.PrimaryColor,
            AutoEllipsis = false,
            AutoSize = false,
            Name = isCoverage ? "AdvancedReportCoverageTitle" : string.Empty
        });
        return card;
    }

    /// <summary>
    /// Keeps the summary in one compact row whenever the report surface has
    /// enough width.  The previous fixed 210px cards wrapped at 1280x720,
    /// consuming the only flexible row and leaving the grid with little more
    /// than its header.  Widths are recalculated after DPI scaling and resize,
    /// so high-DPI surfaces continue to show the same seven metrics without
    /// clipping the table below.
    /// </summary>
    private void ArrangeSummaryCards()
    {
        if (_summary.IsDisposed || _summary.Controls.Count == 0)
        {
            return;
        }

        var available = _summary.ClientSize.Width - _summary.Padding.Horizontal;
        if (available <= 0)
        {
            return;
        }

        var cards = _summary.Controls.Cast<Control>().Where(control => control.Visible).ToArray();
        if (cards.Length == 0)
        {
            return;
        }

        var marginWidth = cards.Sum(card => card.Margin.Horizontal);
        var contentWidth = Math.Max(1, available - marginWidth);
        var coverage = cards.FirstOrDefault(card =>
            string.Equals(card.Tag as string, "coverage-summary-card", StringComparison.Ordinal));
        var regularCards = cards.Length - (coverage is null ? 0 : 1);
        var coverageWidth = coverage is null
            ? 0
            : Math.Clamp(
                TextRenderer.MeasureText(
                    (coverage.Controls
                        .Cast<Control>()
                        .FirstOrDefault(control => control.Name == "AdvancedReportCoverageValue") as Label)?.Text ??
                    string.Empty,
                    (coverage.Controls
                        .Cast<Control>()
                        .FirstOrDefault(control => control.Name == "AdvancedReportCoverageValue") as Label)?.Font ??
                    SystemFonts.MessageBoxFont,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 38,
                200,
                260);
        var width = regularCards > 0
            ? Math.Max(150, (contentWidth - coverageWidth) / regularCards)
            : contentWidth;
        width = Math.Min(320, width);
        foreach (var card in cards)
        {
            var cardWidth = ReferenceEquals(card, coverage)
                ? Math.Min(coverageWidth, contentWidth - (width * regularCards))
                : width;
            if (cardWidth <= 0)
            {
                cardWidth = width;
            }

            if (card.Width != cardWidth)
            {
                card.Width = cardWidth;
            }

            var titleLabel = card.Controls
                .Cast<Control>()
                .OfType<Label>()
                .FirstOrDefault(label => label.Dock == DockStyle.Top);
            if (titleLabel is not null)
            {
                FitSummaryTitle(titleLabel, Math.Max(1, cardWidth - card.Padding.Horizontal));
            }

            if (string.Equals(card.Tag as string, "coverage-summary-card", StringComparison.Ordinal))
            {
                var valueLabel = card.Controls
                    .Cast<Control>()
                    .FirstOrDefault(control => control.Name == "AdvancedReportCoverageValue") as Label;
                if (valueLabel is not null)
                {
                    var valueWidth = Math.Max(1, cardWidth - card.Padding.Horizontal);
                    var wrapped = TextRenderer.MeasureText(
                        valueLabel.Text,
                        valueLabel.Font,
                        new Size(valueWidth, 1000),
                        TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
                    var height = Math.Clamp(Math.Max(58, wrapped.Height + 30), 58, 90);
                    if (card.Height != height)
                    {
                        card.Height = height;
                    }
                }
            }
        }
    }

    private static void FitSummaryTitle(Label titleLabel, int availableWidth)
    {
        var current = titleLabel.Font;
        var size = current.Size;
        while (size > 6f)
        {
            using var candidate = new Font(current.FontFamily, size, current.Style);
            var measured = TextRenderer.MeasureText(
                titleLabel.Text,
                candidate,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (measured.Width <= availableWidth)
            {
                break;
            }

            size -= 0.25f;
        }

        if (Math.Abs(size - current.Size) > 0.01f)
        {
            titleLabel.Font = new Font(current.FontFamily, size, current.Style);
            current.Dispose();
        }
    }

    private static object? DisplayValue(object? value, AdvancedReportValueKind kind)
    {
        if (value is null)
        {
            return kind is AdvancedReportValueKind.Money or AdvancedReportValueKind.Percentage
                ? "No calculable"
                : "—";
        }
        return kind switch
        {
            AdvancedReportValueKind.Boolean => Convert.ToBoolean(value) ? "Sí" : "No",
            AdvancedReportValueKind.Percentage => $"{Convert.ToDecimal(value):N2}%",
            _ => value
        };
    }

    private static string? SelectedFilter(bool visible, ComboBox combo) =>
        visible && combo.SelectedItem is string value && value != "Todos" ? value : null;

    private static void ConfigureSimpleCombo(ComboBox combo, IReadOnlyList<string> values)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.DataSource = values.ToList();
        combo.SelectedIndex = 0;
    }

    private static IReadOnlyList<string> StateNames(AdvancedReportKind kind) => kind switch
    {
        AdvancedReportKind.Products or AdvancedReportKind.Inventory or AdvancedReportKind.CurrentInventoryValue => Enum.GetNames<EstadoProducto>(),
        AdvancedReportKind.Purchases => Enum.GetNames<EstadoFinancieroCompra>(),
        AdvancedReportKind.Suppliers => Enum.GetNames<EstadoProveedor>(),
        AdvancedReportKind.Customers => Enum.GetNames<EstadoCliente>(),
        AdvancedReportKind.AccountsReceivable => Enum.GetNames<EstadoCuentaPorCobrar>(),
        AdvancedReportKind.AccountsPayable => Enum.GetNames<EstadoCuentaPorPagar>(),
        AdvancedReportKind.Quotations => Enum.GetNames<EstadoCotizacion>(),
        AdvancedReportKind.CustomOrders => Enum.GetNames<EstadoPedidoPersonalizado>(),
        _ => []
    };

    private static string GroupName(AdvancedReportGroup value) => value switch
    {
        AdvancedReportGroup.Commercial => "Comercial",
        AdvancedReportGroup.Inventory => "Inventario",
        AdvancedReportGroup.Financial => "Financiero",
        AdvancedReportGroup.Customers => "Clientes",
        AdvancedReportGroup.Suppliers => "Proveedores",
        _ => "Auditoría / operaciones"
    };

    private static string FilterName(AdvancedReportFilterKind value) => value switch
    {
        AdvancedReportFilterKind.Customer => "Cliente",
        AdvancedReportFilterKind.Product => "Producto",
        AdvancedReportFilterKind.Category => "Categoría",
        AdvancedReportFilterKind.Subcategory => "Subcategoría",
        AdvancedReportFilterKind.Supplier => "Proveedor",
        AdvancedReportFilterKind.User => "Usuario",
        AdvancedReportFilterKind.CashRegister => "Caja",
        _ => value.ToString()
    };

    private sealed record GroupOption(AdvancedReportGroup Value, string Name);
    private sealed record ReportOption(AdvancedReportKind Value, string Name);
    private sealed record FilterOption(AdvancedReportFilterKind Value, string Name);
}
