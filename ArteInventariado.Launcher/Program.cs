using System.Diagnostics;
using System.Globalization;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

if (!OperatingSystem.IsWindows())
{
    return 10;
}

UpdateRuntimePaths? diagnosticPaths = null;
var diagnosticStage = "entrada";
try
{
    diagnosticStage = "resolucion-rutas";
    var paths = UpdateRuntimePaths.CreateFromArguments(args);
    diagnosticPaths = paths;
    MachineWideUpdateDiagnostics.Write(
        paths,
        diagnosticStage,
        executablePath: Environment.ProcessPath,
        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var initialElevation)
            ? initialElevation
            : null);
    diagnosticStage = "espera-aplicacion";
    await WaitForProcessExitAsync(GetArgument(args, "--wait-pid"), paths);
    diagnosticStage = "bootstrap-estable";
    var shortcutLocations = CreateShortcutLocations(args);
    var bootstrap = new StableDeploymentBootstrap(paths, shortcutLocations);
    var processPath = Path.GetFullPath(Environment.ProcessPath ?? string.Empty);
    var stablePath = Path.GetFullPath(paths.StableLauncherPath);

    if (!string.Equals(processPath, stablePath, StringComparison.OrdinalIgnoreCase))
    {
        var installSource = GetArgument(args, "--install-source") ?? AppContext.BaseDirectory;
        var installed = bootstrap.EnsureInstalled(installSource, AppContext.BaseDirectory);
        if (HasFlag(args, "--no-launch"))
        {
            return 0;
        }

        var launcherStartInfo = new ProcessStartInfo(installed.LauncherPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installed.LauncherPath)!
        };
        paths.AddLayoutArguments(launcherStartInfo);
        _ = Process.Start(launcherStartInfo);
        return 0;
    }

    var verificationMode = Environment.GetEnvironmentVariable("ARTE_UPDATE_VERIFY") == "1";
    if (!verificationMode && !HasFlag(args, "--no-launch"))
    {
        bootstrap.EnsureStableEntryPoints(AppContext.BaseDirectory);
    }
    var recoveryAlreadyAttempted = Environment.GetEnvironmentVariable("ARTE_LAUNCHER_RECOVERY_ATTEMPT") == "1";
    diagnosticStage = "resolucion-current";
    var executable = new StableLauncherCoordinator(paths).ResolveCurrentWithRecovery(recoveryAlreadyAttempted);
    WriteResolutionIfRequested(args, executable);
    if (HasFlag(args, "--no-launch"))
    {
        return 0;
    }

    var startInfo = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(executable)!
    };
    startInfo.Environment["ARTE_STABLE_LAUNCHED"] = "1";
    paths.AddLayoutArguments(startInfo);
    diagnosticStage = "lanzamiento-aplicacion";
    _ = Process.Start(startInfo)
        ?? throw new InvalidOperationException("No se pudo iniciar ArteInventariado.");
    MachineWideUpdateDiagnostics.Write(
        paths,
        "completado",
        executablePath: Environment.ProcessPath,
        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var completedElevation)
            ? completedElevation
            : null,
        relatedPath: executable);
    return 0;
}
catch (Exception ex)
{
    var waitPid = GetArgument(args, "--wait-pid");
    MachineWideUpdateDiagnostics.Write(
        diagnosticPaths,
        diagnosticStage,
        ex,
        executablePath: Environment.ProcessPath,
        processId: int.TryParse(waitPid, NumberStyles.None, CultureInfo.InvariantCulture, out var waitedPid)
            ? waitedPid
            : null,
        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var failedElevation)
            ? failedElevation
            : null,
        relatedPath: diagnosticStage == "espera-aplicacion"
            ? diagnosticPaths?.PathProvider?.IsMachineWide == true
                ? diagnosticPaths.StableUpdaterPath
                : null
            : diagnosticPaths?.CurrentPointerPath);
    WriteFailureIfRequested(args, ex);
    var detail = MachineWideUpdateDiagnostics.Describe(
        diagnosticStage,
        ex,
        diagnosticStage == "espera-aplicacion"
            ? diagnosticPaths?.PathProvider?.IsMachineWide == true
                ? diagnosticPaths.StableUpdaterPath
                : null
            : diagnosticPaths?.CurrentPointerPath);
    if (HasFlag(args, "--headless"))
    {
        Console.Error.WriteLine($"ArteInventariado no pudo iniciar de forma segura: {detail}");
        return 1;
    }

    MessageBox.Show(
        $"ArteInventariado no pudo iniciar de forma segura.\n\n{detail}\n\n" +
        "Abra Configuracion > Actualizaciones o contacte al administrador del sistema.",
        "ArteInventariado",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return 1;
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

static void WriteResolutionIfRequested(string[] arguments, string executable)
{
    var output = GetArgument(arguments, "--resolve-output");
    if (string.IsNullOrWhiteSpace(output)) return;
    var path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "desconocida";
    File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O}|{version}|{Path.GetFullPath(executable)}{Environment.NewLine}");
}

static void WriteFailureIfRequested(string[] arguments, Exception exception)
{
    var output = GetArgument(arguments, "--resolve-output");
    if (string.IsNullOrWhiteSpace(output)) return;
    try
    {
        var path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var message = exception.Message.Replace('|', '/').ReplaceLineEndings(" ");
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O}|ERROR|{message}{Environment.NewLine}");
    }
    catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException or ArgumentException)
    {
    }
}

static string? GetArgument(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase)) return arguments[index + 1];
    }

    return null;
}

static bool HasFlag(string[] arguments, string name) =>
    arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

static async Task WaitForProcessExitAsync(string? processIdText, UpdateRuntimePaths paths)
{
    if (string.IsNullOrWhiteSpace(processIdText))
    {
        return;
    }

    if (!int.TryParse(
        processIdText,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var processId) ||
        processId <= 0 ||
        processId == Environment.ProcessId)
    {
        throw new ArgumentException("--wait-pid no contiene un identificador valido.");
    }

    try
    {
        using var process = Process.GetProcessById(processId);
        await MachineWideProcessWait.WaitForExitAsync(
            process,
            paths,
            TimeSpan.FromMinutes(2));
    }
    catch (ArgumentException)
    {
        // The updater may have completed between Process.GetProcessById and
        // the wait; in that case the Launcher can safely continue.
    }
}
