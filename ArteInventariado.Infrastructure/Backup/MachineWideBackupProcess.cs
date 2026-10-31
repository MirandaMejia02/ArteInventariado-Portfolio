namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Owns the native process exit-code contract for the protected backup runner.
/// Successful and intentionally skipped runs return zero; every exception is
/// reported and returns a non-zero code.
/// </summary>
public static class MachineWideBackupProcess
{
    public static async Task<int> RunAsync(
        Func<Task<MachineWideBackupRunResult>> runAsync,
        TextWriter standardOutput,
        TextWriter standardError,
        Action<Exception>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(runAsync);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        try
        {
            var result = await runAsync().ConfigureAwait(false);
            await standardOutput
                .WriteLineAsync($"{result.Message} Resultado={result.Outcome}.")
                .ConfigureAwait(false);
            await standardOutput.FlushAsync().ConfigureAwait(false);
            return GetExitCode(result.Outcome);
        }
        catch (Exception ex)
        {
            try
            {
                onFailure?.Invoke(ex);
            }
            catch (Exception)
            {
                // A diagnostics failure must never replace or hide the
                // original process failure. The callback is best effort.
            }

            await standardError
                .WriteLineAsync($"BackupRunner fallido: {ex.GetType().Name}: {ex.Message}")
                .ConfigureAwait(false);
            await standardError.FlushAsync().ConfigureAwait(false);
            return 1;
        }
    }

    internal static int GetExitCode(MachineWideBackupRunOutcome outcome) => outcome is
        MachineWideBackupRunOutcome.Succeeded or
        MachineWideBackupRunOutcome.SkippedDisabled or
        MachineWideBackupRunOutcome.SkippedConcurrent
            ? 0
            : 1;
}
