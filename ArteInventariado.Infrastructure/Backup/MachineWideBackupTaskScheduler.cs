using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.Backup;

public interface IMachineWideBackupTaskScheduler
{
    void EnsureRegistered(
        ArteInventariadoPathProvider paths,
        MachineWideBackupPolicy policy);

    /// <summary>
    /// Reads the task back from Windows and confirms the protected SYSTEM
    /// contract.  Implementations used by legacy callers may return false;
    /// the installer treats that as not-ready and repairs the task.
    /// </summary>
    bool IsRegisteredCorrectly(
        ArteInventariadoPathProvider paths,
        MachineWideBackupPolicy policy) => false;
}

/// <summary>
/// Registers the protected daily BackupRunner task. The XML definition is
/// deterministic and explicitly uses LocalSystem, StartWhenAvailable and an
/// IgnoreNew concurrency policy. Registration is an elevated installer
/// operation; normal application startup never changes the task.
/// </summary>
public sealed class MachineWideBackupTaskScheduler : IMachineWideBackupTaskScheduler
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly Func<ProcessStartInfo, MachineWideTaskSchedulerProcessResult> _execute;
    private readonly Func<ArteInventariadoPathProvider, string, string, MachineWideTaskVerificationResult>? _verify;
    private readonly bool _allowSyntheticRoots;
    private readonly Guid? _runId;

    public MachineWideBackupTaskScheduler()
        : this(MachineWideTaskSchedulerProcess.Execute, verify: null, allowSyntheticRoots: false, runId: null)
    {
    }

    public MachineWideBackupTaskScheduler(Guid runId)
        : this(MachineWideTaskSchedulerProcess.Execute, verify: null, allowSyntheticRoots: false, runId)
    {
    }

    internal MachineWideBackupTaskScheduler(
        Func<ProcessStartInfo, int> execute,
        bool allowSyntheticRoots = false)
        : this(
            startInfo => new MachineWideTaskSchedulerProcessResult(
                execute(startInfo),
                string.Empty,
                string.Empty),
            verify: allowSyntheticRoots
                ? static (_, _, _) => MachineWideTaskVerificationResult.SyntheticSuccess()
                : null,
            allowSyntheticRoots,
            runId: null)
    {
    }

    internal MachineWideBackupTaskScheduler(
        Func<ProcessStartInfo, MachineWideTaskSchedulerProcessResult> execute,
        bool allowSyntheticRoots = false)
        : this(
            execute,
            verify: allowSyntheticRoots
                ? static (_, _, _) => MachineWideTaskVerificationResult.SyntheticSuccess()
                : null,
            allowSyntheticRoots,
            runId: null)
    {
    }

    internal MachineWideBackupTaskScheduler(
        Func<ProcessStartInfo, MachineWideTaskSchedulerProcessResult> execute,
        Func<ArteInventariadoPathProvider, string, string, bool> verify,
        bool allowSyntheticRoots = false)
        : this(
            execute,
            (paths, taskName, command) =>
            {
                var verified = verify(paths, taskName, command);
                return new MachineWideTaskVerificationResult(
                    verified,
                    verified ? 0 : -1,
                    string.Empty,
                    verified ? string.Empty : "La verificación física fue rechazada por el escenario de prueba.");
            },
            allowSyntheticRoots,
            runId: null)
    {
    }

    internal MachineWideBackupTaskScheduler(
        Func<ProcessStartInfo, MachineWideTaskSchedulerProcessResult> execute,
        Func<ArteInventariadoPathProvider, string, string, MachineWideTaskVerificationResult>? verify,
        bool allowSyntheticRoots = false,
        Guid? runId = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _verify = verify;
        _allowSyntheticRoots = allowSyntheticRoots;
        _runId = runId;
    }

    public bool IsRegisteredCorrectly(
        ArteInventariadoPathProvider paths,
        MachineWideBackupPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(policy);
        if (!paths.IsMachineWide || !File.Exists(paths.BackupRunnerPath))
        {
            return false;
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, _allowSyntheticRoots);
        MachineWideBackupPolicyStore.Validate(paths, policy);
        return Verify(paths, policy.TaskName, paths.BackupRunnerPath).Verified;
    }

    public void EnsureRegistered(
        ArteInventariadoPathProvider paths,
        MachineWideBackupPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(policy);
        if (!paths.IsMachineWide)
        {
            throw new InvalidOperationException("La tarea protegida requiere el layout machine-wide.");
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, _allowSyntheticRoots);
        MachineWideBackupPolicyStore.Validate(paths, policy);
        if (!File.Exists(paths.BackupRunnerPath))
        {
            throw new FileNotFoundException(
                "La instalación protegida no contiene ArteInventariado.BackupRunner.exe.",
                paths.BackupRunnerPath);
        }

        var taskDefinitionPath = Path.Combine(
            paths.MachineInstallRoot,
            $".backup-task-{Guid.NewGuid():N}.xml");
        EnsureNoReparsePoints(paths.MachineInstallRoot);
        try
        {
            File.WriteAllText(taskDefinitionPath, BuildTaskXml(paths, policy), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/Create");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(policy.TaskName);
            startInfo.ArgumentList.Add("/RU");
            startInfo.ArgumentList.Add("SYSTEM");
            startInfo.ArgumentList.Add("/XML");
            startInfo.ArgumentList.Add(taskDefinitionPath);
            startInfo.ArgumentList.Add("/F");
            var log = CreateLog(paths);
            log.Phase("backup-task-registering");
            log.TaskCreateBegin("backup-task-registering", policy.TaskName);
            var result = _execute(startInfo);
            log.TaskCreateResult("backup-task-registering", policy.TaskName, result);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"No se pudo registrar la tarea protegida de respaldos (schtasks {result.ExitCode}).");
            }

            var verification = Verify(paths, policy.TaskName, paths.BackupRunnerPath);
            if (_verify is not null)
            {
                // Test/legacy adapters supply their own verifier. Keep the
                // same observable verification event without weakening the
                // production physical-query path.
                log.TaskVerification("backup-task-verifying", policy.TaskName, verification);
            }
            if (!verification.Verified)
            {
                throw new InvalidOperationException(
                    "schtasks confirmó el registro, pero la tarea protegida de respaldos no pasó la verificación física.");
            }
        }
        finally
        {
            TryDelete(taskDefinitionPath);
        }
    }

    public static string BuildTaskXml(
        ArteInventariadoPathProvider paths,
        MachineWideBackupPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(policy);
        MachineWideBackupPolicyStore.Validate(paths, policy);

        var ns = XNamespace.Get(TaskNamespace);
        var task = new XElement(
            ns + "Task",
            new XAttribute("version", "1.4"),
            new XElement(
                ns + "RegistrationInfo",
                new XElement(ns + "Author", "ArteInventariado"),
                new XElement(ns + "Description", "Respaldo local protegido de ArteInventariado."),
                new XElement(ns + "URI", policy.TaskName)),
            new XElement(
                ns + "Triggers",
                new XElement(
                    ns + "CalendarTrigger",
                    new XElement(ns + "StartBoundary", "2024-01-01T02:00:00"),
                    new XElement(ns + "Enabled", policy.Enabled),
                    new XElement(
                        ns + "ScheduleByDay",
                        new XElement(ns + "DaysInterval", "1")))),
            new XElement(
                ns + "Principals",
                new XElement(
                    ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", policy.TaskIdentitySid),
                    new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(
                ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", false),
                new XElement(ns + "StopIfGoingOnBatteries", false),
                new XElement(ns + "AllowHardTerminate", true),
                new XElement(ns + "StartWhenAvailable", true),
                new XElement(ns + "RunOnlyIfNetworkAvailable", false),
                new XElement(ns + "IdleSettings", new XElement(ns + "StopOnIdleEnd", false)),
                new XElement(ns + "AllowStartOnDemand", true),
                new XElement(ns + "Enabled", policy.Enabled),
                new XElement(ns + "Hidden", false),
                new XElement(ns + "ExecutionTimeLimit", "PT2H"),
                new XElement(
                    ns + "RestartOnFailure",
                    new XElement(ns + "Interval", "PT15M"),
                    new XElement(ns + "Count", "3"))),
            new XElement(
                ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(
                    ns + "Exec",
                    new XElement(ns + "Command", paths.BackupRunnerPath),
                    new XElement(ns + "Arguments", "--run"),
                    new XElement(ns + "WorkingDirectory", paths.MachineInstallRoot))));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), task).ToString(SaveOptions.None);
    }

    private MachineWideTaskVerificationResult Verify(
        ArteInventariadoPathProvider paths,
        string taskName,
        string expectedCommand)
    {
        if (_verify is not null)
        {
            return _verify(paths, taskName, expectedCommand);
        }

        return MachineWideScheduledTaskVerifier.VerifyDetailed(
            paths,
            taskName,
            expectedCommand,
            CreateLog(paths),
            _execute,
            phase: "backup-task-verifying");
    }

    private MachineWideBootstrapLogWriter CreateLog(ArteInventariadoPathProvider paths) =>
        new(paths, _runId ?? Guid.NewGuid());

    private static void EnsureNoReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"La ruta de la tarea protegida contiene un reparse point: '{current}'.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
