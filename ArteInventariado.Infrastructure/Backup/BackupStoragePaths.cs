using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;

namespace ArteInventariado.Infrastructure.Backup;

internal sealed record BackupStoragePaths(
    string DatabasePath,
    string DefaultBackupDirectory,
    string MaintenanceDirectory,
    string PendingRestorePath,
    string BusinessAssetsDirectory,
    string? ManagedAssetsDirectory = null,
    ArteInventariadoPathProvider? PathProvider = null)
{
    public static BackupStoragePaths CreateDefault() =>
        CreateDefault(ArteInventariadoPathProvider.CreateDefault());

    public static BackupStoragePaths CreateDefault(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new BackupStoragePaths(
            DatabasePathProvider.GetDatabasePath(paths),
            BackupPathProvider.GetBackupDirectory(paths),
            StationConfigurationPathProvider.GetMaintenanceDirectory(paths),
            StationConfigurationPathProvider.GetPendingRestorePath(paths),
            StationConfigurationPathProvider.GetBusinessAssetsDirectory(paths),
            StationConfigurationPathProvider.GetManagedAssetsDirectory(paths),
            paths);
    }

    public string CreateStagingDirectory(string operation)
    {
        var root = Path.Combine(MaintenanceDirectory, "Staging");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{operation}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
