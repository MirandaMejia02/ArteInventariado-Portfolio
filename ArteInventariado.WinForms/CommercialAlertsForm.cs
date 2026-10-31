using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.WinForms;

public sealed class CommercialAlertsForm : Form
{
    private readonly ICommercialAlertQueryService _query;
    private readonly IEmployeePermissionService _permissions;
    private readonly IServiceProvider _services;
    private readonly BindingSource _binding = new();
    private readonly ComboBox _type = NewCombo(270);
    private readonly ComboBox _state = NewCombo(180);
    private readonly DateTimePicker _from = new() { Width = 132, Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _to = new() { Width = 132, Format = DateTimePickerFormat.Short };
    private readonly CheckBox _useDates = new()
    {
        Text = "Usar fechas",
        AutoSize = false,
        MinimumSize = new Size(100, 36),
        TextAlign = ContentAlignment.MiddleLeft,
        CheckAlign = ContentAlignment.MiddleLeft
    };
    private readonly TextBox _search = new() { Width = 320 };
    private readonly Button _searchButton = NewButton("Buscar", 110);
    private readonly Button _clearButton = NewButton("Limpiar", 110, true);
    private readonly TableLayoutPanel _cards = new();
    private readonly DataGridView _grid = new();
    private readonly FlowLayoutPanel _actions = new();
    private readonly Button _openQuotation = NewButton("Abrir cotizacion", 145);
    private readonly Button _openOrder = NewButton("Abrir pedido", 135);
    private readonly Button _payment = NewButton("Registrar pago", 135);
    private readonly Button _refund = NewButton("Registrar reembolso", 165);
    private readonly Button _convert = NewButton("Convertir", 120);

    public CommercialAlertsForm(
        ICommercialAlertQueryService query,
        IEmployeePermissionService permissions,
        IServiceProvider services)
    {
        _query = query;
        _permissions = permissions;
        _services = services;
        Text = "Alertas comerciales";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1366, 790);
        MinimumSize = new Size(1080, 680);
        UITheme.StyleForm(this);
        BuildUi();
        Load += FormLoad;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var title = new Label { Text = "Alertas de cotizaciones y pedidos", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildFilters(), 0, 1);
        root.Controls.Add(BuildCards(), 0, 2);
        root.Controls.Add(BuildGrid(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = UITheme.SurfaceColor, Padding = new Padding(12, 8, 12, 8) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var first = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        first.Controls.Add(FilterGroup("Tipo de alerta", _type), 0, 0);
        first.Controls.Add(FilterGroup("Estado", _state), 1, 0);
        first.Controls.Add(FilterGroup("Desde", _from), 2, 0);
        first.Controls.Add(FilterGroup("Hasta", _to), 3, 0);
        var dateGroup = FilterGroup("Aplicar rango", _useDates);
        _useDates.Dock = DockStyle.Top;
        _useDates.Height = 36;
        _useDates.Margin = new Padding(0, 2, 0, 0);
        first.Controls.Add(dateGroup, 4, 0);

        var second = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        second.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        second.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        second.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        second.Controls.Add(FilterGroup("Busqueda general", _search), 0, 0);
        UITheme.StylePrimaryButton(_searchButton);
        UITheme.StyleSecondaryButton(_clearButton);
        _searchButton.Dock = DockStyle.Fill;
        _clearButton.Dock = DockStyle.Fill;
        _searchButton.Margin = new Padding(8, 14, 4, 6);
        _clearButton.Margin = new Padding(4, 14, 0, 6);
        foreach (var button in new[] { _searchButton, _clearButton })
        {
            button.MinimumSize = new Size(110, 40);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
        }
        _searchButton.Click += async (_, _) => await RefreshAsync();
        _clearButton.Click += async (_, _) => await ClearAsync();
        second.Controls.Add(_searchButton, 1, 0);
        second.Controls.Add(_clearButton, 2, 0);
        panel.Controls.Add(first, 0, 0);
        panel.Controls.Add(second, 0, 1);
        return panel;
    }

    private Control BuildCards()
    {
        _cards.Dock = DockStyle.Fill;
        _cards.ColumnCount = 5;
        _cards.RowCount = 1;
        _cards.Padding = new Padding(0, 8, 0, 8);
        for (var index = 0; index < 5; index++)
            _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        return _cards;
    }

    private Control BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ShowCellToolTips = true;
        _grid.DataSource = _binding;
        _grid.SelectionChanged += async (_, _) => await UpdateActionsAsync();
        AddColumn("Tipo", "Tipo", 190);
        AddColumn("Documento", "Documento", 125);
        AddColumn("Cliente", "Cliente/destinatario", 190);
        AddColumn("Fecha", "Fecha relevante", 135);
        AddColumn("Estado", "Estado", 125);
        AddColumn("Prioridad", "Prioridad", 85);
        AddColumn("Total", "Total ($)", 95, rightAligned: true);
        AddColumn("Pagado", "Pagado ($)", 95, rightAligned: true);
        AddColumn("Saldo", "Saldo ($)", 95, rightAligned: true);
        AddColumn("Dias", "Dias", 60);
        AddColumn("Usuario", "Responsable", 110);
        AddColumn("Descripcion", "Descripcion", 280, DataGridViewAutoSizeColumnMode.Fill);
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                e.ToolTipText = _grid[e.ColumnIndex, e.RowIndex].Value?.ToString();
        };
        UITheme.StyleGrid(_grid);
        return _grid;
    }

