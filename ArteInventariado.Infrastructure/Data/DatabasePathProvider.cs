namespace ArteInventariado.Infrastructure.Data;

public static class DatabasePathProvider
{
    public static string GetDatabasePath()
        => GetDatabasePath(ArteInventariadoPathProvider.CreateDefault());

    public static string GetDatabasePath(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var databasePath = paths.DatabasePath;

        var dataDirectory = Path.GetDirectoryName(databasePath);

        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory!);
        }

        return databasePath;
    }
}
