using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

/// <summary>
/// Explicit owner step for a new installation. It never displays or
/// pre-populates a password and delegates hashing/persistence to the service.
/// </summary>
public sealed class PrincipalProvisioningForm : Form
{
    private readonly IPrincipalProvisioningService _provisioning;
    private readonly TextBox _password = new();
    private readonly TextBox _confirmation = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();

    public PrincipalProvisioningForm(IPrincipalProvisioningService provisioning)
    {
        _provisioning = provisioning;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Configurar Administrador Principal";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ClientSize = new Size(560, 390);
        MinimumSize = new Size(520, 360);
        UITheme.StyleForm(this);

        ConfigurePassword(_password);
        ConfigurePassword(_confirmation);

        _save.Text = "Guardar y continuar";
        _save.Click += Save_Click;
        UITheme.StylePrimaryButton(_save);
        UITheme.EnsureButtonContentFits(_save, 190, 42, 36);
        _save.Size = _save.MinimumSize;

        _cancel.Text = "Cancelar";
        _cancel.Click += (_, _) =>
        {
            ClearSecrets();
            DialogResult = DialogResult.Cancel;
            Close();
        };
        UITheme.StyleOutlineButton(_cancel);
        UITheme.EnsureButtonContentFits(_cancel, 120, 42, 36);
        _cancel.Size = _cancel.MinimumSize;

        var intro = new Label
        {
            Text = "Esta instalación necesita que el propietario establezca la contraseña del Administrador Principal antes de continuar.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm)
        };
        UITheme.StyleStatusLabel(intro, UITheme.SecondaryColor);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor
        };
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(VisualLayout.CreateField("Contraseña del Administrador Principal", _password), 0, 0);
        fields.Controls.Add(VisualLayout.CreateField("Confirmar contraseña", _confirmation), 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm)
        };
        _cancel.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        actions.Controls.Add(_cancel);
        actions.Controls.Add(_save);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.SurfaceColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            "Configurar Administrador Principal",
            "La cuenta protegida se crea una sola vez durante el primer arranque."), 0, 0);
        root.Controls.Add(intro, 0, 1);
        root.Controls.Add(fields, 0, 2);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);

        AcceptButton = _save;
    }

    private async void Save_Click(object? sender, EventArgs e)
    {
        try
        {
            _save.Enabled = false;
            await _provisioning.ProvisionAsync(new PrincipalProvisioningRequest(
                _password.Text,
                _confirmation.Text));
            ClearSecrets();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ClearSecrets();
            MessageBox.Show(ex.Message, "Configuración inicial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _save.Enabled = true;
            }
        }
    }

    private void ClearSecrets()
    {
        _password.Clear();
        _confirmation.Clear();
    }

    private static void ConfigurePassword(TextBox textBox)
    {
        textBox.UseSystemPasswordChar = true;
        textBox.TabIndex = 0;
    }
}
