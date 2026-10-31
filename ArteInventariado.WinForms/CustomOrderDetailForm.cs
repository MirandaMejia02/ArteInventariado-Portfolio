using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class CustomOrderDetailForm : Form
{
    private readonly CustomOrderDetail _order;
    private readonly ICustomOrderService _service;
    private readonly ICustomOrderDocumentService _documents;
    private readonly IEmployeePermissionService _permissions;
    private readonly ICustomerQueryService _customers;
    private readonly IProductService _products;
    private readonly IPosCatalogService _catalog;
    private readonly ICustomOrderFinanceService _finance;
    private readonly ICustomOrderFinanceQueryService _financeQuery;
    private readonly ICustomOrderFinanceDocumentService _financeDocuments;
    private readonly FlowLayoutPanel _actions = new();
    private readonly DataGridView _financeGrid = NewGrid();
    private readonly ToolTip _toolTip = new();
    private TabPage? _financeTab;

    public CustomOrderDetailForm(
        CustomOrderDetail order,
        ICustomOrderService service,
        ICustomOrderDocumentService documents,
        IEmployeePermissionService permissions,
        ICustomerQueryService customers,
        IProductService products,
        IPosCatalogService catalog,
        ICustomOrderFinanceService finance,
        ICustomOrderFinanceQueryService financeQuery,
        ICustomOrderFinanceDocumentService financeDocuments)
    {
        _order = order;
        _service = service;
        _documents = documents;
        _permissions = permissions;
        _customers = customers;
        _products = products;
        _catalog = catalog;
        _finance = finance;
        _financeQuery = financeQuery;
        _financeDocuments = financeDocuments;
        BuildUi();
        Load += async (_, _) => await ApplyPermissionsAsync();
    }

    private void BuildUi()
    {
        Text = $"Pedido {_order.CodigoPedido}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1320, 820);
        MinimumSize = new Size(1100, 700);
        UITheme.StyleForm(this);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildLinesTab());
        tabs.TabPages.Add(BuildNotesTab());
        tabs.TabPages.Add(BuildHistoryTab());
        _financeTab = BuildFinanceTab();
        tabs.TabPages.Add(_financeTab);
        root.Controls.Add(tabs);

        _actions.Dock = DockStyle.Fill;
        _actions.FlowDirection = FlowDirection.RightToLeft;
        _actions.WrapContents = true;
        _actions.AutoScroll = true;
        _actions.Padding = new Padding(0, 10, 0, 4);
        AddAction("Volver", UITheme.StyleSecondaryButton, (_, _) => Close());
        AddAction("Generar PDF", UITheme.StyleSecondaryButton, async (_, _) => await GeneratePdfAsync());
        AddAction("Historial de pagos", UITheme.StyleSecondaryButton, async (_, _) => await PaymentHistoryAsync(), PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER, 165);
        if (!_order.VentaId.HasValue && _order.Estado is not EstadoPedidoPersonalizado.BORRADOR and not EstadoPedidoPersonalizado.CANCELADO)
            AddAction("Registrar pago", UITheme.StylePrimaryButton, async (_, _) => await PaymentAsync(), PermisoEmpleado.PEDIDO_PAGO_REGISTRAR, 145);
        if (_order.TotalPagadoCentavos > _order.TotalReembolsadoCentavos && !_order.VentaId.HasValue)
            AddAction("Reembolsar", UITheme.StyleDangerButton, async (_, _) => await RefundAsync(), PermisoEmpleado.PEDIDO_REEMBOLSO_REGISTRAR, 125);
        if (_order.Estado == EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA && !_order.VentaId.HasValue)
            AddAction("Convertir en venta", UITheme.StyleSuccessButton, async (_, _) => await ConvertAsync(), PermisoEmpleado.PEDIDO_CONVERTIR_VENTA, 165);
        AddAction("Duplicar", UITheme.StyleSecondaryButton, async (_, _) => await DuplicateAsync(), PermisoEmpleado.PEDIDO_CREAR);
        if (_order.Estado == EstadoPedidoPersonalizado.BORRADOR)
        {
            AddAction("Editar", UITheme.StylePrimaryButton, async (_, _) => await EditAsync(), PermisoEmpleado.PEDIDO_EDITAR);
            AddAction("Confirmar", UITheme.StyleSuccessButton, async (_, _) => await ConfirmAsync(), PermisoEmpleado.PEDIDO_CONFIRMAR);
        }
        if (CustomOrderStatusForm.AllowedTargets(_order.Estado).Count > 0)
            AddAction("Cambiar estado", UITheme.StylePrimaryButton, async (_, _) => await ChangeStateAsync(),
                [PermisoEmpleado.PEDIDO_ESTADO_CAMBIAR, PermisoEmpleado.PEDIDO_CANCELAR], 150);
        root.Controls.Add(_actions);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(16, 8, 16, 8),
            Margin = new Padding(0, 0, 0, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var title = new Label
        {
            Text = $"{_order.CodigoPedido} - {CustomOrderStatusForm.Friendly(_order.Estado)}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        UITheme.StyleLabelTitle(title);
        header.Controls.Add(title, 0, 0);
        header.SetColumnSpan(title, 2);

        AddHeaderValue(header, $"Cotizacion origen: {_order.CodigoCotizacion ?? "Pedido manual"}", 0);
        AddHeaderValue(header, $"Prioridad: {FriendlyPriority(_order.Prioridad)}", 1);
        return header;
    }

    private Control BuildSummary()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 4, 0, 10)
        };
        for (var index = 0; index < 4; index++)
        {
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        AddSummaryField(summary, "Cliente/destinatario", _order.Destinatario, 0, 0);
        AddSummaryField(summary, "Contacto", $"{_order.Telefono ?? "-"} | {_order.Correo ?? "-"}", 1, 0);
        AddSummaryField(summary, "Fecha del pedido", _order.FechaPedido.ToString("yyyy-MM-dd"), 2, 0);
        AddSummaryField(summary, "Fecha prometida", _order.FechaPrometida.ToString("yyyy-MM-dd"), 3, 0);
        AddSummaryField(summary, "Inicio", Date(_order.FechaInicio), 0, 2);
        AddSummaryField(summary, "Finalizacion", Date(_order.FechaFinalizacion), 1, 2);
        AddSummaryField(summary, "Entrega", Date(_order.FechaEntregaReal), 2, 2);
        AddSummaryField(summary, "Total estimado", Money(_order.TotalEstimadoCentavos), 3, 2);
        return summary;
    }

    private void AddHeaderValue(TableLayoutPanel header, string text, int column)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(2, 0, 8, 0)
        };
        _toolTip.SetToolTip(label, text);
        header.Controls.Add(label, column, 1);
    }

    private void AddSummaryField(
        TableLayoutPanel summary,
        string title,
        string value,
        int column,
        int labelRow)
    {
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(8, 0, 8, 2)
        };
        var content = new Label
        {
            Text = value,
            AccessibleName = $"Resumen {title}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.TopLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Padding = new Padding(8, 7, 8, 2),
            Margin = new Padding(0)
        };
        _toolTip.SetToolTip(label, value);
        _toolTip.SetToolTip(content, value);
        summary.Controls.Add(label, column, labelRow);
        summary.Controls.Add(content, column, labelRow + 1);
    }

    private TabPage BuildLinesTab()
    {
        var page = new TabPage("Productos y especificaciones") { BackColor = UITheme.SurfaceColor, Padding = new Padding(8) };
        var grid = NewGrid();
        foreach (var (property, title, weight) in new[] {
            ("Codigo","Codigo",65),("Descripcion","Producto / descripcion",150),("Cantidad","Cantidad",55),
            ("Precio","Precio",60),("Descuento","Descuento",65),("Subtotal","Subtotal",65),
            ("Especificaciones","Especificaciones",130),("Personalizacion","Personalizacion",130),("Observaciones","Observaciones",110)})
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, FillWeight = weight });
        grid.DataSource = _order.Detalles.Select(d => new LineRow(
            d.CodigoSku ?? d.Codigo ?? "-",
            string.IsNullOrWhiteSpace(d.Variante) ? d.Descripcion : $"{d.Descripcion} / {d.Variante}",
            d.Cantidad, Money(d.PrecioUnitarioCentavos),
            Money(d.DescuentoLineaCentavos), Money(d.SubtotalCentavos), d.Especificaciones ?? "-",
            string.Join(" | ", new[] { d.Color, d.Medidas, d.Material, d.TextoPersonalizado }
                .Where(v => !string.IsNullOrWhiteSpace(v))), d.Observaciones ?? "-")).ToList();
        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildNotesTab()
    {
        var page = new TabPage("Datos y notas") { BackColor = UITheme.SurfaceColor, Padding = new Padding(12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        AddNote(layout, "Descripcion general", _order.DescripcionGeneral, 0, 0);
        AddNote(layout, "Especificaciones generales", _order.EspecificacionesGenerales, 1, 0);
        AddNote(layout, "Notas para el cliente", _order.NotasCliente, 0, 1);
        AddNote(layout, "Observaciones internas", _order.ObservacionesInternas, 1, 1);
        AddNote(layout, "Direccion", _order.Direccion, 0, 2);
        AddNote(layout, "Usuarios", $"Creado por {_order.UsuarioCreador}\r\nActualizado por {_order.UsuarioUltimaActualizacion}", 1, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildHistoryTab()
    {
        var page = new TabPage("Historial de estados") { BackColor = UITheme.SurfaceColor, Padding = new Padding(8) };
        var grid = NewGrid();
        foreach (var (property, title, weight) in new[] {
            ("Fecha","Fecha/hora",90),("Anterior","Estado anterior",90),("Nuevo","Estado nuevo",90),("Usuario","Usuario",80),("Motivo","Motivo",180)})
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, FillWeight = weight });
        grid.DataSource = _order.Historial.Select(h => new HistoryRow(
            Local(h.Fecha).ToString("yyyy-MM-dd HH:mm"), h.EstadoAnterior?.ToString() ?? "-",
            h.EstadoNuevo.ToString(), h.Usuario, h.Motivo ?? "-")).ToList();
        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildFinanceTab()
    {
        var page = new TabPage("Pagos y saldo") { BackColor = UITheme.SurfaceColor, Padding = new Padding(8) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = $"Total: {Money(_order.TotalEstimadoCentavos)}    Pagado: {Money(_order.TotalPagadoCentavos)}    " +
                $"Reembolsado: {Money(_order.TotalReembolsadoCentavos)}    Saldo: {Money(_order.SaldoPendienteCentavos)}\r\n" +
                $"Estado financiero: {_order.EstadoFinanciero}    Venta relacionada: {_order.NumeroVenta ?? "-"}",
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Font = UITheme.NormalBoldFont
        });
        foreach (var (property, title, weight) in new[] {
            ("Date","Fecha",80),("Code","Codigo",85),("Type","Tipo",70),("Method","Metodo",65),
            ("Amount","Monto",60),("Before","Saldo anterior",70),("After","Saldo nuevo",70),
            ("User","Usuario",65),("Status","Estado",80)})
            _financeGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, FillWeight = weight });
        root.Controls.Add(_financeGrid);
        page.Controls.Add(root);
        return page;
    }

    private async Task ApplyPermissionsAsync()
    {
        var canViewFinances = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_FINANZAS_VER);
        if (!canViewFinances && _financeTab?.Parent is TabControl tabs)
            tabs.TabPages.Remove(_financeTab);
        foreach (Control control in _actions.Controls)
        {
            if (control.Tag is PermisoEmpleado permission)
                control.Visible = await _permissions.HasPermissionAsync(permission);
            else if (control.Tag is IReadOnlyList<PermisoEmpleado> alternatives)
                control.Visible = await _permissions.HasAnyPermissionAsync(alternatives);
        }
        if (canViewFinances &&
            await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_PAGOS_HISTORIAL_VER))
        {
            var rows = await _financeQuery.GetHistoryAsync(_order.PedidoPersonalizadoId);
            _financeGrid.DataSource = rows.Select(r => new FinanceRow(
                Local(r.Fecha).ToString("yyyy-MM-dd HH:mm"), r.Codigo, r.Tipo,
                r.MetodoPago?.ToString() ?? "-", Money(r.MontoCentavos),
                Money(r.SaldoAnteriorCentavos), Money(r.SaldoNuevoCentavos), r.Usuario, r.Estado)).ToList();
        }
    }

    private async Task EditAsync()
    {
        using var form = new CustomOrderEditForm(_service, _customers, _catalog, _order);
        if (form.ShowDialog(this) == DialogResult.OK) Close();
    }
    private async Task ConfirmAsync() => await ExecuteAndCloseAsync(
        () => _service.ConfirmAsync(_order.PedidoPersonalizadoId), "Pedido confirmado.");
    private async Task DuplicateAsync() => await ExecuteAndCloseAsync(
        () => _service.DuplicateAsync(_order.PedidoPersonalizadoId), "Pedido duplicado como nuevo borrador.");
    private async Task ChangeStateAsync()
    {
        var canOperate = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_ESTADO_CAMBIAR);
        var canCancel = await _permissions.HasPermissionAsync(PermisoEmpleado.PEDIDO_CANCELAR);
        using var form = new CustomOrderStatusForm(_order.CodigoPedido, _order.Estado, canOperate, canCancel);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        await ExecuteAndCloseAsync(() => _service.ChangeStateAsync(form.Request(_order.PedidoPersonalizadoId)), "Estado actualizado.");
    }
    private async Task GeneratePdfAsync()
    {
        try
        {
            var path = await _documents.GeneratePdfAsync(_order.PedidoPersonalizadoId);
            using var form = new GeneratedFileForm("Pedido generado", path);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("custom-order-detail-command", ex);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private async Task PaymentAsync()
    {
        var summary = await _financeQuery.GetSummaryAsync(_order.PedidoPersonalizadoId);
        if (summary is null) return;
        using var form = new CustomOrderPaymentForm(summary, _finance);
        if (form.ShowDialog(this) == DialogResult.OK) { DialogResult = DialogResult.OK; Close(); }
    }
    private async Task RefundAsync()
    {
        var summary = await _financeQuery.GetSummaryAsync(_order.PedidoPersonalizadoId);
        if (summary is null) return;
        using var form = new CustomOrderRefundForm(summary, _finance);
        if (form.ShowDialog(this) == DialogResult.OK) { DialogResult = DialogResult.OK; Close(); }
    }
    private async Task PaymentHistoryAsync()
    {
        using var form = new CustomOrderPaymentsHistoryForm(
            _order.PedidoPersonalizadoId, _order.CodigoPedido, _financeQuery, _financeDocuments);
        form.ShowDialog(this);
    }
    private async Task ConvertAsync()
    {
        try
        {
            var preview = await _finance.GetConversionPreviewAsync(_order.PedidoPersonalizadoId);
            using var form = new CustomOrderConversionForm(preview, _finance, _catalog);
            if (form.ShowDialog(this) == DialogResult.OK) { DialogResult = DialogResult.OK; Close(); }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async Task ExecuteAndCloseAsync(Func<Task<CustomOrderDetail>> action, string message)
    {
        try
        {
            await action();
            MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("custom-order-detail-command", ex);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddAction(string text, Action<Button> style, EventHandler click, PermisoEmpleado? permission = null, int width = 120)
    {
        var button = CreateActionButton(text, width, permission);
        style(button); button.Click += click; _actions.Controls.Add(button);
    }
    private void AddAction(string text, Action<Button> style, EventHandler click, IReadOnlyList<PermisoEmpleado> permissions, int width)
    {
        var button = CreateActionButton(text, width, permissions);
        style(button); button.Click += click; _actions.Controls.Add(button);
    }

    private Button CreateActionButton(string text, int requestedWidth, object? permissionTag)
    {
        var measuredWidth = TextRenderer.MeasureText(text, UITheme.NormalBoldFont).Width + 34;
        var width = Math.Max(requestedWidth, measuredWidth);
        var button = new Button
        {
            Text = text,
            Size = new Size(width, 40),
            MinimumSize = new Size(width, 40),
            Margin = new Padding(7, 0, 0, 7),
            Tag = permissionTag
        };
        _toolTip.SetToolTip(button, GetActionHelp(text));
        return button;
    }

    private static string GetActionHelp(string text) => text switch
    {
        "Cambiar estado" => "Actualiza el estado operativo del pedido.",
        "Duplicar" => "Crea un nuevo borrador a partir de este pedido.",
        "Registrar pago" => "Registra un anticipo, abono o pago final.",
        "Historial de pagos" => "Consulta pagos, reembolsos y aplicaciones del pedido.",
        "Generar PDF" => "Genera el documento PDF del pedido.",
        "Volver" => "Cierra el detalle y vuelve al listado.",
        _ => text
    };
    private static DataGridView NewGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        UITheme.StyleDataGridView(grid);
        return grid;
    }
    private static void AddNote(TableLayoutPanel layout, string title, string? value, int column, int row)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(6) };
        group.Controls.Add(new TextBox { Text = value ?? "-", Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = UITheme.SurfaceColor });
        layout.Controls.Add(group, column, row);
    }
    private static string Date(DateTime? value) => value.HasValue ? Local(value.Value).ToString("yyyy-MM-dd HH:mm") : "-";
    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private static string FriendlyPriority(PrioridadPedido priority) => priority == PrioridadPedido.NORMAL ? "Normal" : priority == PrioridadPedido.ALTA ? "Alta" : "Urgente";
    private sealed record LineRow(string Codigo, string Descripcion, int Cantidad, string Precio, string Descuento, string Subtotal, string Especificaciones, string Personalizacion, string Observaciones);
    private sealed record HistoryRow(string Fecha, string Anterior, string Nuevo, string Usuario, string Motivo);
    private sealed record FinanceRow(string Date, string Code, string Type, string Method, string Amount, string Before, string After, string User, string Status);
}
