using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Station;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// The only write boundary exposed by the station helper. It accepts a mode
/// and an expected identity, never an arbitrary JSON document or destination
/// path. The command-line host performs the final elevation check.
/// </summary>
public static class MachineWideStationConfigurationHelper
{
    public const string WriteModeCommand = "--write-station-mode";

    public static void ValidateManagedRoots(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide)
        {
            throw new InvalidDataException("El helper protegido requiere el layout machine-wide.");
        }

        EnsureLocalPath(paths.MachineInstallRoot, "MachineInstallRoot");
        EnsureLocalPath(paths.MachineDataRoot, "MachineDataRoot");
        EnsureLocalPath(paths.StationRoot, "StationRoot");
        EnsureLocalPath(paths.StationSettingsPath, "station core");
        EnsureLocalPath(paths.StationOperationalRoot, "station operativo");

        var roots = new[] { paths.MachineInstallRoot, paths.MachineDataRoot, paths.UserDataRoot };
        for (var left = 0; left < roots.Length; left++)
        {
            for (var right = left + 1; right < roots.Length; right++)
            {
                if (IsWithin(roots[left], roots[right]) || IsWithin(roots[right], roots[left]))
                {
                    throw new InvalidDataException("Las raices machine-wide no pueden superponerse.");
                }
            }
        }

        if (!allowSyntheticRoots)
        {
            var canonical = ArteInventariadoPathProvider.CreateMachineWideDefault();
            if (!PathEquals(paths.MachineInstallRoot, canonical.MachineInstallRoot) ||
                !PathEquals(paths.MachineDataRoot, canonical.MachineDataRoot) ||
                !IsRecognizedUserDataRoot(paths.UserDataRoot))
            {
                throw new InvalidDataException(
                    "Las raices no corresponden al layout machine-wide reconocido de ArteInventariado.");
            }
        }

        foreach (var path in new[]
        {
            paths.MachineInstallRoot,
            paths.MachineDataRoot,
            paths.StationHelperPath,
            paths.StationRoot,
            paths.StationSettingsPath,
            paths.StationOperationalRoot,
            paths.StationOperationalSettingsPath,
            paths.BootstrapStatePath
        })
        {
            EnsureNoReparsePoints(path);
        }
    }

    public static void WriteMode(
        ArteInventariadoPathProvider paths,
        InstallationMode mode,
        Guid expectedInstallationId,
        InstallationMode? expectedCurrentMode,
        bool allowSyntheticRoots = false)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "El modo de instalacion no es valido.");
        }

        if (expectedInstallationId == Guid.Empty)
        {
            throw new InvalidDataException("La identidad esperada de la estacion no es valida.");
        }

        ValidateManagedRoots(paths, allowSyntheticRoots);
        if (!MachineWideElevation.IsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El helper protegido debe ejecutarse con una cuenta elevada.");
        }

        var store = new LocalStationConfigurationStore(paths);
        var current = store.Load();
        if (!current.Status.IsConfigured &&
            current.Status.State != StationConfigurationState.RequiresSetup)
        {
            throw new InvalidDataException(
                "La configuracion protegida de la estacion no esta disponible para cambiar el modo.");
        }

        if (current.Status.InstallationId != expectedInstallationId)
        {
            throw new InvalidDataException(
                "La identidad de la estacion cambio antes de la operacion elevada.");
        }

        if (expectedCurrentMode.HasValue && current.Status.Mode != expectedCurrentMode)
        {
            throw new InvalidDataException(
                "El modo de la estacion cambio antes de la operacion elevada.");
        }

        if (!expectedCurrentMode.HasValue && current.Status.IsConfigured)
        {
            throw new InvalidDataException(
                "La operacion inicial ya no corresponde a una estacion pendiente.");
        }

        store.SaveConfigured(mode);
    }

    private static bool IsRecognizedUserDataRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var localAppData = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expected = Path.Combine(localAppData, ArteInventariadoPathProvider.ApplicationDirectoryName);
        return PathEquals(full, expected);
    }

    private static void EnsureLocalPath(string path, string description)
    {
        if (!Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"La ruta {description} no es local y absoluta.");
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        var current = full;
        while (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException($"La ruta protegida contiene un reparse point: '{current}'.");
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidDataException($"No se pudo inspeccionar la ruta protegida: '{current}'.", ex);
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || PathEquals(parent, current))
            {
                break;
            }

            current = parent;
        }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PathEquals(fullCandidate, fullRoot) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
