using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class PasswordChangeForm : Form
{
    private readonly IUserService _userService;
    private readonly ICurrentSession _currentSession;
    private readonly UserSummary _user;

    private readonly TextBox _txtPassword = new();
    private readonly TextBox _txtConfirmacion = new();
    private readonly Button _btnGuardar = new();
    private readonly Button _btnCancelar = new();

    public PasswordChangeForm(IUserService userService, ICurrentSession currentSession, UserSummary user)
    {
        _userService = userService;
        _currentSession = currentSession;
        _user = user;

        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Cambiar contraseña";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 400);
        MinimumSize = new Size(480, 370);
        UITheme.StyleForm(this);

        var lblUsuario = new Label
        {
            Text = $"Usuario: {_user.NombreUsuario}",
            Dock = DockStyle.Fill
        };
        UITheme.StyleStatusLabel(lblUsuario, UITheme.SecondaryColor);

        _txtPassword.UseSystemPasswordChar = true;

        _txtConfirmacion.UseSystemPasswordChar = true;

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
            RowCount = 3,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor
        };
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(lblUsuario, 0, 0);
        fields.Controls.Add(VisualLayout.CreateField("Nueva contraseña", _txtPassword), 0, 1);
        fields.Controls.Add(VisualLayout.CreateField("Confirmar contraseña", _txtConfirmacion), 0, 2);

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
            "Cambiar contraseña",
            "Asigna una nueva contraseña a la cuenta seleccionada."), 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = _btnGuardar;
        CancelButton = _btnCancelar;
    }

    private async void BtnGuardar_Click(object? sender, EventArgs e)
    {
        if (_txtPassword.Text != _txtConfirmacion.Text)
        {
            MessageBox.Show("Las contraseñas no coinciden.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnGuardar.Enabled = false;

        try
        {
            await _userService.ChangePasswordAsync(
                _user.UsuarioId,
                _txtPassword.Text,
                _currentSession.UsuarioActual?.UsuarioId);

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

}
