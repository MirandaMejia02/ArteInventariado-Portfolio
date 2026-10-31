using System.Diagnostics;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Updater;

public enum UpdateLaunchOutcome
{
    Started = 1,
    Cancelled = 2
}

public sealed record UpdateLaunchResult(
    UpdateLaunchOutcome Outcome,
    Process? Process = null);

public static class UpdateLauncher
{
    public static bool TryLaunchRecoveryIfRequired(string[]? arguments = null)
    {
        if (IsVerificationOrRedirected())
        {
            return false;
        }

        var paths = ResolvePaths(arguments);
        var state = new UpdateStateStore(paths.StatePath).Load();
        UpdateVersionPointer? current = null;
        try
        {
            current = new UpdatePointerStore(paths.CurrentPointerPath).Load();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // Recovery remains fail-closed when CURRENT cannot be read.
        }

        if (!UpdateRecoveryPolicy.RequiresRecovery(state, current))
        {
            return false;
        }

        var currentDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var updater = File.Exists(paths.StableUpdaterPath)
            ? paths.StableUpdaterPath
            : Path.Combine(currentDirectory, "ArteInventariado.Updater.exe");
        if (!File.Exists(updater))
        {
            throw new FileNotFoundException(
                "Existe una actualizacion incompleta, pero no se encontro ArteInventariado.Updater.exe.",
                updater);
        }

        var result = LaunchUpdater(
            paths,
            updater,
            [
                "--recover",
                "--wait-pid",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--wait-executable",
                Environment.ProcessPath ?? string.Empty,
                "--state",
                paths.StatePath
            ]);
        return result.Outcome == UpdateLaunchOutcome.Started;
    }

    /// <summary>
    /// Starts an updater operation using the correct privilege boundary for
    /// the selected layout. Machine-wide launches cross UAC with runas and
    /// never fall back to a non-elevated process when UAC is cancelled.
    /// Legacy launches retain the v1.11 non-elevated process model.
    /// </summary>
    public static UpdateLaunchResult LaunchUpdater(
        UpdateRuntimePaths paths,
        string updaterPath,
        IReadOnlyList<string> operationArguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(updaterPath);
        ArgumentNullException.ThrowIfNull(operationArguments);

        var startInfo = CreateUpdaterStartInfo(paths, updaterPath, operationArguments, environment);
        var machineWide = paths.PathProvider?.IsMachineWide == true;
        if (machineWide)
        {
            MachineWideUpdateGuard.ValidateUpdaterArguments(startInfo.ArgumentList, paths);
        }

        if (machineWide && !MachineWideElevation.IsAdministrator())
        {
            var elevation = MachineWideElevation.EnsureElevated(
                startInfo.FileName,
                startInfo.ArgumentList.ToArray());
            return elevation.Outcome switch
            {
                MachineWideElevationOutcome.Cancelled =>
                    new UpdateLaunchResult(UpdateLaunchOutcome.Cancelled),
                MachineWideElevationOutcome.RelaunchedElevated =>
                    new UpdateLaunchResult(UpdateLaunchOutcome.Started, elevation.Process),
                MachineWideElevationOutcome.AlreadyElevated =>
                    StartDirect(startInfo, machineWide),
                _ => throw new InvalidOperationException("Resultado de elevación desconocido.")
            };
        }

        return StartDirect(startInfo, machineWide);
    }

    /// <summary>
    /// Builds an updater command without concatenating a raw command line.
    /// The returned machine-wide start info is the exact shape used for the
    /// UAC boundary (UseShellExecute=true, Verb=runas).  ShellExecute does
    /// not support per-process environment overrides; machine-wide callers
    /// must transfer non-sensitive continuation state through validated
    /// arguments instead.
    /// </summary>
    public static ProcessStartInfo CreateUpdaterStartInfo(
        UpdateRuntimePaths paths,
        string updaterPath,
        IReadOnlyList<string> operationArguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(updaterPath);
        ArgumentNullException.ThrowIfNull(operationArguments);

        var fullUpdater = Path.GetFullPath(updaterPath);
        var expectedUpdater = Path.GetFullPath(paths.StableUpdaterPath);
        var machineWide = paths.PathProvider?.IsMachineWide == true;
        if (machineWide && !string.Equals(fullUpdater, expectedUpdater, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "El updater solicitado no coincide con el ejecutable estable del layout.");
        }

        MachineWideUpdateGuard.ValidateUpdaterExecutablePath(paths, fullUpdater);

        if (!File.Exists(fullUpdater))
        {
            throw new FileNotFoundException("No se encontró el updater estable.", fullUpdater);
        }

        var startInfo = new ProcessStartInfo(fullUpdater)
        {
            UseShellExecute = machineWide,
            WorkingDirectory = Path.GetDirectoryName(fullUpdater)!,
            Verb = machineWide ? "runas" : string.Empty
        };
        foreach (var argument in operationArguments)
        {
            if (argument is null)
            {
                throw new ArgumentException("Los argumentos del updater no pueden ser nulos.", nameof(operationArguments));
            }

            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            if (machineWide && environment.Count > 0)
            {
                throw new InvalidOperationException(
                    "El límite UAC runas no admite variables de entorno. " +
                    "Transfiera únicamente estado no sensible mediante argumentos validados.");
            }

            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        paths.AddLayoutArguments(startInfo);
        return startInfo;
    }

    public static bool TryRedirectToCurrentVersion(string[]? arguments = null)
    {
        if (IsVerificationOrRedirected())
        {
            return false;
        }

        var paths = ResolvePaths(arguments);
        var destination = new UpdateLaunchTargetResolver(paths).ResolveCurrentOrNull();
        if (destination is null)
        {
            return false;
        }

        var current = Path.GetFullPath(Environment.ProcessPath ?? string.Empty);
        if (string.Equals(current, destination, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var info = new ProcessStartInfo(destination)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(destination)!
        };
        info.Environment["ARTE_UPDATE_REDIRECTED"] = "1";
        paths.AddLayoutArguments(info);
        _ = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar la version instalada.");
        return true;
    }

    private static bool IsVerificationOrRedirected() =>
        Environment.GetEnvironmentVariable("ARTE_UPDATE_VERIFY") == "1" ||
        Environment.GetEnvironmentVariable("ARTE_UPDATE_REDIRECTED") == "1";

    private static UpdateRuntimePaths ResolvePaths(string[]? arguments)
    {
        var effectiveArguments = arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
        return UpdateRuntimePaths.CreateFromArguments(effectiveArguments);
    }

    private static UpdateLaunchResult StartDirect(ProcessStartInfo startInfo, bool machineWide)
    {
        // An already elevated machine-wide caller still uses ShellExecute so
        // Windows applies the same executable resolution semantics, but no
        // second UAC prompt is requested.
        if (machineWide)
        {
            startInfo.UseShellExecute = true;
            startInfo.Verb = string.Empty;
        }
        else
        {
            startInfo.UseShellExecute = false;
            startInfo.Verb = string.Empty;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo iniciar ArteInventariado.Updater.exe.");
        return new UpdateLaunchResult(UpdateLaunchOutcome.Started, process);
    }
}
