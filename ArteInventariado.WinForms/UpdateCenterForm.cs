using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.WinForms;

public sealed class UpdateCenterForm : Form
{
    private readonly IUpdateService _service;
    private readonly Label _installed = new();
    private readonly Label _state = new();
    private readonly Label _package = new();
    private readonly TextBox _manifestPath = new();
    private readonly TextBox _packagePath = new();
    private readonly TextBox _notes = new();
    private readonly Button _validate = new();
    private readonly Button _prepare = new();
    private readonly Button _install = new();
    private string? _preparedStagingPath;
    private bool _refreshingStatus;

    public UpdateCenterForm(IUpdateService service)
    {
        _service = service;
        BuildUi();
        Shown += async (_, _) => await RefreshStatusAsync();
        Activated += async (_, _) => await RefreshStatusAsync();
    }

    private void BuildUi()
    {
        Text = "Actualizaciones";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 500);
        MinimumSize = new Size(680, 440);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);
        BackColor = UITheme.DashboardBackgroundColor;

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20), BackColor = UITheme.DashboardBackgroundColor };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(Header(), 0, 0);
        shell.Controls.Add(StatusPanel(), 0, 1);
        shell.Controls.Add(DetailsPanel(), 0, 2);
        shell.Controls.Add(ActionPanel(), 0, 3);
        Controls.Add(shell);
    }

    private Control Header() => new Label
    {
        Text = "Actualizaciones\r\nPrepare e instale versiones locales de forma segura, conservando sus datos.",
        Dock = DockStyle.Fill,
        Font = UITheme.HeaderFont,
        ForeColor = UITheme.PrimaryColor,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0)
    };

    private Control StatusPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = UITheme.SurfaceColor, Padding = new Padding(14, 9, 14, 9) };
        for (var i = 0; i < 3; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        panel.Controls.Add(StatusCell("Versión instalada", _installed), 0, 0);
        panel.Controls.Add(StatusCell("Estado", _state), 1, 0);
        panel.Controls.Add(StatusCell("Paquete", _package), 2, 0);
        panel.SetColumnSpan(_installed, 1);
        return panel;
    }

    private static Control StatusCell(string title, Label value)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(4), BackColor = UITheme.SurfaceColor };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.MutedTextColor }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.Font = UITheme.NormalBoldFont;
        value.ForeColor = UITheme.PrimaryColor;
        value.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private Control DetailsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(14), BackColor = UITheme.SurfaceColor };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddPathRow(panel, "Manifest local", _manifestPath, "Seleccionar...", SelectManifest, 0);
        AddPathRow(panel, "Paquete ZIP", _packagePath, "Seleccionar...", SelectPackage, 1);
        _notes.Multiline = true;
        _notes.ReadOnly = true;
        _notes.ScrollBars = ScrollBars.Vertical;
        _notes.Dock = DockStyle.Fill;
        _notes.PlaceholderText = "Las notas del manifest aparecerán aquí.";
        UITheme.StyleTextBox(_notes);
        panel.Controls.Add(new Label { Text = "Notas de versión", Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.TextDark, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        panel.Controls.Add(_notes, 1, 2);
        panel.SetColumnSpan(_notes, 2);
        return panel;
    }

    private void AddPathRow(TableLayoutPanel panel, string title, TextBox textBox, string buttonText, EventHandler click, int row)
    {
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = UITheme.NormalBoldFont, ForeColor = UITheme.TextDark, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(3, 7, 8, 7);
        UITheme.StyleTextBox(textBox);
        panel.Controls.Add(textBox, 1, row);
        var button = new Button { Text = buttonText, Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0, 5, 0, 5) };
        button.Click += click;
        UITheme.StyleSecondaryButton(button);
        UITheme.EnsureButtonContentFits(button, 108, 36, 36);
        panel.Controls.Add(button, 2, row);
    }

    private Control ActionPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = UITheme.SurfaceColor
        };
        _validate.Text = "Validar paquete";
        _prepare.Text = "Preparar actualización";
        _install.Text = "Instalar";
        _validate.Click += async (_, _) => await ValidateAsync();
        _prepare.Click += async (_, _) => await PrepareAsync();
        _install.Click += (_, _) => LaunchUpdater(handoffOnly: !string.IsNullOrWhiteSpace(_preparedStagingPath));
        UITheme.StyleSecondaryButton(_validate);
        UITheme.StyleSecondaryButton(_prepare);
        UITheme.StylePrimaryButton(_install);
        foreach (var button in new[] { _validate, _prepare, _install })
        {
            button.AutoSize = true;
            UITheme.EnsureButtonContentFits(button, 122, 42);
        }
        panel.Controls.Add(_install);
        panel.Controls.Add(_prepare);
        panel.Controls.Add(_validate);
        _install.Enabled = false;
        return panel;
    }

    private void SelectManifest(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "Manifest JSON (*.json)|*.json|Todos los archivos (*.*)|*.*", Title = "Seleccione el manifest de actualización" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _manifestPath.Text = dialog.FileName;
    }

    private void SelectPackage(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "Paquete ZIP (*.zip)|*.zip|Todos los archivos (*.*)|*.*", Title = "Seleccione el paquete de actualización" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _packagePath.Text = dialog.FileName;
    }

    private async Task ValidateAsync()
    {
        try
        {
            _install.Enabled = false;
            var result = await _service.ValidatePackageAsync(_manifestPath.Text, _packagePath.Text);
            _notes.Text = result.Manifest?.ReleaseNotes ?? string.Empty;
            _package.Text = result.IsValid ? $"Válido ({FormatBytes(result.Manifest!.SizeBytes)})" : "No válido";
            if (result.IsValid)
            {
                _state.Text = "Paquete validado; falta preparar la actualización";
                _install.Enabled = false;
            }
            MessageBox.Show(this, result.IsValid ? "El manifest y el paquete son válidos." : string.Join(Environment.NewLine, result.Errors), Text, MessageBoxButtons.OK, result.IsValid ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task PrepareAsync()
    {
        if (!string.IsNullOrWhiteSpace(_preparedStagingPath))
        {
            MessageBox.Show(
                this,
                "Ya hay un paquete en staging. Use 'Preparar protegida' para reintentar la validación elevada; no vuelva a preparar el mismo paquete.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _prepare.Enabled = false;
        try
        {
            var result = await _service.PrepareAsync(_manifestPath.Text, _packagePath.Text);
            _preparedStagingPath = result.StagingPath;
            var canInstall = result.Succeeded &&
                             result.PreparationStage == UpdatePreparationStage.ReadyToInstall &&
                             !string.IsNullOrWhiteSpace(result.PreUpdateBackupPath);
            _install.Enabled = canInstall;
            _state.Text = StateText(result.State, result.Message, result.PreparationStage);

            if (result.Succeeded &&
                result.PreparationStage == UpdatePreparationStage.Staged &&
                !string.IsNullOrWhiteSpace(result.StagingPath))
            {
                MessageBox.Show(
                    this,
                    result.Message + Environment.NewLine +
                    "Revise el estado y pulse 'Preparar protegida' para solicitar UAC y crear PRE_UPDATE. " +
                    "No se copiará otro ZIP y la instalación todavía no comenzará.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _install.Text = "Preparar protegida";
                _install.Enabled = true;
                _prepare.Enabled = false;
                return;
            }

            MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, canInstall ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { _prepare.Enabled = string.IsNullOrWhiteSpace(_preparedStagingPath); }
    }

    private void LaunchUpdater(bool handoffOnly = false)
    {
        var updater = _service.GetUpdaterExecutablePath();
        if (!File.Exists(updater)) { MessageBox.Show(this, "El actualizador no está incluido en esta instalación publicada.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var confirmation = handoffOnly
            ? "ArteInventariado se cerrará temporalmente para autorizar la preparación protegida y crear PRE_UPDATE. La instalación no comenzará todavía. ¿Desea continuar?"
            : "ArteInventariado se cerrará, instalará la versión preparada y volverá a abrir la versión verificada. ¿Desea continuar?";
        if (MessageBox.Show(
                this,
                confirmation,
                "Instalar actualización",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        UpdateRuntimePaths? diagnosticPaths = null;
        var diagnosticStage = "inicio";
        try
        {
            diagnosticStage = "resolucion-rutas";
            var paths = UpdateRuntimePaths.CreateDefault();
            diagnosticPaths = paths;
            var operationArguments = new List<string>
            {
                "--wait-pid",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--wait-executable",
                Environment.ProcessPath ?? throw new InvalidOperationException(
                    "No se pudo resolver el ejecutable actual para coordinar el cierre."),
                "--state",
                paths.StatePath
            };
            if (paths.PathProvider?.IsMachineWide == true)
            {
                if (handoffOnly && string.IsNullOrWhiteSpace(_preparedStagingPath))
                {
                    MessageBox.Show(
                        this,
                        "Prepare primero el paquete para crear el staging temporal de esta instalación protegida.",
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (handoffOnly)
                {
                    operationArguments.Add("--handoff-only");
                    operationArguments.Add("--handoff");
                    operationArguments.Add(_preparedStagingPath!);
                }
                else if (!string.IsNullOrWhiteSpace(_preparedStagingPath))
                {
                    MessageBox.Show(
                        this,
                        "La preparación protegida aún no terminó. Autorice el handoff antes de instalar.",
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (!handoffOnly)
                {
                    // The elevated updater owns installation and asks
                    // Explorer to open the stable launcher at normal
                    // integrity after it finishes.
                    operationArguments.Add("--launch-stable");
                }
            }

            diagnosticStage = "lanzamiento-updater";
            MachineWideUpdateDiagnostics.Write(
                paths,
                handoffOnly ? "uac-requested-prepare" : "uac-requested-install",
                executablePath: updater,
                tokenElevated: MachineWideElevation.TryGetTokenElevation(out var requestElevation)
                    ? requestElevation
                    : null,
                relatedPath: handoffOnly ? _preparedStagingPath : paths.StatePath,
                canonicalState: handoffOnly ? "staging" : "ready-to-install");
            var result = UpdateLauncher.LaunchUpdater(
                paths,
                updater,
                operationArguments);
            if (result.Outcome == UpdateLaunchOutcome.Cancelled)
            {
                MachineWideUpdateDiagnostics.Write(
                    paths,
                    "uac-cancelled",
                    executablePath: updater,
                    tokenElevated: MachineWideElevation.TryGetTokenElevation(out var cancelledElevation)
                        ? cancelledElevation
                        : null,
                    relatedPath: paths.StatePath);
                MessageBox.Show(
                    this,
                    "La actualización fue cancelada en UAC. La versión actual y previous permanecen intactas.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // The updater owns the protected handoff and will relaunch the
            // stable entry point after installation.  Do not inspect or wait
            // on its elevated Process handle from this medium-integrity UI.
            result.Process?.Dispose();
            diagnosticStage = "cierre-aplicacion";
            System.Windows.Forms.Application.Exit();
        }
        catch (Exception ex)
        {
            MachineWideUpdateDiagnostics.Write(
                diagnosticPaths,
                diagnosticStage,
                ex,
                executablePath: diagnosticPaths?.StableUpdaterPath,
                tokenElevated: MachineWideElevation.TryGetTokenElevation(out var failedElevation)
                    ? failedElevation
                    : null,
                relatedPath: diagnosticPaths?.StatePath);
            MessageBox.Show(
                this,
                MachineWideUpdateDiagnostics.Describe(diagnosticStage, ex, diagnosticPaths?.StatePath),
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshingStatus || IsDisposed)
        {
            return;
        }

        _refreshingStatus = true;
        try
        {
            var status = await _service.GetStatusAsync();
            if (IsDisposed)
            {
                return;
            }

            if (status.State is UpdateState.Failed or UpdateState.RollbackRequired or UpdateState.RollingBack or UpdateState.Success)
            {
                // A rollback or completed installation invalidates any local
                // staging reference. Recovery/PRE_UPDATE remain untouched on
                // disk and are still owned by the updater.
                _preparedStagingPath = null;
            }
            if (status.PreparationStage == UpdatePreparationStage.Staged &&
                !string.IsNullOrWhiteSpace(status.StagingPath))
            {
                _preparedStagingPath = status.StagingPath;
                _install.Text = "Preparar protegida";
                _prepare.Enabled = false;
            }
            else if (status.PreparationStage != UpdatePreparationStage.Staged)
            {
                _preparedStagingPath = null;
                _install.Text = "Instalar";
                _prepare.Enabled = true;
            }

            _installed.Text = status.InstalledVersion;
            _state.Text = StateText(status.State, status.LastError, status.PreparationStage);
            _package.Text = status.AvailableVersion ?? "Sin paquete seleccionado";
            // Legacy/fake implementations that predate PreparationStage are
            // accepted only for an explicitly ReadyToInstall state.  The real
            // machine-wide service also requires a validated PRE_UPDATE and
            // sets CanInstall itself.
            _install.Enabled = (status.PreparationStage == UpdatePreparationStage.Staged &&
                                !string.IsNullOrWhiteSpace(status.StagingPath)) ||
                               status.CanInstall ||
                               (status.PreparationStage == UpdatePreparationStage.None &&
                                status.State == UpdateState.ReadyToInstall);
        }
        finally
        {
            _refreshingStatus = false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch { > 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB", > 1024 => $"{bytes / 1024d:0} KB", _ => $"{bytes} bytes" };

    private static string StateText(
        UpdateState state,
        string? lastError = null,
        UpdatePreparationStage preparationStage = UpdatePreparationStage.None)
    {
        if (preparationStage == UpdatePreparationStage.PackageValidated)
        {
            return "Paquete validado; preparación no iniciada";
        }

        if (preparationStage == UpdatePreparationStage.Staged)
        {
            return "Paquete en staging; falta crear PRE_UPDATE";
        }

        if (preparationStage == UpdatePreparationStage.PreUpdateCreated &&
            state == UpdateState.BackupCreated)
        {
            return "PRE_UPDATE creado; validando preparación";
        }

        if (preparationStage == UpdatePreparationStage.ReadyToInstall &&
            state == UpdateState.ReadyToInstall)
        {
            return "Listo para instalar";
        }

        if (preparationStage == UpdatePreparationStage.Rollback &&
            state == UpdateState.Failed &&
            (lastError?.Contains("rollback", StringComparison.OrdinalIgnoreCase) == true ||
             lastError?.Contains("revert", StringComparison.OrdinalIgnoreCase) == true))
        {
            return "Instalación revertida; prepare nuevamente";
        }

        return state switch
        {
        UpdateState.Idle => "Actualizado / sin operación",
        UpdateState.Preparing => "Preparando",
        UpdateState.BackupCreated => "Respaldo creado",
        UpdateState.ReadyToInstall => "Listo para instalar",
        UpdateState.Installing => "Instalando",
        UpdateState.Verifying => "Verificando",
        UpdateState.Success => "Actualización completada",
        UpdateState.Failed when lastError?.Contains("Rollback completado", StringComparison.OrdinalIgnoreCase) == true
            => "Instalación revertida; prepare nuevamente",
        UpdateState.Failed => "Error recuperado",
        UpdateState.RollbackRequired => "Recuperación requerida",
        UpdateState.RollingBack => "Restaurando versión anterior",
        _ => "Estado desconocido"
        };
    }
}
