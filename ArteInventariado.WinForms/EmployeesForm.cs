using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;

namespace ArteInventariado.WinForms;

/// <summary>
/// Read-only administrative view of employee accounts and their activity.
/// Business rules remain in IEmployeeActivityQueryService; this form only
/// coordinates filters and presentation.
/// </summary>
public sealed class EmployeesForm : Form
{
    private static readonly string[] MonthNames =
    [
        "Todos",
        "Enero",
        "Febrero",
        "Marzo",
        "Abril",
        "Mayo",
        "Junio",
        "Julio",
        "Agosto",
        "Septiembre",
        "Octubre",
        "Noviembre",
        "Diciembre"
    ];

    private readonly IEmployeeActivityQueryService _activity;
    private readonly IEmployeeSalesQueryService _sales;
    private readonly IEmployeePaymentService _payments;
    private readonly IEmployeePaymentQueryService _paymentQueries;
    private readonly TextBox _search = new();
    private readonly ComboBox _status = new();
    private readonly ComboBox _month = new();
    private readonly ComboBox _year = new();
    private readonly Button _clearFilters = new();
    private readonly Button _refresh = new();
    private readonly Button _detail = new();
    private readonly Button _ranking = new();
    private readonly Label _statusLabel = new();
    private readonly DataGridView _grid = new();
    private readonly BindingSource _binding = new();
    private IReadOnlyList<EmployeeActivitySummary> _rows = [];
    private bool _loading;
    private bool _suppressFilterEvents;

