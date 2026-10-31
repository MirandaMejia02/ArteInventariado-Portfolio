using System.Diagnostics;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Infrastructure.Backup;

internal sealed record ProtectedRestoreCandidateStageLaunchResult(bool UacCancelled);

internal interface IProtectedRestoreCandidateStageLauncher
{
    Task<ProtectedRestoreCandidateStageLaunchResult> LaunchAndWaitAsync(
        string executablePath,
        UpdateRuntimePaths runtimePaths,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

/// <summary>
/// Starts only the fixed-copy operation in StationHelper. Once UAC accepts,
/// observation continues until the helper exits so the UI cannot mistake an
/// incomplete copy for a staged candidate.
/// </summary>
internal sealed class ProtectedRestoreCandidateStageLauncher : IProtectedRestoreCandidateStageLauncher
{
    public async Task<ProtectedRestoreCandidateStageLaunchResult> LaunchAndWaitAsync(
        string executablePath,
        UpdateRuntimePaths runtimePaths,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        if (MachineWideElevation.IsAdministrator())
        {
            process = StartNormally(executablePath, arguments);
        }
        else
        {
            var elevation = MachineWideElevation.EnsureElevated(executablePath, arguments);
            if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
            {
                return new ProtectedRestoreCandidateStageLaunchResult(UacCancelled: true);
            }

            process = elevation.Outcome == MachineWideElevationOutcome.RelaunchedElevated
                ? elevation.Process
                : StartNormally(executablePath, arguments);
        }

        if (process is null)
        {
            throw new InvalidOperationException("Windows no devolvio el proceso del helper de importacion protegido.");
        }

        using (process)
        {
            await MachineWideProcessWait.WaitForExitAsync(
                process,
                runtimePaths,
                TimeSpan.FromHours(2),
                CancellationToken.None).ConfigureAwait(false);
        }

        return new ProtectedRestoreCandidateStageLaunchResult(UacCancelled: false);
    }

    private static Process StartNormally(string executablePath, IReadOnlyList<string> arguments)
    {
        var fullPath = Path.GetFullPath(executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows no pudo iniciar el helper de importacion protegido.");
    }
}
