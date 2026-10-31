namespace ArteInventariado.Infrastructure.Data;

public static class BackupPathProvider
{
    public static string GetBackupDirectory()
        => GetBackupDirectory(ArteInventariadoPathProvider.CreateDefault());

    public static string GetBackupDirectory(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var backupDirectory = paths.ProtectedBackupsRoot;

        if (!Directory.Exists(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        return backupDirectory;
    }
}
