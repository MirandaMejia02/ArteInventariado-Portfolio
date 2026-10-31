using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ArteInventariado.Infrastructure.Updater;

/// <summary>
/// Best-effort diagnostics for the machine-wide update boundary.  The log is
/// intentionally operational (PID, stage, token state and exception details)
/// and never participates in the update decision.  It is useful when a
/// non-elevated launcher cannot inspect an elevated updater process.
/// </summary>
public static class MachineWideUpdateDiagnostics
{
    public const string FileName = "machine-update-diagnostics.log";

    public static string GetLogPath(UpdateRuntimePaths? paths)
    {
        if (paths?.PathProvider?.IsMachineWide == true)
        {
            return Path.Combine(paths.LogDirectory, FileName);
        }

        return Path.Combine(
            Path.GetTempPath(),
            "ArteInventariado",
            FileName);
    }

    public static void Write(
        UpdateRuntimePaths? paths,
        string stage,
        Exception? exception = null,
        string? executablePath = null,
        int? processId = null,
        bool? tokenElevated = null,
        string? relatedPath = null,
        string? canonicalState = null,
        string? detail = null)
    {
        // Keep normal per-user launches silent; diagnostics are persisted for
        // the protected machine-wide boundary or when a legacy launch fails.
        if (paths?.PathProvider?.IsMachineWide != true && exception is null && string.IsNullOrWhiteSpace(detail))
        {
            return;
        }

        var line =
            $"utc={DateTimeOffset.UtcNow:O}" +
            $" pid={(processId ?? Environment.ProcessId).ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" executable={Sanitize(executablePath ?? Environment.ProcessPath)}" +
            $" stage={SanitizeToken(stage)}" +
            $" tokenElevated={(tokenElevated.HasValue ? (tokenElevated.Value ? "true" : "false") : "unknown")}" +
            $" user={Sanitize(Environment.UserDomainName + "\\" + Environment.UserName)}" +
            $" sessionId={TryGetSessionId()?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}" +
            $" integrity={DescribeIntegrity(tokenElevated)}" +
            $" canonicalState={SanitizeToken(canonicalState)}" +
            $" relatedPath={SanitizePath(relatedPath)}" +
            (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" detail={Sanitize(detail)}");

        if (exception is not null)
        {
            line +=
                $" exceptionType={Sanitize(exception.GetType().FullName)}" +
                $" nativeErrorCode={FindNativeErrorCode(exception)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}" +
                $" exception={Sanitize(exception.ToString())}";
        }

        var primary = GetLogPath(paths);
        if (TryAppend(primary, line))
        {
            return;
        }

        var fallback = Path.Combine(
            Path.GetTempPath(),
            "ArteInventariado",
            FileName);
        if (!string.Equals(primary, fallback, StringComparison.OrdinalIgnoreCase))
        {
            TryAppend(fallback, line);
        }
    }

    public static int? FindNativeErrorCode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win32)
            {
                return win32.NativeErrorCode;
            }
        }

        return null;
    }

    public static string Describe(
        string stage,
        Exception exception,
        string? relatedPath = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var native = FindNativeErrorCode(exception);
        var nativeText = native.HasValue
            ? $"; código nativo {native.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : string.Empty;
        var pathText = string.IsNullOrWhiteSpace(relatedPath)
            ? string.Empty
            : $"; ruta: {SanitizePath(relatedPath)}";
        return $"Fallo en etapa '{SanitizeToken(stage)}' ({exception.GetType().Name}{nativeText}{pathText}): {Sanitize(exception.Message)}";
    }

    private static bool TryAppend(string path, string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            File.AppendAllText(path, line + Environment.NewLine);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            ArgumentException)
        {
            return false;
        }
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<none>";
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        sanitized = Regex.Replace(
            sanitized,
            "(?i)(password|secret|access.?key|credential)\\s*[:=]?\\s*\\S+",
            "$1=<redacted>");
        return sanitized.Length <= 4096 ? sanitized : sanitized[..4096];
    }

    private static string SanitizePath(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "<none>"
            : Sanitize(value);

    private static string SanitizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<none>";
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 96 && Regex.IsMatch(sanitized, "^[A-Za-z0-9._-]+$")
            ? sanitized
            : "<stage>";
    }

    private static int? TryGetSessionId()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static string DescribeIntegrity(bool? tokenElevated) => tokenElevated switch
    {
        true => "high-or-system",
        false => "medium-or-low",
        _ => "unknown"
    };
}
