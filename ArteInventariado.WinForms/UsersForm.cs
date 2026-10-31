using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class UsersForm : Form
{
    private readonly IUserService _userService;
    private readonly ICurrentSession _currentSession;
    private readonly BindingSource _bindingSource = new();

    private readonly Button _btnNuevo = new();
    private readonly Button _btnEditar = new();
    private readonly Button _btnPassword = new();
    private readonly Button _btnActivar = new();
    private readonly Button _btnDesactivar = new();
    private readonly Button _btnRefrescar = new();
    private readonly Label _lblScope = new();
    private readonly Label _lblStatus = new();
    private readonly DataGridView _grid = new();

    private List<UserSummary> _loadedUsers = [];

    public UsersForm(IUserService userService, ICurrentSession currentSession)
    {
        _userService = userService;
        _currentSession = currentSession;

        BuildUi();
        Load += UsersForm_Load;
    }

    private async void UsersForm_Load(object? sender, EventArgs e)
    {
        await RefreshUsersAsync();
    }

    private void BuildUi()
    {
        Text = "Administracion de usuarios";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 640);
        MinimumSize = new Size(1000, 560);
        UITheme.StyleForm(this);

        ConfigureButton(_btnNuevo, "Nuevo usuario", BtnNuevo_Click, UITheme.StylePrimaryButton, 145);
        ConfigureButton(_btnEditar, "Editar usuario", BtnEditar_Click, UITheme.StyleSecondaryButton, 145);
        ConfigureButton(_btnPassword, "Cambiar contraseña", BtnPassword_Click, UITheme.StyleSecondaryButton, 190);
        ConfigureButton(_btnActivar, "Activar", BtnActivar_Click, UITheme.StyleOutlineButton, 105);
        ConfigureButton(_btnDesactivar, "Desactivar", BtnDesactivar_Click, UITheme.StyleDangerButton, 120);
        ConfigureButton(_btnRefrescar, "Actualizar", async (_, _) => await RefreshUsersAsync(), UITheme.StyleOutlineButton, 120);

        _lblScope.Dock = DockStyle.Fill;
        _lblScope.Text = GetScopeText();
        UITheme.StyleStatusLabel(_lblScope, UITheme.SecondaryColor);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _bindingSource;
        _grid.CellDoubleClick += (_, _) => BtnEditar_Click(_grid, EventArgs.Empty);
        UITheme.StyleDataGridView(_grid);

        AddGridColumn("NombreCompleto", "Nombre completo", 220, fill: true);
        AddGridColumn("NombreUsuario", "Usuario", 140);
        AddGridColumn("Rol", "Rol", 100, centered: true);
        AddGridColumn("Activo", "Estado", 90, centered: true);
        AddGridColumn("FechaCreacion", "Creado", 150);
        AddGridColumn("UltimoLogin", "Último acceso", 150);

        var actions = VisualLayout.CreateActionBar(
            _btnNuevo, _btnEditar, _btnPassword, _btnActivar, _btnDesactivar, _btnRefrescar);
        var gridHost = VisualLayout.CreateSection(
            "Usuarios con acceso al sistema",
            "Los permisos delegados se administran por separado; el administrador principal permanece protegido.",
            _grid);
        UITheme.StyleStatusLabel(_lblStatus);
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.Text = "Preparando usuarios...";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            "Usuarios",
            "Administra cuentas y estado de acceso sin exponer credenciales."), 0, 0);
        root.Controls.Add(_lblScope, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(gridHost, 0, 3);
        root.Controls.Add(_lblStatus, 0, 4);
        Controls.Add(root);
    }

    private async Task RefreshUsersAsync()
    {
        try
        {
            SetActionsEnabled(false);

            var users = (await _userService.GetAllAsync()).ToList();
            _loadedUsers = CanManageAdminUsers()
                ? users
                : users
                    .Where(u => u.Rol == RolUsuario.EMPLEADO && !IsProtectedAdminName(u.NombreUsuario))
                    .ToList();

            _lblScope.Text = GetScopeText();
            _bindingSource.DataSource = _loadedUsers
                .Select(UserGridRow.FromSummary)
                .ToList();
            _lblStatus.Text = _loadedUsers.Count == 0
                ? "No hay usuarios visibles para el alcance actual."
                : $"{_loadedUsers.Count} usuario(s) visibles.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al cargar usuarios:\n\n{ex.Message}",
                "Usuarios",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async void BtnNuevo_Click(object? sender, EventArgs e)
    {
        using var form = new UserEditForm(_userService, _currentSession);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Usuario creado.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshUsersAsync();
        }
    }

    private async void BtnEditar_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user is null)
        {
            return;
        }

        using var form = new UserEditForm(_userService, _currentSession, user);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Usuario editado.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshUsersAsync();
        }
    }

    private async void BtnPassword_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user is null)
        {
            return;
        }

        using var form = new PasswordChangeForm(_userService, _currentSession, user);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Contraseña actualizada.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshUsersAsync();
        }
    }

    private async void BtnActivar_Click(object? sender, EventArgs e)
    {
        await SetSelectedUserActiveAsync(true, "Usuario activado.");
    }

    private async void BtnDesactivar_Click(object? sender, EventArgs e)
    {
        await SetSelectedUserActiveAsync(false, "Usuario desactivado.");
    }

    private async Task SetSelectedUserActiveAsync(bool activo, string successMessage)
    {
        var user = GetSelectedUser();
        if (user is null)
        {
            return;
        }

        try
        {
            await _userService.SetActiveAsync(
                user.UsuarioId,
                activo,
                _currentSession.UsuarioActual?.UsuarioId);

            MessageBox.Show(successMessage, "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private UserSummary? GetSelectedUser()
    {
        if (_grid.CurrentRow?.DataBoundItem is not UserGridRow row)
        {
            MessageBox.Show("Seleccione un usuario.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _loadedUsers.FirstOrDefault(u => u.UsuarioId == row.UsuarioId);
    }

    private void SetActionsEnabled(bool enabled)
    {
        _btnNuevo.Enabled = enabled;
        _btnEditar.Enabled = enabled;
        _btnPassword.Enabled = enabled;
        _btnActivar.Enabled = enabled;
        _btnDesactivar.Enabled = enabled;
        _btnRefrescar.Enabled = enabled;
    }

    private string GetScopeText()
    {
        return CanManageAdminUsers()
            ? "Sesion tecnica admin: puede gestionar ADMIN y EMPLEADO. El usuario admin no se puede desactivar."
            : "ADMIN normal: solo puede ver y gestionar usuarios EMPLEADO.";
    }

    private bool CanManageAdminUsers()
    {
        return IsProtectedAdminName(_currentSession.UsuarioActual?.NombreUsuario);
    }

    private static bool IsProtectedAdminName(string? nombreUsuario)
    {
        return ProtectedAdministrator.IsUserName(nombreUsuario);
    }

    private void AddGridColumn(string propertyName, string headerText, int width, bool fill = false, bool centered = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            MinimumWidth = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
        };
        if (centered)
        {
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        _grid.Columns.Add(column);
    }

    private static void ConfigureButton(
        Button button,
        string text,
        EventHandler clickHandler,
        Action<Button> style,
        int minimumWidth)
    {
        button.Text = text;
        button.Click += clickHandler;
        style(button);
        UITheme.EnsureButtonContentFits(button, minimumWidth, UITheme.StandardControlHeight, 32);
        button.Size = button.MinimumSize;
    }

    private sealed record UserGridRow(
        int UsuarioId,
        string NombreCompleto,
        string NombreUsuario,
        string Rol,
        string Activo,
        string FechaCreacion,
        string UltimoLogin)
    {
        public static UserGridRow FromSummary(UserSummary user)
        {
            return new UserGridRow(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol.ToString(),
                user.Activo ? "Si" : "No",
                user.FechaCreacion.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                user.UltimoLogin?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty);
        }
    }
}
