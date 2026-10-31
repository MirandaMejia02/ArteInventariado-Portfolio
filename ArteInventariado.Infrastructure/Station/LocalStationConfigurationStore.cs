using System.Text.Json;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Station;

internal sealed class LocalStationConfigurationStore
{
    internal const int CurrentSchemaVersion = 5;
    private const long MaximumConfigurationBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _configurationPath;
    private readonly string _operationalConfigurationPath;
    private readonly bool _isMachineWideSplit;
    private readonly TimeProvider _timeProvider;

    public LocalStationConfigurationStore()
        : this(ArteInventariadoPathProvider.CreateDefault(), TimeProvider.System)
    {
    }

    internal LocalStationConfigurationStore(
        ArteInventariadoPathProvider paths,
        TimeProvider? timeProvider = null)
        : this(
            paths.StationCorePath,
            paths.StationOperationalSettingsPath,
            timeProvider,
            paths.IsMachineWide)
    {
    }

    internal LocalStationConfigurationStore(string configurationPath, TimeProvider? timeProvider = null)
        : this(configurationPath, configurationPath, timeProvider, isMachineWideSplit: false)
    {
    }

    private LocalStationConfigurationStore(
        string configurationPath,
        string operationalConfigurationPath,
        TimeProvider? timeProvider,
        bool isMachineWideSplit)
    {
        _configurationPath = Path.GetFullPath(configurationPath);
        _operationalConfigurationPath = Path.GetFullPath(operationalConfigurationPath);
        _isMachineWideSplit = isMachineWideSplit &&
            !string.Equals(_configurationPath, _operationalConfigurationPath, StringComparison.OrdinalIgnoreCase);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string ConfigurationPath => _configurationPath;

    internal bool IsMachineWideSplit => _isMachineWideSplit;

    internal string OperationalConfigurationPath => _operationalConfigurationPath;

    public StationConfigurationLoadResult Load()
    {
        lock (_sync)
        {
            return LoadCore();
        }
    }

    public StationConfigurationLoadResult EnsureInitialized()
    {
        lock (_sync)
        {
            var current = LoadCore();
            if (File.Exists(_configurationPath))
            {
                return current;
            }

            var document = new StationConfigurationDocument(
                CurrentSchemaVersion,
                Guid.NewGuid(),
                InstallationMode: null,
                IsFirstRunCompleted: false,
                Backup: null,
                Reports: null,
                Pos: null,
                Peripherals: null);
            if (_isMachineWideSplit)
            {
                WriteCoreAtomically(document);
            }
            else
            {
                WriteAtomically(document, _configurationPath);
            }
            return LoadCore();
        }
    }

    public StationConfigurationDocument SaveConfigured(InstallationMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "El modo de instalacion no es valido.");
        }

