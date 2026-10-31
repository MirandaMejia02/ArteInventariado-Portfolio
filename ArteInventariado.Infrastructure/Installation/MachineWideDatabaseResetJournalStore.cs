using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;

namespace ArteInventariado.Infrastructure.Installation;

public enum MachineWideDatabaseResetPhase
{
    BackingUp = 1,
    Prepared = 2,
    Provisioning = 3,
    RollingBack = 4,
    Committed = 5
}

public sealed record MachineWideDatabaseResetJournal(
    int SchemaVersion,
    Guid OperationId,
    Guid InstallationId,
    InstallationMode StationMode,
    MachineWideDatabaseResetPhase Phase,
    DateTimeOffset StartedUtc,
    string BackupFileName,
    string BackupSha256);

/// <summary>
/// Protected recovery journal for a database-only reset. Paths are derived
/// from the operation id; no journal field can direct deletion outside the
/// fixed recovery directory.
/// </summary>
public static class MachineWideDatabaseResetJournalStore
{
    public const int CurrentSchemaVersion = 1;
    private const string DirectoryName = "DatabaseReset";
    private const string JournalFileName = "journal.json";
    private const string StagingDirectoryName = "Staging";
    private const long MaximumJournalBytes = 16 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ResetRoot(ArteInventariadoPathProvider paths) =>
        Path.Combine(paths.RecoveryRoot, DirectoryName);

    public static string JournalPath(ArteInventariadoPathProvider paths) =>
        Path.Combine(ResetRoot(paths), JournalFileName);

    public static string StagedDatabasePath(ArteInventariadoPathProvider paths, Guid operationId)
    {
        EnsureOperationId(operationId);
        return Path.Combine(
            ResetRoot(paths),
            StagingDirectoryName,
            operationId.ToString("N"),
            ArteInventariadoPathProvider.DatabaseFileName);
    }

