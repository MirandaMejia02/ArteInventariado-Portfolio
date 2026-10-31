using System.Diagnostics;
using System.Globalization;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("El actualizador solo puede ejecutarse en Windows.");
    return 10;
}

UpdateRuntimePaths? diagnosticPaths = null;
var diagnosticStage = "entrada";
try
{
    if (HasFlag(args, "--machine-entrypoint-bootstrap"))
    {
        return await RunMachineEntrypointBootstrapAsync(args);
    }

    if (HasFlag(args, "--machine-bootstrap"))
    {
        return await RunMachineBootstrapAsync(args);
    }

    if (HasFlag(args, "--smoke"))
    {
        var publishDirectory = GetArgument(args, "--publish-directory") ?? AppContext.BaseDirectory;
        var outputPath = GetArgument(args, "--output")
            ?? Path.Combine(Path.GetTempPath(), $"ArteInventariado-updater-smoke-{Guid.NewGuid():N}.log");
        return await UpdaterSmokeHarness.RunAsync(
            publishDirectory,
            outputPath,
            GetArgument(args, "--repo-root"));
    }

    if (HasFlag(args, "--machine-update-smoke"))
    {
        return MachineWideUpdaterSmokeHarness.Run(GetArgument(args, "--output"));
    }

    if (HasFlag(args, "--station-hardening-smoke"))
    {
        return MachineWideStationHardeningSmokeHarness.Run();
    }

    diagnosticStage = "resolucion-rutas";
    var paths = UpdateRuntimePaths.CreateFromArguments(args);
    diagnosticPaths = paths;
    var updaterArguments = args;
    MachineWideUpdateDiagnostics.Write(
        paths,
        diagnosticStage,
        executablePath: Environment.ProcessPath,
        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var initialElevation)
            ? initialElevation
            : null);
    var statePath = GetArgument(args, "--state");
    if (!string.IsNullOrWhiteSpace(statePath))
    {
        paths = paths with { StatePath = Path.GetFullPath(statePath) };
    }
    // Keep the final, argument-resolved layout for failure diagnostics.  A
    // --state override is validated below and must be the path reported if
    // that validation fails.
    diagnosticPaths = paths;

    diagnosticStage = "validacion-ejecutable";
    // The elevated process must validate the complete command line again;
    // launchers and shortcuts are not trusted merely because they are local.
    MachineWideUpdateGuard.ValidateUpdaterExecutablePath(
        paths,
        Environment.ProcessPath ?? AppContext.BaseDirectory);
    diagnosticStage = "validacion-argumentos";
    var hadExplicitMachineWideRoots = MachineWideUpdateGuard.HasMachineWideLayout(updaterArguments);
    updaterArguments = MachineWideUpdateGuard.EnsureStableUpdaterLayoutArguments(
        updaterArguments,
        paths,
        Environment.ProcessPath ?? AppContext.BaseDirectory);
    if (!hadExplicitMachineWideRoots && MachineWideUpdateGuard.HasMachineWideLayout(updaterArguments))
    {
        MachineWideUpdateDiagnostics.Write(
            paths,
            "raices-estables-inferidas",
            executablePath: Environment.ProcessPath,
            tokenElevated: MachineWideElevation.TryGetTokenElevation(out var inferredElevation)
                ? inferredElevation
                : null,
            relatedPath: paths.MachineInstallRoot);
    }

    MachineWideUpdateGuard.ValidateUpdaterArguments(updaterArguments, paths);
    if (paths.PathProvider is { IsMachineWide: true } initialMachineWideProvider)
    {
        MachineWideDatabaseResetJournalStore.EnsureUpdaterMayProceed(initialMachineWideProvider);
    }
    MachineWideUpdateDiagnostics.Write(
        paths,
        diagnosticStage,
        executablePath: Environment.ProcessPath,
        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var validatedElevation)
            ? validatedElevation
            : null,
        relatedPath: statePath ?? paths.StatePath);
    diagnosticStage = "elevacion";
    MachineWideUpdateGuard.EnsureElevated(paths);

    diagnosticStage = "espera-aplicacion";
    await WaitForApplicationExitAsync(
        GetArgument(updaterArguments, "--wait-pid"),
        GetArgument(updaterArguments, "--wait-executable"),
        paths);

    using var maintenanceGate = paths.PathProvider is { IsMachineWide: true } machineWideProvider
        ? MachineWideMaintenanceLease.Acquire(machineWideProvider)
        : null;
    if (paths.PathProvider?.IsMachineWide == true)
    {
        // Recheck after waiting for the UI: a protected DB reset may have
        // started after the updater's first argument validation.
        MachineWideUpdateGuard.ValidateUpdaterArguments(updaterArguments, paths);
        MachineWideDatabaseResetJournalStore.EnsureUpdaterMayProceed(paths.PathProvider!);
    }

    diagnosticStage = "handoff-pre-update";
    var handoffPath = GetArgument(updaterArguments, "--handoff");
    if (paths.PathProvider?.IsMachineWide == true && !string.IsNullOrWhiteSpace(handoffPath))
    {
        await new MachineWideUpdateHandoffService().PrepareAsync(paths, handoffPath);
    }
    else if (paths.PathProvider?.IsMachineWide != true && !string.IsNullOrWhiteSpace(handoffPath))
    {
        throw new InvalidDataException("El handoff de staging solo está disponible para el layout machine-wide.");
    }

    if (HasFlag(updaterArguments, "--handoff-only"))
    {
        if (paths.PathProvider?.IsMachineWide != true || string.IsNullOrWhiteSpace(handoffPath))
        {
            throw new InvalidDataException(
                "--handoff-only requiere el layout machine-wide y un staging de handoff.");
        }

        MachineWideUpdateDiagnostics.Write(
            paths,
            "handoff-completado-sin-instalar",
            executablePath: Environment.ProcessPath,
            tokenElevated: MachineWideElevation.TryGetTokenElevation(out var handoffElevation)
                ? handoffElevation
                : null,
            relatedPath: paths.StatePath,
            canonicalState: new UpdateStateStore(paths.StatePath).Load()?.State.ToString());
        return 0;
    }

    diagnosticStage = "instalacion";
    var installer = new UpdateInstaller(
        new UpdatePackageValidator(),
        new UpdateStateStore(paths.StatePath),
        new UpdateBackupService(),
        paths,
        stableDeploymentFinalizer: new StableDeploymentFinalizer(
            new StableEntryPointInstaller(paths, new WindowsShortcutManager())));
    var result = HasFlag(updaterArguments, "--rollback")
        ? await installer.RollbackSuccessfulAsync()
        : HasFlag(updaterArguments, "--recover")
            ? await installer.RecoverInterruptedAsync()
            : await installer.InstallFromStateAsync();
    if (result is 0 or 2 &&
        (paths.PathProvider?.IsMachineWide == true
            ? HasFlag(updaterArguments, "--launch-stable")
            : !HasFlag(updaterArguments, "--no-relaunch")))
    {
        diagnosticStage = "relanzamiento-estable";
        LaunchCurrentVersion(paths);
    }

    MachineWideUpdateDiagnostics.Write(
        paths,
        "completado",
        executablePath: Environment.ProcessPath,
        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var completedElevation)
            ? completedElevation
            : null);
    return result;
}
catch (Exception ex)
{
    MachineWideUpdateDiagnostics.Write(
        diagnosticPaths,
        diagnosticStage,
        ex,
        executablePath: Environment.ProcessPath,
        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var failedElevation)
            ? failedElevation
            : null,
        relatedPath: diagnosticPaths?.StatePath);
    Console.Error.WriteLine(
        $"Actualizacion fallida en etapa '{diagnosticStage}': " +
        MachineWideUpdateDiagnostics.Describe(diagnosticStage, ex, diagnosticPaths?.StatePath));
    return 1;
}

