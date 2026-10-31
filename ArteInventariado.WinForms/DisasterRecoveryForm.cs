using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.CloudBackup;

namespace ArteInventariado.WinForms;

/// <summary>
/// Elevated, explicit H2 recovery dialog. Credentials are kept only in
/// controls/in-memory request objects for the duration of the operation and
/// are never written to station, cloud or recovery state.
/// </summary>
public sealed class DisasterRecoveryForm : Form
{
    private readonly IDisasterRecoveryBootstrapService _recovery;
    private readonly TextBox _bucket = new();
    private readonly TextBox _region = new();
    private readonly TextBox _prefix = new();
    private readonly TextBox _sourceInstallation = new();
    private readonly TextBox _accessKey = new();
    private readonly TextBox _secret = new();
    private readonly TextBox _roleArn = new();
    private readonly TextBox _mfaArn = new();
    private readonly TextBox _mfaCode = new();
    private readonly ComboBox _mode = new();
    private readonly CheckBox _ownerConfirmed = new();
    private readonly CheckBox _confirmRecovery = new();
    private readonly ListBox _catalog = new();
    private readonly Label _status = new();
    private readonly Button _load = new();
    private readonly Button _recoverButton = new();
    private readonly Button _cancel = new();
    private IReadOnlyList<CloudRecoveryCatalogItem> _items = [];

    public DisasterRecoveryForm(IDisasterRecoveryBootstrapService recovery)
    {
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        Text = "ArteInventariado - Recuperar instalación";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 620);
        ClientSize = new Size(900, 720);
        BackColor = Color.FromArgb(255, 248, 252);
        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "Recuperar instalación existente",
            Dock = DockStyle.Top,
            Height = 52,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(196, 35, 112),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var subtitle = new Label
        {
            Text = "Selecciona explícitamente la instalación remota, el backup y el modo de esta estación.",
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(100, 80, 95)
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 230,
            ColumnCount = 4,
            RowCount = 5,
            Padding = new Padding(18, 8, 18, 4)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 5; i++) fields.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        AddField(fields, "Bucket", _bucket, 0, 0, "portfolio-example-backups");
        AddField(fields, "Región", _region, 2, 0, "us-east-1");
        AddField(fields, "Prefijo", _prefix, 0, 1, "v1");
        AddField(fields, "InstallationId origen", _sourceInstallation, 2, 1, "GUID de la instalación remota");
        AddField(fields, "Access key temporal", _accessKey, 0, 2, "solo memoria");
        AddField(fields, "Secret temporal", _secret, 2, 2, "solo memoria");
        _secret.UseSystemPasswordChar = true;
        AddField(fields, "Role ARN", _roleArn, 0, 3, "arn:aws:iam::...:role/...");
        AddField(fields, "MFA ARN", _mfaArn, 2, 3, "arn:aws:iam::...:mfa/...");
        AddField(fields, "Código MFA", _mfaCode, 0, 4, "123456");
        _mfaCode.UseSystemPasswordChar = true;
        AddField(fields, "Modo estación", _mode, 2, 4, null);
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.DataSource = Enum.GetValues<InstallationMode>();
        _mode.SelectedItem = InstallationMode.Principal;

        _ownerConfirmed.Text = "Confirmo que soy el propietario y la instalación origen es la correcta.";
        _ownerConfirmed.Dock = DockStyle.Fill;
        _ownerConfirmed.Margin = new Padding(18, 4, 18, 4);
        _ownerConfirmed.AutoSize = true;

        var catalogTitle = new Label
        {
            Text = "Backups disponibles (Healthy recomendado; DegradedAssets se muestra como advertencia)",
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(18, 6, 18, 0),
            ForeColor = Color.FromArgb(117, 45, 87)
        };
        _catalog.Dock = DockStyle.Fill;
        _catalog.Margin = new Padding(18, 4, 18, 4);
        _catalog.HorizontalScrollbar = false;

        _confirmRecovery.Text = "Confirmo restaurar el backup seleccionado en esta máquina.";
        _confirmRecovery.Dock = DockStyle.Top;
        _confirmRecovery.Height = 30;
        _confirmRecovery.Margin = new Padding(18, 4, 18, 4);

