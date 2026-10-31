using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;

namespace ArteInventariado.WinForms;

public sealed class AnalyticsForm : Form
{
    private static readonly Color[] ChartColors =
    [
        UITheme.PrimaryColor,
        UITheme.SecondaryColor,
        Color.FromArgb(224, 139, 46),
        UITheme.SuccessColor,
        Color.FromArgb(137, 117, 176),
        Color.FromArgb(188, 94, 128)
    ];

    private readonly ISalesAnalyticsQueryService _analytics;
    private readonly IFinancialAnalyticsQueryService _financialAnalytics;
    private readonly ICommercialAnalyticsQueryService _commercialAnalytics;
    private readonly IServiceProvider? _serviceProvider;
    private readonly TableLayoutPanel _shell = new();
    private readonly TableLayoutPanel _workspace = new();
    private readonly TableLayoutPanel _navigation = new();
    private readonly AnalyticsPeriodSelectorControl _periodSelector = new();
    private readonly Panel _sectionHost = new();
    private readonly List<Control> _sectionPages = [];
    private readonly List<AnalyticsNavigationButton> _sectionButtons = [];
    private readonly ToolTip _coverageToolTip = new();
    private readonly FlowLayoutPanel _salesCards = new();
    private readonly CartesianChart _salesChart = new();
    private readonly Label _salesCoverage = new();
    private readonly DataGridView _productsGrid = new();
    private readonly TextBox _productSearch = new();
    private readonly Button _productSearchClear = new();
    private readonly ComboBox _productRanking = new();
    private readonly ComboBox _productLimit = new();
    private readonly CartesianChart _productChart = new();
    private readonly Label _productGridEmptyState = new();
    private readonly Label _productChartState = new();
    private readonly Label _productChartTitle = new();
    private readonly Label _productChartSubtitle = new();
    private readonly AnalyticsProductMetricCardControl _mostSoldCard = new(
        "Más vendido", "\uE734", UITheme.PrimaryColor, UITheme.DashboardAccentSoftColor);
    private readonly AnalyticsProductMetricCardControl _highestSalesCard = new(
        "Mayor venta neta", "\uE8EC", Color.FromArgb(106, 70, 164), Color.FromArgb(246, 241, 255));
    private readonly AnalyticsProductMetricCardControl _mostReturnedCard = new(
        "Más devuelto", "\uE7A7", Color.FromArgb(201, 91, 25), Color.FromArgb(255, 244, 233));
    private readonly AnalyticsStatusStripControl _productStatus = new();
    private readonly DataGridView _categoriesGrid = new();
    private readonly DataGridView _subcategoriesGrid = new();
    private readonly Label _subcategoryEmptyState = new();
    private readonly ComboBox _categoryFilter = new();
    private readonly ComboBox _subcategoryFilter = new();
    private readonly PieChart _categoryPie = new();
    private readonly CartesianChart _categoryTrend = new();
    private readonly Label _categoryTrendEmptyState = new();
    private readonly Label _categoryCoverage = new();
    private readonly FlowLayoutPanel _financeCards = new();
    private readonly CartesianChart _financeChart = new();
    private readonly DataGridView _expensesGrid = new();
    private readonly Label _financeCoverage = new();
    private readonly FlowLayoutPanel _cashCards = new();
    private readonly FlowLayoutPanel _purchaseCards = new();
    private readonly CartesianChart _purchaseChart = new();
    private readonly Label _purchaseCoverage = new();
    private readonly Label _purchaseEmptyState = new();
    private readonly DataGridView _receivablesAgingGrid = new();
    private readonly DataGridView _receivablesGrid = new();
    private readonly CartesianChart _receivablesChart = new();
    private readonly DataGridView _payablesAgingGrid = new();
    private readonly DataGridView _payablesGrid = new();
    private readonly CartesianChart _payablesChart = new();
    private readonly FlowLayoutPanel _commercialCards = new();
    private readonly CartesianChart _commercialChart = new();
    private readonly DataGridView _commercialConversionsGrid = new();
    private readonly DataGridView _commercialCustomersGrid = new();
    private readonly Label _commercialCoverage = new();
    private readonly Label _commercialEmptyState = new();
    private readonly Label _status = new();
    private readonly Dictionary<string, AnalyticsMetricCardControl> _metricCards = [];
    private readonly Dictionary<string, AnalyticsMetricCardControl> _financeMetricCards = [];
    private readonly Dictionary<string, AnalyticsMetricCardControl> _cashMetricCards = [];
    private readonly Dictionary<string, AnalyticsMetricCardControl> _purchaseMetricCards = [];
    private readonly Dictionary<string, AnalyticsMetricCardControl> _receivableMetricCards = [];
    private readonly Dictionary<string, AnalyticsMetricCardControl> _payableMetricCards = [];
    private readonly Dictionary<string, AnalyticsMetricCardControl> _commercialMetricCards = [];
    private ProductAnalyticsReport? _productReport;
    private AnalyticsQueryRequest? _lastRequest;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _productTrendCts;
    private LiveSearchController? _productSearchController;
    private bool _bindingProductRanking;
    private bool _loadingClassificationFilters;
    private bool _classificationFiltersLoaded;
    private List<AnalyticsCategoryChoice> _categoryChoices = [];
    private List<AnalyticsSubcategoryChoice> _subcategoryChoices = [];
    private bool _resourcesDisposed;

    public AnalyticsForm(
        ISalesAnalyticsQueryService analytics,
        IFinancialAnalyticsQueryService financialAnalytics,
        ICommercialAnalyticsQueryService commercialAnalytics,
        IServiceProvider? serviceProvider = null)
    {
        _analytics = analytics;
        _financialAnalytics = financialAnalytics;
        _commercialAnalytics = commercialAnalytics;
        _serviceProvider = serviceProvider;
        BuildUi();
        Shown += async (_, _) => await RefreshAllAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _productTrendCts?.Cancel();
            _productTrendCts?.Dispose();
            _productSearchController?.Dispose();
            _coverageToolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Text = "Centro de análisis";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(980, 640);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);
        BackColor = UITheme.DashboardBackgroundColor;

        _shell.Dock = DockStyle.Fill;
        _shell.ColumnCount = 1;
        _shell.RowCount = 4;
        _shell.ColumnStyles.Clear();
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _shell.Padding = new Padding(16);
        _shell.BackColor = UITheme.DashboardBackgroundColor;
        _shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        _shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        _shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        _shell.Controls.Add(BuildHeader(), 0, 0);
        _shell.Controls.Add(_periodSelector, 0, 1);
        _shell.Controls.Add(BuildWorkspace(), 0, 2);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Font = new Font("Segoe UI", 9f);
        _productStatus.Visible = false;
        _shell.Controls.Add(_status, 0, 3);
        _shell.Controls.Add(_productStatus, 0, 3);

