using System.ComponentModel;
using System.Diagnostics;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class StableLauncherCoordinator
{
    private readonly UpdateRuntimePaths _paths;
    private readonly UpdateLaunchTargetResolver _resolver;
    private readonly UpdateStateStore _stateStore;
    private readonly IUpdateRecoveryRunner _recoveryRunner;

    public StableLauncherCoordinator(UpdateRuntimePaths paths)
        : this(
            paths,
            new UpdateLaunchTargetResolver(paths),
            new UpdateStateStore(paths.StatePath),
            new ProcessUpdateRecoveryRunner(paths))
    {
    }

    internal StableLauncherCoordinator(
        UpdateRuntimePaths paths,
        UpdateLaunchTargetResolver resolver,
        UpdateStateStore stateStore,
        IUpdateRecoveryRunner recoveryRunner)
    {
        _paths = paths;
        _resolver = resolver;
        _stateStore = stateStore;
        _recoveryRunner = recoveryRunner;
    }

    public string ResolveCurrentWithRecovery(bool recoveryAlreadyAttempted)
    {
        try
        {
            return ResolveCurrent();
        }
        catch (Exception original) when (IsResolutionFailure(original))
        {
            var state = _stateStore.Load();
            if (!UpdateRecoveryPolicy.RequiresRecovery(state))
            {
                throw;
            }

            if (recoveryAlreadyAttempted)
            {
                throw new InvalidOperationException(
                    "La recuperacion ya fue intentada y CURRENT sigue siendo invalido.",
                    original);
            }

            var exitCode = _recoveryRunner.Recover();
            if (exitCode is not (0 or 2))
            {
                throw new InvalidOperationException($"El recovery termino con codigo {exitCode}.", original);
            }

            try
            {
                return ResolveCurrent();
            }
            catch (Exception recoveryError) when (IsResolutionFailure(recoveryError))
            {
                throw new InvalidOperationException(
                    "Recovery termino sin establecer una version CURRENT valida.",
                    recoveryError);
            }
        }
    }

    private string ResolveCurrent() =>
        _resolver.ResolveCurrentOrNull()
        ?? throw new InvalidDataException("No existe una version CURRENT instalada.");

    private static bool IsResolutionFailure(Exception exception) =>
        exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException;
}

internal interface IUpdateRecoveryRunner
{
    int Recover();
}

internal sealed class ProcessUpdateRecoveryRunner(UpdateRuntimePaths paths) : IUpdateRecoveryRunner
{
    public int Recover()
    {
        if (!File.Exists(paths.StableUpdaterPath))
        {
            throw new FileNotFoundException(
                "La actualizacion requiere recovery, pero falta el updater estable.",
                paths.StableUpdaterPath);
        }

        var stateStore = new UpdateStateStore(paths.StatePath);
        var stateBeforeLaunch = stateStore.Load();
        var result = UpdateLauncher.LaunchUpdater(
            paths,
            paths.StableUpdaterPath,
            [
                "--recover",
                "--no-relaunch",
                "--state",
                paths.StatePath
            ]);
        if (result.Outcome == UpdateLaunchOutcome.Cancelled)
        {
            throw new UnauthorizedAccessException(
                "El recovery machine-wide fue cancelado en UAC; no se modificaron current ni previous.");
        }

        using var process = result.Process
            ?? throw new InvalidOperationException("No se obtuvo el proceso del recovery oficial.");
        try
        {
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("El recovery no termino dentro del tiempo seguro.");
            }

            return process.ExitCode;
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            // A medium-integrity launcher is not allowed to open a full
            // process handle for the elevated updater.  The updater still
            // owns the operation; observe its atomically-written state rather
            // than treating this handle error as a failed recovery.
            MachineWideUpdateDiagnostics.Write(
                paths,
                "recovery-wait-access-denied",
                ex,
                executablePath: paths.StableUpdaterPath,
                processId: TryGetProcessId(process),
                tokenElevated: MachineWideElevation.TryGetTokenElevation(out var elevated)
                    ? elevated
                    : null,
                relatedPath: paths.StatePath);
            return WaitForRecoveryState(stateStore, stateBeforeLaunch);
        }
    }

    private int WaitForRecoveryState(
        UpdateStateStore stateStore,
        UpdateStateDocument? stateBeforeLaunch)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        Exception? lastReadError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var current = stateStore.Load();
                if (HasTransitioned(current, stateBeforeLaunch) &&
                    IsRecoveryTerminal(current!.State))
                {
                    MachineWideUpdateDiagnostics.Write(
                        paths,
                        "recovery-state-completed",
                        executablePath: paths.StableUpdaterPath,
                        relatedPath: paths.StatePath);
                    return current!.State switch
                    {
                        UpdateState.RollbackRequired or UpdateState.RollingBack => 3,
                        _ => 0
                    };
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
            {
                lastReadError = ex;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException(
            "El recovery termino sin que el estado administrado confirmara su finalizacion.",
            lastReadError);
    }

    private static bool HasTransitioned(
        UpdateStateDocument? current,
        UpdateStateDocument? before)
    {
        if (current is null)
        {
            return false;
        }

        if (before is null)
        {
            return current.State is UpdateState.Idle or UpdateState.Success or UpdateState.Failed;
        }

        return current.State != before.State || current.UpdatedUtc > before.UpdatedUtc;
    }

    private static bool IsRecoveryTerminal(UpdateState state) =>
        state is UpdateState.Idle or
            UpdateState.Success or
            UpdateState.Failed or
            UpdateState.RollbackRequired;

    private static bool IsAccessDenied(Exception exception) =>
        exception is UnauthorizedAccessException ||
        exception is Win32Exception { NativeErrorCode: 5 } ||
        exception.InnerException is not null && IsAccessDenied(exception.InnerException);

    private static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            ArgumentException or
            Win32Exception)
        {
            return null;
        }
    }
}
