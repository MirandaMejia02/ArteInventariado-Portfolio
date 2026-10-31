using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Updater;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Reads installation evidence without creating the business database.  The
/// classifier is intentionally conservative: anything that looks like a
/// previously initialized or interrupted installation is never presented as
/// a fresh machine.
/// </summary>
public sealed class InstallationStateClassifier : IInstallationStateClassifier
{
    private readonly ArteInventariadoPathProvider _paths;
    private readonly ArteInventariadoPathProvider _legacyPaths;
    private readonly Func<IReadOnlyList<string>> _migrationsProvider;
    private readonly MachineWideBootstrapLogWriter? _logWriter;

    public InstallationStateClassifier(ArteInventariadoPathProvider paths)
        : this(
            paths,
            ArteInventariadoPathProvider.CreateLegacyDefault(),
            ReadCurrentMigrations)
    {
    }

    internal InstallationStateClassifier(
        ArteInventariadoPathProvider paths,
        ArteInventariadoPathProvider? legacyPaths,
        Func<IReadOnlyList<string>> migrationsProvider)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _legacyPaths = legacyPaths ?? ArteInventariadoPathProvider.CreateLegacyDefault();
        _migrationsProvider = migrationsProvider ?? throw new ArgumentNullException(nameof(migrationsProvider));
        _logWriter = _paths.IsMachineWide
            ? new MachineWideBootstrapLogWriter(_paths)
            : null;
    }

    public InstallationStateAssessment Classify()
    {
        var stationFileExists = File.Exists(_paths.StationSettingsPath);
        var stationResult = new LocalStationConfigurationStore(_paths).Load();
        var station = stationResult.Status;
        var databaseFileExists = File.Exists(_paths.DatabasePath);
        var databaseValid = databaseFileExists && IsDatabaseValid(_paths.DatabasePath);
        var currentPresent = File.Exists(_paths.CurrentStatePath);
        var currentPointerValid = !currentPresent || IsCurrentPointerValid();
        var updateStatePresent = File.Exists(_paths.UpdateStatePath);
        var bootstrapMarkerPresent = File.Exists(_paths.BootstrapStatePath);
        var cleanBootstrap = IsUninitializedCleanBootstrap(station, bootstrapMarkerPresent);
        var legacyDetected = IsRecognizedLegacyCandidate();
        var recoveryJournalPresent = HasRecoveryJournal();
        var installedBinariesPresent = HasInstalledBinaries();

        if (recoveryJournalPresent)
        {
            return Assessment(
                InstallationLifecycleState.RecoveryPending,
                station,
                stationFileExists,
                databaseFileExists,
                databaseValid,
                currentPointerValid,
                updateStatePresent,
                bootstrapMarkerPresent,
                legacyDetected,
                true,
                "Existe una recuperación protegida o un bootstrap de desastre pendiente.");
        }

        if (legacyDetected && !HasInitializedTargetEvidence(station, databaseFileExists, bootstrapMarkerPresent))
        {
            return Assessment(
                InstallationLifecycleState.MigrationCandidate,
                station,
                stationFileExists,
                databaseFileExists,
                databaseValid,
                currentPointerValid,
                updateStatePresent,
                bootstrapMarkerPresent,
                true,
                false,
                "Se encontró una instalación legacy v1.11 reconocida que puede migrarse.");
        }

        if (databaseValid &&
            station.State == StationConfigurationState.RequiresSetup &&
            !station.IsFirstRunCompleted &&
            !HasProtectedAdministrator())
        {
            return Assessment(
                InstallationLifecycleState.PrincipalProvisioningRequired,
                station,
                stationFileExists,
                databaseFileExists,
                true,
                currentPointerValid,
                updateStatePresent,
                bootstrapMarkerPresent,
                legacyDetected,
                false,
                "La instalación estructural está lista y requiere provisionar el Administrador Principal.");
        }

        if (databaseValid &&
            station.State == StationConfigurationState.RequiresSetup &&
            !station.IsFirstRunCompleted &&
            HasProtectedAdministrator())
        {
            return Assessment(
                InstallationLifecycleState.SetupPending,
                station,
                stationFileExists,
                databaseFileExists,
                true,
                currentPointerValid,
                updateStatePresent,
                bootstrapMarkerPresent,
                legacyDetected,
                false,
                "La instalación es válida y tiene un Administrador Principal; debe reanudarse la configuración de esta estación.");
        }

        if (cleanBootstrap && !databaseFileExists)
        {
            return Assessment(
                InstallationLifecycleState.FreshMachine,
                station,
                stationFileExists,
                false,
                false,
                currentPointerValid,
                updateStatePresent,
                bootstrapMarkerPresent,
                legacyDetected,
                false,
                null);
        }

        if (!HasAnyAuthoritativeEvidence(
                stationFileExists,
                databaseFileExists,
                currentPresent,
                updateStatePresent,
                bootstrapMarkerPresent,
                installedBinariesPresent))
        {
            return Assessment(
                InstallationLifecycleState.FreshMachine,
                station,
                false,
                false,
                false,
                true,
                false,
                false,
                legacyDetected,
                false,
                null);
        }

        if (station.IsConfigured && databaseValid && currentPointerValid)
        {
            return Assessment(
                InstallationLifecycleState.ExistingValidInstallation,
                station,
                stationFileExists,
                databaseFileExists,
                true,
                currentPointerValid,
                updateStatePresent,
                bootstrapMarkerPresent,
                legacyDetected,
                false,
                null);
        }

        return Assessment(
            InstallationLifecycleState.ExistingDamagedInstallation,
            station,
            stationFileExists,
            databaseFileExists,
            databaseValid,
            currentPointerValid,
            updateStatePresent,
            bootstrapMarkerPresent,
            legacyDetected,
            false,
            station.Problem ??
            (!databaseFileExists
                ? "La base de datos de una instalación existente no está disponible."
                : "La instalación existente no superó las comprobaciones de integridad."));
    }

    private InstallationStateAssessment Assessment(
        InstallationLifecycleState state,
        InstallationStatus station,
        bool stationFileExists,
        bool databaseFileExists,
        bool databaseValid,
        bool currentPointerValid,
        bool updateStatePresent,
        bool bootstrapMarkerPresent,
        bool legacyDetected,
        bool recoveryJournalPresent,
        string? problem) => new(
        state,
        station,
        stationFileExists,
        databaseFileExists,
        databaseValid,
        currentPointerValid,
        updateStatePresent,
        bootstrapMarkerPresent,
        legacyDetected,
        recoveryJournalPresent,
        problem);

    private bool IsCurrentPointerValid()
    {
        UpdateVersionPointer? pointer = null;
        try
        {
            pointer = new UpdatePointerStore(_paths.CurrentStatePath).Load();
            if (pointer is null)
            {
                _logWriter?.IntegrityFailure(
                    "current-pointer",
                    "current.json válido",
                    "ausente",
                    null,
                    _paths.CurrentStatePath);
                return false;
            }

            _ = new UpdateLaunchTargetResolver(UpdateRuntimePaths.Create(_paths)).ResolveCurrent(pointer);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileNotFoundException)
        {
            _logWriter?.IntegrityFailure(
                "current-pointer",
                "ejecutable administrado existente y coherente",
                ex.GetType().Name,
                pointer?.Version,
                pointer?.ExecutablePath ?? _paths.CurrentStatePath,
                ex);
            return false;
        }
    }

    private bool IsDatabaseValid(string databasePath)
    {
        try
        {
            var errors = new BackupPackageEngine()
                .ValidateDatabaseAsync(
                    databasePath,
                    _migrationsProvider(),
                    // An existing database may legitimately be one or more
                    // migrations behind the executable that is starting. It
                    // is still a valid installation: DatabaseInitializer is
                    // the component responsible for applying those pending
                    // migrations. ValidateDatabaseAsync continues to reject
                    // newer schemas and non-matching migration prefixes.
                    allowOlderSchema: true,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return errors.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            return false;
        }
    }

    private bool HasProtectedAdministrator()
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _paths.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT 1 FROM usuarios WHERE nombre_usuario = $user LIMIT 1;";
            command.Parameters.AddWithValue("$user", ProtectedAdministrator.UserName);
            return command.ExecuteScalar() is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            // A database that cannot be inspected must never be offered for
            // owner provisioning; the normal damaged-installation path will
            // handle it conservatively.
            return true;
        }
    }

    private bool IsRecognizedLegacyCandidate()
    {
        if (!_paths.IsMachineWide ||
            PathsEqual(_paths.MachineDataRoot, _legacyPaths.MachineDataRoot) ||
            !File.Exists(_legacyPaths.DatabasePath) ||
            !File.Exists(_legacyPaths.StationSettingsPath))
        {
            return false;
        }

        try
        {
            var status = new LocalStationConfigurationStore(_legacyPaths).Load().Status;
            return status.IsConfigured && IsLegacyDatabaseValid();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private bool IsLegacyDatabaseValid()
    {
        var errors = new BackupPackageEngine()
            .ValidateDatabaseAsync(
                _legacyPaths.DatabasePath,
                _migrationsProvider(),
                allowOlderSchema: true,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return errors.Count == 0;
    }

    private bool IsUninitializedCleanBootstrap(InstallationStatus station, bool markerPresent)
    {
        if (!_paths.IsMachineWide || !markerPresent || station.IsConfigured || station.State != StationConfigurationState.RequiresSetup)
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_paths.BootstrapStatePath);
            var marker = JsonSerializer.Deserialize<MachineWideBootstrapMarker>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return marker is not null &&
                   marker.SchemaVersion == 1 &&
                   marker.InstallationId is null &&
                   !string.IsNullOrWhiteSpace(marker.CurrentVersion) &&
                   IsCurrentPointerValid();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private bool HasRecoveryJournal()
    {
        try
        {
            var h1Store = new ProtectedRestoreJournalStore(_paths.RecoveryRoot);
            foreach (var operation in h1Store.EnumerateOperationDirectories())
            {
                try
                {
                    var journal = h1Store.Load(operation);
                    if (journal.Phase is not ProtectedRestoreJournalPhase.Succeeded and not ProtectedRestoreJournalPhase.RolledBack)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    return true;
                }
            }

            return DisasterRecoveryJournalStore.HasPending(_paths.RecoveryRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return true;
        }
    }

    private bool HasInitializedTargetEvidence(
        InstallationStatus station,
        bool databaseFileExists,
        bool bootstrapMarkerPresent) =>
        station.IsConfigured ||
        databaseFileExists ||
        bootstrapMarkerPresent ||
        File.Exists(_paths.CurrentStatePath) ||
        File.Exists(_paths.UpdateStatePath);

    private bool HasAnyAuthoritativeEvidence(
        bool stationFileExists,
        bool databaseFileExists,
        bool currentPresent,
        bool updateStatePresent,
        bool bootstrapMarkerPresent,
        bool installedBinariesPresent) =>
        stationFileExists ||
        databaseFileExists ||
        currentPresent ||
        updateStatePresent ||
        bootstrapMarkerPresent ||
        installedBinariesPresent;

    private bool HasInstalledBinaries()
    {
        try
        {
            foreach (var path in new[]
                     {
                         _paths.LauncherPath,
                         _paths.UpdaterPath,
                         _paths.StationHelperPath,
                         _paths.BackupRunnerPath,
                         _paths.CloudBackupRunnerPath
                     })
            {
                if (File.Exists(path))
                {
                    return true;
                }
            }

            // An interrupted deployment can leave a version directory while
            // the current pointer is missing. The directory itself is
            // installation evidence, but an empty root created by the
            // installer is not.
            return Directory.Exists(_paths.VersionsRoot) &&
                   Directory.EnumerateFileSystemEntries(_paths.VersionsRoot).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // An unreadable installation root is evidence of a damaged
            // installation; never downgrade it to FreshMachine.
            return true;
        }
    }

    private static IReadOnlyList<string> ReadCurrentMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new AppDbContext(options);
        return context.Database.GetMigrations().ToList();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
