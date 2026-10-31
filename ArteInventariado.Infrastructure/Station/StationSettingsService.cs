using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Data;

namespace ArteInventariado.Infrastructure.Station;

internal sealed class StationSettingsService : IStationSettingsService
{
    private readonly LocalStationConfigurationStore _store;
    private readonly ICurrentSession _currentSession;
    private readonly IInstallationContext _installationContext;
    private readonly StationEventLog _eventLog;
    private readonly ArteInventariadoPathProvider _paths;

    public StationSettingsService(
        LocalStationConfigurationStore store,
        ICurrentSession currentSession,
        IInstallationContext installationContext,
        StationEventLog eventLog,
        ArteInventariadoPathProvider? paths = null)
    {
        _store = store;
        _currentSession = currentSession;
        _installationContext = installationContext;
        _eventLog = eventLog;
        _paths = paths ?? ArteInventariadoPathProvider.CreateDefault();
    }

    public Task<LocalReportSettings> GetReportSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configured = _store.GetReportSettings()?.DestinationDirectory;
        var directory = string.IsNullOrWhiteSpace(configured)
            ? StationConfigurationPathProvider.GetDefaultReportsDirectory()
            : configured.Trim();

        return Task.FromResult(new LocalReportSettings(directory, _store.ConfigurationPath));
    }

    public Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_paths.IsMachineWide)
        {
            // The operational station document is user-writable in the
            // machine-wide layout. It is never authoritative for protected
            // backup placement; the installer-owned root is the only valid
            // destination exposed to application services.
            return Task.FromResult(_paths.ProtectedBackupsRoot);
        }

        var configured = _store.GetBackupSettings()?.DestinationDirectory;
        var directory = string.IsNullOrWhiteSpace(configured)
            ? BackupPathProvider.GetBackupDirectory(_paths)
            : configured.Trim();
        return Task.FromResult(directory);
    }

    public Task<LocalPosSettings> GetPosSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var enabled = _store.GetPosSettings()?.QuickPosEnabled ?? false;
        return Task.FromResult(new LocalPosSettings(enabled, _store.ConfigurationPath));
    }

    public Task<LocalPeripheralSettings> GetPeripheralSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _store.GetPeripheralSettings();
        return Task.FromResult(new LocalPeripheralSettings(
            settings?.TicketsEnabled ?? false,
            NormalizeLogicalName(settings?.ReceiptPrinterLogicalName),
            settings?.LabelsEnabled ?? false,
            NormalizeLogicalName(settings?.LabelPrinterLogicalName),
            settings?.CashDrawerEnabled ?? false,
            NormalizeLogicalName(settings?.CashDrawerLogicalName),
            _store.ConfigurationPath));
    }

    public async Task UpdateReportSettingsAsync(
        UpdateLocalReportSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAdminSession();

        var directory = NormalizeDirectory(request.ReportsDirectory);
        Directory.CreateDirectory(directory);
        _store.SaveReportSettings(new ReportStationSettingsDocument(directory));

        _eventLog.Write(
            "LOCAL_REPORT_DIRECTORY_UPDATED",
            _currentSession.UsuarioActual!.NombreUsuario,
            _installationContext.Status.Mode,
            $"La carpeta local de reportes se actualizo a '{directory}'.");

        await Task.CompletedTask;
    }

    public async Task UpdatePosSettingsAsync(
        UpdateLocalPosSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAdminSession();

        _store.SavePosSettings(new PosStationSettingsDocument(request.QuickPosEnabled));
        _eventLog.Write(
            "LOCAL_QUICK_POS_UPDATED",
            _currentSession.UsuarioActual!.NombreUsuario,
            _installationContext.Status.Mode,
            request.QuickPosEnabled
                ? "El POS rapido fue habilitado para esta estacion."
                : "El POS rapido fue deshabilitado para esta estacion.");

        await Task.CompletedTask;
    }

    public async Task UpdatePeripheralSettingsAsync(
        UpdateLocalPeripheralSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAdminSession();

        _store.SavePeripheralSettings(new PeripheralStationSettingsDocument(
            request.TicketsEnabled,
            NormalizeLogicalName(request.ReceiptPrinterLogicalName),
            request.LabelsEnabled,
            NormalizeLogicalName(request.LabelPrinterLogicalName),
            request.CashDrawerEnabled,
            NormalizeLogicalName(request.CashDrawerLogicalName)));
        _eventLog.Write(
            "LOCAL_PERIPHERALS_UPDATED",
            _currentSession.UsuarioActual!.NombreUsuario,
            _installationContext.Status.Mode,
            "La configuracion local de tickets, etiquetas y gaveta fue actualizada.");

        await Task.CompletedTask;
    }

    private void EnsureAdminSession()
    {
        if (!_currentSession.EsAdmin || _currentSession.UsuarioActual is null)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede modificar la configuracion local de la estacion.");
        }
    }

    private static string NormalizeDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("La carpeta local de reportes es obligatoria.", nameof(value));
        }

        try
        {
            return Path.GetFullPath(value.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("La carpeta local de reportes no tiene una ruta valida.", nameof(value), ex);
        }
    }

    private static string? NormalizeLogicalName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 160)
            throw new ArgumentException("El nombre logico del periferico no puede exceder 160 caracteres.", nameof(value));
        return normalized;
    }
}
