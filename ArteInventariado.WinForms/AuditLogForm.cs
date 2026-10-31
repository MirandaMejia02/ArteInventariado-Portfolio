using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class AuditLogForm : Form
{
    private readonly IAuditLogService _auditLogService;
    private readonly IUserService _userService;
    private readonly BindingSource _bindingSource = new();

    private readonly DateTimePicker _dtpDesde = new();
    private readonly DateTimePicker _dtpHasta = new();
    private readonly ComboBox _cmbUsuario = new();
    private readonly ComboBox _cmbTipo = new();
    private readonly TextBox _txtBuscar = new();
    private readonly Button _btnFiltrar = new();
    private readonly DataGridView _grid = new();

    public AuditLogForm(IAuditLogService auditLogService, IUserService userService)
    {
        _auditLogService = auditLogService;
        _userService = userService;

        BuildUi();
        Load += AuditLogForm_Load;
    }

    private async void AuditLogForm_Load(object? sender, EventArgs e)
    {
        await LoadFiltersAsync();
        await RefreshLogAsync();
    }

    private void BuildUi()
    {
        Text = "Bitacora";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(860, 520);
        UITheme.StyleForm(this);

        // ── TOP FILTER PANEL ─────────────────────────────────────────
        var pnlFilters = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 136,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(15, 10, 15, 10),
            ColumnCount = 5,
            RowCount = 4,
            Margin = Padding.Empty
        };

        pnlFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        pnlFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        pnlFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        pnlFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        pnlFilters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlFilters.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        pnlFilters.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        pnlFilters.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        pnlFilters.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _dtpDesde.Format = DateTimePickerFormat.Short;
        _dtpDesde.Value = DateTime.Today.AddDays(-30);

        _dtpHasta.Format = DateTimePickerFormat.Short;
        _dtpHasta.Value = DateTime.Today;

        _cmbUsuario.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbUsuario.DisplayMember = nameof(UserOption.Name);
        _cmbUsuario.ValueMember = nameof(UserOption.Id);

        _cmbTipo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTipo.DisplayMember = nameof(ActionOption.Name);
        _cmbTipo.ValueMember = nameof(ActionOption.Value);

        _txtBuscar.Name = "AuditLogSearchText";
        _txtBuscar.PlaceholderText = "Usuario, acción, entidad o descripción";
        _txtBuscar.Dock = DockStyle.Fill;
        _txtBuscar.Margin = new Padding(0, 0, 8, 0);
        _txtBuscar.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            await RefreshLogAsync();
        };

        _btnFiltrar.Text = "Filtrar";
        _btnFiltrar.Dock = DockStyle.Fill;
        _btnFiltrar.MinimumSize = new Size(100, UITheme.CompactControlHeight);
        _btnFiltrar.Margin = new Padding(8, 0, 0, 0);
        _btnFiltrar.Click += async (_, _) => await RefreshLogAsync();
        UITheme.StylePrimaryButton(_btnFiltrar);

        AddFilterField(pnlFilters, 0, "Desde", _dtpDesde);
        AddFilterField(pnlFilters, 1, "Hasta", _dtpHasta);
        AddFilterField(pnlFilters, 2, "Usuario", _cmbUsuario);
        AddFilterField(pnlFilters, 3, "Accion", _cmbTipo);
        AddFilterField(pnlFilters, 0, "Buscar", _txtBuscar, 2, 3, 4);
        pnlFilters.Controls.Add(_btnFiltrar, 4, 3);

        // ── GRID (Fill) ──────────────────────────────────────────────
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _bindingSource;
        UITheme.StyleDataGridView(_grid);

        AddGridColumn("Fecha", "Fecha/hora", 150);
        AddGridColumn("Usuario", "Usuario", 120);
        AddGridColumn("TipoAccion", "Accion", 180);
        AddGridColumn("Entidad", "Entidad", 120);
        AddGridColumn("Descripcion", "Descripcion", 500);

        Controls.AddRange([_grid, pnlFilters]);
    }

    private async Task LoadFiltersAsync()
    {
        var users = await _userService.GetAllAsync();
        var userOptions = users
            .Select(u => new UserOption(u.UsuarioId, u.NombreUsuario))
            .Prepend(new UserOption(null, "Todos"))
            .ToList();

        _cmbUsuario.DataSource = userOptions;

        var actionOptions = Enum.GetValues<TipoBitacora>()
            .Select(t => new ActionOption(t, t.ToString()))
            .Prepend(new ActionOption(null, "Todas"))
            .ToList();

        _cmbTipo.DataSource = actionOptions;
    }

    private async Task RefreshLogAsync()
    {
        try
        {
            _btnFiltrar.Enabled = false;
            var filter = new AuditLogFilter(
                DateOnly.FromDateTime(_dtpDesde.Value.Date),
                DateOnly.FromDateTime(_dtpHasta.Value.Date),
                _cmbUsuario.SelectedValue as int?,
                _cmbTipo.SelectedValue is TipoBitacora tipo ? tipo : null,
                _txtBuscar.Text);

            var entries = await _auditLogService.GetEntriesAsync(filter);
            _bindingSource.DataSource = entries.Select(AuditLogGridRow.FromEntry).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Bitacora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnFiltrar.Enabled = true;
        }
    }

    private static void AddFilterField(
        TableLayoutPanel layout,
        int column,
        string text,
        Control input,
        int labelRow = 0,
        int inputRow = 1,
        int columnSpan = 1)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0)
        };
        UITheme.StyleLabelNormal(label);
        UITheme.StyleInput(input);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 0, 8, 0);
        layout.Controls.Add(label, column, labelRow);
        layout.SetColumnSpan(label, columnSpan);
        layout.Controls.Add(input, column, inputRow);
        layout.SetColumnSpan(input, columnSpan);
    }

    private void AddGridColumn(string propertyName, string headerText, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            MinimumWidth = width,
            FillWeight = width
        });
    }

    private sealed record UserOption(int? Id, string Name);

    private sealed record ActionOption(TipoBitacora? Value, string Name);

    private sealed record AuditLogGridRow(
        string Fecha,
        string Usuario,
        string TipoAccion,
        string Entidad,
        string Descripcion)
    {
        public static AuditLogGridRow FromEntry(AuditLogEntry entry)
        {
            return new AuditLogGridRow(
                ToLocal(entry.FechaAccion).ToString("yyyy-MM-dd HH:mm:ss"),
                entry.Usuario,
                entry.TipoAccion.ToString(),
                entry.Entidad,
                entry.Descripcion);
        }

        private static DateTime ToLocal(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Local => value,
                DateTimeKind.Utc => value.ToLocalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
            };
        }
    }
}
