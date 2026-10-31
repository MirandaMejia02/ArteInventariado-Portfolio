using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class SuppliersForm : Form
{
    private const int PageSize = 100;
    private readonly ISupplierQueryService _supplierQueryService;
    private readonly ISupplierService _supplierService;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IProductService _productService;
    private readonly IPurchaseQueryService _purchaseQueryService;
    private readonly IPurchaseReceptionService _purchaseReceptionService;
    private readonly ICurrentSession _currentSession;
    private readonly IAccountsPayableQueryService? _accountsPayableQueryService;
    private readonly IAccountsPayableService? _accountsPayableService;
    private readonly BindingSource _binding = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbTipo = new();
    private readonly ComboBox _cmbEstado = new();
    private readonly Button _btnClearSearch = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnProfile = new();
    private readonly Button _btnEdit = new();
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

    public SuppliersForm(
        ISupplierQueryService supplierQueryService,
        ISupplierService supplierService,
        IEmployeePermissionService permissionService,
        IProductService productService,
        IPurchaseQueryService purchaseQueryService,
        IPurchaseReceptionService purchaseReceptionService,
        ICurrentSession currentSession,
        IAccountsPayableQueryService? accountsPayableQueryService = null,
        IAccountsPayableService? accountsPayableService = null)
    {
        _supplierQueryService = supplierQueryService;
        _supplierService = supplierService;
        _permissionService = permissionService;
        _productService = productService;
        _purchaseQueryService = purchaseQueryService;
        _purchaseReceptionService = purchaseReceptionService;
        _currentSession = currentSession;
        _accountsPayableQueryService = accountsPayableQueryService;
        _accountsPayableService = accountsPayableService;

        BuildUi();
        _liveSearch = new LiveSearchController(
            _txtSearch,
            _btnClearSearch,
            SearchFromFirstPageAsync,
            ShowSearchError);
        Load += SuppliersForm_Load;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void SuppliersForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDORES_ACCEDER);
            await LoadPermissionsAsync();
            await RefreshSuppliersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Proveedores y artesanos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void BuildUi()
    {
        Text = "Proveedores y artesanos";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 680);
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
            Text = "Proveedores y artesanos",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = "Consulte, registre y administre proveedores, artesanos y contactos operativos.",
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
            ColumnCount = 5,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));

        UITheme.StyleInput(_txtSearch);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "Código o nombre del proveedor";
        ConfigureClearSearchButton(_btnClearSearch);

        _cmbTipo.Dock = DockStyle.Fill;
        _cmbTipo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTipo.DisplayMember = nameof(TypeOption.Name);
        _cmbTipo.ValueMember = nameof(TypeOption.Value);
        UITheme.StyleInput(_cmbTipo);
        _cmbTipo.DataSource = new List<TypeOption>
        {
            new(null, "Todos"),
            new(TipoProveedor.PROVEEDOR, "Proveedor"),
            new(TipoProveedor.ARTESANO, "Artesano"),
            new(TipoProveedor.AMBOS, "Proveedor y artesano")
        };

        _cmbEstado.Dock = DockStyle.Fill;
        _cmbEstado.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbEstado.DisplayMember = nameof(StateOption.Name);
        _cmbEstado.ValueMember = nameof(StateOption.Value);
        UITheme.StyleInput(_cmbEstado);
        _cmbEstado.DataSource = new List<StateOption>
        {
            new(null, "Todos"),
            new(EstadoProveedor.ACTIVO, "Activos"),
            new(EstadoProveedor.INACTIVO, "Inactivos")
        };
        _cmbTipo.SelectedValueChanged += Filter_SelectedValueChanged;
        _cmbEstado.SelectedValueChanged += Filter_SelectedValueChanged;

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

        AddLabel(layout, "Buscar por nombre, contacto, telefono, DUI/NIT o codigo", 0);
        AddLabel(layout, "Tipo", 1);
        AddLabel(layout, "Estado", 2);
        AddLabel(layout, string.Empty, 3);
        AddLabel(layout, string.Empty, 4);

        layout.Controls.Add(BuildSearchInput(), 0, 1);
        layout.Controls.Add(_cmbTipo, 1, 1);
        layout.Controls.Add(_cmbEstado, 2, 1);
        layout.Controls.Add(actionFilters, 3, 1);

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
        ConfigureButton(_btnEdit, "Editar", 96, async (_, _) => await EditSelectedAsync());
        UITheme.StyleSecondaryButton(_btnEdit);
        ConfigureButton(_btnProfile, "Ver perfil", 112, (_, _) => OpenProfile());
        UITheme.StyleSecondaryButton(_btnProfile);
        ConfigureButton(_btnNew, "Nuevo", 112, async (_, _) => await CreateSupplierAsync());
        UITheme.StylePrimaryButton(_btnNew);
        ConfigureButton(_btnNextPage, "Siguiente", 112, async (_, _) => await ChangePageAsync(1));
        UITheme.StyleSecondaryButton(_btnNextPage);
        ConfigureButton(_btnPreviousPage, "Anterior", 106, async (_, _) => await ChangePageAsync(-1));
        UITheme.StyleSecondaryButton(_btnPreviousPage);

        actions.Controls.AddRange([_btnToggleState, _btnEdit, _btnProfile, _btnNew, _btnNextPage, _btnPreviousPage]);
        panel.Controls.Add(_lblStatus, 0, 0);
        panel.Controls.Add(actions, 1, 0);
        return panel;
    }

    private async Task LoadPermissionsAsync()
    {
        _canCreate = await _permissionService.HasPermissionAsync(PermisoEmpleado.PROVEEDOR_CREAR);
        _canEdit = await _permissionService.HasPermissionAsync(PermisoEmpleado.PROVEEDOR_EDITAR);

        _btnNew.Visible = _canCreate;
        _btnEdit.Visible = _canEdit;
        _btnToggleState.Visible = _currentSession.EsAdmin;
    }

    private async Task RefreshSuppliersAsync(CancellationToken cancellationToken = default)
    {
        SetBusy(true, "Cargando proveedores...");
        try
        {
            var tipo = _cmbTipo.SelectedItem is TypeOption typeOption ? typeOption.Value : null;
            var estado = _cmbEstado.SelectedItem is StateOption stateOption ? stateOption.Value : null;
            var rows = await _supplierQueryService.SearchAsync(new SupplierFilter(
                _txtSearch.Text,
                tipo,
                estado,
                _currentPage,
                PageSize), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _binding.DataSource = rows.Select(SupplierGridRow.FromSummary).ToList();
            _lblStatus.Text = $"Pagina {_currentPage} - {rows.Count} proveedor(es) mostrado(s).";
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
            if (_cmbTipo.Items.Count > 0)
            {
                _cmbTipo.SelectedIndex = 0;
            }

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
            await RefreshSuppliersAsync();
        }
    }

    private async Task SearchFromFirstPageAsync(CancellationToken cancellationToken = default)
    {
        _currentPage = 1;
        await RefreshSuppliersAsync(cancellationToken);
    }

    private async Task ChangePageAsync(int delta)
    {
        _currentPage = Math.Max(1, _currentPage + delta);
        if (_liveSearch is not null)
        {
            await _liveSearch.ExecuteNowAsync(RefreshSuppliersAsync);
        }
        else
        {
            await RefreshSuppliersAsync();
        }
    }

    private async Task CreateSupplierAsync()
    {
        using var form = new SupplierEditForm(_supplierService, openSupplier: OpenProfileById);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshSuppliersAsync();
            if (form.SavedSupplier is not null)
            {
                OpenProfileById(form.SavedSupplier.ProveedorId);
            }
        }
    }

    private async Task EditSelectedAsync()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        var detail = await _supplierQueryService.GetByIdAsync(row.ProveedorId);
        if (detail is null)
        {
            MessageBox.Show("El proveedor seleccionado ya no existe.", "Proveedores", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshSuppliersAsync();
            return;
        }

        using var form = new SupplierEditForm(_supplierService, detail, OpenProfileById);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshSuppliersAsync();
        }
    }

    private void OpenProfile()
    {
        if (CurrentRow() is { } row)
        {
            OpenProfileById(row.ProveedorId);
        }
    }

    private void OpenProfileById(int proveedorId)
    {
        using var form = new SupplierProfileForm(
            proveedorId,
            _supplierQueryService,
            _supplierService,
            _permissionService,
            _productService,
            _purchaseQueryService,
            _purchaseReceptionService,
            _accountsPayableQueryService,
            _accountsPayableService);
        form.ShowDialog(this);
    }

    private async Task ToggleStateAsync()
    {
        if (CurrentRow() is not { } row)
        {
            return;
        }

        var newState = row.EstadoValue == EstadoProveedor.ACTIVO
            ? EstadoProveedor.INACTIVO
            : EstadoProveedor.ACTIVO;
        var action = newState == EstadoProveedor.ACTIVO ? "reactivar" : "desactivar";
        var motivo = PromptForText($"Motivo para {action} proveedor", "Motivo obligatorio:");
        if (string.IsNullOrWhiteSpace(motivo))
        {
            return;
        }

        try
        {
            await _supplierService.SetStateAsync(new SupplierStateChangeRequest(row.ProveedorId, newState, motivo));
            await RefreshSuppliersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Proveedores", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        AddGridColumn("CodigoProveedor", "Codigo", 105, 10);
        AddGridColumn("Tipo", "Tipo", 140, 14);
        AddGridColumn("NombreRazonSocial", "Nombre", 280, 32);
        AddGridColumn("NombreContacto", "Contacto", 170, 18);
        AddGridColumn("Telefono", "Telefono", 120, 12);
        AddGridColumn("Documento", "Documento", 135, 14);
        AddGridColumn("ProductosAsociados", "Productos", 90, 7, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Estado", "Estado", 96, 9, DataGridViewContentAlignment.MiddleCenter);
    }

    private void AddGridColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
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
        _btnToggleState.Enabled = hasSelection && _currentSession.EsAdmin;
        if (row is not null)
        {
            _btnToggleState.Text = row.EstadoValue == EstadoProveedor.ACTIVO ? "Desactivar" : "Reactivar";
        }
    }

    private SupplierGridRow? CurrentRow()
    {
        return _binding.Current as SupplierGridRow;
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

    private async void Filter_SelectedValueChanged(object? sender, EventArgs e)
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

    private static string FormatSupplierType(TipoProveedor tipo)
    {
        return tipo switch
        {
            TipoProveedor.PROVEEDOR => "Proveedor",
            TipoProveedor.ARTESANO => "Artesano",
            TipoProveedor.AMBOS => "Proveedor y artesano",
            _ => tipo.ToString()
        };
    }

    private sealed record TypeOption(TipoProveedor? Value, string Name);

    private sealed record StateOption(EstadoProveedor? Value, string Name);

    private sealed record SupplierGridRow(
        int ProveedorId,
        string CodigoProveedor,
        string Tipo,
        string NombreRazonSocial,
        string NombreContacto,
        string Telefono,
        string Documento,
        int ProductosAsociados,
        string Estado,
        EstadoProveedor EstadoValue)
    {
        public static SupplierGridRow FromSummary(SupplierSummary summary)
        {
            return new SupplierGridRow(
                summary.ProveedorId,
                summary.CodigoProveedor,
                FormatSupplierType(summary.Tipo),
                summary.NombreRazonSocial,
                summary.NombreContacto ?? string.Empty,
                summary.Telefono ?? string.Empty,
                summary.DocumentoIdentidad ?? summary.DocumentoFiscal ?? string.Empty,
                summary.ProductosAsociados,
                summary.Estado.ToString(),
                summary.Estado);
        }
    }
}
