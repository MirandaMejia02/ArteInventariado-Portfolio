using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.WinForms;

public class SystemConfigForm : Form
{
    private readonly ISystemConfigService _configService;
    private readonly ICreditService _creditService;
    private readonly ICreditAlertQueryService _creditAlertService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IStationSettingsService? _stationSettingsService;
    private readonly IInstallationContext? _installationContext;
    private readonly TextBox _txtBusinessName = new();
    private readonly TextBox _txtBusinessAddress = new();
    private readonly TextBox _txtBusinessPhone = new();
    private readonly TextBox _txtBusinessEmail = new();
    private readonly TextBox _txtCashOpeningTime = new();
    private readonly TextBox _txtReportsFolder = new();
    private readonly NumericUpDown _numCreditLimit = new();
    private readonly NumericUpDown _numMaxAccounts = new();
    private readonly NumericUpDown _numDefaultTerm = new();
    private readonly NumericUpDown _numEmployeeMaxTerm = new();
    private readonly NumericUpDown _numAdminMaxTerm = new();
    private readonly NumericUpDown _numInterest = new();
    private readonly NumericUpDown _numMorosoDays = new();
    private readonly NumericUpDown _numAlertUpcomingDays = new();
    private readonly NumericUpDown _numAlertNoPaymentDays = new();
    private readonly NumericUpDown _numAlertNearLimit = new();
    private readonly CheckBox _chkAllowOutstanding = new();
    private readonly CheckBox _chkAlertDueToday = new();
    private readonly CheckBox _chkAlertUpcomingDue = new();
    private readonly CheckBox _chkAlertOverdue = new();
    private readonly CheckBox _chkAlertMoroso = new();
    private readonly CheckBox _chkAlertNoRecentPayment = new();
    private readonly CheckBox _chkAlertNearLimit = new();
    private readonly CheckBox _chkAlertLimitReached = new();
    private readonly CheckBox _chkBitcoinEnabled = new();
    private readonly CheckBox _chkQuickPosEnabled = new();
    private readonly CheckBox _chkTicketsEnabled = new();
    private readonly CheckBox _chkLabelsEnabled = new();
    private readonly CheckBox _chkCashDrawerEnabled = new();
    private readonly TextBox _txtReceiptPrinter = new();
    private readonly TextBox _txtLabelPrinter = new();
    private readonly TextBox _txtCashDrawer = new();
    private readonly Label _lblReceiptStatus = new();
    private readonly Label _lblLabelStatus = new();
    private readonly Label _lblCashDrawerStatus = new();
    private readonly Button _btnReportsFolder = new();
    private readonly Button _btnMaintenance = new();
    private readonly Button _btnUpdates = new();
    private readonly Button _btnCloudProvisioning = new();
    private readonly Button _btnSave = new();

    public SystemConfigForm(
        ISystemConfigService configService,
        ICreditService creditService,
        ICreditAlertQueryService creditAlertService,
        IServiceProvider serviceProvider,
        IStationSettingsService stationSettingsService,
        IInstallationContext installationContext)
        : this(
            configService,
            creditService,
            creditAlertService,
            serviceProvider,
            loadSettingsOnShow: true,
            stationSettingsService: stationSettingsService,
            installationContext: installationContext)
    {
    }

    internal SystemConfigForm(
        ISystemConfigService configService,
        ICreditService creditService,
        ICreditAlertQueryService creditAlertService,
        IServiceProvider serviceProvider,
        bool loadSettingsOnShow)
        : this(
            configService,
            creditService,
            creditAlertService,
            serviceProvider,
            loadSettingsOnShow,
            stationSettingsService: null,
            installationContext: null)
    {
    }

    private SystemConfigForm(
        ISystemConfigService configService,
        ICreditService creditService,
        ICreditAlertQueryService creditAlertService,
        IServiceProvider serviceProvider,
        bool loadSettingsOnShow,
        IStationSettingsService? stationSettingsService,
        IInstallationContext? installationContext)
    {
        _configService = configService;
        _creditService = creditService;
        _creditAlertService = creditAlertService;
        _serviceProvider = serviceProvider;
        _stationSettingsService = stationSettingsService;
        _installationContext = installationContext;

        BuildUi();
        if (loadSettingsOnShow)
        {
            Load += SystemConfigForm_Load;
        }
    }