static async Task WaitForApplicationExitAsync(
    string? processIdText,
    string? expectedExecutablePath,
    UpdateRuntimePaths paths)
{
    if (string.IsNullOrWhiteSpace(processIdText))
    {
        return;
    }

    if (!int.TryParse(processIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId <= 0)
    {
        throw new ArgumentException("--wait-pid no contiene un identificador valido.");
    }

    await MachineWideProcessWait.WaitForProcessIdExitAsync(
        processId,
        paths,
        TimeSpan.FromMinutes(2),
        expectedExecutablePath);
}

static string? GetArgument(string[] arguments, string name)
    => MachineWideBootstrapArgumentReader.GetPathValue(arguments, name);

static bool HasFlag(string[] arguments, string name) =>
    arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

static void LaunchCurrentVersion(UpdateRuntimePaths paths)
{
    if (paths.PathProvider?.IsMachineWide == true)
    {
        var launcher = paths.StableLauncherPath;
        if (!File.Exists(launcher))
        {
            throw new FileNotFoundException(
                "No se encontró el launcher estable para relanzar la estación.",
                launcher);
        }

        // Explorer is the unelevated shell broker.  The updater may be
        // elevated, but the normal launcher/runtime must return to the user's
        // medium-integrity token after the protected operation completes.
        var launcherStartInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcher)!,
            Arguments = $"\"{launcher.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
        };
        _ = Process.Start(launcherStartInfo)
            ?? throw new InvalidOperationException("No se pudo iniciar el launcher estable mediante Explorer.");
        return;
    }

    var executable = new UpdateLaunchTargetResolver(paths).ResolveCurrentOrNull();
    if (executable is null)
    {
        return;
    }

    var startInfo = new ProcessStartInfo(executable)
    {
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(executable)!
    };
    paths.AddLayoutArguments(startInfo);
    _ = Process.Start(startInfo);
}