    public EmployeesForm(
        IEmployeeActivityQueryService activity,
        IEmployeeSalesQueryService sales,
        IEmployeePaymentService payments,
        IEmployeePaymentQueryService paymentQueries)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _sales = sales ?? throw new ArgumentNullException(nameof(sales));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _paymentQueries = paymentQueries ?? throw new ArgumentNullException(nameof(paymentQueries));
        BuildUi();
        Load += EmployeesForm_Load;
    }

    private async void EmployeesForm_Load(object? sender, EventArgs e)
    {
        await RefreshRowsAsync();
    }

    private void BuildUi()
    {
        Text = "Empleados";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1160, 720);
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(920, 600);
        UITheme.StyleForm(this);

        ConfigureFilters();
        ConfigureGrid();
        ConfigureButtons();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        root.Controls.Add(
            VisualLayout.CreatePageHeader(
                "Empleados",
                "Consulta el estado de las cuentas y su actividad de acceso."),
            0,
            0);
        root.Controls.Add(BuildFiltersPanel(), 0, 1);

        UITheme.StyleStatusLabel(_statusLabel, UITheme.SecondaryColor);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Preparando empleados...";
        root.Controls.Add(_statusLabel, 0, 2);

        root.Controls.Add(
            VisualLayout.CreateSection(
                "Listado de empleados",
                "La fecha de creación es la fecha registrada de la cuenta; si no existe se muestra como No disponible.",
                _grid),
            0,
            3);

        root.Controls.Add(
            VisualLayout.CreateActionBar(_detail, _ranking),
            0,
            4);

        Controls.Add(root);
    }

    private void ConfigureFilters()
    {
        UITheme.StyleInput(_search);
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Buscar empleado...";
        _search.Margin = Padding.Empty;
        _search.TextChanged += (_, _) => ApplyFilter();

        ConfigureCombo(_status, "Todos", "Activos", "Inactivos");
        ConfigureCombo(_month, MonthNames);
        ConfigureCombo(_year, "Todos");

        _status.SelectedIndexChanged += (_, _) => ApplyFilter();
        _month.SelectedIndexChanged += async (_, _) => await PeriodChangedAsync();
        _year.SelectedIndexChanged += async (_, _) => await PeriodChangedAsync();
    }

    private static void ConfigureCombo(ComboBox combo, params string[] items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.Margin = Padding.Empty;
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        UITheme.StyleInput(combo);
    }

    private Control BuildFiltersPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceMd),
            Margin = new Padding(0, UITheme.SpaceMd, 0, UITheme.SpaceMd)
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2,
            Margin = Padding.Empty
        };
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, UITheme.StandardControlHeight));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));

        AddFilterLabel(fields, "Buscar empleado", 0);
        AddFilterLabel(fields, "Estado", 1);
        AddFilterLabel(fields, "Mes", 2);
        AddFilterLabel(fields, "Año", 3);

        fields.Controls.Add(_search, 0, 1);
        fields.Controls.Add(_status, 1, 1);
        fields.Controls.Add(_month, 2, 1);
        fields.Controls.Add(_year, 3, 1);

        UITheme.StyleOutlineButton(_clearFilters);
        _clearFilters.Text = "Limpiar";
        _clearFilters.Dock = DockStyle.Fill;
        _clearFilters.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        _clearFilters.Click += async (_, _) => await ClearFiltersAsync();
        fields.Controls.Add(_clearFilters, 4, 1);

        UITheme.StyleOutlineButton(_refresh);
        _refresh.Text = "Actualizar";
        _refresh.Dock = DockStyle.Fill;
        _refresh.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        _refresh.Click += async (_, _) => await RefreshRowsAsync();
        fields.Controls.Add(_refresh, 5, 1);

        panel.Controls.Add(fields, 0, 0);
        return panel;
    }

    private static void AddFilterLabel(TableLayoutPanel fields, string text, int column)
    {
        fields.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, UITheme.SpaceSm, UITheme.SpaceXs)
        }, column, 0);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.DataSource = _binding;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                OpenDetail();
            }
        };
        _grid.SelectionChanged += (_, _) => UpdateDetailButton();
        _grid.CellFormatting += Grid_CellFormatting;
        UITheme.StyleDataGridView(_grid);

        AddGridColumn(nameof(EmployeeGridRow.FullName), "Nombre", 210, 2.1f);
        AddGridColumn(nameof(EmployeeGridRow.UserName), "Usuario", 140, 1.25f);
        AddGridColumn(nameof(EmployeeGridRow.State), "Estado", 100, 0.9f, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(nameof(EmployeeGridRow.CreatedAt), "Fecha de creación", 154, 1.35f);
        AddGridColumn(nameof(EmployeeGridRow.LastActivity), "Última actividad", 154, 1.4f);
        AddGridColumn(nameof(EmployeeGridRow.ActivityDays), "Días con actividad", 128, 1.1f, DataGridViewContentAlignment.MiddleCenter);
    }

    private void AddGridColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = property,
            DataPropertyName = property,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private void ConfigureButtons()
    {
        UITheme.StylePrimaryButton(_detail);
        _detail.Text = "Ver detalle";
        _detail.AutoSize = false;
        _detail.Width = 140;
        _detail.Enabled = false;
        _detail.Click += (_, _) => OpenDetail();

        UITheme.StyleOutlineButton(_ranking);
        _ranking.Text = "Ranking de ventas";
        _ranking.AutoSize = false;
        _ranking.Width = 210;
        _ranking.Click += (_, _) => OpenRanking();
    }

    private async Task RefreshRowsAsync()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            _loading = true;
            SetBusy(true);
            var (fromUtc, toUtc) = GetSelectedPeriodUtc();
            _rows = await _activity.GetEmployeesAsync(fromUtc, toUtc);

            if (_year.Items.Count == 1)
            {
                PopulateYears(_rows);
            }

            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudieron cargar los empleados.\n\n{ex.Message}",
                "Empleados",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _loading = false;
            SetBusy(false);
        }
    }

    private async Task PeriodChangedAsync()
    {
        if (_suppressFilterEvents || _loading)
        {
            return;
        }

        await RefreshRowsAsync();
    }

    private void PopulateYears(IEnumerable<EmployeeActivitySummary> rows)
    {
        var years = rows
            .SelectMany(row => new[]
            {
                row.CreatedAtUtc == default ? null : (int?)row.CreatedAtUtc.ToLocalTime().Year,
                row.LastLoginUtc.HasValue ? (int?)row.LastLoginUtc.Value.ToLocalTime().Year : null
            })
            .Where(year => year.HasValue)
            .Select(year => year!.Value)
            .Append(DateTime.Now.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();

        _suppressFilterEvents = true;
        try
        {
            _year.Items.Clear();
            _year.Items.Add("Todos");
            foreach (var year in years)
            {
                _year.Items.Add(year.ToString());
            }

            _year.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void ApplyFilter()
    {
        var search = _search.Text.Trim();
        var status = _status.SelectedItem?.ToString();
        var selectedMonth = GetSelectedMonth();
        var selectedYear = GetSelectedYear();

        var filtered = _rows.Where(row =>
            (string.IsNullOrWhiteSpace(search) ||
             row.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             row.UserName.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (status is "Todos" ||
             (status == "Activos" && row.Active) ||
             (status == "Inactivos" && !row.Active)));

        // A month without a year is a presentation-only filter over the
        // loaded activity. Once a year is selected, the service query above
        // supplies the period-aware activity-day count.
        if (selectedYear is null && selectedMonth is not null)
        {
            filtered = filtered.Where(row =>
                row.LastLoginUtc.HasValue &&
                row.LastLoginUtc.Value.ToLocalTime().Month == selectedMonth.Value);
        }

        var visible = filtered
            .OrderBy(row => row.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(row => new EmployeeGridRow(row))
            .ToList();
        _binding.DataSource = visible;

        var active = visible.Count(row => row.Source.Active);
        var inactive = visible.Count - active;
        var period = DescribePeriod(selectedMonth, selectedYear);
        _statusLabel.Text =
            $"{visible.Count} empleados · {active} activos · {inactive} inactivos · Periodo: {period}";
        UpdateDetailButton();
    }

    private async Task ClearFiltersAsync()
    {
        _suppressFilterEvents = true;
        try
        {
            _search.Clear();
            _status.SelectedIndex = 0;
            _month.SelectedIndex = 0;
            _year.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        await RefreshRowsAsync();
    }

    private void OpenDetail()
    {
        if (_grid.CurrentRow?.DataBoundItem is not EmployeeGridRow row)
        {
            return;
        }

        using var detail = new EmployeeActivityDetailForm(
            _activity,
            _sales,
            _payments,
            _paymentQueries,
            row.Source,
            GetSelectedSalesPeriod());
        detail.ShowDialog(this);
    }

    private void OpenRanking()
    {
        using var ranking = new EmployeeSalesRankingForm(_sales, GetSelectedSalesPeriod());
        ranking.ShowDialog(this);
    }

    private void UpdateDetailButton()
    {
        _detail.Enabled = _grid.CurrentRow?.DataBoundItem is EmployeeGridRow;
    }

    private void SetBusy(bool busy)
    {
        _search.Enabled = !busy;
        _status.Enabled = !busy;
        _month.Enabled = !busy;
        _year.Enabled = !busy;
        _clearFilters.Enabled = !busy;
        _refresh.Enabled = !busy;
        _ranking.Enabled = !busy;
        _grid.Enabled = !busy;
        if (busy)
        {
            _detail.Enabled = false;
        }
    }

    private (DateTimeOffset? FromUtc, DateTimeOffset? ToUtc) GetSelectedPeriodUtc()
    {
        var year = GetSelectedYear();
        if (year is null)
        {
            return (null, null);
        }

        var month = GetSelectedMonth();
        var localStart = new DateTime(
            year.Value,
            month ?? 1,
            1,
            0,
            0,
            0,
            DateTimeKind.Unspecified);
        var localEnd = month.HasValue
            ? localStart.AddMonths(1)
            : new DateTime(year.Value + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        return (ToUtcBoundary(localStart), ToUtcBoundary(localEnd));
    }

    private EmployeeSalesPeriod? GetSelectedSalesPeriod()
    {
        var year = GetSelectedYear() ?? DateTime.Now.Year;
        var month = GetSelectedMonth();

        if (GetSelectedYear() is null && month is null)
        {
            return null;
        }

        var start = new DateOnly(year, month ?? 1, 1);
        var end = month.HasValue
            ? start.AddMonths(1)
            : new DateOnly(year + 1, 1, 1);
        return new EmployeeSalesPeriod(start, end);
    }

    private static DateTimeOffset ToUtcBoundary(DateTime local)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private int? GetSelectedMonth()
        => _month.SelectedIndex <= 0 ? null : _month.SelectedIndex;

    private int? GetSelectedYear()
    {
        if (_year.SelectedIndex <= 0 || !int.TryParse(_year.SelectedItem?.ToString(), out var year))
        {
            return null;
        }

        return year;
    }

    private static string DescribePeriod(int? month, int? year)
    {
        if (month is null && year is null)
        {
            return "todos";
        }

        if (year is null)
        {
            return MonthNames[month!.Value];
        }

        return month is null
            ? year.Value.ToString()
            : $"{MonthNames[month.Value]} {year.Value}";
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != nameof(EmployeeGridRow.State))
        {
            return;
        }

        if (string.Equals(e.Value?.ToString(), "Activo", StringComparison.Ordinal))
        {
            e.CellStyle.ForeColor = UITheme.SuccessColor;
            e.CellStyle.Font = UITheme.NormalBoldFont;
        }
        else
        {
            e.CellStyle.ForeColor = UITheme.MutedTextColor;
        }
    }

    private sealed record EmployeeGridRow(EmployeeActivitySummary Source)
    {
        public string FullName => Source.FullName;
        public string UserName => Source.UserName;
        public string State => Source.Active ? "Activo" : "Inactivo";
        public string CreatedAt => FormatDate(Source.CreatedAtUtc);
        public string LastActivity => Source.LastLoginUtc.HasValue
            ? FormatDate(Source.LastLoginUtc.Value)
            : "No disponible";
        public int ActivityDays => Source.ActivityDays;

        private static string FormatDate(DateTime value)
            => value == default
                ? "No disponible"
                : value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }
}

/// <summary>
/// Employee detail view. Summary, activity, sales and administrative payments
/// are coordinated through application services; Forms do not use EF.
/// </summary>
public sealed class EmployeeActivityDetailForm : Form
{
    private static readonly string[] PaymentMonthNames =
    [
        "Todos",
        "Enero",
        "Febrero",
        "Marzo",
        "Abril",
        "Mayo",
        "Junio",
        "Julio",
        "Agosto",
        "Septiembre",
        "Octubre",
        "Noviembre",
        "Diciembre"
    ];

    private static readonly string[] CalendarMonthNames =
    [
        "Enero",
        "Febrero",
        "Marzo",
        "Abril",
        "Mayo",
        "Junio",
        "Julio",
        "Agosto",
        "Septiembre",
        "Octubre",
        "Noviembre",
        "Diciembre"
    ];

    private static readonly string[] CalendarWeekdayNames =
    [
        "Lun",
        "Mar",
        "Mié",
        "Jue",
        "Vie",
        "Sáb",
        "Dom"
    ];

    private readonly IEmployeeActivityQueryService _activity;
    private readonly IEmployeeSalesQueryService _sales;
    private readonly IEmployeePaymentService _payments;
    private readonly IEmployeePaymentQueryService _paymentQueries;
    private readonly EmployeeActivitySummary _employee;
    private readonly EmployeeSalesPeriod? _salesPeriod;
    private readonly DataGridView _historyGrid = new();
    private readonly BindingSource _historyBinding = new();
    private readonly Label _activityStatus = new();
    private readonly ComboBox _calendarMonth = new();
    private readonly ComboBox _calendarYear = new();
    private readonly Button _calendarPrevious = new();
    private readonly Button _calendarNext = new();
    private readonly TableLayoutPanel _calendarGrid = new();
    private readonly Label _calendarStatus = new();
    private readonly ToolTip _calendarToolTip = new();
    private readonly Label _salesStatus = new();
    private readonly Label _salesTotal = new();
    private readonly Label _salesCount = new();
    private readonly Label _salesAverage = new();
    private readonly Label _salesLast = new();
    private readonly CartesianChart _salesChart = new();
    private readonly DataGridView _salesGrid = new();
    private readonly BindingSource _salesBinding = new();
    private readonly DataGridView _paymentsGrid = new();
    private readonly BindingSource _paymentsBinding = new();
    private readonly Label _paymentsStatus = new();
    private readonly Label _paymentsTotal = new();
    private readonly Label _paymentsCount = new();
    private readonly Label _paymentsLast = new();
    private readonly Label _paymentsCancelled = new();
    private readonly CartesianChart _paymentsChart = new();
    private readonly ComboBox _paymentsGrouping = new();
    private readonly ComboBox _paymentsType = new();
    private readonly ComboBox _paymentsStatusFilter = new();
    private readonly ComboBox _paymentsMonth = new();
    private readonly ComboBox _paymentsYear = new();
    private readonly Button _paymentsRefresh = new();
    private readonly Button _paymentsRegister = new();
    private readonly Button _paymentsCancel = new();
    private readonly Label _summaryActivityLast = new();
    private readonly Label _summaryActivityDays = new();
    private readonly Label _summarySalesCount = new();
    private readonly Label _summarySalesTotal = new();
    private readonly Label _summarySalesAverage = new();
    private readonly Label _summaryPaymentsCount = new();
    private readonly Label _summaryPaymentsTotal = new();
    private readonly Label _summaryPaymentsLast = new();
    private EmployeeSalesSummary? _latestSalesSummary;
    private EmployeePaymentSummary? _latestPaymentSummary;
    private bool _paymentsLoading;
    private bool _suppressPaymentFilterEvents;
    private DateOnly _calendarPeriod;
    private bool _calendarLoading;
    private bool _suppressCalendarEvents;

    public EmployeeActivityDetailForm(
        IEmployeeActivityQueryService activity,
        IEmployeeSalesQueryService sales,
        IEmployeePaymentService payments,
        IEmployeePaymentQueryService paymentQueries,
        EmployeeActivitySummary employee,
        EmployeeSalesPeriod? salesPeriod = null)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _sales = sales ?? throw new ArgumentNullException(nameof(sales));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _paymentQueries = paymentQueries ?? throw new ArgumentNullException(nameof(paymentQueries));
        _employee = employee ?? throw new ArgumentNullException(nameof(employee));
        _salesPeriod = salesPeriod;
        BuildUi();
        Load += EmployeeActivityDetailForm_Load;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _calendarToolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void EmployeeActivityDetailForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var history = await _activity.GetLoginHistoryAsync(_employee.UserId);
            _historyBinding.DataSource = history
                .Select(EmployeeLoginGridRow.FromActivity)
                .ToList();
            _activityStatus.Text = history.Count == 0
                ? "Sin registros de actividad en el periodo seleccionado."
                : $"{history.Count:N0} logins registrados · {_employee.ActivityDays:N0} días con actividad";
            await LoadCalendarAsync();
            await LoadSalesAsync();
            await LoadPaymentsAsync();
        }
        catch (Exception ex)
        {
            _activityStatus.Text = "No se pudo cargar el historial de actividad.";
            MessageBox.Show(
                $"No se pudo cargar la actividad del empleado.\n\n{ex.Message}",
                "Actividad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void BuildUi()
    {
        var fullName = DisplayText(_employee.FullName);
        var userName = DisplayText(_employee.UserName);
        Text = $"Empleado · {fullName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1280, 740);
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 680);
        UITheme.StyleForm(this);

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty };
        UITheme.StyleTabControl(tabs);
        tabs.TabPages.Add(BuildSummaryTab());
        tabs.TabPages.Add(BuildActivityTab());
        tabs.TabPages.Add(BuildSalesTab());
        tabs.TabPages.Add(BuildPaymentsTab());

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(
            VisualLayout.CreatePageHeader(
                fullName,
                $"Usuario: {userName} · {_employee.Active switch { true => "Cuenta activa", false => "Cuenta inactiva" }}"),
            0,
            0);
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);
    }

    private TabPage BuildSummaryTab()
    {
        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            MinimumSize = new Size(0, 80),
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(UITheme.SpaceSm),
            BackColor = UITheme.SurfaceColor
        };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        identity.Controls.Add(CreateInfoCard("Nombre", _employee.FullName), 0, 0);
        identity.Controls.Add(CreateInfoCard("Usuario", _employee.UserName), 1, 0);
        identity.Controls.Add(CreateInfoCard("Estado", _employee.Active ? "Cuenta activa" : "Cuenta inactiva"), 2, 0);

        _summaryActivityLast.Text = _employee.LastLoginUtc.HasValue ? FormatDate(_employee.LastLoginUtc.Value) : "No disponible";
        _summaryActivityDays.Text = HasActivity(_employee.LastLoginUtc, _employee.ActivityDays)
            ? _employee.ActivityDays.ToString("N0")
            : "Sin registros";
        SetSummaryLoading(_summarySalesCount, _summarySalesTotal, _summarySalesAverage,
            _summaryPaymentsCount, _summaryPaymentsTotal, _summaryPaymentsLast);

        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(UITheme.SpaceSm),
            BackColor = UITheme.BackgroundColor
        };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < content.RowCount; row++)
        {
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        content.Controls.Add(CreateSummarySection(
            "Cuenta",
            "Identidad y estado de la cuenta.",
            identity), 0, 0);
        content.Controls.Add(CreateStatisticsSection(
            "Actividad",
            "Accesos registrados en el periodo seleccionado.",
            ("Último login", _summaryActivityLast),
            ("Días con actividad", _summaryActivityDays)), 0, 1);
        content.Controls.Add(CreateStatisticsSection(
            "Ventas",
            "Métrica comercial; no se mezcla con pagos ni actividad.",
            ("Número de ventas", _summarySalesCount),
            ("Total vendido", _summarySalesTotal),
            ("Venta promedio", _summarySalesAverage)), 0, 2);
        content.Controls.Add(CreateStatisticsSection(
            "Pagos",
            "Pagos administrativos activos; los anulados no suman al total.",
            60,
            ("Cantidad de pagos", _summaryPaymentsCount),
            ("Total pagado", _summaryPaymentsTotal),
            ("Último pago", _summaryPaymentsLast)), 0, 3);

        viewport.Controls.Add(content);

        var page = new TabPage("Resumen") { BackColor = UITheme.BackgroundColor, Padding = new Padding(UITheme.SpaceMd) };
        page.Controls.Add(viewport);
        return page;
    }

    private static Control CreateSummarySection(string title, string description, Control content)
    {
        var section = VisualLayout.CreateSection(title, description, content);
        section.Dock = DockStyle.Top;
        section.AutoSize = true;
        section.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        return section;
    }

    private static Control CreateStatisticsSection(
        string title,
        string description,
        params (string Caption, Label Value)[] metrics)
        => CreateStatisticsSection(title, description, 72, metrics);

    private static Control CreateStatisticsSection(
        string title,
        string description,
        int minimumHeight,
        params (string Caption, Label Value)[] metrics)
    {
        if (metrics.Length == 0)
        {
            throw new ArgumentException("Debe existir al menos una métrica.", nameof(metrics));
        }

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = metrics.Length,
            RowCount = 1,
            MinimumSize = new Size(0, minimumHeight),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        foreach (var metric in metrics)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / metrics.Length));
            row.Controls.Add(CreateMetricCard(metric.Caption, metric.Value, UITheme.SpaceSm, captionAutoSize: false));
        }

        return CreateSummarySection(title, description, row);
    }

    private static void SetSummaryLoading(params Label[] labels)
    {
        foreach (var label in labels)
        {
            label.Text = "Cargando...";
        }
    }

    private TabPage BuildActivityTab()
    {
        _historyGrid.Dock = DockStyle.Fill;
        _historyGrid.AutoGenerateColumns = false;
        _historyGrid.AllowUserToAddRows = false;
        _historyGrid.AllowUserToDeleteRows = false;
        _historyGrid.AllowUserToResizeRows = false;
        _historyGrid.MultiSelect = false;
        _historyGrid.ReadOnly = true;
        _historyGrid.RowHeadersVisible = false;
        _historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _historyGrid.DataSource = _historyBinding;
        UITheme.StyleDataGridView(_historyGrid);
        AddGridColumn(_historyGrid, nameof(EmployeeLoginGridRow.Date), "Fecha", 140, 1.1f);
        AddGridColumn(_historyGrid, nameof(EmployeeLoginGridRow.Time), "Hora", 110, 0.9f);
        AddGridColumn(_historyGrid, nameof(EmployeeLoginGridRow.Station), "Estación", 220, 1.7f);
        AddGridColumn(_historyGrid, nameof(EmployeeLoginGridRow.StationMode), "Modo de estación", 180, 1.3f);

        UITheme.StyleStatusLabel(_activityStatus, UITheme.SecondaryColor);
        _activityStatus.Dock = DockStyle.Fill;
        _activityStatus.Text = "Cargando historial...";

        var historyContent = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceSm)
        };
        historyContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        historyContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyContent.Controls.Add(_activityStatus, 0, 0);
        historyContent.Controls.Add(_historyGrid, 0, 1);

        var sections = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty
        };
        sections.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        sections.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        sections.Controls.Add(BuildCalendarSection(), 0, 0);
        sections.Controls.Add(
            VisualLayout.CreateSection(
                "Historial de actividad",
                "Cada login se conserva; varios logins del mismo día cuentan como un solo día con actividad.",
                historyContent),
            0,
            1);

        var page = new TabPage("Actividad") { BackColor = UITheme.BackgroundColor, Padding = new Padding(UITheme.SpaceMd) };
        page.Controls.Add(sections);
        return page;
    }

    private TabPage BuildSalesTab()
    {
        ConfigureSalesGrid();
        ConfigureSalesChart();

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        for (var column = 0; column < metrics.ColumnCount; column++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        metrics.Controls.Add(CreateMetricCard("Total vendido", _salesTotal, UITheme.SpaceMd, captionAutoSize: true), 0, 0);
        metrics.Controls.Add(CreateMetricCard("Número de ventas", _salesCount, UITheme.SpaceMd, captionAutoSize: true), 1, 0);
        metrics.Controls.Add(CreateMetricCard("Venta promedio", _salesAverage, UITheme.SpaceMd, captionAutoSize: true), 2, 0);
        metrics.Controls.Add(CreateMetricCard("Última venta", _salesLast, UITheme.SpaceMd, captionAutoSize: true), 3, 0);

        UITheme.StyleStatusLabel(_salesStatus, UITheme.SecondaryColor);
        _salesStatus.Dock = DockStyle.Fill;
        _salesStatus.Text = "Cargando ventas...";
        _salesStatus.TextAlign = ContentAlignment.MiddleLeft;
        _salesStatus.Margin = new Padding(0, UITheme.SpaceXs, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(UITheme.SpaceMd)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        content.Controls.Add(metrics, 0, 0);
        content.Controls.Add(VisualLayout.CreateSection(
            "Evolución diaria",
            "Ventas completadas atribuidas a este usuario en el periodo seleccionado.",
            _salesChart), 0, 1);
        content.Controls.Add(VisualLayout.CreateSection(
            "Detalle del periodo",
            "Las ventas anuladas no forman parte de los totales.",
            _salesGrid), 0, 2);
        content.Controls.Add(_salesStatus, 0, 3);

        var page = new TabPage("Ventas")
        {
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(UITheme.SpaceSm)
        };
        page.Controls.Add(content);
        return page;
    }

    private void ConfigureSalesGrid()
    {
        _salesGrid.Dock = DockStyle.Fill;
        _salesGrid.AutoGenerateColumns = false;
        _salesGrid.AllowUserToAddRows = false;
        _salesGrid.AllowUserToDeleteRows = false;
        _salesGrid.AllowUserToResizeRows = false;
        _salesGrid.MultiSelect = false;
        _salesGrid.ReadOnly = true;
        _salesGrid.RowHeadersVisible = false;
        _salesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _salesGrid.DataSource = _salesBinding;
        UITheme.StyleDataGridView(_salesGrid);
        AddGridColumn(_salesGrid, nameof(EmployeeSalesGridRow.Date), "Fecha", 130, 1.1f);
        AddGridColumn(_salesGrid, nameof(EmployeeSalesGridRow.SaleCount), "Ventas", 100, 0.8f, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_salesGrid, nameof(EmployeeSalesGridRow.Total), "Total vendido", 160, 1.2f, DataGridViewContentAlignment.MiddleRight);
    }

    private void ConfigureSalesChart()
    {
        _salesChart.Dock = DockStyle.Fill;
        _salesChart.BackColor = UITheme.SurfaceColor;
        _salesChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Top;
        _salesChart.ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None;
        _salesChart.AnimationsSpeed = TimeSpan.FromMilliseconds(200);
        _salesChart.Series = Array.Empty<ISeries>();
    }

    private static Control CreateMetricCard(
        string caption,
        Label valueLabel,
        int padding,
        bool captionAutoSize)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceMutedColor,
            Padding = new Padding(padding),
            Margin = new Padding(UITheme.SpaceXs)
        };
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            AutoSize = captionAutoSize,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = Padding.Empty
        }, 0, 0);
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.AutoEllipsis = true;
        valueLabel.Font = UITheme.NormalBoldFont;
        valueLabel.ForeColor = UITheme.TextDark;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.Margin = new Padding(0, UITheme.SpaceXs, 0, 0);
        card.Controls.Add(valueLabel, 0, 1);
        return card;
    }

    private async Task LoadSalesAsync()
    {
        try
        {
            _salesStatus.Text = "Ventas: cargando...";
            SetSalesBusy(true);
            var report = await _sales.GetEmployeeSalesAsync(_employee.UserId, _salesPeriod);
            var summary = report.Summary;
            _latestSalesSummary = summary;
            if (summary.SaleCount == 0)
            {
                _salesTotal.Text = "Sin registros";
                _salesCount.Text = "Sin registros";
                _salesAverage.Text = "Sin registros";
                _salesLast.Text = "Sin registros";
            }
            else
            {
                _salesTotal.Text = FormatMoney(summary.TotalSoldCentavos);
                _salesCount.Text = summary.SaleCount.ToString("N0");
                _salesAverage.Text = FormatMoney(summary.AverageTicketCentavos);
                _salesLast.Text = summary.LastSaleUtc.HasValue
                    ? FormatDate(summary.LastSaleUtc.Value)
                    : "No disponible";
            }

            var rows = report.DailyTrend
                .Select(point => new EmployeeSalesGridRow(
                    point.LocalPeriodStart.ToString("dd/MM/yyyy"),
                    point.SaleCount.ToString("N0"),
                    FormatMoney(point.TotalSoldCentavos)))
                .ToList();
            _salesBinding.DataSource = rows;
            RenderSalesChart(report.DailyTrend);
            _salesStatus.Text = summary.SaleCount == 0
                ? "Sin ventas completadas en el periodo seleccionado."
                : $"{summary.SaleCount:N0} ventas completadas · Periodo: {DescribeSalesPeriod(_salesPeriod)}";
            UpdateCombinedSummary();
        }
        catch (Exception ex)
        {
            _latestSalesSummary = null;
            _salesTotal.Text = "No disponible";
            _salesCount.Text = "No disponible";
            _salesAverage.Text = "No disponible";
            _salesLast.Text = "No disponible";
            _salesBinding.DataSource = Array.Empty<EmployeeSalesGridRow>();
            _salesChart.Series = Array.Empty<ISeries>();
            _salesStatus.Text = "Ventas: no disponible.";
            SetSummaryUnavailable(_summarySalesCount, _summarySalesTotal, _summarySalesAverage);
            MessageBox.Show(
                $"No se pudieron cargar las ventas del empleado.\n\n{ex.Message}",
                "Ventas",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            SetSalesBusy(false);
        }
    }

    private void RenderSalesChart(IReadOnlyList<EmployeeSalesTrendPoint> trend)
    {
        _salesChart.Series = trend.Count == 0
            ? Array.Empty<ISeries>()
            : new ISeries[]
            {
                new LineSeries<long>
                {
                    Name = "Total vendido",
                    Values = trend.Select(point => point.TotalSoldCentavos).ToArray(),
                    GeometrySize = 5,
                    LineSmoothness = 0.25,
                    Stroke = new SolidColorPaint(ToSkColor(UITheme.PrimaryColor), 2),
                    GeometryFill = new SolidColorPaint(SKColors.White),
                    GeometryStroke = new SolidColorPaint(ToSkColor(UITheme.PrimaryColor), 2),
                    Fill = new SolidColorPaint(new SKColor(UITheme.PrimaryColor.R, UITheme.PrimaryColor.G, UITheme.PrimaryColor.B, 28)),
                    YToolTipLabelFormatter = point => FormatMoney((long)point.Coordinate.PrimaryValue)
                }
            };
        _salesChart.XAxes =
        [
            new Axis
            {
                Labels = trend.Select(point => point.LocalPeriodStart.ToString("dd/MM")).ToArray(),
                LabelsRotation = trend.Count > 16 ? 35 : 0,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ToSkColor(UITheme.MutedTextColor)),
                SeparatorsPaint = null
            }
        ];
        _salesChart.YAxes =
        [
            new Axis
            {
                TextSize = 10,
                Labeler = value => FormatMoney((long)value),
                LabelsPaint = new SolidColorPaint(ToSkColor(UITheme.MutedTextColor)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(235, 229, 233), 1)
            }
        ];
    }

    private void SetSalesBusy(bool busy)
    {
        _salesGrid.Enabled = !busy;
        _salesChart.Enabled = !busy;
    }

    private TabPage BuildPaymentsTab()
    {
        ConfigurePaymentsControls();
        ConfigurePaymentsChart();

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        for (var column = 0; column < metrics.ColumnCount; column++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        metrics.Controls.Add(CreateMetricCard("Total pagado", _paymentsTotal, UITheme.SpaceSm, captionAutoSize: false), 0, 0);
        metrics.Controls.Add(CreateMetricCard("Cantidad de pagos", _paymentsCount, UITheme.SpaceSm, captionAutoSize: false), 1, 0);
        metrics.Controls.Add(CreateMetricCard("Último pago", _paymentsLast, UITheme.SpaceSm, captionAutoSize: false), 2, 0);
        metrics.Controls.Add(CreateMetricCard("Anulados", _paymentsCancelled, UITheme.SpaceSm, captionAutoSize: false), 3, 0);

        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, UITheme.SpaceSm, 0, UITheme.SpaceSm),
            Padding = new Padding(UITheme.SpaceSm)
        };
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, UITheme.StandardControlHeight));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        filters.Controls.Add(new Label
        {
            Text = "Tipo de pago",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, UITheme.SpaceSm, UITheme.SpaceXs)
        }, 0, 0);
        filters.Controls.Add(new Label
        {
            Text = "Estado",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, UITheme.SpaceSm, UITheme.SpaceXs)
        }, 1, 0);
        filters.Controls.Add(new Label
        {
            Text = "Mes",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, UITheme.SpaceSm, UITheme.SpaceXs)
        }, 2, 0);
        filters.Controls.Add(new Label
        {
            Text = "Año",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, UITheme.SpaceSm, UITheme.SpaceXs)
        }, 3, 0);
        filters.Controls.Add(new Label
        {
            Text = "Agrupación",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, UITheme.SpaceSm, UITheme.SpaceXs)
        }, 4, 0);
        filters.Controls.Add(new Label
        {
            Text = _salesPeriod is null ? "Periodo: todo el historial" :
                $"Periodo inicial: {_salesPeriod.StartDate:dd/MM/yyyy} - {_salesPeriod.EndDateExclusive.AddDays(-1):dd/MM/yyyy}",
            Dock = DockStyle.Fill,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            AutoSize = false,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, UITheme.SpaceSm, UITheme.SpaceXs)
        }, 5, 0);
        filters.Controls.Add(_paymentsType, 0, 1);
        filters.Controls.Add(_paymentsStatusFilter, 1, 1);
        filters.Controls.Add(_paymentsMonth, 2, 1);
        filters.Controls.Add(_paymentsYear, 3, 1);
        filters.Controls.Add(_paymentsGrouping, 4, 1);
        filters.Controls.Add(_paymentsRefresh, 6, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, UITheme.SpaceSm, 0, 0),
            Margin = Padding.Empty
        };
        UITheme.StylePrimaryButton(_paymentsRegister);
        _paymentsRegister.Text = "Registrar pago";
        _paymentsRegister.Size = new Size(180, 40);
        _paymentsRegister.Click += RegisterPaymentAsync;
        UITheme.StyleOutlineButton(_paymentsCancel);
        _paymentsCancel.Text = "Anular pago";
        _paymentsCancel.Size = new Size(135, 40);
        _paymentsCancel.Click += CancelPaymentAsync;
        actions.Controls.Add(_paymentsRegister);
        actions.Controls.Add(_paymentsCancel);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(UITheme.SpaceSm)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        content.Controls.Add(metrics, 0, 0);
        content.Controls.Add(filters, 0, 1);
        content.Controls.Add(VisualLayout.CreateSection(
            "Pagos activos por periodo",
            "Los pagos anulados se conservan en el historial y no suman en esta gráfica.",
            _paymentsChart), 0, 2);
        content.Controls.Add(VisualLayout.CreateSection(
            "Historial de pagos",
            "Los registros anulados permanecen visibles con su motivo y trazabilidad.",
            _paymentsGrid), 0, 3);
        UITheme.StyleStatusLabel(_paymentsStatus, UITheme.SecondaryColor);
        _paymentsStatus.Dock = DockStyle.Fill;
        _paymentsStatus.Text = "Pagos: cargando...";
        content.Controls.Add(_paymentsStatus, 0, 4);
        content.Controls.Add(actions, 0, 5);

        var page = new TabPage("Pagos")
        {
            BackColor = UITheme.BackgroundColor,
            Padding = new Padding(UITheme.SpaceMd)
        };
        page.Controls.Add(content);
        return page;
    }

    private void ConfigurePaymentsControls()
    {
        _suppressPaymentFilterEvents = true;
        try
        {
            ConfigurePaymentCombo(_paymentsType, ["Todos", "Diario", "Semanal", "Quincenal", "Mensual", "Extraordinario"]);
            ConfigurePaymentCombo(_paymentsStatusFilter, ["Todos", "Activo", "Anulado"]);
            ConfigurePaymentCombo(_paymentsMonth, PaymentMonthNames);
            ConfigurePaymentCombo(_paymentsGrouping, ["Diario", "Semanal", "Quincenal", "Mensual"]);
            var currentYear = DateTime.Now.Year;
            var years = Enumerable.Range(Math.Max(1, currentYear - 5), 11)
                .Append(_salesPeriod?.StartDate.Year ?? currentYear)
                .Distinct()
                .OrderByDescending(year => year)
                .Select(year => year.ToString())
                .Prepend("Todos")
                .ToArray();
            ConfigurePaymentCombo(_paymentsYear, years);
            if (_salesPeriod is not null)
            {
                _paymentsMonth.SelectedIndex = _salesPeriod.StartDate.Month;
                var yearText = _salesPeriod.StartDate.Year.ToString();
                var yearIndex = _paymentsYear.Items.IndexOf(yearText);
                if (yearIndex >= 0)
                {
                    _paymentsYear.SelectedIndex = yearIndex;
                }
            }
        }
        finally
        {
            _suppressPaymentFilterEvents = false;
        }

        _paymentsType.SelectedIndexChanged += async (_, _) => await PaymentFilterChangedAsync();
        _paymentsStatusFilter.SelectedIndexChanged += async (_, _) => await PaymentFilterChangedAsync();
        _paymentsMonth.SelectedIndexChanged += async (_, _) => await PaymentFilterChangedAsync();
        _paymentsYear.SelectedIndexChanged += async (_, _) => await PaymentFilterChangedAsync();
        _paymentsGrouping.SelectedIndexChanged += async (_, _) => await PaymentFilterChangedAsync();
        UITheme.StyleOutlineButton(_paymentsRefresh);
        _paymentsRefresh.Text = "Actualizar";
        _paymentsRefresh.Dock = DockStyle.Fill;
        _paymentsRefresh.Click += async (_, _) => await LoadPaymentsAsync();

        _paymentsGrid.Dock = DockStyle.Fill;
        _paymentsGrid.AutoGenerateColumns = false;
        _paymentsGrid.AllowUserToAddRows = false;
        _paymentsGrid.AllowUserToDeleteRows = false;
        _paymentsGrid.AllowUserToResizeRows = false;
        _paymentsGrid.MultiSelect = false;
        _paymentsGrid.ReadOnly = true;
        _paymentsGrid.RowHeadersVisible = false;
        _paymentsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _paymentsGrid.DataSource = _paymentsBinding;
        _paymentsGrid.SelectionChanged += (_, _) => UpdatePaymentCancelButton();
        UITheme.StyleDataGridView(_paymentsGrid);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.Date), "Fecha", 115, 1.0f);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.Type), "Tipo", 110, 1.0f);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.Period), "Periodo", 155, 1.4f);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.Days), "Días considerados", 120, 1.0f, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.Amount), "Monto", 105, 1.0f, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.Funding), "Origen / método / efecto", 260, 1.8f);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.RegisteredBy), "Registrado por", 125, 1.1f);
        AddGridColumn(_paymentsGrid, nameof(EmployeePaymentGridRow.State), "Estado", 105, 0.9f, DataGridViewContentAlignment.MiddleCenter);
        _paymentsGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                _paymentsGrid.Columns[e.ColumnIndex].Name != nameof(EmployeePaymentGridRow.State))
            {
                return;
            }

            e.CellStyle.ForeColor = string.Equals(e.Value?.ToString(), "Activo", StringComparison.Ordinal)
                ? UITheme.SuccessColor
                : UITheme.MutedTextColor;
            e.CellStyle.Font = UITheme.NormalBoldFont;
        };
    }

    private void ConfigurePaymentsChart()
    {
        _paymentsChart.Dock = DockStyle.Fill;
        _paymentsChart.BackColor = UITheme.SurfaceColor;
        _paymentsChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Top;
        _paymentsChart.ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None;
        _paymentsChart.AnimationsSpeed = TimeSpan.FromMilliseconds(200);
        _paymentsChart.Series = Array.Empty<ISeries>();
    }

    private void RenderPaymentsChart(
        IReadOnlyList<EmployeePaymentTrendPoint> trend,
        EmployeePaymentGrouping grouping)
    {
        _paymentsChart.Series = trend.Count == 0
            ? Array.Empty<ISeries>()
            : new ISeries[]
            {
                new ColumnSeries<long>
                {
                    Name = "Total pagado",
                    Values = trend.Select(point => point.TotalPaidCentavos).ToArray(),
                    Fill = new SolidColorPaint(ToSkColor(UITheme.PrimaryColor)),
                    Stroke = null,
                    YToolTipLabelFormatter = point => FormatMoney((long)point.Coordinate.PrimaryValue)
                }
            };
        _paymentsChart.XAxes =
        [
            new Axis
            {
                Labels = trend.Select(point => PaymentTrendLabel(point.LocalPeriodStart, grouping)).ToArray(),
                LabelsRotation = trend.Count > 16 ? 35 : 0,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ToSkColor(UITheme.MutedTextColor)),
                SeparatorsPaint = null
            }
        ];
        _paymentsChart.YAxes =
        [
            new Axis
            {
                TextSize = 10,
                Labeler = value => FormatMoney((long)value),
                LabelsPaint = new SolidColorPaint(ToSkColor(UITheme.MutedTextColor)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(235, 229, 233), 1)
            }
        ];
    }

    private static string PaymentTrendLabel(DateOnly start, EmployeePaymentGrouping grouping) => grouping switch
    {
        EmployeePaymentGrouping.Month => start.ToString("MM/yyyy"),
        EmployeePaymentGrouping.Biweekly => start.Day == 1 ? $"01-15/{start:MM}" : $"16-fin/{start:MM}",
        EmployeePaymentGrouping.Week => $"Sem. {start:dd/MM}",
        _ => start.ToString("dd/MM")
    };

    private static void ConfigurePaymentCombo(ComboBox combo, string[] items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.Margin = Padding.Empty;
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        UITheme.StyleInput(combo);
    }

    private async Task LoadPaymentsAsync()
    {
        if (_paymentsLoading || IsDisposed)
        {
            return;
        }

        try
        {
            _paymentsLoading = true;
            SetPaymentsBusy(true);
            _paymentsStatus.Text = "Pagos: cargando...";
            var filter = GetPaymentFilter();
            var summary = await _paymentQueries.GetSummaryAsync(
                _employee.UserId,
                filter with { Status = null });
            var combinedSummary = await _paymentQueries.GetSummaryAsync(
                _employee.UserId,
                GetSummaryPaymentFilter());
            var history = await _paymentQueries.GetEmployeeHistoryAsync(_employee.UserId, filter);
            var trend = await _paymentQueries.GetPaymentTrendAsync(
                _employee.UserId,
                filter,
                GetPaymentGrouping());
            _latestPaymentSummary = combinedSummary;
            _paymentsTotal.Text = FormatMoney(summary.TotalPaidCentavos);
            _paymentsCount.Text = summary.PaymentCount.ToString("N0");
            _paymentsLast.Text = summary.LastPaymentCreatedAtUtc.HasValue
                ? summary.LastPaymentCreatedAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy")
                : "No disponible";
            _paymentsCancelled.Text = summary.CancelledPaymentCount.ToString("N0");
            _paymentsBinding.DataSource = history.Select(EmployeePaymentGridRow.FromRow).ToList();
            RenderPaymentsChart(trend, GetPaymentGrouping());
            _paymentsStatus.Text = history.Count == 0
                ? "No hay pagos en el periodo seleccionado."
                : $"{history.Count:N0} registros · {summary.PaymentCount:N0} activos · {summary.CancelledPaymentCount:N0} anulados";
            _paymentsRegister.Visible = await _payments.CanRegisterAsync();
            _paymentsCancel.Visible = await _payments.CanCancelAsync();
            UpdateCombinedSummary();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("permiso", StringComparison.OrdinalIgnoreCase))
        {
            _paymentsBinding.DataSource = Array.Empty<EmployeePaymentGridRow>();
            _paymentsChart.Series = Array.Empty<ISeries>();
            _latestPaymentSummary = null;
            _paymentsTotal.Text = "No disponible";
            _paymentsCount.Text = "No disponible";
            _paymentsLast.Text = "No disponible";
            _paymentsCancelled.Text = "No disponible";
            _paymentsRegister.Visible = false;
            _paymentsCancel.Visible = false;
            _paymentsStatus.Text = "No tiene permiso para consultar pagos de empleados.";
            SetSummaryUnavailable(_summaryPaymentsCount, _summaryPaymentsTotal, _summaryPaymentsLast);
        }
        catch (Exception ex)
        {
            _latestPaymentSummary = null;
            _paymentsChart.Series = Array.Empty<ISeries>();
            _paymentsStatus.Text = "Pagos: no disponible.";
            SetSummaryUnavailable(_summaryPaymentsCount, _summaryPaymentsTotal, _summaryPaymentsLast);
            MessageBox.Show(
                $"No se pudo cargar el historial de pagos.\n\n{ex.Message}",
                "Pagos de empleados",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _paymentsLoading = false;
            SetPaymentsBusy(false);
            UpdatePaymentCancelButton();
        }
    }

    private async Task PaymentFilterChangedAsync()
    {
        if (_suppressPaymentFilterEvents || _paymentsLoading || IsDisposed)
        {
            return;
        }

        await LoadPaymentsAsync();
    }

    private EmployeePaymentFilter GetPaymentFilter()
    {
        var type = _paymentsType.SelectedIndex switch
        {
            1 => EmployeePaymentType.Daily,
            2 => EmployeePaymentType.Weekly,
            3 => EmployeePaymentType.Biweekly,
            4 => EmployeePaymentType.Monthly,
            5 => EmployeePaymentType.Extraordinary,
            _ => (EmployeePaymentType?)null
        };
        var status = _paymentsStatusFilter.SelectedIndex switch
        {
            1 => EmployeePaymentStatus.Active,
            2 => EmployeePaymentStatus.Cancelled,
            _ => (EmployeePaymentStatus?)null
        };
        var year = _paymentsYear.SelectedIndex > 0 &&
            int.TryParse(_paymentsYear.SelectedItem?.ToString(), out var selectedYear)
            ? selectedYear
            : (int?)null;
        var month = _paymentsMonth.SelectedIndex > 0 ? _paymentsMonth.SelectedIndex : (int?)null;
        DateOnly? from = null;
        DateOnly? to = null;
        if (year.HasValue)
        {
            var localStart = new DateOnly(year.Value, month ?? 1, 1);
            from = localStart;
            to = month.HasValue ? localStart.AddMonths(1) : new DateOnly(year.Value + 1, 1, 1);
        }
        else if (month.HasValue)
        {
            var localStart = new DateOnly(DateTime.Now.Year, month.Value, 1);
            from = localStart;
            to = localStart.AddMonths(1);
        }
        else if (_salesPeriod is not null)
        {
            from = _salesPeriod.StartDate;
            to = _salesPeriod.EndDateExclusive;
        }

        return new EmployeePaymentFilter(
            _employee.UserId,
            from,
            to,
            type,
            status);
    }

    private EmployeePaymentFilter GetSummaryPaymentFilter()
        => new(
            _employee.UserId,
            _salesPeriod?.StartDate,
            _salesPeriod?.EndDateExclusive,
            Status: null);

    private EmployeePaymentGrouping GetPaymentGrouping() => _paymentsGrouping.SelectedIndex switch
    {
        1 => EmployeePaymentGrouping.Week,
        2 => EmployeePaymentGrouping.Biweekly,
        3 => EmployeePaymentGrouping.Month,
        _ => EmployeePaymentGrouping.Day
    };

    private void UpdateCombinedSummary()
    {
        if (_latestSalesSummary is null || _latestPaymentSummary is null)
        {
            return;
        }

        var summary = EmployeeStatisticsSummary.Combine(
            _employee,
            _latestSalesSummary,
            _latestPaymentSummary);
        _summaryActivityLast.Text = summary.LastLoginUtc.HasValue
            ? FormatDate(summary.LastLoginUtc.Value)
            : "No disponible";
        _summaryActivityDays.Text = HasActivity(summary.LastLoginUtc, summary.ActivityDays)
            ? summary.ActivityDays.ToString("N0")
            : "Sin registros";
        if (summary.SaleCount == 0)
        {
            _summarySalesCount.Text = "Sin registros";
            _summarySalesTotal.Text = "Sin registros";
            _summarySalesAverage.Text = "Sin registros";
        }
        else
        {
            _summarySalesCount.Text = summary.SaleCount.ToString("N0");
            _summarySalesTotal.Text = FormatMoney(summary.TotalSoldCentavos);
            _summarySalesAverage.Text = FormatMoney(summary.AverageTicketCentavos);
        }
        _summaryPaymentsCount.Text = summary.PaymentCount.ToString("N0");
        _summaryPaymentsTotal.Text = FormatMoney(summary.TotalPaidCentavos);
        _summaryPaymentsLast.Text = summary.LastPaymentUtc.HasValue
            ? FormatDate(summary.LastPaymentUtc.Value)
            : "No disponible";
    }

    private static void SetSummaryUnavailable(params Label[] labels)
    {
        foreach (var label in labels)
        {
            label.Text = "No disponible";
        }
    }

    private async void RegisterPaymentAsync(object? sender, EventArgs e)
    {
        using var dialog = new EmployeePaymentForm(_employee, _payments);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await LoadPaymentsAsync();
        }
    }

    private async void CancelPaymentAsync(object? sender, EventArgs e)
    {
        if (_paymentsGrid.CurrentRow?.DataBoundItem is not EmployeePaymentGridRow row ||
            row.Status != EmployeePaymentStatus.Active)
        {
            return;
        }

        using var reasonDialog = new EmployeePaymentCancellationForm();
        if (reasonDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _payments.CancelPaymentAsync(new CancelEmployeePaymentRequest(row.PaymentId, reasonDialog.Reason));
            MessageBox.Show(
                "Pago anulado administrativamente. El registro permanece en el historial; si el efectivo ya salió de caja, la anulación no lo devuelve.",
                "Pagos de empleados",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            await LoadPaymentsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Pagos de empleados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetPaymentsBusy(bool busy)
    {
        _paymentsType.Enabled = !busy;
        _paymentsStatusFilter.Enabled = !busy;
        _paymentsMonth.Enabled = !busy;
        _paymentsYear.Enabled = !busy;
        _paymentsGrouping.Enabled = !busy;
        _paymentsRefresh.Enabled = !busy;
        _paymentsGrid.Enabled = !busy;
        _paymentsChart.Enabled = !busy;
        _paymentsRegister.Enabled = !busy;
        _paymentsCancel.Enabled = !busy;
    }

    private void UpdatePaymentCancelButton()
    {
        _paymentsCancel.Enabled = !_paymentsLoading &&
            _paymentsCancel.Visible &&
            _paymentsGrid.CurrentRow?.DataBoundItem is EmployeePaymentGridRow row &&
            row.Status == EmployeePaymentStatus.Active;
    }

    private static string PaymentTypeLabel(EmployeePaymentType type) => type switch
    {
        EmployeePaymentType.Daily => "Diario",
        EmployeePaymentType.Weekly => "Semanal",
        EmployeePaymentType.Biweekly => "Quincenal",
        EmployeePaymentType.Monthly => "Mensual",
        EmployeePaymentType.Extraordinary => "Extraordinario",
        _ => type.ToString()
    };

    private static string PaymentOriginLabel(OrigenFondos? origin) => origin switch
    {
        OrigenFondos.CAJA_FISICA => "Caja física",
        OrigenFondos.FONDOS_EXTERNOS_DUENO => "Fondos del dueño",
        OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA => "Negocio fuera de caja",
        _ => "No especificado (histórico)"
    };

    private static string PaymentMethodLabel(MetodoPago? method) => method switch
    {
        MetodoPago.EFECTIVO => "Efectivo",
        MetodoPago.TRANSFERENCIA => "Transferencia",
        MetodoPago.BITCOIN => "Bitcoin",
        _ => "Método no especificado"
    };

    private sealed record EmployeePaymentGridRow(
        int PaymentId,
        string Date,
        string Type,
        string Period,
        string Days,
        string Amount,
        string Funding,
        string RegisteredBy,
        string State,
        EmployeePaymentStatus Status)
    {
        public static EmployeePaymentGridRow FromRow(EmployeePaymentHistoryRow row)
        {
            var period = row.PeriodStart.HasValue || row.PeriodEnd.HasValue
                ? $"{row.PeriodStart?.ToString("dd/MM/yyyy") ?? "-"} - {row.PeriodEnd?.ToString("dd/MM/yyyy") ?? "-"}"
                : "No definido";
            return new EmployeePaymentGridRow(
                row.PaymentId,
                row.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy"),
                PaymentTypeLabel(row.PaymentType),
                period,
                row.DaysConsidered?.ToString() ?? "No definido",
                FormatMoney(row.AmountCentavos),
                $"{PaymentMethodLabel(row.MetodoPago)} · {PaymentOriginLabel(row.OrigenFondos)} · " +
                $"{row.ResponsableOrigen ?? "-"} · " +
                $"{(row.CajaId.HasValue ? "Reduce caja física" : row.OrigenFondos is null ? "Efecto histórico desconocido" : "No altera caja")}",
                row.RegisteredByUserName,
                row.Status == EmployeePaymentStatus.Active ? "Activo" : "Anulado",
                row.Status);
        }
    }

    private static string FormatMoney(long centavos)
        => $"${centavos / 100m:N2}";

    private static string DescribeSalesPeriod(EmployeeSalesPeriod? period)
        => period is null
            ? "todo el historial"
            : $"{period.StartDate:dd/MM/yyyy} - {period.EndDateExclusive.AddDays(-1):dd/MM/yyyy}";

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

    private sealed record EmployeeSalesGridRow(string Date, string SaleCount, string Total);

    private Control BuildCalendarSection()
    {
        ConfigureCalendarControls();

        _calendarGrid.Dock = DockStyle.Fill;
        _calendarGrid.Margin = Padding.Empty;
        _calendarGrid.Padding = new Padding(0, UITheme.SpaceXs, 0, 0);
        RenderCalendar(new Dictionary<DateOnly, int>());

        UITheme.StyleStatusLabel(_calendarStatus, UITheme.SecondaryColor);
        _calendarStatus.Dock = DockStyle.Fill;
        _calendarStatus.Text = "Días con actividad: cargando...";

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(UITheme.SpaceSm)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, UITheme.StandardControlHeight));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        content.Controls.Add(BuildCalendarToolbar(), 0, 0);
        content.Controls.Add(_calendarGrid, 0, 1);
        content.Controls.Add(_calendarStatus, 0, 2);

        return VisualLayout.CreateSection(
            "Días con actividad",
            "Cada cuadro representa un día; los días marcados muestran la cantidad de logins registrada.",
            content);
    }

    private void ConfigureCalendarControls()
    {
        var initialDate = _employee.LastLoginUtc.HasValue
            ? _employee.LastLoginUtc.Value.ToLocalTime().Date
            : DateTime.Now.Date;
        _calendarPeriod = new DateOnly(initialDate.Year, initialDate.Month, 1);

        _suppressCalendarEvents = true;
        try
        {
            ConfigureCalendarCombo(_calendarMonth, CalendarMonthNames);

            var currentYear = DateTime.Now.Year;
            var years = Enumerable.Range(Math.Max(1, currentYear - 5), 11)
                .Append(_calendarPeriod.Year)
                .Distinct()
                .OrderByDescending(year => year)
                .ToArray();
            ConfigureCalendarCombo(_calendarYear, years.Select(year => year.ToString()).ToArray());
            SelectCalendarPeriod();
        }
        finally
        {
            _suppressCalendarEvents = false;
        }

        UITheme.StyleOutlineButton(_calendarPrevious);
        _calendarPrevious.Text = "‹";
        _calendarPrevious.AccessibleName = "Mes anterior";
        _calendarPrevious.Width = 42;
        _calendarPrevious.Click += async (_, _) => await MoveCalendarAsync(-1);

        UITheme.StyleOutlineButton(_calendarNext);
        _calendarNext.Text = "›";
        _calendarNext.AccessibleName = "Mes siguiente";
        _calendarNext.Width = 42;
        _calendarNext.Click += async (_, _) => await MoveCalendarAsync(1);

        _calendarMonth.SelectedIndexChanged += async (_, _) => await CalendarPeriodChangedAsync();
        _calendarYear.SelectedIndexChanged += async (_, _) => await CalendarPeriodChangedAsync();
    }

    private static void ConfigureCalendarCombo(ComboBox combo, params string[] items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.Margin = Padding.Empty;
        combo.Items.Clear();
        combo.Items.AddRange(items);
        UITheme.StyleInput(combo);
    }

    private Control BuildCalendarToolbar()
    {
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(_calendarPrevious, 0, 0);
        toolbar.Controls.Add(_calendarMonth, 1, 0);
        toolbar.Controls.Add(_calendarYear, 2, 0);
        toolbar.Controls.Add(_calendarNext, 3, 0);
        toolbar.Controls.Add(new Label
        {
            Text = "Selecciona un mes para revisar la actividad registrada.",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(UITheme.SpaceSm, 0, 0, 0)
        }, 4, 0);
        return toolbar;
    }

    private async Task CalendarPeriodChangedAsync()
    {
        if (_suppressCalendarEvents || _calendarLoading ||
            _calendarMonth.SelectedIndex < 0 || _calendarYear.SelectedIndex < 0)
        {
            return;
        }

        if (!int.TryParse(_calendarYear.SelectedItem?.ToString(), out var year))
        {
            return;
        }

        _calendarPeriod = new DateOnly(year, _calendarMonth.SelectedIndex + 1, 1);
        await LoadCalendarAsync();
    }

    private async Task MoveCalendarAsync(int monthDelta)
    {
        if (_calendarLoading)
        {
            return;
        }

        var next = _calendarPeriod.AddMonths(monthDelta);
        if (!_calendarYear.Items.Contains(next.Year.ToString()))
        {
            _calendarYear.Items.Add(next.Year.ToString());
        }

        _suppressCalendarEvents = true;
        try
        {
            _calendarPeriod = next;
            SelectCalendarPeriod();
        }
        finally
        {
            _suppressCalendarEvents = false;
        }

        await LoadCalendarAsync();
    }

    private void SelectCalendarPeriod()
    {
        _calendarMonth.SelectedIndex = _calendarPeriod.Month - 1;
        var yearText = _calendarPeriod.Year.ToString();
        var yearIndex = _calendarYear.Items.IndexOf(yearText);
        if (yearIndex < 0)
        {
            _calendarYear.Items.Add(yearText);
            yearIndex = _calendarYear.Items.IndexOf(yearText);
        }

        _calendarYear.SelectedIndex = yearIndex;
    }

    private async Task LoadCalendarAsync()
    {
        if (_calendarLoading)
        {
            return;
        }

        try
        {
            _calendarLoading = true;
            SetCalendarBusy(true);
            _calendarStatus.Text = "Días con actividad: cargando...";
            var activity = await _activity.GetActivityCalendarAsync(
                _employee.UserId,
                _calendarPeriod.Year,
                _calendarPeriod.Month);
            var byDate = activity.ToDictionary(day => day.LocalDate, day => day.LoginCount);
            RenderCalendar(byDate);
            _calendarStatus.Text =
                $"Días con actividad: {activity.Count} · {activity.Sum(day => day.LoginCount)} logins · {CalendarMonthNames[_calendarPeriod.Month - 1]} {_calendarPeriod.Year}";
        }
        catch (Exception ex)
        {
            RenderCalendar(new Dictionary<DateOnly, int>());
            _calendarStatus.Text = "Días con actividad: no disponible.";
            MessageBox.Show(
                $"No se pudo cargar el calendario de actividad.\n\n{ex.Message}",
                "Actividad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _calendarLoading = false;
            SetCalendarBusy(false);
        }
    }

    private void SetCalendarBusy(bool busy)
    {
        _calendarMonth.Enabled = !busy;
        _calendarYear.Enabled = !busy;
        _calendarPrevious.Enabled = !busy;
        _calendarNext.Enabled = !busy;
    }

    private void RenderCalendar(IReadOnlyDictionary<DateOnly, int> activityByDate)
    {
        _calendarGrid.SuspendLayout();
        _calendarGrid.Controls.Clear();
        _calendarToolTip.RemoveAll();
        _calendarGrid.ColumnCount = 7;
        _calendarGrid.RowCount = 7;
        _calendarGrid.ColumnStyles.Clear();
        _calendarGrid.RowStyles.Clear();
        for (var column = 0; column < 7; column++)
        {
            _calendarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.2857f));
        }

        _calendarGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        for (var row = 0; row < 6; row++)
        {
            _calendarGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 16.6667f));
        }

        for (var column = 0; column < CalendarWeekdayNames.Length; column++)
        {
            _calendarGrid.Controls.Add(new Label
            {
                Text = CalendarWeekdayNames[column],
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UITheme.NormalBoldFont,
                ForeColor = UITheme.SecondaryColor,
                Margin = Padding.Empty
            }, column, 0);
        }

        var firstDay = _calendarPeriod;
        var firstColumn = ((int)firstDay.DayOfWeek + 6) % 7;
        var daysInMonth = DateTime.DaysInMonth(_calendarPeriod.Year, _calendarPeriod.Month);
        for (var slot = 0; slot < 42; slot++)
        {
            var dayNumber = slot - firstColumn + 1;
            var isCurrentMonth = dayNumber is >= 1 and <= 31 && dayNumber <= daysInMonth;
            var day = isCurrentMonth
                ? new DateOnly(_calendarPeriod.Year, _calendarPeriod.Month, dayNumber)
                : default;
            var loginCount = 0;
            var hasActivity = isCurrentMonth && activityByDate.TryGetValue(day, out loginCount);
            var cell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(UITheme.SpaceXs / 2),
                Padding = new Padding(UITheme.SpaceXs),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = hasActivity ? UITheme.PrimaryColor : UITheme.SurfaceMutedColor,
                Enabled = isCurrentMonth
            };
            var label = new Label
            {
                Text = isCurrentMonth ? dayNumber.ToString() : string.Empty,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UITheme.NormalBoldFont,
                ForeColor = hasActivity ? UITheme.TextLight : UITheme.TextDark,
                Margin = Padding.Empty
            };
            cell.Controls.Add(label);

            if (isCurrentMonth)
            {
                var tooltip = hasActivity
                    ? $"{day:dd/MM/yyyy} · {loginCount} {(loginCount == 1 ? "login" : "logins")}"
                    : $"{day:dd/MM/yyyy} · Sin actividad";
                _calendarToolTip.SetToolTip(cell, tooltip);
                _calendarToolTip.SetToolTip(label, tooltip);
            }

            _calendarGrid.Controls.Add(cell, slot % 7, 1 + slot / 7);
        }

        _calendarGrid.ResumeLayout(true);
    }

    private static TabPage BuildPlaceholderTab(string title, string message)
    {
        var page = new TabPage(title) { BackColor = UITheme.BackgroundColor, Padding = new Padding(UITheme.SpaceMd) };
        page.Controls.Add(VisualLayout.CreateEmptyState(title, message));
        return page;
    }

    private static Control CreateInfoCard(string caption, string value)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceMutedColor,
            Padding = new Padding(UITheme.SpaceMd),
            Margin = new Padding(UITheme.SpaceXs)
        };
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = Padding.Empty
        }, 0, 0);
        card.Controls.Add(new Label
        {
            Text = DisplayText(value),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, UITheme.SpaceXs, 0, 0)
        }, 0, 1);
        return card;
    }

    private static bool HasActivity(DateTime? lastLoginUtc, int activityDays)
        => lastLoginUtc.HasValue || activityDays > 0;

    private static string DisplayText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "No disponible" : value.Trim();

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
            Name = property,
            DataPropertyName = property,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private static string FormatDate(DateTime value)
        => value == default
            ? "No disponible"
            : value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    private sealed record EmployeeLoginGridRow(string Date, string Time, string Station, string StationMode)
    {
        public static EmployeeLoginGridRow FromActivity(EmployeeLoginActivity activity)
        {
            var local = activity.LoggedInAtUtc.ToLocalTime();
            return new EmployeeLoginGridRow(
                local.ToString("dd/MM/yyyy"),
                local.ToString("HH:mm:ss"),
                string.IsNullOrWhiteSpace(activity.InstallationId) ? "No registrada" : activity.InstallationId,
                string.IsNullOrWhiteSpace(activity.StationMode) ? "No registrado" : activity.StationMode);
        }
    }
}