    private async void SystemConfigForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await LoadSettingsAsync();
        }
        finally
        {
            await ApplyCloudProvisioningVisibilityAsync();
        }
    }

    private async Task ApplyCloudProvisioningVisibilityAsync()
    {
        try
        {
            var authorization = _serviceProvider.GetService<CloudBackupProvisioningAuthorizationService>();
            _btnCloudProvisioning.Visible = authorization is not null &&
                await authorization.CanCurrentSessionOpenAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A failed read must fail closed. The service boundary still
            // rechecks authorization if a stale UI event reaches the handler.
            _btnCloudProvisioning.Visible = false;
        }
    }

    private void BuildUi()
    {
        Text = "Configuración";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(900, 760);
        MinimumSize = new Size(780, 640);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);
        BackColor = UITheme.DashboardBackgroundColor;

        ConfigureInputs();
        ApplyInstallationModeRestrictions();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UITheme.DashboardBackgroundColor
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        shell.Controls.Add(BuildHeader(), 0, 0);

        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(24, 0, 24, 14),
            BackColor = UITheme.DashboardBackgroundColor
        };
        var sections = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UITheme.DashboardBackgroundColor
        };
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 6; row++)
        {
            sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        sections.Controls.Add(CreateSection(
            "Configuración general",
            "Datos básicos de esta instalación y ubicación de reportes.",
            BuildGeneralSettings()), 0, 0);
        sections.Controls.Add(CreateSection(
            "Punto de venta y periféricos",
            "Configuración local. No se intentará usar hardware hasta que exista una integración real.",
            BuildPeripheralSettings()), 0, 1);
        sections.Controls.Add(CreateSection(
            "Continuidad de datos",
            "Cree respaldos, programe copias automáticas y restaure paquetes validados.",
            BuildMaintenanceSettings()), 0, 2);
        sections.Controls.Add(CreateSection(
            "Políticas generales de crédito",
            "Límites y plazos predeterminados para las operaciones de crédito.",
            BuildCreditSettings()), 0, 3);
        sections.Controls.Add(CreateSection(
            "Alertas de crédito",
            "Umbrales y eventos que deben mostrarse al equipo.",
            BuildAlertSettings()), 0, 4);
        sections.Controls.Add(CreateSection(
            "Información de la aplicación",
            "Identidad del ejecutable actualmente en ejecución.",
            BuildApplicationInfo()), 0, 5);
        viewport.Controls.Add(sections);
        shell.Controls.Add(viewport, 0, 1);
        shell.Controls.Add(BuildFooter(), 0, 2);
        Controls.Add(shell);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(24, 16, 24, 10),
            Margin = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        header.Controls.Add(new Label
        {
            Text = "Configuración",
            Dock = DockStyle.Fill,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = IsConsultaMode
                ? "Modo CONSULTA: los datos globales son de solo lectura; la carpeta de reportes sigue siendo local a esta PC."
                : "Preferencias globales del negocio, crédito y continuidad de datos local.",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        return header;
    }

    private Control BuildGeneralSettings()
    {
        var layout = CreateContentGrid(5, 6);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));

        AddFieldLabel(layout, "Nombre del negocio", 0, 0);
        _txtBusinessName.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtBusinessName, 1, 0);
        layout.SetColumnSpan(_txtBusinessName, 4);

        AddFieldLabel(layout, "Direccion comercial", 0, 1);
        _txtBusinessAddress.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtBusinessAddress, 1, 1);
        layout.SetColumnSpan(_txtBusinessAddress, 4);

        AddFieldLabel(layout, "Telefono", 0, 2);
        _txtBusinessPhone.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtBusinessPhone, 1, 2);

        AddFieldLabel(layout, "Correo", 2, 2);
        _txtBusinessEmail.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtBusinessEmail, 3, 2);
        layout.SetColumnSpan(_txtBusinessEmail, 2);

        AddFieldLabel(layout, "Apertura sugerida de caja", 0, 3);
        _txtCashOpeningTime.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtCashOpeningTime, 1, 3);

        AddFieldLabel(layout, "Bitcoin", 2, 3);
        _chkBitcoinEnabled.Dock = DockStyle.Fill;
        layout.Controls.Add(_chkBitcoinEnabled, 3, 3);
        layout.SetColumnSpan(_chkBitcoinEnabled, 2);

        AddFieldLabel(layout, "Reportes de esta PC", 0, 4);
        _txtReportsFolder.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtReportsFolder, 1, 4);
        layout.SetColumnSpan(_txtReportsFolder, 3);
        _btnReportsFolder.Dock = DockStyle.Fill;
        layout.Controls.Add(_btnReportsFolder, 4, 4);

        AddFieldLabel(layout, "POS rápido en esta PC", 0, 5);
        _chkQuickPosEnabled.Dock = DockStyle.Fill;
        layout.Controls.Add(_chkQuickPosEnabled, 1, 5);
        layout.SetColumnSpan(_chkQuickPosEnabled, 4);
        return layout;
    }

    private Control BuildMaintenanceSettings()
    {
        var layout = CreateContentGrid(2, 3);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        layout.Controls.Add(new Label
        {
            Text = "Administre backups manuales y automáticos, historial, validación y restore seguro al reiniciar.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 14, 0)
        }, 0, 0);
        _btnMaintenance.Dock = DockStyle.Fill;
        _btnMaintenance.Margin = new Padding(8, 4, 0, 4);
        layout.Controls.Add(_btnMaintenance, 1, 0);
        layout.Controls.Add(new Label
        {
            Text = "Instale paquetes locales validados conservando la base de datos y la identidad de esta estación.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 14, 0)
        }, 0, 1);
        _btnUpdates.Dock = DockStyle.Fill;
        _btnUpdates.Margin = new Padding(8, 4, 0, 4);
        layout.Controls.Add(_btnUpdates, 1, 1);
        layout.Controls.Add(new Label
        {
            Text = "Configure el uploader S3 upload-only con UAC; la credencial se cifra con DPAPI machine-scope y no se suben archivos de prueba.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 14, 0)
        }, 0, 2);
        _btnCloudProvisioning.Dock = DockStyle.Fill;
        _btnCloudProvisioning.Margin = new Padding(8, 4, 0, 4);
        layout.Controls.Add(_btnCloudProvisioning, 1, 2);
        return layout;
    }

    private Control BuildPeripheralSettings()
    {
        var layout = CreateContentGrid(3, 4);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));

        AddPeripheralRow(layout, 0, "Tickets", _chkTicketsEnabled, _txtReceiptPrinter, _lblReceiptStatus,
            "Nombre lógico pendiente de hardware");
        AddPeripheralRow(layout, 1, "Etiquetas", _chkLabelsEnabled, _txtLabelPrinter, _lblLabelStatus,
            "Nombre lógico pendiente de hardware");
        AddPeripheralRow(layout, 2, "Gaveta", _chkCashDrawerEnabled, _txtCashDrawer, _lblCashDrawerStatus,
            "Nombre lógico pendiente de hardware");

        var note = new Label
        {
            Text = "Sin hardware confirmado: la venta, el inventario y los códigos continúan funcionando aunque todos estén deshabilitados.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(0, 10, 0, 0)
        };
        layout.Controls.Add(note, 0, 3);
        layout.SetColumnSpan(note, 3);
        return layout;
    }

    private static void AddPeripheralRow(
        TableLayoutPanel layout,
        int row,
        string title,
        CheckBox enabled,
        TextBox logicalName,
        Label status,
        string placeholder)
    {
        enabled.Text = title;
        enabled.AutoSize = true;
        enabled.Font = UITheme.NormalBoldFont;
        enabled.Dock = DockStyle.Fill;
        enabled.Margin = new Padding(0, 8, 12, 8);
        logicalName.PlaceholderText = placeholder;
        logicalName.Dock = DockStyle.Fill;
        logicalName.Margin = new Padding(0, 5, 16, 5);
        UITheme.StyleTextBox(logicalName);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Font = UITheme.NormalBoldFont;
        status.ForeColor = UITheme.MutedTextColor;
        layout.Controls.Add(enabled, 0, row);
        layout.Controls.Add(logicalName, 1, row);
        layout.Controls.Add(status, 2, row);
        enabled.CheckedChanged += (_, _) => UpdatePeripheralStatus(enabled, logicalName, status, title);
        logicalName.TextChanged += (_, _) => UpdatePeripheralStatus(enabled, logicalName, status, title);
    }

    private static void UpdatePeripheralStatus(CheckBox enabled, TextBox logicalName, Label status, string title)
    {
        status.Text = !enabled.Checked
            ? "Deshabilitado"
            : string.IsNullOrWhiteSpace(logicalName.Text)
                ? "No configurado"
                : "Pendiente de hardware real";
        status.ForeColor = !enabled.Checked ? UITheme.MutedTextColor : UITheme.WarningColor;
        status.AccessibleName = $"Estado de {title}";
    }

    private Control BuildCreditSettings()
    {
        var layout = CreateContentGrid(3, 3);
        for (var column = 0; column < 3; column++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        }

        layout.Controls.Add(CreateNumericField("Límite predeterminado", _numCreditLimit), 0, 0);
        layout.Controls.Add(CreateNumericField("Máximo de cuentas", _numMaxAccounts), 1, 0);
        layout.Controls.Add(CreateNumericField("Plazo predeterminado", _numDefaultTerm), 2, 0);
        layout.Controls.Add(CreateNumericField("Plazo máximo empleado", _numEmployeeMaxTerm), 0, 1);
        layout.Controls.Add(CreateNumericField("Plazo máximo ADMIN", _numAdminMaxTerm), 1, 1);
        layout.Controls.Add(CreateNumericField("Interés (%)", _numInterest), 2, 1);
        layout.Controls.Add(CreateNumericField("Días para mora", _numMorosoDays), 0, 2);
        _chkAllowOutstanding.Dock = DockStyle.Fill;
        _chkAllowOutstanding.Margin = new Padding(8, 18, 8, 4);
        layout.Controls.Add(_chkAllowOutstanding, 1, 2);
        layout.SetColumnSpan(_chkAllowOutstanding, 2);
        return layout;
    }

    private Control BuildAlertSettings()
    {
        var layout = CreateContentGrid(3, 4);
        for (var column = 0; column < 3; column++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        }

        layout.Controls.Add(CreateNumericField("Próximo vencimiento (días)", _numAlertUpcomingDays), 0, 0);
        layout.Controls.Add(CreateNumericField("Sin abono (días)", _numAlertNoPaymentDays), 1, 0);
        layout.Controls.Add(CreateNumericField("Cerca del límite (%)", _numAlertNearLimit), 2, 0);
        AddCheck(layout, _chkAlertDueToday, "Vence hoy", 0, 1);
        AddCheck(layout, _chkAlertUpcomingDue, "Próxima a vencer", 1, 1);
        AddCheck(layout, _chkAlertOverdue, "Vencida", 2, 1);
        AddCheck(layout, _chkAlertMoroso, "Morosa", 0, 2);
        AddCheck(layout, _chkAlertNoRecentPayment, "Sin abono reciente", 1, 2);
        AddCheck(layout, _chkAlertNearLimit, "Cerca del límite", 2, 2);
        AddCheck(layout, _chkAlertLimitReached, "Límite alcanzado", 0, 3);
        return layout;
    }

    private static Control BuildApplicationInfo()
    {
        var layout = CreateContentGrid(1, 1);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Name = "lblApplicationVersion",
            Text = $"Versión de ArteInventariado: {ApplicationVersion.ProductVersion}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 3, 0, 3)
        }, 0, 0);
        return layout;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(24, 12, 24, 14),
            Margin = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        _btnSave.Dock = DockStyle.Fill;
        footer.Controls.Add(_btnSave, 1, 0);
        return footer;
    }

    private void ConfigureInputs()
    {
        UITheme.StyleTextBox(_txtBusinessName);
        UITheme.StyleTextBox(_txtBusinessAddress);
        UITheme.StyleTextBox(_txtBusinessPhone);
        UITheme.StyleTextBox(_txtBusinessEmail);
        UITheme.StyleTextBox(_txtCashOpeningTime);
        UITheme.StyleTextBox(_txtReportsFolder);

        _chkBitcoinEnabled.Text = "Habilitado";
        _chkBitcoinEnabled.AutoSize = true;
        _chkBitcoinEnabled.Font = UITheme.NormalBoldFont;

        _chkQuickPosEnabled.Text = "Habilitar acceso al Punto de venta rápido";
        _chkQuickPosEnabled.AutoSize = true;
        _chkQuickPosEnabled.Font = UITheme.NormalBoldFont;

        foreach (var textBox in new[] { _txtReceiptPrinter, _txtLabelPrinter, _txtCashDrawer })
            UITheme.StyleTextBox(textBox);

        _btnReportsFolder.Name = "btnReportsFolder";
        _btnReportsFolder.Text = "…";
        _btnReportsFolder.Margin = new Padding(8, 4, 0, 4);
        _btnReportsFolder.Click += (_, _) => SelectFolder(_txtReportsFolder);
        UITheme.StyleSecondaryButton(_btnReportsFolder);

        _btnMaintenance.Name = "btnBackupRestore";
        _btnMaintenance.Text = "Abrir respaldos y restauración";
        _btnMaintenance.Click += (_, _) =>
        {
            using var form = (BackupRestoreForm)_serviceProvider.GetService(typeof(BackupRestoreForm))!;
            form.ShowDialog(this);
        };
        UITheme.StyleSecondaryButton(_btnMaintenance);
        UITheme.EnsureButtonContentFits(_btnMaintenance, 330, 48, 48);

        _btnUpdates.Name = "btnUpdates";
        _btnUpdates.Text = "Abrir actualizaciones";
        _btnUpdates.Click += (_, _) =>
        {
            using var form = (UpdateCenterForm)_serviceProvider.GetService(typeof(UpdateCenterForm))!;
            form.ShowDialog(this);
        };
        UITheme.StyleSecondaryButton(_btnUpdates);
        UITheme.EnsureButtonContentFits(_btnUpdates, 330, 48, 48);

        _btnCloudProvisioning.Name = "btnCloudProvisioning";
        _btnCloudProvisioning.Text = "Aprovisionar uploader cloud";
        _btnCloudProvisioning.Click += (_, _) => OpenCloudProvisioning();
        UITheme.StyleSecondaryButton(_btnCloudProvisioning);
        UITheme.EnsureButtonContentFits(_btnCloudProvisioning, 330, 48, 48);

        _btnSave.Name = "btnSaveSettings";
        _btnSave.Text = "Guardar cambios";
        _btnSave.Click += BtnSave_Click;
        UITheme.StylePrimaryButton(_btnSave);
        UITheme.EnsureButtonContentFits(_btnSave, 140, 42);

        ConfigureMoney(_numCreditLimit);
        ConfigureInteger(_numMaxAccounts, 1, 99);
        ConfigureInteger(_numDefaultTerm, 1, 365);
        ConfigureInteger(_numEmployeeMaxTerm, 1, 365);
        ConfigureInteger(_numAdminMaxTerm, 1, 365);
        ConfigurePercent(_numInterest);
        ConfigureInteger(_numMorosoDays, 0, 365);
        ConfigureInteger(_numAlertUpcomingDays, 0, 365);
        ConfigureInteger(_numAlertNoPaymentDays, 1, 365);
        ConfigurePercent(_numAlertNearLimit);

        _chkAllowOutstanding.Text = "Permitir crédito con saldo vigente";
        _chkAllowOutstanding.AutoSize = true;
        _chkAllowOutstanding.Font = UITheme.NormalBoldFont;
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _configService.GetBasicSettingsAsync();
        var map = settings.ToDictionary(setting => setting.Clave, setting => setting.Valor);

        _txtBusinessName.Text = map.GetValueOrDefault(SystemConfigService.BusinessNameKey, string.Empty);
        _txtBusinessAddress.Text = map.GetValueOrDefault(SystemConfigService.BusinessAddressKey, string.Empty);
        _txtBusinessPhone.Text = map.GetValueOrDefault(SystemConfigService.BusinessPhoneKey, string.Empty);
        _txtBusinessEmail.Text = map.GetValueOrDefault(SystemConfigService.BusinessEmailKey, string.Empty);
        _txtCashOpeningTime.Text = map.GetValueOrDefault(SystemConfigService.AutomaticCashOpeningTimeKey, string.Empty);
        _chkBitcoinEnabled.Checked = bool.TryParse(
            map.GetValueOrDefault(SystemConfigService.BitcoinEnabledKey, "true"),
            out var bitcoinEnabled) && bitcoinEnabled;
        _txtReportsFolder.Text = _stationSettingsService is null
            ? map.GetValueOrDefault(SystemConfigService.ReportsFolderKey, string.Empty)
            : (await _stationSettingsService.GetReportSettingsAsync()).ReportsDirectory;
        _chkQuickPosEnabled.Checked = _stationSettingsService is not null &&
            (await _stationSettingsService.GetPosSettingsAsync()).QuickPosEnabled;
        if (_stationSettingsService is not null)
        {
            var peripherals = await _stationSettingsService.GetPeripheralSettingsAsync();
            _chkTicketsEnabled.Checked = peripherals.TicketsEnabled;
            _txtReceiptPrinter.Text = peripherals.ReceiptPrinterLogicalName ?? string.Empty;
            _chkLabelsEnabled.Checked = peripherals.LabelsEnabled;
            _txtLabelPrinter.Text = peripherals.LabelPrinterLogicalName ?? string.Empty;
            _chkCashDrawerEnabled.Checked = peripherals.CashDrawerEnabled;
            _txtCashDrawer.Text = peripherals.CashDrawerLogicalName ?? string.Empty;
        }
        UpdatePeripheralStatus(_chkTicketsEnabled, _txtReceiptPrinter, _lblReceiptStatus, "Tickets");
        UpdatePeripheralStatus(_chkLabelsEnabled, _txtLabelPrinter, _lblLabelStatus, "Etiquetas");
        UpdatePeripheralStatus(_chkCashDrawerEnabled, _txtCashDrawer, _lblCashDrawerStatus, "Gaveta");

        var credit = await _creditService.GetGeneralPolicyAsync();
        _numCreditLimit.Value = credit.DefaultLimitCentavos / 100m;
        _numMaxAccounts.Value = credit.DefaultMaxActiveAccounts;
        _numDefaultTerm.Value = credit.DefaultTermDays;
        _numEmployeeMaxTerm.Value = credit.EmployeeMaxTermDays;
        _numAdminMaxTerm.Value = credit.AdminMaxTermDays;
        _numInterest.Value = credit.DefaultInterestPercent;
        _numMorosoDays.Value = credit.DaysPastDueForMoroso;
        _chkAllowOutstanding.Checked = credit.AllowCreditWithOutstandingBalance;

        var alerts = await _creditAlertService.GetSettingsAsync();
        _numAlertUpcomingDays.Value = alerts.UpcomingDueDays;
        _numAlertNoPaymentDays.Value = alerts.NoPaymentDays;
        _numAlertNearLimit.Value = alerts.NearLimitPercent;
        _chkAlertDueToday.Checked = alerts.EnableDueToday;
        _chkAlertUpcomingDue.Checked = alerts.EnableUpcomingDue;
        _chkAlertOverdue.Checked = alerts.EnableOverdue;
        _chkAlertMoroso.Checked = alerts.EnableMoroso;
        _chkAlertNoRecentPayment.Checked = alerts.EnableNoRecentPayment;
        _chkAlertNearLimit.Checked = alerts.EnableNearLimit;
        _chkAlertLimitReached.Checked = alerts.EnableLimitReached;
    }

    private bool IsConsultaMode =>
        _installationContext?.Status.Mode == InstallationMode.Consulta;

    private async void OpenCloudProvisioning()
    {
        if (!_btnCloudProvisioning.Visible)
        {
            return;
        }

        _btnCloudProvisioning.Enabled = false;
        ArteInventariadoPathProvider? paths = null;
        CloudBackupProvisioningAuthorizationService? authorization = null;
        var handoffTransferred = false;
        try
        {
            paths = _serviceProvider.GetRequiredService<ArteInventariadoPathProvider>();
            if (!paths.IsMachineWide)
            {
                MessageBox.Show(
                    this,
                    "El uploader cloud protegido solo está disponible en una instalación machine-wide.",
                    "Aprovisionamiento cloud",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            authorization = _serviceProvider
                .GetRequiredService<CloudBackupProvisioningAuthorizationService>();
            if (!await authorization.CanCurrentSessionOpenAsync())
            {
                _btnCloudProvisioning.Visible = false;
                throw new UnauthorizedAccessException(
                    "Solo el Administrador Principal puede aprovisionar el uploader cloud.");
            }

            using var reauthentication = new CloudBackupProvisioningAuthorizationForm();
            if (reauthentication.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var grant = await authorization.ReauthenticateAndCreateGrantAsync(
                reauthentication.TakePassword(),
                reauthentication.ConfirmationAccepted);

            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("No se pudo resolver el ejecutable de ArteInventariado.");
            var elevationArguments = new List<string>
            {
                "--provision-cloud",
                "--machine-install-root",
                paths.MachineInstallRoot,
                "--machine-data-root",
                paths.MachineDataRoot
            };
            var elevation = MachineWideElevation.EnsureElevated(executable, elevationArguments);
            if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
            {
                authorization.DiscardPendingHandoff();
                MessageBox.Show(
                    this,
                    "El aprovisionamiento cloud fue cancelado en UAC. No se creó ni modificó ninguna credencial.",
                    "Aprovisionamiento cloud",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else if (elevation.Outcome == MachineWideElevationOutcome.RelaunchedElevated)
            {
                handoffTransferred = true;
                using var process = elevation.Process
                    ?? throw new InvalidOperationException("Windows no devolvió el proceso cloud elevado.");
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"La ventana elevada de aprovisionamiento cloud terminó con código {process.ExitCode}.");
                }

                MessageBox.Show(
                    this,
                    "Se solicitó UAC. La ventana elevada continuará el aprovisionamiento sin compartir secretos por argumentos.",
                    "Aprovisionamiento cloud",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else if (elevation.Outcome == MachineWideElevationOutcome.AlreadyElevated)
            {
                var consumedGrant = await authorization.ConsumeHandoffAsync();
                handoffTransferred = true;
                var provisioning = _serviceProvider.GetRequiredService<CloudBackupProvisioningService>();
                var configurationStore = _serviceProvider.GetRequiredService<CloudBackupConfigurationStore>();
                using var form = new CloudBackupProvisioningForm(
                    provisioning,
                    configurationStore,
                    consumedGrant);
                form.ShowDialog(this);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            if (!handoffTransferred && paths is not null)
            {
                authorization?.DiscardPendingHandoff();
            }

            MessageBox.Show(this, ex.Message, "Aprovisionamiento cloud", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnCloudProvisioning.Enabled = _btnCloudProvisioning.Visible;
            }
        }
    }

    private void ApplyInstallationModeRestrictions()
    {
        if (!IsConsultaMode)
        {
            return;
        }

        foreach (var control in new Control[]
        {
            _txtBusinessName,
            _txtBusinessAddress,
            _txtBusinessPhone,
            _txtBusinessEmail,
            _txtCashOpeningTime,
            _chkBitcoinEnabled,
            _numCreditLimit,
            _numMaxAccounts,
            _numDefaultTerm,
            _numEmployeeMaxTerm,
            _numAdminMaxTerm,
            _numInterest,
            _numMorosoDays,
            _numAlertUpcomingDays,
            _numAlertNoPaymentDays,
            _numAlertNearLimit,
            _chkAllowOutstanding,
            _chkAlertDueToday,
            _chkAlertUpcomingDue,
            _chkAlertOverdue,
            _chkAlertMoroso,
            _chkAlertNoRecentPayment,
            _chkAlertNearLimit,
            _chkAlertLimitReached
        })
        {
            control.Enabled = false;
        }

        _btnSave.Text = "Guardar ajustes locales";
        _btnSave.Enabled = _stationSettingsService is not null;
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            _btnSave.Enabled = false;
            if (!IsConsultaMode)
            {
                await _configService.UpdateBasicSettingsAsync(new UpdateSystemConfigRequest(
                [
                    new SystemConfigSetting(SystemConfigService.BusinessNameKey, _txtBusinessName.Text, null),
                    new SystemConfigSetting(SystemConfigService.BusinessAddressKey, _txtBusinessAddress.Text, null),
                    new SystemConfigSetting(SystemConfigService.BusinessPhoneKey, _txtBusinessPhone.Text, null),
                    new SystemConfigSetting(SystemConfigService.BusinessEmailKey, _txtBusinessEmail.Text, null),
                    new SystemConfigSetting(SystemConfigService.AutomaticCashOpeningTimeKey, _txtCashOpeningTime.Text, null),
                    new SystemConfigSetting(SystemConfigService.BitcoinEnabledKey, _chkBitcoinEnabled.Checked.ToString(), null)
                ]));
                await _creditService.UpdateGeneralPolicyAsync(new UpdateCreditPolicySettingsRequest(
                    DollarsToCentavos(_numCreditLimit.Value),
                    (int)_numMaxAccounts.Value,
                    (int)_numDefaultTerm.Value,
                    (int)_numEmployeeMaxTerm.Value,
                    (int)_numAdminMaxTerm.Value,
                    _numInterest.Value,
                    (int)_numMorosoDays.Value,
                    _chkAllowOutstanding.Checked));
                await _creditAlertService.UpdateSettingsAsync(new UpdateCreditAlertSettingsRequest(
                    (int)_numAlertUpcomingDays.Value,
                    (int)_numAlertNoPaymentDays.Value,
                    _numAlertNearLimit.Value,
                    _chkAlertDueToday.Checked,
                    _chkAlertUpcomingDue.Checked,
                    _chkAlertOverdue.Checked,
                    _chkAlertMoroso.Checked,
                    _chkAlertNoRecentPayment.Checked,
                    _chkAlertNearLimit.Checked,
                    _chkAlertLimitReached.Checked));
            }

            if (_stationSettingsService is not null)
            {
                await _stationSettingsService.UpdateReportSettingsAsync(
                    new UpdateLocalReportSettingsRequest(_txtReportsFolder.Text));
                await _stationSettingsService.UpdatePosSettingsAsync(
                    new UpdateLocalPosSettingsRequest(_chkQuickPosEnabled.Checked));
                await _stationSettingsService.UpdatePeripheralSettingsAsync(
                    new UpdateLocalPeripheralSettingsRequest(
                        _chkTicketsEnabled.Checked,
                        _txtReceiptPrinter.Text,
                        _chkLabelsEnabled.Checked,
                        _txtLabelPrinter.Text,
                        _chkCashDrawerEnabled.Checked,
                        _txtCashDrawer.Text));
            }

            MessageBox.Show(
                IsConsultaMode ? "Configuración local de esta PC guardada." : "Configuración guardada.",
                "Configuración",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }

    private static TableLayoutPanel CreateSection(string title, string description, Control content)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 16),
            Margin = new Padding(0, 0, 0, 14),
            BackColor = UITheme.SurfaceColor
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, 2)
        }, 0, 0);
        section.Controls.Add(new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 1);
        content.Dock = DockStyle.Top;
        section.Controls.Add(content, 0, 2);
        section.Paint += (_, args) => ControlPaint.DrawBorder(
            args.Graphics,
            section.ClientRectangle,
            UITheme.DashboardBorderColor,
            ButtonBorderStyle.Solid);
        return section;
    }

    private static TableLayoutPanel CreateContentGrid(int columns, int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            RowCount = rows,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        for (var row = 0; row < rows; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private static Control CreateNumericField(string labelText, NumericUpDown number)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 2, 16, 8)
        };
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);
        number.Dock = DockStyle.Top;
        number.MaximumSize = new Size(220, 0);
        field.Controls.Add(number, 0, 1);
        return field;
    }

    private static void AddFieldLabel(TableLayoutPanel layout, string text, int column, int row)
    {
        layout.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 7, 12, 7)
        }, column, row);
    }

    private static void AddCheck(TableLayoutPanel layout, CheckBox checkBox, string text, int column, int row)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.Dock = DockStyle.Fill;
        checkBox.Font = UITheme.NormalBoldFont;
        checkBox.Margin = new Padding(0, 7, 18, 7);
        layout.Controls.Add(checkBox, column, row);
    }

    private static void SelectFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(target.Text)
                ? target.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private static void ConfigureMoney(NumericUpDown number)
    {
        number.DecimalPlaces = 2;
        number.Minimum = 0;
        number.Maximum = 999999;
        number.ThousandsSeparator = true;
    }

    private static void ConfigureInteger(NumericUpDown number, int minimum, int maximum)
    {
        number.DecimalPlaces = 0;
        number.Minimum = minimum;
        number.Maximum = maximum;
    }

    private static void ConfigurePercent(NumericUpDown number)
    {
        number.DecimalPlaces = 2;
        number.Minimum = 0;
        number.Maximum = 100;
    }

    private static long DollarsToCentavos(decimal dollars) =>
        (long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);
}