        var catalogPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0, 0, 0, 0)
        };
        catalogPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        catalogPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        catalogPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        catalogPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        catalogPanel.Controls.Add(catalogTitle, 0, 0);
        catalogPanel.Controls.Add(_catalog, 0, 1);
        catalogPanel.Controls.Add(_confirmRecovery, 0, 2);
        catalogPanel.Controls.Add(_status, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(18, 8, 18, 8)
        };
        ConfigureButton(_cancel, "Cancelar", Color.White, Color.FromArgb(117, 45, 87));
        ConfigureButton(_recoverButton, "Descargar y recuperar", Color.FromArgb(76, 164, 121), Color.White);
        ConfigureButton(_load, "Cargar catálogo", Color.FromArgb(117, 45, 87), Color.White);
        actions.Controls.Add(_cancel);
        actions.Controls.Add(_recoverButton);
        actions.Controls.Add(_load);

        Controls.Add(catalogPanel);
        Controls.Add(actions);
        Controls.Add(_ownerConfirmed);
        Controls.Add(fields);
        Controls.Add(subtitle);
        Controls.Add(title);

        _load.Click += async (_, _) => await LoadCatalogAsync();
        _recoverButton.Click += async (_, _) => await RecoverAsync();
        _cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        _catalog.SelectedIndexChanged += (_, _) => _recoverButton.Enabled = _catalog.SelectedIndex >= 0;
        _recoverButton.Enabled = false;
        AcceptButton = _load;
        CancelButton = _cancel;
    }

    private async Task LoadCatalogAsync()
    {
        try
        {
            SetBusy(true, "Autenticando y consultando catálogo...");
            var request = BuildCloudRequest();
            var result = await _recovery.GetCatalogAsync(request);
            _items = result.Items;
            _catalog.Items.Clear();
            foreach (var item in _items)
            {
                _catalog.Items.Add(FormatCatalogItem(item));
            }

            var preferred = result.RecommendedHealthy ?? result.RecommendedFallback;
            if (preferred is not null)
            {
                _catalog.SelectedIndex = Array.IndexOf(_items.ToArray(), preferred);
            }

            _status.Text = result.Warnings.Count == 0
                ? $"{result.Items.Count} backup(s) disponible(s)."
                : string.Join(" ", result.Warnings);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Catálogo cloud", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private async Task RecoverAsync()
    {
        if (_catalog.SelectedIndex < 0 || _catalog.SelectedIndex >= _items.Count)
        {
            MessageBox.Show(this, "Selecciona un backup antes de continuar.", "Recuperación", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_ownerConfirmed.Checked || !_confirmRecovery.Checked)
        {
            MessageBox.Show(this, "Debes confirmar la instalación origen y la restauración.", "Recuperación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SetBusy(true, "Descargando, validando y restaurando...");
            var request = new DisasterRecoveryRequest(
                BuildCloudRequest(),
                _items[_catalog.SelectedIndex],
                (InstallationMode)_mode.SelectedItem!,
                ConfirmationAccepted: true);
            var result = await _recovery.RecoverAsync(request);
            _status.Text = result.Message;
            if (result.Outcome == DisasterRecoveryOutcome.Succeeded)
            {
                MessageBox.Show(this, result.Message, "Recuperación completada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show(this, result.Message, "Recuperación no aplicada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Recuperación", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private DisasterRecoveryCloudRequest BuildCloudRequest()
    {
        if (!Guid.TryParse(_sourceInstallation.Text.Trim(), out var source) || source == Guid.Empty)
        {
            throw new ArgumentException("InstallationId origen no es un GUID válido.");
        }

        var configuration = new CloudBackupConfiguration(
            CloudBackupConfigurationStore.CurrentSchemaVersion,
            Enabled: true,
            _bucket.Text.Trim(),
            _region.Text.Trim(),
            _prefix.Text.Trim());
        var authentication = new CloudRecoveryAuthenticationRequest(
            _accessKey.Text.Trim(),
            _secret.Text,
            _roleArn.Text.Trim(),
            _mfaArn.Text.Trim(),
            _mfaCode.Text.Trim(),
            _region.Text.Trim());
        return new DisasterRecoveryCloudRequest(configuration, authentication, source, _ownerConfirmed.Checked);
    }

    private static string FormatCatalogItem(CloudRecoveryCatalogItem item)
    {
        var status = item.ContentStatus?.ToString() ?? "Estado desconocido";
        var date = item.CreatedUtc?.ToLocalTime().ToString("g") ?? "sin fecha";
        var version = string.IsNullOrWhiteSpace(item.VersionId) ? "sin versión" : item.VersionId;
        return $"{status} | {date} | Backup {item.BackupId:N} | {item.SizeBytes:N0} bytes | v{version}";
    }

    private void SetBusy(bool busy, string? status)
    {
        _load.Enabled = !busy;
        _recoverButton.Enabled = !busy && _catalog.SelectedIndex >= 0;
        _cancel.Enabled = !busy;
        _status.Text = status ?? string.Empty;
        UseWaitCursor = busy;
    }

    private static void AddField(TableLayoutPanel panel, string labelText, Control control, int column, int row, string? placeholder)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 8, 0),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 45, 65)
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(2, 2, 8, 2);
        if (control is TextBox textBox && !string.IsNullOrWhiteSpace(placeholder))
        {
            textBox.PlaceholderText = placeholder;
        }

        panel.Controls.Add(label, column, row);
        panel.Controls.Add(control, column + 1, row);
    }

    private static void ConfigureButton(Button button, string text, Color backColor, Color foreColor)
    {
        button.Text = text;
        button.AutoSize = true;
        button.MinimumSize = new Size(150, 34);
        button.Margin = new Padding(5, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
    }
}
