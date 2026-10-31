using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Updater;

public static class UpdaterSmokeHarness
{
    public static async Task<int> RunAsync(
        string publishDirectory,
        string outputPath,
        string? repositoryRoot = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("El smoke del launcher y accesos directos requiere Windows.");
        }

        var publish = Path.GetFullPath(publishDirectory);
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, $"Updater smoke iniciado {DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        var smokeRoot = Path.Combine(Path.GetTempPath(), $"ArteInventariado-updater-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(smokeRoot);
        var completed = false;

        try
        {
            var applicationExecutable = Path.Combine(publish, "ArteInventariado.WinForms.exe");
            var updaterExecutable = Path.Combine(publish, "ArteInventariado.Updater.exe");
            var launcherExecutable = Path.Combine(publish, "ArteInventariado.Launcher.exe");
            Require(File.Exists(applicationExecutable), "No existe ArteInventariado.WinForms.exe en el publish.");
            Require(File.Exists(updaterExecutable), "No existe ArteInventariado.Updater.exe en el publish.");
            Require(File.Exists(launcherExecutable), "No existe ArteInventariado.Launcher.exe en el publish.");

            await StageAsync("A", "app normal sin update", output, async () =>
            {
                var appSmokeDirectory = Path.Combine(smokeRoot, "app-normal");
                var appVerificationRoot = Path.Combine(appSmokeDirectory, "app-data");
                var appSmokeLog = Path.Combine(appSmokeDirectory, "pos-barcode-smoke.log");
                var exitCode = await RunProcessAsync(
                    applicationExecutable,
                    publish,
                    new Dictionary<string, string>
                    {
                        ["ARTE_UPDATE_VERIFY"] = "1",
                        [LocalApplicationDataPathProvider.VerificationRootEnvironmentVariable] = appVerificationRoot,
                        ["ARTE_UPDATE_REDIRECTED"] = "1",
                        ["ARTE_POS_BARCODE_SMOKE"] = "1",
                        ["ARTE_POS_BARCODE_SMOKE_OUTPUT"] = appSmokeDirectory,
                        ["ARTE_POS_BARCODE_SMOKE_QUICK_ONLY"] = "1"
                    },
                    TimeSpan.FromMinutes(3),
                    cancellationToken);
                Require(exitCode == 0, $"La aplicacion publicada termino con codigo {exitCode}.");
                Require(File.Exists(appSmokeLog), "La aplicacion publicada no genero evidencia de smoke.");
                return "ejecutable publicado abre y termina su smoke normal";
            });

            var targetVersion = ReadProductVersion(applicationExecutable);
            var sourceVersion = PreviousVersion(targetVersion);
            var sourceExecutable = ResolveSourceExecutable(repositoryRoot, sourceVersion, smokeRoot);
            var packagePath = Path.Combine(smokeRoot, $"ArteInventariado-{targetVersion}-win-x64.zip");
            await CreateUpdatePackageAsync(publish, packagePath, cancellationToken);
            var manifestPath = await CreateUpdateManifestAsync(packagePath, sourceVersion, targetVersion, cancellationToken);
            var validator = new UpdatePackageValidator();

            await StageAsync("B", "manifest inexistente", output, async () =>
            {
                var missing = await validator.ValidateAsync(
                    Path.Combine(smokeRoot, "missing-manifest.json"),
                    packagePath,
                    sourceVersion,
                    cancellationToken);
                Require(!missing.IsValid && missing.Errors.Any(error => error.Contains("manifest", StringComparison.OrdinalIgnoreCase)),
                    "El manifest inexistente no fue rechazado.");
                return "rechazado de forma controlada";
            });

            UpdatePackageValidationResult packageValidation = null!;
            await StageAsync("C", "package local valido + SHA-256 + ZIP/Zip Slip", output, async () =>
            {
                packageValidation = await validator.ValidateAsync(manifestPath, packagePath, sourceVersion, cancellationToken);
                Require(packageValidation.IsValid, string.Join(" ", packageValidation.Errors));
                var unsafePackage = Path.Combine(smokeRoot, "unsafe.zip");
                using (var archive = ZipFile.Open(unsafePackage, ZipArchiveMode.Create))
                {
                    await WriteEntryAsync(archive, "ArteInventariado.WinForms.exe", await File.ReadAllBytesAsync(applicationExecutable, cancellationToken));
                    await WriteEntryAsync(archive, "../escape.txt", Encoding.UTF8.GetBytes("escape"));
                }

                var unsafeManifest = await CreateUpdateManifestAsync(unsafePackage, sourceVersion, targetVersion, cancellationToken);
                var unsafeResult = await validator.ValidateAsync(unsafeManifest, unsafePackage, sourceVersion, cancellationToken);
                Require(!unsafeResult.IsValid && unsafeResult.Errors.Any(error => error.Contains("ruta", StringComparison.OrdinalIgnoreCase)),
                    "Zip Slip no fue rechazado.");
                return $"SHA-256 OK; expandido {packageValidation.ExpandedSizeBytes} bytes; Zip Slip rechazado";
            });

            await using var principal = await SmokeFixture.CreateAsync(
                Path.Combine(smokeRoot, "principal"),
                InstallationMode.Principal,
                sourceExecutable,
                manifestPath,
                packagePath,
                sourceVersion,
                targetVersion,
                cancellationToken);

            var deploymentDesktop = Path.Combine(smokeRoot, "deployment", "Desktop");
            var deploymentPrograms = Path.Combine(smokeRoot, "deployment", "Programs");
            var resolutionOutput = Path.Combine(smokeRoot, "deployment", "launcher-resolution.log");
            var shortcutArguments = BuildLauncherSmokeArguments(
                principal.Paths.ApplicationDataRoot,
                deploymentDesktop,
                deploymentPrograms,
                resolutionOutput);
            var shortcutLocations = new ShortcutLocations(deploymentDesktop, deploymentPrograms);
            var shortcutBackend = new ComWindowsShortcutBackend();
            SeedLegacyShortcuts(shortcutBackend, shortcutLocations, principal.SourceExecutable, smokeRoot);
            var shortcutManager = new WindowsShortcutManager(shortcutLocations, shortcutBackend, shortcutArguments);
            var stableEntryPoint = new StableEntryPointInstaller(principal.Paths, shortcutManager);
            stableEntryPoint.InstallStableBinaries(publish);
            stableEntryPoint.EnsureShortcuts();
            var deploymentFinalizer = new StableDeploymentFinalizer(stableEntryPoint);
            var startShortcut = Path.Combine(deploymentPrograms, "ArteInventariado.lnk");
            var desktopShortcut = Path.Combine(deploymentDesktop, "ArteInventariado.lnk");
            var principalBaselineFingerprint = await principal.ComputeDatabaseFingerprintAsync(cancellationToken);

            await StageAsync("D0", "first install + shortcuts estables + version base", output, async () =>
            {
                RequireStableShortcutTargets(shortcutBackend, shortcutLocations, principal.Paths.StableLauncherPath);
                var opened = await RunShortcutAndReadResolutionAsync(
                    startShortcut,
                    resolutionOutput,
                    TimeSpan.FromMinutes(1),
                    cancellationToken);
                Require(opened.Version == sourceVersion, $"Inicio abrio {opened.Version} y se esperaba {sourceVersion}.");
                Require(opened.Executable == Path.GetFullPath(principal.SourceExecutable), "Inicio no resolvio CURRENT base.");
                return $"Inicio/Escritorio y legados apuntan al launcher; mismo acceso abre {sourceVersion}";
            });

            await StageAsync("D", "PRE_UPDATE", output, async () =>
            {
                var backupManifest = await new UpdateBackupService().ValidatePreUpdateAsync(
                    principal.PreUpdatePath,
                    cancellationToken);
                Require(backupManifest.Kind == BackupKind.PreUpdate, "El respaldo no quedo marcado PRE_UPDATE.");
                Require(backupManifest.SourceInstallationId == principal.InstallationId, "El PRE_UPDATE no conserva installationId.");
                return $"valido {Path.GetFileName(principal.PreUpdatePath)}";
            });

            await StageAsync("E", "update completo", output, async () =>
            {
                var installer = principal.CreateInstaller(
                    new ProcessUpdateApplicationVerifier(TimeSpan.FromMinutes(2)),
                    deploymentFinalizer);
                var result = await installer.InstallFromStateAsync(cancellationToken);
                Require(result == 0, $"Instalacion termino con codigo {result}.");
                var state = principal.StateStore.Load()!;
                Require(state.State == UpdateState.Success, $"Estado final inesperado: {state.State}.");
                Require(File.Exists(state.CurrentVersionPath), "No existe el ejecutable side-by-side.");
                var pointer = principal.PointerStore.Load()!;
                Require(pointer.ExecutablePath == Path.GetFullPath(state.CurrentVersionPath!), "current no apunta al ejecutable verificado.");
                Require(pointer.PreviousExecutablePath == principal.SourceExecutable, "previous no conserva el ejecutable anterior.");
                Require(await principal.ComputeDatabaseFingerprintAsync(cancellationToken) == principalBaselineFingerprint,
                    "El contenido comercial cambio durante un update sin migraciones.");
                var fromStart = await RunShortcutAndReadResolutionAsync(
                    startShortcut,
                    resolutionOutput,
                    TimeSpan.FromMinutes(1),
                    cancellationToken);
                var fromDesktop = await RunShortcutAndReadResolutionAsync(
                    desktopShortcut,
                    resolutionOutput,
                    TimeSpan.FromMinutes(1),
                    cancellationToken);
                Require(fromStart.Version == targetVersion && fromDesktop.Version == targetVersion,
                    "Los accesos estables no abrieron RC3 despues del update.");
                return "verificacion real, side-by-side, current/previous y mismos shortcuts abren RC3";
            });

            await using var failing = await SmokeFixture.CreateAsync(
                Path.Combine(smokeRoot, "rollback"),
                InstallationMode.Principal,
                sourceExecutable,
                manifestPath,
                packagePath,
                sourceVersion,
                targetVersion,
                cancellationToken);
            await StageAsync("F", "fallo de migracion/verificacion + rollback", output, async () =>
            {
                var beforeDatabase = await UpdatePackageValidator.ComputeSha256Async(failing.Paths.DatabasePath, cancellationToken);
                var installer = failing.CreateInstaller(new MutatingFailureVerifier(failing));
                var result = await installer.InstallFromStateAsync(cancellationToken);
                Require(result == 2, $"El fallo controlado termino con codigo {result}.");
                Require(failing.StateStore.Load()!.State == UpdateState.Failed, "No termino en FAILED tras rollback completo.");
                Require(await UpdatePackageValidator.ComputeSha256Async(failing.Paths.DatabasePath, cancellationToken) == beforeDatabase,
                    "La DB no regreso al PRE_UPDATE.");
                Require(await File.ReadAllTextAsync(failing.AssetPath, cancellationToken) == SmokeFixture.OriginalAssetContent,
                    "El asset no regreso al PRE_UPDATE.");
                Require(failing.PointerStore.Load()!.ExecutablePath == failing.SourceExecutable,
                    "El puntero anterior no fue restaurado.");
                failing.EnsureStationIdentity();
                return "DB, asset, station-settings y previous restaurados; SUCCESS no fue emitido";
            });

            await StageAsync("G", "PRINCIPAL preservado", output, () =>
            {
                principal.EnsureStationIdentity();
                Require(principal.Mode == InstallationMode.Principal, "La estacion PRINCIPAL cambio de modo.");
                return Task.FromResult("installationId y stationMode PRINCIPAL intactos");
            });

            await using var consulta = await SmokeFixture.CreateAsync(
                Path.Combine(smokeRoot, "consulta"),
                InstallationMode.Consulta,
                sourceExecutable,
                manifestPath,
                packagePath,
                sourceVersion,
                targetVersion,
                cancellationToken);
            await StageAsync("H", "CONSULTA preservado", output, async () =>
            {
                var result = await consulta.CreateInstaller(
                    new ProcessUpdateApplicationVerifier(TimeSpan.FromMinutes(2)))
                    .InstallFromStateAsync(cancellationToken);
                Require(result == 0, $"La verificacion CONSULTA termino con codigo {result}.");
                consulta.EnsureStationIdentity();
                Require(consulta.Mode == InstallationMode.Consulta, "La estacion CONSULTA cambio de modo.");
                return "arranque read-only verificado; installationId y modo intactos";
            });

            await using var interrupted = await SmokeFixture.CreateAsync(
                Path.Combine(smokeRoot, "interrupted"),
                InstallationMode.Principal,
                sourceExecutable,
                manifestPath,
                packagePath,
                sourceVersion,
                targetVersion,
                cancellationToken);
            await StageAsync("I", "recuperacion de estado incompleto", output, async () =>
            {
                var ready = interrupted.StateStore.Load()!;
                var targetDirectory = interrupted.Paths.GetVersionDirectory(targetVersion);
                Directory.CreateDirectory(targetDirectory);
                var targetExecutable = Path.Combine(targetDirectory, "ArteInventariado.WinForms.exe");
                File.Copy(applicationExecutable, targetExecutable);
                interrupted.PointerStore.Save(targetVersion, targetExecutable, interrupted.SourceExecutable, DateTimeOffset.UtcNow);
                await interrupted.MutateAsync(cancellationToken);
                interrupted.StateStore.Save(ready with
                {
                    State = UpdateState.Verifying,
                    PreviousVersionPath = interrupted.SourceExecutable,
                    CurrentVersionPath = targetExecutable,
                    DatabaseMayHaveChanged = true,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });
                var recovery = await interrupted.CreateInstaller(new NeverCalledVerifier())
                    .RecoverInterruptedAsync(cancellationToken);
                Require(recovery == 0, $"Recuperacion termino con codigo {recovery}.");
                Require(interrupted.StateStore.Load()!.State == UpdateState.Failed, "La recuperacion no cerro el estado incompleto.");
                Require(!Directory.Exists(targetDirectory), "La version incompleta no fue retirada.");
                interrupted.EnsureStationIdentity();

                var futureDatabase = Path.Combine(smokeRoot, "future-schema.db");
                File.Copy(interrupted.Paths.DatabasePath, futureDatabase);
                await AddFutureMigrationAsync(futureDatabase, cancellationToken);
                var migrations = await ReadMigrationsAsync(interrupted.Paths.DatabasePath, cancellationToken);
                RequireThrows<IncompatibleDatabaseException>(() =>
                    SchemaCompatibilityGuard.EnsureCompatible(futureDatabase, migrations));
                return "crash recovery idempotente y schema guard de migracion futura OK";
            });

            await StageAsync("J", "ejecucion publicada fuera del repo", output, () =>
            {
                if (!string.IsNullOrWhiteSpace(repositoryRoot))
                {
                    var repo = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    Require(!publish.StartsWith(repo, StringComparison.OrdinalIgnoreCase),
                        "El smoke J se ejecuto dentro del repositorio.");
                }

                Require(string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), publish.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
                    "El updater ejecutado no pertenece al publish externo.");
                return Task.FromResult($"WinForms + Updater ejecutados desde {publish}");
            });

