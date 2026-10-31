using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class RestoreConfirmationForm : Form
{
    private readonly TextBox _password = new();

    public RestoreConfirmationForm(
        BackupManifest manifest,
        InstallationMode? destinationMode,
        string backupPath)
    {
        Text = "Restaurar respaldo";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(600, 410);
        UITheme.StyleForm(this);

        var warning = manifest.SourceInstallationMode == InstallationMode.Consulta &&
            destinationMode == InstallationMode.Principal
            ? "\r\nADVERTENCIA: este respaldo fue creado desde una instalacion de Consulta. Puede representar una copia anterior de los datos del negocio."
            : string.Empty;
        var summary = new Label
        {
            Location = new Point(24, 22),
            Size = new Size(552, 210),
            Font = UITheme.NormalFont,
            Text = $"Respaldo: {manifest.CreatedLocal:dd/MM/yyyy HH:mm}\r\n" +
                   $"Archivo: {Path.GetFileName(backupPath)}\r\n" +
                   $"Origen: {manifest.SourceInstallationMode} / {manifest.SourceInstallationId}\r\n" +
                   $"Tamano DB: {manifest.DatabaseSizeBytes / 1024d / 1024d:N2} MB\r\n\r\n" +
                   "La base de datos actual sera reemplazada. Antes de continuar se creara un respaldo preventivo." + warning
        };

        var passwordLabel = new Label
        {
            Text = "Contraseña actual",
            AutoSize = true,
            Location = new Point(24, 250),
            Font = UITheme.NormalBoldFont
        };
        _password.Location = new Point(24, 276);
        _password.Size = new Size(552, 30);
        _password.UseSystemPasswordChar = true;

        var cancel = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Location = new Point(330, 338),
            Size = new Size(110, 38)
        };
        var restore = new Button
        {
            Text = "Restaurar y reiniciar",
            DialogResult = DialogResult.OK,
            Location = new Point(448, 338),
            Size = new Size(128, 38)
        };
        UITheme.StyleDangerButton(restore);
        UITheme.StyleSecondaryButton(cancel);

        Controls.AddRange([summary, passwordLabel, _password, cancel, restore]);
        AcceptButton = restore;
        CancelButton = cancel;
    }

    public string Password => _password.Text;
}
