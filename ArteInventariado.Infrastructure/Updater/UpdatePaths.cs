using System.Reflection;

namespace ArteInventariado.Infrastructure.Updater;

public static class UpdatePaths
{
    private static UpdateRuntimePaths Default => UpdateRuntimePaths.CreateDefault();

    public static string Root => Default.Root;
    public static string VersionsDirectory => Default.VersionsDirectory;
    public static string StatePath => Default.StatePath;
    public static string CurrentPointerPath => Default.CurrentPointerPath;
    public static string CurrentStatePath => Default.CurrentStatePath;
    public static string UpdateStatePath => Default.UpdateStatePath;
    public static string LogDirectory => Default.LogDirectory;

    public static string InstalledVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "unknown";

    public static string GetVersionDirectory(string version)
    {
        return Default.GetVersionDirectory(version);
    }
}

public sealed record UpdateApplicationInfo(
    string InstalledVersion,
    string ExecutablePath,
    string UpdaterExecutablePath)
{
    public static UpdateApplicationInfo CreateDefault(
        ArteInventariadoPathProvider? paths = null)
    {
        var executable = Path.GetFullPath(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ArteInventariado.WinForms.exe"));
        var directory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
        var updaterPath = paths?.UpdaterPath
            ?? Path.Combine(directory, "ArteInventariado.Updater.exe");
        return new UpdateApplicationInfo(
            UpdatePaths.InstalledVersion,
            executable,
            updaterPath);
    }
}
