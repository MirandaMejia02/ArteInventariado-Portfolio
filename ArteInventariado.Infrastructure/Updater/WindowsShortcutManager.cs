using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ArteInventariado.Infrastructure.Updater;

public sealed record ShortcutLocations(string DesktopDirectory, string ProgramsDirectory)
{
    public static ShortcutLocations CreateDefault() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs));

    public static ShortcutLocations CreateMachineWideDefault() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
}

public sealed class WindowsShortcutManager
{
    private readonly ShortcutLocations _locations;
    private readonly IWindowsShortcutBackend _backend;
    private readonly string _launcherArguments;

    public WindowsShortcutManager(ShortcutLocations? locations = null, string? launcherArguments = null)
        : this(locations ?? ShortcutLocations.CreateDefault(), CreateDefaultBackend(), launcherArguments)
    {
    }

    internal WindowsShortcutManager(
        ShortcutLocations locations,
        IWindowsShortcutBackend backend,
        string? launcherArguments = null)
    {
        _locations = locations;
        _backend = backend;
        _launcherArguments = launcherArguments?.Trim() ?? string.Empty;
    }

    private static IWindowsShortcutBackend CreateDefaultBackend()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Los accesos directos de ArteInventariado requieren Windows.");
        }

        return new ComWindowsShortcutBackend();
    }

    public void EnsureStableShortcuts(string launcherPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Los accesos directos de ArteInventariado requieren Windows.");
        }

        var launcher = Path.GetFullPath(launcherPath);
        if (!File.Exists(launcher))
        {
            throw new FileNotFoundException("No existe el launcher estable para crear accesos directos.", launcher);
        }

        Directory.CreateDirectory(_locations.DesktopDirectory);
        Directory.CreateDirectory(_locations.ProgramsDirectory);
        var canonical = new[]
        {
            Path.Combine(_locations.DesktopDirectory, "ArteInventariado.lnk"),
            Path.Combine(_locations.ProgramsDirectory, "ArteInventariado.lnk")
        };
        foreach (var shortcut in canonical)
        {
            EnsureShortcut(shortcut, launcher);
        }

        var legacy = new[]
        {
            Path.Combine(_locations.ProgramsDirectory, "ArteInventariado.WinForms.lnk"),
            Path.Combine(_locations.ProgramsDirectory, "ArteInventariado", "ArteInventariado.lnk")
        };
        foreach (var shortcut in legacy.Where(_backend.Exists))
        {
            EnsureShortcut(shortcut, launcher);
        }
    }

    /// <summary>
    /// Verifies the two canonical shortcuts without changing them.  The
    /// machine-wide bootstrap uses this as a postcondition instead of
    /// treating a successful COM call as proof that the files were created.
    /// </summary>
    public bool AreStableShortcutsCorrect(string launcherPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var launcher = Path.GetFullPath(launcherPath);
        if (!File.Exists(launcher))
        {
            return false;
        }

        var canonical = new[]
        {
            Path.Combine(_locations.DesktopDirectory, "ArteInventariado.lnk"),
            Path.Combine(_locations.ProgramsDirectory, "ArteInventariado.lnk")
        };
        return canonical.All(shortcut =>
            _backend.Exists(shortcut) &&
            string.Equals(_backend.GetTargetOrNull(shortcut), launcher, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_backend.GetArgumentsOrNull(shortcut) ?? string.Empty, _launcherArguments, StringComparison.Ordinal));
    }

    private void EnsureShortcut(string shortcutPath, string launcher)
    {
        if (string.Equals(_backend.GetTargetOrNull(shortcutPath), launcher, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_backend.GetArgumentsOrNull(shortcutPath) ?? string.Empty, _launcherArguments, StringComparison.Ordinal))
        {
            return;
        }

        _backend.Save(
            shortcutPath,
            launcher,
            Path.GetDirectoryName(launcher)!,
            launcher,
            "ArteInventariado",
            _launcherArguments);
    }
}

internal interface IWindowsShortcutBackend
{
    bool Exists(string path);

    string? GetTargetOrNull(string path);

    string? GetArgumentsOrNull(string path);

    void Save(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath,
        string description,
        string arguments);
}

[SupportedOSPlatform("windows")]
internal sealed class ComWindowsShortcutBackend : IWindowsShortcutBackend
{
    public bool Exists(string path) => File.Exists(path);

    public string? GetTargetOrNull(string path)
    {
        if (!File.Exists(path)) return null;
        return WithShortcut(path, (type, shortcut) =>
            Convert.ToString(type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null)));
    }

    public string? GetArgumentsOrNull(string path)
    {
        if (!File.Exists(path)) return null;
        return WithShortcut(path, (type, shortcut) =>
            Convert.ToString(type.InvokeMember("Arguments", BindingFlags.GetProperty, null, shortcut, null)));
    }

    public void Save(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath,
        string description,
        string arguments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        WithShortcut(shortcutPath, (shortcutType, shortcut) =>
        {
            Set(shortcutType, shortcut, "TargetPath", targetPath);
            Set(shortcutType, shortcut, "WorkingDirectory", workingDirectory);
            Set(shortcutType, shortcut, "IconLocation", $"{iconPath},0");
            Set(shortcutType, shortcut, "Description", description);
            Set(shortcutType, shortcut, "Arguments", arguments);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            return true;
        });
    }

    private static T WithShortcut<T>(string shortcutPath, Func<Type, object, T> action)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
            ?? throw new InvalidOperationException("Windows Script Host no esta disponible.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("No se pudo iniciar Windows Script Host.");
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath])
                ?? throw new InvalidOperationException("No se pudo crear el acceso directo.");
            return action(shortcut.GetType(), shortcut);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void Set(Type type, object target, string property, string value) =>
        type.InvokeMember(property, BindingFlags.SetProperty, null, target, [value]);
}