        lock (_sync)
        {
            var current = LoadCore();
            if (current.Status.State is StationConfigurationState.Corrupt or
                StationConfigurationState.Invalid or
                StationConfigurationState.Unsupported)
            {
                ArchiveInvalidConfiguration();
            }

            var installationId = current.Document?.InstallationId is { } id && id != Guid.Empty
                ? id
                : Guid.NewGuid();
            var document = new StationConfigurationDocument(
                CurrentSchemaVersion,
                installationId,
                mode.ToString(),
                IsFirstRunCompleted: true,
                current.Document?.Backup,
                current.Document?.Reports,
                current.Document?.Pos,
                current.Document?.Peripherals);
            if (_isMachineWideSplit)
            {
                WriteCoreAtomically(document);
            }
            else
            {
                WriteAtomically(document, _configurationPath);
            }
            return document;
        }
    }

    /// <summary>
    /// Materializes a new identity while the station is still unconfigured.
    /// This is used only by the elevated disaster-recovery bootstrap before
    /// the protected mode is committed.  It deliberately refuses to replace a
    /// configured identity, so an existing installation cannot be converted
    /// into a fresh-machine flow.
    /// </summary>
    internal StationConfigurationDocument SetFreshIdentity(Guid installationId)
    {
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("La identidad de la estación no puede estar vacía.", nameof(installationId));
        }

        lock (_sync)
        {
            var current = LoadCore();
            if (current.Status.IsConfigured)
            {
                throw new InvalidOperationException(
                    "No se puede regenerar la identidad de una estación ya configurada.");
            }

            var document = new StationConfigurationDocument(
                CurrentSchemaVersion,
                installationId,
                InstallationMode: null,
                IsFirstRunCompleted: false,
                current.Document?.Backup,
                current.Document?.Reports,
                current.Document?.Pos,
                current.Document?.Peripherals);
            if (_isMachineWideSplit)
            {
                WriteCoreAtomically(document);
            }
            else
            {
                WriteAtomically(document, _configurationPath);
            }

            return document;
        }
    }

    internal BackupStationSettingsDocument? GetBackupSettings()
    {
        lock (_sync)
        {
            return LoadCore().Document?.Backup;
        }
    }

    internal void SaveBackupSettings(BackupStationSettingsDocument backup)
    {
        lock (_sync)
        {
            var current = LoadCore();
            var document = current.Document
                ?? throw new InvalidOperationException("La configuracion local de la estacion no esta disponible.");
            SaveOperationalSettings(document with
            {
                SchemaVersion = CurrentSchemaVersion,
                Backup = backup
            });
        }
    }

    internal ReportStationSettingsDocument? GetReportSettings()
    {
        lock (_sync)
        {
            return LoadCore().Document?.Reports;
        }
    }

    internal void SaveReportSettings(ReportStationSettingsDocument reports)
    {
        lock (_sync)
        {
            var current = LoadCore();
            var document = current.Document
                ?? throw new InvalidOperationException("La configuracion local de la estacion no esta disponible.");
            SaveOperationalSettings(document with
            {
                SchemaVersion = CurrentSchemaVersion,
                Reports = reports
            });
        }
    }

    internal PosStationSettingsDocument? GetPosSettings()
    {
        lock (_sync)
        {
            return LoadCore().Document?.Pos;
        }
    }

    internal void SavePosSettings(PosStationSettingsDocument pos)
    {
        lock (_sync)
        {
            var current = LoadCore();
            var document = current.Document
                ?? throw new InvalidOperationException("La configuracion local de la estacion no esta disponible.");
            SaveOperationalSettings(document with
            {
                SchemaVersion = CurrentSchemaVersion,
                Pos = pos
            });
        }
    }

    internal PeripheralStationSettingsDocument? GetPeripheralSettings()
    {
        lock (_sync)
        {
            return LoadCore().Document?.Peripherals;
        }
    }

    internal void SavePeripheralSettings(PeripheralStationSettingsDocument peripherals)
    {
        lock (_sync)
        {
            var current = LoadCore();
            var document = current.Document
                ?? throw new InvalidOperationException("La configuracion local de la estacion no esta disponible.");
            SaveOperationalSettings(document with
            {
                SchemaVersion = CurrentSchemaVersion,
                Peripherals = peripherals
            });
        }
    }

    private StationConfigurationLoadResult LoadCore()
    {
        if (!File.Exists(_configurationPath))
        {
            return Result(
                StationConfigurationState.RequiresSetup,
                null,
                false,
                null,
                null,
                "La configuracion local de esta estacion aun no existe.",
                null);
        }

        try
        {
            var file = new FileInfo(_configurationPath);
            if (file.Length is <= 0 or > MaximumConfigurationBytes)
            {
                return Result(
                    StationConfigurationState.Invalid,
                    null,
                    false,
                    null,
                    null,
                    "El archivo de configuracion local tiene un tamano invalido.",
                    null);
            }

            var json = File.ReadAllText(_configurationPath);
            var document = JsonSerializer.Deserialize<StationConfigurationDocument>(json, SerializerOptions);
            if (document is null)
            {
                return Result(
                    StationConfigurationState.Corrupt,
                    null,
                    false,
                    null,
                    null,
                    "El archivo de configuracion local esta vacio o corrupto.",
                    null);
            }

            if (_isMachineWideSplit && File.Exists(_operationalConfigurationPath))
            {
                var operationalFile = new FileInfo(_operationalConfigurationPath);
                if (operationalFile.Length is <= 0 or > MaximumConfigurationBytes)
                {
                    return Result(
                        StationConfigurationState.Invalid,
                        null,
                        document.IsFirstRunCompleted,
                        document.InstallationId,
                        document.SchemaVersion,
                        "El archivo de configuracion operativa de la estacion tiene un tamano invalido.",
                        document);
                }

                var operational = JsonSerializer.Deserialize<StationOperationalConfigurationDocument>(
                    File.ReadAllText(_operationalConfigurationPath),
                    SerializerOptions);
                if (operational is null ||
                    operational.SchemaVersion is < 1 or > CurrentSchemaVersion)
                {
                    return Result(
                        StationConfigurationState.Invalid,
                        null,
                        document.IsFirstRunCompleted,
                        document.InstallationId,
                        document.SchemaVersion,
                        "El archivo de configuracion operativa de la estacion no es compatible.",
                        document);
                }

                document = document with
                {
                    Backup = operational.Backup,
                    Reports = operational.Reports,
                    Pos = operational.Pos,
                    Peripherals = operational.Peripherals
                };
            }

            if (document.SchemaVersion is < 1 or > CurrentSchemaVersion)
            {
                return Result(
                    StationConfigurationState.Unsupported,
                    null,
                    document.IsFirstRunCompleted,
                    document.InstallationId,
                    document.SchemaVersion,
                    $"La version {document.SchemaVersion} de la configuracion local no es compatible.",
                    document);
            }

            if (document.SchemaVersion < CurrentSchemaVersion)
            {
                document = document with { SchemaVersion = CurrentSchemaVersion };
                if (_isMachineWideSplit)
                {
                    WriteCoreAtomically(document);
                }
                else
                {
                    WriteAtomically(document, _configurationPath);
                }
            }

            if (document.InstallationId == Guid.Empty)
            {
                return Result(
                    StationConfigurationState.Invalid,
                    null,
                    document.IsFirstRunCompleted,
                    null,
                    document.SchemaVersion,
                    "La identidad local de la estacion no es valida.",
                    document);
            }

            if (!document.IsFirstRunCompleted && string.IsNullOrWhiteSpace(document.InstallationMode))
            {
                return Result(
                    StationConfigurationState.RequiresSetup,
                    null,
                    false,
                    document.InstallationId,
                    document.SchemaVersion,
                    "La configuracion inicial de esta estacion esta pendiente.",
                    document);
            }

            if (!document.IsFirstRunCompleted ||
                !Enum.TryParse<InstallationMode>(document.InstallationMode, ignoreCase: true, out var mode) ||
                !Enum.IsDefined(mode))
            {
                return Result(
                    StationConfigurationState.Invalid,
                    null,
                    document.IsFirstRunCompleted,
                    document.InstallationId,
                    document.SchemaVersion,
                    "El modo de instalacion almacenado no es valido.",
                    document);
            }

            return Result(
                StationConfigurationState.Ready,
                mode,
                true,
                document.InstallationId,
                document.SchemaVersion,
                null,
                document);
        }
        catch (JsonException)
        {
            return Result(
                StationConfigurationState.Corrupt,
                null,
                false,
                null,
                null,
                "El archivo de configuracion local no contiene JSON valido.",
                null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result(
                StationConfigurationState.Corrupt,
                null,
                false,
                null,
                null,
                "No fue posible leer la configuracion local de esta estacion.",
                null);
        }
    }

    private void SaveOperationalSettings(StationConfigurationDocument document)
    {
        if (!_isMachineWideSplit)
        {
            WriteAtomically(document, _configurationPath);
            return;
        }

        var operational = new StationOperationalConfigurationDocument(
            CurrentSchemaVersion,
            document.Backup,
            document.Reports,
            document.Pos,
            document.Peripherals);
        WriteAtomically(operational, _operationalConfigurationPath);
    }

    private void WriteCoreAtomically(StationConfigurationDocument document)
    {
        var core = new StationCoreConfigurationDocument(
            CurrentSchemaVersion,
            document.InstallationId,
            document.InstallationMode,
            document.IsFirstRunCompleted);
        WriteAtomically(core, _configurationPath);
    }

    private StationConfigurationLoadResult Result(
        StationConfigurationState state,
        InstallationMode? mode,
        bool completed,
        Guid? installationId,
        int? schemaVersion,
        string? problem,
        StationConfigurationDocument? document)
    {
        return new StationConfigurationLoadResult(
            new InstallationStatus(
                state,
                mode,
                completed,
                installationId,
                schemaVersion,
                _configurationPath,
                problem),
            document);
    }

    private void ArchiveInvalidConfiguration()
    {
        if (!File.Exists(_configurationPath))
        {
            return;
        }

        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd_HHmmss_fff");
        var archivePath = $"{_configurationPath}.corrupt-{timestamp}.bak";
        File.Copy(_configurationPath, archivePath, overwrite: false);
    }

    private void WriteAtomically(object document, string path)
    {
        var targetPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("La ruta de configuracion local no tiene directorio.");
        EnsureNoReparsePoints(directory);
        Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(targetPath);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"La ruta de configuracion local contiene un reparse point: '{current}'.");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidDataException(
                    $"No se pudo inspeccionar la ruta de configuracion local: '{current}'.",
                    ex);
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }
}

