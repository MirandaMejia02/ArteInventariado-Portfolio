using System.Diagnostics;
using System.Xml.Linq;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Tests;

public sealed class MachineWideScheduledTaskVerifierTests
{
    [Fact]
    public void VerificationUsesFreshQueryProcessAndRequiresTheRequestedTaskUri()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        var calls = 0;
        var log = new MachineWideBootstrapLogWriter(fixture.Paths, Guid.NewGuid());
        var result = MachineWideScheduledTaskVerifier.VerifyDetailed(
            fixture.Paths,
            fixture.TaskName,
            fixture.RunnerPath,
            log,
            _ =>
            {
                calls++;
                return new MachineWideTaskSchedulerProcessResult(
                    0,
                    calls == 1
                        ? fixture.TaskXml(fixture.TaskName)
                        : fixture.TaskXml("\\ArteInventariado\\OtherTask"),
                    string.Empty);
            },
            "verifier-regression");

        Assert.True(result.Verified);
        var second = MachineWideScheduledTaskVerifier.VerifyDetailed(
            fixture.Paths,
            fixture.TaskName,
            fixture.RunnerPath,
            log,
            _ =>
            {
                calls++;
                return new MachineWideTaskSchedulerProcessResult(
                    0,
                    fixture.TaskXml("\\ArteInventariado\\OtherTask"),
                    string.Empty);
            },
            "verifier-regression");

        Assert.False(second.Verified);
        Assert.Equal(2, calls);
        var content = File.ReadAllText(log.Path);
        Assert.Contains("TASK-QUERY-BEGIN", content, StringComparison.Ordinal);
        Assert.Contains("TASK-QUERY-RESULT", content, StringComparison.Ordinal);
        Assert.Contains("TASK-VERIFY", content, StringComparison.Ordinal);
        Assert.Contains($"runId={log.RunId:D}", content, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationRejectsQueryFailureEmptyOutputMalformedXmlAndWrongPrincipal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        var scenarios = new[]
        {
            new MachineWideTaskSchedulerProcessResult(1, string.Empty, "ERROR: task not found"),
            new MachineWideTaskSchedulerProcessResult(0, string.Empty, string.Empty),
            new MachineWideTaskSchedulerProcessResult(0, "not xml", string.Empty),
            new MachineWideTaskSchedulerProcessResult(
                0,
                fixture.TaskXml(fixture.TaskName, principalSid: "S-1-5-19"),
                string.Empty)
        };

        foreach (var scenario in scenarios)
        {
            var result = MachineWideScheduledTaskVerifier.VerifyDetailed(
                fixture.Paths,
                fixture.TaskName,
                fixture.RunnerPath,
                execute: _ => scenario);
            Assert.False(result.Verified);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-task-verifier-{Guid.NewGuid():N}");

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            Paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(_root, "Install"),
                Path.Combine(_root, "Data"),
                Path.Combine(_root, "User"));
            Directory.CreateDirectory(Paths.MachineInstallRoot);
            Directory.CreateDirectory(Paths.MachineDataRoot);
            Directory.CreateDirectory(Paths.UserDataRoot);
            Directory.CreateDirectory(Paths.StationRoot);
            Directory.CreateDirectory(Paths.StationOperationalRoot);
            Directory.CreateDirectory(Paths.UpdateStateRoot);
            Directory.CreateDirectory(Paths.UpdateLogsRoot);
            RunnerPath = Path.Combine(Paths.MachineInstallRoot, "runner.exe");
            File.WriteAllText(RunnerPath, "runner");
        }

        public ArteInventariadoPathProvider Paths { get; }

        public string RunnerPath { get; }

        public string TaskName => "\\ArteInventariado\\BackupProtegido";

        public string TaskXml(string taskName, string principalSid = "S-1-5-18")
        {
            const string nsValue = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var ns = XNamespace.Get(nsValue);
            var task = new XElement(
                ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(
                    ns + "RegistrationInfo",
                    new XElement(ns + "URI", taskName)),
                new XElement(
                    ns + "Principals",
                    new XElement(
                        ns + "Principal",
                        new XElement(ns + "UserId", principalSid),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(
                    ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(
                        ns + "RestartOnFailure",
                        new XElement(ns + "Interval", "PT15M"),
                        new XElement(ns + "Count", "3"))),
                new XElement(
                    ns + "Actions",
                    new XElement(
                        ns + "Exec",
                        new XElement(ns + "Command", RunnerPath),
                        new XElement(ns + "Arguments", "--run"),
                        new XElement(ns + "WorkingDirectory", Paths.MachineInstallRoot))));
            return new XDocument(task).ToString(SaveOptions.None);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
