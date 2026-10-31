using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

/// <summary>
/// Explicit owner flow for provisioning the machine-wide upload-only cloud
/// transport.  The form runs in the UAC-elevated continuation, keeps the
/// credential only in memory, and delegates all persistence to the DPAPI
/// provisioning service.
/// </summary>
public sealed class CloudBackupProvisioningForm : Form
{
    private readonly CloudBackupProvisioningService _provisioning;
    private readonly CloudBackupConfigurationStore _configurationStore;
    private readonly CloudBackupProvisioningAuthorizationGrant? _authorizationGrant;
    private readonly TextBox _bucket = new();
    private readonly TextBox _region = new();
    private readonly TextBox _prefix = new();
    private readonly TextBox _accessKey = new();
    private readonly TextBox _secret = new();
    private readonly Button _validate = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();
    private readonly Label _status = new();
    private bool _identityValidated;

    internal CloudBackupProvisioningForm(
        CloudBackupProvisioningService provisioning,
        CloudBackupConfigurationStore configurationStore,
        CloudBackupProvisioningAuthorizationGrant authorizationGrant)
        : this(provisioning, configurationStore, authorizationGrant, loadExistingOnShow: true)
    {
    }

    internal CloudBackupProvisioningForm(
        CloudBackupProvisioningService provisioning,
        CloudBackupConfigurationStore configurationStore,
        bool loadExistingOnShow)
        : this(provisioning, configurationStore, authorizationGrant: null, loadExistingOnShow)
    {
    }

    private CloudBackupProvisioningForm(
        CloudBackupProvisioningService provisioning,
        CloudBackupConfigurationStore configurationStore,
        CloudBackupProvisioningAuthorizationGrant? authorizationGrant,
        bool loadExistingOnShow)
    {
        // The layout smoke harness intentionally passes null dependencies and
        // never invokes the action handlers.  The production constructor is
        // resolved by DI, so both services are non-null when the form can be
        // used to validate or persist a credential.
        _provisioning = provisioning!;
        _configurationStore = configurationStore!;
        _authorizationGrant = authorizationGrant;

        BuildUi();
        if (loadExistingOnShow)
        {
            Load += (_, _) => LoadExistingConfiguration();
        }
    }

    public bool Provisioned { get; private set; }

    private void BuildUi()
    {
        Text = "Aprovisionar uploader cloud";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(700, 520);
        UITheme.StyleForm(this);

        var header = VisualLayout.CreatePageHeader(
            "Aprovisionar respaldos cloud",
            "Credencial IAM upload-only introducida localmente y cifrada con DPAPI.");

        var warning = new Label
        {
            Text = "Valida el destino local y la identidad mediante STS; no usa el login web de AWS ni sube " +
                   "archivos de prueba. PutObject se confirma con la primera subida real de un .aibak; el " +
                   "backup local no depende de AWS. El prefijo es base (por ejemplo v1, sin / final); el " +
                   "runner añade /<installationId>.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm),
            Font = UITheme.NormalFont,
            ForeColor = Color.FromArgb(120, 67, 10),
            BackColor = Color.FromArgb(255, 248, 225)
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < fields.RowCount; row++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        }

        AddField(fields, "Bucket S3", _bucket, 0, "arteinventariado-backup-lab-...");
        AddField(fields, "Región AWS", _region, 1, "us-east-1");
        AddField(fields, "Prefijo base S3", _prefix, 2, "v1 (sin / final)");
        AddField(fields, "Access Key ID", _accessKey, 3, "se introduce solo aquí");
        AddField(fields, "Secret Access Key", _secret, 4, "se introduce solo aquí");
        _secret.UseSystemPasswordChar = true;
        _accessKey.UseSystemPasswordChar = true;
        foreach (var textBox in new[] { _bucket, _region, _prefix, _accessKey, _secret })
        {
            textBox.TextChanged += (_, _) => InvalidateRemoteCheck();
        }

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(UITheme.SpaceLg, 0, UITheme.SpaceLg, 0),
            BackColor = UITheme.SurfaceColor
        };
        _status.Dock = DockStyle.Fill;
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = UITheme.NormalFont;
        _status.ForeColor = UITheme.MutedTextColor;
        statusPanel.Controls.Add(_status, 0, 0);

