using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.CloudBackup;

public interface IMachineWideCloudBackupTaskScheduler
{
    void EnsureRegistered(ArteInventariadoPathProvider paths);

    /// <summary>
    /// Reads the task back from Windows and confirms the protected SYSTEM
    /// contract. Implementations used by legacy callers may return false;
    /// the installer treats that as not-ready and repairs the task.
    /// </summary>
    bool IsRegisteredCorrectly(ArteInventariadoPathProvider paths) => false;
}

/// <summary>
/// Registers the independent SYSTEM cloud transport task. It has no network
/// prerequisite: StartWhenAvailable lets the runner retry after an offline
/// period, while the runner itself remains a no-op when cloud provisioning is
/// disabled.
/// </summary>
public sealed class MachineWideCloudBackupTaskScheduler : IMachineWideCloudBackupTaskScheduler
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly Func<ProcessStartInfo, MachineWideTaskSchedulerProcessResult> _execute;
    private readonly Func<ArteInventariadoPathProvider, string, string, MachineWideTaskVerificationResult>? _verify;
    private readonly bool _allowSyntheticRoots;
    private readonly Guid? _runId;

    public MachineWideCloudBackupTaskScheduler()
        : this(MachineWideTaskSchedulerProcess.Execute, verify: null, allowSyntheticRoots: false, runId: null)
    {
    }

    public MachineWideCloudBackupTaskScheduler(Guid runId)
        : this(MachineWideTaskSchedulerProcess.Execute, verify: null, allowSyntheticRoots: false, runId)
    {
    }

    internal MachineWideCloudBackupTaskScheduler(
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

    internal MachineWideCloudBackupTaskScheduler(
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

    internal MachineWideCloudBackupTaskScheduler(
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

    internal MachineWideCloudBackupTaskScheduler(
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

    public bool IsRegisteredCorrectly(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide || !File.Exists(paths.CloudBackupRunnerPath))
        {
            return false;
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, _allowSyntheticRoots);
        return Verify(paths, DefaultTaskName, paths.CloudBackupRunnerPath).Verified;
    }

    public void EnsureRegistered(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide)
        {
            throw new InvalidOperationException("La tarea cloud protegida requiere el layout machine-wide.");
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, _allowSyntheticRoots);
        if (!File.Exists(paths.CloudBackupRunnerPath))
        {
            throw new FileNotFoundException(
                "La instalación protegida no contiene ArteInventariado.CloudBackupRunner.exe.",
                paths.CloudBackupRunnerPath);
        }

        var definitionPath = Path.Combine(
            paths.MachineInstallRoot,
            $".cloud-backup-task-{Guid.NewGuid():N}.xml");
        EnsureNoReparsePoints(paths.MachineInstallRoot);
        try
        {
            File.WriteAllText(
                definitionPath,
                BuildTaskXml(paths),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
            startInfo.ArgumentList.Add(DefaultTaskName);
            startInfo.ArgumentList.Add("/RU");
            startInfo.ArgumentList.Add("SYSTEM");
            startInfo.ArgumentList.Add("/XML");
            startInfo.ArgumentList.Add(definitionPath);
            startInfo.ArgumentList.Add("/F");
            var log = CreateLog(paths);
            log.Phase("cloud-task-registering");
            log.TaskCreateBegin("cloud-task-registering", DefaultTaskName);
            var result = _execute(startInfo);
            log.TaskCreateResult("cloud-task-registering", DefaultTaskName, result);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"No se pudo registrar la tarea cloud protegida (schtasks {result.ExitCode}).");
            }

            var verification = Verify(paths, DefaultTaskName, paths.CloudBackupRunnerPath);
            if (_verify is not null)
            {
                log.TaskVerification("cloud-task-verifying", DefaultTaskName, verification);
            }
            if (!verification.Verified)
            {
                throw new InvalidOperationException(
                    "schtasks confirmó el registro, pero la tarea cloud protegida no pasó la verificación física.");
            }
        }
        finally
        {
            TryDelete(definitionPath);
        }
    }

    public const string DefaultTaskName = "\\ArteInventariado\\CloudBackupProtegido";

    public static string BuildTaskXml(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide)
        {
            throw new InvalidOperationException("La tarea cloud protegida requiere el layout machine-wide.");
        }

        var ns = XNamespace.Get(TaskNamespace);
        var task = new XElement(
            ns + "Task",
            new XAttribute("version", "1.4"),
            new XElement(
                ns + "RegistrationInfo",
                new XElement(ns + "Author", "ArteInventariado"),
                new XElement(ns + "Description", "Transporte cloud protegido de respaldos ArteInventariado."),
                new XElement(ns + "URI", DefaultTaskName)),
            new XElement(
                ns + "Triggers",
                new XElement(
                    ns + "CalendarTrigger",
                    new XElement(ns + "StartBoundary", "2024-01-01T02:30:00"),
                    new XElement(ns + "Enabled", true),
                    new XElement(
                        ns + "ScheduleByDay",
                        new XElement(ns + "DaysInterval", "1")))),
            new XElement(
                ns + "Principals",
                new XElement(
                    ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", MachineWideBackupPolicyStore.LocalSystemSid),
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
                new XElement(ns + "Enabled", true),
                new XElement(ns + "Hidden", false),
                new XElement(ns + "ExecutionTimeLimit", "PT4H"),
                new XElement(
                    ns + "RestartOnFailure",
                    new XElement(ns + "Interval", "PT15M"),
                    new XElement(ns + "Count", "3"))),
            new XElement(
                ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(
                    ns + "Exec",
                    new XElement(ns + "Command", paths.CloudBackupRunnerPath),
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
            phase: "cloud-task-verifying");
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
                throw new InvalidDataException($"La ruta de la tarea cloud contiene un reparse point: '{current}'.");
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