/// <summary>
/// Read-only ranking for completed sales in the selected local-date period.
/// It deliberately includes any user who attributed a sale, including the
/// protected administrator and delegated administrators.
/// </summary>
public sealed class EmployeeSalesRankingForm : Form
{
    private readonly IEmployeeSalesQueryService _sales;
    private readonly EmployeeSalesPeriod? _period;
    private readonly DataGridView _grid = new();
    private readonly BindingSource _binding = new();
    private readonly Label _status = new();

    public EmployeeSalesRankingForm(
        IEmployeeSalesQueryService sales,
        EmployeeSalesPeriod? period)
    {
        _sales = sales ?? throw new ArgumentNullException(nameof(sales));
        _period = period;
        BuildUi();
        Load += EmployeeSalesRankingForm_Load;
    }

    private async void EmployeeSalesRankingForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var rows = await _sales.GetSalesRankingAsync(_period);
            _binding.DataSource = rows.Select(EmployeeSalesRankingGridRow.FromRow).ToList();
            _status.Text = rows.Count == 0
                ? "No hay ventas completadas en el periodo seleccionado."
                : $"{rows.Count:N0} usuarios con ventas · Periodo: {DescribePeriod(_period)}";
        }
        catch (Exception ex)
        {
            _status.Text = "No se pudo cargar el ranking.";
            MessageBox.Show(
                $"No se pudo cargar el ranking de ventas.\n\n{ex.Message}",
                "Ranking de ventas",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void BuildUi()
    {
        Text = "Ranking de ventas";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 680);
        MinimumSize = new Size(980, 560);
        UITheme.StyleForm(this);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _binding;
        UITheme.StyleDataGridView(_grid);
        AddColumn(nameof(EmployeeSalesRankingGridRow.Position), "Pos.", 62, 0.5f, DataGridViewContentAlignment.MiddleCenter);
        AddColumn(nameof(EmployeeSalesRankingGridRow.Name), "Nombre", 200, 1.6f);
        AddColumn(nameof(EmployeeSalesRankingGridRow.UserName), "Usuario", 140, 1.1f);
        AddColumn(nameof(EmployeeSalesRankingGridRow.Sales), "Ventas", 90, 0.7f, DataGridViewContentAlignment.MiddleCenter);
        AddColumn(nameof(EmployeeSalesRankingGridRow.Total), "Total vendido", 150, 1.2f, DataGridViewContentAlignment.MiddleRight);
        AddColumn(nameof(EmployeeSalesRankingGridRow.Average), "Promedio", 140, 1.1f, DataGridViewContentAlignment.MiddleRight);

        UITheme.StyleStatusLabel(_status, UITheme.SecondaryColor);
        _status.Dock = DockStyle.Fill;
        _status.Text = "Cargando ranking...";
        _status.AutoEllipsis = false;
        _status.Margin = new Padding(0, UITheme.SpaceSm, 0, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.Controls.Add(
            VisualLayout.CreatePageHeader(
                "Ranking de ventas",
                $"Total vendido por usuario · Periodo: {DescribePeriod(_period)}"),
            0,
            0);
        root.Controls.Add(VisualLayout.CreateSection(
            "Usuarios con ventas",
            "Se incluyen empleados, ADMIN y Principal cuando atribuyeron ventas completadas.",
            _grid), 0, 1);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
    }

    private void AddColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = property,
            DataPropertyName = property,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private static string DescribePeriod(EmployeeSalesPeriod? period)
        => period is null
            ? "todo el historial"
            : $"{period.StartDate:dd/MM/yyyy} - {period.EndDateExclusive.AddDays(-1):dd/MM/yyyy}";

    private sealed record EmployeeSalesRankingGridRow(
        int Position,
        string Name,
        string UserName,
        string Sales,
        string Total,
        string Average)
    {
        public static EmployeeSalesRankingGridRow FromRow(EmployeeSalesRankingRow row) => new(
            row.Position,
            row.Active ? row.FullName : $"{row.FullName} (inactivo)",
            row.UserName,
            row.SaleCount.ToString("N0"),
            FormatMoney(row.TotalSoldCentavos),
            FormatMoney(row.AverageTicketCentavos));

        private static string FormatMoney(long centavos) => $"${centavos / 100m:N2}";
    }
}