    private Control BuildActions()
    {
        _actions.Dock = DockStyle.Fill;
        _actions.FlowDirection = FlowDirection.RightToLeft;
        _actions.WrapContents = false;
        var close = NewButton("Cerrar", 110, true);
        var update = NewButton("Actualizar", 120, true);
        close.Click += (_, _) => Close();
        update.Click += async (_, _) => await RefreshAsync();
        _openQuotation.Click += async (_, _) => await OpenQuotationAsync();
        _openOrder.Click += async (_, _) => await OpenOrderAsync();
        _payment.Click += async (_, _) => await RegisterPaymentAsync();
        _refund.Click += async (_, _) => await RegisterRefundAsync();
        _convert.Click += async (_, _) => await ConvertAsync();
        foreach (var button in new[] { _openQuotation, _openOrder, _payment, _refund, _convert })
            UITheme.StylePrimaryButton(button);
        _actions.Controls.AddRange([close, update, _convert, _refund, _payment, _openOrder, _openQuotation]);
        return _actions;
    }

    private async void FormLoad(object? sender, EventArgs e)
    {
        try
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.ALERTAS_COMERCIALES_VER);
            _from.Value = DateTime.Today.AddDays(-30);
            _to.Value = DateTime.Today.AddDays(30);
            _type.DataSource = AlertOptions();
            _state.DataSource = new[] { "Todos", "EMITIDA", "CONFIRMADO", "EN_PRODUCCION", "LISTO_PARA_ENTREGA", "ENTREGADO", "CANCELADO" };
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            SetBusy(true);
            var selectedType = (_type.SelectedItem as AlertOption)?.Value;
            var state = _state.SelectedIndex > 0 ? _state.SelectedItem?.ToString() : null;
            var result = await _query.SearchAsync(new(
                selectedType, state, _useDates.Checked, _from.Value.Date, _to.Value.Date, _search.Text));
            _binding.DataSource = result.Alerts.Select(a => new GridRow(
                a.DocumentoTipo, a.DocumentoId, a.Tipo, Friendly(a.Tipo), a.Documento,
                a.ClienteDestinatario, Local(a.FechaRelevante).ToString("yyyy-MM-dd"), a.Estado,
                Friendly(a.Prioridad), Money(a.TotalCentavos), Money(a.PagadoCentavos), Money(a.SaldoCentavos),
                a.DiasAtraso, a.UsuarioResponsable, a.Descripcion)).ToList();
            RenderCards(result.Summary);
            await UpdateActionsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudieron consultar las alertas:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderCards(CommercialAlertSummary s)
    {
        _cards.Controls.Clear();
        AddCard("Cotizaciones vencidas", s.CotizacionesVencidas);
        AddCard("Pedidos atrasados", s.PedidosAtrasados);
        AddCard("Listos para entrega", s.ListosParaEntrega);
        AddCard("Saldos pendientes", s.SaldosPendientes);
        AddCard("Reembolsos pendientes", s.ReembolsosPendientes);
    }

    private void AddCard(string text, int value)
    {
        var label = new Label
        {
            Text = $"{text}\r\n{value}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            BackColor = UITheme.SurfaceColor, Margin = new Padding(5), Padding = new Padding(8, 10, 8, 10), Font = UITheme.NormalBoldFont
        };
        _cards.Controls.Add(label);
    }