            await StageAsync("K", "rollback oficial + mismo shortcut + re-update + current invalido", output, async () =>
            {
                await principal.MutateBusinessAsync(cancellationToken);
                var rollbackResult = await principal.CreateInstaller(
                        new NeverCalledVerifier(),
                        deploymentFinalizer)
                    .RollbackSuccessfulAsync(cancellationToken);
                Require(rollbackResult == 0, $"Rollback manual termino con codigo {rollbackResult}.");
                Require(await principal.ComputeDatabaseFingerprintAsync(cancellationToken) == principalBaselineFingerprint,
                    "El rollback manual no recupero la DB PRE_UPDATE.");
                Require(await File.ReadAllTextAsync(principal.AssetPath, cancellationToken) == SmokeFixture.OriginalAssetContent,
                    "El rollback manual no recupero assets.");
                principal.EnsureStationIdentity();
                var rolledBack = await RunShortcutAndReadResolutionAsync(
                    startShortcut,
                    resolutionOutput,
                    TimeSpan.FromMinutes(1),
                    cancellationToken);
                Require(rolledBack.Version == sourceVersion, "El mismo shortcut no abrio la version anterior tras rollback.");

                await principal.PrepareForReinstallAsync(cancellationToken);
                var reinstallResult = await principal.CreateInstaller(
                        new ProcessUpdateApplicationVerifier(TimeSpan.FromMinutes(2)),
                        deploymentFinalizer)
                    .InstallFromStateAsync(cancellationToken);
                Require(reinstallResult == 0, $"Re-update termino con codigo {reinstallResult}.");
                var reupdated = await RunShortcutAndReadResolutionAsync(
                    startShortcut,
                    resolutionOutput,
                    TimeSpan.FromMinutes(1),
                    cancellationToken);
                Require(reupdated.Version == targetVersion, "El mismo shortcut no abrio RC3 tras re-update.");

                var validPointer = principal.PointerStore.Load()!;
                principal.PointerStore.Save(
                    targetVersion,
                    Path.Combine(smokeRoot, "outside", UpdateLaunchTargetResolver.ApplicationExecutableName),
                    principal.SourceExecutable,
                    DateTimeOffset.UtcNow);
                var invalidResult = await RunLauncherDiagnosticAsync(
                    principal.Paths.StableLauncherPath,
                    principal.Paths.ApplicationDataRoot,
                    deploymentDesktop,
                    deploymentPrograms,
                    resolutionOutput,
                    cancellationToken);
                Require(invalidResult != 0, "El launcher acepto un CURRENT externo manipulado.");
                principal.PointerStore.Save(
                    validPointer.Version,
                    validPointer.ExecutablePath,
                    validPointer.PreviousExecutablePath,
                    DateTimeOffset.UtcNow);
                RequireStableShortcutTargets(shortcutBackend, shortcutLocations, principal.Paths.StableLauncherPath);
                return "rollback DB/assets/estacion OK; mismo acceso base -> RC3 -> base -> RC3; current externo rechazado";
            });