        ConfigureButton(_validate, "Validar cuenta (sin upload)", primary: false);
        _validate.Name = "btnCloudValidate";
        _validate.Click += Validate_Click;
        ConfigureButton(_save, "Guardar y activar", primary: true);
        _save.Name = "btnCloudSave";
        _save.Enabled = false;
        _save.Click += Save_Click;
        ConfigureButton(_cancel, "Cancelar", primary: false);
        _cancel.Name = "btnCloudCancel";
        _cancel.Click += (_, _) =>
        {
            ClearSecrets();
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm),
            BackColor = UITheme.SurfaceColor
        };
        actions.Controls.Add(_cancel);
        actions.Controls.Add(_save);
        actions.Controls.Add(_validate);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UITheme.SurfaceColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(warning, 0, 1);
        root.Controls.Add(fields, 0, 2);
        root.Controls.Add(statusPanel, 0, 3);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);

        AcceptButton = _validate;
        CancelButton = _cancel;
    }

    private void LoadExistingConfiguration()
    {
        try
        {
            var configuration = _configurationStore.LoadOptional();
            if (configuration is { Enabled: true })
            {
                _bucket.Text = configuration.BucketName;
                _region.Text = configuration.RegionSystemName;
                _prefix.Text = configuration.KeyPrefix;
                _status.Text = "Existe una configuración cloud. La credencial nunca se muestra; " +
                                "introduce una nueva para validarla o rotarla.";
            }
            else
            {
                _status.Text = "No hay una configuración cloud activa en esta instalación.";
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _status.Text = "La configuración existente no pudo leerse; se requerirá una nueva validación.";
        }
    }

    private async void Validate_Click(object? sender, EventArgs e)
    {
        try
        {
            EnsureAuthorizedContinuation();
            SetBusy(true, "Validando destino local e identidad AWS sin subir archivos...");
            var check = await _provisioning.ValidateAsync(
                BuildConfiguration(),
                BuildCredential(),
                _authorizationGrant!);
            _identityValidated = true;
            _save.Enabled = true;
            _status.Text = $"Cuenta AWS validada: {check.Identity.AccountId}. " +
                            "El formato local de bucket, región y prefijo es válido. " +
                            check.WritePermissionNote;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _identityValidated = false;
            _save.Enabled = false;
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Validación cloud", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        try
        {
            EnsureAuthorizedContinuation();
        }
        catch (UnauthorizedAccessException ex)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Aprovisionamiento cloud", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_identityValidated)
        {
            MessageBox.Show(
                this,
                "Valida primero la cuenta AWS con el botón de preflight.",
                "Aprovisionamiento cloud",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true, "Guardando configuración y credencial con DPAPI machine-scope...");
            _provisioning.Provision(BuildConfiguration(), BuildCredential(), _authorizationGrant!);
            Provisioned = true;
            _status.Text = "Configuración cloud activada. La tarea SYSTEM usará el prefijo de esta instalación.";
            MessageBox.Show(this, _status.Text, "Aprovisionamiento cloud", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ClearSecrets();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Aprovisionamiento cloud", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false, _status.Text);
            }
        }
    }

    private CloudBackupConfiguration BuildConfiguration() =>
        new(
            CloudBackupConfigurationStore.CurrentSchemaVersion,
            Enabled: true,
            _bucket.Text.Trim(),
            _region.Text.Trim(),
            _prefix.Text.Trim());

    private CloudBackupCredential BuildCredential() =>
        new(_accessKey.Text.Trim(), _secret.Text);

    private void InvalidateRemoteCheck()
    {
        _identityValidated = false;
        _save.Enabled = false;
    }

    private void SetBusy(bool busy, string? status)
    {
        _validate.Enabled = !busy;
        _save.Enabled = !busy && _identityValidated;
        _cancel.Enabled = !busy;
        _status.Text = status ?? string.Empty;
        UseWaitCursor = busy;
    }

    private void ClearSecrets()
    {
        _accessKey.Clear();
        _secret.Clear();
    }

    private void EnsureAuthorizedContinuation()
    {
        if (_authorizationGrant is null)
        {
            throw new UnauthorizedAccessException(
                "Esta ventana solo puede abrirse desde una continuación autorizada del Administrador Principal.");
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ClearSecrets();
        base.OnFormClosed(e);
    }

    private static void AddField(
        TableLayoutPanel panel,
        string labelText,
        TextBox textBox,
        int row,
        string placeholder)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Padding = new Padding(0, 0, 8, 0)
        };
        textBox.Dock = DockStyle.Fill;
        textBox.PlaceholderText = placeholder;
        UITheme.StyleTextBox(textBox);
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(textBox, 1, row);
    }

    private static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.AutoSize = true;
        button.MinimumSize = new Size(primary ? 170 : 190, 40);
        button.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        if (primary)
        {
            UITheme.StylePrimaryButton(button);
        }
        else
        {
            UITheme.StyleSecondaryButton(button);
        }

        UITheme.EnsureButtonContentFits(button, button.MinimumSize.Width, 40);
    }
}
