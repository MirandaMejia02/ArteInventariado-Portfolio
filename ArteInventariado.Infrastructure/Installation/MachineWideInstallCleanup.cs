using System.ComponentModel;
using System.Diagnostics;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Destructive operations used only by the explicit clean-install/uninstall
/// flow.  The normal updater never calls this type.  Every deletion is scoped
/// to the three canonical ArteInventariado roots and the two canonical task
/// names; arbitrary paths are rejected before any filesystem mutation.
/// </summary>
public static class MachineWideInstallCleanup
{
    public static IReadOnlyList<string> ProtectedTaskNames { get; } =
    [
        Backup.MachineWideBackupPolicyStore.DefaultTaskName,
        CloudBackup.MachineWideCloudBackupTaskScheduler.DefaultTaskName
    ];

    public static bool HasAnyManagedState(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return HasAnyManagedStateCore(paths, allowSyntheticRoots: false);
    }

    internal static bool HasAnyManagedStateSynthetic(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return HasAnyManagedStateCore(paths, allowSyntheticRoots: true);
    }

    private static bool HasAnyManagedStateCore(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots)
    {
        EnsureSafeRoots(paths, allowSyntheticRoots);
        return RootExists(paths.MachineInstallRoot) ||
               RootExists(paths.MachineDataRoot) ||
               RootExists(paths.UserDataRoot);
    }

    public static void EnsureNoApplicationProcesses(int? ignoredProcessId = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("La instalación machine-wide requiere Windows.");
        }

        var processNames = new[]
        {
            "ArteInventariado.WinForms",
            "ArteInventariado.Launcher",
            "ArteInventariado.Updater",
            "ArteInventariado.StationHelper",
            "ArteInventariado.BackupRunner",
            "ArteInventariado.CloudBackupRunner"
        };

        foreach (var processName in processNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (ignoredProcessId.HasValue && process.Id == ignoredProcessId.Value)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Cierre ArteInventariado antes de continuar. El proceso '{processName}.exe' (PID {process.Id}) sigue activo.");
                }
            }
        }
    }

    public static MachineWideCleanupResult RemoveInstallationOnly(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureSafeRoots(paths, allowSyntheticRoots);
        if (!allowSyntheticRoots)
        {
            EnsureNoApplicationProcesses(Environment.ProcessId);
        }

        var removedTasks = allowSyntheticRoots
            ? []
            : RemoveProtectedTasks();
        if (!allowSyntheticRoots)
        {
            RemoveCanonicalShortcuts();
        }

        var removedInstall = DeleteRoot(paths.MachineInstallRoot);
        return new MachineWideCleanupResult(
            removedInstall,
            DataRootRemoved: false,
            UserRootRemoved: false,
            removedTasks);
    }

    public static MachineWideCleanupResult RemoveAllManagedState(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureSafeRoots(paths, allowSyntheticRoots);
        if (!allowSyntheticRoots)
        {
            EnsureNoApplicationProcesses(Environment.ProcessId);
        }

        var removedTasks = allowSyntheticRoots
            ? []
            : RemoveProtectedTasks();
        if (!allowSyntheticRoots)
        {
            RemoveCanonicalShortcuts();
        }

        // Delete only the exact roots after all protected processes/tasks have
        // been checked.  The order keeps Program Files from being recreated by
        // a still-running scheduled runner while the data tree is removed.
        var removedInstall = DeleteRoot(paths.MachineInstallRoot);
        var removedData = DeleteRoot(paths.MachineDataRoot);
        var removedUser = DeleteRoot(paths.UserDataRoot);
        return new MachineWideCleanupResult(
            removedInstall,
            removedData,
            removedUser,
            removedTasks);
    }

    public static void EnsureSafeRoots(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide)
        {
            throw new InvalidDataException("La limpieza requiere el layout machine-wide.");
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, allowSyntheticRoots);
        foreach (var root in new[]
        {
            paths.MachineInstallRoot,
            paths.MachineDataRoot,
            paths.UserDataRoot
        })
        {
            var full = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var volume = Path.GetPathRoot(full)?.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(volume) ||
                string.Equals(full, volume, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("La raíz de ArteInventariado no puede ser una unidad completa.");
            }

            EnsureNoReparsePoints(full);
        }
    }

    private static IReadOnlyList<string> RemoveProtectedTasks()
    {
        var removed = new List<string>();
        foreach (var taskName in ProtectedTaskNames)
        {
            if (!TaskExists(taskName))
            {
                continue;
            }

            var result = RunSchtasks("/Delete", "/TN", taskName, "/F");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"No se pudo eliminar la tarea protegida '{taskName}'. {FormatProcessError(result)}");
            }

            removed.Add(taskName);
        }

        return removed;
    }

    private static bool TaskExists(string taskName)
    {
        // /NH is valid for TABLE output but schtasks rejects it together
        // with LIST. The invalid combination returned exit 1 before any
        // clean-install deletion, making a missing optional task look like
        // an installer failure (and leaving the old installation intact).
        var result = RunSchtasks(BuildTaskQueryArguments(taskName).ToArray());
        if (result.ExitCode == 0)
        {
            return true;
        }

        var text = $"{result.StandardOutput}\n{result.StandardError}";
        if (text.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("no puede encontrar", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("no se puede encontrar", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("no se encuentra", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ruta especificada", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("path specified", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new InvalidOperationException(
            $"No se pudo consultar la tarea protegida '{taskName}'. {FormatProcessError(result)}");
    }

    internal static IReadOnlyList<string> BuildTaskQueryArguments(string taskName) =>
    [
        "/Query",
        "/TN",
        taskName,
        "/FO",
        "LIST"
    ];

    private static void RemoveCanonicalShortcuts()
    {
        var locations = Updater.ShortcutLocations.CreateMachineWideDefault();
        foreach (var shortcut in new[]
        {
            Path.Combine(locations.DesktopDirectory, "ArteInventariado.lnk"),
            Path.Combine(locations.ProgramsDirectory, "ArteInventariado.lnk")
        })
        {
            EnsureNoReparsePoints(shortcut);
            if (File.Exists(shortcut))
            {
                File.Delete(shortcut);
            }
        }
    }

    private static bool DeleteRoot(string root)
    {
        if (File.Exists(root))
        {
            throw new IOException($"La ruta protegida esperada como directorio es un archivo: '{root}'.");
        }

        if (!Directory.Exists(root))
        {
            return false;
        }

        EnsureNoReparsePoints(root);
        Directory.Delete(root, recursive: true);
        return true;
    }

    private static bool RootExists(string root) =>
        File.Exists(root) || Directory.Exists(root);

    private static void EnsureNoReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        var current = full;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"La ruta protegida contiene un reparse point: '{current}'.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        if (!Directory.Exists(full))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"La ruta protegida contiene un reparse point: '{entry}'.");
            }
        }
    }

    private static ProcessResult RunSchtasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("No se pudo iniciar schtasks.exe.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(output, error);
            return new ProcessResult(process.ExitCode, output.Result, error.Result);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("No se pudo consultar Task Scheduler mediante schtasks.exe.", ex);
        }
    }

    private static string FormatProcessError(ProcessResult result) =>
        string.Join(
            ' ',
            new[] { result.StandardError.Trim(), result.StandardOutput.Trim() }
                .Where(text => !string.IsNullOrWhiteSpace(text)));

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

public sealed record MachineWideCleanupResult(
    bool InstallRootRemoved,
    bool DataRootRemoved,
    bool UserRootRemoved,
    IReadOnlyList<string> RemovedTasks);
