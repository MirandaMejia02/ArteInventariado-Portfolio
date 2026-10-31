using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class AccountPaymentsHistoryForm : Form
{
    private readonly IAccountPaymentQueryService _paymentQueryService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly AccountPaymentHistoryFilter _initialFilter;
    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbMethod = new();
    private readonly DateTimePicker _dtpFrom = new();
    private readonly DateTimePicker _dtpTo = new();
    private readonly CheckBox _chkUseDates = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblFooter = new();
    private readonly Label _lblEmpty = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnReceipt = new();
    private readonly Button _btnClose = new();

    private IReadOnlyList<PaymentRow> _rows = [];
    private bool _isRefreshing;

    public AccountPaymentsHistoryForm(
        IAccountPaymentQueryService paymentQueryService,
        IEmployeePermissionService permissionService,
        AccountPaymentHistoryFilter? initialFilter = null)
    {
        _paymentQueryService = paymentQueryService;
        _permissionService = permissionService;
        _initialFilter = initialFilter ?? new AccountPaymentHistoryFilter();
        BuildUi();
        Load += AccountPaymentsHistoryForm_Load;
    }

    private async void AccountPaymentsHistoryForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CUENTAS_COBRAR_VER);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Historial de abonos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Historial de abonos";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 700);
        MinimumSize = new Size(1100, 650);
        MaximizeBox = true;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildFilters(), 0, 1);
        root.Controls.Add(BuildGrid(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 8, 16, 8),
            Margin = new Padding(0, 0, 0, 10),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Historial de abonos",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Consulta y reapertura de comprobantes internos",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.MutedTextColor
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var primaryFilters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        primaryFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        primaryFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));

        var dateFilters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0, 6, 0, 0)
        };
        dateFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        dateFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        dateFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        dateFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        dateFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));

        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "Abono, cliente, cuenta o venta";
        _txtSearch.Text = _initialFilter.Search ?? _initialFilter.CodigoAbono ?? string.Empty;
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await RefreshAsync();
            }
        };
        UITheme.StyleTextBox(_txtSearch);

        _cmbMethod.Dock = DockStyle.Fill;
        _cmbMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMethod.Items.AddRange([
            new MethodFilter("Todos", null),
            new MethodFilter("Efectivo", MetodoPago.EFECTIVO),
            new MethodFilter("Transferencia", MetodoPago.TRANSFERENCIA),
            new MethodFilter("Bitcoin", MetodoPago.BITCOIN)
        ]);
        var initialMethodIndex = _cmbMethod.Items
            .Cast<MethodFilter>()
            .Select((item, index) => (item, index))
            .FirstOrDefault(entry => entry.item.Method == _initialFilter.MetodoPago)
            .index;
        _cmbMethod.SelectedIndex = initialMethodIndex;

        _chkUseDates.Text = "Usar fechas";
        _chkUseDates.Dock = DockStyle.Fill;
        _chkUseDates.Font = UITheme.NormalBoldFont;
        _chkUseDates.TextAlign = ContentAlignment.MiddleLeft;
        _chkUseDates.AutoEllipsis = false;
        _chkUseDates.Checked = _initialFilter.Desde.HasValue || _initialFilter.Hasta.HasValue;
        _chkUseDates.CheckedChanged += (_, _) => UpdateDatePickerState();

        ConfigureDatePicker(_dtpFrom, _initialFilter.Desde ?? DateTime.Today.AddDays(-30));
        ConfigureDatePicker(_dtpTo, _initialFilter.Hasta ?? DateTime.Today);
        UpdateDatePickerState();

        _btnRefresh.Text = "Buscar";
        _btnRefresh.Size = new Size(145, 38);
        _btnRefresh.Dock = DockStyle.None;
        _btnRefresh.Anchor = AnchorStyles.None;
        _btnRefresh.Margin = Padding.Empty;
        _btnRefresh.Click += async (_, _) => await RefreshAsync();
        UITheme.StylePrimaryButton(_btnRefresh);
        _btnRefresh.TextAlign = ContentAlignment.MiddleCenter;
        _btnRefresh.Padding = Padding.Empty;

        _btnClear.Text = "Limpiar";
        _btnClear.Size = new Size(145, 38);
        _btnClear.Dock = DockStyle.None;
        _btnClear.Anchor = AnchorStyles.None;
        _btnClear.Margin = Padding.Empty;
        _btnClear.Click += async (_, _) => await ClearFiltersAsync();
        UITheme.StyleSecondaryButton(_btnClear);
        _btnClear.TextAlign = ContentAlignment.MiddleCenter;
        _btnClear.Padding = Padding.Empty;

        primaryFilters.Controls.Add(BuildField("Buscar", _txtSearch), 0, 0);
        primaryFilters.Controls.Add(BuildField("Método", _cmbMethod, new Padding(10, 0, 0, 0)), 1, 0);
        dateFilters.Controls.Add(BuildField("Desde", _dtpFrom), 0, 0);
        dateFilters.Controls.Add(BuildField("Hasta", _dtpTo, new Padding(10, 0, 0, 0)), 1, 0);
        dateFilters.Controls.Add(_chkUseDates, 2, 0);
        dateFilters.Controls.Add(_btnRefresh, 3, 0);
        dateFilters.Controls.Add(_btnClear, 4, 0);
        panel.Controls.Add(primaryFilters, 0, 0);
        panel.Controls.Add(dateFilters, 0, 1);
        return panel;
    }

    private Control BuildGrid()
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };

        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ShowCellToolTips = true;
        _grid.DataSource = _binding;
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                await OpenReceiptAsync();
            }
        };
        _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        _grid.SelectionChanged += (_, _) => UpdateReceiptButtonState();

        AddColumn("Fecha", "Fecha", 100, 11, DataGridViewContentAlignment.MiddleCenter, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Codigo", "Código", 115, 15);
        AddColumn("Cliente", "Cliente", 130, 17);
        AddColumn("Cuenta", "Cuenta", 100, 13);
        AddColumn("Venta", "Venta", 100, 13);
        AddColumn("Metodo", "Método", 80, 9, DataGridViewContentAlignment.MiddleCenter, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Monto", "Monto", 75, 8.5f, DataGridViewContentAlignment.MiddleRight, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Interes", "Interés", 75, 8.5f, DataGridViewContentAlignment.MiddleRight, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Capital", "Capital", 75, 8.5f, DataGridViewContentAlignment.MiddleRight, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("SaldoNuevo", "Saldo nuevo", 80, 9.5f, DataGridViewContentAlignment.MiddleRight, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("Usuario", "Usuario", 85, 10, DataGridViewContentAlignment.MiddleLeft, DataGridViewContentAlignment.MiddleCenter);

        _lblEmpty.Text = "No se encontraron abonos con los filtros seleccionados.";
        _lblEmpty.Height = 48;
        _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmpty.Font = UITheme.NormalBoldFont;
        _lblEmpty.ForeColor = UITheme.MutedTextColor;
        _lblEmpty.BackColor = UITheme.SurfaceColor;
        _lblEmpty.Visible = false;
        _lblEmpty.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        container.Controls.Add(_grid);
        container.Controls.Add(_lblEmpty);
        container.Resize += (_, _) => PositionEmptyState(container);
        PositionEmptyState(container);
        return container;
    }

    private Control BuildFooter()
    {
        _lblFooter.Dock = DockStyle.Fill;
        _lblFooter.TextAlign = ContentAlignment.MiddleLeft;
        _lblFooter.Font = UITheme.NormalBoldFont;
        _lblFooter.ForeColor = UITheme.MutedTextColor;

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 322));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        ConfigureButton(_btnClose, "Cerrar", 110, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnClose);
        ConfigureButton(_btnReceipt, "Ver comprobante", 180, async (_, _) => await OpenReceiptAsync());
        _btnReceipt.Enabled = false;
        UITheme.StylePrimaryButton(_btnReceipt);
        actions.Controls.AddRange([_btnClose, _btnReceipt]);
        footer.Controls.Add(_lblFooter, 0, 0);
        footer.Controls.Add(actions, 1, 0);
        return footer;
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing || !TryBuildFilter(out var filter))
        {
            return;
        }

        try
        {
            SetBusy(true);
            var payments = await _paymentQueryService.GetHistoryAsync(filter);
            _rows = payments.Select(PaymentRow.FromSummary).ToList();
            _binding.DataSource = _rows;
            _grid.ClearSelection();
            _lblEmpty.Visible = _rows.Count == 0;
            if (_lblEmpty.Visible)
            {
                _lblEmpty.BringToFront();
            }

            _lblFooter.Text = $"{_rows.Count} abono(s) · Total mostrado: {SaleReceiptViewModel.FormatDollars(payments.Sum(p => p.MontoCentavos))}";
            UpdateReceiptButtonState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Historial de abonos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryBuildFilter(out AccountPaymentHistoryFilter filter)
    {
        if (_chkUseDates.Checked && _dtpFrom.Value.Date > _dtpTo.Value.Date)
        {
            MessageBox.Show(
                "La fecha Desde no puede ser posterior a la fecha Hasta.",
                "Historial de abonos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _dtpFrom.Focus();
            filter = default!;
            return false;
        }

        var method = _cmbMethod.SelectedItem is MethodFilter methodFilter ? methodFilter.Method : null;
        filter = _initialFilter with
        {
            Desde = _chkUseDates.Checked ? _dtpFrom.Value.Date : null,
            Hasta = _chkUseDates.Checked ? _dtpTo.Value.Date.AddDays(1).AddTicks(-1) : null,
            MetodoPago = method,
            CodigoAbono = null,
            Search = string.IsNullOrWhiteSpace(_txtSearch.Text) ? null : _txtSearch.Text.Trim()
        };
        return true;
    }

    private async Task ClearFiltersAsync()
    {
        _txtSearch.Clear();
        _cmbMethod.SelectedIndex = 0;
        _chkUseDates.Checked = false;
        _dtpFrom.Value = DateTime.Today.AddDays(-30);
        _dtpTo.Value = DateTime.Today;
        UpdateDatePickerState();
        await RefreshAsync();
        _txtSearch.Focus();
    }

    private async Task OpenReceiptAsync()
    {
        var row = CurrentRow();
        if (row is null)
        {
            return;
        }

        var detail = await _paymentQueryService.GetDetailAsync(row.AbonoCuentaPorCobrarId);
        if (detail is null)
        {
            MessageBox.Show("El abono seleccionado ya no existe.", "Historial de abonos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshAsync();
            return;
        }

        using var receipt = new AccountPaymentReceiptForm(
            AccountPaymentReceiptViewModel.FromDetail(detail),
            canNewPayment: false);
        receipt.ShowDialog(this);
    }

    private static void ConfigureDatePicker(DateTimePicker picker, DateTime value)
    {
        picker.Dock = DockStyle.Fill;
        picker.MinimumSize = new Size(145, 0);
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "dd/MM/yyyy";
        picker.ShowCheckBox = false;
        picker.Value = value;
    }

    private static Control BuildField(string labelText, Control control, Padding? margin = null)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = margin ?? Padding.Empty
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };
        wrapper.Controls.Add(label, 0, 0);
        wrapper.Controls.Add(control, 0, 1);
        return wrapper;
    }

    private void AddColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft,
        DataGridViewContentAlignment headerAlignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment },
            HeaderCell = { Style = { Alignment = headerAlignment } }
        });
    }

    private void UpdateDatePickerState()
    {
        _dtpFrom.Enabled = _chkUseDates.Checked;
        _dtpTo.Enabled = _chkUseDates.Checked;
    }

    private void SetBusy(bool busy)
    {
        _isRefreshing = busy;
        _btnRefresh.Enabled = !busy;
        _btnClear.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void UpdateReceiptButtonState()
    {
        _btnReceipt.Enabled = CurrentRow() is not null;
    }

    private PaymentRow? CurrentRow()
    {
        return _grid.SelectedRows.Count > 0
            ? _grid.SelectedRows[0].DataBoundItem as PaymentRow
            : null;
    }

    private void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var property = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (property is not ("Codigo" or "Cliente" or "Cuenta" or "Venta" or "Usuario"))
        {
            return;
        }

        e.ToolTipText = _grid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? string.Empty;
    }

    private void PositionEmptyState(Control container)
    {
        _lblEmpty.SetBounds(
            1,
            Math.Max(1, _grid.ColumnHeadersHeight + 1),
            Math.Max(0, container.ClientSize.Width - 2),
            48);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 38;
        button.Margin = new Padding(8, 0, 0, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Padding = Padding.Empty;
        button.UseCompatibleTextRendering = false;
        button.Click += handler;
    }

    private sealed record MethodFilter(string Text, MetodoPago? Method)
    {
        public override string ToString() => Text;
    }

    private sealed record PaymentRow(
        int AbonoCuentaPorCobrarId,
        string Fecha,
        string Codigo,
        string Cliente,
        string Cuenta,
        string Venta,
        string Metodo,
        string Monto,
        string Interes,
        string Capital,
        string SaldoNuevo,
        string Usuario)
    {
        public static PaymentRow FromSummary(AccountPaymentSummary summary)
        {
            return new PaymentRow(
                summary.AbonoCuentaPorCobrarId,
                SaleReceiptViewModel.ToLocal(summary.FechaAbono).ToString("dd/MM/yyyy HH:mm"),
                summary.CodigoAbono,
                summary.Cliente,
                summary.CodigoCuenta,
                summary.NumeroVenta,
                summary.MetodoPago.ToString(),
                SaleReceiptViewModel.FormatDollars(summary.MontoCentavos),
                SaleReceiptViewModel.FormatDollars(summary.AplicadoInteresCentavos),
                SaleReceiptViewModel.FormatDollars(summary.AplicadoCapitalCentavos),
                SaleReceiptViewModel.FormatDollars(summary.SaldoNuevoCentavos),
                summary.Usuario);
        }
    }
}