    private async Task UpdateActionsAsync()
    {
        var row = Current;
        var isOrder = row?.DocumentType == CommercialDocumentType.PEDIDO;
        _openQuotation.Visible = row?.DocumentType == CommercialDocumentType.COTIZACION;
        _openOrder.Visible = isOrder;
        _payment.Visible = isOrder && row!.State is not ("CANCELADO" or "ENTREGADO") &&
            await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_PAGO_REGISTRAR);
        _refund.Visible = isOrder && row!.Type == CommercialAlertType.PEDIDO_CANCELADO_REEMBOLSO_PENDIENTE &&
            await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_REEMBOLSO_REGISTRAR);
        _convert.Visible = isOrder && row!.State == "LISTO_PARA_ENTREGA" &&
            await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CONVERTIR_VENTA);
    }

    private async Task OpenQuotationAsync()
    {
        var row = Current;
        if (row?.DocumentType != CommercialDocumentType.COTIZACION) return;
        var query = _services.GetRequiredService<IQuotationQueryService>();
        var detail = await query.GetByIdAsync(row.DocumentId);
        if (detail is null) return;
        using var form = new QuotationDetailForm(detail,
            _services.GetRequiredService<IQuotationService>(),
            _services.GetRequiredService<IQuotationDocumentService>(), _permissions);
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task OpenOrderAsync()
    {
        var row = Current;
        if (row?.DocumentType != CommercialDocumentType.PEDIDO) return;
        var query = _services.GetRequiredService<ICustomOrderQueryService>();
        var detail = await query.GetByIdAsync(row.DocumentId);
        if (detail is null) return;
        using var form = new CustomOrderDetailForm(detail,
            _services.GetRequiredService<ICustomOrderService>(),
            _services.GetRequiredService<ICustomOrderDocumentService>(), _permissions,
            _services.GetRequiredService<ICustomerQueryService>(),
            _services.GetRequiredService<IProductService>(),
            _services.GetRequiredService<IPosCatalogService>(),
            _services.GetRequiredService<ICustomOrderFinanceService>(),
            _services.GetRequiredService<ICustomOrderFinanceQueryService>(),
            _services.GetRequiredService<ICustomOrderFinanceDocumentService>());
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task RegisterPaymentAsync()
    {
        var summary = await CurrentFinanceAsync();
        if (summary is null) return;
        using var form = new CustomOrderPaymentForm(summary, _services.GetRequiredService<ICustomOrderFinanceService>());
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task RegisterRefundAsync()
    {
        var summary = await CurrentFinanceAsync();
        if (summary is null) return;
        using var form = new CustomOrderRefundForm(summary, _services.GetRequiredService<ICustomOrderFinanceService>());
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task ConvertAsync()
    {
        var row = Current;
        if (row?.DocumentType != CommercialDocumentType.PEDIDO) return;
        var finance = _services.GetRequiredService<ICustomOrderFinanceService>();
        var preview = await finance.GetConversionPreviewAsync(row.DocumentId);
        using var form = new CustomOrderConversionForm(preview, finance, _services.GetRequiredService<IPosCatalogService>());
        form.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task<CustomOrderFinanceSummary?> CurrentFinanceAsync()
    {
        var row = Current;
        return row?.DocumentType == CommercialDocumentType.PEDIDO
            ? await _services.GetRequiredService<ICustomOrderFinanceQueryService>().GetSummaryAsync(row.DocumentId)
            : null;
    }

    private async Task ClearAsync()
    {
        _type.SelectedIndex = 0;
        _state.SelectedIndex = 0;
        _useDates.Checked = false;
        _search.Clear();
        await RefreshAsync();
    }

    private void SetBusy(bool busy)
    {
        _searchButton.Enabled = !busy;
        _clearButton.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private GridRow? Current => _binding.Current as GridRow;
    private void AddColumn(
        string property,
        string header,
        int width,
        DataGridViewAutoSizeColumnMode mode = DataGridViewAutoSizeColumnMode.None,
        bool rightAligned = false) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            MinimumWidth = Math.Min(width, 70),
            AutoSizeMode = mode,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = rightAligned ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft
            }
        });
    private static ComboBox NewCombo(int width) => new() { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
    private static Button NewButton(string text, int width, bool secondary = false)
    {
        var button = new Button { Text = text, Size = new Size(width, 36), Margin = new Padding(8, 4, 0, 0) };
        if (secondary) UITheme.StyleSecondaryButton(button); else UITheme.StylePrimaryButton(button);
        return button;
    }
    private static Control FilterGroup(string label, Control control)
    {
        var group = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 10, 0) };
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, AutoEllipsis = true, Font = UITheme.NormalBoldFont }, 0, 0);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 0, 2);
        if (control is CheckBox checkBox)
        {
            checkBox.AutoSize = false;
            checkBox.Padding = Padding.Empty;
            checkBox.TextAlign = ContentAlignment.MiddleLeft;
            checkBox.CheckAlign = ContentAlignment.MiddleLeft;
        }
        group.Controls.Add(control, 0, 1);
        return group;
    }
    private static List<AlertOption> AlertOptions() =>
        [new(null, "Todos"), .. Enum.GetValues<CommercialAlertType>().Select(x => new AlertOption(x, Friendly(x)))];
    private static string Friendly<T>(T value) where T : notnull => value.ToString()!.Replace('_', ' ');
    private static string Money(long cents) => (cents / 100m).ToString("0.00");
    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

    private sealed record AlertOption(CommercialAlertType? Value, string Text)
    {
        public override string ToString() => Text;
    }
    private sealed record GridRow(
        CommercialDocumentType DocumentType, int DocumentId, CommercialAlertType Type,
        string Tipo, string Documento, string Cliente, string Fecha, string State,
        string Prioridad, string Total, string Pagado, string Saldo, int Dias,
        string Usuario, string Descripcion);
}
