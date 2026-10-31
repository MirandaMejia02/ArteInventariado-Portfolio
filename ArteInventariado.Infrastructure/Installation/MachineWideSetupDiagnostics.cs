using System.ComponentModel;
using System.Security;
using System.Text.RegularExpressions;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Diagnostic journal for the standalone setup bootstrap. It intentionally
/// lives under the user's temporary directory rather than an installation
/// root, so an explicit clean-install rollback cannot erase the evidence of
/// the failure. It never records credentials or command-line secrets.
/// </summary>
public sealed class MachineWideSetupDiagnostics
{
    private static readonly Regex SecretPattern = new(
        "(?i)(password|passwd|secret|token|access.?key)\\s*[:=]?\\s*\\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly object Sync = new();
    private readonly string _path;

    public MachineWideSetupDiagnostics(Guid? runId = null)
    {
        RunId = runId.GetValueOrDefault(Guid.NewGuid());
        if (RunId == Guid.Empty)
        {
            RunId = Guid.NewGuid();
        }

        _path = Path.Combine(
            Path.GetTempPath(),
            "ArteInventariado",
            "SetupDiagnostics",
            $"setup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{RunId:N}.log");
    }

    public Guid RunId { get; }

    public string LogPath => _path;

    public void Started(
        ArteInventariadoPathProvider? target,
        string? executablePath,
        string? payloadDirectory,
        bool cleanInstall)
    {
        Write(
            "START",
            $"pid={Environment.ProcessId} executable={SanitizeValue(executablePath)} " +
            $"elevated={GetElevation()} cleanInstall={cleanInstall} " +
            $"payload={SanitizeValue(payloadDirectory)} " +
            $"machineInstallRoot={SanitizeValue(target?.MachineInstallRoot)} " +
            $"machineDataRoot={SanitizeValue(target?.MachineDataRoot)} " +
            $"userDataRoot={SanitizeValue(target?.UserDataRoot)}");
    }

    public void Phase(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return;
        }

        Write("PHASE", $"stage={SanitizeToken(phase)}");
    }

    public void Succeeded()
    {
        Write("SUCCESS", "setup-complete");
        Write("EXIT", "0");
    }

    public void Failed(string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var nativeErrorCode = FindNativeErrorCode(exception);
        var hresult = $"0x{exception.HResult:X8}";
        var detail = SanitizeMessage(exception.ToString());
        Write(
            "ERROR",
            $"stage={SanitizeToken(stage)} nativeErrorCode={nativeErrorCode?.ToString() ?? "none"} " +
            $"hresult={hresult} exception={detail}");
        Write("EXIT", "1");
    }

    public void CleanupFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write(
            "CLEANUP-ERROR",
            $"nativeErrorCode={FindNativeErrorCode(exception)?.ToString() ?? "none"} " +
            $"exception={SanitizeMessage(exception.ToString())}");
    }

    private void Write(string kind, string detail)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            lock (Sync)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    _path,
                    $"{DateTimeOffset.UtcNow:O} | {kind} | {detail} | runId={RunId:D}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or SecurityException)
        {
            // Diagnostics must never replace the setup error.
        }
    }

    private static string GetElevation()
    {
        try
        {
            return MachineWideElevation.TryGetTokenElevation(out var elevated)
                ? elevated ? "true" : "false"
                : "unknown";
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or Win32Exception)
        {
            return "unknown";
        }
    }

    private static int? FindNativeErrorCode(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win32)
            {
                return win32.NativeErrorCode;
            }
        }

        return null;
    }

    private static string SanitizeMessage(string value)
    {
        var sanitized = SecretPattern.Replace(
            value.Replace('\r', ' ').Replace('\n', ' ').Trim(),
            "$1=<redacted>");
        return sanitized.Length <= 12000 ? sanitized : sanitized[..12000];
    }

    private static string SanitizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<none>";
        }

        return SanitizeMessage(value);
    }

    private static string SanitizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : Regex.IsMatch(value, "^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)
                ? value
                : "sanitized";
}
