using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class QuotationsForm : Form
{
    private const int PageSize = 100;
    private readonly IQuotationQueryService _query;
    private readonly IQuotationService _service;
    private readonly IQuotationDocumentService _documentService;
    private readonly ICustomerQueryService _customerQuery;
    private readonly IProductService _productService;
    private readonly IEmployeePermissionService _permissions;
    private readonly ICustomOrderService? _customOrderService;
    private readonly IInstallationContext? _installationContext;
    private readonly ToolTip _pageNavigationToolTip = new();
    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly Button _btnClearSearch = new();
    private readonly ComboBox _cmbState = new();
    private readonly CheckBox _chkDates = new();
    private readonly DateTimePicker _dtpFrom = new();
    private readonly DateTimePicker _dtpTo = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblDrafts = new();
    private readonly Label _lblIssued = new();
    private readonly Label _lblAccepted = new();
    private readonly Label _lblExpired = new();
    private readonly Label _lblPage = new();
    private Button _btnNew = new();
    private Button _btnEdit = new();
    private Button _btnIssue = new();
    private Button _btnState = new();
    private Button _btnDuplicate = new();
    private Button _btnPdf = new();
    private Button _btnConvert = new();
    private int _page = 1;
    private LiveSearchController? _liveSearch;
    private bool _suppressFilterRefresh;

    public QuotationsForm(
        IQuotationQueryService query,
        IQuotationService service,
        IQuotationDocumentService documentService,
        ICustomerQueryService customerQuery,
        IProductService productService,
        IEmployeePermissionService permissions,
        ICustomOrderService? customOrderService = null,
        IInstallationContext? installationContext = null)
    {
        _query = query;
        _service = service;
        _documentService = documentService;
        _customerQuery = customerQuery;
        _productService = productService;
        _permissions = permissions;
        _customOrderService = customOrderService;
        _installationContext = installationContext;
        BuildUi();
        _liveSearch = new LiveSearchController(
            _txtSearch,
            _btnClearSearch,
            SearchFromFirstPageAsync,
            ShowSearchError);
        Load += QuotationsForm_Load;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
            _pageNavigationToolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void QuotationsForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACIONES_ACCEDER);
            if (_installationContext is not { AllowsOperationalWrites: false })
            {
                await _service.UpdateExpiredAsync();
            }
            await ApplyPermissionsAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Cotizaciones";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 680);
        UITheme.StyleForm(this);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStats(), 0, 1);
        root.Controls.Add(BuildFilters(), 0, 2);
        root.Controls.Add(BuildGrid(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 10, 18, 10),
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "Cotizaciones",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        UITheme.StyleLabelTitle(title);
        var subtitle = new Label
        {
            Text = "Borradores y propuestas comerciales sin afectar inventario, caja ni cuentas.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildStats()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 10, 0, 10) };
        for (var i = 0; i < 4; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.Controls.Add(StatCard("Borradores", _lblDrafts), 0, 0);
        panel.Controls.Add(StatCard("Emitidas", _lblIssued), 1, 0);
        panel.Controls.Add(StatCard("Aceptadas", _lblAccepted), 2, 0);
        panel.Controls.Add(StatCard("Vencidas", _lblExpired), 3, 0);
        return panel;
    }

    private Control BuildFilters()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        AddLabel(layout, "Buscar por codigo, cliente, destinatario o producto", 0);
        AddLabel(layout, "Estado", 1);
        AddLabel(layout, "Desde", 2);
        AddLabel(layout, "Hasta", 3);
        _txtSearch.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_txtSearch);
        _txtSearch.PlaceholderText = "Número de cotización o cliente";
        ConfigureClearSearchButton(_btnClearSearch);
        _cmbState.Dock = DockStyle.Fill;
        _cmbState.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbState.DisplayMember = nameof(StateOption.Name);
        _cmbState.ValueMember = nameof(StateOption.Value);
        _cmbState.DataSource = new[] { new StateOption(null, "TODOS") }
            .Concat(Enum.GetValues<EstadoCotizacion>().Where(s => s != EstadoCotizacion.CONVERTIDA).Select(s => new StateOption(s, s.ToString())))
            .ToList();
        _cmbState.SelectedValueChanged += FilterChanged;
        ConfigureDate(_dtpFrom, DateTime.Today.AddMonths(-1));
        ConfigureDate(_dtpTo, DateTime.Today);
        _dtpFrom.ValueChanged += FilterChanged;
        _dtpTo.ValueChanged += FilterChanged;
        _chkDates.Text = "Usar fechas";
        _chkDates.Dock = DockStyle.Fill;
        _chkDates.TextAlign = ContentAlignment.MiddleLeft;
        _chkDates.CheckedChanged += FilterChanged;
        var clear = CreateButton("Limpiar", UITheme.StyleSecondaryButton, async (_, _) => await ClearAsync());
        layout.Controls.Add(BuildSearchInput(), 0, 1);
        layout.Controls.Add(_cmbState, 1, 1);
        layout.Controls.Add(_dtpFrom, 2, 1);
        layout.Controls.Add(_dtpTo, 3, 1);
        layout.Controls.Add(_chkDates, 4, 1);
        layout.Controls.Add(clear, 5, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildGrid()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(10) };
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.CellDoubleClick += async (_, _) => await ViewDetailAsync();
        _grid.SelectionChanged += (_, _) => UpdateActions();
        AddColumn("Codigo", "Codigo", 120, 12);
        AddColumn("Fecha", "Fecha", 105, 10);
        AddColumn("Vencimiento", "Vencimiento", 105, 10);
        AddColumn("Destinatario", "Destinatario", 220, 24);
        AddColumn("Estado", "Estado", 110, 11);
        AddColumn("Lineas", "Lineas", 70, 6);
        AddColumn("Total", "Total", 110, 10);
        AddColumn("Usuario", "Usuario", 110, 10);
        _grid.DataSource = _binding;
        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildActions()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 2, 0, 0),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        var pagePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        pagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        pagePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lblPage.Dock = DockStyle.Fill;
        _lblPage.TextAlign = ContentAlignment.MiddleLeft;
        _lblPage.Font = UITheme.NormalBoldFont;
        _lblPage.ForeColor = UITheme.MutedTextColor;

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        var previous = CreateButton("←", UITheme.StyleSecondaryButton, async (_, _) => await ChangePageAsync(-1), 48);
        previous.Name = "PreviousQuotationPageButton";
        previous.AccessibleName = "Cotizaciones anteriores";
        _pageNavigationToolTip.SetToolTip(previous, "Página anterior");
        var next = CreateButton("→", UITheme.StyleSecondaryButton, async (_, _) => await ChangePageAsync(1), 48);
        next.Name = "NextQuotationPageButton";
        next.AccessibleName = "Cotizaciones siguientes";
        _pageNavigationToolTip.SetToolTip(next, "Página siguiente");
        _btnNew = CreateButton("Nueva cotización", UITheme.StylePrimaryButton, async (_, _) => await NewAsync(), 190);
        navigation.Controls.AddRange([previous, next, _btnNew]);
        pagePanel.Controls.Add(_lblPage, 0, 0);
        pagePanel.Controls.Add(navigation, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 2)
        };
        buttons.Controls.Add(CreateButton("Cerrar", UITheme.StyleSecondaryButton, (_, _) => Close()));
        _btnPdf = CreateButton("PDF", UITheme.StyleSecondaryButton, async (_, _) => await GeneratePdfAsync(), 78);
        _btnDuplicate = CreateButton("Duplicar", UITheme.StyleSecondaryButton, async (_, _) => await DuplicateAsync());
        _btnConvert = CreateButton("Convertir en pedido", UITheme.StyleSuccessButton, async (_, _) => await ConvertAsync(), 220);
        _btnState = CreateButton("Cambiar estado", UITheme.StyleSecondaryButton, async (_, _) => await ChangeStateAsync(), 145);
        _btnIssue = CreateButton("Emitir", UITheme.StyleSuccessButton, async (_, _) => await IssueAsync());
        _btnEdit = CreateButton("Editar", UITheme.StyleSecondaryButton, async (_, _) => await EditAsync());
        var detail = CreateButton("Ver detalle", UITheme.StyleSecondaryButton, async (_, _) => await ViewDetailAsync(), 130);
        buttons.Controls.AddRange([_btnPdf, _btnDuplicate, _btnConvert, _btnState, _btnIssue, _btnEdit, detail]);
        layout.Controls.Add(pagePanel, 0, 0);
        layout.Controls.Add(buttons, 1, 0);
        return layout;
    }

    private async Task ApplyPermissionsAsync()
    {
        _btnNew.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.COTIZACION_CREAR);
        _btnEdit.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.COTIZACION_EDITAR);
        _btnIssue.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.COTIZACION_EMITIR);
        _btnState.Visible = await _permissions.HasAnyPermissionAsync([
            PermisoEmpleado.COTIZACION_EMITIR,
            PermisoEmpleado.COTIZACION_CANCELAR]);
        _btnDuplicate.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.COTIZACION_CREAR);
        _btnConvert.Visible = _customOrderService is not null &&
            await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CREAR);

        if (_installationContext is { AllowsOperationalWrites: false })
        {
            _btnNew.Visible = false;
            _btnEdit.Visible = false;
            _btnIssue.Visible = false;
            _btnState.Visible = false;
            _btnDuplicate.Visible = false;
            _btnConvert.Visible = false;
            Text = "Cotizaciones - Modo Consulta";
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = CurrentFilter();
            var rows = await _query.SearchAsync(filter, cancellationToken);
            var stats = await _query.GetStatsAsync(filter with { Page = 1, PageSize = 250 }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _binding.DataSource = rows.Select(Row.FromSummary).ToList();
            _lblDrafts.Text = stats.Borradores.ToString();
            _lblIssued.Text = stats.Emitidas.ToString();
            _lblAccepted.Text = stats.Aceptadas.ToString();
            _lblExpired.Text = stats.Vencidas.ToString();
            _lblPage.Text = $"Página {_page} - {rows.Count} cotización(es) mostrada(s).";
            if (rows.Count == 0 && _page > 1)
            {
                _page--;
                await RefreshAsync(cancellationToken);
                return;
            }
            UpdateActions();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private QuotationFilter CurrentFilter() => new(
        _txtSearch.Text,
        (_cmbState.SelectedItem as StateOption)?.Value,
        _chkDates.Checked,
        _dtpFrom.Value.Date,
        _dtpTo.Value.Date,
        _page,
        PageSize);
    private Row? CurrentRow() => _binding.Current as Row;
    private void UpdateActions()
    {
        var row = CurrentRow();
        var selected = row is not null;
        _btnEdit.Enabled = selected && row!.EstadoValue == EstadoCotizacion.BORRADOR;
        _btnIssue.Enabled = selected && row!.EstadoValue == EstadoCotizacion.BORRADOR;
        _btnState.Enabled = selected && row!.EstadoValue is EstadoCotizacion.BORRADOR or EstadoCotizacion.EMITIDA;
        _btnDuplicate.Enabled = selected;
        _btnConvert.Enabled = selected && row!.EstadoValue == EstadoCotizacion.ACEPTADA;
        _btnPdf.Enabled = selected;
    }

    private async Task NewAsync()
    {
        using var form = new QuotationEditForm(_service, _customerQuery, _productService);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
    }
    private async Task EditAsync()
    {
        if (CurrentRow() is not { } row) return;
        var detail = await _query.GetByIdAsync(row.Id);
        if (detail is null) return;
        using var form = new QuotationEditForm(_service, _customerQuery, _productService, detail);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
    }
    private async Task ViewDetailAsync()
    {
        if (CurrentRow() is not { } row) return;
        var detail = await _query.GetByIdAsync(row.Id);
        if (detail is null) return;
        using var form = new QuotationDetailForm(detail, _service, _documentService, _permissions);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
    }
    private async Task IssueAsync() => await RunAsync(async row => await _service.EmitAsync(row.Id), "Cotizacion emitida.");
    private async Task DuplicateAsync() => await RunAsync(async row => await _service.DuplicateAsync(row.Id), "Cotizacion duplicada.");
    private async Task ConvertAsync()
    {
        if (_customOrderService is null ||
            CurrentRow() is not { EstadoValue: EstadoCotizacion.ACEPTADA } row) return;
        using var dialog = new QuotationConversionDateForm(row.Codigo);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var order = await _customOrderService.ConvertQuotationAsync(row.Id, dialog.PromiseDate);
            MessageBox.Show(
                $"Cotizacion convertida correctamente en {order.CodigoPedido}.",
                "Convertir en pedido",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Convertir en pedido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private async Task ChangeStateAsync()
    {
        if (CurrentRow() is not { } row) return;
        var canIssue = await _permissions.HasPermissionAsync(PermisoEmpleado.COTIZACION_EMITIR);
        var canCancel = await _permissions.HasPermissionAsync(PermisoEmpleado.COTIZACION_CANCELAR);
        using var dialog = new QuotationStateDialog(row.EstadoValue, canIssue, canCancel);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunAsync(
            async _ => await _service.ChangeStateAsync(new QuotationStateChangeRequest(row.Id, dialog.SelectedState, dialog.Reason)),
            $"Estado actualizado a {dialog.SelectedState}.");
    }
    private async Task GeneratePdfAsync()
    {
        if (CurrentRow() is not { } row) return;
        try
        {
            var path = await _documentService.GeneratePdfAsync(row.Id);
            using var actions = new GeneratedPdfActionsForm(path);
            actions.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Generar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private async Task RunAsync(Func<Row, Task> action, string success)
    {
        if (CurrentRow() is not { } row) return;
        try
        {
            await action(row);
            MessageBox.Show(success, "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private async Task ClearAsync()
    {
        _suppressFilterRefresh = true;
        try
        {
            _txtSearch.Clear();
            _cmbState.SelectedIndex = 0;
            _chkDates.Checked = false;
            _dtpFrom.Value = DateTime.Today.AddMonths(-1);
            _dtpTo.Value = DateTime.Today;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        _page = 1;
        if (_liveSearch is not null)
        {
            await _liveSearch.SearchNowAsync();
        }
        else
        {
            await RefreshAsync();
        }
    }

    private async Task ChangePageAsync(int delta)
    {
        var nextPage = Math.Max(1, _page + delta);
        if (nextPage == _page)
        {
            return;
        }

        _page = nextPage;
        if (_liveSearch is not null)
        {
            await _liveSearch.ExecuteNowAsync(RefreshAsync);
        }
        else
        {
            await RefreshAsync();
        }
    }

    private async Task SearchFromFirstPageAsync(CancellationToken cancellationToken)
    {
        _page = 1;
        await RefreshAsync(cancellationToken);
    }

    private async void FilterChanged(object? sender, EventArgs e)
    {
        if (!_suppressFilterRefresh && _liveSearch is not null)
        {
            await _liveSearch.SearchNowAsync();
        }
    }

    private Control BuildSearchInput()
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        host.Controls.Add(_txtSearch, 0, 0);
        host.Controls.Add(_btnClearSearch, 1, 0);
        return host;
    }

    private static void ConfigureClearSearchButton(Button button)
    {
        button.Text = "X";
        button.AccessibleName = "Limpiar búsqueda";
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 0, 0, 0);
        UITheme.StyleSecondaryButton(button);
    }

    private void ShowSearchError(Exception ex)
    {
        if (!IsDisposed)
        {
            _lblPage.ForeColor = UITheme.DangerColor;
            _lblPage.Text = $"No se pudo actualizar la búsqueda: {ex.Message}";
        }
    }

    private static Control StatCard(string title, Label value)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, RowCount = 2, Padding = new Padding(16, 10, 16, 12), Margin = new Padding(0, 0, 12, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor }, 0, 0);
        value.Text = "0";
        value.Dock = DockStyle.Fill;
        value.Font = UITheme.HeaderFont;
        value.ForeColor = UITheme.PrimaryColor;
        panel.Controls.Add(value, 0, 1);
        return panel;
    }
    private static void AddLabel(TableLayoutPanel layout, string text, int column) =>
        layout.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, TextAlign = ContentAlignment.MiddleLeft }, column, 0);
    private static void ConfigureDate(DateTimePicker picker, DateTime value)
    {
        picker.Dock = DockStyle.Fill;
        picker.Format = DateTimePickerFormat.Short;
        picker.Value = value;
    }
    private static Button CreateButton(string text, Action<Button> style, EventHandler click, int width = 115)
    {
        var button = new Button { Text = text, Size = new Size(width, 40), Margin = new Padding(8, 0, 0, 0) };
        style(button);
        button.Click += click;
        return button;
    }
    private void AddColumn(string property, string title, int min, float weight) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, MinimumWidth = min, FillWeight = weight });
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private sealed record StateOption(EstadoCotizacion? Value, string Name);
    private sealed record Row(int Id, string Codigo, string Fecha, string Vencimiento, string Destinatario, EstadoCotizacion EstadoValue, string Estado, int Lineas, string Total, string Usuario)
    {
        public static Row FromSummary(QuotationSummary q) => new(
            q.CotizacionId, q.CodigoCotizacion, q.Fecha.ToString("yyyy-MM-dd"),
            q.FechaVencimiento.ToString("yyyy-MM-dd"), q.Destinatario, q.Estado,
            q.Estado.ToString(), q.Lineas, Money(q.TotalCentavos), q.UsuarioCreador);
    }

    internal sealed class QuotationConversionDateForm : Form
    {
        private readonly DateTimePicker _date = new()
        {
            Dock = DockStyle.Fill,
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Today.AddDays(15)
        };
        public DateTime PromiseDate => _date.Value.Date;

        public QuotationConversionDateForm(string code)
        {
            Text = "Convertir en pedido";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 260);
            MinimumSize = new Size(520, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            UITheme.StyleForm(this);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22),
                ColumnCount = 2,
                RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var title = new Label { Text = $"Convertir {code} en pedido", AutoSize = true };
            UITheme.StyleLabelHeader(title);
            layout.Controls.Add(title, 0, 0);
            layout.SetColumnSpan(title, 2);
            layout.Controls.Add(new Label { Text = "Fecha prometida de entrega", AutoSize = true, Margin = new Padding(0, 14, 0, 4) }, 0, 1);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, 1)!, 2);
            layout.Controls.Add(_date, 0, 2);
            layout.SetColumnSpan(_date, 2);
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 2, RowCount = 1, Height = 48, Margin = new Padding(0, 14, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            var cancel = new Button { Text = "Cancelar", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 4, 6, 4), MinimumSize = new Size(120, 38) };
            var accept = new Button { Text = "Convertir en pedido", Dock = DockStyle.Fill, Margin = new Padding(6, 4, 0, 4), MinimumSize = new Size(175, 38) };
            UITheme.StyleSecondaryButton(cancel);
            UITheme.StyleSuccessButton(accept);
            accept.Click += (_, _) =>
            {
                if (_date.Value.Date < DateTime.Today)
                {
                    MessageBox.Show("La fecha prometida no puede ser anterior a hoy.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            footer.Controls.Add(cancel, 0, 0);
            footer.Controls.Add(accept, 1, 0);
            layout.Controls.Add(footer, 0, 3);
            layout.SetColumnSpan(footer, 2);
            Controls.Add(layout);
            AcceptButton = accept;
            CancelButton = cancel;
        }
    }

    private sealed class QuotationStateDialog : Form
    {
        private readonly ComboBox _states = new();
        private readonly TextBox _reason = new();
        public EstadoCotizacion SelectedState => (EstadoCotizacion)_states.SelectedItem!;
        public string? Reason => string.IsNullOrWhiteSpace(_reason.Text) ? null : _reason.Text.Trim();
        public QuotationStateDialog(EstadoCotizacion current, bool canIssue, bool canCancel)
        {
            Text = "Cambiar estado";
            ClientSize = new Size(480, 210);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            UITheme.StyleForm(this);
            var commercialStates = current == EstadoCotizacion.BORRADOR
                ? new[] { EstadoCotizacion.EMITIDA, EstadoCotizacion.CANCELADA }
                : new[] { EstadoCotizacion.ACEPTADA, EstadoCotizacion.RECHAZADA, EstadoCotizacion.VENCIDA, EstadoCotizacion.CANCELADA };
            var allowed = commercialStates
                .Where(state => state == EstadoCotizacion.CANCELADA ? canCancel : canIssue)
                .ToArray();
            if (allowed.Length == 0)
            {
                throw new InvalidOperationException("No tiene permisos para cambiar el estado de esta cotizacion.");
            }
            _states.DataSource = allowed;
            _states.DropDownStyle = ComboBoxStyle.DropDownList;
            _states.SetBounds(20, 45, 440, 32);
            _reason.SetBounds(20, 110, 440, 32);
            UITheme.StyleTextBox(_reason);
            var ok = CreateButton("Aceptar", UITheme.StylePrimaryButton, (_, _) =>
            {
                if (SelectedState == EstadoCotizacion.CANCELADA && string.IsNullOrWhiteSpace(_reason.Text))
                {
                    MessageBox.Show("El motivo es obligatorio para cancelar.", "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
            });
            ok.Location = new Point(220, 158);
            var cancel = CreateButton("Cancelar", UITheme.StyleSecondaryButton, (_, _) => DialogResult = DialogResult.Cancel);
            cancel.Location = new Point(345, 158);
            Controls.AddRange([
                new Label { Text = "Nuevo estado", Location = new Point(20, 20), AutoSize = true, Font = UITheme.NormalBoldFont },
                _states,
                new Label { Text = "Motivo (obligatorio al cancelar)", Location = new Point(20, 85), AutoSize = true, Font = UITheme.NormalBoldFont },
                _reason, ok, cancel]);
        }
    }
}
