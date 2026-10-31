namespace ArteInventariado.Infrastructure.Station;

public static class StationConfigurationPathProvider
{
    public static string GetConfigurationPath()
        => GetConfigurationPath(ArteInventariadoPathProvider.CreateDefault());

    public static string GetConfigurationPath(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.StationSettingsPath;
    }

    public static string GetAuditLogPath()
        => GetAuditLogPath(ArteInventariadoPathProvider.CreateDefault());

    public static string GetAuditLogPath(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.StationLogsRoot, "station-audit.log");
    }

    public static string GetMaintenanceDirectory()
        => GetMaintenanceDirectory(ArteInventariadoPathProvider.CreateDefault());

    public static string GetMaintenanceDirectory(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.MaintenanceRoot;
    }

    public static string GetPendingRestorePath() =>
        GetPendingRestorePath(ArteInventariadoPathProvider.CreateDefault());

    public static string GetPendingRestorePath(ArteInventariadoPathProvider paths) =>
        Path.Combine(GetMaintenanceDirectory(paths), "pending-restore.json");

    public static string GetBusinessAssetsDirectory() =>
        GetBusinessAssetsDirectory(ArteInventariadoPathProvider.CreateDefault());

    public static string GetBusinessAssetsDirectory(ArteInventariadoPathProvider paths) =>
        paths.ProductBusinessAssetsRoot;

    public static string GetManagedAssetsDirectory() =>
        GetManagedAssetsDirectory(ArteInventariadoPathProvider.CreateDefault());

    public static string GetManagedAssetsDirectory(ArteInventariadoPathProvider paths) =>
        paths.AssetsRoot;

    public static string GetDefaultReportsDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ArteInventariado",
            "Reportes");
}
