using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class CustomersForm : Form
{
    private const int PageSize = 100;
    private readonly ICustomerQueryService _customerQueryService;
    private readonly ICustomerService _customerService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly ICurrentSession _currentSession;
    private readonly IServiceProvider _serviceProvider;
    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbEstado = new();
    private readonly Button _btnClearSearch = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnProfile = new();
    private readonly Button _btnEdit = new();
    private readonly Button _btnNewSale = new();
    private readonly Button _btnToggleState = new();
    private readonly Button _btnPreviousPage = new();
    private readonly Button _btnNextPage = new();
    private readonly Label _lblStatus = new();
    private readonly DataGridView _grid = new();

    private bool _canCreate;
    private bool _canEdit;
    private int _currentPage = 1;
    private LiveSearchController? _liveSearch;
    private bool _suppressFilterRefresh;

    public CustomersForm(
        ICustomerQueryService customerQueryService,
        ICustomerService customerService,
        IEmployeePermissionService permissionService,
        ICurrentSession currentSession,
        IServiceProvider serviceProvider)
    {
        _customerQueryService = customerQueryService;
        _customerService = customerService;
        _permissionService = permissionService;
        _currentSession = currentSession;
        _serviceProvider = serviceProvider;

        BuildUi();
        _liveSearch = new LiveSearchController(
            _txtSearch,
            _btnClearSearch,
            SearchFromFirstPageAsync,
            ShowSearchError);
        Load += CustomersForm_Load;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void CustomersForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER);
            await LoadPermissionsAsync();
            await RefreshCustomersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Clientes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Clientes";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 680);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildFilters(), 0, 1);
        root.Controls.Add(BuildGridSection(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 12, 18, 12),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Clientes",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Consulte, registre y administre la informacion de los clientes.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = UITheme.MutedTextColor,
            Font = UITheme.NormalBoldFont,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0)
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildFilters()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 14)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 162));

        UITheme.StyleInput(_txtSearch);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "Código, nombre, teléfono o DUI";
        ConfigureClearSearchButton(_btnClearSearch);

        _cmbEstado.Dock = DockStyle.Fill;
        _cmbEstado.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbEstado.DisplayMember = nameof(StateOption.Name);
        _cmbEstado.ValueMember = nameof(StateOption.Value);
        UITheme.StyleInput(_cmbEstado);
        _cmbEstado.DataSource = new List<StateOption>
        {
            new(null, "Todos"),
            new(EstadoCliente.ACTIVO, "Activos"),
            new(EstadoCliente.INACTIVO, "Inactivos")
        };
        _cmbEstado.SelectedValueChanged += async (_, _) =>
        {
            if (!_suppressFilterRefresh && _liveSearch is not null)
            {
                await _liveSearch.SearchNowAsync();
            }
        };

        ConfigureButton(_btnClear, "Limpiar", 104, async (_, _) => await ClearFiltersAsync());
        UITheme.StyleSecondaryButton(_btnClear);

        var actionFilters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(10, 4, 0, 0)
        };
        actionFilters.Controls.Add(_btnClear);

        AddLabel(layout, "Buscar por nombre, telefono, DUI o codigo", 0);
        AddLabel(layout, "Estado", 1);
        AddLabel(layout, string.Empty, 2);
        AddLabel(layout, string.Empty, 3);

        layout.Controls.Add(BuildSearchInput(), 0, 1);
        layout.Controls.Add(_cmbEstado, 1, 1);
        layout.Controls.Add(actionFilters, 2, 1);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildGridSection()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };

        ConfigureGrid();
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateActionState();
        _grid.CellDoubleClick += (_, _) => OpenProfile();

        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        UITheme.StyleStatusLabel(_lblStatus);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true
        };

        ConfigureButton(_btnToggleState, "Desactivar", 122, async (_, _) => await ToggleStateAsync());
        UITheme.StyleDangerButton(_btnToggleState);
        ConfigureButton(_btnNewSale, "Nueva venta", 126, async (_, _) => await OpenNewSaleAsync());
        UITheme.StylePrimaryButton(_btnNewSale);
        ConfigureButton(_btnEdit, "Editar", 96, async (_, _) => await EditSelectedAsync());
        UITheme.StyleSecondaryButton(_btnEdit);
        ConfigureButton(_btnProfile, "Ver perfil", 112, (_, _) => OpenProfile());
        UITheme.StyleSecondaryButton(_btnProfile);
        ConfigureButton(_btnNew, "Nuevo cliente", 138, async (_, _) => await CreateCustomerAsync());
        UITheme.StylePrimaryButton(_btnNew);
        ConfigureButton(_btnNextPage, "Siguiente", 112, async (_, _) => await ChangePageAsync(1));
        UITheme.StyleSecondaryButton(_btnNextPage);
        ConfigureButton(_btnPreviousPage, "Anterior", 106, async (_, _) => await ChangePageAsync(-1));
        UITheme.StyleSecondaryButton(_btnPreviousPage);

        actions.Controls.AddRange([_btnToggleState, _btnNewSale, _btnEdit, _btnProfile, _btnNew, _btnNextPage, _btnPreviousPage]);
        panel.Controls.Add(_lblStatus, 0, 0);
        panel.Controls.Add(actions, 1, 0);
        return panel;
    }

    private async Task LoadPermissionsAsync()
    {
        _canCreate = await _permissionService.HasPermissionAsync(PermisoEmpleado.CLIENTE_CREAR);
        _canEdit = await _permissionService.HasPermissionAsync(PermisoEmpleado.CLIENTE_EDITAR);

        _btnNew.Visible = _canCreate;
        _btnEdit.Visible = _canEdit;
        _btnToggleState.Visible = _currentSession.EsAdmin;
    }

    private async Task RefreshCustomersAsync(CancellationToken cancellationToken = default)
    {
        SetBusy(true, "Cargando clientes...");
        try
        {
            var state = _cmbEstado.SelectedItem is StateOption option ? option.Value : null;
            var rows = await _customerQueryService.SearchAsync(
                new CustomerFilter(_txtSearch.Text, state, _currentPage, PageSize),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _binding.DataSource = rows.Select(CustomerGridRow.FromSummary).ToList();
            _lblStatus.Text = $"Pagina {_currentPage} - {rows.Count} cliente(s) mostrados.";
            _btnPreviousPage.Enabled = _currentPage > 1;
            _btnNextPage.Enabled = rows.Count == PageSize;
            UpdateActionState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ClearFiltersAsync()
    {
        _currentPage = 1;
        _suppressFilterRefresh = true;
        try
        {
            _txtSearch.Clear();
            if (_cmbEstado.Items.Count > 0)
            {
                _cmbEstado.SelectedIndex = 0;
            }
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
            await RefreshCustomersAsync();
        }
    }

    private async Task SearchFromFirstPageAsync(CancellationToken cancellationToken = default)
    {
        _currentPage = 1;
        await RefreshCustomersAsync(cancellationToken);
    }

    private async Task ChangePageAsync(int delta)
    {
        _currentPage = Math.Max(1, _currentPage + delta);
        if (_liveSearch is not null)
        {
            await _liveSearch.ExecuteNowAsync(RefreshCustomersAsync);
        }
        else
        {
            await RefreshCustomersAsync();
        }
    }

    private async Task CreateCustomerAsync()
    {
        using var form = new CustomerEditForm(_customerService, openCustomer: OpenProfileById);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshCustomersAsync();
            if (form.SavedCustomer is not null)
            {
                OpenProfileById(form.SavedCustomer.ClienteId);
            }
        }
    }

    private async Task EditSelectedAsync()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        var detail = await _customerQueryService.GetByIdAsync(row.ClienteId);
        if (detail is null)
        {
            MessageBox.Show("El cliente seleccionado ya no existe.", "Clientes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshCustomersAsync();
            return;
        }

        using var form = new CustomerEditForm(_customerService, detail, OpenProfileById);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshCustomersAsync();
        }
    }

    private void OpenProfile()
    {
        if (CurrentRow() is { } row)
        {
            OpenProfileById(row.ClienteId);
        }
    }

    private void OpenProfileById(int clienteId)
    {
        var creditService = (ICreditService)_serviceProvider.GetService(typeof(ICreditService))!;
        var accountPaymentService = (IAccountPaymentService)_serviceProvider.GetService(typeof(IAccountPaymentService))!;
        var accountPaymentQueryService = (IAccountPaymentQueryService)_serviceProvider.GetService(typeof(IAccountPaymentQueryService))!;
        using var form = new CustomerProfileForm(
            clienteId,
            _customerQueryService,
            _customerService,
            creditService,
            _permissionService,
            _currentSession,
            _serviceProvider,
            accountPaymentService,
            accountPaymentQueryService);
        form.ShowDialog(this);
    }

    private async Task OpenNewSaleAsync()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        var customer = await _customerQueryService.GetSaleSelectionAsync(row.ClienteId);
        if (customer is null)
        {
            MessageBox.Show("El cliente seleccionado ya no existe.", "Clientes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshCustomersAsync();
            return;
        }

        if (!customer.Activo)
        {
            MessageBox.Show("No se puede iniciar una venta con un cliente inactivo.", "Clientes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = (EmployeePOSForm)_serviceProvider.GetService(typeof(EmployeePOSForm))!;
        form.SetSelectedCustomer(customer);
        form.ShowDialog(this);
        await RefreshCustomersAsync();
    }

    private async Task ToggleStateAsync()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        var newState = row.EstadoValue == EstadoCliente.ACTIVO
            ? EstadoCliente.INACTIVO
            : EstadoCliente.ACTIVO;
        var action = newState == EstadoCliente.ACTIVO ? "reactivar" : "desactivar";
        var motivo = PromptForText($"Motivo para {action} cliente", "Motivo obligatorio:");
        if (string.IsNullOrWhiteSpace(motivo))
        {
            return;
        }

        try
        {
            await _customerService.SetStateAsync(new CustomerStateChangeRequest(row.ClienteId, newState, motivo));
            await RefreshCustomersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Clientes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.ShowCellToolTips = true;
        _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;

        AddGridColumn("CodigoCliente", "Codigo", 105, 11);
        AddGridColumn("NombreCompleto", "Nombre completo", 280, 36);
        AddGridColumn("TelefonoCelular", "Telefono", 130, 14);
        AddGridColumn("DocumentoIdentidad", "DUI", 130, 14);
        AddGridColumn("Estado", "Estado", 96, 9, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("UltimaCompra", "Ultima compra", 140, 13, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn(
            "TotalCompras",
            "Compras",
            110,
            8,
            DataGridViewContentAlignment.MiddleCenter,
            DataGridViewContentAlignment.MiddleCenter,
            new Padding(8, 0, 8, 0));
    }

    private void AddGridColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft,
        DataGridViewContentAlignment headerAlignment = DataGridViewContentAlignment.MiddleLeft,
        Padding? cellPadding = null)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = alignment,
                Padding = cellPadding ?? Padding.Empty
            },
            HeaderCell = { Style = { Alignment = headerAlignment } }
        });
    }

    private void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var value = _grid[e.ColumnIndex, e.RowIndex].Value?.ToString();
        e.ToolTipText = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    private void UpdateActionState()
    {
        var row = CurrentRow();
        var hasSelection = row is not null;
        _btnProfile.Enabled = hasSelection;
        _btnEdit.Enabled = hasSelection && _canEdit;
        _btnNewSale.Enabled = hasSelection && row!.EstadoValue == EstadoCliente.ACTIVO;
        _btnToggleState.Enabled = hasSelection && _currentSession.EsAdmin;
        if (row is not null)
        {
            _btnToggleState.Text = row.EstadoValue == EstadoCliente.ACTIVO ? "Desactivar" : "Reactivar";
        }
    }

    private CustomerGridRow? CurrentRow()
    {
        return _binding.Current as CustomerGridRow;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _btnClear.Enabled = !busy;
        _grid.Enabled = !busy;
        if (status is not null)
        {
            _lblStatus.Text = status;
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
        UITheme.StyleOutlineButton(button);
    }

    private void ShowSearchError(Exception ex)
    {
        if (!IsDisposed)
        {
            _lblStatus.ForeColor = UITheme.DangerColor;
            _lblStatus.Text = $"No se pudo actualizar la búsqueda: {ex.Message}";
        }
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int column)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 10, 0)
        };
        layout.Controls.Add(label, column, 0);
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private static string? PromptForText(string title, string labelText)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(460, 180),
            MinimumSize = new Size(420, 170),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        UITheme.StyleForm(form);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var label = new Label { Text = labelText, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont };
        var textBox = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        UITheme.StyleTextBox(textBox);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        var ok = new Button { Text = "Aceptar", Width = 100, Height = 34, DialogResult = DialogResult.OK };
        UITheme.StylePrimaryButton(ok);
        var cancel = new Button { Text = "Cancelar", Width = 100, Height = 34, DialogResult = DialogResult.Cancel };
        UITheme.StyleSecondaryButton(cancel);
        actions.Controls.AddRange([cancel, ok]);

        root.Controls.Add(label, 0, 0);
        root.Controls.Add(textBox, 0, 1);
        root.Controls.Add(actions, 0, 2);
        form.Controls.Add(root);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
    }

    private sealed record StateOption(EstadoCliente? Value, string Name);

    private sealed record CustomerGridRow(
        int ClienteId,
        string CodigoCliente,
        string NombreCompleto,
        string TelefonoCelular,
        string DocumentoIdentidad,
        string Estado,
        EstadoCliente EstadoValue,
        string UltimaCompra,
        int TotalCompras)
    {
        public static CustomerGridRow FromSummary(CustomerSummary summary)
        {
            return new CustomerGridRow(
                summary.ClienteId,
                summary.CodigoCliente,
                summary.NombreCompleto,
                summary.TelefonoCelular ?? string.Empty,
                summary.DocumentoIdentidad ?? string.Empty,
                summary.Estado.ToString(),
                summary.Estado,
                summary.UltimaCompra.HasValue
                    ? SaleReceiptViewModel.ToLocal(summary.UltimaCompra.Value).ToString("yyyy-MM-dd")
                    : string.Empty,
                summary.TotalCompras);
        }
    }
}
