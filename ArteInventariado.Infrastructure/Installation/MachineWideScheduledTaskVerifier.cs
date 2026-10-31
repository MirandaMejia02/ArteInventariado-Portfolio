using System.Diagnostics;
using System.ComponentModel;
using System.Xml;
using System.Xml.Linq;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Reads a scheduled task back from Windows and verifies the properties that
/// make it part of the protected machine-wide installation contract.  A
/// successful <c>schtasks /Create</c> response is not sufficient: the task
/// must exist physically and still point at the expected SYSTEM runner.
/// </summary>
internal static class MachineWideScheduledTaskVerifier
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static bool Verify(
        ArteInventariadoPathProvider paths,
        string taskName,
        string expectedCommand)
        => VerifyDetailed(paths, taskName, expectedCommand).Verified;

    public static MachineWideTaskVerificationResult VerifyDetailed(
        ArteInventariadoPathProvider paths,
        string taskName,
        string expectedCommand,
        MachineWideBootstrapLogWriter? logWriter = null,
        Func<ProcessStartInfo, MachineWideTaskSchedulerProcessResult>? execute = null,
        string phase = "task-query")
    {
        ArgumentNullException.ThrowIfNull(paths);
        logWriter?.TaskQueryBegin(phase, taskName);

        MachineWideTaskVerificationResult result;
        if (!paths.IsMachineWide ||
            string.IsNullOrWhiteSpace(taskName) ||
            string.IsNullOrWhiteSpace(expectedCommand) ||
            !OperatingSystem.IsWindows())
        {
            result = MachineWideTaskVerificationResult.NotVerified(
                -1,
                string.Empty,
                "La verificación de tareas requiere Windows y parámetros administrados.");
            logWriter?.TaskQueryResult(phase, taskName, result);
            logWriter?.TaskVerification(phase, taskName, result);
            return result;
        }

        MachineWideTaskSchedulerProcessResult? processResult = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/Query");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(taskName);
            startInfo.ArgumentList.Add("/XML");

            // Every verification is a fresh physical Query process.  Never
            // reuse the /Create result or a previously parsed XML document.
            processResult = (execute ?? MachineWideTaskSchedulerProcess.Execute)(startInfo);
            if (processResult.ExitCode != 0 || string.IsNullOrWhiteSpace(processResult.StandardOutput))
            {
                var notVerified = MachineWideTaskVerificationResult.NotVerified(
                    processResult.ExitCode,
                    processResult.StandardOutput,
                    processResult.StandardError);
                logWriter?.TaskQueryResult(phase, taskName, notVerified);
                logWriter?.TaskVerification(phase, taskName, notVerified);
                return notVerified;
            }

            var xml = ParseXml(processResult.StandardOutput);
            var verified = MatchesContract(xml, paths, taskName, expectedCommand);
            var verification = new MachineWideTaskVerificationResult(
                verified,
                processResult.ExitCode,
                processResult.StandardOutput,
                verified ? processResult.StandardError : "La definición física no coincide con el contrato esperado.");
            logWriter?.TaskQueryResult(phase, taskName, verification);
            logWriter?.TaskVerification(phase, taskName, verification);
            return verification;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or XmlException or Win32Exception)
        {
            result = MachineWideTaskVerificationResult.NotVerified(
                -1,
                processResult?.StandardOutput ?? string.Empty,
                string.IsNullOrWhiteSpace(processResult?.StandardError)
                    ? ex.Message
                    : processResult!.StandardError);
            logWriter?.TaskQueryResult(phase, taskName, result);
            logWriter?.TaskVerification(phase, taskName, result);
            return result;
        }
    }

    private static XDocument ParseXml(string output)
    {
        // schtasks normally emits XML directly, but older localized builds
        // can prepend a short banner.  Discard only the prefix before the
        // actual Task element; never interpret arbitrary command output.
        var taskStart = output.IndexOf("<Task", StringComparison.OrdinalIgnoreCase);
        if (taskStart < 0)
        {
            throw new XmlException("schtasks no devolvió una definición XML.");
        }

        return XDocument.Parse(output[taskStart..], LoadOptions.PreserveWhitespace);
    }

    private static bool MatchesContract(
        XDocument document,
        ArteInventariadoPathProvider paths,
        string expectedTaskName,
        string expectedCommand)
    {
        var ns = XNamespace.Get(TaskNamespace);
        var task = document.Root;
        if (task is null || task.Name != ns + "Task")
        {
            return false;
        }

        var expectedUri = CanonicalTaskName(expectedTaskName);
        var actualUri = task.Descendants(ns + "URI").SingleOrDefault()?.Value;
        if (expectedUri is null || !string.Equals(
                CanonicalTaskName(actualUri),
                expectedUri,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var principal = task.Descendants(ns + "Principal").SingleOrDefault();
        if (principal is null ||
            !TextEquals(principal.Element(ns + "UserId"), MachineWideBackupPolicyStore.LocalSystemSid) ||
            !TextEquals(principal.Element(ns + "RunLevel"), "HighestAvailable"))
        {
            return false;
        }

        var settings = task.Element(ns + "Settings");
        if (settings is null ||
            !TextEquals(settings.Element(ns + "MultipleInstancesPolicy"), "IgnoreNew") ||
            !BoolEquals(settings.Element(ns + "StartWhenAvailable"), expected: true))
        {
            return false;
        }

        var restart = settings.Element(ns + "RestartOnFailure");
        if (restart is null ||
            !TextEquals(restart.Element(ns + "Interval"), "PT15M") ||
            !TextEquals(restart.Element(ns + "Count"), "3"))
        {
            return false;
        }

        var action = task.Descendants(ns + "Exec").SingleOrDefault();
        if (action is null ||
            !PathEquals(action.Element(ns + "Command")?.Value, expectedCommand) ||
            !TextEquals(action.Element(ns + "Arguments"), "--run") ||
            !PathEquals(action.Element(ns + "WorkingDirectory")?.Value, paths.MachineInstallRoot))
        {
            return false;
        }

        return true;
    }

    private static string? CanonicalTaskName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().Replace('/', '\\');
        if (!trimmed.StartsWith('\\'))
        {
            return null;
        }

        var segments = trimmed
            .Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return "\\" + string.Join('\\', segments);
    }

    private static bool TextEquals(XElement? element, string expected) =>
        element is not null &&
        string.Equals(element.Value.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool BoolEquals(XElement? element, bool expected) =>
        element is not null &&
        bool.TryParse(element.Value.Trim(), out var value) &&
        value == expected;

    private static bool PathEquals(string? actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(actual.Trim()),
                Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal sealed record MachineWideTaskVerificationResult(
    bool Verified,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public static MachineWideTaskVerificationResult SyntheticSuccess() =>
        new(true, 0, string.Empty, string.Empty);

    public static MachineWideTaskVerificationResult NotVerified(
        int exitCode,
        string standardOutput,
        string standardError) =>
        new(false, exitCode, standardOutput, standardError);
}
