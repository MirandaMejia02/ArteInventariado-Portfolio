using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;

namespace ArteInventariado.Infrastructure.Updater;

public sealed record MachineWideEntrypointBootstrapResult(
    string SourceVersion,
    string TargetVersion,
    string LauncherPath,
    string UpdaterPath,
    string LauncherSha256,
    string UpdaterSha256);

internal sealed record MachineWideEntrypointProcessInfo(
    int ProcessId,
    string ExecutablePath);

/// <summary>
/// Performs the narrow, one-time bridge from a legacy machine-wide
/// installation to the corrected stable Launcher/Updater entrypoints.
///
/// It deliberately does not create PRE_UPDATE, rewrite CURRENT, touch the
/// database, or install a version.  Once it succeeds, the already-installed
/// Update Center can run the ordinary elevated handoff, which remains the
/// only path that creates PRE_UPDATE and changes the installation.
/// </summary>
public sealed class MachineWideEntrypointBootstrapService
{
    private const long MaximumEntrypointBytes = 512L * 1024 * 1024;
    private readonly UpdatePackageValidator _validator;
    private readonly Func<bool> _isAdministrator;
    private readonly Func<UpdateRuntimePaths, UpdateLaunchTargetResolver> _resolverFactory;
    private readonly Func<UpdateRuntimePaths, IReadOnlyList<MachineWideEntrypointProcessInfo>> _processProbe;
    private readonly Func<string, string, CancellationToken, Task> _replaceFile;
    private readonly bool _allowSyntheticRoots;

    public MachineWideEntrypointBootstrapService()
        : this(
            new UpdatePackageValidator(),
            MachineWideElevation.IsAdministrator,
            paths => new UpdateLaunchTargetResolver(paths),
            FindRunningStableProcesses,
            ReplaceFileAtomicallyAsync,
            allowSyntheticRoots: false)
    {
    }

