using System.ComponentModel;
using System.Security;
using System.Security.Principal;
using System.Text.RegularExpressions;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Best-effort diagnostics for the process boundary of the protected backup.
/// The runner can fail while resolving its machine-wide roots or constructing
/// the runner, before <see cref="MachineWideBackupRunner.RunAsync"/> can write
/// BACKUP_RUNNER_STARTED.  This class records that boundary without changing
/// the backup outcome and never writes secrets.
/// </summary>
internal static class MachineWideBackupDiagnostics
{
    public const string FileName = "backup-runner-diagnostics.log";

    public static void RecordEarlyFailure(
        ArteInventariadoPathProvider? paths,
        string stage,
        Exception exception,
        DateTimeOffset? attempt = null,
        string? relatedPath = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var timestamp = attempt ?? DateTimeOffset.UtcNow;
        var detail = BuildDetail(stage, exception, relatedPath);
        TryAppend(GetLogPath(paths), detail);

        if (paths?.IsMachineWide == true)
        {
            TryWriteAudit(
                paths,
                "BACKUP_RUNNER_PROCESS_FAILED_BEFORE_START",
                $"Fallo antes de entrar al runner; etapa={SanitizeToken(stage)}; {detail}");
            TryPersistFailureStatus(
                paths,
                timestamp,
                $"Fallo temprano en etapa '{SanitizeToken(stage)}': {exception.GetType().Name}.");
        }
    }

    public static void RecordRunnerFailure(
        ArteInventariadoPathProvider paths,
        string stage,
        Exception exception,
        string? relatedPath = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(exception);
        TryAppend(GetLogPath(paths), BuildDetail(stage, exception, relatedPath));
    }

    public static string GetLogPath(ArteInventariadoPathProvider? paths)
    {
        if (paths?.IsMachineWide == true)
        {
            return Path.Combine(paths.StationLogsRoot, FileName);
        }

        return Path.Combine(Path.GetTempPath(), "ArteInventariado", FileName);
    }

    private static string BuildDetail(
        string stage,
        Exception exception,
        string? relatedPath)
    {
        var native = FindNativeErrorCode(exception);
        var path = relatedPath ?? GetExceptionPath(exception);
        var token = MachineWideElevation.TryGetTokenElevation(out var elevated)
            ? elevated ? "true" : "false"
            : "unknown";
        return
            $"utc={DateTimeOffset.UtcNow:O}" +
            $" pid={Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" executable={Sanitize(Environment.ProcessPath)}" +
            $" user={Sanitize(GetCurrentUser())}" +
            $" tokenElevated={token}" +
            $" temp={Sanitize(Environment.GetEnvironmentVariable("TEMP"))}" +
            $" tmp={Sanitize(Environment.GetEnvironmentVariable("TMP"))}" +
            $" bundleExtractBase={Sanitize(Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"))}" +
            $" stage={SanitizeToken(stage)}" +
            $" relatedPath={Sanitize(path)}" +
            $" exceptionType={Sanitize(exception.GetType().FullName)}" +
            $" nativeErrorCode={native?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}" +
            $" exception={Sanitize(exception.ToString())}";
    }

    private static void TryPersistFailureStatus(
        ArteInventariadoPathProvider paths,
        DateTimeOffset attempt,
        string error)
    {
        try
        {
            var store = new MachineWideBackupPolicyStore();
            MachineWideBackupStatus previous;
            try
            {
                previous = store.LoadStatus(paths);
            }
            catch (Exception ex) when (IsPersistenceFailure(ex))
            {
                previous = MachineWideBackupPolicyStore.EmptyStatus;
            }

            store.SaveStatus(
                paths,
                previous with
                {
                    LastAttemptUtc = attempt,
                    LastError = error,
                    LastOutcome = MachineWideBackupStatusKind.Failure,
                    LastOmittedAssetCount = 0
                });
        }
        catch (Exception ex) when (IsPersistenceFailure(ex))
        {
            TryAppend(
                GetLogPath(paths),
                $"utc={DateTimeOffset.UtcNow:O} stage=estado exceptionType={ex.GetType().Name} " +
                "event=BACKUP_STATUS_WRITE_FAILED");
        }
    }

    private static void TryWriteAudit(
        ArteInventariadoPathProvider paths,
        string eventName,
        string description)
    {
        try
        {
            new StationEventLog(paths).Write(eventName, "SYSTEM", null, Sanitize(description));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            TryAppend(
                GetLogPath(paths),
                $"utc={DateTimeOffset.UtcNow:O} event=BACKUP_AUDIT_WRITE_FAILED " +
                $"exceptionType={ex.GetType().Name}");
        }
    }

    private static bool IsPersistenceFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidDataException or
        SecurityException or ArgumentException;

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
        catch (Exception ex) when (IsPersistenceFailure(ex))
        {
            var fallback = Path.Combine(Path.GetTempPath(), "ArteInventariado", FileName);
            if (!string.Equals(path, fallback, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
                    File.AppendAllText(fallback, line + Environment.NewLine);
                }
                catch (Exception fallbackException) when (IsPersistenceFailure(fallbackException))
                {
                    // Diagnostics are deliberately best effort.
                }
            }

            return false;
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

    private static string? GetExceptionPath(Exception exception) => exception switch
    {
        FileNotFoundException file => file.FileName,
        DirectoryNotFoundException directory => directory.Message,
        _ => null
    };

    private static string GetCurrentUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.UserName;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name ?? Environment.UserName;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or Win32Exception)
        {
            return Environment.UserName;
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
            "(?i)(password|secret|access.?key|credential|token)\\s*[:=]?\\s*\\S+",
            "$1=<redacted>");
        return sanitized.Length <= 4096 ? sanitized : sanitized[..4096];
    }

    private static string SanitizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<stage>";
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 96 && Regex.IsMatch(sanitized, "^[A-Za-z0-9._-]+$")
            ? sanitized
            : "<stage>";
    }
}
