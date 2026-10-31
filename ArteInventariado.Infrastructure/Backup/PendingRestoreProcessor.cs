using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

public sealed class PendingRestoreProcessor : IPendingRestoreProcessor
{
    private readonly BackupStoragePaths _paths;
    private readonly PendingRestoreStore _store;
    private readonly BackupPackageEngine _engine;
    private readonly IInstallationContext _installation;
    private readonly StationEventLog _eventLog;
    private readonly Action? _afterReplace;

    internal PendingRestoreProcessor(
        BackupStoragePaths paths,
        PendingRestoreStore store,
        BackupPackageEngine engine,
        IInstallationContext installation,
        StationEventLog eventLog,
        Action? afterReplace = null)
    {
        _paths = paths;
        _store = store;
        _engine = engine;
        _installation = installation;
        _eventLog = eventLog;
        _afterReplace = afterReplace;
    }

    public PendingRestoreStatus GetStatus()
    {
        try
        {
            var pending = _store.Load();
            return pending is null
                ? new PendingRestoreStatus(false, null, null, null)
                : new PendingRestoreStatus(true, pending.RestoreId, pending.RequestedUtc, null);
        }
        catch (Exception ex)
        {
            return new PendingRestoreStatus(true, null, null, ex.Message);
        }
    }

    public RestoreStartupResult ProcessPendingRestore()
    {
        PendingRestoreDocument? pending;
        try
        {
            pending = _store.Load();
        }
        catch (Exception ex)
        {
            _eventLog.Write("RESTORE_PENDING_INVALID", "system", _installation.Status.Mode, $"PendingRestore invalido: {ex.GetType().Name}.");
            _store.ArchiveFailed();
            return new RestoreStartupResult(true, false, false, "La restauracion pendiente era invalida y no se modifico la base actual.");
        }

        if (pending is null)
        {
            return new RestoreStartupResult(false, true, false, "No existe una restauracion pendiente.");
        }

        var restoreDirectory = Path.GetDirectoryName(pending.StagedDatabasePath)!;
        var rollbackPath = Path.Combine(restoreDirectory, "rollback-current.db");
        var replacedDatabasePath = Path.Combine(restoreDirectory, "replaced-current.db");
        try
        {
            var migrations = GetCurrentMigrations(pending.StagedDatabasePath);
            ValidatePreparedDatabase(pending, migrations);
            ValidatePreventiveBackup(pending.PreventiveBackupPath, migrations, restoreDirectory);

            _engine.CreateSnapshotAsync(_paths.DatabasePath, rollbackPath, CancellationToken.None).GetAwaiter().GetResult();
            EnsureDatabaseValid(rollbackPath, migrations);

            SqliteConnection.ClearAllPools();
            DeleteSidecars(_paths.DatabasePath);
            File.Replace(pending.StagedDatabasePath, _paths.DatabasePath, replacedDatabasePath, ignoreMetadataErrors: true);
            DeleteSidecars(_paths.DatabasePath);
            _afterReplace?.Invoke();
            EnsureDatabaseValid(_paths.DatabasePath, migrations);

            _store.Complete();
            _eventLog.Write("RESTORE_COMPLETED", "system", _installation.Status.Mode, $"Restauracion {pending.RestoreId} completada al iniciar.");
            DeleteDirectoryBestEffort(restoreDirectory);
            return new RestoreStartupResult(true, true, false, "El respaldo fue restaurado y validado correctamente.");
        }
        catch (Exception restoreError)
        {
            _eventLog.Write("RESTORE_FAILED", "system", _installation.Status.Mode, $"Restauracion {pending.RestoreId} fallo: {restoreError.GetType().Name}.");
            try
            {
                if (File.Exists(rollbackPath))
                {
                    SqliteConnection.ClearAllPools();
                    DeleteSidecars(_paths.DatabasePath);
                    if (File.Exists(_paths.DatabasePath))
                    {
                        File.Replace(rollbackPath, _paths.DatabasePath, null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(rollbackPath, _paths.DatabasePath);
                    }

                    DeleteSidecars(_paths.DatabasePath);
                    EnsureDatabaseValid(_paths.DatabasePath, GetCurrentMigrations(_paths.DatabasePath));
                    DeleteInstalledAssetsBestEffort(pending.InstalledAssetPaths);
                    _store.ArchiveFailed();
                    _eventLog.Write("RESTORE_ROLLBACK_COMPLETED", "system", _installation.Status.Mode, $"Rollback de restauracion {pending.RestoreId} completado.");
                    return new RestoreStartupResult(true, false, true, "La restauracion fallo; la base anterior fue recuperada y validada.");
                }
            }
            catch (Exception rollbackError)
            {
                _eventLog.Write("RESTORE_ROLLBACK_FAILED", "system", _installation.Status.Mode, $"Rollback fallo: {rollbackError.GetType().Name}.");
                throw new InvalidOperationException(
                    $"La restauracion y su rollback fallaron. Los archivos se conservaron en '{restoreDirectory}'. No es seguro iniciar la aplicacion.",
                    new AggregateException(restoreError, rollbackError));
            }

            _store.ArchiveFailed();
            DeleteInstalledAssetsBestEffort(pending.InstalledAssetPaths);
            return new RestoreStartupResult(true, false, false, "La restauracion fallo antes de reemplazar la base actual; no se realizaron cambios.");
        }
    }

    private void DeleteInstalledAssetsBestEffort(IReadOnlyList<string>? paths)
    {
        if (paths is null) return;
        var allowedRoots = new[] { _paths.BusinessAssetsDirectory, _paths.ManagedAssetsDirectory }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (allowedRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) && File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
    }

    private void ValidatePreparedDatabase(PendingRestoreDocument pending, IReadOnlyList<string> migrations)
    {
        if (!File.Exists(pending.StagedDatabasePath))
        {
            throw new FileNotFoundException("No existe la base preparada para restaurar.", pending.StagedDatabasePath);
        }

        var hash = BackupPackageEngine.ComputeSha256Async(pending.StagedDatabasePath, CancellationToken.None).GetAwaiter().GetResult();
        if (!string.Equals(hash, pending.StagedDatabaseSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El checksum de la base preparada cambio antes del reinicio.");
        }

        EnsureDatabaseValid(pending.StagedDatabasePath, migrations);
    }

    private void ValidatePreventiveBackup(string packagePath, IReadOnlyList<string> migrations, string restoreDirectory)
    {
        var directory = Path.Combine(restoreDirectory, "pre-restore-validation");
        var result = _engine.ValidatePackageAsync(packagePath, migrations, directory, CancellationToken.None).GetAwaiter().GetResult();
        if (!result.IsValid || result.Manifest?.Kind != BackupKind.PreRestore)
        {
            throw new InvalidDataException("El respaldo preventivo no esta disponible o ya no es valido.");
        }
    }

    private void EnsureDatabaseValid(string path, IReadOnlyList<string> migrations)
    {
        var errors = _engine.ValidateDatabaseAsync(path, migrations, allowOlderSchema: false, CancellationToken.None).GetAwaiter().GetResult();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", errors));
        }
    }

    private static IReadOnlyList<string> GetCurrentMigrations(string databasePath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString())
            .Options;
        using var context = new AppDbContext(options);
        return context.Database.GetMigrations().ToList();
    }

    private static void DeleteSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // El restore ya fue validado; un residuo de staging no afecta la base activa.
        }
    }
}