static async Task<int> RunMachineBootstrapAsync(string[] arguments)
{
    MachineWideBootstrapLogWriter? log = null;
    try
    {
        var elevation = MachineWideElevation.EnsureElevated(
            Environment.ProcessPath ?? throw new InvalidOperationException("No se pudo resolver el ejecutable del updater."),
            arguments);
        if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
        {
            Console.Error.WriteLine("Instalacion machine-wide cancelada por el usuario; no se modificaron rutas.");
            return 2;
        }

        if (elevation.Outcome == MachineWideElevationOutcome.RelaunchedElevated)
        {
            return 0;
        }

        var parsed = UpdateRuntimePaths.CreateFromArguments(arguments);
        var target = parsed.PathProvider?.IsMachineWide == true
            ? parsed.PathProvider
            : ArteInventariadoPathProvider.CreateMachineWideDefault();
        var runId = Guid.NewGuid();
        log = new MachineWideBootstrapLogWriter(target, runId);
        log.Started();
        var shortcutLocations = CreateShortcutLocations(arguments);
        var service = new MachineWideInstallationService(
            new MachineWideBootstrapService(log.Phase, log),
            new MachineWideAclService(),
            log.Phase,
            runId);
        var legacyRoot = GetArgument(arguments, "--legacy-root");
        MachineWideInstallationResult result;
        if (!string.IsNullOrWhiteSpace(legacyRoot))
        {
            result = await service.MigrateLegacyAsync(
                ArteInventariadoPathProvider.CreateLegacy(legacyRoot),
                target,
                shortcutLocations: shortcutLocations,
                publishedPayloadDirectory: GetArgument(arguments, "--install-source"));
        }
        else
        {
            var payload = GetArgument(arguments, "--install-source");
            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new ArgumentException(
                    "El bootstrap limpio requiere --install-source; para migrar use --legacy-root.");
            }

            result = await service.InstallCleanAsync(target, payload, shortcutLocations);
        }

        Console.WriteLine(
            $"Machine-wide listo: {result.Bootstrap.Outcome}; " +
            $"launcher={result.LauncherPath ?? "no instalado"}; " +
            $"shortcuts={(result.ShortcutsEnsured ? "ok" : "omitidos")}");
        log.Succeeded();
        log.Exit(0);
        return 0;
    }
    catch (Exception ex)
    {
        log?.Failed(ex);
        log?.Exit(1);
        Console.Error.WriteLine($"Bootstrap machine-wide fallido: {ex.Message}");
        return 1;
    }
}

static async Task<int> RunMachineEntrypointBootstrapAsync(string[] arguments)
{
    UpdateRuntimePaths? paths = null;
    var stage = "bootstrap-entrypoints-argumentos";
    try
    {
        var parsed = MachineWideEntrypointBootstrapArguments.Parse(arguments);
        paths = UpdateRuntimePaths.CreateMachineWide(
            parsed.MachineInstallRoot,
            parsed.MachineDataRoot,
            parsed.UserDataRoot);

        stage = "bootstrap-entrypoints-uac";
        var elevation = MachineWideElevation.EnsureElevated(
            Environment.ProcessPath ?? throw new InvalidOperationException(
                "No se pudo resolver el ejecutable del updater."),
            arguments);
        if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
        {
            MachineWideUpdateDiagnostics.Write(
                paths,
                stage,
                new UnauthorizedAccessException("El usuario canceló la autorización UAC."),
                executablePath: Environment.ProcessPath,
                tokenElevated: false,
                relatedPath: parsed.PackagePath);
            Console.Error.WriteLine(
                "Bootstrap de entrypoints cancelado por el usuario; no se modificaron rutas.");
            return 2;
        }

        if (elevation.Outcome == MachineWideElevationOutcome.RelaunchedElevated)
        {
            return 0;
        }

        stage = "bootstrap-entrypoints-ejecutar";
        var result = await new MachineWideEntrypointBootstrapService().BootstrapAsync(
            paths,
            parsed.ManifestPath,
            parsed.PackagePath);
        Console.WriteLine(
            $"Entrypoints corregidos: {result.SourceVersion} -> {result.TargetVersion}; " +
            $"launcher={result.LauncherSha256}; updater={result.UpdaterSha256}");
        return 0;
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
            relatedPath: paths?.StableUpdaterPath);
        Console.Error.WriteLine(
            $"Bootstrap de entrypoints fallido en etapa '{stage}': " +
            MachineWideUpdateDiagnostics.Describe(stage, ex, paths?.StableUpdaterPath));
        return 1;
    }
}

static ShortcutLocations? CreateShortcutLocations(string[] arguments)
{
    var desktop = GetArgument(arguments, "--desktop-directory");
    var programs = GetArgument(arguments, "--programs-directory");
    if (string.IsNullOrWhiteSpace(desktop) && string.IsNullOrWhiteSpace(programs))
    {
        return null;
    }

    if (string.IsNullOrWhiteSpace(desktop) || string.IsNullOrWhiteSpace(programs))
    {
        throw new ArgumentException("Las rutas de Escritorio y Menu Inicio deben especificarse juntas.");
    }

    return new ShortcutLocations(Path.GetFullPath(desktop), Path.GetFullPath(programs));
}
