using System.Globalization;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Updater;

/// <summary>
/// Security boundary for the elevated machine-wide updater.
///
/// The launcher is a normal user process, so every value crossing the UAC
/// boundary is treated as untrusted again here.  Legacy per-user updates do
/// not enter this guard and keep their v1.11 behavior.
/// </summary>
public static class MachineWideUpdateGuard
{
    private static readonly HashSet<string> ValueArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "--machine-install-root",
        "--machine-data-root",
        "--user-data-root",
        "--state",
        "--handoff",
        "--wait-pid",
        "--wait-executable"
    };

    private static readonly HashSet<string> FlagArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "--rollback",
        "--recover",
        "--handoff-only",
        "--no-relaunch",
        "--launch-stable"
    };

    public static bool HasMachineWideLayout(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(argument =>
            string.Equals(argument, "--machine-install-root", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "--machine-data-root", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "--user-data-root", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Repairs only the legacy rootless invocation of the exact stable
    /// machine-wide updater. Older launchers may start that protected
    /// entrypoint without forwarding layout roots; the updater can safely
    /// recover them from its validated installation path. Explicit or partial
    /// layouts are never repaired and remain subject to normal validation.
    /// </summary>
    public static string[] EnsureStableUpdaterLayoutArguments(
        IReadOnlyList<string> arguments,
        UpdateRuntimePaths paths,
        string updaterExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(updaterExecutablePath);

        if (paths.PathProvider?.IsMachineWide != true)
        {
            return arguments.ToArray();
        }

        if (arguments.Any(argument =>
                string.Equals(argument, "--app-data-root", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "El updater machine-wide no puede combinarse con --app-data-root.");
        }

        if (HasMachineWideLayout(arguments))
        {
            return arguments.ToArray();
        }

        var expectedUpdater = Path.GetFullPath(paths.StableUpdaterPath);
        var actualUpdater = Path.GetFullPath(updaterExecutablePath);
        if (!string.Equals(expectedUpdater, actualUpdater, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "No se inferirán raíces machine-wide para un updater que no sea el ejecutable estable administrado.");
        }

        var provider = paths.PathProvider;
        var normalized = new List<string>(arguments.Count + 6);
        normalized.AddRange(arguments);
        normalized.Add("--machine-install-root");
        normalized.Add(provider.MachineInstallRoot);
        normalized.Add("--machine-data-root");
        normalized.Add(provider.MachineDataRoot);
        normalized.Add("--user-data-root");
        normalized.Add(provider.UserDataRoot);
        return normalized.ToArray();
    }

    /// <summary>
    /// Validates the normal updater command line after UAC elevation.  The
    /// production path accepts only the canonical Program Files/ProgramData
    /// roots.  Tests and the explicit synthetic smoke may opt into temporary
    /// roots without weakening the production entry point.
    /// </summary>
    public static void ValidateUpdaterArguments(
        IReadOnlyList<string> arguments,
        UpdateRuntimePaths paths,
        bool allowSyntheticRoots = false)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.PathProvider?.IsMachineWide != true)
        {
            return;
        }

        ValidateArgumentShape(arguments);
        ValidateLayout(paths, allowSyntheticRoots);

        var requestedState = GetArgument(arguments, "--state") ?? paths.StatePath;
        var canonicalState = Path.GetFullPath(requestedState);
        // Program may carry --state in a record copy for legacy testability;
        // the machine-wide canonical target always comes from the provider,
        // never from that untrusted override.
        var expectedState = Path.GetFullPath(
            paths.PathProvider?.UpdateStatePath ?? paths.StatePath);
        if (!string.Equals(canonicalState, expectedState, StringComparison.OrdinalIgnoreCase) ||
            !IsWithin(canonicalState, paths.UpdateStateRoot))
        {
            throw new InvalidDataException(
                "El updater machine-wide solo puede usar el update-state.json de su layout administrado.");
        }

        var handoff = GetArgument(arguments, "--handoff");
        if (!string.IsNullOrWhiteSpace(handoff))
        {
            ValidateHandoffPath(paths, handoff);
        }

        var waitPid = GetArgument(arguments, "--wait-pid");
        if (!string.IsNullOrWhiteSpace(waitPid) &&
            (!int.TryParse(waitPid, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0))
        {
            throw new ArgumentException("--wait-pid no contiene un identificador valido.");
        }

        var waitExecutable = GetArgument(arguments, "--wait-executable");
        if (!string.IsNullOrWhiteSpace(waitExecutable))
        {
            ValidateManagedExecutablePath(waitExecutable, paths, "proceso esperado");
        }

        if (HasFlag(arguments, "--rollback") && HasFlag(arguments, "--recover"))
        {
            throw new ArgumentException("El updater no puede ejecutar rollback y recovery a la vez.");
        }

        if (HasFlag(arguments, "--launch-stable") && HasFlag(arguments, "--no-relaunch"))
        {
            throw new ArgumentException("El updater no puede combinar --launch-stable con --no-relaunch.");
        }

        if (!string.IsNullOrWhiteSpace(handoff) &&
            (HasFlag(arguments, "--rollback") || HasFlag(arguments, "--recover")))
        {
            throw new ArgumentException("El handoff de staging no puede combinarse con recovery o rollback.");
        }

        if (HasFlag(arguments, "--handoff-only") &&
            (string.IsNullOrWhiteSpace(handoff) ||
             HasFlag(arguments, "--rollback") ||
             HasFlag(arguments, "--recover") ||
             HasFlag(arguments, "--launch-stable") ||
             HasFlag(arguments, "--no-relaunch")))
        {
            throw new ArgumentException(
                "--handoff-only requiere un staging válido y no puede combinarse con instalación, recovery o relanzamiento.");
        }
    }

    /// <summary>
    /// Fails closed when a machine-wide updater is started directly without
    /// the UAC relaunch performed by <see cref="UpdateLauncher"/>.
    /// </summary>
    public static void EnsureElevated(UpdateRuntimePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.PathProvider?.IsMachineWide == true && !MachineWideElevation.IsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El updater machine-wide debe ejecutarse elevado. Autorice UAC desde ArteInventariado.");
        }
    }

    /// <summary>
    /// Confirms that the executable crossing the update boundary is one of
    /// the updater binaries managed by this installation, not an arbitrary
    /// path supplied by a caller.
    /// </summary>
    public static void ValidateUpdaterExecutablePath(
        UpdateRuntimePaths paths,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (paths.PathProvider?.IsMachineWide != true)
        {
            return;
        }

        var full = Path.GetFullPath(executablePath);
        EnsureLocalPath(full, "updater");
        if (!string.Equals(
                Path.GetFileName(full),
                ArteInventariadoPathProvider.UpdaterFileName,
                StringComparison.OrdinalIgnoreCase) ||
            !IsWithin(full, paths.MachineInstallRoot))
        {
            throw new InvalidDataException("El proceso updater queda fuera de la instalación machine-wide administrada.");
        }

        EnsureNoReparsePoints(full);
    }

    /// <summary>
    /// Validates the paths persisted in update-state.json before any elevated
    /// installer operation.  Read-only package/backup sources may live outside
    /// ProgramData (the administrator can select a download), but all paths
    /// that can become write targets remain inside their managed roots.
    /// </summary>
    public static void ValidateStateDocument(
        UpdateStateDocument state,
        UpdateRuntimePaths paths)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.PathProvider?.IsMachineWide != true)
        {
            return;
        }

        if (!UpdateVersion.TryParse(state.TargetVersion, out _))
        {
            throw new InvalidDataException("El estado contiene una version destino invalida.");
        }

        ValidateExternalReadPath(state.PackagePath, "paquete");
        ValidateExternalReadPath(state.ManifestPath, "manifest");
        ValidateExternalReadPath(state.PreUpdateBackupPath, "PRE_UPDATE");
        ValidateManagedExecutablePath(state.SourceApplicationPath, paths, "source");
        ValidateManagedExecutablePath(state.PreviousVersionPath, paths, "previous");
        ValidateManagedExecutablePath(state.CurrentVersionPath, paths, "current");

        if (!string.IsNullOrWhiteSpace(state.StationSettingsBackupPath))
        {
            var snapshot = Path.GetFullPath(state.StationSettingsBackupPath);
            EnsureLocalPath(snapshot, "snapshot de station-settings.json");
            if (!IsWithin(snapshot, paths.StationSnapshotsDirectory))
            {
                throw new InvalidDataException(
                    "La copia de station-settings.json queda fuera de UpdateState/StationSnapshots.");
            }

            EnsureNoReparsePoints(snapshot);
        }
    }

    private static void ValidateArgumentShape(IReadOnlyList<string> arguments)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.IsNullOrWhiteSpace(argument) || !argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("El updater recibió un argumento inválido.");
            }

            if (!ValueArguments.Contains(argument) && !FlagArguments.Contains(argument))
            {
                throw new ArgumentException($"El updater recibió un argumento no reconocido: {argument}.");
            }

            if (!seen.Add(argument))
            {
                throw new ArgumentException($"El argumento del updater está duplicado: {argument}.");
            }

            if (!ValueArguments.Contains(argument))
            {
                continue;
            }

            if (index + 1 >= arguments.Count ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Falta el valor de {argument}.");
            }

            index++;
        }

        if (!HasMachineWideLayout(arguments))
        {
            throw new InvalidDataException("El updater machine-wide no recibió las raíces de instalación y datos.");
        }

        var install = GetArgument(arguments, "--machine-install-root");
        var data = GetArgument(arguments, "--machine-data-root");
        if (string.IsNullOrWhiteSpace(install) || string.IsNullOrWhiteSpace(data))
        {
            throw new InvalidDataException(
                "Las raíces machine-wide de instalación y datos deben especificarse juntas.");
        }

        if (arguments.Any(argument =>
                string.Equals(argument, "--app-data-root", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("--app-data-root no puede combinarse con el updater machine-wide.");
        }
    }

    private static void ValidateLayout(UpdateRuntimePaths paths, bool allowSyntheticRoots)
    {
        var provider = paths.PathProvider
            ?? throw new InvalidDataException("El updater machine-wide no resolvió un proveedor de rutas.");

        EnsureLocalPath(provider.MachineInstallRoot, "MachineInstallRoot");
        EnsureLocalPath(provider.MachineDataRoot, "MachineDataRoot");
        EnsureLocalPath(provider.UserDataRoot, "UserDataRoot");
        EnsureNoReparsePoints(provider.MachineInstallRoot);
        EnsureNoReparsePoints(provider.MachineDataRoot);
        EnsureNoReparsePoints(provider.UserDataRoot);
        ValidateManagedRoots(paths);

        var roots = new[]
        {
            provider.MachineInstallRoot,
            provider.MachineDataRoot,
            provider.UserDataRoot
        };
        for (var left = 0; left < roots.Length; left++)
        {
            for (var right = left + 1; right < roots.Length; right++)
            {
                if (IsWithin(roots[left], roots[right]) || IsWithin(roots[right], roots[left]))
                {
                    throw new InvalidDataException("Las raíces machine-wide no pueden superponerse.");
                }
            }
        }

        if (allowSyntheticRoots)
        {
            return;
        }

        var canonical = ArteInventariadoPathProvider.CreateMachineWideDefault();
        if (!PathEquals(provider.MachineInstallRoot, canonical.MachineInstallRoot) ||
            !PathEquals(provider.MachineDataRoot, canonical.MachineDataRoot) ||
            !IsRecognizedUserDataRoot(provider.UserDataRoot))
        {
            throw new InvalidDataException(
                "Las raíces no corresponden al layout machine-wide reconocido de ArteInventariado.");
        }
    }

    /// <summary>
    /// Checks every directory/file parent the installer can write. This is
    /// repeated immediately before an operation so a junction introduced
    /// after bootstrap cannot redirect a protected write elsewhere.
    /// </summary>
    public static void ValidateManagedRoots(UpdateRuntimePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.PathProvider?.IsMachineWide != true)
        {
            return;
        }

        var provider = paths.PathProvider!;
        foreach (var path in new[]
        {
            paths.VersionsDirectory,
            paths.UpdateStateRoot,
            paths.RecoveryDirectory,
            paths.CurrentPointerPath,
            paths.StatePath,
            paths.DatabasePath,
            paths.BusinessAssetsDirectory,
            paths.ManagedAssetsDirectory,
            paths.StationSettingsPath,
            provider.StationOperationalRoot,
            paths.StationSnapshotsDirectory
        })
        {
            EnsureNoReparsePoints(path);
        }
    }

    /// <summary>
    /// Validates the per-user directory that may cross into the elevated
    /// updater. It is deliberately one GUID directory below UserDataRoot so
    /// an untrusted caller cannot select an arbitrary file or parent root.
    /// </summary>
    public static void ValidateHandoffPath(UpdateRuntimePaths paths, string handoffPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffPath);

        if (paths.PathProvider?.IsMachineWide != true)
        {
            throw new InvalidDataException("El handoff machine-wide requiere el layout protegido.");
        }

        var full = Path.GetFullPath(handoffPath);
        var root = Path.GetFullPath(paths.StagingRoot);
        EnsureLocalPath(full, "staging de actualización");
        EnsureLocalPath(root, "raíz de staging de actualización");
        EnsureNoReparsePoints(root);
        if (!IsWithin(full, root) || PathEquals(full, root))
        {
            throw new InvalidDataException(
                "El staging de actualización queda fuera de UserDataRoot\\UpdateStaging.");
        }

        var parent = Directory.GetParent(full)?.FullName;
        if (!PathEquals(parent, root) ||
            !Guid.TryParseExact(new DirectoryInfo(full).Name, "N", out _))
        {
            throw new InvalidDataException(
                "El staging de actualización debe ser una carpeta GUID directa de UserDataRoot\\UpdateStaging.");
        }

        EnsureNoReparsePoints(full);
    }

    private static void ValidateExternalReadPath(string? path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var full = Path.GetFullPath(path);
        EnsureLocalPath(full, description);
        EnsureNoReparsePoints(full);
    }

    private static void ValidateManagedExecutablePath(
        string? path,
        UpdateRuntimePaths paths,
        string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var full = Path.GetFullPath(path);
        EnsureLocalPath(full, description);
        if (!string.Equals(
                Path.GetFileName(full),
                UpdateLaunchTargetResolver.ApplicationExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"La ruta {description} no referencia el ejecutable administrado.");
        }

        if (!IsWithin(full, paths.VersionsDirectory))
        {
            throw new InvalidDataException($"La ruta {description} queda fuera de Versions.");
        }

        var parent = Directory.GetParent(full)?.FullName;
        var parentParent = parent is null ? null : Directory.GetParent(parent)?.FullName;
        if (!PathEquals(parentParent, paths.VersionsDirectory))
        {
            throw new InvalidDataException($"La ruta {description} no corresponde a una versión administrada.");
        }

        EnsureNoReparsePoints(full);
    }

    private static void EnsureLocalPath(string path, string description)
    {
        if (!Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"La ruta {description} no es una ruta local absoluta válida.");
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)
            ?? throw new InvalidDataException("No se pudo resolver la raíz de una ruta administrada.");
        var current = root;
        RejectIfReparse(current);

        var relative = Path.GetRelativePath(root, full);
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (File.Exists(current) || Directory.Exists(current))
            {
                RejectIfReparse(current);
            }
        }
    }

    private static void RejectIfReparse(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"La ruta administrada contiene un reparse point: '{path}'.");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static string? GetArgument(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(IReadOnlyList<string> arguments, string name) =>
        arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static bool PathEquals(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsRecognizedUserDataRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var application = new DirectoryInfo(full);
        var local = application.Parent;
        var appData = local?.Parent;
        var profile = appData?.Parent;
        var profiles = profile?.Parent;

        return string.Equals(application.Name, ArteInventariadoPathProvider.ApplicationDirectoryName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(local?.Name, "Local", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(appData?.Name, "AppData", StringComparison.OrdinalIgnoreCase) &&
            profile is not null &&
            profiles is not null &&
            string.Equals(profiles.Name, "Users", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