        _periodSelector.RefreshRequested += async (_, _) => await RefreshAllAsync();
        Controls.Add(_shell);
        SelectSection(1);
    }

    private static Control BuildHeader()
    {
        var header = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12, 4, 16, 4),
            Margin = new Padding(0, 0, 0, 5)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new Label
        {
            Text = "\uE9D2",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Font = new Font("Segoe MDL2 Assets", 24f),
            ForeColor = Color.White,
            BackColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var copy = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "Centro de análisis",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 31, 43),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        var subtitle = new Label
        {
            Text = "Ventas, operaciones comerciales, finanzas, caja, inversión y obligaciones en un solo espacio de decisión.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        };
        copy.Controls.Add(title, 0, 0);
        copy.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(icon, 0, 0);
        layout.Controls.Add(copy, 1, 0);
        header.Controls.Add(layout);
        return header;
    }

    private Control BuildWorkspace()
    {
        _workspace.Dock = DockStyle.Fill;
        _workspace.ColumnCount = 1;
        _workspace.RowCount = 2;
        _workspace.ColumnStyles.Clear();
        _workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _workspace.BackColor = UITheme.DashboardBackgroundColor;
        _workspace.Margin = Padding.Empty;
        _workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        _workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _navigation.Dock = DockStyle.Fill;
        _navigation.ColumnCount = 5;
        _navigation.RowCount = 2;
        _navigation.Padding = new Padding(0, 4, 0, 8);
        _navigation.BackColor = UITheme.DashboardBackgroundColor;
        _navigation.Tag = "analytics-navigation";

        var sections = new (string Title, string Icon, Control View)[]
        {
            ("Ventas", "\uE7BF", BuildSalesView()),
            ("Productos", "\uE7B8", BuildProductsView()),
            ("Categorías", "\uE8EC", BuildCategoriesView()),
            ("Finanzas", "\uE9D9", BuildFinanceView()),
            ("Caja", "\uE8C7", BuildCashView()),
            ("Comercial", "\uE8F1", BuildCommercialView()),
            ("Compras e inversión", "\uE719", BuildPurchaseView()),
            ("Cuentas por cobrar", "\uE716", BuildObligationView(
                _receivableMetricCards,
                _receivablesAgingGrid,
                _receivablesGrid,
                _receivablesChart,
                "Antigüedad de cartera",
                "Clientes con mayor saldo")),
            ("Cuentas por pagar", "\uE8CB", BuildObligationView(
                _payableMetricCards,
                _payablesAgingGrid,
                _payablesGrid,
                _payablesChart,
                "Antigüedad de obligaciones",
                "Proveedores con mayor saldo"))
        };

        _sectionHost.Dock = DockStyle.Fill;
        _sectionHost.AutoScroll = true;
        _sectionHost.BackColor = UITheme.DashboardBackgroundColor;
        _sectionHost.Margin = Padding.Empty;
        _sectionHost.Resize += (_, _) => UpdateActiveViewBounds();

        for (var index = 0; index < sections.Length; index++)
        {
            var sectionIndex = index;
            var button = new AnalyticsNavigationButton(sections[index].Title, sections[index].Icon);
            button.Click += (_, _) => SelectSection(sectionIndex);
            _navigation.Controls.Add(button, index % 5, index / 5);
            _sectionButtons.Add(button);

            var view = sections[index].View;
            view.Visible = false;
            if (index is 1 or 2)
            {
                view.Dock = DockStyle.None;
            }
            view.Margin = Padding.Empty;
            _sectionHost.Controls.Add(view);
            _sectionPages.Add(view);
        }

        _navigation.Resize += (_, _) => ConfigureNavigationLayout();
        _workspace.Controls.Add(_navigation, 0, 0);
        _workspace.Controls.Add(_sectionHost, 0, 1);
        ConfigureNavigationLayout();
        return _workspace;
    }

    private void ConfigureNavigationLayout()
    {
        if (_sectionButtons.Count == 0)
        {
            return;
        }

        var requiredOneRowWidth = _sectionButtons
            .Sum(button => button.PreferredSize.Width + button.Margin.Horizontal);
        var oneRow = _navigation.ClientSize.Width >=
            Math.Max(LogicalToDeviceUnits(1450), requiredOneRowWidth + _navigation.Padding.Horizontal);
        var columns = oneRow ? _sectionButtons.Count : 5;
        var rows = oneRow ? 1 : 2;
        _navigation.SuspendLayout();
        _navigation.ColumnStyles.Clear();
        _navigation.RowStyles.Clear();
        _navigation.ColumnCount = columns;
        _navigation.RowCount = rows;
        for (var column = 0; column < columns; column++)
        {
            _navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }
        for (var row = 0; row < rows; row++)
        {
            _navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        }
        for (var index = 0; index < _sectionButtons.Count; index++)
        {
            _navigation.SetCellPosition(
                _sectionButtons[index],
                new TableLayoutPanelCellPosition(index % columns, index / columns));
        }
        _workspace.RowStyles[0].SizeType = SizeType.Absolute;
        _workspace.RowStyles[0].Height = oneRow ? 62 : 98;
        _navigation.ResumeLayout(true);
    }

    private void SelectSection(int selectedIndex)
    {
        for (var index = 0; index < _sectionPages.Count; index++)
        {
            var selected = index == selectedIndex;
            _sectionPages[index].Visible = selected;
            _sectionButtons[index].SetSelected(selected);
        }

        _sectionHost.Tag = selectedIndex;
        var productsSelected = selectedIndex == 1;
        _productStatus.Visible = productsSelected;
        _status.Visible = !productsSelected;
        if (_shell.RowStyles.Count > 3)
        {
            _shell.RowStyles[3].Height = productsSelected ? 104 : 32;
        }
        UpdateActiveViewBounds();
        _sectionPages[selectedIndex].BringToFront();
    }

    private void UpdateActiveViewBounds()
    {
        if (_sectionHost.Tag is not int selectedIndex || selectedIndex < 0 || selectedIndex >= _sectionPages.Count)
        {
            return;
        }

        var page = _sectionPages[selectedIndex];
        var availableWidth = Math.Max(1, _sectionHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        var stackProducts = selectedIndex == 1 && availableWidth < LogicalToDeviceUnits(1180);
        var stackCategories = selectedIndex == 2 && availableWidth < LogicalToDeviceUnits(1350);
        var stackCommercial = selectedIndex == 5 && availableWidth < LogicalToDeviceUnits(1050);
        var stackObligations = selectedIndex >= 7 && availableWidth < LogicalToDeviceUnits(1050);
        var logicalMinimumHeight = stackProducts ? 760 :
            stackCategories ? 860 :
            stackCommercial ? 920 :
            stackObligations ? 820 :
            selectedIndex == 2 ? 620 :
            selectedIndex == 4 ? 520 :
            selectedIndex == 1 ? 320 : 500;
        page.Bounds = new Rectangle(
            0,
            0,
            availableWidth,
            Math.Max(_sectionHost.ClientSize.Height, LogicalToDeviceUnits(logicalMinimumHeight)));
        page.PerformLayout();
    }

    private Control BuildSalesView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AnalyticsMetricCardControl.LogicalMinimumHeight + 10));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        ConfigureCards(
            _salesCards,
            _metricCards,
            ["Brutas", "Descuentos", "Devoluciones", "Netas", "Transacciones", "Ticket", "Unidades"],
            layout);

        ConfigureCartesianChart(_salesChart);
        _salesCoverage.Dock = DockStyle.Fill;
        _salesCoverage.TextAlign = ContentAlignment.MiddleLeft;
        _salesCoverage.ForeColor = UITheme.MutedTextColor;
        layout.Controls.Add(_salesCards, 0, 0);
        layout.Controls.Add(WrapSurface("Tendencia del período", _salesChart), 0, 1);
        layout.Controls.Add(_salesCoverage, 0, 2);
        return layout;
    }

    private Control BuildProductsView()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(4, 0, 4, 0),
            BackColor = UITheme.DashboardBackgroundColor,
            Name = "ProductsAnalyticsWorkspace"
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = BuildProductsToolbar();
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UITheme.DashboardBackgroundColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Name = "ProductsAnalyticsContent"
        };
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.DashboardBackgroundColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, AnalyticsProductMetricCardControl.LogicalHeight + 10));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(BuildProductKpis(), 0, 0);
        left.Controls.Add(BuildProductRankingSurface(), 0, 1);
        var chartSurface = BuildProductChartSurface();
        content.Controls.Add(left, 0, 0);
        content.Controls.Add(chartSurface, 1, 0);
        ConfigureProductsLayout(content, left, chartSurface);
        content.Resize += (_, _) =>
        {
            ConfigureProductsLayout(content, left, chartSurface);
            UpdateActiveViewBounds();
        };

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private Control BuildProductsToolbar()
    {
        var surface = new DashboardSurfacePanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 2, 5, 5),
            Padding = new Padding(12, 7, 12, 7)
        };
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = _serviceProvider is null ? 3 : 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 335));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        if (_serviceProvider is not null)
        {
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        }

        var searchHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(0, 0, 14, 0),
            Padding = new Padding(1)
        };
        searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        searchHost.Controls.Add(new Label
        {
            Text = "⌕",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Symbol", 15f, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = Padding.Empty
        }, 0, 0);
        _productSearch.Name = "ProductAnalyticsSearchTextBox";
        _productSearch.AccessibleName = "Buscar producto o código";
        _productSearch.PlaceholderText = "Buscar producto o código";
        _productSearch.Dock = DockStyle.Fill;
        _productSearch.BorderStyle = BorderStyle.FixedSingle;
        _productSearch.Font = new Font("Segoe UI", 9.5f);
        _productSearch.Margin = new Padding(0, 6, 0, 0);
        searchHost.Controls.Add(_productSearch, 1, 0);
        _productSearchClear.Name = "ClearProductAnalyticsSearchButton";
        _productSearchClear.AccessibleName = "Limpiar búsqueda de productos";
        _productSearchClear.Text = "×";
        _productSearchClear.Dock = DockStyle.Fill;
        _productSearchClear.FlatStyle = FlatStyle.Flat;
        _productSearchClear.FlatAppearance.BorderSize = 0;
        _productSearchClear.FlatAppearance.MouseOverBackColor = UITheme.SoftPinkColor;
        _productSearchClear.BackColor = UITheme.DashboardAccentSoftColor;
        _productSearchClear.ForeColor = UITheme.PrimaryColor;
        _productSearchClear.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _productSearchClear.Cursor = Cursors.Hand;
        _productSearchClear.Margin = Padding.Empty;
        searchHost.Controls.Add(_productSearchClear, 2, 0);

        _productRanking.Name = "ProductAnalyticsRankingComboBox";
        _productRanking.DropDownStyle = ComboBoxStyle.DropDownList;
        _productRanking.Dock = DockStyle.Fill;
        _productRanking.Items.AddRange(
        [
            "Ranking: Más vendidos por unidades",
            "Ranking: Mayor venta neta",
            "Ranking: Más devueltos",
            "Ranking: Sin ventas"
        ]);
        _productRanking.SelectedIndex = 0;
        _productRanking.Margin = new Padding(10, 0, 8, 0);

        _productLimit.Name = "ProductAnalyticsLimitComboBox";
        _productLimit.DropDownStyle = ComboBoxStyle.DropDownList;
        _productLimit.Dock = DockStyle.Fill;
        _productLimit.DisplayMember = nameof(ProductLimitChoice.Text);
        _productLimit.Items.AddRange(
        [
            new ProductLimitChoice("Mostrar: Top 5", 5),
            new ProductLimitChoice("Mostrar: Top 10", 10),
            new ProductLimitChoice("Mostrar: Top 25", 25),
            new ProductLimitChoice("Mostrar: Todos", null)
        ]);
        _productLimit.SelectedIndex = 1;
        _productLimit.Margin = new Padding(8, 0, 0, 0);

        toolbar.Controls.Add(searchHost, 0, 0);
        toolbar.Controls.Add(_productRanking, 1, 0);
        toolbar.Controls.Add(_productLimit, 2, 0);
        if (_serviceProvider is not null)
        {
            var profitability = new Button
            {
                Name = "ProductProfitabilityButton",
                AccessibleName = "Abrir rentabilidad por producto",
                Text = "Rentabilidad",
                Dock = DockStyle.Top,
                AutoSize = false,
                Margin = new Padding(8, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            UITheme.StyleSecondaryButton(profitability);
            profitability.MinimumSize = new Size(0, UITheme.CompactControlHeight);
            profitability.Height = UITheme.CompactControlHeight;
            profitability.AutoEllipsis = false;
            profitability.Padding = new Padding(12, 0, 12, 0);
            _coverageToolTip.SetToolTip(
                profitability,
                "Abre el reporte histórico de ventas, costos, ganancias y margen por producto.");
            profitability.Click += (_, _) => OpenProductProfitability();
            toolbar.Controls.Add(profitability, 3, 0);
        }
        surface.Controls.Add(toolbar);

        _productSearchController = new LiveSearchController(
            _productSearch,
            _productSearchClear,
            ApplyProductFiltersAsync,
            _ => ShowProductRankingError(),
            TimeSpan.FromMilliseconds(300));
        _productRanking.SelectedIndexChanged += async (_, _) => await ApplyProductFiltersNowAsync();
        _productLimit.SelectedIndexChanged += async (_, _) => await ApplyProductFiltersNowAsync();
        return surface;
    }

    private void OpenProductProfitability()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        using var form = (ProductProfitabilityForm)_serviceProvider
            .GetService(typeof(ProductProfitabilityForm))!;
        form.ShowDialog(this);
    }

    private Control BuildProductKpis()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(5, 0, 0, 5),
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        _mostReturnedCard.Margin = Padding.Empty;
        layout.Controls.Add(_mostSoldCard, 0, 0);
        layout.Controls.Add(_highestSalesCard, 1, 0);
        layout.Controls.Add(_mostReturnedCard, 2, 0);
        return layout;
    }

    private Control BuildProductRankingSurface()
    {
        ConfigureProductRankingGrid();
        var stateHost = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Margin = Padding.Empty };
        ConfigureProductStateLabel(_productGridEmptyState, "ProductAnalyticsRankingEmptyState");
        stateHost.Controls.Add(_productsGrid);
        stateHost.Controls.Add(_productGridEmptyState);
        return WrapSurface("Ranking de productos", stateHost);
    }

    private Control BuildProductChartSurface()
    {
        ConfigureCartesianChart(_productChart);
        _productChart.Name = "ProductAnalyticsTrendChart";
        _productChart.Dock = DockStyle.Fill;
        _productChart.Margin = Padding.Empty;
        var panel = new DashboardSurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(5) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 8, 12, 2),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = "Evolución del producto",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        }, 0, 0);
        _productChartTitle.Name = "ProductAnalyticsChartTitle";
        _productChartTitle.Text = "Selecciona un producto";
        _productChartTitle.Dock = DockStyle.Fill;
        _productChartTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _productChartTitle.ForeColor = UITheme.PrimaryColor;
        _productChartTitle.AutoEllipsis = true;
        _productChartTitle.Margin = Padding.Empty;
        header.Controls.Add(_productChartTitle, 0, 1);
        _productChartSubtitle.Name = "ProductAnalyticsChartSubtitle";
        _productChartSubtitle.Text = "La evolución aparecerá aquí.";
        _productChartSubtitle.Dock = DockStyle.Fill;
        _productChartSubtitle.Font = new Font("Segoe UI", 9f);
        _productChartSubtitle.ForeColor = UITheme.MutedTextColor;
        _productChartSubtitle.AutoEllipsis = true;
        _productChartSubtitle.Margin = Padding.Empty;
        header.Controls.Add(_productChartSubtitle, 0, 2);

        var stateHost = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Margin = new Padding(6) };
        ConfigureProductStateLabel(_productChartState, "ProductAnalyticsTrendState");
        _productChartState.Text = "Selecciona un producto para consultar su evolución.";
        stateHost.Controls.Add(_productChart);
        stateHost.Controls.Add(_productChartState);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(stateHost, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static void ConfigureProductStateLabel(Label label, string name)
    {
        label.Name = name;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        label.ForeColor = UITheme.MutedTextColor;
        label.BackColor = UITheme.SurfaceColor;
        label.Padding = new Padding(24);
    }

    private void ConfigureProductRankingGrid()
    {
        ConfigureGrid(_productsGrid);
        _productsGrid.Name = "ProductAnalyticsRankingGrid";
        _productsGrid.AutoGenerateColumns = false;
        _productsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _productsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 248, 250);
        _productsGrid.ColumnHeadersDefaultCellStyle.ForeColor = UITheme.TextDark;
        _productsGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(250, 248, 250);
        _productsGrid.ColumnHeadersHeight = 36;
        _productsGrid.RowTemplate.Height = 32;
        _productsGrid.GridColor = UITheme.DashboardBorderColor;
        _productsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 231, 241);
        _productsGrid.Columns.AddRange(
            ProductTextColumn("Rank", "#", 40, DataGridViewContentAlignment.MiddleCenter),
            ProductTextColumn("Code", "Código", 100),
            ProductTextColumn("Product", "Producto", 145, fillWeight: 190),
            ProductTextColumn("Sold", "Vendidas", 78, DataGridViewContentAlignment.MiddleCenter),
            ProductTextColumn("NetSales", "Ventas netas", 100, DataGridViewContentAlignment.MiddleRight),
            ProductTextColumn("Returns", "Dev.", 52, DataGridViewContentAlignment.MiddleCenter),
            ProductTextColumn("Stock", "Stock", 58, DataGridViewContentAlignment.MiddleCenter),
            ProductTextColumn("LastSale", "Última venta", 100, DataGridViewContentAlignment.MiddleCenter),
            ProductTextColumn("Quality", "Calidad", 110, DataGridViewContentAlignment.MiddleCenter));
        _productsGrid.Columns["Product"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _productsGrid.CurrentCellChanged += async (_, _) => await LoadSelectedProductTrendAsync();
        _productsGrid.CellPainting += ProductsGrid_CellPainting;
        _productsGrid.CellFormatting += ProductsGrid_CellFormatting;
        _productsGrid.CellToolTipTextNeeded += ProductsGrid_CellToolTipTextNeeded;
    }

    private static DataGridViewTextBoxColumn ProductTextColumn(
        string property,
        string header,
        int width,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft,
        float fillWeight = 100) => new()
        {
            Name = property,
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            MinimumWidth = Math.Min(width, 42),
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        };

    private void ConfigureProductsLayout(TableLayoutPanel layout, Control ranking, Control chart)
    {
        if (layout.ClientSize.Width <= 0)
        {
            return;
        }

        var stacked = layout.ClientSize.Width < LogicalToDeviceUnits(1180);
        layout.SuspendLayout();
        layout.ColumnStyles.Clear();
        layout.RowStyles.Clear();
        layout.ColumnCount = stacked ? 1 : 2;
        layout.RowCount = stacked ? 2 : 1;
        if (stacked)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 51));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 49));
            layout.SetCellPosition(ranking, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(0, 1));
        }
        else
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.SetCellPosition(ranking, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(1, 0));
        }
        layout.ResumeLayout(true);
    }

    private Control BuildCategoriesView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        ConfigureGrid(_categoriesGrid);
        ConfigureGrid(_subcategoriesGrid);
        _categoriesGrid.Name = "AnalyticsCategoriesGrid";
        _subcategoriesGrid.Name = "AnalyticsSubcategoriesGrid";
        _categoryPie.Name = "AnalyticsCategoryPieChart";
        _categoryTrend.Name = "AnalyticsCategoryTrendChart";
        ConfigurePieChart(_categoryPie);
        ConfigureCartesianChart(_categoryTrend);
        _categoryTrend.LegendPosition = LiveChartsCore.Measure.LegendPosition.Right;

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false,
            MinimumSize = Size.Empty
        };
        var chartSurface = WrapSurface("Participación por categoría", _categoryPie);
        chartSurface.MinimumSize = Size.Empty;
        split.Controls.Add(chartSurface, 0, 0);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        right.MinimumSize = Size.Empty;
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        right.Controls.Add(WrapSurface("Resumen por categoría", _categoriesGrid), 0, 0);
        right.Controls.Add(WrapSurface("Resumen por subcategoría", BuildSubcategoryGridState()), 0, 1);
        right.Controls.Add(WrapSurface("Tendencia", BuildCategoryTrendState()), 0, 2);
        split.Controls.Add(right, 1, 0);
        split.Resize += (_, _) => ConfigureCategoriesLayout(split, chartSurface, right);
        ConfigureCategoriesLayout(split, chartSurface, right);

        _categoryCoverage.Dock = DockStyle.Fill;
        _categoryCoverage.TextAlign = ContentAlignment.MiddleLeft;
        _categoryCoverage.ForeColor = UITheme.MutedTextColor;
        layout.Controls.Add(BuildClassificationFilters(), 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(_categoryCoverage, 0, 2);
        return layout;
    }

    private void ConfigureCategoriesLayout(
        TableLayoutPanel layout,
        Control chart,
        Control summaries)
    {
        if (layout.ClientSize.Width <= 0)
        {
            return;
        }

        var stacked = layout.ClientSize.Width < LogicalToDeviceUnits(1350);
        layout.SuspendLayout();
        layout.ColumnStyles.Clear();
        layout.RowStyles.Clear();
        layout.ColumnCount = stacked ? 1 : 2;
        layout.RowCount = stacked ? 2 : 1;
        if (stacked)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(summaries, new TableLayoutPanelCellPosition(0, 1));
        }
        else
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(summaries, new TableLayoutPanelCellPosition(1, 0));
        }

        layout.ResumeLayout(true);
    }

    private Control BuildClassificationFilters()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(6, 4, 6, 2),
            Margin = Padding.Empty
        };
        ConfigureClassificationFilter(_categoryFilter, "Categoría", 240);
        ConfigureClassificationFilter(_subcategoryFilter, "Subcategoría", 260);
        _categoryFilter.DisplayMember = nameof(AnalyticsCategoryChoice.Text);
        _categoryFilter.ValueMember = nameof(AnalyticsCategoryChoice.Id);
        _categoryFilter.DataSource = new List<AnalyticsCategoryChoice> { new(null, "Todas") };
        _subcategoryFilter.DisplayMember = nameof(AnalyticsSubcategoryChoice.Text);
        _subcategoryFilter.ValueMember = nameof(AnalyticsSubcategoryChoice.Id);
        _subcategoryFilter.DataSource = new List<AnalyticsSubcategoryChoice>
        {
            new(null, "Todas", null),
            new(null, AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel, null,
                AnalyticsSubcategoryFilter.ExplicitlyUnassigned),
            new(null, AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel, null,
                AnalyticsSubcategoryFilter.HistoricalUnavailable)
        };
        _categoryFilter.SelectedValueChanged += (_, _) =>
        {
            FilterAnalyticsSubcategories();
            RequestClassificationRefresh();
        };
        _subcategoryFilter.SelectedValueChanged += (_, _) => RequestClassificationRefresh();
        flow.Controls.Add(FilterField("Categoría", _categoryFilter));
        flow.Controls.Add(FilterField("Subcategoría", _subcategoryFilter));
        return flow;
    }

    private Control BuildCategoryTrendState()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
            Margin = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        _categoryTrend.Dock = DockStyle.Fill;
        _categoryTrendEmptyState.Name = "AnalyticsCategoryTrendEmptyState";
        _categoryTrendEmptyState.Dock = DockStyle.Fill;
        _categoryTrendEmptyState.Text = "Sin registros";
        _categoryTrendEmptyState.TextAlign = ContentAlignment.MiddleCenter;
        _categoryTrendEmptyState.ForeColor = UITheme.MutedTextColor;
        _categoryTrendEmptyState.Visible = false;
        host.Controls.Add(_categoryTrend);
        host.Controls.Add(_categoryTrendEmptyState);
        return host;
    }

    private static void ConfigureClassificationFilter(ComboBox combo, string accessibleName, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Width = width;
        combo.Height = UITheme.CompactControlHeight;
        combo.AccessibleName = accessibleName;
        combo.Margin = Padding.Empty;
    }

    private static Control FilterField(string label, Control control)
    {
        var panel = new TableLayoutPanel
        {
            Width = control.Width + 12,
            Height = 44,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = Padding.Empty
        }, 0, 0);
        panel.Controls.Add(control, 0, 1);
        return panel;
    }

    private Control BuildSubcategoryGridState()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), Margin = Padding.Empty };
        _subcategoryEmptyState.Dock = DockStyle.Fill;
        _subcategoryEmptyState.Text = "Sin registros de subcategorías para el período seleccionado.";
        _subcategoryEmptyState.TextAlign = ContentAlignment.MiddleCenter;
        _subcategoryEmptyState.ForeColor = UITheme.MutedTextColor;
        _subcategoryEmptyState.Visible = false;
        host.Controls.Add(_subcategoriesGrid);
        host.Controls.Add(_subcategoryEmptyState);
        return host;
    }

    private Control BuildFinanceView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AnalyticsMetricCardControl.LogicalMinimumHeight + 10));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ConfigureCards(
            _financeCards,
            _financeMetricCards,
            ["Ventas netas", "Costo vendido", "Ganancia bruta", "Margen bruto", "Gastos operativos", "Inversión inventario"],
            layout);
        ConfigureCartesianChart(_financeChart);
        ConfigureGrid(_expensesGrid);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.Controls.Add(_financeCards, 0, 0);
        content.Controls.Add(WrapSurface("Ganancia bruta exacta en el tiempo", _financeChart, _financeCoverage), 0, 0);
        content.Controls.Add(WrapSurface("Gastos del período", _expensesGrid), 1, 0);
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private Control BuildCashView()
    {
        ConfigureCards(
            _cashCards,
            _cashMetricCards,
            ["Efectivo esperado", "Ventas efectivo", "Cobros clientes", "Otras entradas", "Aportes para caja", "Reembolsos", "Pagos proveedores", "Gastos", "Gastos fuera de caja", "Pagos empleados", "Pagos empleados fuera de caja", "Retiros", "Movimiento neto", "Diferencia cierre"]);
        _cashCards.Padding = new Padding(10);
        _cashCards.AutoScroll = true;
        return WrapSurface("Caja global de solo lectura", _cashCards);
    }

    private Control BuildCommercialView()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(4)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AnalyticsMetricCardControl.LogicalMinimumHeight + 10));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ConfigureCards(
            _commercialCards,
            _commercialMetricCards,
            ["Cotizaciones", "Importe cotizado", "Aceptadas", "Pedidos", "Anticipos", "Saldo pedidos", "Pedido → venta", "Atrasados"],
            layout);

        ConfigureGrid(_commercialConversionsGrid);
        ConfigureGrid(_commercialCustomersGrid);
        ConfigureCartesianChart(_commercialChart);
        _commercialConversionsGrid.Name = "CommercialConversionGrid";
        _commercialCustomersGrid.Name = "CommercialCustomerRankingGrid";
        _commercialChart.Name = "CommercialTrendChart";

        var chartState = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor };
        _commercialChart.Dock = DockStyle.Fill;
        _commercialEmptyState.Name = "CommercialAnalyticsEmptyState";
        _commercialEmptyState.Text = "No hubo cotizaciones ni pedidos personalizados en este período.";
        _commercialEmptyState.Dock = DockStyle.Fill;
        _commercialEmptyState.TextAlign = ContentAlignment.MiddleCenter;
        _commercialEmptyState.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _commercialEmptyState.ForeColor = UITheme.MutedTextColor;
        _commercialEmptyState.BackColor = UITheme.SurfaceColor;
        _commercialEmptyState.Padding = new Padding(24);
        chartState.Controls.Add(_commercialChart);
        chartState.Controls.Add(_commercialEmptyState);

        var conversionSurface = WrapSurface("Conversión por cohorte", _commercialConversionsGrid);
        var chartSurface = WrapSurface("Cotizaciones y pedidos en el tiempo", chartState, _commercialCoverage);
        var customerSurface = WrapSurface("Clientes con mayor actividad comercial", _commercialCustomersGrid);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        content.Controls.Add(conversionSurface, 0, 0);
        content.Controls.Add(chartSurface, 1, 0);
        content.Controls.Add(customerSurface, 2, 0);
        ConfigureCommercialLayout(content, conversionSurface, chartSurface, customerSurface);
        content.Resize += (_, _) => ConfigureCommercialLayout(content, conversionSurface, chartSurface, customerSurface);

        layout.Controls.Add(_commercialCards, 0, 0);
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private Control BuildPurchaseView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AnalyticsMetricCardControl.LogicalMinimumHeight + 10));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ConfigureCards(
            _purchaseCards,
            _purchaseMetricCards,
            [
                "Compras comprometidas", "Mercancía recibida", "Pagos proveedores",
                "Inversión recibida", "Inversión actual", "Venta potencial",
                "Venta disponible", "Ganancia bruta potencial", "Margen bruto potencial"
            ],
            layout);
        ConfigureCartesianChart(_purchaseChart);
        var chartState = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor };
        _purchaseChart.Dock = DockStyle.Fill;
        _purchaseEmptyState.Text = "No hubo mercancía recibida en este período.";
        _purchaseEmptyState.Name = "PurchaseAnalyticsEmptyState";
        _purchaseEmptyState.Dock = DockStyle.Fill;
        _purchaseEmptyState.TextAlign = ContentAlignment.MiddleCenter;
        _purchaseEmptyState.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _purchaseEmptyState.ForeColor = UITheme.MutedTextColor;
        _purchaseEmptyState.BackColor = UITheme.SurfaceColor;
        _purchaseEmptyState.Padding = new Padding(24);
        chartState.Controls.Add(_purchaseChart);
        chartState.Controls.Add(_purchaseEmptyState);
        layout.Controls.Add(_purchaseCards, 0, 0);
        layout.Controls.Add(WrapSurface("Valor neto recibido por período", chartState, _purchaseCoverage), 0, 1);
        return layout;
    }

    private static Control BuildObligationView(
        IDictionary<string, AnalyticsMetricCardControl> cards,
        DataGridView agingGrid,
        DataGridView rankingGrid,
        CartesianChart chart,
        string agingTitle,
        string rankingTitle)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AnalyticsMetricCardControl.LogicalMinimumHeight + 10));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var flow = new FlowLayoutPanel();
        ConfigureCards(
            flow,
            cards,
            ["Saldo pendiente", "Creadas en período", "Pagos del período", "Cuentas pagadas", "Cuentas vencidas"],
            layout);
        ConfigureGrid(agingGrid);
        ConfigureGrid(rankingGrid);
        ConfigureCartesianChart(chart);
        var agingSurface = WrapSurface(agingTitle, agingGrid);
        var rankingSurface = WrapSurface(rankingTitle, rankingGrid);
        var chartSurface = WrapSurface("Evolución del saldo", chart);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        content.Controls.Add(agingSurface, 0, 0);
        content.Controls.Add(rankingSurface, 1, 0);
        content.Controls.Add(chartSurface, 2, 0);
        ConfigureObligationLayout(content, agingSurface, rankingSurface, chartSurface);
        content.Resize += (_, _) => ConfigureObligationLayout(content, agingSurface, rankingSurface, chartSurface);
        layout.Controls.Add(flow, 0, 0);
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private static void ConfigureCards(
        FlowLayoutPanel panel,
        IDictionary<string, AnalyticsMetricCardControl> destination,
        IReadOnlyList<string> titles,
        TableLayoutPanel? owner = null,
        int rowIndex = 0)
    {
        panel.Dock = DockStyle.Fill;
        panel.WrapContents = true;
        panel.AutoScroll = owner is null;
        panel.BackColor = Color.Transparent;
        panel.Margin = Padding.Empty;
        foreach (var title in titles)
        {
            var card = new AnalyticsMetricCardControl(title);
            destination[title] = card;
            panel.Controls.Add(card);
        }
        panel.Resize += (_, _) => LayoutMetricCards(panel, owner, rowIndex);
        panel.HandleCreated += (_, _) => LayoutMetricCards(panel, owner, rowIndex);
    }

    private static void LayoutMetricCards(FlowLayoutPanel panel, TableLayoutPanel? owner, int rowIndex)
    {
        if (panel.ClientSize.Width <= 0 || panel.Controls.Count == 0)
        {
            return;
        }

        var cards = panel.Controls.OfType<AnalyticsMetricCardControl>().ToArray();
        var firstCard = cards[0];
        var minimumWidth = firstCard.MinimumSize.Width;
        var availableWidth = Math.Max(1, panel.ClientSize.Width - panel.Padding.Horizontal);
        var columns = Math.Max(1, Math.Min(cards.Length, availableWidth / Math.Max(1, minimumWidth + 10)));
        var cardWidth = Math.Max(minimumWidth, availableWidth / columns - 10);
        var cardHeight = cards.Max(card => card.GetPreferredSize(new Size(cardWidth, 0)).Height);
        foreach (var card in cards)
        {
            card.Size = new Size(cardWidth, cardHeight);
        }

        if (owner is null || rowIndex >= owner.RowStyles.Count)
        {
            return;
        }

        var rows = (int)Math.Ceiling(cards.Length / (double)columns);
        var requiredHeight = panel.Padding.Vertical + rows * (cardHeight + 10);
        if (Math.Abs(owner.RowStyles[rowIndex].Height - requiredHeight) > 1)
        {
            owner.RowStyles[rowIndex].SizeType = SizeType.Absolute;
            owner.RowStyles[rowIndex].Height = requiredHeight;
        }
    }

    private static void ConfigureObligationLayout(
        TableLayoutPanel layout,
        Control aging,
        Control ranking,
        Control chart)
    {
        if (layout.ClientSize.Width <= 0)
        {
            return;
        }

        var stacked = layout.ClientSize.Width < (int)Math.Round(1050 * layout.DeviceDpi / 96d);
        layout.SuspendLayout();
        layout.ColumnStyles.Clear();
        layout.RowStyles.Clear();
        layout.ColumnCount = stacked ? 1 : 3;
        layout.RowCount = stacked ? 3 : 1;
        if (stacked)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var row = 0; row < 3; row++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
            }
            layout.SetCellPosition(aging, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(ranking, new TableLayoutPanelCellPosition(0, 1));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(0, 2));
        }
        else
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.SetCellPosition(aging, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(ranking, new TableLayoutPanelCellPosition(1, 0));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(2, 0));
        }
        layout.ResumeLayout(true);
    }

    private static void ConfigureCommercialLayout(
        TableLayoutPanel layout,
        Control conversions,
        Control chart,
        Control customers)
    {
        if (layout.ClientSize.Width <= 0)
        {
            return;
        }

        var stacked = layout.ClientSize.Width < (int)Math.Round(1050 * layout.DeviceDpi / 96d);
        layout.SuspendLayout();
        layout.ColumnStyles.Clear();
        layout.RowStyles.Clear();
        layout.ColumnCount = stacked ? 1 : 3;
        layout.RowCount = stacked ? 3 : 1;
        if (stacked)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
            layout.SetCellPosition(conversions, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(0, 1));
            layout.SetCellPosition(customers, new TableLayoutPanelCellPosition(0, 2));
        }
        else
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.SetCellPosition(conversions, new TableLayoutPanelCellPosition(0, 0));
            layout.SetCellPosition(chart, new TableLayoutPanelCellPosition(1, 0));
            layout.SetCellPosition(customers, new TableLayoutPanelCellPosition(2, 0));
        }
        layout.ResumeLayout(true);
    }

    private async Task RefreshAllAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _productTrendCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        _periodSelector.SetBusy(true);
        _status.ForeColor = UITheme.MutedTextColor;
        _status.Text = "Cargando análisis...";
        ShowProductLoading();
        _lastRequest = _periodSelector.BuildRequest();
        EnsureClassificationFilterSelections();
        var productLoaded = false;
        try
        {
            var sales = await _analytics.GetSalesAsync(_lastRequest, token);
            RenderSales(sales);
            try
            {
                _productReport = await _analytics.GetProductsAsync(
                    _lastRequest,
                    limit: SalesAnalyticsQueryLimits.MaximumProducts,
                    cancellationToken: token);
                RenderProductSummary();
                await ApplyProductFiltersAsync(token);
                SetProductFiltersEnabled(true);
                productLoaded = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _productReport = null;
                ShowProductLoadError();
            }
            await EnsureClassificationFiltersAsync(token);
            var classificationRequest = BuildClassificationRequest(_lastRequest);
            var categories = await _analytics.GetCategoriesAsync(classificationRequest, token);
            RenderCategories(categories);
            var financial = await _financialAnalytics.GetAsync(classificationRequest, token);
            RenderFinancial(financial);
            var commercial = await _commercialAnalytics.GetAsync(_lastRequest, token);
            RenderCommercial(commercial);
            _status.Text = $"Datos actualizados. Período: {sales.Period.DisplayName}. Comparación: {sales.ComparedPeriod.DisplayName}.";
            if (productLoaded)
            {
                _productStatus.SetSuccess(
                    $"Datos actualizados.\nPeríodo: {sales.Period.DisplayName}.\n" +
                    $"Comparación: {sales.ComparedPeriod.DisplayName}.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo cargar la analítica: {ex.Message}";
            _status.ForeColor = UITheme.DangerColor;
            _productStatus.SetError("No se pudo actualizar el Centro de análisis. Intenta nuevamente.");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _periodSelector.SetBusy(false);
            }
        }
    }

    private async Task EnsureClassificationFiltersAsync(CancellationToken cancellationToken)
    {
        if (_classificationFiltersLoaded || _serviceProvider is null)
        {
            return;
        }

        if (_serviceProvider.GetService(typeof(IAdvancedReportFilterOptionsService))
            is not IAdvancedReportFilterOptionsService optionsService)
        {
            _classificationFiltersLoaded = true;
            return;
        }

        _loadingClassificationFilters = true;
        try
        {
            var categories = await optionsService.GetOptionsAsync(
                AdvancedReportKind.Categories,
                AdvancedReportFilterKind.Category,
                limit: 100,
                cancellationToken: cancellationToken);
            _categoryChoices = categories
                .Select(option => new AnalyticsCategoryChoice(option.Id, option.Name))
                .ToList();
            _categoryFilter.DisplayMember = nameof(AnalyticsCategoryChoice.Text);
            _categoryFilter.ValueMember = nameof(AnalyticsCategoryChoice.Id);
            _categoryFilter.DataSource = new List<AnalyticsCategoryChoice> { new(null, "Todas") }
                .Concat(_categoryChoices)
                .ToList();
            EnsureClassificationFilterSelections();

            var subcategories = await optionsService.GetOptionsAsync(
                AdvancedReportKind.Categories,
                AdvancedReportFilterKind.Subcategory,
                limit: 100,
                cancellationToken: cancellationToken);
            _subcategoryChoices = subcategories
                .Where(option => AnalyticsSubcategoryFilterOptions.FromOptionId(option.Id) == AnalyticsSubcategoryFilter.All)
                .Select(option => new AnalyticsSubcategoryChoice(option.Id, option.Name, option.ParentId))
                .ToList();
            FilterAnalyticsSubcategories();
            _classificationFiltersLoaded = true;
        }
        finally
        {
            _loadingClassificationFilters = false;
        }
    }

    private void FilterAnalyticsSubcategories()
    {
        var categoryId = (_categoryFilter.SelectedItem as AnalyticsCategoryChoice)?.Id;
        var selected = _subcategoryFilter.SelectedItem as AnalyticsSubcategoryChoice;
        var choices = new List<AnalyticsSubcategoryChoice>
        {
            new(null, "Todas", null),
            new(null, AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel, null,
                AnalyticsSubcategoryFilter.ExplicitlyUnassigned),
            new(null, AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel, null,
                AnalyticsSubcategoryFilter.HistoricalUnavailable)
        };
        choices.AddRange(_subcategoryChoices
            .Where(choice => categoryId is null || choice.CategoryId == categoryId)
            .OrderBy(choice => choice.Text, StringComparer.CurrentCultureIgnoreCase));
        _subcategoryFilter.DisplayMember = nameof(AnalyticsSubcategoryChoice.Text);
        _subcategoryFilter.ValueMember = nameof(AnalyticsSubcategoryChoice.Id);
        _subcategoryFilter.DataSource = choices;
        var index = selected is null
            ? 0
            : choices.FindIndex(choice => choice.Id == selected.Id && choice.Filter == selected.Filter);
        if (index < 0)
        {
            index = 0;
        }
        if (_subcategoryFilter.Items.Count > 0)
        {
            _subcategoryFilter.SelectedIndex = Math.Min(index, _subcategoryFilter.Items.Count - 1);
        }
    }

    private void EnsureClassificationFilterSelections()
    {
        if (_categoryFilter.Items.Count > 0 && _categoryFilter.SelectedIndex < 0)
        {
            _categoryFilter.SelectedIndex = 0;
        }

        if (_subcategoryFilter.Items.Count > 0 && _subcategoryFilter.SelectedIndex < 0)
        {
            _subcategoryFilter.SelectedIndex = 0;
        }
    }

    private void RequestClassificationRefresh()
    {
        if (_loadingClassificationFilters || !_classificationFiltersLoaded || !IsHandleCreated)
        {
            return;
        }

        _ = RefreshAllAsync();
    }

    private AnalyticsQueryRequest BuildClassificationRequest(AnalyticsQueryRequest request)
    {
        var categoryId = (_categoryFilter.SelectedItem as AnalyticsCategoryChoice)?.Id;
        var selected = _subcategoryFilter.SelectedItem as AnalyticsSubcategoryChoice;
        var subcategoryId = selected?.Filter == AnalyticsSubcategoryFilter.All ? selected.Id : null;
        return request with
        {
            CategoryId = categoryId,
            SubcategoryId = subcategoryId,
            SubcategoryFilter = selected?.Filter ?? AnalyticsSubcategoryFilter.All
        };
    }

    private void RenderSales(SalesAnalyticsReport report)
    {
        _metricCards["Brutas"].SetMoney(report.Summary.GrossSales.Value, report.Summary.GrossSales.Comparison);
        _metricCards["Descuentos"].SetMoney(report.Summary.Discounts.Value, report.Summary.Discounts.Comparison);
        _metricCards["Devoluciones"].SetMoney(report.Summary.Returns.Value, report.Summary.Returns.Comparison);
        _metricCards["Netas"].SetMoney(report.Summary.NetSales.Value, report.Summary.NetSales.Comparison);
        _metricCards["Transacciones"].SetNumber(report.Summary.Transactions.Value, report.Summary.Transactions.Comparison);
        _metricCards["Ticket"].SetMoney(report.Summary.AverageTicket.Value, report.Summary.AverageTicket.Comparison);
        _metricCards["Unidades"].SetNumber(report.Summary.NetUnits.Value, report.Summary.NetUnits.Comparison);

        _salesChart.Series =
        [
            Line("Ventas netas", report.Trend.Select(point => point.NetSalesCentavos), UITheme.PrimaryColor, true),
            Line("Ventas brutas", report.Trend.Select(point => point.GrossSalesCentavos), UITheme.SecondaryColor, false),
            Line("Devoluciones", report.Trend.Select(point => point.ReturnsCentavos), UITheme.DangerColor, false)
        ];
        SetAxes(_salesChart, report.Trend.Select(point => point.Label).ToArray(), money: true);
        _salesCoverage.Text = FormatCoverage(report.Coverage);
    }

    private void RenderProductSummary()
    {
        if (_productReport is null)
        {
            _mostSoldCard.SetEmpty();
            _highestSalesCard.SetEmpty();
            _mostReturnedCard.SetEmpty();
            return;
        }

        if (_productReport.MostSoldByUnits.FirstOrDefault() is { } mostSold)
        {
            _mostSoldCard.SetContent(mostSold.ProductName, $"{mostSold.NetUnits:N0} {UnitWord(mostSold.NetUnits)}");
        }
        else
        {
            _mostSoldCard.SetEmpty();
        }

        if (_productReport.HighestNetSales.FirstOrDefault() is { } highestSales)
        {
            _highestSalesCard.SetContent(highestSales.ProductName, Money(highestSales.NetSalesCentavos));
        }
        else
        {
            _highestSalesCard.SetEmpty();
        }

        if (_productReport.MostReturned.FirstOrDefault() is { } mostReturned)
        {
            _mostReturnedCard.SetContent(
                mostReturned.ProductName,
                $"{mostReturned.ReturnedUnits:N0} {(mostReturned.ReturnedUnits == 1 ? "devuelta" : "devueltas")}");
        }
        else
        {
            _mostReturnedCard.SetEmpty("Sin devoluciones");
        }

        var coverage = _productReport.Coverage;
        _productStatus.SetCapture(coverage.ReliableSnapshotsFromUtc.HasValue
            ? $"Captura activa desde\n{coverage.ReliableSnapshotsFromUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}."
            : "Fecha de captura histórica no disponible.");
        _productStatus.SetCoverage(
            $"Cobertura histórica\nProducto {coverage.ProductSnapshots.Percentage:N1}% · " +
            $"Categoría {coverage.CategorySnapshots.Percentage:N1}% · " +
            $"Costo {coverage.CostSnapshots.Percentage:N1}% · " +
            $"Descuento {coverage.GeneralDiscountSnapshots.Percentage:N1}%");
    }

    private async Task ApplyProductFiltersNowAsync()
    {
        if (_productSearchController is null)
        {
            await ApplyProductFiltersAsync(CancellationToken.None);
            return;
        }

        await _productSearchController.ExecuteNowAsync(ApplyProductFiltersAsync);
    }

    private async Task ApplyProductFiltersAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (_productReport is null)
        {
            return;
        }

        var selectedProductId = (_productsGrid.CurrentRow?.DataBoundItem as ProductRankingDisplayRow)?.ProductId;
        var ranked = CurrentProductRows().Select((row, index) => row with { Rank = index + 1 });
        var search = _productSearch.Text.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            ranked = ranked.Where(row =>
                row.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.Product.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var limit = (_productLimit.SelectedItem as ProductLimitChoice)?.Limit;
        if (limit.HasValue)
        {
            ranked = ranked.Take(limit.Value);
        }
        var rows = ranked.ToList();
        cancellationToken.ThrowIfCancellationRequested();

        _bindingProductRanking = true;
        try
        {
            _productsGrid.DataSource = rows;
            _productsGrid.ClearSelection();
            if (rows.Count > 0)
            {
                var selectedIndex = selectedProductId.HasValue
                    ? rows.FindIndex(row => row.ProductId == selectedProductId.Value)
                    : 0;
                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                }
                _productsGrid.CurrentCell = _productsGrid.Rows[selectedIndex].Cells["Product"];
                _productsGrid.Rows[selectedIndex].Selected = true;
            }
        }
        finally
        {
            _bindingProductRanking = false;
        }

        var hasRows = rows.Count > 0;
        _productsGrid.Visible = hasRows;
        _productGridEmptyState.Visible = !hasRows;
        _productGridEmptyState.ForeColor = UITheme.MutedTextColor;
        if (!hasRows)
        {
            _productGridEmptyState.Text = !string.IsNullOrEmpty(search)
                ? $"No se encontraron productos para \"{search}\"."
                : _productRanking.SelectedIndex == 3
                    ? "No hay productos sin ventas para este período."
                    : "No hay productos con actividad para este período.";
            _productGridEmptyState.BringToFront();
            _productChartTitle.Text = "Sin producto seleccionado";
            _productChartSubtitle.Text = !string.IsNullOrEmpty(search)
                ? "Ajusta o limpia la búsqueda para ver una evolución."
                : "No existe actividad para construir la evolución.";
            ClearProductChart("No hay un producto para mostrar en la gráfica.");
            return;
        }

        _productsGrid.BringToFront();
        await LoadSelectedProductTrendAsync();
    }

    private IEnumerable<ProductRankingDisplayRow> CurrentProductRows()
    {
        if (_productReport is null)
        {
            return [];
        }

        if (_productRanking.SelectedIndex == 3)
        {
            return _productReport.WithoutSales.Select(row => new ProductRankingDisplayRow(
                row.ProductId,
                0,
                row.ProductCode,
                row.ProductName,
                0,
                0,
                0,
                row.CurrentStock,
                HumanizeLastSale(row.DaysSinceLastSale),
                "—"));
        }

        var rows = _productRanking.SelectedIndex switch
        {
            1 => _productReport.HighestNetSales,
            2 => _productReport.MostReturned,
            _ => _productReport.MostSoldByUnits
        };
        return rows.Select(row => new ProductRankingDisplayRow(
            row.ProductId,
            0,
            row.ProductCode,
            row.ProductName,
            row.NetUnits,
            row.NetSalesCentavos,
            row.ReturnedUnits,
            row.CurrentStock,
            HumanizeLastSale(row.DaysSinceLastSale),
            QualityText(row.SnapshotQuality)));
    }

    private async Task LoadSelectedProductTrendAsync()
    {
        if (_bindingProductRanking ||
            _lastRequest is null ||
            _productsGrid.CurrentRow?.DataBoundItem is not ProductRankingDisplayRow selected)
        {
            return;
        }

        _productTrendCts?.Cancel();
        _productTrendCts?.Dispose();
        _productTrendCts = _loadCts is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(_loadCts.Token);
        var token = _productTrendCts.Token;
        _productChartTitle.Text = $"{selected.Product}  ·  {selected.Code}";
        _productChartSubtitle.Text = $"{selected.Sold:N0} {UnitWord(selected.Sold)} · {Money(selected.NetSalesCentavos)} netos";
        _productChart.Visible = true;
        _productChartState.Text = "Cargando evolución del producto…";
        _productChartState.ForeColor = UITheme.MutedTextColor;
        _productChartState.Visible = true;
        _productChartState.BringToFront();

        try
        {
            var report = await _analytics.GetProductTrendAsync(selected.ProductId, _lastRequest, token);
            token.ThrowIfCancellationRequested();
            _productChartTitle.Text = $"{report.ProductName}  ·  {report.ProductCode}";
            if (!report.Trend.Any(point => point.NetSalesCentavos != 0))
            {
                ClearProductChart("Este producto no tuvo ventas netas en el período.");
                return;
            }

            _productChart.Series =
            [
                Line("Ventas netas", report.Trend.Select(point => point.NetSalesCentavos), UITheme.PrimaryColor, true)
            ];
            SetAxes(_productChart, report.Trend.Select(point => point.Label).ToArray(), money: true);
            _productChartState.Visible = false;
            _productChart.Visible = true;
            _productChart.BringToFront();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            ClearProductChart("No se pudo cargar la evolución. Intenta seleccionar el producto nuevamente.", error: true);
        }
    }

    private void ClearProductChart(string message, bool error = false)
    {
        _productChart.Series = [];
        _productChart.Visible = true;
        _productChartState.Text = message;
        _productChartState.ForeColor = error ? UITheme.DangerColor : UITheme.MutedTextColor;
        _productChartState.Visible = true;
        _productChartState.BringToFront();
    }

    private void ShowProductLoading()
    {
        _productReport = null;
        _productSearchController?.CancelPending();
        SetProductFiltersEnabled(false);
        _mostSoldCard.SetEmpty("Cargando…");
        _highestSalesCard.SetEmpty("Cargando…");
        _mostReturnedCard.SetEmpty("Cargando…");
        _productsGrid.Visible = false;
        _productGridEmptyState.Text = "Cargando productos…";
        _productGridEmptyState.Visible = true;
        _productGridEmptyState.BringToFront();
        _productChartTitle.Text = "Cargando productos";
        _productChartSubtitle.Text = "La evolución se actualizará automáticamente.";
        ClearProductChart("Preparando la evolución del producto…");
        _productStatus.SetLoading("Cargando analítica de productos…");
    }

    private void ShowProductLoadError()
    {
        _mostSoldCard.SetEmpty("No disponible");
        _highestSalesCard.SetEmpty("No disponible");
        _mostReturnedCard.SetEmpty("No disponible");
        _productsGrid.Visible = false;
        _productGridEmptyState.Text = "No se pudieron cargar los productos. Intenta actualizar nuevamente.";
        _productGridEmptyState.Visible = true;
        _productGridEmptyState.BringToFront();
        _productChartTitle.Text = "Evolución no disponible";
        _productChartSubtitle.Text = "La consulta de productos no pudo completarse.";
        ClearProductChart("No se pudo preparar la gráfica del producto.", error: true);
        _productStatus.SetError("No se pudo actualizar Productos. Intenta nuevamente.");
    }

    private void SetProductFiltersEnabled(bool enabled)
    {
        _productSearch.Enabled = enabled;
        _productRanking.Enabled = enabled;
        _productLimit.Enabled = enabled;
        _productSearchClear.Enabled = enabled && !string.IsNullOrEmpty(_productSearch.Text);
    }

    private void ShowProductRankingError()
    {
        _productsGrid.Visible = false;
        _productGridEmptyState.Text = "No se pudo aplicar la búsqueda. Intenta nuevamente.";
        _productGridEmptyState.ForeColor = UITheme.DangerColor;
        _productGridEmptyState.Visible = true;
        _productGridEmptyState.BringToFront();
    }

    private void ProductsGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        e.CellStyle.SelectionBackColor = Color.FromArgb(255, 231, 241);
        e.CellStyle.SelectionForeColor = UITheme.TextDark;
        if (_productsGrid.Rows[e.RowIndex].DataBoundItem is ProductRankingDisplayRow row && row.Rank <= 3)
        {
            e.CellStyle.ForeColor = e.ColumnIndex == _productsGrid.Columns["Rank"]!.Index
                ? UITheme.PrimaryColor
                : UITheme.TextDark;
        }
    }

    private void ProductsGrid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || _productsGrid.Rows[e.RowIndex].DataBoundItem is not ProductRankingDisplayRow row)
        {
            return;
        }

        if (e.ColumnIndex == _productsGrid.Columns["Product"]!.Index)
        {
            e.ToolTipText = row.Product;
        }
        else if (e.ColumnIndex == _productsGrid.Columns["Quality"]!.Index)
        {
            e.ToolTipText = row.Quality switch
            {
                "Exacto" => "Información histórica cubierta por snapshots.",
                "Parcial" => "La cobertura histórica de este producto está incompleta.",
                "Sin snapshot" => "El movimiento es anterior a la captura histórica disponible.",
                _ => "La calidad histórica no aplica a este ranking."
            };
        }
    }

    private void ProductsGrid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != _productsGrid.Columns["Quality"]?.Index)
        {
            return;
        }

        var text = Convert.ToString(e.FormattedValue) ?? string.Empty;
        var graphics = e.Graphics;
        var cellStyle = e.CellStyle;
        var font = cellStyle?.Font ?? _productsGrid.Font;
        if (graphics is null || cellStyle is null || font is null)
        {
            return;
        }
        e.PaintBackground(e.CellBounds, true);
        if (text == "—")
        {
            TextRenderer.DrawText(
                graphics,
                text,
                font,
                e.CellBounds,
                UITheme.MutedTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            e.Handled = true;
            return;
        }

        var (background, foreground) = text switch
        {
            "Exacto" => (Color.FromArgb(229, 245, 226), Color.FromArgb(48, 111, 52)),
            "Parcial" => (Color.FromArgb(255, 240, 214), Color.FromArgb(143, 82, 16)),
            _ => (Color.FromArgb(237, 238, 242), Color.FromArgb(76, 78, 87))
        };
        var preferred = TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var badgeWidth = Math.Min(e.CellBounds.Width - 10, preferred.Width + 22);
        var badgeHeight = Math.Min(e.CellBounds.Height - 8, preferred.Height + 6);
        var badge = new Rectangle(
            e.CellBounds.Left + (e.CellBounds.Width - badgeWidth) / 2,
            e.CellBounds.Top + (e.CellBounds.Height - badgeHeight) / 2,
            badgeWidth,
            badgeHeight);
        using var path = CreateRoundedRectangle(badge, badgeHeight / 2);
        using var brush = new SolidBrush(background);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillPath(brush, path);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            badge,
            foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        e.Handled = true;
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void RenderCategories(CategoryAnalyticsReport report)
    {
        _categoriesGrid.DataSource = report.Categories.Select(row => new
        {
            Categoría = row.CategoryName,
            Unidades_netas = row.NetUnits,
            Ventas_netas = row.NetSalesCentavos / 100m,
            Devueltas = row.ReturnedUnits,
            Participación = $"{row.ParticipationPercentage:N2}%",
            Calidad = QualityText(row.SnapshotQuality)
        }).ToList();

        var subcategoryRows = (report.Subcategories ?? [])
            .Select(row => new
            {
                Categoría = row.CategoryName,
                Subcategoría = row.SubcategoryName,
                Unidades_netas = row.NetUnits,
                Ventas_netas = row.NetSalesCentavos / 100m,
                Devueltas = row.ReturnedUnits,
                Participación = $"{row.ParticipationPercentage:N2}%",
                Calidad = QualityText(row.SnapshotQuality)
            })
            .ToList();
        _subcategoriesGrid.DataSource = subcategoryRows;
        EnsureSubcategoryColumnReadable(_subcategoriesGrid);
        _subcategoriesGrid.Visible = subcategoryRows.Count > 0;
        _subcategoryEmptyState.Visible = subcategoryRows.Count == 0;
        if (_subcategoryEmptyState.Visible)
        {
            _subcategoryEmptyState.BringToFront();
        }
        else
        {
            _subcategoriesGrid.BringToFront();
        }

        _categoryPie.Series = report.Categories
            .Where(row => row.NetSalesCentavos > 0)
            .Select((row, index) => (ISeries)new PieSeries<long>
            {
                Name = row.CategoryName,
                Values = [row.NetSalesCentavos],
                InnerRadius = 48,
                Fill = new SolidColorPaint(ToSkColor(ChartColors[index % ChartColors.Length])),
                Stroke = new SolidColorPaint(SKColors.White, 2),
                ToolTipLabelFormatter = _ => $"${row.NetSalesCentavos / 100m:N2} ({row.ParticipationPercentage:N1}%)"
            }).ToArray();

        var topCategories = report.Categories.Where(row => row.NetSalesCentavos > 0).Take(5).ToList();
        var buckets = report.Trend
            .GroupBy(point => new { point.BucketStart, point.Label })
            .OrderBy(group => group.Key.BucketStart)
            .Select(group => group.Key)
            .ToArray();
        var trendSeries = topCategories.Select((category, index) =>
            (ISeries)new LineSeries<long>
            {
                Name = category.CategoryName,
                Values = buckets
                    .Select(bucket => report.Trend
                        .Where(point => point.BucketStart == bucket.BucketStart
                            && point.CategoryIdSnapshot == category.CategoryIdSnapshot
                            && point.CategoryName == category.CategoryName)
                        .Sum(point => point.NetSalesCentavos))
                    .ToArray(),
                GeometrySize = 5,
                LineSmoothness = 0.25,
                Stroke = new SolidColorPaint(ToSkColor(ChartColors[index % ChartColors.Length]), 2),
                Fill = null,
                YToolTipLabelFormatter = point => $"${point.Coordinate.PrimaryValue / 100:N2}"
            }).ToArray();
        var hasTrendData = trendSeries.Length > 0 && buckets.Length > 0 &&
            trendSeries.OfType<LineSeries<long>>().Any(series =>
                series.Values?.Any(value => value != 0) == true);
        _categoryTrend.Series = hasTrendData ? trendSeries : Array.Empty<ISeries>();
        var labels = buckets.Select(bucket => bucket.Label).ToArray();
        SetAxes(_categoryTrend, labels, money: true);
        _categoryTrendEmptyState.Visible = !hasTrendData;
        if (_categoryTrendEmptyState.Visible)
        {
            _categoryTrendEmptyState.BringToFront();
        }
        else
        {
            _categoryTrend.BringToFront();
        }
        _categoryCoverage.Text = FormatCoverage(report.Coverage);
    }

    private static void EnsureSubcategoryColumnReadable(DataGridView grid)
    {
        var column = grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(item => string.Equals(item.HeaderText, "Subcategoría", StringComparison.Ordinal));
        if (column is null)
        {
            return;
        }

        var preferred = TextRenderer.MeasureText(
            AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel,
            grid.Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 24;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        column.Width = Math.Max(column.Width, preferred);
    }

    private void RenderFinancial(FinancialAnalyticsReport report)
    {
        var coverageDetail = $"Cobertura histórica: {report.Finance.CostCoverage.Percentage:N1}%";
        _financeMetricCards["Ventas netas"].SetMoney(
            report.Finance.NetSales.Value,
            report.Finance.NetSales.Comparison);
        _financeMetricCards["Costo vendido"].SetMoney(
            report.Finance.CostOfGoodsSold.Value,
            report.Finance.CostOfGoodsSold.Comparison,
            coverageDetail);
        _financeMetricCards["Ganancia bruta"].SetMoney(
            report.Finance.GrossProfit.Value,
            report.Finance.GrossProfit.Comparison,
            coverageDetail);
        _financeMetricCards["Margen bruto"].SetPercentage(report.Finance.GrossMargin, coverageDetail);
        _financeMetricCards["Gastos operativos"].SetMoney(
            report.Finance.OperatingExpenses.Value,
            report.Finance.OperatingExpenses.Comparison);
        _financeMetricCards["Inversión inventario"].SetMoney(
            report.Finance.InventoryInvestment.Value,
            report.Finance.InventoryInvestment.Comparison,
            "Mercancía neta recibida; no pagos");

        _financeChart.Series =
        [
            Line("Ventas netas cubiertas", report.Finance.Trend.Select(point => point.CoveredNetSalesCentavos), UITheme.PrimaryColor, true),
            Line("Costo vendido", report.Finance.Trend.Select(point => point.CostOfGoodsSoldCentavos), UITheme.SecondaryColor, false),
            Line("Ganancia bruta", report.Finance.Trend.Select(point => point.GrossProfitCentavos), UITheme.SuccessColor, false)
        ];
        SetAxes(_financeChart, report.Finance.Trend.Select(point => point.Label).ToArray(), money: true);
        _expensesGrid.DataSource = report.Finance.Expenses.Select(expense => new
        {
            Fecha = expense.DateUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            Categoría = expense.Category,
            Descripción = expense.Description,
            Usuario = expense.User,
            Método = expense.PaymentMethod,
            Referencia = expense.Reference ?? "",
            Monto = expense.AmountCentavos / 100m
        }).ToList();
        _financeCoverage.Text = $"Cobertura exacta: {report.Finance.CostCoverage.Percentage:N1} %";
        _coverageToolTip.SetToolTip(
            _financeCoverage,
            report.Finance.CostCoverage.TotalRecords == 0
                ? "El período no contiene movimientos con costo histórico."
                : $"{report.Finance.CostCoverage.CompleteRecords} de {report.Finance.CostCoverage.TotalRecords} operaciones tienen costo histórico exacto. No se extrapola.");

        RenderCash(report.Cash);
        RenderPurchases(report);
        RenderObligations(
            report.Receivables,
            _receivableMetricCards,
            _receivablesAgingGrid,
            _receivablesGrid,
            _receivablesChart);
        RenderObligations(
            report.Payables,
            _payableMetricCards,
            _payablesAgingGrid,
            _payablesGrid,
            _payablesChart);
    }

    private void RenderCommercial(CommercialAnalyticsReport report)
    {
        _commercialMetricCards["Cotizaciones"].SetNumber(
            report.Quotations.Created.Value,
            report.Quotations.Created.Comparison);
        _commercialMetricCards["Importe cotizado"].SetMoney(
            report.Quotations.QuotedAmount.Value,
            report.Quotations.QuotedAmount.Comparison);
        _commercialMetricCards["Aceptadas"].SetText(
            report.Quotations.Accepted.ToString("N0"),
            $"Rechazadas: {report.Quotations.Rejected:N0} · Vencidas: {report.Quotations.Expired:N0}");
        _commercialMetricCards["Pedidos"].SetNumber(
            report.Orders.Created.Value,
            report.Orders.Created.Comparison);
        _commercialMetricCards["Anticipos"].SetText(
            Money(report.Orders.AdvancesCentavos),
            $"Pagos recibidos: {Money(report.Orders.PaymentsCentavos)}");
        _commercialMetricCards["Saldo pedidos"].SetText(
            Money(report.Orders.OutstandingBalanceCentavos),
            $"Reembolsos: {Money(report.Orders.RefundsCentavos)}");
        var orderToSale = report.Conversions.Single(metric => metric.Name == "Pedido → venta");
        _commercialMetricCards["Pedido → venta"].SetText(
            orderToSale.Percentage.HasValue ? $"{orderToSale.Percentage.Value:N1}%" : "Sin base",
            $"{orderToSale.Numerator:N0} de {orderToSale.Denominator:N0} pedidos");
        _commercialMetricCards["Atrasados"].SetText(
            report.Orders.Delayed.ToString("N0"),
            "Solo pedidos abiertos con fecha prometida válida");

        _commercialConversionsGrid.DataSource = report.Conversions.Select(metric => new
        {
            Conversión = metric.Name,
            Casos = metric.Available ? $"{metric.Numerator:N0} / {metric.Denominator:N0}" : "—",
            Tasa = metric.Available
                ? metric.Percentage.HasValue ? $"{metric.Percentage.Value:N2}%" : "Sin base"
                : "No disponible",
            Alcance = metric.Limitation ?? "Cohorte creada en el período"
        }).ToList();
        _commercialCustomersGrid.DataSource = report.TopCustomers.Select(row => new
        {
            Código = row.CustomerCode,
            Cliente = row.CustomerName,
            Cotizaciones = row.Quotations,
            Pedidos = row.Orders,
            Importe = (row.QuotedAmountCentavos + row.OrderAmountCentavos) / 100m
        }).ToList();

        var hasActivity = report.Quotations.Created.Value > 0 || report.Orders.Created.Value > 0;
        _commercialChart.Visible = hasActivity;
        _commercialEmptyState.Visible = !hasActivity;
        if (hasActivity)
        {
            _commercialChart.Series =
            [
                Line("Importe cotizado", report.Trend.Select(point => point.QuotedAmountCentavos), UITheme.PrimaryColor, true),
                Line("Importe de pedidos", report.Trend.Select(point => point.OrderAmountCentavos), UITheme.SecondaryColor, false)
            ];
            SetAxes(_commercialChart, report.Trend.Select(point => point.Label).ToArray(), money: true);
            _commercialChart.BringToFront();
        }
        else
        {
            _commercialChart.Series = [];
            _commercialEmptyState.BringToFront();
        }

        _commercialCoverage.Text = "Cobertura exacta: 100 %";
        _coverageToolTip.SetToolTip(_commercialCoverage, report.CoverageNote);
    }

    private void RenderCash(CashAnalyticsSummary cash)
    {
        _cashMetricCards["Efectivo esperado"].SetText(Money(cash.ExpectedCashCentavos), $"Apertura: {Money(cash.OpeningCashCentavos)}");
        _cashMetricCards["Ventas efectivo"].SetText(Money(cash.CashSalesCentavos), "Ventas y cobros por corrección");
        _cashMetricCards["Cobros clientes"].SetText(Money(cash.CashCustomerPaymentsCentavos), "Abonos CxC en efectivo");
        _cashMetricCards["Otras entradas"].SetText(Money(cash.OtherCashInputsCentavos), "Anticipos y pagos de pedidos");
        _cashMetricCards["Aportes para caja"].SetText(Money(cash.CashContributionsCentavos), "Aportes registrados; no son ventas ni ingresos comerciales");
        _cashMetricCards["Reembolsos"].SetText(Money(cash.CashRefundsCentavos), "Solo salidas de efectivo");
        _cashMetricCards["Pagos proveedores"].SetText(Money(cash.SupplierCashPaymentsCentavos), "Pagos iniciales y abonos");
        _cashMetricCards["Gastos"].SetText(Money(cash.ExpensesCentavos), "Efectivo que salió del cajón");
        _cashMetricCards["Gastos fuera de caja"].SetText(Money(cash.ExpensesOutsideCashCentavos), "Gastos del negocio pagados por otra fuente");
        _cashMetricCards["Pagos empleados"].SetText(Money(cash.EmployeePaymentsCentavos), "Pagos que redujeron efectivo físico");
        _cashMetricCards["Pagos empleados fuera de caja"].SetText(Money(cash.EmployeePaymentsOutsideCashCentavos), "Pagos con origen externo conocido");
        _cashMetricCards["Retiros"].SetText(Money(cash.WithdrawalsCentavos), "No tratados como gasto");
        _cashMetricCards["Movimiento neto"].SetText(Money(cash.NetMovementCentavos), "Entradas menos salidas; sin apertura");
        _cashMetricCards["Diferencia cierre"].SetText(
            cash.ClosingDifferenceCentavos.HasValue ? Money(cash.ClosingDifferenceCentavos.Value) : "Pendiente",
            $"Cajas abiertas: {cash.OpenCashRegisters}; cerradas: {cash.ClosedCashRegisters}");
    }

    private void RenderPurchases(FinancialAnalyticsReport report)
    {
        var purchases = report.Purchases;
        _purchaseMetricCards["Compras comprometidas"].SetMoney(
            purchases.CommittedPurchases.Value,
            purchases.CommittedPurchases.Comparison,
            "Total acordado en compras");
        _purchaseMetricCards["Mercancía recibida"].SetMoney(
            purchases.ReceivedMerchandise.Value,
            purchases.ReceivedMerchandise.Comparison,
            "Valor neto con snapshot exacto");
        _purchaseMetricCards["Pagos proveedores"].SetMoney(
            purchases.SupplierPayments.Value,
            purchases.SupplierPayments.Comparison,
            "Flujo pagado; no nueva inversión");
        _purchaseMetricCards["Inversión recibida"].SetMoney(
            purchases.ReceivedMerchandise.Value,
            purchases.ReceivedMerchandise.Comparison,
            "Definición oficial de inversión");
        var valuation = purchases.InventoryValuation;
        _purchaseMetricCards["Inversión actual"].SetText(
            valuation is null
                ? Money(purchases.CurrentInventoryValueCentavos)
                : valuation.InvestmentCurrentCentavos is long investment
                    ? Money(investment)
                    : "No calculable",
            "Stock físico actual × costo efectivo vigente");
        _purchaseMetricCards["Venta potencial"].SetText(
            valuation?.PotentialSalesValueCentavos is long potential
                ? Money(potential)
                : "No calculable",
            "Stock físico actual × precio de venta vigente");
        _purchaseMetricCards["Venta disponible"].SetText(
            valuation?.AvailableSalesValueCentavos is long availableValue
                ? Money(availableValue)
                : "No calculable",
            "Disponible (físico − reservado) × precio vigente");
        _purchaseMetricCards["Ganancia bruta potencial"].SetText(
            valuation?.PotentialGrossProfitCentavos is long gain
                ? Money(gain)
                : "No calculable",
            "Venta potencial − inversión actual; no es ganancia realizada ni neta");
        _purchaseMetricCards["Margen bruto potencial"].SetText(
            valuation?.PotentialGrossMarginPercentage is decimal margin
                ? $"{margin:N2}%"
                : "No calculable",
            "Ganancia potencial ÷ venta potencial; global, sin promediar filas");
        var hasReceivedMerchandise = report.Finance.Trend.Any(point => point.ReceivedInventoryCentavos != 0);
        _purchaseChart.Visible = hasReceivedMerchandise;
        _purchaseEmptyState.Visible = !hasReceivedMerchandise;
        if (hasReceivedMerchandise)
        {
            _purchaseChart.Series =
            [
                Line("Mercancía neta recibida", report.Finance.Trend.Select(point => point.ReceivedInventoryCentavos), UITheme.PrimaryColor, true)
            ];
            SetAxes(_purchaseChart, report.Finance.Trend.Select(point => point.Label).ToArray(), money: true);
            _purchaseChart.BringToFront();
        }
        else
        {
            _purchaseChart.Series = [];
            _purchaseEmptyState.BringToFront();
        }

        _purchaseCoverage.Text = $"Cobertura exacta: {purchases.ReceptionSnapshotCoverage.Percentage:N1} %";
        _coverageToolTip.SetToolTip(
            _purchaseCoverage,
            purchases.ReceptionSnapshotCoverage.TotalRecords == 0
                ? "No hay recepciones en el período; la cobertura permanece visible."
                : $"{purchases.ReceptionSnapshotCoverage.CompleteRecords} de {purchases.ReceptionSnapshotCoverage.TotalRecords} recepciones tienen snapshot neto exacto. Los registros anteriores no se reconstruyen.");
    }

    private static void RenderObligations(
        ObligationAnalytics analytics,
        IReadOnlyDictionary<string, AnalyticsMetricCardControl> cards,
        DataGridView agingGrid,
        DataGridView rankingGrid,
        CartesianChart chart)
    {
        cards["Saldo pendiente"].SetText(Money(analytics.OutstandingBalanceCentavos), "Saldo reconstruido al cierre del período");
        cards["Creadas en período"].SetText(Money(analytics.CreatedCentavos), "Obligaciones originadas");
        cards["Pagos del período"].SetText(Money(analytics.PaymentsCentavos), "Cobros o pagos registrados");
        cards["Cuentas pagadas"].SetText(analytics.PaidAccounts.ToString("N0"), "Liquidadas en el período");
        cards["Cuentas vencidas"].SetText(analytics.OverdueAccounts.ToString("N0"), "Con saldo y fecha vencida");

        agingGrid.DataSource = analytics.Aging.Select(row => new
        {
            Antigüedad = row.Name,
            Cuentas = row.Accounts,
            Saldo = row.BalanceCentavos / 100m
        }).ToList();
        rankingGrid.DataSource = analytics.HighestBalances.Select(row => new
        {
            Código = row.Code,
            Nombre = row.Name,
            Saldo = row.BalanceCentavos / 100m
        }).ToList();
        chart.Series =
        [
            Line("Saldo", analytics.BalanceEvolution.Select(point => point.BalanceCentavos), UITheme.PrimaryColor, true),
            Line("Pagos", analytics.BalanceEvolution.Select(point => point.PaymentsCentavos), UITheme.SuccessColor, false)
        ];
        SetAxes(chart, analytics.BalanceEvolution.Select(point => point.Label).ToArray(), money: true);
    }

    private static string Money(long centavos) => $"${centavos / 100m:N2}";

    private static string HumanizeLastSale(int? daysSinceLastSale) => daysSinceLastSale switch
    {
        null => "Sin ventas",
        0 => "Hoy",
        1 => "Ayer",
        var days => $"Hace {days:N0} días"
    };

    private static string UnitWord(int units) => Math.Abs(units) == 1 ? "unidad" : "unidades";

    private static Control WrapSurface(string title, Control content, Label? badge = null)
    {
        var panel = new DashboardSurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(5) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12, 5, 10, 4),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        header.Controls.Add(label, 0, 0);
        if (badge is not null)
        {
            badge.AutoSize = true;
            badge.Anchor = AnchorStyles.Right;
            badge.Padding = new Padding(9, 4, 9, 4);
            badge.Margin = new Padding(8, 0, 0, 0);
            badge.BackColor = UITheme.DashboardAccentSoftColor;
            badge.ForeColor = UITheme.PrimaryColor;
            badge.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            header.Controls.Add(badge, 1, 0);
        }

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(6);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(content, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowHeadersVisible = false;
        UITheme.StyleDataGridView(grid);
    }

    private static void ConfigureCartesianChart(CartesianChart chart)
    {
        chart.BackColor = UITheme.SurfaceColor;
        chart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Top;
        chart.ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None;
        chart.AnimationsSpeed = TimeSpan.FromMilliseconds(250);
    }

    private static void ConfigurePieChart(PieChart chart)
    {
        chart.BackColor = UITheme.SurfaceColor;
        chart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Right;
        chart.AnimationsSpeed = TimeSpan.FromMilliseconds(250);
    }

    private static LineSeries<long> Line(string name, IEnumerable<long> values, Color color, bool fill) => new()
    {
        Name = name,
        Values = values.ToArray(),
        GeometrySize = 5,
        LineSmoothness = 0.25,
        Stroke = new SolidColorPaint(ToSkColor(color), 2),
        GeometryFill = new SolidColorPaint(SKColors.White),
        GeometryStroke = new SolidColorPaint(ToSkColor(color), 2),
        Fill = fill ? new SolidColorPaint(new SKColor(color.R, color.G, color.B, 28)) : null,
        YToolTipLabelFormatter = point => $"${point.Coordinate.PrimaryValue / 100:N2}"
    };

    private static void SetAxes(CartesianChart chart, string[] labels, bool money)
    {
        chart.XAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsRotation = labels.Length > 12 ? 28 : 0,
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(ToSkColor(UITheme.MutedTextColor)),
                SeparatorsPaint = null
            }
        ];
        chart.YAxes =
        [
            new Axis
            {
                TextSize = 10,
                Labeler = money ? value => $"${value / 100:N0}" : value => value.ToString("N0"),
                LabelsPaint = new SolidColorPaint(ToSkColor(UITheme.MutedTextColor)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(235, 229, 233), 1)
            }
        ];
    }

    private static string FormatCoverage(HistoricalCoverageSummary coverage)
    {
        var from = coverage.ReliableSnapshotsFromUtc.HasValue
            ? $"Captura activa desde {coverage.ReliableSnapshotsFromUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}. "
            : "Fecha de captura no disponible. ";
        return from +
            $"Cobertura: producto {coverage.ProductSnapshots.Percentage:N1}%, categoría {coverage.CategorySnapshots.Percentage:N1}%, " +
            $"costo {coverage.CostSnapshots.Percentage:N1}%, descuento distribuido {coverage.GeneralDiscountSnapshots.Percentage:N1}%.";
    }

    private static string QualityText(HistoricalDataQuality quality) => quality switch
    {
        HistoricalDataQuality.Exact => "Exacto",
        HistoricalDataQuality.Partial => "Parcial",
        _ => "Sin snapshot"
    };

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

    private sealed record AnalyticsCategoryChoice(int? Id, string Text);
    private sealed record AnalyticsSubcategoryChoice(
        int? Id,
        string Text,
        int? CategoryId,
        AnalyticsSubcategoryFilter Filter = AnalyticsSubcategoryFilter.All);
    private sealed record ProductLimitChoice(string Text, int? Limit);

    private sealed record ProductRankingDisplayRow(
        int ProductId,
        int Rank,
        string Code,
        string Product,
        int Sold,
        long NetSalesCentavos,
        int Returns,
        int Stock,
        string LastSale,
        string Quality)
    {
        public string NetSales => Money(NetSalesCentavos);
    }

}
