using System.Diagnostics;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Sanitizable result from a Windows Task Scheduler command.  The scheduler
/// registration boundary needs the complete process result because schtasks
/// reports XML/permission failures on stderr rather than in the exit code.
/// </summary>
internal sealed record MachineWideTaskSchedulerProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal static class MachineWideTaskSchedulerProcess
{
    public static MachineWideTaskSchedulerProcessResult Execute(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo iniciar schtasks.exe.");

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);

        return new MachineWideTaskSchedulerProcessResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result);
    }
}