    internal MachineWideEntrypointBootstrapService(
        UpdatePackageValidator validator,
        Func<bool> isAdministrator,
        Func<UpdateRuntimePaths, UpdateLaunchTargetResolver> resolverFactory,
        Func<UpdateRuntimePaths, IReadOnlyList<MachineWideEntrypointProcessInfo>> processProbe,
        Func<string, string, CancellationToken, Task> replaceFile,
        bool allowSyntheticRoots = false)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        _resolverFactory = resolverFactory ?? throw new ArgumentNullException(nameof(resolverFactory));
        _processProbe = processProbe ?? throw new ArgumentNullException(nameof(processProbe));
        _replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public async Task<MachineWideEntrypointBootstrapResult> BootstrapAsync(
        UpdateRuntimePaths paths,
        string manifestPath,
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "El bootstrap de entrypoints machine-wide requiere Windows.");
        }

        if (paths.PathProvider?.IsMachineWide != true)
        {
            throw new InvalidOperationException(
                "El bootstrap de entrypoints requiere el layout machine-wide protegido.");
        }

        if (!_isAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El bootstrap de entrypoints requiere autorización UAC y un token elevado.");
        }

        var stage = "bootstrap-entrypoints-inicio";
        string? relatedPath = null;
        try
        {
            stage = "bootstrap-entrypoints-raices";
            relatedPath = paths.MachineInstallRoot;
            ValidateMachineWideArguments(paths, _allowSyntheticRoots);
            MachineWideUpdateGuard.ValidateManagedRoots(paths);

            stage = "bootstrap-entrypoints-paquete";
            relatedPath = packagePath;
            EnsureLocalReadableFile(manifestPath, "manifest");
            EnsureLocalReadableFile(packagePath, "paquete");

            stage = "bootstrap-entrypoints-current";
            relatedPath = paths.CurrentPointerPath;
            var pointer = new UpdatePointerStore(paths.CurrentPointerPath).Load()
                ?? throw new InvalidDataException(
                    "No existe CURRENT para preparar el bootstrap de entrypoints.");
            var resolver = _resolverFactory(paths);
            _ = resolver.ResolveCurrent(pointer);
            var sourceVersion = pointer.Version;

            stage = "bootstrap-entrypoints-estacion";
            relatedPath = paths.StationSettingsPath;
            var station = new LocalStationConfigurationStore(paths.StationSettingsPath).Load().Status;
            await new UpdateOperationalPreflight().ValidateAsync(paths, station, cancellationToken);

            stage = "bootstrap-entrypoints-estado";
            relatedPath = paths.StatePath;
            var state = new UpdateStateStore(paths.StatePath).Load();
            if (state is not null && state.State is not (
                    UpdateState.Idle or UpdateState.Success or UpdateState.Failed))
            {
                throw new InvalidOperationException(
                    $"No se puede entregar entrypoints mientras existe una actualización en estado {state.State}.");
            }

            stage = "bootstrap-entrypoints-procesos";
            relatedPath = paths.StableUpdaterPath;
            var running = _processProbe(paths)
                .Where(process => process.ProcessId != Environment.ProcessId)
                .ToArray();
            if (running.Length > 0)
            {
                var names = string.Join(", ", running.Select(process =>
                    $"{Path.GetFileName(process.ExecutablePath)} (PID {process.ProcessId})"));
                throw new InvalidOperationException(
                    "Cierre el Launcher/Updater administrado antes del bootstrap: " + names);
            }

            stage = "bootstrap-entrypoints-validacion-manifest";
            relatedPath = manifestPath;
            var validation = await _validator.ValidateAsync(
                manifestPath,
                packagePath,
                sourceVersion,
                cancellationToken,
                UpdatePackageTargets.MachineWide);
            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidDataException(
                    "El ZIP no supera la validación del updater: " +
                    string.Join(" ", validation.Errors));
            }

            var targetVersion = validation.Manifest.Version;
            var stagingRoot = CreateBootstrapStagingRoot(paths);
            var launcherSource = Path.Combine(stagingRoot, StableDeploymentBootstrap.LauncherExecutableName);
            var updaterSource = Path.Combine(stagingRoot, StableDeploymentBootstrap.UpdaterExecutableName);
            var launcherBackup = Path.Combine(stagingRoot, "old-" + StableDeploymentBootstrap.LauncherExecutableName);
            var updaterBackup = Path.Combine(stagingRoot, "old-" + StableDeploymentBootstrap.UpdaterExecutableName);
            var replaced = new List<(string Destination, string Backup)>();
            try
            {
                stage = "bootstrap-entrypoints-extraccion";
                relatedPath = packagePath;
                await ExtractEntrypointsAsync(
                    packagePath,
                    launcherSource,
                    updaterSource,
                    cancellationToken);
                ValidateEntrypointVersion(launcherSource, targetVersion, StableDeploymentBootstrap.LauncherExecutableName);
                ValidateEntrypointVersion(updaterSource, targetVersion, StableDeploymentBootstrap.UpdaterExecutableName);
                var expectedLauncherHash = await UpdatePackageValidator.ComputeSha256Async(
                    launcherSource,
                    cancellationToken);
                var expectedUpdaterHash = await UpdatePackageValidator.ComputeSha256Async(
                    updaterSource,
                    cancellationToken);

                stage = "bootstrap-entrypoints-reemplazo-launcher";
                relatedPath = paths.StableLauncherPath;
                EnsureStableDestination(paths.StableLauncherPath, StableDeploymentBootstrap.LauncherExecutableName);
                EnsureStableDestination(paths.StableUpdaterPath, StableDeploymentBootstrap.UpdaterExecutableName);
                await CopyFileWithFlushAsync(paths.StableLauncherPath, launcherBackup, cancellationToken);
                await CopyFileWithFlushAsync(paths.StableUpdaterPath, updaterBackup, cancellationToken);
                await _replaceFile(launcherSource, paths.StableLauncherPath, cancellationToken);
                replaced.Add((paths.StableLauncherPath, launcherBackup));

                stage = "bootstrap-entrypoints-reemplazo-updater";
                relatedPath = paths.StableUpdaterPath;
                await _replaceFile(updaterSource, paths.StableUpdaterPath, cancellationToken);
                replaced.Add((paths.StableUpdaterPath, updaterBackup));

                stage = "bootstrap-entrypoints-verificacion";
                relatedPath = paths.StableLauncherPath;
                var launcherHash = await UpdatePackageValidator.ComputeSha256Async(paths.StableLauncherPath, cancellationToken);
                var updaterHash = await UpdatePackageValidator.ComputeSha256Async(paths.StableUpdaterPath, cancellationToken);
                if (!FixedEquals(launcherHash, expectedLauncherHash) ||
                    !FixedEquals(updaterHash, expectedUpdaterHash))
                {
                    throw new InvalidDataException(
                        "Los entrypoints instalados no coinciden con los bytes extraídos del ZIP.");
                }

                return new MachineWideEntrypointBootstrapResult(
                    sourceVersion,
                    targetVersion,
                    paths.StableLauncherPath,
                    paths.StableUpdaterPath,
                    launcherHash,
                    updaterHash);
            }
            catch
            {
                stage = "bootstrap-entrypoints-rollback";
                foreach (var (destination, backup) in replaced.AsEnumerable().Reverse())
                {
                    try
                    {
                        await _replaceFile(backup, destination, CancellationToken.None);
                    }
                    catch (Exception rollbackError) when (
                        rollbackError is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        MachineWideUpdateDiagnostics.Write(
                            paths,
                            "bootstrap-entrypoints-rollback-failed",
                            rollbackError,
                            executablePath: paths.StableUpdaterPath,
                            relatedPath: destination);
                        throw new IOException(
                            $"No se pudo restaurar el entrypoint '{destination}' tras un fallo del bootstrap.",
                            rollbackError);
                    }
                }

                throw;
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
        catch (Exception ex)
        {
            MachineWideUpdateDiagnostics.Write(
                paths,
                stage,
                ex,
                executablePath: Environment.ProcessPath,
                tokenElevated: MachineWideElevation.TryGetTokenElevation(out var elevated)
                    ? elevated
                    : null,
                relatedPath: relatedPath);
            throw;
        }
    }

    private static void ValidateMachineWideArguments(
        UpdateRuntimePaths paths,
        bool allowSyntheticRoots)
    {
        var provider = paths.PathProvider
            ?? throw new InvalidDataException("El bootstrap no resolvió el proveedor machine-wide.");
        MachineWideUpdateGuard.ValidateUpdaterArguments(
            [
                "--machine-install-root", provider.MachineInstallRoot,
                "--machine-data-root", provider.MachineDataRoot,
                "--user-data-root", provider.UserDataRoot,
                "--state", paths.StatePath
            ],
            paths,
            allowSyntheticRoots);
    }

    private static string CreateBootstrapStagingRoot(UpdateRuntimePaths paths)
    {
        var root = Path.Combine(
            paths.MachineInstallRoot,
            $".entrypoint-bootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("El staging de entrypoints no puede ser un reparse point.");
        }

        return root;
    }

    private static async Task ExtractEntrypointsAsync(
        string packagePath,
        string launcherDestination,
        string updaterDestination,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var launcher = FindRootEntry(archive, StableDeploymentBootstrap.LauncherExecutableName);
        var updater = FindRootEntry(archive, StableDeploymentBootstrap.UpdaterExecutableName);
        await ExtractEntryAsync(launcher, launcherDestination, cancellationToken);
        await ExtractEntryAsync(updater, updaterDestination, cancellationToken);
    }

    private static ZipArchiveEntry FindRootEntry(ZipArchive archive, string name)
    {
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null || entry.Length <= 0 || entry.Length > MaximumEntrypointBytes)
        {
            throw new InvalidDataException(
                $"El ZIP no contiene un entrypoint válido: {name}.");
        }

        return entry;
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static void ValidateEntrypointVersion(
        string path,
        string expectedVersion,
        string name)
    {
        var productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        if (!UpdateVersion.TryParse(productVersion, out var actual) ||
            actual.CompareTo(UpdateVersion.Parse(expectedVersion)) != 0)
        {
            throw new InvalidDataException(
                $"El {name} no declara ProductVersion {expectedVersion}.");
        }
    }

    private static void EnsureStableDestination(string path, string expectedName)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(full), expectedName, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full))
        {
            throw new FileNotFoundException(
                $"No existe el entrypoint estable requerido: {expectedName}.",
                full);
        }

        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"El entrypoint estable no puede ser un reparse point: {full}.");
        }
    }

    private static void EnsureLocalReadableFile(string path, string description)
    {
        var full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(full) ||
            full.StartsWith("\\\\", StringComparison.Ordinal) ||
            full.StartsWith("//", StringComparison.Ordinal) ||
            full.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"La ruta de {description} debe ser local y absoluta.");
        }

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"No existe el {description} seleccionado.", full);
        }

        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"El {description} no puede ser un reparse point.");
        }
    }

    private static IReadOnlyList<MachineWideEntrypointProcessInfo> FindRunningStableProcesses(
        UpdateRuntimePaths paths)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(paths.StableLauncherPath),
            Path.GetFullPath(paths.StableUpdaterPath)
        };
        var expectedNames = expected
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<MachineWideEntrypointProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                var executable = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executable) && expected.Contains(Path.GetFullPath(executable)))
                {
                    result.Add(new MachineWideEntrypointProcessInfo(process.Id, executable));
                }
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                // If Windows refuses inspection of a process with one of the
                // managed entrypoint names, fail closed: replacing a file
                // that may still be executing is not safe. Other protected
                // processes are outside this operation's scope.
                if (expectedNames.Contains(process.ProcessName))
                {
                    var expectedPath = expected.First(path =>
                        string.Equals(
                            Path.GetFileNameWithoutExtension(path),
                            process.ProcessName,
                            StringComparison.OrdinalIgnoreCase));
                    result.Add(new MachineWideEntrypointProcessInfo(process.Id, expectedPath));
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static async Task CopyFileWithFlushAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static Task ReplaceFileAtomicallyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Replace(source, destination, destinationBackupFileName: null);
        return Task.CompletedTask;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover private staging directory is harmless and is never
            // used as installation state. The next bootstrap validates it
            // before creating another unique directory.
        }
    }

    private static bool FixedEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