    public static bool HasPending(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            EnsureSafeRoots(paths, allowSyntheticRoots: false);
            var path = JournalPath(paths);
            EnsureNoReparsePoints(paths.RecoveryRoot, path);
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Unsafe or unreadable recovery state is a hard startup stop.
            return true;
        }
    }

    public static void EnsureUpdaterMayProceed(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (HasPending(paths))
        {
            throw new InvalidOperationException(
                "Hay un reinicio de base de datos pendiente. El updater no puede continuar hasta completar o recuperar esa operación.");
        }
    }

    internal static bool HasPendingSynthetic(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            EnsureSafeRoots(paths, allowSyntheticRoots: true);
            var path = JournalPath(paths);
            EnsureNoReparsePoints(paths.RecoveryRoot, path);
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return true;
        }
    }

    internal static void EnsureUpdaterMayProceedSynthetic(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (HasPendingSynthetic(paths))
        {
            throw new InvalidOperationException(
                "Hay un reinicio de base de datos pendiente. El updater no puede continuar hasta completar o recuperar esa operación.");
        }
    }

    public static MachineWideDatabaseResetJournal? Load(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureSafeRoots(paths, allowSyntheticRoots: false);
        return LoadCore(paths);
    }

    internal static MachineWideDatabaseResetJournal? LoadSynthetic(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureSafeRoots(paths, allowSyntheticRoots: true);
        return LoadCore(paths);
    }

    public static bool IsProvisioningRequest(
        ArteInventariadoPathProvider paths,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide || operationId == Guid.Empty)
        {
            return false;
        }

        try
        {
            var journal = Load(paths);
            if (journal is null ||
                journal.OperationId != operationId ||
                journal.Phase != MachineWideDatabaseResetPhase.Provisioning ||
                journal.StationMode != InstallationMode.Principal ||
                !File.Exists(paths.DatabasePath) ||
                !File.Exists(StagedDatabasePath(paths, operationId)))
            {
                return false;
            }

            var station = new LocalStationConfigurationStore(paths).Load().Status;
            return station.IsConfigured &&
                   station.Mode == InstallationMode.Principal &&
                   station.InstallationId == journal.InstallationId &&
                   HasNoUsers(paths.DatabasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return false;
        }
    }

    internal static bool IsProvisioningRequestSynthetic(
        ArteInventariadoPathProvider paths,
        Guid operationId)
    {
        try
        {
            var journal = LoadSynthetic(paths);
            if (journal is null ||
                journal.OperationId != operationId ||
                journal.Phase != MachineWideDatabaseResetPhase.Provisioning ||
                journal.StationMode != InstallationMode.Principal ||
                !File.Exists(paths.DatabasePath) ||
                !File.Exists(StagedDatabasePath(paths, operationId)))
            {
                return false;
            }

            var station = new LocalStationConfigurationStore(paths).Load().Status;
            return station.IsConfigured &&
                   station.Mode == InstallationMode.Principal &&
                   station.InstallationId == journal.InstallationId &&
                   HasNoUsers(paths.DatabasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return false;
        }
    }

    internal static void Save(
        ArteInventariadoPathProvider paths,
        MachineWideDatabaseResetJournal journal,
        bool allowSyntheticRoots = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(journal);
        EnsureSafeRoots(paths, allowSyntheticRoots);
        Validate(journal);

        var root = ResetRoot(paths);
        Directory.CreateDirectory(root);
        var journalPath = JournalPath(paths);
        EnsureNoReparsePoints(paths.RecoveryRoot, journalPath);
        var temporaryPath = journalPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, journal, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, journalPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    internal static void DeleteJournal(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots = false)
    {
        EnsureSafeRoots(paths, allowSyntheticRoots);
        var journalPath = JournalPath(paths);
        EnsureNoReparsePoints(paths.RecoveryRoot, journalPath);
        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }
    }

    internal static void EnsureCommittedBackupIsPresent(
        ArteInventariadoPathProvider paths,
        MachineWideDatabaseResetJournal journal,
        bool allowSyntheticRoots = false)
    {
        EnsureSafeRoots(paths, allowSyntheticRoots);
        Validate(journal);
        var backupRoot = Path.GetFullPath(paths.ProtectedBackupsRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var backupPath = Path.GetFullPath(Path.Combine(backupRoot, journal.BackupFileName));
        if (!string.Equals(Path.GetDirectoryName(backupPath), backupRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La ruta del respaldo guardado en el diario no es válida.");
        }

        EnsureNoReparsePoints(paths.MachineDataRoot, backupPath);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException(
                "El respaldo protegido del reinicio ya no está disponible; se conserva la DB anterior en recuperación.",
                backupPath);
        }

        using var stream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actualHash, journal.BackupSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "El respaldo protegido cambió desde que se confirmó el reinicio; se conserva la DB anterior en recuperación.");
        }
    }

    internal static void EnsureStagingDirectory(
        ArteInventariadoPathProvider paths,
        Guid operationId,
        bool allowSyntheticRoots = false)
    {
        EnsureSafeRoots(paths, allowSyntheticRoots);
        var databasePath = StagedDatabasePath(paths, operationId);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        EnsureNoReparsePoints(paths.RecoveryRoot, databasePath);
    }

    internal static void DeleteStagingFiles(
        ArteInventariadoPathProvider paths,
        Guid operationId,
        bool allowSyntheticRoots = false)
    {
        EnsureSafeRoots(paths, allowSyntheticRoots);
        var stagedDatabase = StagedDatabasePath(paths, operationId);
        var stagingDirectory = Path.GetDirectoryName(stagedDatabase)!;
        EnsureNoReparsePoints(paths.RecoveryRoot, stagingDirectory);

        foreach (var suffix in DatabaseSidecarSuffixes)
        {
            var candidate = stagedDatabase + suffix;
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }

        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: false);
        }

        var stagingRoot = Path.GetDirectoryName(stagingDirectory)!;
        if (Directory.Exists(stagingRoot) && !Directory.EnumerateFileSystemEntries(stagingRoot).Any())
        {
            Directory.Delete(stagingRoot, recursive: false);
        }

        var resetRoot = ResetRoot(paths);
        if (Directory.Exists(resetRoot) &&
            !Directory.EnumerateFileSystemEntries(resetRoot).Any(entry =>
                !string.Equals(entry, JournalPath(paths), StringComparison.OrdinalIgnoreCase)))
        {
            // The root is intentionally retained while its journal exists.
            // Setup recovery can remove it later if desired; keeping this
            // directory avoids broad recursive cleanup in a protected root.
        }
    }

    internal static void RestorePreviousDatabase(
        ArteInventariadoPathProvider paths,
        MachineWideDatabaseResetJournal journal,
        bool allowSyntheticRoots = false)
    {
        EnsureSafeRoots(paths, allowSyntheticRoots);
        var stagedDatabase = StagedDatabasePath(paths, journal.OperationId);
        var stagingDirectory = Path.GetDirectoryName(stagedDatabase)!;
        EnsureNoReparsePoints(paths.RecoveryRoot, stagingDirectory);

        var stagedMainExists = File.Exists(stagedDatabase);
        if (stagedMainExists)
        {
            DeleteDatabaseAndSidecars(paths.DatabasePath);
            Directory.CreateDirectory(paths.DataRoot);
            File.Move(stagedDatabase, paths.DatabasePath);
        }

        foreach (var suffix in DatabaseSidecarSuffixes)
        {
            var stagedSidecar = stagedDatabase + suffix;
            if (!File.Exists(stagedSidecar))
            {
                continue;
            }

            var liveSidecar = paths.DatabasePath + suffix;
            if (File.Exists(liveSidecar))
            {
                if (!stagedMainExists)
                {
                    throw new InvalidDataException(
                        "El rollback encontró un sidecar duplicado junto a la DB original; no se modificó ese archivo.");
                }

                File.Delete(liveSidecar);
            }

            File.Move(stagedSidecar, liveSidecar);
        }
    }

    internal static void DeleteDatabaseAndSidecars(string databasePath)
    {
        foreach (var suffix in DatabaseSidecarSuffixes)
        {
            TryDeleteFile(databasePath + suffix);
        }

        TryDeleteFile(databasePath);
    }

    private static readonly string[] DatabaseSidecarSuffixes = ["", "-wal", "-shm", "-journal"];

    private static MachineWideDatabaseResetJournal? LoadCore(ArteInventariadoPathProvider paths)
    {
        var journalPath = JournalPath(paths);
        EnsureNoReparsePoints(paths.RecoveryRoot, journalPath);
        if (!File.Exists(journalPath))
        {
            return null;
        }

        var file = new FileInfo(journalPath);
        if (file.Length is <= 0 or > MaximumJournalBytes)
        {
            throw new InvalidDataException("El diario de reinicio de DB tiene un tamaño inválido.");
        }

        var journal = JsonSerializer.Deserialize<MachineWideDatabaseResetJournal>(
                          File.ReadAllText(journalPath),
                          SerializerOptions)
                      ?? throw new InvalidDataException("El diario de reinicio de DB está vacío o dañado.");
        Validate(journal);
        return journal;
    }

    private static void Validate(MachineWideDatabaseResetJournal journal)
    {
        EnsureOperationId(journal.OperationId);
        if (journal.SchemaVersion != CurrentSchemaVersion ||
            journal.InstallationId == Guid.Empty ||
            journal.StationMode != InstallationMode.Principal ||
            !Enum.IsDefined(journal.Phase) ||
            string.IsNullOrWhiteSpace(journal.BackupFileName) ||
            !string.Equals(Path.GetFileName(journal.BackupFileName), journal.BackupFileName, StringComparison.Ordinal) ||
            !journal.BackupFileName.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(journal.BackupSha256))
        {
            throw new InvalidDataException("El diario de reinicio de DB no cumple el formato protegido esperado.");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool HasNoUsers(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();
        using var tablesCommand = connection.CreateCommand();
        tablesCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        using var tablesReader = tablesCommand.ExecuteReader();
        var tableNames = new List<string>();
        while (tablesReader.Read())
        {
            tableNames.Add(tablesReader.GetString(0));
        }

        tablesReader.Close();
        using var countCommand = connection.CreateCommand();
        foreach (var tableName in tableNames)
        {
            if (tableName.StartsWith("__EFMigrations", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, "categorias", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, "categorias_gasto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, "configuracion_sistema", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var escapedName = tableName.Replace("\"", "\"\"", StringComparison.Ordinal);
            countCommand.CommandText = $"SELECT COUNT(*) FROM \"{escapedName}\";";
            if (Convert.ToInt64(countCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new InvalidDataException("El identificador de operación del reinicio de DB no es válido.");
        }
    }

    private static void EnsureSafeRoots(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots) =>
        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, allowSyntheticRoots);

    private static void EnsureNoReparsePoints(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La ruta de recuperación queda fuera de su raíz protegida.");
        }

        var current = fullPath;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("La ruta de recuperación contiene un reparse point.");
                }
            }

            if (string.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new InvalidDataException("No se pudo validar la raíz protegida de recuperación.");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Cleanup is retried by the journal recovery command.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is retried by the journal recovery command.
        }
    }
}
