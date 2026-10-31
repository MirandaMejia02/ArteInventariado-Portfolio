using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public sealed class StationConfigurationForm : Form
{
    private readonly IInstallationManagementService _managementService;
    private readonly InstallationSetupProfile _profile;
    private readonly bool _required;
    private readonly ComboBox _mode = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _confirmation = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();

    public StationConfigurationForm(
        IInstallationManagementService managementService,
        InstallationSetupProfile profile,
        bool required)
    {
        _managementService = managementService;
        _profile = profile;
        _required = required;
        BuildUi();
    }

    public bool RestartRequired { get; private set; }

    private void BuildUi()
    {
        Text = _required ? "Configuracion inicial de esta estacion" : "Identidad de la estacion";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = !_required;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(650, 530);
        UITheme.StyleForm(this);

        var title = new Label
        {
            Text = _required ? "Configure esta computadora" : "Cambiar modo de instalacion",
            Location = new Point(28, 24),
            Size = new Size(590, 42),
            Font = UITheme.TitleFont,
            ForeColor = UITheme.PrimaryColor
        };
        var intro = new Label
        {
            Text = _profile.Message,
            Location = new Point(30, 72),
            Size = new Size(585, 58),
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark
        };
        var warning = new Label
        {
            Text = BuildWarningText(),
            Location = new Point(28, 140),
            Size = new Size(590, 130),
            Padding = new Padding(14),
            Font = UITheme.NormalBoldFont,
            ForeColor = Color.FromArgb(120, 67, 10),
            BackColor = Color.FromArgb(255, 248, 225)
        };

        AddLabel("Modo de esta computadora", 30, 286);
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Location = new Point(30, 310);
        _mode.Size = new Size(260, 28);
        _mode.DataSource = Enum.GetValues<InstallationMode>();
        _mode.SelectedItem = _profile.Status.Mode switch
        {
            InstallationMode.Principal => InstallationMode.Consulta,
            InstallationMode.Consulta => InstallationMode.Principal,
            _ => InstallationMode.Principal
        };
        _mode.SelectedIndexChanged += (_, _) => UpdateConfirmationText();

        AddLabel("Contraseña actual de admin", 320, 286);
        _password.Location = new Point(320, 310);
        _password.Size = new Size(298, 28);
        _password.UseSystemPasswordChar = true;
        UITheme.StyleTextBox(_password);

        _confirmation.Location = new Point(30, 362);
        _confirmation.Size = new Size(588, 64);
        _confirmation.Font = UITheme.NormalFont;
        _confirmation.ForeColor = UITheme.TextDark;
        UpdateConfirmationText();

        _save.Text = _required ? "Guardar y reiniciar" : "Confirmar cambio";
        _save.SetBounds(352, 456, 150, 40);
        _save.Click += Save_Click;
        UITheme.StylePrimaryButton(_save);

        _cancel.Text = _required ? "Cerrar sesion" : "Cancelar";
        _cancel.SetBounds(514, 456, 104, 40);
        _cancel.DialogResult = DialogResult.Cancel;
        UITheme.StyleSecondaryButton(_cancel);

        AcceptButton = _save;
        CancelButton = _cancel;
        Controls.AddRange([title, intro, warning, _mode, _password, _confirmation, _save, _cancel]);
    }

    private string BuildWarningText()
    {
        if (_profile.Kind == InstallationSetupKind.ConfigurationRecovery)
        {
            return "La configuracion local esta danada o no es compatible. La aplicacion permanece bloqueada para escrituras. Al recuperar la estacion, el archivo anterior se conservara como respaldo tecnico.";
        }

        if (_profile.Status.Mode == InstallationMode.Consulta)
        {
            return "Esta instalacion dejara de ser una copia de consulta y podra registrar operaciones. Si otra computadora sigue siendo la fuente de verdad, los datos pueden divergir. V1 no fusiona ni sincroniza bases.";
        }

        if (_profile.Status.Mode == InstallationMode.Principal)
        {
            return "Esta computadora dejara de registrar operaciones y funcionara como copia de consulta. Ventas, caja, inventario, compras, pagos y correcciones quedaran bloqueados tras reiniciar.";
        }

        return "PRINCIPAL es la fuente de verdad operativa. CONSULTA permite revisar una copia sin crear una segunda realidad. La identidad pertenece a esta PC y no viaja dentro de la base de datos.";
    }

    private void UpdateConfirmationText()
    {
        var selected = _mode.SelectedItem is InstallationMode mode ? mode : InstallationMode.Principal;
        _confirmation.Text = selected == InstallationMode.Principal
            ? "Confirmo que esta computadora sera fuente de verdad y entiendo que V1 no sincroniza ni fusiona bases."
            : "Confirmo que esta computadora sera de consulta y que las operaciones de escritura quedaran deshabilitadas.";
    }

    private async void Save_Click(object? sender, EventArgs e)
    {
        try
        {
            _save.Enabled = false;
            var selected = (InstallationMode)_mode.SelectedItem!;
            var result = _profile.Status.IsConfigured
                ? await _managementService.ChangeModeAsync(new ChangeInstallationModeRequest(
                    selected,
                    _password.Text,
                    _confirmation.Checked))
                : await _managementService.CompleteSetupAsync(new CompleteInstallationSetupRequest(
                    selected,
                    _password.Text,
                    _confirmation.Checked));

            RestartRequired = result.RestartRequired;
            MessageBox.Show(result.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _save.Enabled = true;
            }
        }
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y),
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        });
    }
}
