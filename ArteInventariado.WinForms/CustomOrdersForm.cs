using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class CustomOrdersForm : Form
{
    private readonly ICustomOrderQueryService _query;
    private readonly ICustomOrderService _service;
    private readonly ICustomOrderDocumentService _documents;
    private readonly ICustomerQueryService _customers;
    private readonly IProductService _products;
    private readonly IPosCatalogService _catalog;
    private readonly IEmployeePermissionService _permissions;
    private readonly ICustomOrderFinanceService _finance;
    private readonly ICustomOrderFinanceQueryService _financeQuery;
    private readonly ICustomOrderFinanceDocumentService _financeDocuments;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill };
    private readonly Button _clearSearch = new();
    private readonly ComboBox _state = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _priority = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _from = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _to = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };
    private readonly CheckBox _useDates = new() { Text = "Usar fechas", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _overdueOnly = new() { Text = "Solo atrasados", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly DataGridView _grid = new();
    private readonly Label[] _stats = Enumerable.Range(0, 5).Select(_ => new Label()).ToArray();
    private readonly Button _new = new() { Text = "Nuevo pedido" };
    private readonly Button _view = new() { Text = "Ver detalle" };
    private readonly Button _edit = new() { Text = "Editar" };
    private readonly Button _confirm = new() { Text = "Confirmar" };
    private readonly Button _change = new() { Text = "Cambiar estado" };
    private readonly Button _duplicate = new() { Text = "Duplicar" };
    private readonly Button _pdf = new() { Text = "PDF" };
    private readonly Button _payment = new() { Text = "Registrar pago" };
    private readonly Button _paymentHistory = new() { Text = "Historial pagos" };
    private readonly Button _convert = new() { Text = "Convertir en venta" };
    private List<GridRow> _rows = [];
    private LiveSearchController? _liveSearch;
    private bool _suppressFilterRefresh;

    public CustomOrdersForm(
        ICustomOrderQueryService query,
        ICustomOrderService service,
        ICustomOrderDocumentService documents,
        ICustomerQueryService customers,
        IProductService products,
        IPosCatalogService catalog,
        IEmployeePermissionService permissions,
        ICustomOrderFinanceService finance,
        ICustomOrderFinanceQueryService financeQuery,
        ICustomOrderFinanceDocumentService financeDocuments)
    {
        _query = query;
        _service = service;
        _documents = documents;
        _customers = customers;
        _products = products;
        _catalog = catalog;
        _permissions = permissions;
        _finance = finance;
        _financeQuery = financeQuery;
        _financeDocuments = financeDocuments;
        BuildUi();
        _liveSearch = new LiveSearchController(
            _search,
            _clearSearch,
            SearchFromFirstPageAsync,
            ShowSearchError);
        Load += FormLoad;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Text = "Pedidos personalizados";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 680);
        UITheme.StyleForm(this);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor };
        var title = new Label { Text = "Pedidos personalizados", AutoSize = true, Location = new Point(18, 8) };
        var subtitle = new Label { Text = "Controle solicitudes especiales desde su confirmacion hasta la entrega.", AutoSize = true, Location = new Point(20, 52), ForeColor = UITheme.MutedTextColor };
        UITheme.StyleLabelTitle(title);
        header.Controls.AddRange([title, subtitle]);
        root.Controls.Add(header);

        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new Padding(0, 10, 0, 4) };
        for (var i = 0; i < 5; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        var names = new[] { "Borradores", "Confirmados", "En produccion", "Listos para entrega", "Atrasados" };
        for (var i = 0; i < names.Length; i++)
        {
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Margin = new Padding(i == 0 ? 0 : 6, 0, i == 4 ? 0 : 6, 0), Padding = new Padding(14, 8, 14, 8), RowCount = 2 };
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            card.Controls.Add(new Label { Text = names[i], Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor, TextAlign = ContentAlignment.MiddleLeft });
            _stats[i] = new Label { Text = "0", Dock = DockStyle.Fill, Font = UITheme.HeaderFont, ForeColor = i == 4 ? UITheme.DangerColor : UITheme.PrimaryColor, TextAlign = ContentAlignment.MiddleLeft };
            card.Controls.Add(_stats[i]);
            stats.Controls.Add(card, i, 0);
        }
        root.Controls.Add(stats);

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UITheme.SurfaceColor, Padding = new Padding(14, 8, 14, 8), ColumnCount = 8, RowCount = 4 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        UITheme.StyleTextBox(_search);
        _search.PlaceholderText = "Número de pedido o cliente";
        ConfigureClearSearchButton(_clearSearch);
        AddFilter(filters, "Buscar por pedido, cliente, destinatario, cotizacion o producto", BuildSearchInput(), 0, 0, 3);
        AddFilter(filters, "Estado", _state, 3, 0);
        AddFilter(filters, "Prioridad", _priority, 4, 0);
        AddFilter(filters, "Desde", _from, 0, 2);
        AddFilter(filters, "Hasta", _to, 1, 2);
        filters.Controls.Add(_useDates, 4, 3);
        filters.Controls.Add(_overdueOnly, 5, 3);
        var clearButton = new Button { Text = "Limpiar", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
        UITheme.StyleSecondaryButton(clearButton);
        clearButton.Click += async (_, _) => await ClearFiltersAsync();
        filters.Controls.Add(clearButton, 7, 3);
        root.Controls.Add(filters);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        UITheme.StyleDataGridView(_grid);
        AddColumns();
        _grid.SelectionChanged += (_, _) => UpdateActions();
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await ViewAsync(); };
        root.Controls.Add(_grid);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 8, 0, 0) };
        var close = new Button { Text = "Cerrar", Width = 105, Height = 40 };
        close.Click += (_, _) => Close();
        foreach (var button in new[] { close, _pdf, _convert, _paymentHistory, _payment, _duplicate, _change, _confirm, _edit, _view, _new })
        {
            button.Width = button == close ? 105 : button == _pdf ? 90 : button == _duplicate ? 125 : button == _change ? 150 : button == _new ? 140 : 115;
            button.Height = 44;
            button.Margin = new Padding(6, 0, 0, 0);
            UITheme.StyleSecondaryButton(button);
            var textWidth = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            button.Width = Math.Max(button.Width, textWidth + 36);
            button.MinimumSize = new Size(button.Width, button.Height);
            button.AutoEllipsis = false;
            button.TextAlign = ContentAlignment.MiddleCenter;
            actions.Controls.Add(button);
        }
        UITheme.StylePrimaryButton(_new);
        UITheme.StyleSuccessButton(_confirm);
        _new.Click += async (_, _) => await NewAsync();
        _view.Click += async (_, _) => await ViewAsync();
        _edit.Click += async (_, _) => await EditAsync();
        _confirm.Click += async (_, _) => await ConfirmAsync();
        _change.Click += async (_, _) => await ChangeStateAsync();
        _duplicate.Click += async (_, _) => await DuplicateAsync();
        _pdf.Click += async (_, _) => await PdfAsync();
        _payment.Click += async (_, _) => await PaymentAsync();
        _paymentHistory.Click += async (_, _) => await PaymentHistoryAsync();
        _convert.Click += async (_, _) => await ConvertAsync();
        root.Controls.Add(actions);
        Controls.Add(root);
        _state.DataSource = new[] { new Option<EstadoPedidoPersonalizado>(null, "Todos") }
            .Concat(Enum.GetValues<EstadoPedidoPersonalizado>().Select(s => new Option<EstadoPedidoPersonalizado>(s, CustomOrderStatusForm.Friendly(s)))).ToList();
        _state.DisplayMember = nameof(Option<EstadoPedidoPersonalizado>.Text);
        _priority.DataSource = new[] { new Option<PrioridadPedido>(null, "Todas") }
            .Concat(Enum.GetValues<PrioridadPedido>().Select(p => new Option<PrioridadPedido>(p, FriendlyPriority(p)))).ToList();
        _priority.DisplayMember = nameof(Option<PrioridadPedido>.Text);
        _from.Value = DateTime.Today.AddMonths(-1);
        _to.Value = DateTime.Today;
        _state.SelectedValueChanged += FilterChanged;
        _priority.SelectedValueChanged += FilterChanged;
        _from.ValueChanged += FilterChanged;
        _to.ValueChanged += FilterChanged;
        _useDates.CheckedChanged += FilterChanged;
        _overdueOnly.CheckedChanged += FilterChanged;
    }

    private static void AddFilter(TableLayoutPanel panel, string label, Control control, int column, int row, int span = 1)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, column, row);
        panel.Controls.Add(control, column, row + 1);
        if (span > 1)
        {
            panel.SetColumnSpan(panel.GetControlFromPosition(column, row)!, span);
            panel.SetColumnSpan(control, span);
        }
    }

    private async void FormLoad(object? sender, EventArgs e)
    {
        try
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER);
            _new.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CREAR);
            _edit.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_EDITAR);
            _confirm.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CONFIRMAR);
            _change.Visible = await _permissions.HasAnyPermissionAsync([
                PermisoEmpleado.PEDIDO_ESTADO_CAMBIAR, PermisoEmpleado.PEDIDO_CANCELAR]);
            _duplicate.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CREAR);
            var canViewFinances = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_FINANZAS_VER);
            foreach (DataGridViewColumn column in _grid.Columns)
                if (column.DataPropertyName is "Financial" or "Paid" or "Balance")
                    column.Visible = canViewFinances;
            _payment.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_PAGO_REGISTRAR);
            _paymentHistory.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER);
            _convert.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CONVERTIR_VENTA);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = CurrentFilter();
            var summaries = await _query.SearchAsync(filter, cancellationToken);
            var stats = await _query.GetStatsAsync(filter with { Page = 1, PageSize = 250 }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _rows = summaries.Select(GridRow.From).ToList();
            _grid.DataSource = null;
            _grid.DataSource = _rows;
            _stats[0].Text = stats.Borradores.ToString();
            _stats[1].Text = stats.Confirmados.ToString();
            _stats[2].Text = stats.EnProduccion.ToString();
            _stats[3].Text = stats.ListosParaEntrega.ToString();
            _stats[4].Text = stats.Atrasados.ToString();
            UpdateActions();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("custom-order-command", ex);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private CustomOrderFilter CurrentFilter() => new(
        _search.Text,
        (_state.SelectedItem as Option<EstadoPedidoPersonalizado>)?.Value,
        (_priority.SelectedItem as Option<PrioridadPedido>)?.Value,
        _useDates.Checked, _from.Value, _to.Value, _overdueOnly.Checked);
    private async Task ClearFiltersAsync()
    {
        _suppressFilterRefresh = true;
        try
        {
            _search.Clear(); _state.SelectedIndex = 0; _priority.SelectedIndex = 0;
            _useDates.Checked = false; _overdueOnly.Checked = false;
            _from.Value = DateTime.Today.AddMonths(-1); _to.Value = DateTime.Today;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        if (_liveSearch is not null)
        {
            await _liveSearch.SearchNowAsync();
        }
        else
        {
            await RefreshAsync();
        }
    }

    private Task SearchFromFirstPageAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

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
        host.Controls.Add(_search, 0, 0);
        host.Controls.Add(_clearSearch, 1, 0);
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
            TechnicalErrorLogger.Log("custom-order-live-search", ex);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private GridRow? Selected() => _grid.CurrentRow?.DataBoundItem as GridRow;
    private void UpdateActions()
    {
        var row = Selected();
        _view.Enabled = row is not null;
        _edit.Enabled = row?.State == EstadoPedidoPersonalizado.BORRADOR;
        _confirm.Enabled = row?.State == EstadoPedidoPersonalizado.BORRADOR;
        _change.Enabled = row is not null && CustomOrderStatusForm.AllowedTargets(row.State).Count > 0;
        _duplicate.Enabled = row is not null;
        _pdf.Enabled = row is not null;
        _payment.Enabled = row is not null && row.State is not EstadoPedidoPersonalizado.BORRADOR and not EstadoPedidoPersonalizado.CANCELADO && !row.SaleId.HasValue;
        _paymentHistory.Enabled = row is not null;
        _convert.Enabled = row?.State == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA && !row.SaleId.HasValue;
    }
    private async Task NewAsync()
    {
        using var form = new CustomOrderEditForm(_service, _customers, _catalog);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
    }
    private async Task EditAsync()
    {
        var row = Selected(); if (row is null) return;
        var detail = await _query.GetByIdAsync(row.Id); if (detail is null) return;
        using var form = new CustomOrderEditForm(_service, _customers, _catalog, detail);
        if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
    }
    private async Task ViewAsync()
    {
        var row = Selected(); if (row is null) return;
        var detail = await _query.GetByIdAsync(row.Id); if (detail is null) return;
        using var form = new CustomOrderDetailForm(detail, _service, _documents, _permissions, _customers, _products, _catalog,
            _finance, _financeQuery, _financeDocuments);
        form.ShowDialog(this);
        await RefreshAsync();
    }
    private async Task ConfirmAsync() => await ExecuteAsync(
        row => _service.ConfirmAsync(row.Id), "Pedido confirmado.");
    private async Task DuplicateAsync() => await ExecuteAsync(
        row => _service.DuplicateAsync(row.Id), "Pedido duplicado como nuevo borrador.");
    private async Task ChangeStateAsync()
    {
        var row = Selected(); if (row is null) return;
        var canOperate = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_ESTADO_CAMBIAR);
        var canCancel = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CANCELAR);
        using var dialog = new CustomOrderStatusForm(row.Code, row.State, canOperate, canCancel);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await ExecuteAsync(_ => _service.ChangeStateAsync(dialog.Request(row.Id)), "Estado actualizado.");
    }
    private async Task PdfAsync()
    {
        var row = Selected(); if (row is null) return;
        try
        {
            var path = await _documents.GeneratePdfAsync(row.Id);
            using var dialog = new GeneratedFileForm("Pedido generado", path);
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async Task PaymentAsync()
    {
        var row = Selected(); if (row is null) return;
        try
        {
            var summary = await _financeQuery.GetSummaryAsync(row.Id);
            if (summary is null) return;
            using var form = new CustomOrderPaymentForm(summary, _finance);
            if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async Task PaymentHistoryAsync()
    {
        var row = Selected(); if (row is null) return;
        using var form = new CustomOrderPaymentsHistoryForm(row.Id, row.Code, _financeQuery, _financeDocuments);
        form.ShowDialog(this);
        await RefreshAsync();
    }
    private async Task ConvertAsync()
    {
        var row = Selected(); if (row is null) return;
        try
        {
            var preview = await _finance.GetConversionPreviewAsync(row.Id);
            using var form = new CustomOrderConversionForm(preview, _finance, _catalog);
            if (form.ShowDialog(this) == DialogResult.OK) await RefreshAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async Task ExecuteAsync(Func<GridRow, Task<CustomOrderDetail>> action, string message)
    {
        var row = Selected(); if (row is null) return;
        try
        {
            await action(row);
            MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("custom-order-command", ex);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddColumns()
    {
        foreach (var (name, header, weight) in new[] {
            ("Code","Pedido",90),("Quotation","Cotizacion",85),("Date","Fecha",75),("Promise","Fecha prometida",95),
            ("Recipient","Cliente/destinatario",145),("Status","Estado",105),("Priority","Prioridad",70),
            ("Products","Productos",55),("Total","Total",70),("Financial","Estado financiero",85),
            ("Paid","Pagado",65),("Balance","Saldo",65),("Remaining","Dias restantes/atraso",95),("User","Usuario",65)})
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = name, HeaderText = header, FillWeight = weight });
    }
    private static string FriendlyPriority(PrioridadPedido priority) =>
        priority == PrioridadPedido.NORMAL ? "Normal" : priority == PrioridadPedido.ALTA ? "Alta" : "Urgente";
    private sealed record Option<T>(T? Value, string Text) where T : struct;
    private sealed record GridRow(
        int Id, string Code, string Quotation, string Date, string Promise, string Recipient,
        EstadoPedidoPersonalizado State, string Status, string Priority, int Products,
        string Total, string Financial, string Paid, string Balance, string Remaining, string User, int? SaleId)
    {
        public static GridRow From(CustomOrderSummary p) => new(
            p.PedidoPersonalizadoId, p.CodigoPedido, p.CodigoCotizacion ?? "-",
            p.FechaPedido.ToString("yyyy-MM-dd"), p.FechaPrometida.ToString("yyyy-MM-dd"),
            p.Destinatario, p.Estado, CustomOrderStatusForm.Friendly(p.Estado),
            FriendlyPriority(p.Prioridad), p.Lineas, (p.TotalEstimadoCentavos / 100m).ToString("$0.00"),
            p.EstadoFinanciero.ToString().Replace('_', ' '), (p.TotalPagadoCentavos / 100m).ToString("$0.00"),
            (p.SaldoPendienteCentavos / 100m).ToString("$0.00"),
            p.Atrasado ? $"{Math.Abs(p.DiasRestantes)} dia(s) tarde" : $"{p.DiasRestantes} dia(s)", p.Usuario, p.VentaId);
    }
}