internal sealed record StationConfigurationDocument(
    int SchemaVersion,
    Guid InstallationId,
    string? InstallationMode,
    bool IsFirstRunCompleted,
    BackupStationSettingsDocument? Backup,
    ReportStationSettingsDocument? Reports = null,
    PosStationSettingsDocument? Pos = null,
    PeripheralStationSettingsDocument? Peripherals = null);

internal sealed record StationCoreConfigurationDocument(
    int SchemaVersion,
    Guid InstallationId,
    string? InstallationMode,
    bool IsFirstRunCompleted);

internal sealed record StationOperationalConfigurationDocument(
    int SchemaVersion,
    BackupStationSettingsDocument? Backup,
    ReportStationSettingsDocument? Reports,
    PosStationSettingsDocument? Pos,
    PeripheralStationSettingsDocument? Peripherals);

internal sealed record BackupStationSettingsDocument(
    bool AutomaticEnabled,
    string DestinationDirectory,
    string Frequency,
    int AutomaticRetentionCount,
    DateTimeOffset? LastAutomaticBackupUtc,
    string? LastAutomaticError);

internal sealed record ReportStationSettingsDocument(
    string DestinationDirectory);

internal sealed record PosStationSettingsDocument(
    bool QuickPosEnabled);

internal sealed record PeripheralStationSettingsDocument(
    bool TicketsEnabled,
    string? ReceiptPrinterLogicalName,
    bool LabelsEnabled,
    string? LabelPrinterLogicalName,
    bool CashDrawerEnabled,
    string? CashDrawerLogicalName);

internal sealed record StationConfigurationLoadResult(
    InstallationStatus Status,
    StationConfigurationDocument? Document);
