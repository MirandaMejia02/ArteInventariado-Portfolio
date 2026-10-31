using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class UserEditForm : Form
{
    private readonly IUserService _userService;
    private readonly ICurrentSession _currentSession;
    private readonly UserSummary? _user;

    private readonly TextBox _txtNombreCompleto = new();
    private readonly TextBox _txtNombreUsuario = new();
    private readonly TextBox _txtPassword = new();
    private readonly ComboBox _cmbRol = new();
    private readonly Button _btnGuardar = new();
    private readonly Button _btnCancelar = new();

    public UserEditForm(IUserService userService, ICurrentSession currentSession, UserSummary? user = null)
    {
        _userService = userService;
        _currentSession = currentSession;
        _user = user;

        BuildUi();
        LoadValues();
    }

    private bool EsEdicion => _user is not null;

    private void BuildUi()
    {
        Text = EsEdicion ? "Editar usuario" : "Crear usuario";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, EsEdicion ? 430 : 520);
        MinimumSize = new Size(520, EsEdicion ? 410 : 490);
        UITheme.StyleForm(this);

        _txtNombreUsuario.Enabled = !EsEdicion;

        if (!EsEdicion)
        {
            _txtPassword.UseSystemPasswordChar = true;
        }

        _cmbRol.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRol.DataSource = CanManageAdminUsers()
            ? new[] { RolUsuario.ADMIN, RolUsuario.EMPLEADO }
            : new[] { RolUsuario.EMPLEADO };

        _btnGuardar.Text = "Guardar";
        _btnGuardar.Click += BtnGuardar_Click;
        UITheme.StylePrimaryButton(_btnGuardar);
        UITheme.EnsureButtonContentFits(_btnGuardar, 125, 42, 36);
        _btnGuardar.Size = _btnGuardar.MinimumSize;

        _btnCancelar.Text = "Cancelar";
        _btnCancelar.Click += (_, _) => Close();
        UITheme.StyleOutlineButton(_btnCancelar);
        UITheme.EnsureButtonContentFits(_btnCancelar, 125, 42, 36);
        _btnCancelar.Size = _btnCancelar.MinimumSize;

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = EsEdicion ? 3 : 4,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor
        };
        for (var row = 0; row < fields.RowCount; row++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        var fieldRow = 0;
        fields.Controls.Add(VisualLayout.CreateField("Nombre completo", _txtNombreCompleto), 0, fieldRow++);
        fields.Controls.Add(VisualLayout.CreateField("Nombre de usuario", _txtNombreUsuario,
            EsEdicion ? "El identificador de acceso no cambia durante la edición." : "Se utilizará para iniciar sesión."), 0, fieldRow++);
        if (!EsEdicion)
        {
            fields.Controls.Add(VisualLayout.CreateField("Contraseña del usuario", _txtPassword,
                "El usuario podrá cambiarla siguiendo las políticas actuales."), 0, fieldRow++);
        }
        fields.Controls.Add(VisualLayout.CreateField("Rol", _cmbRol,
            "Los permisos específicos se administran desde la pantalla de permisos."), 0, fieldRow);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm)
        };
        _btnCancelar.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        actions.Controls.Add(_btnCancelar);
        actions.Controls.Add(_btnGuardar);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            EsEdicion ? "Editar usuario" : "Nuevo usuario",
            EsEdicion ? "Actualiza el nombre y rol de la cuenta seleccionada." : "Crea una cuenta con el alcance permitido para tu sesión."), 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = _btnGuardar;
        CancelButton = _btnCancelar;
    }

    private void LoadValues()
    {
        if (_user is null)
        {
            _cmbRol.SelectedItem = RolUsuario.EMPLEADO;
            return;
        }

        _txtNombreCompleto.Text = _user.NombreCompleto;
        _txtNombreUsuario.Text = _user.NombreUsuario;
        _cmbRol.SelectedItem = _user.Rol;
    }

    private async void BtnGuardar_Click(object? sender, EventArgs e)
    {
        _btnGuardar.Enabled = false;

        try
        {
            var rol = _cmbRol.SelectedItem is RolUsuario selectedRol
                ? selectedRol
                : throw new InvalidOperationException("Debe seleccionar un rol.");

            var usuarioActualId = _currentSession.UsuarioActual?.UsuarioId;

            if (_user is null)
            {
                await _userService.CreateAsync(
                    new CreateUserRequest(
                        _txtNombreCompleto.Text,
                        _txtNombreUsuario.Text,
                        _txtPassword.Text,
                        rol),
                    usuarioActualId);
            }
            else
            {
                await _userService.UpdateAsync(
                    new UpdateUserRequest(_user.UsuarioId, _txtNombreCompleto.Text, rol),
                    usuarioActualId);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnGuardar.Enabled = true;
            }
        }
    }

    private bool CanManageAdminUsers()
    {
        return ProtectedAdministrator.IsUserName(_currentSession.UsuarioActual?.NombreUsuario);
    }
}
