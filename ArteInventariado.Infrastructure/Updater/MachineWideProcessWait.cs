using System.ComponentModel;
using System.Diagnostics;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Updater;

/// <summary>
/// Observes a machine-wide process without requiring a caller at a different
/// integrity level to open a full process handle. Windows can reject that
/// handle with ERROR_ACCESS_DENIED, so the fallback observes process existence
/// from the system process snapshot and never treats an unknown state as
/// completion.
/// </summary>
public static class MachineWideProcessWait
{
    public static async Task WaitForExitAsync(
        Process process,
        UpdateRuntimePaths paths,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.PathProvider?.IsMachineWide != true)
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            return;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            return;
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            MachineWideUpdateDiagnostics.Write(
                paths,
                "espera-aplicacion-access-denied",
                ex,
                executablePath: paths.StableUpdaterPath,
                processId: TryGetProcessId(process),
                tokenElevated: MachineWideElevation.TryGetTokenElevation(out var elevated)
                    ? elevated
                    : null,
                relatedPath: paths.StatePath,
                canonicalState: TryReadState(paths));
        }

        var processId = TryGetProcessId(process)
            ?? throw new InvalidOperationException(
                "No se pudo obtener el PID del proceso machine-wide que debe finalizar.");
        await WaitForProcessIdExitAsync(
            processId,
            paths,
            timeout,
            paths.StableUpdaterPath,
            cancellationToken);
    }

    /// <summary>
    /// Waits for a process by PID using a process snapshot. This is the
    /// machine-wide handoff primitive used by the elevated updater to wait for
    /// the normal WinForms process. It deliberately does not call
    /// Process.GetProcessById/WaitForExitAsync because those APIs require a
    /// process handle that may be denied across integrity levels.
    /// </summary>
    public static async Task WaitForProcessIdExitAsync(
        int processId,
        UpdateRuntimePaths paths,
        TimeSpan timeout,
        string? expectedExecutablePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (paths.PathProvider?.IsMachineWide != true)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            }
            catch (ArgumentException)
            {
                // The process already exited between PID capture and lookup.
            }

            return;
        }

        var deadline = DateTime.UtcNow + timeout;
        var activeLogged = false;
        var unknownLogged = false;
        Exception? lastObservationError = null;
        string? lastCanonicalState = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastCanonicalState = TryReadState(paths);
            var observation = ObserveProcess(processId, out var observationError);
            if (observation == ProcessObservation.Exited)
            {
                MachineWideUpdateDiagnostics.Write(
                    paths,
                    "espera-proceso-finalizado",
                    executablePath: expectedExecutablePath,
                    processId: processId,
                    tokenElevated: MachineWideElevation.TryGetTokenElevation(out var exitedElevation)
                        ? exitedElevation
                        : null,
                    relatedPath: paths.StatePath,
                    canonicalState: lastCanonicalState);
                return;
            }

            if (observation == ProcessObservation.Active)
            {
                if (!activeLogged)
                {
                    MachineWideUpdateDiagnostics.Write(
                        paths,
                        "espera-proceso-activo",
                        executablePath: expectedExecutablePath,
                        processId: processId,
                        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var activeElevation)
                            ? activeElevation
                            : null,
                        relatedPath: paths.StatePath,
                        canonicalState: lastCanonicalState);
                    activeLogged = true;
                }
            }
            else
            {
                lastObservationError = observationError;
                if (!unknownLogged)
                {
                    MachineWideUpdateDiagnostics.Write(
                        paths,
                        "espera-proceso-estado-desconocido",
                        observationError,
                        executablePath: expectedExecutablePath,
                        processId: processId,
                        tokenElevated: MachineWideElevation.TryGetTokenElevation(out var unknownElevation)
                            ? unknownElevation
                            : null,
                        relatedPath: paths.StatePath,
                        canonicalState: lastCanonicalState);
                    unknownLogged = true;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        MachineWideUpdateDiagnostics.Write(
            paths,
            "espera-proceso-timeout",
            lastObservationError,
            executablePath: expectedExecutablePath,
            processId: processId,
            tokenElevated: MachineWideElevation.TryGetTokenElevation(out var timeoutElevation)
                ? timeoutElevation
                : null,
            relatedPath: paths.StatePath,
            canonicalState: lastCanonicalState);
        throw new TimeoutException(
            $"No se pudo confirmar el cierre del proceso PID {processId} " +
            $"(estado canonico: {lastCanonicalState ?? "desconocido"}) dentro del tiempo seguro.",
            lastObservationError);
    }

    private static ProcessObservation ObserveProcess(int processId, out Exception? error)
    {
        error = null;
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == processId)
                    {
                        return ProcessObservation.Active;
                    }
                }
            }

            return ProcessObservation.Exited;
        }
        catch (Exception ex) when (
            ex is Win32Exception or
            InvalidOperationException or
            UnauthorizedAccessException)
        {
            error = ex;
            return ProcessObservation.Unknown;
        }
    }

    private static string? TryReadState(UpdateRuntimePaths paths)
    {
        try
        {
            return new UpdateStateStore(paths.StatePath).Load()?.State.ToString();
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            return $"read-error:{ex.GetType().Name}";
        }
    }

    private enum ProcessObservation
    {
        Active,
        Exited,
        Unknown
    }

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
            ex is InvalidOperationException or ArgumentException or Win32Exception)
        {
            return null;
        }
    }
}
