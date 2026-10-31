using System.Diagnostics;
using System.Security.Cryptography;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public sealed class BackupRestoreForm : Form
{
    private readonly IBackupMaintenanceService _service;
    private readonly IInstallationContext _installation;
    private readonly IProtectedRestoreService? _protectedRestore;
    private readonly IProtectedBackupService? _protectedBackup;
    private readonly TextBox _destination = new();
    private readonly CheckBox _automatic = new();
    private readonly ComboBox _frequency = new();
    private readonly NumericUpDown _retention = new();
    private readonly Label _last = new();
    private readonly Label _next = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _create = new();
    private readonly Button _browse = new();
    private readonly Button _saveSettings = new();
    private readonly Button _cancel = new();
    private readonly Button _restore = new();
    private readonly Button _selectRestore = new();
    private readonly Label _restoreSummary = new();
    private readonly DataGridView _history = new();
    private CancellationTokenSource? _operationCancellation;
    private string? _selectedRestorePath;
    private BackupManifest? _selectedRestoreManifest;
    private string? _selectedRestorePackageSha256;
    private ProtectedRestoreCandidateSource _selectedRestoreSource = ProtectedRestoreCandidateSource.LocalProtectedBackup;
    private bool _protectedBackupInProgress;
    private bool _protectedCandidateStageInProgress;
    private bool _restoreSelectionInProgress;

    public BackupRestoreForm(
        IBackupMaintenanceService service,
        IInstallationContext installation,
        IProtectedRestoreService? protectedRestore = null,
        IProtectedBackupService? protectedBackup = null)
        : this(service, installation, loadSettingsOnShow: true, protectedRestore, protectedBackup)
    {
    }

    internal BackupRestoreForm(
        IBackupMaintenanceService service,
        IInstallationContext installation,
        bool loadSettingsOnShow,
        IProtectedRestoreService? protectedRestore = null,
        IProtectedBackupService? protectedBackup = null)
    {
        _service = service;
        _installation = installation;
        _protectedRestore = protectedRestore;
        _protectedBackup = protectedBackup;
        BuildUi();
        if (loadSettingsOnShow)
        {
            Load += BackupRestoreForm_Load;
        }
        FormClosed += (_, _) => _operationCancellation?.Cancel();
        FormClosing += (_, e) =>
        {
            if (!_protectedBackupInProgress && !_restoreSelectionInProgress)
            {
                return;
            }

            e.Cancel = true;
            MessageBox.Show(
                "Una operación protegida sigue trabajando. Espera el resultado antes de cerrar esta ventana.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
    }

    private void BuildUi()
    {
        Text = "Respaldos y restauracion";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 650);
        ClientSize = new Size(1060, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(18, 9),
            Font = UITheme.NormalBoldFont
        };
        var backupPage = new TabPage("Respaldos")
        {
            Padding = new Padding(18),
            BackColor = UITheme.DashboardBackgroundColor
        };
        var restorePage = new TabPage("Restaurar")
        {
            Padding = new Padding(18),
            BackColor = UITheme.DashboardBackgroundColor
        };
        tabs.TabPages.AddRange([backupPage, restorePage]);
        BuildBackupPage(backupPage);
        BuildRestorePage(restorePage);
        Controls.Add(tabs);
    }

    private void BuildBackupPage(Control page)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 8,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddLabel(layout, "Carpeta destino", 0, 0);
        _destination.Dock = DockStyle.Fill;
        _destination.Margin = new Padding(3, 10, 3, 8);
        layout.Controls.Add(_destination, 1, 0);
        layout.SetColumnSpan(_destination, 2);
        _browse.Text = "Examinar...";
        _browse.Dock = DockStyle.Fill;
        _browse.Margin = new Padding(8, 8, 0, 8);
        _browse.Name = "btnBrowseBackupFolder";
        _browse.Click += (_, _) => SelectDestination();
        UITheme.StyleSecondaryButton(_browse);
        UITheme.EnsureButtonContentFits(_browse, 120, 38);
        layout.Controls.Add(_browse, 3, 0);

        AddLabel(layout, "Respaldo automatico", 0, 1);
        _automatic.Text = "Habilitado";
        _automatic.Dock = DockStyle.Fill;
        layout.Controls.Add(_automatic, 1, 1);
        AddLabel(layout, "Frecuencia", 2, 1);
        _frequency.DropDownStyle = ComboBoxStyle.DropDownList;
        _frequency.DisplayMember = nameof(BackupFrequencyOption.Label);
        _frequency.ValueMember = nameof(BackupFrequencyOption.Value);
        _frequency.DataSource = new[]
        {
            new BackupFrequencyOption(BackupFrequency.Daily, "Diario"),
            new BackupFrequencyOption(BackupFrequency.Weekly, "Semanal"),
            new BackupFrequencyOption(BackupFrequency.EveryFifteenDays, "Cada 15 días"),
            new BackupFrequencyOption(BackupFrequency.Monthly, "Mensual")
        };
        _frequency.Dock = DockStyle.Fill;
        _frequency.Margin = new Padding(3, 9, 0, 8);
        layout.Controls.Add(_frequency, 3, 1);

        AddLabel(layout, "Retener automaticos", 0, 2);
        _retention.Minimum = 1;
        _retention.Maximum = 365;
        _retention.Dock = DockStyle.Left;
        _retention.Width = 120;
        _retention.Margin = new Padding(3, 9, 3, 8);
        layout.Controls.Add(_retention, 1, 2);
        _saveSettings.Text = "Guardar configuración";
        _saveSettings.Dock = DockStyle.Fill;
        _saveSettings.Margin = new Padding(8);
        _saveSettings.Name = "btnSaveBackupSettings";
        UITheme.StylePrimaryButton(_saveSettings);
        UITheme.EnsureButtonContentFits(_saveSettings, 210, 42);
        _saveSettings.Click += async (_, _) => await SaveSettingsAsync();
        layout.Controls.Add(_saveSettings, 3, 2);

        var isMachineWide = _protectedBackup?.IsMachineWide == true;
        AddLabel(layout, isMachineWide ? "Último respaldo protegido" : "Último automático", 0, 3);
        _last.Dock = DockStyle.Fill;
        _last.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_last, 1, 3);
        AddLabel(layout, isMachineWide ? "Horario protegido" : "Próximo aproximado", 2, 3);
        _next.Dock = DockStyle.Fill;
        _next.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_next, 3, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _create.Text = "Crear respaldo ahora";
        _create.Name = "btnCreateBackup";
        UITheme.StylePrimaryButton(_create);
        UITheme.EnsureButtonContentFits(_create, 220, 44, 48);
        _create.Size = _create.MinimumSize;
        _create.Click += async (_, _) => await CreateBackupAsync();
        _cancel.Text = "Cancelar";
        _cancel.Name = "btnCancelBackup";
        _cancel.Enabled = false;
        _cancel.Click += (_, _) => _operationCancellation?.Cancel();
        UITheme.StyleSecondaryButton(_cancel);
        UITheme.EnsureButtonContentFits(_cancel, 130, 44);
        _cancel.Size = _cancel.MinimumSize;
        var open = new Button { Text = "Abrir carpeta" };
        open.Name = "btnOpenBackupFolder";
        open.Click += (_, _) => OpenDestination();
        UITheme.StyleSecondaryButton(open);
        UITheme.EnsureButtonContentFits(open, 150, 44);
        open.Size = open.MinimumSize;
        buttons.Controls.AddRange([_create, _cancel, open]);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 4);

        _progress.Dock = DockStyle.Fill;
        _progress.Visible = false;
        layout.Controls.Add(_progress, 0, 6);
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UITheme.MutedTextColor;
        layout.Controls.Add(_status, 1, 6);
        layout.SetColumnSpan(_status, 3);

        _history.Dock = DockStyle.Fill;
        _history.ReadOnly = true;
        _history.AllowUserToAddRows = false;
        _history.AllowUserToDeleteRows = false;
        _history.AutoGenerateColumns = true;
        _history.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _history.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UITheme.StyleDataGridView(_history);
        layout.Controls.Add(_history, 0, 7);
        layout.SetColumnSpan(_history, 4);
        page.Controls.Add(layout);
    }

    private void BuildRestorePage(Control page)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = "Restaurar es una operacion de mantenimiento protegida. El archivo se valida completamente, se solicita su contraseña y se crea un respaldo preventivo antes de preparar el reinicio.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor
        }, 0, 0);
        _selectRestore.Text = "Seleccionar y validar respaldo...";
        _selectRestore.Dock = DockStyle.Left;
        _selectRestore.Name = "btnSelectBackup";
        _selectRestore.Click += async (_, _) => await SelectAndValidateRestoreAsync();
        UITheme.StyleSecondaryButton(_selectRestore);
        UITheme.EnsureButtonContentFits(_selectRestore, 310, 44, 52);
        _selectRestore.Width = _selectRestore.MinimumSize.Width;
        panel.Controls.Add(_selectRestore, 0, 1);
        _restore.Text = "Restaurar y reiniciar";
        _restore.Name = "btnRestoreBackup";
        _restore.Dock = DockStyle.Left;
        _restore.Enabled = false;
        UITheme.StyleDangerButton(_restore);
        UITheme.EnsureButtonContentFits(_restore, 250, 46, 52);
        _restore.Width = _restore.MinimumSize.Width;
        _restore.Click += async (_, _) =>
        {
            if (_selectedRestorePath is not null && _selectedRestoreManifest is not null)
            {
                await PrepareRestoreAsync(_selectedRestorePath, _selectedRestoreManifest);
            }
        };
        panel.Controls.Add(_restore, 0, 2);
        _restoreSummary.Name = "RestoreSummary";
        _restoreSummary.Dock = DockStyle.Fill;
        _restoreSummary.Font = UITheme.NormalBoldFont;
        _restoreSummary.ForeColor = UITheme.PrimaryColor;
        _restoreSummary.Text = "Ningun respaldo seleccionado.";
        panel.Controls.Add(_restoreSummary, 0, 4);
        page.Controls.Add(panel);
    }

    private async void BackupRestoreForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await LoadSettingsAndHistoryAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private async Task LoadSettingsAndHistoryAsync()
    {
        var settings = await _service.GetSettingsAsync();
        _destination.Text = settings.DestinationDirectory;
        _automatic.Checked = settings.AutomaticEnabled;
        _frequency.SelectedValue = settings.Frequency;
        _retention.Value = settings.AutomaticRetentionCount;
        var isMachineWide = _protectedBackup?.IsMachineWide == true;
        _last.Text = settings.LastAutomaticBackupUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Aun no ejecutado";
        _next.Text = isMachineWide
            ? settings.AutomaticEnabled
                ? "Diario 02:00 — SYSTEM; recupera al reanudarse"
                : "Tarea automática deshabilitada"
            : settings.AutomaticEnabled
                ? settings.NextAutomaticBackupUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Al proximo inicio"
                : "Deshabilitado";
        _status.Text = settings.LastAutomaticError is null
            ? isMachineWide ? "La política y el destino los administra el instalador protegido." : ""
            : $"Ultimo error: {settings.LastAutomaticError}";
        if (isMachineWide)
        {
            _destination.ReadOnly = true;
            _automatic.Enabled = false;
            _frequency.Enabled = false;
            _retention.Enabled = false;
            _browse.Enabled = false;
            _saveSettings.Enabled = false;
        }
        _history.DataSource = (await _service.GetHistoryAsync())
            .Select(item => new
            {
                Fecha = item.CreatedUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "-",
                Tipo = item.Kind?.ToString() ?? "Desconocido",
                TamanoMB = Math.Round(item.SizeBytes / 1024d / 1024d, 2),
                Origen = item.SourceInstallationMode ?? "-",
                Estado = item.Status,
                Archivo = Path.GetFileName(item.FilePath)
            }).ToList();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _service.UpdateSettingsAsync(new UpdateBackupSettingsRequest(
                _automatic.Checked,
                _destination.Text,
                (BackupFrequency)_frequency.SelectedValue!,
                (int)_retention.Value));
            await LoadSettingsAndHistoryAsync();
            MessageBox.Show("Configuracion local de respaldos guardada.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CreateBackupAsync()
    {
        if (_protectedBackup?.IsMachineWide == true)
        {
            await CreateProtectedManualBackupAsync();
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        SetOperationState(true);
        try
        {
            var progress = new Progress<BackupProgress>(value =>
            {
                _progress.Value = Math.Clamp(value.Percent, 0, 100);
                _status.Text = value.Message;
            });
            var result = await _service.CreateBackupAsync(
                new CreateBackupRequest(BackupKind.Manual, _destination.Text),
                progress,
                _operationCancellation.Token);
            await LoadSettingsAndHistoryAsync();
            MessageBox.Show($"Respaldo creado y validado:\r\n{result.FilePath}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Operacion cancelada antes de publicar el respaldo.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetOperationState(false);
            _operationCancellation.Dispose();
            _operationCancellation = null;
        }
    }

    private async Task CreateProtectedManualBackupAsync()
    {
        _operationCancellation = new CancellationTokenSource();
        _protectedBackupInProgress = true;
        SetOperationState(true);
        _cancel.Enabled = false;
        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = "Esperando autorización UAC y resultado del BackupRunner protegido...";
        try
        {
            var result = await _protectedBackup!.CreateManualBackupAsync(_operationCancellation.Token);
            if (result.Outcome == ProtectedBackupOutcome.Cancelled)
            {
                _status.Text = "UAC cancelado; no se inició el respaldo.";
                MessageBox.Show(result.Message, "Respaldo protegido cancelado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (result.Outcome != ProtectedBackupOutcome.Succeeded || string.IsNullOrWhiteSpace(result.FilePath))
            {
                _status.Text = result.Message;
                MessageBox.Show(result.Message, "Respaldo protegido no confirmado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var validation = await _service.ValidateBackupAsync(result.FilePath, _operationCancellation.Token);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    $"BackupRunner reportó éxito, pero la verificación desde la aplicación falló: {string.Join(" ", validation.Errors)}");
            }

            await LoadSettingsAndHistoryAsync();
            MessageBox.Show(
                $"Respaldo manual protegido creado y validado:\r\n{result.FilePath}\r\nSHA-256: {result.Sha256}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "La solicitud se canceló antes de iniciar el helper protegido.";
        }
        catch (Exception ex)
        {
            _status.Text = "El respaldo protegido no se confirmó.";
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _protectedBackupInProgress = false;
            SetOperationState(false);
            _progress.Style = ProgressBarStyle.Blocks;
            _operationCancellation.Dispose();
            _operationCancellation = null;
        }
    }

    private async Task SelectAndValidateRestoreAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Respaldo ArteInventariado (*.aibak)|*.aibak",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _selectedRestorePath = null;
        _selectedRestoreManifest = null;
        _selectedRestorePackageSha256 = null;
        _restore.Enabled = false;
        _restoreSummary.Text = "Validando el respaldo seleccionado...";
        _operationCancellation = new CancellationTokenSource();
        _protectedCandidateStageInProgress = _protectedRestore?.IsMachineWide == true;
        _restoreSelectionInProgress = true;
        SetOperationState(true);
        try
        {
            var token = _operationCancellation.Token;
            var selectedHash = await ComputeSha256Async(dialog.FileName, token);
            var validation = await _service.ValidateBackupAsync(dialog.FileName, token);
            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidDataException(string.Join("\r\n", validation.Errors));
            }

            var selectedPath = dialog.FileName;
            var selectedSource = ProtectedRestoreCandidateSource.LocalProtectedBackup;
            var selectedManifest = validation.Manifest;
            var selectedPackageHash = selectedHash;
            var machineWide = _protectedRestore?.IsMachineWide == true;
            if (machineWide)
            {
                _restoreSummary.Text = "Paquete válido. Solicitando autorización para copiarlo al staging protegido; la DB no se modifica...";
                var staged = await _protectedRestore!.StageLocalCandidateAsync(dialog.FileName, selectedHash, token);
                if (staged.Outcome == ProtectedRestoreOutcome.Cancelled)
                {
                    _restoreSummary.Text = "Importación cancelada en UAC. La DB permanece intacta.";
                    MessageBox.Show(staged.Message, "Importación cancelada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (staged.Outcome != ProtectedRestoreOutcome.Succeeded ||
                    string.IsNullOrWhiteSpace(staged.CandidatePath) ||
                    !staged.Source.HasValue ||
                    !string.Equals(staged.PackageSha256, selectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(staged.Message);
                }

                var stagedValidation = await _service.ValidateBackupAsync(staged.CandidatePath, token);
                if (!stagedValidation.IsValid || stagedValidation.Manifest is null)
                {
                    throw new InvalidDataException(
                        "La copia protegida no pasó la segunda validación: " + string.Join(" ", stagedValidation.Errors));
                }

                if (!SameSelectedBackup(selectedManifest, stagedValidation.Manifest))
                {
                    throw new InvalidDataException("El manifest de la copia protegida no coincide con el archivo que seleccionaste.");
                }

                selectedPath = staged.CandidatePath;
                selectedSource = staged.Source.Value;
                selectedManifest = stagedValidation.Manifest;
                selectedPackageHash = staged.PackageSha256;
            }

            var validationLabel = !machineWide
                ? "Validado"
                : selectedSource == ProtectedRestoreCandidateSource.LocalStagedBackup
                    ? "Importado y validado en staging protegido"
                    : "Validado en una raíz protegida";
            _restoreSummary.Text = validationLabel + ": " +
                                   $"{selectedManifest.CreatedLocal:dd/MM/yyyy HH:mm}\r\n" +
                                   $"Origen: {selectedManifest.SourceInstallationMode}\r\n" +
                                   $"DB: {selectedManifest.DatabaseSizeBytes / 1024d / 1024d:N2} MB\r\n" +
                                   $"Assets: {selectedManifest.Assets.Count(asset => !asset.WasMissing)}\r\n" +
                                   (machineWide ? "La DB actual sigue intacta. Pulsa Restaurar para iniciar el paso destructivo." : "");
            _selectedRestorePath = selectedPath;
            _selectedRestoreSource = selectedSource;
            _selectedRestorePackageSha256 = selectedPackageHash;
            _selectedRestoreManifest = selectedManifest;
            _restore.Enabled = true;
        }
        catch (Exception ex)
        {
            _restoreSummary.Text = "No se importó el respaldo; la base actual sigue intacta.";
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _protectedCandidateStageInProgress = false;
            _restoreSelectionInProgress = false;
            SetOperationState(false);
            _operationCancellation.Dispose();
            _operationCancellation = null;
        }
    }

    private async Task PrepareRestoreAsync(string filePath, BackupManifest manifest)
    {
        using var confirmation = new RestoreConfirmationForm(manifest, _installation.Status.Mode, filePath);
        if (confirmation.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        SetOperationState(true);
        try
        {
            if (_protectedRestore?.IsMachineWide == true)
            {
                var protectedResult = await _protectedRestore.RestoreAsync(
                    new ProtectedRestoreRequest(
                        filePath,
                        _selectedRestoreSource,
                        manifest.BackupId,
                        manifest.SourceInstallationId,
                        _selectedRestorePackageSha256,
                        ExpectedContentStatus: manifest.ContentStatus,
                        ConfirmationAccepted: true),
                    confirmation.Password,
                    _operationCancellation.Token);

                if (protectedResult.Outcome == ProtectedRestoreOutcome.Cancelled)
                {
                    MessageBox.Show(protectedResult.Message, "Restauracion cancelada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (protectedResult.Outcome != ProtectedRestoreOutcome.RelaunchedElevated)
                {
                    MessageBox.Show(protectedResult.Message, "Restauracion protegida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                MessageBox.Show(
                    protectedResult.Message,
                    "Restauracion protegida",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                ControlledShutdownState.Begin();
                System.Windows.Forms.Application.Exit();
                return;
            }

            var progress = new Progress<BackupProgress>(value =>
            {
                _progress.Visible = true;
                _progress.Value = Math.Clamp(value.Percent, 0, 100);
                _status.Text = value.Message;
            });
            var result = await _service.PrepareRestoreAsync(
                new PrepareRestoreRequest(filePath, confirmation.Password, true),
                progress,
                _operationCancellation.Token);
            MessageBox.Show(result.Message, "Restauracion preparada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ControlledShutdownState.Begin();
            System.Windows.Forms.Application.Restart();
            System.Windows.Forms.Application.Exit();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Restauracion cancelada antes de la fase critica.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetOperationState(false);
            _operationCancellation.Dispose();
            _operationCancellation = null;
        }
    }

    private void SetOperationState(bool active)
    {
        _create.Enabled = !active;
        _selectRestore.Enabled = !active;
        _restore.Enabled = !active && _selectedRestoreManifest is not null && _selectedRestorePath is not null;
        _cancel.Enabled = active && !_protectedBackupInProgress && !_protectedCandidateStageInProgress;
        _progress.Visible = active;
        if (!active)
        {
            _progress.Value = 0;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static bool SameSelectedBackup(BackupManifest selected, BackupManifest staged) =>
        selected.BackupId == staged.BackupId &&
        selected.SourceInstallationId == staged.SourceInstallationId &&
        selected.Kind == staged.Kind &&
        selected.ContentStatus == staged.ContentStatus &&
        selected.DatabaseSha256.Equals(staged.DatabaseSha256, StringComparison.OrdinalIgnoreCase) &&
        selected.DatabaseSizeBytes == staged.DatabaseSizeBytes;

    private void SelectDestination()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(_destination.Text) ? _destination.Text : string.Empty };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _destination.Text = dialog.SelectedPath;
        }
    }

    private void OpenDestination()
    {
        if (Directory.Exists(_destination.Text))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", _destination.Text) { UseShellExecute = true });
        }
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int column, int row)
    {
        layout.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UITheme.NormalBoldFont
        }, column, row);
    }

    private sealed record BackupFrequencyOption(BackupFrequency Value, string Label);
}
