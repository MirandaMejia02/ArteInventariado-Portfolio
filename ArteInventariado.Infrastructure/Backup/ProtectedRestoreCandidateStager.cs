using System.Security.Cryptography;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Copy-only boundary for importing a local .aibak into a fixed protected
/// recovery directory. The live database and station files are never opened.
/// </summary>
public sealed class ProtectedRestoreCandidateStager
{
    private const int CopyBufferSize = 1024 * 1024;
    private readonly Func<string, bool> _isSupportedLocalVolume;
    private readonly Func<bool> _isElevated;
    private readonly bool _allowSyntheticRoots;

    public ProtectedRestoreCandidateStager()
        : this(IsSupportedLocalVolume, MachineWideElevation.IsAdministrator, allowSyntheticRoots: false)
    {
    }

    internal ProtectedRestoreCandidateStager(
        Func<string, bool> isSupportedLocalVolume,
        Func<bool> isElevated,
        bool allowSyntheticRoots = false)
    {
        _isSupportedLocalVolume = isSupportedLocalVolume ?? throw new ArgumentNullException(nameof(isSupportedLocalVolume));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public async Task<(string Path, string Sha256, long SizeBytes)> StageAsync(
        ArteInventariadoPathProvider paths,
        string sourcePath,
        Guid requestId,
        string expectedPackageSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide)
        {
            throw new InvalidDataException("La importacion protegida requiere una instalacion machine-wide.");
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, _allowSyntheticRoots);
        if (!_isElevated())
        {
            throw new UnauthorizedAccessException("La importacion protegida requiere el helper elevado.");
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de importacion no es valido.", nameof(requestId));
        }

        ValidateSha256(expectedPackageSha256);
        var source = ValidateRemovableSourcePath(sourcePath);
        var stagingRoot = EnsureStagingRoot(paths);
        var stem = requestId.ToString("N");
        var target = Path.Combine(stagingRoot, $"{stem}.aibak");
        var partial = Path.Combine(stagingRoot, $"{stem}.partial");
        if (File.Exists(target) || Directory.Exists(target) || File.Exists(partial) || Directory.Exists(partial))
        {
            throw new IOException("El identificador de importacion ya existe; no se sobrescribio ningun archivo.");
        }

        var published = false;
        try
        {
            await using var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length <= 0)
            {
                throw new InvalidDataException("El respaldo seleccionado esta vacio.");
            }

            string actualHash;
            long size;
            await using (var output = new FileStream(
                             partial,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.WriteThrough | FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[CopyBufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
                actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (!FixedEquals(actualHash, expectedPackageSha256))
                {
                    throw new InvalidDataException("El .aibak cambio antes o durante la copia; no se publico la importacion.");
                }

                size = output.Length;
                if (size <= 0 || size != input.Length)
                {
                    throw new InvalidDataException("La copia protegida no coincide en tamaño con el archivo seleccionado.");
                }
            }

            File.Move(partial, target);
            published = true;
            return (target, actualHash, size);
        }
        catch
        {
            TryDelete(partial);
            if (published)
            {
                TryDelete(target);
            }

            throw;
        }
    }

    internal static string ValidateSourcePathSyntax(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath.Trim()))
        {
            throw new ArgumentException("El origen debe ser una ruta local absoluta.", nameof(sourcePath));
        }

        var full = Path.GetFullPath(sourcePath.Trim());
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root) ||
            full.StartsWith("\\\\", StringComparison.Ordinal) ||
            full.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            full.AsSpan(root.Length).Contains(':'))
        {
            throw new InvalidDataException("No se aceptan rutas de red, extendidas ni flujos alternativos para importar respaldos.");
        }

        if (!full.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El archivo seleccionado debe tener extension .aibak.");
        }

        if (!MachineWideAssetPathGuard.TryGetRegularFile(full, root, out var file, out var issue) || file is null)
        {
            throw new InvalidDataException($"El origen no es un archivo local regular y sin enlaces ({issue}).");
        }

        return full;
    }

    internal static bool IsSupportedLocalVolume(string sourcePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(sourcePath);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root) || root.StartsWith("\\\\", StringComparison.Ordinal))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private string ValidateRemovableSourcePath(string sourcePath)
    {
        var full = ValidateSourcePathSyntax(sourcePath);
        if (!_isSupportedLocalVolume(full))
        {
            throw new InvalidDataException("El respaldo debe estar en una unidad local o USB disponible; no se aceptan rutas de red.");
        }

        return full;
    }

    private static string EnsureStagingRoot(ArteInventariadoPathProvider paths)
    {
        var recoveryRoot = Path.GetFullPath(paths.RecoveryRoot);
        var updateStateRoot = Path.GetFullPath(paths.UpdateStateRoot);
        var stagingRoot = Path.GetFullPath(paths.LocalRestoreStagingRoot);
        var machineDataRoot = Path.GetFullPath(paths.MachineDataRoot);
        if (!MachineWideAssetPathGuard.IsWithin(updateStateRoot, machineDataRoot) ||
            !PathEquals(Directory.GetParent(updateStateRoot)?.FullName, machineDataRoot) ||
            !MachineWideAssetPathGuard.IsWithin(recoveryRoot, updateStateRoot) ||
            !PathEquals(Directory.GetParent(recoveryRoot)?.FullName, updateStateRoot) ||
            !PathEquals(Directory.GetParent(stagingRoot)?.FullName, recoveryRoot))
        {
            throw new InvalidDataException("La raiz de importacion no coincide con Recovery\\Local.");
        }

        if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(updateStateRoot, machineDataRoot, out var updateIssue))
        {
            throw new InvalidDataException($"La raiz UpdateState contiene un enlace no permitido ({updateIssue}).");
        }

        if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(recoveryRoot, updateStateRoot, out var recoveryIssue))
        {
            throw new InvalidDataException($"La raiz Recovery contiene un enlace no permitido ({recoveryIssue}).");
        }

        Directory.CreateDirectory(stagingRoot);
        if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(stagingRoot, recoveryRoot, out var stagingIssue))
        {
            throw new InvalidDataException($"La raiz Local protegida contiene un enlace no permitido ({stagingIssue}).");
        }

        return stagingRoot;
    }

    private static void ValidateSha256(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("El SHA-256 esperado debe tener 64 digitos hexadecimales.");
        }
    }

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

    private static bool PathEquals(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
