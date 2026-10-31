using SQLitePCL;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Gives SQLite a protected, machine-local temporary directory before any
/// connection is opened. SYSTEM tasks cannot rely on the TEMP/TMP values of
/// the interactive user that last logged on to the computer.
/// </summary>
internal static class MachineWideSqliteRuntime
{
    private const int SqliteWin32TempDirectoryType = 2;

    public static string Configure(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var directory = Path.Combine(paths.MaintenanceRoot, "BackupRunnerTemp");
        Directory.CreateDirectory(directory);

        if (OperatingSystem.IsWindows())
        {
            Batteries_V2.Init();
            var result = raw.sqlite3_win32_set_directory(
                SqliteWin32TempDirectoryType,
                directory);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"SQLite no pudo configurar su directorio temporal protegido (rc={result}).");
            }
        }

        return directory;
    }
}
