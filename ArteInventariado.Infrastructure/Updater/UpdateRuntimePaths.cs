using System.Diagnostics;

namespace ArteInventariado.Infrastructure.Updater;

public sealed record UpdateRuntimePaths(
    string ApplicationDataRoot,
    string Root,
    string VersionsDirectory,
    string StatePath,
    string CurrentPointerPath,
    string LogDirectory,
    string DatabasePath,
    string StationSettingsPath,
    string BusinessAssetsDirectory,
    string ManagedAssetsDirectory)
{
    /// <summary>
    /// The provider is optional for binary compatibility with callers that
    /// construct this record directly. Factory-created instances always set it.
    /// </summary>
    public ArteInventariadoPathProvider? PathProvider { get; init; }

    public string StableLauncherPath =>
        PathProvider?.LauncherPath ?? Path.Combine(ApplicationDataRoot, "ArteInventariado.Launcher.exe");

    public string StableUpdaterPath =>
        PathProvider?.UpdaterPath ?? Path.Combine(ApplicationDataRoot, "ArteInventariado.Updater.exe");

    public string MachineInstallRoot => PathProvider?.MachineInstallRoot ?? ApplicationDataRoot;

    public string MachineDataRoot => PathProvider?.MachineDataRoot ?? Root;

    public string UserDataRoot => PathProvider?.UserDataRoot ?? MachineDataRoot;

    public string UpdateStateRoot => PathProvider?.UpdateStateRoot ?? Root;

    public string CurrentStatePath => CurrentPointerPath;

    public string UpdateStatePath => StatePath;

    public string StationSnapshotsDirectory =>
        PathProvider?.StationSnapshotsRoot ?? Path.Combine(Root, "StationSnapshots");

    public string RecoveryDirectory =>
        PathProvider?.RecoveryRoot ?? Path.Combine(Root, "Recovery");

    public string StagingRoot =>
        PathProvider?.UpdateStagingRoot ?? Path.Combine(UserDataRoot, "UpdateStaging");

    public static UpdateRuntimePaths CreateDefault()
    {
        return Create(ArteInventariadoPathProvider.CreateDefault());
    }

    public static UpdateRuntimePaths Create(string applicationDataRoot)
    {
        return Create(ArteInventariadoPathProvider.CreateLegacy(applicationDataRoot));
    }

    public static UpdateRuntimePaths Create(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new UpdateRuntimePaths(
            paths.MachineInstallRoot,
            paths.UpdateStateRoot,
            paths.VersionsRoot,
            paths.UpdateStatePath,
            paths.CurrentStatePath,
            paths.UpdateLogsRoot,
            paths.DatabasePath,
            paths.StationSettingsPath,
            paths.ProductBusinessAssetsRoot,
            paths.AssetsRoot)
        {
            PathProvider = paths
        };
    }

    public static UpdateRuntimePaths CreateMachineWideDefault() =>
        Create(ArteInventariadoPathProvider.CreateMachineWideDefault());

    public static UpdateRuntimePaths CreateMachineWide(
        string machineInstallRoot,
        string machineDataRoot,
        string userDataRoot)
    {
        return Create(ArteInventariadoPathProvider.CreateMachineWide(
            machineInstallRoot,
            machineDataRoot,
            userDataRoot));
    }

    /// <summary>
    /// Creates paths from the explicit layout arguments used by the launcher
    /// and updater. The legacy --app-data-root argument remains supported.
    /// When a protected stable launcher/updater is opened directly without
    /// arguments, the canonical machine-wide layout is inferred from its
    /// location so it can resolve current.json instead of falling into the
    /// legacy first-install path.
    /// </summary>
    public static UpdateRuntimePaths CreateFromArguments(string[] arguments) =>
        CreateFromArguments(arguments, Environment.ProcessPath);

    internal static UpdateRuntimePaths CreateFromArguments(
        string[] arguments,
        string? processPath)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var installRoot = GetArgument(arguments, "--machine-install-root");
        var dataRoot = GetArgument(arguments, "--machine-data-root");
        var userRoot = GetArgument(arguments, "--user-data-root");
        var hasMachineRoot = HasArgument(arguments, "--machine-install-root") ||
            HasArgument(arguments, "--machine-data-root") ||
            HasArgument(arguments, "--user-data-root");
        if (hasMachineRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot) ||
                string.IsNullOrWhiteSpace(dataRoot))
            {
                throw new ArgumentException(
                    "Las raices machine-wide de instalacion y datos deben especificarse juntas.");
            }

            return CreateMachineWide(
                installRoot,
                dataRoot,
                userRoot ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ArteInventariadoPathProvider.ApplicationDirectoryName));
        }

        var applicationDataRoot = GetArgument(arguments, "--app-data-root");
        if (!string.IsNullOrWhiteSpace(applicationDataRoot))
        {
            if (IsDirectMachineWideEntryPoint(processPath))
            {
                throw new ArgumentException(
                    "El launcher/updater estable machine-wide no puede usar --app-data-root.");
            }

            return Create(applicationDataRoot);
        }

        return IsDirectMachineWideEntryPoint(processPath)
            ? CreateMachineWideDefault()
            : CreateDefault();
    }

    public void AddLayoutArguments(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (PathProvider?.IsMachineWide == true)
        {
            startInfo.ArgumentList.Add("--machine-install-root");
            startInfo.ArgumentList.Add(PathProvider.MachineInstallRoot);
            startInfo.ArgumentList.Add("--machine-data-root");
            startInfo.ArgumentList.Add(PathProvider.MachineDataRoot);
            startInfo.ArgumentList.Add("--user-data-root");
            startInfo.ArgumentList.Add(PathProvider.UserDataRoot);
            return;
        }

        startInfo.ArgumentList.Add("--app-data-root");
        startInfo.ArgumentList.Add(ApplicationDataRoot);
    }

    /// <summary>
    /// Returns the stable launcher/updater argument string for this layout.
    /// The command line is deliberately explicit so a common shortcut cannot
    /// accidentally resolve data through the legacy per-user provider.
    /// </summary>
    public string GetLayoutArguments()
    {
        if (PathProvider?.IsMachineWide == true)
        {
            return string.Join(" ",
            [
                "--machine-install-root", QuoteArgument(PathProvider.MachineInstallRoot),
                "--machine-data-root", QuoteArgument(PathProvider.MachineDataRoot),
                "--user-data-root", QuoteArgument(PathProvider.UserDataRoot)
            ]);
        }

        return $"--app-data-root {QuoteArgument(ApplicationDataRoot)}";
    }

    /// <summary>
    /// Arguments embedded in a common machine-wide shortcut.  The user root
    /// is intentionally omitted so Launcher resolves LocalAppData for the
    /// account that clicked the shortcut (not the administrator who installed
    /// it).
    /// </summary>
    public string GetMachineWideShortcutArguments()
    {
        if (PathProvider?.IsMachineWide != true)
        {
            return GetLayoutArguments();
        }

        return string.Join(" ",
        [
            "--machine-install-root", QuoteArgument(PathProvider.MachineInstallRoot),
            "--machine-data-root", QuoteArgument(PathProvider.MachineDataRoot)
        ]);
    }

    public string GetVersionDirectory(string version)
    {
        if (PathProvider is not null)
        {
            return PathProvider.GetVersionDirectory(version);
        }

        var safe = UpdateVersion.Sanitize(version);
        var path = Path.GetFullPath(Path.Combine(VersionsDirectory, safe));
        var root = Path.GetFullPath(VersionsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La version calculada queda fuera del directorio de versiones.");
        }

        return path;
    }

    private static string? GetArgument(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                var value = arguments[index + 1];
                return value.StartsWith("--", StringComparison.Ordinal)
                    ? null
                    : value;
            }
        }

        return null;
    }

    private static bool HasArgument(string[] arguments, string name) =>
        arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsDirectMachineWideEntryPoint(string? processPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var candidate = Path.GetFullPath(processPath);
        var machineInstallRoot = ArteInventariadoPathProvider.CreateMachineWideDefault().MachineInstallRoot;
        return string.Equals(
                   candidate,
                   Path.Combine(machineInstallRoot, ArteInventariadoPathProvider.LauncherFileName),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   candidate,
                   Path.Combine(machineInstallRoot, ArteInventariadoPathProvider.UpdaterFileName),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteArgument(string value)
    {
        // Windows command-line parsing treats backslashes as ordinary path
        // characters inside a quoted argument (all provider roots are
        // normalized without a trailing slash).  Only embedded quotes need
        // escaping here.
        var escaped = value.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}
