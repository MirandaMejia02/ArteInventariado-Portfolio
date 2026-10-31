using System.Diagnostics;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateLaunchTargetResolver
{
    public const string ApplicationExecutableName = "ArteInventariado.WinForms.exe";

    private readonly UpdateRuntimePaths _paths;
    private readonly IUpdateExecutableProbe _probe;

    public UpdateLaunchTargetResolver(UpdateRuntimePaths paths)
        : this(paths, new PhysicalUpdateExecutableProbe())
    {
    }

    internal UpdateLaunchTargetResolver(UpdateRuntimePaths paths, IUpdateExecutableProbe probe)
    {
        _paths = paths;
        _probe = probe;
    }

    public string? ResolveCurrentOrNull()
    {
        var pointer = new UpdatePointerStore(_paths.CurrentPointerPath).Load();
        return pointer is null ? null : ResolveCurrent(pointer);
    }

    public string ResolveCurrent(UpdateVersionPointer pointer) =>
        ValidateManagedExecutable(pointer.Version, pointer.ExecutablePath);

    public string? ResolvePrevious(UpdateVersionPointer pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer.PreviousExecutablePath))
        {
            return null;
        }

        return ValidateManagedExecutable(pointer.PreviousExecutablePath);
    }

    public string ValidateManagedExecutable(string executablePath)
    {
        var version = InferManagedVersion(executablePath);
        return ValidateManagedExecutable(version, executablePath);
    }

    public string ValidateManagedExecutable(string version, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            throw new InvalidDataException("El puntero de version no contiene una ruta absoluta valida.");
        }

        if (IsNetworkOrDevicePath(executablePath))
        {
            throw new InvalidDataException("El puntero de version no puede usar rutas UNC o de dispositivo.");
        }

        var normalizedVersion = UpdateVersion.Sanitize(version);
        var expectedDirectory = _paths.GetVersionDirectory(normalizedVersion);
        var expectedExecutable = Path.GetFullPath(Path.Combine(expectedDirectory, ApplicationExecutableName));
        var candidate = Path.GetFullPath(executablePath);
        if (!string.Equals(candidate, expectedExecutable, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El puntero de version queda fuera de la version administrada esperada.");
        }

        EnsureNoReparsePoints(candidate);
        if (!_probe.FileExists(candidate))
        {
            throw new FileNotFoundException("El ejecutable current no existe.", candidate);
        }

        var productVersion = _probe.GetProductVersion(candidate);
        if (!UpdateVersion.TryParse(productVersion, out var actualVersion) ||
            actualVersion.CompareTo(UpdateVersion.Parse(normalizedVersion)) != 0)
        {
            throw new InvalidDataException(
                $"ProductVersion '{productVersion ?? "desconocida"}' no coincide con '{normalizedVersion}'.");
        }

        return candidate;
    }

    internal string ReadProductVersion(string executablePath)
    {
        var value = _probe.GetProductVersion(executablePath);
        if (!UpdateVersion.TryParse(value, out var parsed))
        {
            throw new InvalidDataException("El ejecutable no declara un ProductVersion compatible.");
        }

        return parsed.ToString();
    }

    private string InferManagedVersion(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            throw new InvalidDataException("El puntero de version no contiene una ruta absoluta valida.");
        }

        if (IsNetworkOrDevicePath(executablePath))
        {
            throw new InvalidDataException("El puntero de version no puede usar rutas UNC o de dispositivo.");
        }

        var candidate = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetFileName(candidate), ApplicationExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El puntero no referencia el ejecutable administrado esperado.");
        }

        var versionDirectory = Path.GetDirectoryName(candidate)
            ?? throw new InvalidDataException("El ejecutable no tiene directorio de version.");
        var versionsRoot = Path.GetFullPath(_paths.VersionsDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(versionDirectory)?.FullName;
        if (!string.Equals(Path.GetFullPath(parent ?? string.Empty), versionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El puntero queda fuera de una version administrada.");
        }

        return UpdateVersion.Sanitize(Path.GetFileName(versionDirectory));
    }

    private void EnsureNoReparsePoints(string executablePath)
    {
        var applicationRoot = Path.GetFullPath(_paths.ApplicationDataRoot);
        var candidate = Path.GetFullPath(executablePath);
        if (!IsWithin(candidate, applicationRoot))
        {
            throw new InvalidDataException("El ejecutable queda fuera del installation root.");
        }

        var current = applicationRoot;
        RejectReparsePoint(current);
        var relative = Path.GetRelativePath(applicationRoot, candidate);
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (_probe.PathExists(current))
            {
                RejectReparsePoint(current);
            }
        }
    }

    private void RejectReparsePoint(string path)
    {
        if ((_probe.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("La ruta administrada contiene un reparse point o junction no permitido.");
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNetworkOrDevicePath(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);
}

internal interface IUpdateExecutableProbe
{
    bool FileExists(string path);
    bool PathExists(string path);
    FileAttributes GetAttributes(string path);
    string? GetProductVersion(string path);
}

internal sealed class PhysicalUpdateExecutableProbe : IUpdateExecutableProbe
{
    public bool FileExists(string path) => File.Exists(path);

    public bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public string? GetProductVersion(string path) => FileVersionInfo.GetVersionInfo(path).ProductVersion;
}
