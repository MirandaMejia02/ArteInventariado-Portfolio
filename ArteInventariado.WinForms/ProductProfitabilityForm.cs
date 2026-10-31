using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

internal sealed class ProductProfitabilityForm : Form
{
    private const int QueryPageSize = 1000;
    private readonly IAdvancedReportQueryService _query;
    private readonly IAdvancedReportExportService _export;
    private readonly IAdvancedReportFilterOptionsService _filterOptions;
    private readonly ICurrentSession _session;
    private readonly AnalyticsPeriodSelectorControl _period = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _category = new();
    private readonly ComboBox _subcategory = new();
    private readonly ComboBox _top = new();
    private readonly ComboBox _sort = new();
    private readonly ComboBox _direction = new();
    private readonly Button _load = new();
    private readonly Button _clear = new();
    private readonly Button _exportAll = new();
    private readonly Button _exportSelected = new();
    private readonly Button _print = new();
    private readonly Button _close = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly Label _coverage = new();
    private readonly Label _status = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private AdvancedReportResult? _current;
    private bool _loadingCategories;
    private List<SubcategoryChoice> _subcategoryChoices = [];

    public ProductProfitabilityForm(
        IAdvancedReportQueryService query,
        IAdvancedReportExportService export,
        IAdvancedReportFilterOptionsService filterOptions,
        ICurrentSession session)
    {
        _query = query;
        _export = export;
        _filterOptions = filterOptions;
        _session = session;
        BuildUi();
        Shown += async (_, _) => await LoadAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _operation?.Cancel();
            _operation?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Text = "Rentabilidad por producto";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1150, 680);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        UITheme.StyleForm(this);
        BackColor = UITheme.DashboardBackgroundColor;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10, 12, 10, 8),
            BackColor = UITheme.DashboardBackgroundColor,
            MinimumSize = Size.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(_period, 0, 1);
        root.Controls.Add(BuildFilters(), 0, 2);
        root.Controls.Add(BuildSummary(), 0, 3);
        root.Controls.Add(BuildGrid(), 0, 4);
        root.Controls.Add(BuildFooter(), 0, 5);
        Controls.Add(root);

        _period.RefreshRequested += async (_, _) => await LoadAsync();
        _category.SelectedValueChanged += (_, _) => FilterSubcategories();
        _load.Click += async (_, _) => await LoadAsync();
        _clear.Click += async (_, _) =>
        {
            _search.Clear();
            _category.SelectedIndex = 0;
            _subcategory.SelectedIndex = 0;
            _top.SelectedIndex = 1;
            _sort.SelectedIndex = 0;
            _direction.SelectedIndex = 0;
            await LoadAsync();
        };
        _exportAll.Click += async (_, _) => await ExportAsync(selected: false);
        _exportSelected.Click += async (_, _) => await ExportAsync(selected: true);
        _print.Click += async (_, _) => await PrintAsync(selected: false);
        _close.Click += (_, _) => Close();
    }

    private static Control BuildHeader()
    {
        var header = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 7, 14, 7),
            BackColor = UITheme.SurfaceColor
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "Rentabilidad por producto",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "Ventas netas, costo histórico, ganancia, margen y cobertura sin extrapolar costos faltantes.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            AutoEllipsis = true,
            Padding = new Padding(2, 0, 0, 0)
        };
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(layout);
        return header;
    }

    private Control BuildFilters()
    {
        var surface = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 7, 12, 7),
            BackColor = UITheme.SurfaceColor
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = false,
            AutoScroll = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        _search.PlaceholderText = "Producto, código o SKU";
        _search.Width = 285;
        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.Width = 220;
        _subcategory.DropDownStyle = ComboBoxStyle.DropDownList;
        _subcategory.Width = 220;
        _top.DropDownStyle = ComboBoxStyle.DropDownList;
        _top.DisplayMember = nameof(TopChoice.Text);
        _top.Items.AddRange(
        [
            new TopChoice("Top 5", 5),
            new TopChoice("Top 10", 10),
            new TopChoice("Top 20", 20),
            new TopChoice("Top 25", 25),
            new TopChoice("Todos", null)
        ]);
        _top.SelectedIndex = 1;
        _top.Width = 120;
        _sort.DropDownStyle = ComboBoxStyle.DropDownList;
        _sort.DisplayMember = nameof(SortChoice.Text);
        _sort.Items.AddRange(
        [
            new SortChoice("Unidades netas", ProductProfitabilitySort.UnitsSold),
            new SortChoice("Ventas netas", ProductProfitabilitySort.NetSales),
            new SortChoice("Ganancia total", ProductProfitabilitySort.TotalGain),
            new SortChoice("Margen %", ProductProfitabilitySort.Margin)
        ]);
        _sort.SelectedIndex = 0;
        _sort.Width = 155;
        _direction.DropDownStyle = ComboBoxStyle.DropDownList;
        _direction.Items.AddRange(["Descendente", "Ascendente"]);
        _direction.SelectedIndex = 0;
        _direction.Width = 125;

        _load.Text = "Buscar";
        _load.Size = new Size(112, 38);
        _clear.Text = "Limpiar";
        _clear.Size = new Size(112, 38);
        UITheme.StylePrimaryButton(_load);
        UITheme.StyleSecondaryButton(_clear);

        var searchField = Field("Buscar", _search, 300);
        var categoryField = Field("Categoría", _category, 235);
        var subcategoryField = Field("Subcategoría", _subcategory, 235);
        var topField = Field("Mostrar", _top, 135);
        var sortField = Field("Ordenar por", _sort, 170);
        var directionField = Field("Dirección", _direction, 140);
        var loadField = ActionHost(_load);
        var clearField = ActionHost(_clear);
        flow.Controls.AddRange([searchField, categoryField, subcategoryField, topField, sortField, directionField, loadField, clearField]);
        flow.SetFlowBreak(subcategoryField, true);
        flow.SetFlowBreak(clearField, true);
        surface.Controls.Add(flow);
        return surface;
    }

    private Control BuildSummary()
    {
        var surface = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 6, 12, 5),
            BackColor = UITheme.SurfaceColor
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _summary.Dock = DockStyle.Fill;
        _summary.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
        _summary.ForeColor = UITheme.TextDark;
        _summary.AutoEllipsis = true;
        _coverage.Dock = DockStyle.Fill;
        _coverage.Font = new Font("Segoe UI", 8.3f);
        _coverage.ForeColor = UITheme.MutedTextColor;
        _coverage.AutoEllipsis = true;
        _coverage.TextAlign = ContentAlignment.MiddleLeft;
        _status.Dock = DockStyle.Fill;
        _status.Font = new Font("Segoe UI", 8.3f);
        _status.ForeColor = UITheme.MutedTextColor;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(_summary, 0, 0);
        layout.SetColumnSpan(_summary, 2);
        layout.Controls.Add(_coverage, 0, 1);
        layout.Controls.Add(_status, 1, 1);
        surface.Controls.Add(layout);
        return surface;
    }

    private Control BuildGrid()
    {
        var surface = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            BackColor = UITheme.SurfaceColor,
            MinimumSize = Size.Empty
        };
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        _grid.BackgroundColor = UITheme.SurfaceColor;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = UITheme.DashboardBorderColor;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = UITheme.SecondaryColor;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 60;
        _grid.RowTemplate.Height = 36;
        _grid.DefaultCellStyle.SelectionBackColor = UITheme.DashboardAccentSoftColor;
        _grid.DefaultCellStyle.SelectionForeColor = UITheme.TextDark;
        surface.Controls.Add(_grid);
        return surface;
    }

    private Control BuildFooter()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoScroll = false,
            AutoSize = false,
            Padding = new Padding(0, 5, 0, 0),
            MinimumSize = Size.Empty
        };
        ConfigureAction(_close, "Cerrar", 110, UITheme.StyleSecondaryButton);
        ConfigureAction(_print, "Vista previa / PDF", 195, UITheme.StyleSecondaryButton);
        ConfigureAction(_exportSelected, "Exportar selección", 205, UITheme.StyleSecondaryButton);
        ConfigureAction(_exportAll, "Exportar todo", 170, UITheme.StylePrimaryButton);
        flow.Controls.AddRange([_close, _print, _exportSelected, _exportAll]);
        flow.SetFlowBreak(_exportSelected, true);
        return flow;
    }

    private async Task LoadAsync()
    {
        if (_loadingCategories)
        {
            return;
        }

        BeginOperation("Cargando rentabilidad por producto...");
        try
        {
            await LoadCategoriesAsync(_operation!.Token);
            await LoadSubcategoriesAsync(_operation.Token);
            var request = BuildRequest(selected: false);
            var result = await AdvancedReportDataLoader.LoadAllAsync(
                _query,
                request,
                cancellationToken: _operation.Token);
            _operation.Token.ThrowIfCancellationRequested();
            _current = result;
            Render(result);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Consulta cancelada.";
        }
        catch (Exception ex)
        {
            _current = null;
            _summary.Text = "No se pudo cargar la rentabilidad.";
            _coverage.Text = string.Empty;
            _status.ForeColor = UITheme.DangerColor;
            _status.Text = ex.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        if (_category.Items.Count > 0)
        {
            return;
        }

        _loadingCategories = true;
        try
        {
            var options = await _filterOptions.GetOptionsAsync(
                AdvancedReportKind.ProductProfitability,
                AdvancedReportFilterKind.Category,
                limit: 100,
                cancellationToken: cancellationToken);
            var choices = new List<CategoryChoice> { new(null, "Todas") };
            choices.AddRange(options.Select(option => new CategoryChoice(option.Id, option.Name)));
            _category.DataSource = choices;
            _category.DisplayMember = nameof(CategoryChoice.Text);
            _category.ValueMember = nameof(CategoryChoice.Id);
            _category.SelectedIndex = 0;
        }
        finally
        {
            _loadingCategories = false;
        }
    }

    private async Task LoadSubcategoriesAsync(CancellationToken cancellationToken)
    {
        if (_subcategoryChoices.Count > 0)
        {
            FilterSubcategories();
            return;
        }

        var options = await _filterOptions.GetOptionsAsync(
            AdvancedReportKind.ProductProfitability,
            AdvancedReportFilterKind.Subcategory,
            limit: 100,
            cancellationToken: cancellationToken);
        _subcategoryChoices = options
            .Select(option => new SubcategoryChoice(
                option.Id,
                option.Name,
                option.ParentId,
                AnalyticsSubcategoryFilterOptions.FromOptionId(option.Id)))
            .ToList();
        FilterSubcategories();
    }

    private void FilterSubcategories()
    {
        var category = _category.SelectedItem as CategoryChoice;
        var choices = new List<SubcategoryChoice> { new(null, "Todas", null) };
        choices.AddRange(_subcategoryChoices
            .Where(choice => category?.Id is null ||
                choice.Filter != AnalyticsSubcategoryFilter.All ||
                choice.CategoryId == category.Id)
            .OrderBy(choice => choice.Text, StringComparer.CurrentCultureIgnoreCase));

        var selected = _subcategory.SelectedItem as SubcategoryChoice;
        _subcategory.DataSource = choices;
        _subcategory.DisplayMember = nameof(SubcategoryChoice.Text);
        _subcategory.ValueMember = nameof(SubcategoryChoice.Id);
        var selectedIndex = selected is null
            ? 0
            : Math.Max(0, choices.FindIndex(choice =>
                choice.Id == selected.Id && choice.Filter == selected.Filter));
        _subcategory.SelectedIndex = selectedIndex;
    }

    private void Render(AdvancedReportResult report)
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        var idColumn = new DataGridViewTextBoxColumn
        {
            Name = "productId",
            Visible = false,
            ReadOnly = true
        };
        _grid.Columns.Add(idColumn);
        foreach (var column in report.Columns)
        {
            var gridColumn = new DataGridViewTextBoxColumn
            {
                Name = column.Key,
                HeaderText = column.Title,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MinimumWidth = MinimumWidth(column),
                Width = DefaultWidth(column),
                AutoSizeMode = column.ValueKind == AdvancedReportValueKind.Text
                    ? DataGridViewAutoSizeColumnMode.None
                    : DataGridViewAutoSizeColumnMode.None
            };
            gridColumn.DefaultCellStyle.Alignment = column.ValueKind switch
            {
                AdvancedReportValueKind.Integer or AdvancedReportValueKind.Money or
                    AdvancedReportValueKind.Percentage => DataGridViewContentAlignment.MiddleRight,
                _ => DataGridViewContentAlignment.MiddleLeft
            };
            gridColumn.DefaultCellStyle.NullValue = "No calculable";
            _grid.Columns.Add(gridColumn);
        }

        foreach (var row in report.Rows)
        {
            var values = new object[report.Columns.Count + 1];
            values[0] = row["productId"] ?? 0;
            for (var index = 0; index < report.Columns.Count; index++)
            {
                var column = report.Columns[index];
                values[index + 1] = DisplayValue(row[column.Key], column.ValueKind) ?? "—";
            }
            var gridRowIndex = _grid.Rows.Add(values);
            if (IsLoss(row))
            {
                var gridRow = _grid.Rows[gridRowIndex];
                gridRow.DefaultCellStyle.ForeColor = UITheme.DangerColor;
                gridRow.DefaultCellStyle.BackColor = Color.FromArgb(255, 239, 242);
                gridRow.DefaultCellStyle.SelectionForeColor = UITheme.TextDark;
                gridRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 218, 226);
            }
        }

        _summary.Text = report.Summary.Count == 0
            ? $"Productos mostrados: {report.TotalRows:N0}"
            : string.Join("   ·   ", report.Summary.Select(item =>
                $"{item.Label}: {Format(item.Value, item.ValueKind)}"));
        _coverage.Text = report.CoverageNote ?? string.Empty;
        _coverage.ForeColor = UITheme.MutedTextColor;
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Text = report.TotalRows == 0
            ? report.EmptyMessage
            : $"{report.TotalRows:N0} producto(s) · Selecciona filas para exportar únicamente esos productos.";
        _exportAll.Enabled = true;
        _exportSelected.Enabled = report.Rows.Count > 0;
        _print.Enabled = true;
    }

    private async Task ExportAsync(bool selected)
    {
        if (selected && SelectedProductIds().Count == 0)
        {
            MessageBox.Show(
                "Selecciona al menos un producto para exportar.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        BeginOperation(selected ? "Exportando selección..." : "Exportando rentabilidad...");
        try
        {
            var result = await _export.ExportExcelAsync(
                BuildRequest(selected),
                _operation!.Token);
            _status.Text = $"Exportación completada · {result.ExportedRows:N0} filas.";
            using var actions = new GeneratedFileActionsForm(
                "Reporte de rentabilidad generado",
                result.FilePath);
            actions.ShowDialog(this);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Exportación cancelada.";
        }
        catch (Exception ex)
        {
            _status.ForeColor = UITheme.DangerColor;
            _status.Text = $"No se pudo exportar: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task PrintAsync(bool selected)
    {
        if (selected && SelectedProductIds().Count == 0)
        {
            MessageBox.Show(
                "Selecciona al menos un producto para imprimir.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        BeginOperation(selected ? "Preparando selección para imprimir..." : "Preparando impresión...");
        try
        {
            var printable = await AdvancedReportDataLoader.LoadAllAsync(
                _query,
                BuildRequest(selected),
                cancellationToken: _operation!.Token);
            using var document = new AdvancedReportPrintDocument(
                printable,
                _session.UsuarioActual?.NombreUsuario ?? "Usuario",
                DateTime.Now);
            await Task.Run(() => document.Prepare(_operation.Token), _operation.Token);
            _operation.Token.ThrowIfCancellationRequested();
            using var preview = new PrintPreviewDialog
            {
                Document = document,
                Width = 1200,
                Height = 800,
                StartPosition = FormStartPosition.CenterParent
            };
            EndOperation();
            preview.ShowDialog(this);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Impresión cancelada.";
        }
        catch (Exception ex)
        {
            _status.ForeColor = UITheme.DangerColor;
            _status.Text = $"No se pudo preparar la impresión: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private AdvancedReportRequest BuildRequest(bool selected)
    {
        var category = _category.SelectedItem as CategoryChoice;
        var subcategory = _subcategory.SelectedItem as SubcategoryChoice;
        var subcategoryFilter = subcategory?.Filter ?? AnalyticsSubcategoryFilter.All;
        var top = _top.SelectedItem as TopChoice;
        var sort = _sort.SelectedItem as SortChoice;
        var selectedIds = selected ? SelectedProductIds() : null;
        return new AdvancedReportRequest(
            AdvancedReportKind.ProductProfitability,
            _period.BuildRequest(),
            Search: string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim(),
            CategoryId: category?.Id,
            SubcategoryId: subcategoryFilter == AnalyticsSubcategoryFilter.All ? subcategory?.Id : null,
            Page: 1,
            PageSize: QueryPageSize,
            ProfitabilitySort: sort?.Value ?? ProductProfitabilitySort.UnitsSold,
            ProfitabilityDescending: _direction.SelectedIndex == 0,
            ProfitabilityTop: selected ? null : top?.Limit,
            SelectedProductIds: selectedIds,
            SubcategoryFilter: subcategoryFilter);
    }

    private IReadOnlyCollection<int> SelectedProductIds() =>
        _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Cells["productId"].Value)
            .OfType<int>()
            .Distinct()
            .ToList();

    private void BeginOperation(string message)
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Text = message;
        SetBusy(true);
    }

    private void EndOperation() => SetBusy(false);

    private void SetBusy(bool busy)
    {
        _period.SetBusy(busy);
        _search.Enabled = !busy;
        _category.Enabled = !busy;
        _subcategory.Enabled = !busy;
        _top.Enabled = !busy;
        _sort.Enabled = !busy;
        _direction.Enabled = !busy;
        _load.Enabled = !busy;
        _clear.Enabled = !busy;
        _exportAll.Enabled = !busy && _current is not null;
        _exportSelected.Enabled = !busy && _current?.Rows.Count > 0;
        _print.Enabled = !busy && _current is not null;
        _close.Enabled = !busy;
    }

    private static Control Field(string label, Control control, int width)
    {
        var panel = new TableLayoutPanel
        {
            Width = width,
            Height = 58,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.BottomLeft
        };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(caption, 0, 0);
        panel.Controls.Add(control, 0, 1);
        return panel;
    }

    private static Control ActionHost(Button button) => new Panel
    {
        Width = button.Width,
        Height = 58,
        Margin = new Padding(0, 0, 10, 0),
        Padding = new Padding(0, 20, 0, 0),
        Controls = { button }
    };

    private static void ConfigureAction(
        Button button,
        string text,
        int width,
        Action<Button> style)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 42;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Margin = new Padding(6, 0, 0, 0);
        style(button);
    }

    private static int MinimumWidth(AdvancedReportColumn column) => column.ValueKind switch
    {
        AdvancedReportValueKind.Text => column.Key is "product" or "category" or "subcategory" ? 150 : 105,
        AdvancedReportValueKind.Money => 125,
        AdvancedReportValueKind.Percentage => 110,
        _ => 105
    };

    private static int DefaultWidth(AdvancedReportColumn column) => column.ValueKind switch
    {
        AdvancedReportValueKind.Text => column.Key switch
        {
            "subcategory" => 230,
            "product" or "category" => 190,
            _ => 115
        },
        AdvancedReportValueKind.Money => 135,
        AdvancedReportValueKind.Percentage => 115,
        _ => 110
    };

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
            AdvancedReportValueKind.Money => $"${Convert.ToDecimal(value):N2}",
            AdvancedReportValueKind.Percentage => $"{Convert.ToDecimal(value):N2}%",
            AdvancedReportValueKind.Integer => Convert.ToInt64(value).ToString("N0"),
            _ => value
        };
    }

    private static string Format(object? value, AdvancedReportValueKind kind) =>
        DisplayValue(value, kind)?.ToString() ?? "—";

    private static bool IsLoss(AdvancedReportRow row) =>
        row["totalGain"] is decimal totalGain && totalGain < 0m ||
        row["margin"] is decimal margin && margin < 0m;

    private sealed record CategoryChoice(int? Id, string Text);
    private sealed record SubcategoryChoice(
        int? Id,
        string Text,
        int? CategoryId,
        AnalyticsSubcategoryFilter Filter = AnalyticsSubcategoryFilter.All);
    private sealed record TopChoice(string Text, int? Limit);
    private sealed record SortChoice(string Text, ProductProfitabilitySort Value);
}
