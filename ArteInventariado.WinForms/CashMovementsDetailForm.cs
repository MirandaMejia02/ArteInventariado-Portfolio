using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed class CashMovementsDetailForm : Form
{
    private readonly ICashMovementQueryService _query;
    private readonly ICommercialOperationsReportService _reports;
    private readonly IEmployeePermissionService _permissions;
    private readonly BindingSource _binding = new();
    private readonly DateTimePicker _from = new() { Width = 150, Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _to = new() { Width = 150, Format = DateTimePickerFormat.Short };
    private readonly ComboBox _type = NewCombo(135);
    private readonly ComboBox _origin = NewCombo(165);
    private readonly ComboBox _method = NewCombo(145);
    private readonly ComboBox _user = NewCombo(180);
    private readonly TextBox _search = new() { Width = 280 };
    private readonly Button _searchButton = new() { Text = "Buscar", Size = new Size(110, 36) };
    private readonly Button _clearButton = new() { Text = "Limpiar", Size = new Size(110, 36) };
    private readonly Button _exportButton = new() { Text = "Exportar caja", Size = new Size(145, 36) };
    private readonly TableLayoutPanel _summary = new();
    private readonly DataGridView _grid = new();

    public CashMovementsDetailForm(
        ICashMovementQueryService query,
        ICommercialOperationsReportService reports,
        IEmployeePermissionService permissions)
    {
        _query = query;
        _reports = reports;
        _permissions = permissions;
        Text = "Detalle de movimientos de caja";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1440, 820);
        MinimumSize = new Size(1120, 700);
        UITheme.StyleForm(this);
        BuildUi();
        Load += FormLoad;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var title = new Label { Text = "Movimientos de caja por origen", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        UITheme.StyleLabelTitle(title);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildFilters(), 0, 1);
        root.Controls.Add(BuildSummary(), 0, 2);
        root.Controls.Add(BuildGrid(), 0, 3);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = new Button { Text = "Cerrar", Size = new Size(120, 36) };
        close.Click += (_, _) => Close();
        UITheme.StyleSecondaryButton(close);
        UITheme.StylePrimaryButton(_exportButton);
        _exportButton.Click += ExportClick;
        footer.Controls.AddRange([close, _exportButton]);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 10, 18, 10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var first = NewFilterRow();
        AddFilter(first, "Desde", _from);
        AddFilter(first, "Hasta", _to);
        AddFilter(first, "Tipo", _type);
        AddFilter(first, "Origen", _origin);
        AddFilter(first, "Metodo", _method);
        var second = NewFilterRow();
        AddFilter(second, "Usuario", _user);
        AddFilter(second, "Buscar", _search);
        UITheme.StylePrimaryButton(_searchButton);
        UITheme.StyleSecondaryButton(_clearButton);
        _searchButton.Click += async (_, _) => await RefreshAsync();
        _clearButton.Click += async (_, _) => await ClearAsync();
        second.Controls.Add(CreateFilterButtonField(_searchButton));
        second.Controls.Add(CreateFilterButtonField(_clearButton));
        panel.Controls.Add(CenterFilterRow(first), 0, 0);
        panel.Controls.Add(CenterFilterRow(second), 0, 1);
        return panel;
    }

    private Control BuildSummary()
    {
        _summary.Dock = DockStyle.Fill;
        _summary.ColumnCount = 6;
        _summary.RowCount = 2;
        _summary.Padding = Padding.Empty;
        for (var index = 0; index < 6; index++)
            _summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        _summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        return _summary;
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
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.ShowCellToolTips = true;
        _grid.DataSource = _binding;
        _grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
        AddColumn("Fecha", "Fecha", 125);
        AddColumn("Tipo", "Tipo", 68);
        AddColumn("Origen", "Origen", 105);
        AddColumn("Caja", "Caja", 60);
        AddColumn("Documento", "Documento", 115);
        AddColumn("Descripcion", "Descripcion", 220, DataGridViewAutoSizeColumnMode.Fill, 180);
        AddColumn("Metodo", "Metodo", 90);
        AddColumn("Entrada", "Entrada ($)", 82);
        AddColumn("Salida", "Salida ($)", 82);
        AddColumn("EfectivoDespues", "Saldo después ($)", 108);
        AddColumn("AfectaEfectivo", "Efectivo", 75);
        AddColumn("Usuario", "Usuario", 95);
        AddColumn("Referencia", "Referencia", 110);
        UITheme.StyleGrid(_grid);
        return _grid;
    }

    private async void FormLoad(object? sender, EventArgs e)
    {
        try
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER);
            _exportButton.Visible = await _permissions.HasPermissionAsync(PermisoEmpleado.REPORTES_COMERCIALES_VER);
            _from.Value = DateTime.Today;
            _to.Value = DateTime.Today;
            BindOptions();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BindOptions()
    {
        _type.DataSource = Options<CashMovementType>();
        _origin.DataSource = Options<CashMovementOrigin>();
        _method.DataSource = Options<MetodoPago>();
    }

    private async Task RefreshAsync()
    {
        try
        {
            SetBusy(true);
            var result = await _query.SearchAsync(new(
                _from.Value.Date, _to.Value.Date,
                Selected<CashMovementType>(_type),
                Selected<CashMovementOrigin>(_origin),
                Selected<MetodoPago>(_method),
                (_user.SelectedItem as UserOption)?.Id,
                _search.Text));
            _binding.DataSource = result.Movements.Select(m => new GridRow(
                Local(m.Fecha).ToString("yyyy-MM-dd HH:mm"), Friendly(m.Tipo), Friendly(m.Origen),
                m.CajaId?.ToString() ?? "—", m.Documento, m.Descripcion, Friendly(m.Metodo), Money(m.EntradaCentavos),
                Money(m.SalidaCentavos), m.EfectivoDespuesCentavos.HasValue ? Money(m.EfectivoDespuesCentavos.Value) : "—",
                m.AfectaEfectivo ? "SI" : "NO", m.Usuario, m.Referencia)).ToList();
            BindUsers(result.Users);
            RenderSummary(result.Summary);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudieron consultar los movimientos:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderSummary(CashAdvancedSummary s)
    {
        _summary.Controls.Clear();
        var physicalIn = s.VentasEfectivoCentavos + s.AbonosClientesEfectivoCentavos + s.AnticiposPedidosEfectivoCentavos + s.AbonosPedidosEfectivoCentavos + s.PagosFinalesPedidosEfectivoCentavos + s.AportesCajaCentavos;
        var physicalOut = s.ReembolsosVentasEfectivoCentavos + s.ReembolsosPedidosEfectivoCentavos + s.PagosComprasEfectivoCentavos + s.AbonosProveedoresEfectivoCentavos + s.GastosCentavos + s.PagosEmpleadosCentavos + s.RetirosCentavos;
        AddCard("Entradas fisicas", physicalIn);
        AddCard("Salidas fisicas", physicalOut);
        AddCard("Aportes para cambio", s.AportesCajaCentavos);
        AddCard("Pagos empleados", s.PagosEmpleadosCentavos);
        AddCard("Transferencias recibidas", s.TransferenciasRecibidasCentavos);
        AddCard("Transferencias pagadas", s.TransferenciasPagadasCentavos);
        AddCard("Bitcoin neto", s.BitcoinRecibidoCentavos - s.BitcoinPagadoCentavos);
        AddCard("Efectivo esperado", s.EfectivoEsperadoCentavos);
    }

    private void AddCard(string title, long amount)
    {
        var label = new Label
        {
            Text = $"{title}\r\n{Money(amount)}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = UITheme.SurfaceColor,
            Margin = new Padding(5),
            Padding = new Padding(4)
        };
        label.Font = UITheme.NormalBoldFont;
        _summary.Controls.Add(label);
    }

    private void BindUsers(IReadOnlyList<UserFilterOption> users)
    {
        var selected = (_user.SelectedItem as UserOption)?.Id;
        var options = new List<UserOption> { new(null, "Todos") };
        options.AddRange(users.Select(u => new UserOption(u.UsuarioId, u.NombreUsuario)));
        _user.DataSource = options;
        if (selected.HasValue)
            _user.SelectedItem = options.FirstOrDefault(x => x.Id == selected) ?? options[0];
    }

    private async Task ClearAsync()
    {
        _from.Value = DateTime.Today;
        _to.Value = DateTime.Today;
        _type.SelectedIndex = 0;
        _origin.SelectedIndex = 0;
        _method.SelectedIndex = 0;
        _user.SelectedIndex = 0;
        _search.Clear();
        await RefreshAsync();
    }

    private async void ExportClick(object? sender, EventArgs e)
    {
        try
        {
            SetBusy(true);
            var result = await _reports.ExportCashDetailAsync(new(_from.Value.Date, _to.Value.Date));
            using var actions = new GeneratedFileActionsForm("Reporte de caja generado", result.FilePath);
            actions.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar el reporte:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _searchButton.Enabled = !busy;
        _clearButton.Enabled = !busy;
        _exportButton.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void AddColumn(
        string property,
        string header,
        int width,
        DataGridViewAutoSizeColumnMode mode = DataGridViewAutoSizeColumnMode.None,
        int minimumWidth = 50) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            MinimumWidth = minimumWidth,
            AutoSizeMode = mode
        });

    private static FlowLayoutPanel NewFilterRow() => new()
    {
        AutoSize = true,
        AutoScroll = false,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Margin = Padding.Empty
    };

    private static Control CenterFilterRow(FlowLayoutPanel row)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Anchor = AnchorStyles.None;
        host.Controls.Add(row, 1, 0);
        return host;
    }
    private static ComboBox NewCombo(int width) => new() { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
    private static void AddFilter(FlowLayoutPanel row, string label, Control control)
    {
        var group = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 14, 0) };
        group.Controls.Add(new Label { Text = label, AutoSize = true });
        group.Controls.Add(control);
        row.Controls.Add(group);
    }

    private static Control CreateFilterButtonField(Button button)
    {
        var panel = new Panel
        {
            Width = button.Width,
            Height = 58,
            Margin = new Padding(0, 18, 12, 0)
        };
        button.Dock = DockStyle.Top;
        panel.Controls.Add(button);
        return panel;
    }

    private void GridCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
            _grid.Rows[e.RowIndex].DataBoundItem is not GridRow row)
        {
            return;
        }

        e.ToolTipText = _grid.Columns[e.ColumnIndex].DataPropertyName switch
        {
            "Documento" => row.Documento,
            "Descripcion" => row.Descripcion,
            "Referencia" => row.Referencia,
            _ => null
        };
    }
    private static List<EnumOption<T>> Options<T>() where T : struct, Enum =>
        [new(null, "Todos"), .. Enum.GetValues<T>().Select(x => new EnumOption<T>(x, Friendly(x)))];
    private static T? Selected<T>(ComboBox combo) where T : struct, Enum => (combo.SelectedItem as EnumOption<T>)?.Value;
    private static string Friendly(MetodoPago? value) => value.HasValue
        ? Friendly(value.Value)
        : "Método no especificado";
    private static string Friendly<T>(T value) where T : Enum
    {
        if (value is CashMovementOrigin origin)
        {
            return origin switch
            {
                CashMovementOrigin.APORTE_CAJA => "Aporte de efectivo",
                CashMovementOrigin.AJUSTE_APERTURA_CAJA => "Ajuste de apertura",
                CashMovementOrigin.AJUSTE_CIERRE_CAJA => "Ajuste de cierre",
                _ => origin.ToString().Replace('_', ' ')
            };
        }

        return value.ToString().Replace('_', ' ');
    }
    private static string Money(long cents) => (cents / 100m).ToString("0.00");
    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

    private sealed record EnumOption<T>(T? Value, string Text) where T : struct, Enum
    {
        public override string ToString() => Text;
    }
    private sealed record UserOption(int? Id, string Text)
    {
        public override string ToString() => Text;
    }
    private sealed record GridRow(string Fecha, string Tipo, string Origen, string Caja, string Documento, string Descripcion,
        string Metodo, string Entrada, string Salida, string EfectivoDespues, string AfectaEfectivo, string Usuario, string? Referencia);
}