            Append(output, "RESULT=OK");
            completed = true;
            return 0;
        }
        catch (Exception ex)
        {
            Append(output, $"RESULT=FAIL | {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            if (completed)
            {
                TryDeleteDirectory(smokeRoot);
            }
            else
            {
                Append(output, $"TEMP_RETAINED={smokeRoot}");
            }
        }
    }

    private static async Task StageAsync(
        string letter,
        string name,
        string outputPath,
        Func<Task<string>> action)
    {
        Append(outputPath, $"{letter} START | {name}");
        try
        {
            var detail = await action();
            Append(outputPath, $"{letter} OK | {detail}");
        }
        catch (Exception ex)
        {
            Append(outputPath, $"{letter} FAIL | {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private static async Task<int> RunProcessAsync(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var pair in environment)
        {
            info.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"No se pudo iniciar {Path.GetFileName(executable)}.");
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            return process.ExitCode;
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }

            throw;
        }
    }

    private static string BuildLauncherSmokeArguments(
        string applicationDataRoot,
        string desktopDirectory,
        string programsDirectory,
        string resolutionOutput) =>
        string.Join(' ', new[]
        {
            "--app-data-root", QuoteArgument(applicationDataRoot),
            "--desktop-directory", QuoteArgument(desktopDirectory),
            "--programs-directory", QuoteArgument(programsDirectory),
            "--resolve-output", QuoteArgument(resolutionOutput),
            "--no-launch",
            "--headless"
        });

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static void SeedLegacyShortcuts(
        IWindowsShortcutBackend backend,
        ShortcutLocations locations,
        string sourceExecutable,
        string smokeRoot)
    {
        var missing = Path.Combine(smokeRoot, "visual-final-inexistente", UpdateLaunchTargetResolver.ApplicationExecutableName);
        backend.Save(
            Path.Combine(locations.DesktopDirectory, "ArteInventariado.lnk"),
            missing,
            Path.GetDirectoryName(missing)!,
            sourceExecutable,
            "ArteInventariado visual-final",
            string.Empty);
        backend.Save(
            Path.Combine(locations.ProgramsDirectory, "ArteInventariado.lnk"),
            sourceExecutable,
            Path.GetDirectoryName(sourceExecutable)!,
            sourceExecutable,
            "ArteInventariado RC1 directo",
            string.Empty);
        backend.Save(
            Path.Combine(locations.ProgramsDirectory, "ArteInventariado.WinForms.lnk"),
            sourceExecutable,
            Path.GetDirectoryName(sourceExecutable)!,
            sourceExecutable,
            "ArteInventariado version vieja",
            string.Empty);
        backend.Save(
            Path.Combine(locations.ProgramsDirectory, "ArteInventariado", "ArteInventariado.lnk"),
            missing,
            Path.GetDirectoryName(missing)!,
            sourceExecutable,
            "ArteInventariado legado",
            string.Empty);
    }

    private static void RequireStableShortcutTargets(
        IWindowsShortcutBackend backend,
        ShortcutLocations locations,
        string launcherPath)
    {
        var expected = Path.GetFullPath(launcherPath);
        var shortcuts = new[]
        {
            Path.Combine(locations.DesktopDirectory, "ArteInventariado.lnk"),
            Path.Combine(locations.ProgramsDirectory, "ArteInventariado.lnk"),
            Path.Combine(locations.ProgramsDirectory, "ArteInventariado.WinForms.lnk"),
            Path.Combine(locations.ProgramsDirectory, "ArteInventariado", "ArteInventariado.lnk")
        };
        Require(shortcuts.All(backend.Exists), "No quedaron creados/reparados todos los accesos esperados.");
        Require(shortcuts.All(path => string.Equals(backend.GetTargetOrNull(path), expected, StringComparison.OrdinalIgnoreCase)),
            "Un acceso directo no apunta al launcher estable.");
    }

    private static async Task<(string Version, string Executable)> RunShortcutAndReadResolutionAsync(
        string shortcutPath,
        string resolutionOutput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var previousLines = File.Exists(resolutionOutput) ? File.ReadAllLines(resolutionOutput).Length : 0;
        _ = Process.Start(new ProcessStartInfo(shortcutPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(shortcutPath)!
        }) ?? throw new InvalidOperationException($"No se pudo abrir {Path.GetFileName(shortcutPath)}.");

        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(resolutionOutput))
            {
                var lines = File.ReadAllLines(resolutionOutput);
                if (lines.Length > previousLines)
                {
                    var fields = lines[^1].Split('|');
                    Require(fields.Length == 3, "La evidencia del launcher tiene formato invalido.");
                    if (string.Equals(fields[1], "ERROR", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Launcher reporto: {fields[2]}");
                    }
                    return (UpdateVersion.Parse(fields[1]).ToString(), Path.GetFullPath(fields[2]));
                }
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("El acceso directo no produjo evidencia del launcher dentro del tiempo esperado.");
    }

    private static async Task<int> RunLauncherDiagnosticAsync(
        string launcherPath,
        string applicationDataRoot,
        string desktopDirectory,
        string programsDirectory,
        string resolutionOutput,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(launcherPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!
        };
        foreach (var argument in new[]
        {
            "--app-data-root", applicationDataRoot,
            "--desktop-directory", desktopDirectory,
            "--programs-directory", programsDirectory,
            "--resolve-output", resolutionOutput,
            "--no-launch",
            "--headless"
        })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar el launcher para current invalido.");
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(1), cancellationToken);
        return process.ExitCode;
    }

    private static async Task CreateUpdatePackageAsync(
        string publishDirectory,
        string packagePath,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var source in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(publishDirectory, source).Replace('\\', '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            await using var destination = entry.Open();
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(destination, cancellationToken);
        }
    }

    private static string ResolveSourceExecutable(string? repositoryRoot, string sourceVersion, string smokeRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new InvalidOperationException("El smoke de despliegue requiere el repositorio para localizar la version base.");
        }

        var repo = Path.GetFullPath(repositoryRoot);
        var published = Path.Combine(
            repo,
            "artifacts",
            $"ArteInventariado-{sourceVersion}-win-x64",
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (File.Exists(published))
        {
            Require(ReadProductVersion(published) == sourceVersion, "El ejecutable base no coincide con sourceVersion.");
            return published;
        }

        var archivePath = Path.Combine(repo, "artifacts", $"ArteInventariado-{sourceVersion}-win-x64.zip");
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"No existe el publish ni el ZIP base {sourceVersion}.", archivePath);
        }

        var extractionDirectory = Path.Combine(smokeRoot, "source-version");
        Directory.CreateDirectory(extractionDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.SingleOrDefault(item =>
            string.Equals(
                item.FullName.Replace('\\', '/'),
                UpdateLaunchTargetResolver.ApplicationExecutableName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("El ZIP base no contiene ArteInventariado.WinForms.exe en su raiz.");
        var extracted = Path.Combine(extractionDirectory, UpdateLaunchTargetResolver.ApplicationExecutableName);
        entry.ExtractToFile(extracted, overwrite: false);
        Require(ReadProductVersion(extracted) == sourceVersion, "El ejecutable extraido no coincide con sourceVersion.");
        return extracted;
    }

    private static async Task<string> CreateUpdateManifestAsync(
        string packagePath,
        string sourceVersion,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(
            Path.GetDirectoryName(packagePath)!,
            $"{Path.GetFileNameWithoutExtension(packagePath)}.manifest.json");
        var manifest = new UpdateManifest(
            targetVersion,
            sourceVersion,
            null,
            Path.GetFileName(packagePath),
            await UpdatePackageValidator.ComputeSha256Async(packagePath, cancellationToken),
            new FileInfo(packagePath).Length,
            "Smoke local del actualizador");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            cancellationToken);
        return manifestPath;
    }

    private static string ReadProductVersion(string executable)
    {
        var value = FileVersionInfo.GetVersionInfo(executable).ProductVersion;
        return UpdateVersion.Parse(value ?? throw new InvalidDataException("El ejecutable no declara ProductVersion.")).ToString();
    }

    private static string PreviousVersion(string version)
    {
        var parsed = UpdateVersion.Parse(version);
        if (!string.IsNullOrWhiteSpace(parsed.PreRelease) &&
            parsed.PreRelease.StartsWith("rc", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parsed.PreRelease[2..], out var releaseCandidate) &&
            releaseCandidate > 1)
        {
            return new UpdateVersion(parsed.Major, parsed.Minor, parsed.Patch, $"rc{releaseCandidate - 1}").ToString();
        }

        return parsed.Patch > 0
            ? new UpdateVersion(parsed.Major, parsed.Minor, parsed.Patch - 1).ToString()
            : parsed.Minor > 0
                ? new UpdateVersion(parsed.Major, parsed.Minor - 1, 0).ToString()
                : new UpdateVersion(Math.Max(0, parsed.Major - 1), 0, 0).ToString();
    }

    private static async Task AddFutureMigrationAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99999999999999_FutureSmoke', '10.0.0');";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadMigrationsAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY rowid;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Se esperaba {typeof(TException).Name}.");
    }

    private static void Append(string path, string line) =>
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} | {line}{Environment.NewLine}");

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class NeverCalledVerifier : IUpdateApplicationVerifier
    {
        public Task<bool> VerifyAsync(string executablePath, UpdateRuntimePaths paths, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("El verificador no debe ejecutarse durante recovery.");
    }

    private sealed class MutatingFailureVerifier(SmokeFixture fixture) : IUpdateApplicationVerifier
    {
        public async Task<bool> VerifyAsync(
            string executablePath,
            UpdateRuntimePaths paths,
            CancellationToken cancellationToken = default)
        {
            await fixture.MutateAsync(cancellationToken);
            return false;
        }
    }

    private sealed class SmokeFixture : IAsyncDisposable
    {
        public const string OriginalAssetContent = "asset-pre-update";
        public UpdateRuntimePaths Paths { get; }
        public UpdateStateStore StateStore { get; }
        public UpdatePointerStore PointerStore { get; }
        public InstallationMode Mode { get; }
        public Guid InstallationId { get; }
        public string PreUpdatePath { get; }
        public string AssetPath { get; }
        public string SourceExecutable => _sourceExecutable;
        private readonly string _sourceExecutable;
        private readonly UpdateStateDocument _readyTemplate;

        private SmokeFixture(
            UpdateRuntimePaths paths,
            UpdateStateStore stateStore,
            UpdatePointerStore pointerStore,
            InstallationMode mode,
            Guid installationId,
            string preUpdatePath,
            string managedAssetPath,
            string sourceExecutable,
            UpdateStateDocument readyTemplate)
        {
            Paths = paths;
            StateStore = stateStore;
            PointerStore = pointerStore;
            Mode = mode;
            InstallationId = installationId;
            PreUpdatePath = preUpdatePath;
            AssetPath = managedAssetPath;
            _sourceExecutable = sourceExecutable;
            _readyTemplate = readyTemplate;
        }

        public static async Task<SmokeFixture> CreateAsync(
            string applicationRoot,
            InstallationMode mode,
            string sourceExecutable,
            string manifestPath,
            string packagePath,
            string sourceVersion,
            string targetVersion,
            CancellationToken cancellationToken)
        {
            var paths = UpdateRuntimePaths.Create(applicationRoot);
            var managedSourceDirectory = paths.GetVersionDirectory(sourceVersion);
            Directory.CreateDirectory(managedSourceDirectory);
            var managedSourceExecutable = Path.Combine(
                managedSourceDirectory,
                UpdateLaunchTargetResolver.ApplicationExecutableName);
            File.Copy(sourceExecutable, managedSourceExecutable, overwrite: false);
            var pointerStore = new UpdatePointerStore(paths.CurrentPointerPath);
            pointerStore.Save(sourceVersion, managedSourceExecutable, null, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
            var stationStore = new LocalStationConfigurationStore(paths.StationSettingsPath);
            stationStore.SaveConfigured(mode);
            var installationId = stationStore.Load().Status.InstallationId!.Value;
            await CreateMigratedDatabaseAsync(paths.DatabasePath, cancellationToken);
            var assetBytes = Encoding.UTF8.GetBytes(OriginalAssetContent);
            var assetHash = Convert.ToHexString(SHA256.HashData(assetBytes));
            var assetPath = Path.Combine(paths.BusinessAssetsDirectory, $"{assetHash.ToLowerInvariant()}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            await File.WriteAllBytesAsync(assetPath, assetBytes, cancellationToken);
            var preUpdate = await CreatePreUpdateAsync(
                paths,
                installationId,
                mode,
                assetPath,
                sourceVersion,
                cancellationToken);
            var guard = new UpdateStationGuard();
            var stationSnapshot = await guard.CaptureAsync(
                paths.StationSettingsPath,
                paths.Root,
                targetVersion,
                cancellationToken);
            var stateStore = new UpdateStateStore(paths.StatePath);
            var readyState = new UpdateStateDocument(
                UpdateStateStore.CurrentSchemaVersion,
                UpdateState.ReadyToInstall,
                sourceVersion,
                targetVersion,
                packagePath,
                manifestPath,
                preUpdate,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                managedSourceExecutable,
                installationId,
                mode,
                stationSnapshot.Sha256,
                false,
                stationSnapshot.BackupPath);
            stateStore.Save(readyState);
            return new SmokeFixture(
                paths,
                stateStore,
                pointerStore,
                mode,
                installationId,
                preUpdate,
                assetPath,
                managedSourceExecutable,
                readyState);
        }

        public UpdateInstaller CreateInstaller(
            IUpdateApplicationVerifier verifier,
            IStableDeploymentFinalizer? stableDeploymentFinalizer = null) =>
            new(
                new UpdatePackageValidator(),
                StateStore,
                new UpdateBackupService(),
                Paths,
                verifier,
                PointerStore,
                new UpdateStationGuard(),
                new UpdateEventLog(Paths.LogDirectory),
                stableDeploymentFinalizer: stableDeploymentFinalizer);

        public async Task PrepareForReinstallAsync(CancellationToken cancellationToken)
        {
            var stationSnapshot = await new UpdateStationGuard().CaptureAsync(
                Paths.StationSettingsPath,
                Paths.Root,
                _readyTemplate.TargetVersion,
                cancellationToken);
            StateStore.Save(_readyTemplate with
            {
                State = UpdateState.ReadyToInstall,
                PreviousVersionPath = null,
                CurrentVersionPath = null,
                LastError = null,
                DatabaseMayHaveChanged = false,
                StationSettingsSha256 = stationSnapshot.Sha256,
                StationSettingsBackupPath = stationSnapshot.BackupPath,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        public async Task MutateBusinessAsync(CancellationToken cancellationToken)
        {
            await using (var connection = new SqliteConnection($"Data Source={Paths.DatabasePath};Pooling=False"))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS update_smoke_mutation (id INTEGER PRIMARY KEY); INSERT INTO update_smoke_mutation VALUES (1);";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await File.WriteAllTextAsync(AssetPath, "asset-mutado", cancellationToken);
        }

        public async Task MutateAsync(CancellationToken cancellationToken)
        {
            await MutateBusinessAsync(cancellationToken);
            await File.WriteAllTextAsync(Paths.StationSettingsPath, "{\"installationId\":\"00000000-0000-0000-0000-000000000001\"}", cancellationToken);
        }

        public void EnsureStationIdentity()
        {
            var status = new LocalStationConfigurationStore(Paths.StationSettingsPath).Load().Status;
            Require(status.IsConfigured, "station-settings dejo de ser valido.");
            Require(status.InstallationId == InstallationId, "installationId no fue preservado.");
            Require(status.Mode == Mode, "stationMode no fue preservado.");
        }

        public async Task<string> ComputeDatabaseFingerprintAsync(CancellationToken cancellationToken)
        {
            var buffer = new StringBuilder();
            await using var connection = new SqliteConnection($"Data Source={Paths.DatabasePath};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(cancellationToken);
            var tables = new List<string>();
            await using (var tableCommand = connection.CreateCommand())
            {
                tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                await using var tableReader = await tableCommand.ExecuteReaderAsync(cancellationToken);
                while (await tableReader.ReadAsync(cancellationToken))
                {
                    tables.Add(tableReader.GetString(0));
                }
            }

            foreach (var table in tables)
            {
                buffer.Append("TABLE:").Append(table).AppendLine();
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY rowid;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        var value = reader.GetValue(index);
                        buffer.Append(value switch
                        {
                            DBNull => "<null>",
                            byte[] bytes => Convert.ToHexString(bytes),
                            IFormattable formatted => formatted.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                            _ => value.ToString()
                        }).Append('|');
                    }

                    buffer.AppendLine();
                }
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString())));
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            TryDeleteDirectory(Paths.ApplicationDataRoot);
            return ValueTask.CompletedTask;
        }

        private static async Task CreateMigratedDatabaseAsync(string path, CancellationToken cancellationToken)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False")
                .Options;
            await using var context = new AppDbContext(options);
            await DatabaseInitializer.InicializarAsync(context);
        }

        private static async Task<string> CreatePreUpdateAsync(
            UpdateRuntimePaths paths,
            Guid installationId,
            InstallationMode mode,
            string assetPath,
            string sourceVersion,
            CancellationToken cancellationToken)
        {
            var package = Path.Combine(paths.Root, $"PRE_UPDATE_{sourceVersion}_{Guid.NewGuid():N}.aibak");
            Directory.CreateDirectory(paths.Root);
            var assetBytes = await File.ReadAllBytesAsync(assetPath, cancellationToken);
            var assetHash = Convert.ToHexString(SHA256.HashData(assetBytes));
            var assets = new[]
            {
                new BackupAssetManifest(
                    0,
                    $"business-assets/products/{assetHash.ToLowerInvariant()}.txt",
                    "original.txt",
                    assetBytes.Length,
                    assetHash,
                    false)
            };
            var migrations = await ReadMigrationsAsync(paths.DatabasePath, cancellationToken);
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.Now,
                sourceVersion,
                "Microsoft.EntityFrameworkCore.Sqlite",
                migrations,
                migrations.LastOrDefault(),
                await UpdatePackageValidator.ComputeSha256Async(paths.DatabasePath, cancellationToken),
                new FileInfo(paths.DatabasePath).Length,
                installationId,
                mode,
                BackupKind.PreUpdate,
                assets);

            using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
            var manifestEntry = archive.CreateEntry(BackupPackageEngine.ManifestEntryName);
            await using (var stream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(stream, manifest, BackupPackageEngine.JsonOptions, cancellationToken);
            }

            var databaseEntry = archive.CreateEntry(BackupPackageEngine.DatabaseEntryName, CompressionLevel.NoCompression);
            await using (var destination = databaseEntry.Open())
            await using (var source = File.OpenRead(paths.DatabasePath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            await WriteEntryAsync(archive, assets[0].PackagePath, assetBytes);
            return package;
        }
    }
}
