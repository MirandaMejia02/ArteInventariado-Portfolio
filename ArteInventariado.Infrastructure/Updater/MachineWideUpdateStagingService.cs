using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Updater;

public sealed record MachineWideUpdateStagingResult(
    string StagingDirectory,
    string ManifestPath,
    string PackagePath,
    UpdatePackageValidationResult Validation)
{
    public Guid TransactionId => Guid.TryParseExact(
            new DirectoryInfo(StagingDirectory).Name,
            "N",
            out var value)
        ? value
        : throw new InvalidDataException("El staging no tiene un identificador de transaccion valido.");
}

/// <summary>
/// Creates the only non-elevated handoff accepted by the machine-wide
/// updater. The directory is per-user, GUID-scoped and contains no canonical
/// update state or PRE_UPDATE backup.
/// </summary>
public sealed class MachineWideUpdateStagingService
{
    public const string ManifestFileName = "manifest.json";

    private readonly UpdatePackageValidator _validator;

    public MachineWideUpdateStagingService(UpdatePackageValidator? validator = null)
    {
        _validator = validator ?? new UpdatePackageValidator();
    }

    public async Task<MachineWideUpdateStagingResult> StageAsync(
        UpdateRuntimePaths paths,
        string manifestPath,
        string packagePath,
        string installedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedVersion);

        EnsureMachineWide(paths);
        EnsureReadableSource(manifestPath, "manifest");
        EnsureReadableSource(packagePath, "paquete");

        var sourceManifest = await _validator.ValidateAsync(
            manifestPath,
            packagePath,
            installedVersion,
            cancellationToken,
            UpdatePackageTargets.MachineWide);
        if (!sourceManifest.IsValid || sourceManifest.Manifest is null)
        {
            throw new InvalidDataException(string.Join(" ", sourceManifest.Errors));
        }

        var packageFileName = Path.GetFileName(packagePath);
        if (!string.Equals(
                packageFileName,
                sourceManifest.Manifest.PackagePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El paquete no coincide con el nombre declarado por el manifest.");
        }

        var stagingRoot = Path.GetFullPath(paths.StagingRoot);
        Directory.CreateDirectory(stagingRoot);
        var stagingDirectory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        MachineWideUpdateGuard.ValidateHandoffPath(
            paths,
            stagingDirectory);
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            MachineWideUpdateGuard.ValidateHandoffPath(paths, stagingDirectory);
            var stagedManifest = Path.Combine(stagingDirectory, ManifestFileName);
            var stagedPackage = Path.Combine(stagingDirectory, packageFileName);
            await CopyAtomicallyAsync(manifestPath, stagedManifest, cancellationToken);
            await CopyAtomicallyAsync(packagePath, stagedPackage, cancellationToken);

            // The source can be changed while the non-elevated copy runs.
            // Validate the bytes that will actually cross the UAC boundary.
            var stagedValidation = await _validator.ValidateAsync(
                stagedManifest,
                stagedPackage,
                installedVersion,
                cancellationToken,
                UpdatePackageTargets.MachineWide);
            if (!stagedValidation.IsValid || stagedValidation.Manifest is null)
            {
                throw new InvalidDataException(
                    "El staging no supera la validacion despues de copiarse: " +
                    string.Join(" ", stagedValidation.Errors));
            }

            return new MachineWideUpdateStagingResult(
                stagingDirectory,
                stagedManifest,
                stagedPackage,
                stagedValidation);
        }
        catch
        {
            DeleteStagingDirectory(paths, stagingDirectory);
            throw;
        }
    }

    public static void DeleteStagingDirectory(UpdateRuntimePaths paths, string stagingDirectory)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        EnsureMachineWide(paths);
        MachineWideUpdateGuard.ValidateHandoffPath(paths, stagingDirectory);
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static void EnsureMachineWide(UpdateRuntimePaths paths)
    {
        if (paths.PathProvider?.IsMachineWide != true)
        {
            throw new InvalidOperationException(
                "El staging machine-wide requiere el layout protegido.");
        }
    }

    private static void EnsureReadableSource(string path, string description)
    {
        var full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(full) ||
            full.StartsWith("\\\\", StringComparison.Ordinal) ||
            full.StartsWith("//", StringComparison.Ordinal) ||
            full.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"La ruta de {description} no es local y absoluta.");
        }

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"No existe el {description} seleccionado.", full);
        }

        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"El {description} seleccionado no puede ser un reparse point.");
        }
    }

    private static async Task CopyAtomicallyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
