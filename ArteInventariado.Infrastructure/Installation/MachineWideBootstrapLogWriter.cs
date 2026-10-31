using System.Security;
using System.Text.RegularExpressions;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Best-effort, non-sensitive diagnostics for the explicit machine-wide
/// bootstrap. The log lives below UpdateState so an early failure remains
/// diagnosable without exposing source paths, credentials, or payload data.
/// Logging must never change the bootstrap outcome.
/// </summary>
public sealed class MachineWideBootstrapLogWriter
{
    private const string FileName = "machine-bootstrap.log";
    private static readonly object Sync = new();
    private readonly string _path;

    public MachineWideBootstrapLogWriter(ArteInventariadoPathProvider target)
        : this(target, Guid.NewGuid())
    {
    }

    public MachineWideBootstrapLogWriter(
        ArteInventariadoPathProvider target,
        Guid runId)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsMachineWide)
        {
            throw new ArgumentException(
                "El log de bootstrap requiere el layout machine-wide.",
                nameof(target));
        }

        _path = System.IO.Path.Combine(target.UpdateLogsRoot, FileName);
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("El runId del bootstrap no puede estar vacio.", nameof(runId));
        }

        RunId = runId;
    }

    public string Path => _path;

    public Guid RunId { get; }

    public void Started() => Write("RUN-START", "machine-bootstrap");

    public void Phase(string phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return;
        }

        // The bootstrap service also emits a generic "completed" phase for
        // its payload work.  Task Scheduler registration is a mandatory
        // installation phase that happens afterwards, so do not persist a
        // misleading completed marker until Succeeded() is called by the
        // outer orchestration after every required task is registered.
        var persistedPhase = string.Equals(phase, "completed", StringComparison.OrdinalIgnoreCase)
            ? "bootstrap-ready-awaiting-required-tasks"
            : phase;
        Write("PHASE", persistedPhase);
    }

    public void Succeeded()
    {
        Write("SUCCESS", "completed");
        Write("RUN-END", "status=SUCCESS");
    }

    internal void TaskRegistration(
        string phase,
        string taskName,
        MachineWideTaskSchedulerProcessResult result)
        => TaskCreateResult(phase, taskName, result);

    internal void TaskCreateBegin(
        string phase,
        string taskName)
    {
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        Write(
            "TASK-CREATE-BEGIN",
            $"phase={safePhase} task={safeTaskName} operation=CREATE");
    }

    internal void TaskCreateResult(
        string phase,
        string taskName,
        MachineWideTaskSchedulerProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        var standardOutput = SanitizeMessage(result.StandardOutput);
        var standardError = SanitizeMessage(result.StandardError);
        Write(
            "TASK-CREATE-RESULT",
            $"phase={safePhase} task={safeTaskName} exit={result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdoutLen={result.StandardOutput.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stderrLen={result.StandardError.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdout={standardOutput} stderr={standardError}");
    }

    internal void TaskQueryBegin(
        string phase,
        string taskName)
    {
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        Write(
            "TASK-QUERY-BEGIN",
            $"phase={safePhase} task={safeTaskName} operation=QUERY");
    }

    internal void TaskQueryResult(
        string phase,
        string taskName,
        MachineWideTaskSchedulerProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        var standardOutput = SanitizeMessage(result.StandardOutput);
        var standardError = SanitizeMessage(result.StandardError);
        Write(
            "TASK-QUERY-RESULT",
            $"phase={safePhase} task={safeTaskName} exit={result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdoutLen={result.StandardOutput.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stderrLen={result.StandardError.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdout={standardOutput} stderr={standardError}");
    }

    internal void TaskQueryResult(
        string phase,
        string taskName,
        MachineWideTaskVerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        var standardOutput = SanitizeMessage(result.StandardOutput);
        var standardError = SanitizeMessage(result.StandardError);
        Write(
            "TASK-QUERY-RESULT",
            $"phase={safePhase} task={safeTaskName} exit={result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdoutLen={result.StandardOutput.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stderrLen={result.StandardError.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdout={standardOutput} stderr={standardError}");
    }

    internal void TaskVerification(
        string phase,
        string taskName,
        bool verified)
    {
        TaskVerification(
            phase,
            taskName,
            verified
                ? new MachineWideTaskVerificationResult(true, 0, string.Empty, string.Empty)
                : MachineWideTaskVerificationResult.NotVerified(-1, string.Empty, "verificación no confirmada"));
    }

    internal void TaskVerification(
        string phase,
        string taskName,
        MachineWideTaskVerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        var standardOutput = SanitizeMessage(result.StandardOutput);
        var standardError = SanitizeMessage(result.StandardError);
        Write(
            "TASK-VERIFY",
            $"phase={safePhase} task={safeTaskName} verified={(result.Verified ? "true" : "false")} " +
            $"exit={result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdoutLen={result.StandardOutput.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stderrLen={result.StandardError.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdout={standardOutput} stderr={standardError}");
    }

    internal void TaskDeleteBegin(
        string phase,
        string taskName)
    {
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        Write(
            "TASK-DELETE-BEGIN",
            $"phase={safePhase} task={safeTaskName} operation=DELETE");
    }

    internal void TaskDeleteResult(
        string phase,
        string taskName,
        MachineWideTaskSchedulerProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var safePhase = SanitizeToken(phase, "fase-sanitizada");
        var safeTaskName = SanitizeTaskName(taskName);
        var standardOutput = SanitizeMessage(result.StandardOutput);
        var standardError = SanitizeMessage(result.StandardError);
        Write(
            "TASK-DELETE-RESULT",
            $"phase={safePhase} task={safeTaskName} exit={result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdoutLen={result.StandardOutput.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stderrLen={result.StandardError.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"stdout={standardOutput} stderr={standardError}");
    }

    internal void IntegrityFailure(
        string check,
        string? expected,
        string? found,
        string? activeVersion,
        string? activePath,
        Exception? exception = null)
    {
        var safeCheck = SanitizeToken(check, "integrity-check");
        var safeExpected = SanitizeDiagnosticValue(expected);
        var safeFound = SanitizeDiagnosticValue(found);
        var safeVersion = SanitizeVersion(activeVersion);
        var safePath = SanitizeDiagnosticValue(activePath);
        var safeError = exception is null
            ? "sin detalle"
            : $"{exception.GetType().Name}: {SanitizeMessage(exception.Message)}";
        Write(
            "INTEGRITY-FAIL",
            $"IntegrityCheck={safeCheck} Expected={safeExpected} Found={safeFound} " +
            $"ActiveVersion={safeVersion} ActivePath={safePath} Error={safeError}");
    }

    public void Failed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write(
            "ERROR",
            $"{exception.GetType().Name}: {SanitizeMessage(exception.Message)}");
        Write("RUN-END", "status=FAIL");
    }

    public void Exit(int exitCode) => Write("EXIT", exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void Write(string kind, string detail)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Diagnostics are intentionally best effort. Never mask the
            // installation error with a logging/ACL error.
        }
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "sin detalle";
        }

        var sanitized = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        sanitized = Regex.Replace(
            sanitized,
            "(?i)(password|secret|token|access.?key)\\s*[:=]?\\s*\\S+",
            "$1=<redacted>");

        // Error messages that contain a filesystem path are reduced to a
        // generic marker so a user profile, source location, or protected
        // installation path cannot leak into this persistent diagnostic.
        if (sanitized.Contains('\\') || sanitized.Contains('/') || sanitized.Length > 512)
        {
            return "mensaje sanitizado (ruta o contenido omitido)";
        }

        return sanitized;
    }

    private static string SanitizeDiagnosticValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<none>";
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (sanitized.Contains('\\') || sanitized.Contains('/') || sanitized.Contains(':'))
        {
            return "<managed-path>";
        }

        return sanitized.Length <= 128
            ? sanitized
            : "<redacted>";
    }

    private static string SanitizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<none>";
        }

        try
        {
            return ArteInventariado.Infrastructure.Updater.UpdateVersion.Sanitize(value);
        }
        catch (FormatException)
        {
            return "<invalid>";
        }
    }

    private static string SanitizeToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 96 && Regex.IsMatch(sanitized, "^[A-Za-z0-9._-]+$")
            ? sanitized
            : fallback;
    }

    private static string SanitizeTaskName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "tarea-sanitizada";
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 128 &&
               Regex.IsMatch(sanitized, "^\\\\[A-Za-z0-9._-]+(?:\\\\[A-Za-z0-9._-]+)*$")
            ? sanitized
            : "tarea-sanitizada";
    }
}
