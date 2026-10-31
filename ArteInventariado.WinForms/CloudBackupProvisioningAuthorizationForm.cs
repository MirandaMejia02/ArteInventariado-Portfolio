namespace ArteInventariado.WinForms;

/// <summary>
/// Reauthentication/confirmation step shared in spirit with the protected
/// station-identity form.  The password is held only in the textbox until the
/// caller hands it to the authorization service and is cleared on close.
/// </summary>
internal sealed class CloudBackupProvisioningAuthorizationForm : Form
{
    private readonly TextBox _password = new();
    private readonly CheckBox _confirmation = new();
    private string? _pendingPassword;

    public CloudBackupProvisioningAuthorizationForm()
    {
        Text = "Autorizar aprovisionamiento cloud";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 360);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var title = new Label
        {
            Text = "Confirme la operación del Administrador Principal",
            Location = new Point(26, 22),
            Size = new Size(568, 34),
            Font = UITheme.TitleFont,
            ForeColor = UITheme.PrimaryColor
        };
        var description = new Label
        {
            Text = "El uploader cloud modifica únicamente la configuración protegida de esta instalación. " +
                   "La credencial se introduce después en la ventana elevada y nunca se muestra ni se pasa por argumentos.",
            Location = new Point(28, 70),
            Size = new Size(564, 92),
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark
        };
        var passwordLabel = new Label
        {
            Text = "Contraseña actual del Administrador Principal",
            AutoSize = true,
            Location = new Point(28, 180),
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };
        _password.Location = new Point(28, 208);
        _password.Size = new Size(564, 30);
        _password.UseSystemPasswordChar = true;
        UITheme.StyleTextBox(_password);

        _confirmation.Text = "Confirmo conscientemente que deseo aprovisionar o reemplazar el uploader cloud de esta estación.";
        _confirmation.Location = new Point(28, 252);
        _confirmation.Size = new Size(564, 42);
        _confirmation.AutoSize = false;
        _confirmation.Font = UITheme.NormalFont;
        _confirmation.ForeColor = UITheme.TextDark;

        var cancel = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Location = new Point(348, 310),
            Size = new Size(120, 38)
        };
        var authorize = new Button
        {
            Text = "Continuar",
            Location = new Point(472, 310),
            Size = new Size(120, 38)
        };
        authorize.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_password.Text) || !_confirmation.Checked)
            {
                MessageBox.Show(
                    this,
                    "Ingrese la contraseña actual y confirme conscientemente la operación.",
                    "Autorizar aprovisionamiento cloud",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Capture before Close triggers OnFormClosed.  The visual control
            // is cleared on close, but the one-shot value remains available
            // until the caller consumes it immediately after ShowDialog().
            _pendingPassword = _password.Text;
            DialogResult = DialogResult.OK;
            Close();
        };
        UITheme.StyleSecondaryButton(cancel);
        UITheme.StylePrimaryButton(authorize);

        Controls.AddRange([title, description, passwordLabel, _password, _confirmation, cancel, authorize]);
        AcceptButton = authorize;
        CancelButton = cancel;
    }

    internal string TakePassword()
    {
        var password = _pendingPassword ?? string.Empty;
        _pendingPassword = null;
        return password;
    }

    public bool ConfirmationAccepted => _confirmation.Checked;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _password.Clear();
        if (DialogResult != DialogResult.OK)
        {
            _pendingPassword = null;
        }

        base.OnFormClosed(e);
    }
}
