using ArteInventariado.Application.Interfaces;
using ArteInventariado.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.WinForms;

public class LoginForm : Form
{
    private readonly IAuthService _authService;
    private readonly IBackupMaintenanceService _backupMaintenance;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly TextBox _txtUsuario = new();
    private readonly TextBox _txtPassword = new();
    private readonly Button _btnLogin = new();
    private readonly Label _lblMensaje = new();
    private readonly Label _lblVersion = new();

    public LoginForm(
        IAuthService authService,
        IBackupMaintenanceService backupMaintenance,
        IServiceScopeFactory scopeFactory)
    {
        _authService = authService;
        _backupMaintenance = backupMaintenance;
        _scopeFactory = scopeFactory;

        BuildUi();
        Shown += LoginForm_Shown;
    }

    private async void LoginForm_Shown(object? sender, EventArgs e)
    {
        SetLoginEnabled(false);
        try
        {
            _lblMensaje.ForeColor = UITheme.MutedTextColor;
            _lblMensaje.Text = "Verificando continuidad de datos...";
            var result = await _backupMaintenance.RunAutomaticBackupIfDueAsync();
            if (result is not null)
            {
                _lblMensaje.ForeColor = UITheme.MutedTextColor;
                _lblMensaje.Text = $"Respaldo automatico actualizado: {Path.GetFileName(result.FilePath)}";
            }
            else
            {
                _lblMensaje.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("automatic-backup", ex);
            _lblMensaje.ForeColor = UITheme.DangerColor;
            _lblMensaje.Text = "No se pudo crear el respaldo automatico. Puede continuar e intentarlo desde Configuracion.";
        }
        finally
        {
            SetLoginEnabled(true);
            if (!IsDisposed)
            {
                _txtUsuario.Focus();
            }
        }
    }

    private void BuildUi()
    {
        Text = "ArteInventariado - Inicio de sesion";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 560);

        UITheme.StyleForm(this);

        var logo = UITheme.CreateLogoPictureBox(150);
        logo.Location = new Point(185, 20);

        var lblTitulo = new Label
        {
            Text = UITheme.BusinessName,
            AutoSize = true,
            Location = new Point(40, 180)
        };
        UITheme.StyleLabelTitle(lblTitulo);

        var lblSubtitle = new Label
        {
            Text = "Inicie sesion para continuar",
            AutoSize = true,
            Location = new Point(40, 225)
        };
        UITheme.StyleLabelNormal(lblSubtitle);
        lblSubtitle.ForeColor = UITheme.MutedTextColor;

        var lblUsuario = new Label
        {
            Text = "Usuario",
            AutoSize = true,
            Location = new Point(40, 280)
        };
        UITheme.StyleLabelNormal(lblUsuario);

        _txtUsuario.Location = new Point(40, 305);
        _txtUsuario.Size = new Size(440, 32);
        _txtUsuario.TabIndex = 0;
        UITheme.StyleTextBox(_txtUsuario);
        _txtUsuario.PlaceholderText = "Ingrese su usuario";

        var lblPassword = new Label
        {
            Text = "Contraseña",
            AutoSize = true,
            Location = new Point(40, 350)
        };
        UITheme.StyleLabelNormal(lblPassword);

        _txtPassword.Location = new Point(40, 375);
        _txtPassword.Size = new Size(440, 32);
        _txtPassword.UseSystemPasswordChar = true;
        _txtPassword.TabIndex = 1;
        UITheme.StyleTextBox(_txtPassword);
        _txtPassword.PlaceholderText = "Ingrese su contraseña";

        _btnLogin.Text = "Iniciar sesion";
        _btnLogin.Location = new Point(40, 435);
        _btnLogin.Size = new Size(440, 45);
        _btnLogin.TabIndex = 2;
        _btnLogin.Click += BtnLogin_Click;
        UITheme.StylePrimaryButton(_btnLogin);

        _lblMensaje.AutoSize = false;
        _lblMensaje.ForeColor = UITheme.DangerColor;
        _lblMensaje.Font = UITheme.NormalFont;
        _lblMensaje.Location = new Point(40, 492);
        _lblMensaje.Size = new Size(440, 38);
        _lblMensaje.TextAlign = ContentAlignment.MiddleCenter;

        _lblVersion.Name = "lblApplicationVersion";
        _lblVersion.AutoSize = false;
        _lblVersion.Font = UITheme.CaptionFont;
        _lblVersion.ForeColor = UITheme.MutedTextColor;
        _lblVersion.Location = new Point(40, 535);
        _lblVersion.Size = new Size(440, 18);
        _lblVersion.TextAlign = ContentAlignment.MiddleCenter;
        _lblVersion.Text = ApplicationVersion.LoginText;

        AcceptButton = _btnLogin;

        Controls.AddRange([
            logo,
            lblTitulo,
            lblSubtitle,
            lblUsuario,
            _txtUsuario,
            lblPassword,
            _txtPassword,
            _btnLogin,
            _lblMensaje,
            _lblVersion
        ]);
    }

    private async void BtnLogin_Click(object? sender, EventArgs e)
    {
        await LoginAsync();
    }

    private async Task LoginAsync()
    {
        SetLoginEnabled(false);
        _lblMensaje.Text = string.Empty;

        try
        {
            var result = await _authService.LoginAsync(_txtUsuario.Text, _txtPassword.Text);

            if (!result.Exitoso || result.Usuario is null)
            {
                _lblMensaje.Text = result.Mensaje;
                return;
            }

            Hide();

            using var dashboardScope = _scopeFactory.CreateScope();
            using var dashboard = ResolveDashboard(result.Usuario.Rol, dashboardScope.ServiceProvider);
            var dialogResult = dashboard.ShowDialog(this);

            if (dialogResult == DialogResult.Retry)
            {
                _txtPassword.Clear();
                _lblMensaje.Text = string.Empty;
                Show();
                _txtUsuario.Focus();
                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            TechnicalErrorLogger.Log("login", ex);
            _lblMensaje.Text = "No fue posible iniciar sesion. Intente nuevamente.";
        }
        finally
        {
            if (!IsDisposed)
            {
                SetLoginEnabled(true);
            }
        }
    }

    private static Form ResolveDashboard(RolUsuario rol, IServiceProvider serviceProvider)
    {
        return rol switch
        {
            RolUsuario.ADMIN => serviceProvider.GetRequiredService<AdminDashboardForm>(),
            RolUsuario.EMPLEADO => serviceProvider.GetRequiredService<EmployeeDashboardForm>(),
            _ => throw new InvalidOperationException("El rol del usuario no es valido.")
        };
    }

    private void SetLoginEnabled(bool enabled)
    {
        _txtUsuario.Enabled = enabled;
        _txtPassword.Enabled = enabled;
        _btnLogin.Enabled = enabled;
    }
}
