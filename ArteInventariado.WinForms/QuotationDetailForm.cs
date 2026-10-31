using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class QuotationDetailForm : Form
{
    private readonly QuotationDetail _quotation;
    private readonly IQuotationService _service;
    private readonly IQuotationDocumentService _documentService;
    private readonly IEmployeePermissionService _permissions;
    private readonly BindingSource _lines = new();
    private readonly BindingSource _history = new();
    private readonly TabControl _tabs = new();
    private readonly TabPage _historyTab = new("Historial") { BackColor = UITheme.SurfaceColor };
    private readonly ToolTip _toolTip = new();

    public QuotationDetailForm(
        QuotationDetail quotation,
        IQuotationService service,
        IQuotationDocumentService documentService,
        IEmployeePermissionService permissions)
    {
        _quotation = quotation;
        _service = service;
        _documentService = documentService;
        _permissions = permissions;
        BuildUi();
        Load += async (_, _) => await ApplyPermissionsAsync();
    }

    private void BuildUi()
    {
        Text = $"Cotizacion {_quotation.CodigoCotizacion}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 740);
        MinimumSize = new Size(980, 660);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildTabs(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 8, 16, 8),
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = $"{_quotation.CodigoCotizacion} - {_quotation.Estado}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        UITheme.StyleLabelTitle(title);
        var recipient = new Label
        {
            Text = $"Destinatario: {_quotation.Destinatario} | Vigencia: {_quotation.Fecha:yyyy-MM-dd} a {_quotation.FechaVencimiento:yyyy-MM-dd}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalBoldFont
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(recipient, 0, 1);
        return panel;
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6, BackColor = UITheme.SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 10, 0, 10) };
        for (var i = 0; i < 4; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddField(panel, "Cliente", _quotation.Destinatario, 0, 0);
        AddField(panel, "Codigo del cliente", _quotation.CodigoCliente ?? "No registrado", 1, 0);
        AddField(panel, "Contacto", $"Telefono: {_quotation.Telefono ?? "-"}\r\nCorreo: {_quotation.Correo ?? "-"}", 2, 0);
        AddField(panel, "Creada por", _quotation.UsuarioCreador, 3, 0);
        AddField(panel, "Actualizada por", _quotation.UsuarioUltimaActualizacion, 0, 2);
        AddField(panel, "Subtotal", Money(_quotation.SubtotalCentavos), 1, 2);
        AddField(panel, "Descuento", Money(_quotation.DescuentoGeneralCentavos), 2, 2);
        AddField(panel, "Total", Money(_quotation.TotalCentavos), 3, 2);
        AddField(panel, "Observaciones", _quotation.Observaciones ?? "-", 0, 4, 4);
        return panel;
    }

    private Control BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        var detailTab = new TabPage("Detalle") { BackColor = UITheme.SurfaceColor };
        var notesTab = new TabPage("Condiciones") { BackColor = UITheme.SurfaceColor };
        detailTab.Controls.Add(CreateLineGrid());
        _historyTab.Controls.Add(CreateHistoryGrid());
        notesTab.Controls.Add(new TextBox
        {
            Text = $"Condiciones comerciales:\r\n{_quotation.CondicionesComerciales ?? "-"}\r\n\r\nDireccion del destinatario:\r\n{_quotation.Direccion ?? "-"}",
            ReadOnly = true,
            Multiline = true,
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            BorderStyle = BorderStyle.None,
            Font = UITheme.NormalFont
        });
        _tabs.TabPages.AddRange([detailTab, _historyTab, notesTab]);
        return _tabs;
    }

    private Control CreateLineGrid()
    {
        var grid = NewGrid();
        AddColumn(grid, "Codigo", "Codigo", 100, 10);
        AddColumn(grid, "Descripcion", "Descripcion", 260, 32);
        AddColumn(grid, "Cantidad", "Cantidad", 80, 8);
        AddColumn(grid, "Precio", "Precio unitario", 110, 10);
        AddColumn(grid, "Descuento", "Descuento", 110, 10);
        AddColumn(grid, "Subtotal", "Subtotal", 110, 10);
        AddColumn(grid, "Observaciones", "Observaciones", 180, 20);
        _lines.DataSource = _quotation.Detalles.Select(d => new LineGridRow(
            d.Codigo ?? "-",
            d.Descripcion,
            d.Cantidad,
            Money(d.PrecioUnitarioCentavos),
            Money(d.DescuentoLineaCentavos),
            Money(d.SubtotalCentavos),
            d.Observaciones ?? string.Empty)).ToList();
        grid.DataSource = _lines;
        return grid;
    }

    private Control CreateHistoryGrid()
    {
        var grid = NewGrid();
        AddColumn(grid, "Fecha", "Fecha/hora", 150, 18);
        AddColumn(grid, "Anterior", "Estado anterior", 120, 15);
        AddColumn(grid, "Nuevo", "Estado nuevo", 120, 15);
        AddColumn(grid, "Usuario", "Usuario", 130, 15);
        AddColumn(grid, "Motivo", "Motivo", 300, 37);
        _history.DataSource = _quotation.Historial.Select(h => new HistoryGridRow(
            ToLocal(h.Fecha).ToString("yyyy-MM-dd HH:mm"),
            h.EstadoAnterior?.ToString() ?? "-",
            h.EstadoNuevo.ToString(),
            h.Usuario,
            h.Motivo ?? string.Empty)).ToList();
        grid.DataSource = _history;
        return grid;
    }

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        panel.Controls.Add(CreateButton("Cerrar", UITheme.StyleSecondaryButton, (_, _) => Close()));
        panel.Controls.Add(CreateButton("Generar PDF", UITheme.StylePrimaryButton, async (_, _) => await GeneratePdfAsync(), 135));
        if (_quotation.Estado == EstadoCotizacion.BORRADOR)
        {
            panel.Controls.Add(CreatePermissionButton("Emitir", UITheme.StyleSuccessButton, async (_, _) => await EmitAsync(), PermisoEmpleado.COTIZACION_EMITIR));
        }
        if (_quotation.Estado == EstadoCotizacion.EMITIDA)
        {
            panel.Controls.Add(CreatePermissionButton("Aceptar", UITheme.StyleSuccessButton, async (_, _) => await ChangeStateAsync(EstadoCotizacion.ACEPTADA), PermisoEmpleado.COTIZACION_EMITIR));
            panel.Controls.Add(CreatePermissionButton("Rechazar", UITheme.StyleDangerButton, async (_, _) => await ChangeStateAsync(EstadoCotizacion.RECHAZADA), PermisoEmpleado.COTIZACION_EMITIR));
        }
        if (_quotation.Estado is EstadoCotizacion.BORRADOR or EstadoCotizacion.EMITIDA)
        {
            panel.Controls.Add(CreatePermissionButton("Cancelar cotizacion", UITheme.StyleDangerButton, async (_, _) => await CancelAsync(), PermisoEmpleado.COTIZACION_CANCELAR, 170));
        }
        panel.Controls.Add(CreatePermissionButton("Duplicar", UITheme.StyleSecondaryButton, async (_, _) => await DuplicateAsync(), PermisoEmpleado.COTIZACION_CREAR));
        return panel;
    }

    private async Task ApplyPermissionsAsync()
    {
        if (!await _permissions.HasPermissionAsync(PermisoEmpleado.COTIZACION_HISTORIAL_VER))
        {
            _tabs.TabPages.Remove(_historyTab);
        }

        foreach (Control control in Controls.OfType<TableLayoutPanel>().SelectMany(Flatten).OfType<Button>())
        {
            if (control.Tag is PermisoEmpleado permission)
            {
                control.Visible = await _permissions.HasPermissionAsync(permission);
            }
        }
    }

    private async Task EmitAsync()
    {
        await ExecuteAsync(() => _service.EmitAsync(_quotation.CotizacionId), "Cotizacion emitida.");
    }
    private async Task ChangeStateAsync(EstadoCotizacion state)
    {
        await ExecuteAsync(() => _service.ChangeStateAsync(new QuotationStateChangeRequest(_quotation.CotizacionId, state)), $"Cotizacion marcada como {state}.");
    }
    private async Task CancelAsync()
    {
        var reason = Prompt.Show("Motivo obligatorio de cancelacion:", "Cancelar cotizacion");
        if (string.IsNullOrWhiteSpace(reason)) return;
        await ExecuteAsync(() => _service.ChangeStateAsync(new QuotationStateChangeRequest(_quotation.CotizacionId, EstadoCotizacion.CANCELADA, reason)), "Cotizacion cancelada.");
    }
    private async Task DuplicateAsync()
    {
        try
        {
            await _service.DuplicateAsync(_quotation.CotizacionId);
            MessageBox.Show("Cotizacion duplicada como nuevo borrador.", "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("quotation-duplication", ex);
            MessageBox.Show(
                "No se pudo duplicar la cotizacion. Revise el registro de errores o intentelo nuevamente.",
                "Cotizaciones",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
    private async Task ExecuteAsync(Func<Task<QuotationDetail>> action, string success)
    {
        try
        {
            await action();
            MessageBox.Show(success, "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cotizaciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private async Task GeneratePdfAsync()
    {
        try
        {
            var path = await _documentService.GeneratePdfAsync(_quotation.CotizacionId);
            using var actions = new GeneratedPdfActionsForm(path);
            actions.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Generar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static DataGridView NewGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        UITheme.StyleDataGridView(grid);
        return grid;
    }
    private static void AddColumn(DataGridView grid, string property, string title, int min, float weight) =>
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, MinimumWidth = min, FillWeight = weight });
    private void AddField(TableLayoutPanel layout, string title, string value, int column, int labelRow, int columnSpan = 1)
    {
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(6, 0, 6, 0)
        };
        var valueLabel = new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(6, 2, 6, 2)
        };
        _toolTip.SetToolTip(valueLabel, value);
        layout.Controls.Add(titleLabel, column, labelRow);
        layout.Controls.Add(valueLabel, column, labelRow + 1);
        if (columnSpan > 1)
        {
            layout.SetColumnSpan(titleLabel, columnSpan);
            layout.SetColumnSpan(valueLabel, columnSpan);
        }
    }
    private static Button CreateButton(string text, Action<Button> style, EventHandler click, int width = 120)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, 40),
            Margin = new Padding(8, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleCenter
        };
        style(button);
        button.Click += click;
        return button;
    }
    private static Button CreatePermissionButton(string text, Action<Button> style, EventHandler click, PermisoEmpleado permission, int width = 120)
    {
        var button = CreateButton(text, style, click, width);
        button.Tag = permission;
        return button;
    }
    private static IEnumerable<Control> Flatten(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Flatten(child)) yield return nested;
        }
    }
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private static DateTime ToLocal(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    private sealed record LineGridRow(string Codigo, string Descripcion, int Cantidad, string Precio, string Descuento, string Subtotal, string Observaciones);
    private sealed record HistoryGridRow(string Fecha, string Anterior, string Nuevo, string Usuario, string Motivo);

    private static class Prompt
    {
        public static string? Show(string text, string title)
        {
            using var form = new Form { Text = title, ClientSize = new Size(500, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            UITheme.StyleForm(form);
            var label = new Label { Text = text, AutoSize = true, Location = new Point(16, 16), Font = UITheme.NormalBoldFont };
            var input = new TextBox { Location = new Point(16, 45), Width = 465 };
            UITheme.StyleTextBox(input);
            var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Location = new Point(275, 92), Size = new Size(100, 36) };
            UITheme.StylePrimaryButton(ok);
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(381, 92), Size = new Size(100, 36) };
            UITheme.StyleSecondaryButton(cancel);
            form.Controls.AddRange([label, input, ok, cancel]);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form.ShowDialog() == DialogResult.OK ? input.Text.Trim() : null;
        }
    }
}
