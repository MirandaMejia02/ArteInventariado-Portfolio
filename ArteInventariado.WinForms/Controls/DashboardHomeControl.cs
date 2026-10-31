using System.Globalization;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;

namespace ArteInventariado.WinForms.Controls;

internal sealed class DashboardHomeControl : UserControl
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly Color[] CategoryColors =
    [
        Color.FromArgb(203, 43, 116),
        Color.FromArgb(113, 50, 82),
        Color.FromArgb(232, 111, 159),
        Color.FromArgb(220, 163, 50),
        Color.FromArgb(79, 160, 121),
        Color.FromArgb(120, 116, 130)
    ];

    private readonly IDashboardQueryService _queryService;
    private readonly SemaphoreSlim _queryGate = new(2, 2);
    private readonly TableLayoutPanel _root = new();
    private readonly TableLayoutPanel _metrics = new();
    private readonly TableLayoutPanel _charts = new();
    private readonly TableLayoutPanel _lower = new();
    private readonly DashboardMetricCardControl _salesCard;
    private readonly DashboardMetricCardControl _productsCard;
    private readonly DashboardMetricCardControl _customersCard;
    private readonly DashboardMetricCardControl _cashCard;
    private readonly DashboardMetricCardControl _ordersCard;
    private readonly CartesianChart _salesChart = new();
    private readonly PieChart _categoryChart = new();
    private readonly FlowLayoutPanel _categoryLegend = new();
    private readonly DataGridView _topProductsGrid = new();
    private readonly FlowLayoutPanel _activityList = new();
    private readonly Label _salesChartStatus = CreateStatusLabel();
    private readonly Label _categoryStatus = CreateStatusLabel();
    private readonly Label _productsStatus = CreateStatusLabel();
    private readonly Label _activityStatus = CreateStatusLabel();
    private readonly ComboBox _rangeSelector = new();
    private readonly Label _welcome = new();
    private readonly Label _updatedAt = new();
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _rangeCancellation;
    private bool _loadedOnce;

    public DashboardHomeControl(IDashboardQueryService queryService, string userName)
    {
        _queryService = queryService;
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = UITheme.DashboardBackgroundColor;
        Padding = Padding.Empty;

        _salesCard = new DashboardMetricCardControl("Ventas netas hoy", "\uE7BF", UITheme.PrimaryColor, highlighted: true);
        _productsCard = new DashboardMetricCardControl("Productos activos", "\uE7B8", UITheme.SecondaryColor);
        _customersCard = new DashboardMetricCardControl("Clientes registrados", "\uE716", Color.FromArgb(34, 126, 143));
        _cashCard = new DashboardMetricCardControl("Efectivo esperado", "\uE8C7", UITheme.SuccessColor);
        _ordersCard = new DashboardMetricCardControl("Pedidos pendientes", "\uE9F9", UITheme.WarningColor);

        BuildUi(userName);
        Resize += (_, _) => ApplyResponsiveLayout();
        VisibleChanged += DashboardHomeControl_VisibleChanged;
        Disposed += (_, _) => CancelLoads();
    }

    public event EventHandler? NewSaleRequested;
    public event EventHandler? NewProductRequested;
    public event EventHandler? NewPurchaseRequested;
    public event EventHandler? NewQuotationRequested;
    public event EventHandler? CashRequested;
    public event EventHandler? ReportsRequested;
    public event EventHandler? ExcelImportRequested;

    public void RefreshDashboard()
    {
        if (!Visible || IsDisposed)
        {
            return;
        }

        _ = LoadDashboardAsync();
    }

    private void BuildUi(string userName)
    {
        _root.Dock = DockStyle.Top;
        _root.ColumnCount = 1;
        _root.RowCount = 4;
        _root.Padding = new Padding(18, 14, 18, 18);
        _root.BackColor = UITheme.DashboardBackgroundColor;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 326));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 292));

        var welcomePanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        _welcome.Text = $"Bienvenido, {userName}";
        _welcome.Font = new Font("Segoe UI", 17, FontStyle.Bold);
        _welcome.ForeColor = UITheme.TextDark;
        _welcome.AutoSize = true;
        _welcome.Location = new Point(0, 0);
        var subtitle = new Label
        {
            Text = "Resumen general del sistema",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = UITheme.MutedTextColor,
            Location = new Point(2, 36)
        };
        _updatedAt.AutoSize = true;
        _updatedAt.Font = new Font("Segoe UI", 8.5f);
        _updatedAt.ForeColor = UITheme.MutedTextColor;
        _updatedAt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        welcomePanel.Controls.AddRange([_welcome, subtitle, _updatedAt]);
        welcomePanel.Resize += (_, _) => _updatedAt.Location = new Point(
            Math.Max(0, welcomePanel.ClientSize.Width - _updatedAt.Width),
            10);

        _metrics.Dock = DockStyle.Fill;
        _metrics.ColumnCount = 5;
        _metrics.RowCount = 1;
        _metrics.BackColor = UITheme.DashboardBackgroundColor;
        _metrics.Margin = Padding.Empty;
        _metrics.Controls.AddRange([_salesCard, _productsCard, _customersCard, _cashCard, _ordersCard]);

        BuildChartsLayout();
        BuildLowerLayout();

        _root.Controls.Add(welcomePanel, 0, 0);
        _root.Controls.Add(_metrics, 0, 1);
        _root.Controls.Add(_charts, 0, 2);
        _root.Controls.Add(_lower, 0, 3);
        Controls.Add(_root);
        ApplyResponsiveLayout();
    }

    private void BuildChartsLayout()
    {
        _charts.Dock = DockStyle.Fill;
        _charts.ColumnCount = 2;
        _charts.RowCount = 1;
        _charts.Margin = Padding.Empty;
        _charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        _charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _charts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _rangeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _rangeSelector.Width = 150;
        _rangeSelector.Items.AddRange(["Ultimos 7 dias", "Ultimos 30 dias", "Este mes"]);
        _rangeSelector.SelectedIndex = 0;
        _rangeSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_loadedOnce)
            {
                _ = ReloadSalesRangeAsync();
            }
        };

        var salesSection = CreateSection("Resumen de ventas", _rangeSelector, out var salesContent);
        ConfigureSalesChart();
        salesContent.Controls.Add(_salesChart);
        salesContent.Controls.Add(_salesChartStatus);

        var categorySection = CreateSection("Ventas netas por categoria", null, out var categoryContent);
        var categoryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(4)
        };
        categoryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        categoryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _categoryChart.Dock = DockStyle.Fill;
        _categoryChart.InitialRotation = -90;
        _categoryChart.LegendPosition = LegendPosition.Hidden;
        _categoryLegend.Dock = DockStyle.Fill;
        _categoryLegend.FlowDirection = FlowDirection.TopDown;
        _categoryLegend.WrapContents = false;
        _categoryLegend.AutoScroll = true;
        _categoryLegend.Padding = new Padding(6, 16, 0, 0);
        _categoryLegend.SizeChanged += (_, _) => ResizeFlowChildren(_categoryLegend, 8);
        categoryLayout.Controls.Add(_categoryChart, 0, 0);
        categoryLayout.Controls.Add(_categoryLegend, 1, 0);
        categoryContent.Controls.Add(categoryLayout);
        categoryContent.Controls.Add(_categoryStatus);

        _charts.Controls.Add(salesSection, 0, 0);
        _charts.Controls.Add(categorySection, 1, 0);
    }

    private void BuildLowerLayout()
    {
        _lower.Dock = DockStyle.Fill;
        _lower.ColumnCount = 3;
        _lower.RowCount = 1;
        _lower.Margin = new Padding(0, 12, 0, 0);
        _lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        _lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        _lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        _lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topSection = CreateSection("Productos mas vendidos", null, out var topContent);
        ConfigureTopProductsGrid();
        topContent.Controls.Add(_topProductsGrid);
        topContent.Controls.Add(_productsStatus);

        var activitySection = CreateSection("Ultimas actividades", null, out var activityContent);
        _activityList.Dock = DockStyle.Fill;
        _activityList.FlowDirection = FlowDirection.TopDown;
        _activityList.WrapContents = false;
        _activityList.AutoScroll = true;
        _activityList.Padding = new Padding(10, 5, 10, 5);
        _activityList.SizeChanged += (_, _) => ResizeFlowChildren(_activityList, 20);
        activityContent.Controls.Add(_activityList);
        activityContent.Controls.Add(_activityStatus);

        var quickSection = CreateSection("Accesos rapidos", null, out var quickContent);
        quickContent.Controls.Add(BuildQuickActions());

        topSection.Margin = new Padding(0, 0, 6, 0);
        activitySection.Margin = new Padding(6, 0, 6, 0);
        quickSection.Margin = new Padding(6, 0, 0, 0);
        _lower.Controls.Add(topSection, 0, 0);
        _lower.Controls.Add(activitySection, 1, 0);
        _lower.Controls.Add(quickSection, 2, 0);
    }

    private static DashboardSurfacePanel CreateSection(
        string title,
        Control? headerAction,
        out Panel content)
    {
        var section = new DashboardSurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(13, 6, 10, 3),
            BackColor = UITheme.SurfaceColor
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(titleLabel, 0, 0);
        if (headerAction is not null)
        {
            headerAction.Anchor = AnchorStyles.Right;
            header.Controls.Add(headerAction, 1, 0);
        }

        content = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(6) };
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(content, 0, 1);
        section.Controls.Add(layout);
        return section;
    }

    private void ConfigureSalesChart()
    {
        _salesChart.Dock = DockStyle.Fill;
        _salesChart.LegendPosition = LegendPosition.Hidden;
        _salesChart.TooltipPosition = TooltipPosition.Top;
        _salesChart.BackColor = UITheme.SurfaceColor;
    }

    private void ConfigureTopProductsGrid()
    {
        _topProductsGrid.Dock = DockStyle.Fill;
        _topProductsGrid.ReadOnly = true;
        _topProductsGrid.AllowUserToAddRows = false;
        _topProductsGrid.AllowUserToDeleteRows = false;
        _topProductsGrid.AllowUserToResizeRows = false;
        _topProductsGrid.RowHeadersVisible = false;
        _topProductsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _topProductsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _topProductsGrid.ScrollBars = ScrollBars.Vertical;
        UITheme.StyleDataGridView(_topProductsGrid);
        _topProductsGrid.ColumnHeadersHeight = 34;
        _topProductsGrid.RowTemplate.Height = 31;
        _topProductsGrid.TabStop = false;
        _topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Producto", HeaderText = "Producto", FillWeight = 60 });
        _topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Vendidos", HeaderText = "Vendidos", FillWeight = 22 });
        _topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Stock", HeaderText = "Stock", FillWeight = 18 });
    }

    private TableLayoutPanel BuildQuickActions()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8),
            BackColor = UITheme.SurfaceColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 4; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }

        AddQuickAction(layout, "\uE7BF", "Nueva venta", 0, 0, () => NewSaleRequested?.Invoke(this, EventArgs.Empty));
        AddQuickAction(layout, "\uE7B8", "Nuevo producto", 1, 0, () => NewProductRequested?.Invoke(this, EventArgs.Empty));
        AddQuickAction(layout, "\uE719", "Nueva compra", 0, 1, () => NewPurchaseRequested?.Invoke(this, EventArgs.Empty));
        AddQuickAction(layout, "\uE9F9", "Cotizacion", 1, 1, () => NewQuotationRequested?.Invoke(this, EventArgs.Empty));
        AddQuickAction(layout, "\uE8C7", "Abrir o ver caja", 0, 2, () => CashRequested?.Invoke(this, EventArgs.Empty));
        AddQuickAction(layout, "\uE9D2", "Reportes", 1, 2, () => ReportsRequested?.Invoke(this, EventArgs.Empty));
        var importButton = AddQuickAction(layout, "\uE8B5", "Importar Excel", 0, 3, () => ExcelImportRequested?.Invoke(this, EventArgs.Empty));
        layout.SetColumnSpan(importButton, 2);
        return layout;
    }

    private static Button AddQuickAction(
        TableLayoutPanel layout,
        string icon,
        string text,
        int column,
        int row,
        Action action)
    {
        var button = new Button
        {
            Text = $"{icon}  {text}",
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            FlatStyle = FlatStyle.Flat,
            BackColor = UITheme.DashboardAccentSoftColor,
            ForeColor = UITheme.SecondaryColor,
            Font = new Font("Segoe MDL2 Assets", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 4, 0),
            Cursor = Cursors.Hand,
            AutoEllipsis = true
        };
        button.FlatAppearance.BorderColor = UITheme.BorderPinkColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = UITheme.SoftPinkColor;
        button.Click += (_, _) => action();
        layout.Controls.Add(button, column, row);
        return button;
    }

    private async void DashboardHomeControl_VisibleChanged(object? sender, EventArgs e)
    {
        if (Visible && !_loadedOnce)
        {
            _loadedOnce = true;
            ApplyResponsiveLayout();
            await LoadDashboardAsync();
        }
    }

    private async Task LoadDashboardAsync()
    {
        CancelLoads();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        SetLoadingState();

        await LoadOverviewAsync(token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await Task.WhenAll(
                RunLimitedAsync(() => LoadSalesSectionsAsync(CurrentRange, token), token),
                RunLimitedAsync(() => LoadTopProductsAsync(CurrentRange, token), token),
                RunLimitedAsync(() => LoadActivityAsync(token), token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        _updatedAt.Text = $"Actualizado {DateTime.Now:HH:mm}";
    }

    private async Task ReloadSalesRangeAsync()
    {
        _rangeCancellation?.Cancel();
        _rangeCancellation?.Dispose();
        _rangeCancellation = _loadCancellation is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation.Token);
        var token = _rangeCancellation.Token;
        try
        {
            await RunLimitedAsync(() => LoadSalesSectionsAsync(CurrentRange, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task RunLimitedAsync(Func<Task> load, CancellationToken cancellationToken)
    {
        await _queryGate.WaitAsync(cancellationToken);
        try
        {
            await load();
        }
        finally
        {
            _queryGate.Release();
        }
    }

    private async Task LoadOverviewAsync(CancellationToken cancellationToken)
    {
        try
        {
            var overview = await _queryService.GetOverviewAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var comparison = FormatComparison(overview.SalesToday.Comparison);
            _salesCard.SetContent(
                FormatMoney(overview.SalesToday.NetSalesCentavos),
                $"{overview.SalesToday.SaleCount} venta(s)",
                comparison.Text,
                Color.White);
            _productsCard.SetContent(overview.ActiveProducts.ToString("N0"), "Activos");
            _customersCard.SetContent(overview.RegisteredCustomers.ToString("N0"), "Registrados");
            _cashCard.SetContent(
                overview.CashExpected.HasOpenCash
                    ? FormatMoney(overview.CashExpected.ExpectedAmountCentavos!.Value)
                    : "Sin caja abierta",
                overview.CashExpected.HasOpenCash ? "Efectivo fisico esperado" : "Abra caja para operar");
            _ordersCard.SetContent(overview.PendingOrders.ToString("N0"), "Pendientes");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            _salesCard.SetError();
            _productsCard.SetError();
            _customersCard.SetError();
            _cashCard.SetError();
            _ordersCard.SetError();
        }
    }

    private async Task LoadSalesSectionsAsync(DashboardSalesRange range, CancellationToken cancellationToken)
    {
        _salesChartStatus.Text = "Cargando tendencia...";
        _salesChartStatus.Visible = true;
        _categoryStatus.Text = "Cargando categorias...";
        _categoryStatus.Visible = true;
        try
        {
            var trend = await _queryService.GetSalesTrendAsync(range, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RenderSalesTrend(trend);
            _salesChartStatus.Visible = false;

            var categories = await _queryService.GetCategorySalesAsync(range, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RenderCategories(categories);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            ShowSectionError(_salesChartStatus, "No se pudo cargar la tendencia.");
            ShowSectionError(_categoryStatus, "No se pudieron cargar las categorias.");
        }
    }

    private void RenderSalesTrend(DashboardSalesTrend trend)
    {
        var accent = ToSkColor(UITheme.PrimaryColor);
        _salesChart.Series =
        [
            new LineSeries<long>
            {
                Name = "Ventas netas",
                Values = trend.Points.Select(p => p.NetSalesCentavos).ToArray(),
                GeometrySize = 7,
                LineSmoothness = 0.35,
                Stroke = new SolidColorPaint(accent, 3),
                GeometryStroke = new SolidColorPaint(accent, 2),
                GeometryFill = new SolidColorPaint(SKColors.White),
                Fill = new SolidColorPaint(new SKColor(accent.Red, accent.Green, accent.Blue, 34)),
                YToolTipLabelFormatter = point => FormatMoney((long)point.Coordinate.PrimaryValue)
            }
        ];
        _salesChart.XAxes =
        [
            new Axis
            {
                Labels = trend.Points.Select(p => p.Date.ToString("dd/MM")).ToArray(),
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
                MinLimit = 0,
                Labeler = value => $"${value / 100:N0}",
                LabelsPaint = new SolidColorPaint(ToSkColor(UITheme.MutedTextColor)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(235, 229, 233), 1)
            }
        ];
    }

    private void RenderCategories(DashboardCategorySales result)
    {
        _categoryLegend.Controls.Clear();
        if (result.Categories.Count == 0)
        {
            _categoryChart.Series = [];
            _categoryStatus.Text = "Sin ventas netas en este periodo.";
            _categoryStatus.Visible = true;
            return;
        }

        var series = new List<ISeries>();
        for (var index = 0; index < result.Categories.Count; index++)
        {
            var item = result.Categories[index];
            var color = CategoryColors[index % CategoryColors.Length];
            series.Add(new PieSeries<long>
            {
                Name = item.CategoryName,
                Values = [item.NetSalesCentavos],
                InnerRadius = 52,
                Fill = new SolidColorPaint(ToSkColor(color)),
                Stroke = new SolidColorPaint(SKColors.White, 2),
                ToolTipLabelFormatter = _ => $"{FormatMoney(item.NetSalesCentavos)} ({item.Percentage:N1} %)"
            });
            _categoryLegend.Controls.Add(CreateLegendRow(color, item));
        }

        _categoryChart.Series = series;
        ResizeFlowChildren(_categoryLegend, 8);
        _categoryStatus.Visible = false;
    }

    private static Control CreateLegendRow(Color color, DashboardCategorySalesPoint item)
    {
        var row = new Panel { Width = 190, Height = 42, Margin = new Padding(0, 0, 0, 2) };
        var swatch = new Panel { BackColor = color, Size = new Size(11, 11), Location = new Point(0, 7) };
        var name = new Label
        {
            Text = item.CategoryName,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = UITheme.TextDark,
            Location = new Point(19, 0),
            Size = new Size(165, 20)
        };
        var value = new Label
        {
            Text = $"{FormatMoney(item.NetSalesCentavos)} | {item.Percentage:N1} %",
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8),
            ForeColor = UITheme.MutedTextColor,
            Location = new Point(19, 20),
            Size = new Size(165, 18)
        };
        row.Controls.AddRange([swatch, name, value]);
        return row;
    }

    private async Task LoadTopProductsAsync(DashboardSalesRange range, CancellationToken cancellationToken)
    {
        _productsStatus.Text = "Cargando productos...";
        _productsStatus.Visible = true;
        try
        {
            var products = await _queryService.GetTopProductsAsync(range, 5, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _topProductsGrid.Rows.Clear();
            foreach (var product in products)
            {
                _topProductsGrid.Rows.Add(product.ProductName, product.NetUnitsSold, product.CurrentStock);
            }
            _topProductsGrid.ClearSelection();

            _productsStatus.Text = products.Count == 0 ? "Sin productos vendidos en este periodo." : string.Empty;
            _productsStatus.Visible = products.Count == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            ShowSectionError(_productsStatus, "No se pudo cargar el ranking.");
        }
    }

    private async Task LoadActivityAsync(CancellationToken cancellationToken)
    {
        _activityStatus.Text = "Cargando actividad...";
        _activityStatus.Visible = true;
        try
        {
            var activity = await _queryService.GetRecentActivityAsync(5, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _activityList.Controls.Clear();
            foreach (var item in activity)
            {
                _activityList.Controls.Add(CreateActivityRow(item));
            }
            ResizeFlowChildren(_activityList, 20);

            _activityStatus.Text = activity.Count == 0 ? "Todavia no hay actividad registrada." : string.Empty;
            _activityStatus.Visible = activity.Count == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            ShowSectionError(_activityStatus, "No se pudo cargar la actividad.");
        }
    }

    private static Control CreateActivityRow(DashboardRecentActivity item)
    {
        var row = new TableLayoutPanel
        {
            Width = 340,
            Height = 41,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 3)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        row.Controls.Add(new Label
        {
            Text = item.Title,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = UITheme.TextDark
        }, 0, 0);
        row.Controls.Add(new Label
        {
            Text = RelativeTime(item.OccurredAtUtc),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopRight,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = UITheme.MutedTextColor
        }, 1, 0);
        var description = new Label
        {
            Text = item.Description,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = UITheme.MutedTextColor
        };
        row.Controls.Add(description, 0, 1);
        row.SetColumnSpan(description, 2);
        return row;
    }

    private void SetLoadingState()
    {
        _salesCard.SetContent("Cargando...", string.Empty);
        _productsCard.SetContent("Cargando...", string.Empty);
        _customersCard.SetContent("Cargando...", string.Empty);
        _cashCard.SetContent("Cargando...", string.Empty);
        _ordersCard.SetContent("Cargando...", string.Empty);
        _salesChartStatus.Text = "Cargando tendencia...";
        _categoryStatus.Text = "Cargando categorias...";
        _productsStatus.Text = "Cargando productos...";
        _activityStatus.Text = "Cargando actividad...";
        _salesChartStatus.Visible = true;
        _categoryStatus.Visible = true;
        _productsStatus.Visible = true;
        _activityStatus.Visible = true;
    }

    private void ApplyResponsiveLayout()
    {
        if (ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(650, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        _root.Width = available;
        var contentWidth = Math.Max(610, available - _root.Padding.Horizontal);

        var metricColumns = contentWidth >= 1220 ? 5 : contentWidth >= 760 ? 3 : 2;
        var metricRows = (int)Math.Ceiling(5d / metricColumns);
        ConfigureMetricGrid(metricColumns);
        var metricsHeight = metricRows * 144;

        var stackCharts = contentWidth < 900;
        ConfigureAdaptiveGrid(_charts, stackCharts, 2, stackCharts ? 620 : 326);
        var chartsHeight = stackCharts ? 620 : 326;

        var stackLower = contentWidth < 950;
        ConfigureAdaptiveGrid(_lower, stackLower, 3, stackLower ? 780 : 292);
        var lowerHeight = stackLower ? 780 : 292;

        _root.RowStyles[1].Height = metricsHeight;
        _root.RowStyles[2].Height = chartsHeight;
        _root.RowStyles[3].Height = lowerHeight;
        _root.Height = _root.Padding.Vertical + 66 + metricsHeight + chartsHeight + lowerHeight;
    }

    private void ConfigureMetricGrid(int columns)
    {
        _metrics.SuspendLayout();
        _metrics.ColumnStyles.Clear();
        _metrics.RowStyles.Clear();
        _metrics.ColumnCount = columns;
        var rows = (int)Math.Ceiling(5d / columns);
        _metrics.RowCount = rows;
        for (var column = 0; column < columns; column++)
        {
            _metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }
        for (var row = 0; row < rows; row++)
        {
            _metrics.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        }
        for (var index = 0; index < _metrics.Controls.Count; index++)
        {
            var card = _metrics.Controls[index];
            _metrics.SetCellPosition(card, new TableLayoutPanelCellPosition(index % columns, index / columns));
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, index % columns == columns - 1 ? 0 : 12, 6);
        }
        _metrics.ResumeLayout(true);
    }

    private static void ConfigureAdaptiveGrid(TableLayoutPanel grid, bool stacked, int count, int totalHeight)
    {
        grid.SuspendLayout();
        grid.ColumnStyles.Clear();
        grid.RowStyles.Clear();
        grid.ColumnCount = stacked ? 1 : count;
        grid.RowCount = stacked ? count : 1;
        if (stacked)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var index = 0; index < count; index++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / count));
                var control = grid.Controls[index];
                grid.SetCellPosition(control, new TableLayoutPanelCellPosition(0, index));
                control.Margin = new Padding(0, index == 0 ? 0 : 6, 0, index == count - 1 ? 0 : 6);
            }
        }
        else
        {
            for (var index = 0; index < count; index++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / count));
                var control = grid.Controls[index];
                grid.SetCellPosition(control, new TableLayoutPanelCellPosition(index, 0));
                control.Margin = new Padding(index == 0 ? 0 : 6, 0, index == count - 1 ? 0 : 6, 0);
            }
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }
        grid.Height = totalHeight;
        grid.ResumeLayout(true);
    }

    private DashboardSalesRange CurrentRange => _rangeSelector.SelectedIndex switch
    {
        1 => DashboardSalesRange.LastThirtyDays,
        2 => DashboardSalesRange.CurrentMonth,
        _ => DashboardSalesRange.LastSevenDays
    };

    private void CancelLoads()
    {
        _rangeCancellation?.Cancel();
        _rangeCancellation?.Dispose();
        _rangeCancellation = null;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private static void ResizeFlowChildren(FlowLayoutPanel panel, int reservedWidth)
    {
        var width = Math.Max(120, panel.ClientSize.Width - panel.Padding.Horizontal - reservedWidth);
        foreach (Control child in panel.Controls)
        {
            child.Width = width;
        }
    }

    private static (string Text, Color Color) FormatComparison(MetricComparison comparison) => comparison.State switch
    {
        MetricComparisonState.Increase => ($"↑ {comparison.Percentage:N1} % vs. ayer ({FormatMoney(comparison.PreviousValue)})", UITheme.SuccessColor),
        MetricComparisonState.Decrease => ($"↓ {Math.Abs(comparison.Percentage ?? 0):N1} % vs. ayer ({FormatMoney(comparison.PreviousValue)})", UITheme.DangerColor),
        MetricComparisonState.NewMovement => ("Nuevo movimiento vs. ayer", UITheme.SuccessColor),
        MetricComparisonState.NoComparisonBase => ("Sin base de comparacion", UITheme.MutedTextColor),
        _ => ($"Sin cambios vs. ayer ({FormatMoney(comparison.PreviousValue)})", UITheme.MutedTextColor)
    };

    private static string FormatMoney(long centavos) => (centavos / 100m).ToString("C2", CurrencyCulture);

    private static string RelativeTime(DateTime occurredAtUtc)
    {
        var utc = occurredAtUtc.Kind == DateTimeKind.Utc
            ? occurredAtUtc
            : DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalMinutes < 1) return "Ahora";
        if (elapsed.TotalMinutes < 60) return $"Hace {(int)elapsed.TotalMinutes} min";
        if (elapsed.TotalHours < 24) return $"Hace {(int)elapsed.TotalHours} h";
        return utc.ToLocalTime().ToString("dd/MM HH:mm");
    }

    private static Label CreateStatusLabel() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = UITheme.SurfaceColor,
        ForeColor = UITheme.MutedTextColor,
        Font = new Font("Segoe UI", 9),
        TextAlign = ContentAlignment.MiddleCenter
    };

    private static void ShowSectionError(Label label, string message)
    {
        label.Text = message;
        label.ForeColor = UITheme.DangerColor;
        label.Visible = true;
        label.BringToFront();
    }

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);
}
