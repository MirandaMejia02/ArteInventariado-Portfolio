using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Tests;

public sealed class MachineWideBootstrapLogWriterTests
{
    [Fact]
    public void LogWriter_PersistsPhasesAndRedactsSensitiveDetails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado bootstrap log {Guid.NewGuid():N}");
        var target = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(root, "Program Files"),
            Path.Combine(root, "ProgramData"),
            Path.Combine(root, "UserData"));

        try
        {
            var writer = new MachineWideBootstrapLogWriter(target);
            writer.Started();
            writer.Phase("payload-validating");
            writer.Phase("completed");
            writer.IntegrityFailure(
                "current-pointer",
                Path.Combine(root, "Program Files", "ArteInventariado", "Versions", "1.13.0-dev"),
                "missing",
                "1.13.0-dev",
                Path.Combine(root, "ProgramData", "ArteInventariado", "UpdateState", "current.json"),
                new InvalidDataException("active path is not valid"));
            writer.Failed(new InvalidOperationException(
                $"password=supersecret source={Path.Combine(root, "source")}"));
            writer.Exit(1);

            var content = File.ReadAllText(writer.Path);
            Assert.Contains("RUN-START", content, StringComparison.Ordinal);
            Assert.Contains($"runId={writer.RunId:D}", content, StringComparison.Ordinal);
            Assert.Contains("PHASE | payload-validating", content, StringComparison.Ordinal);
            Assert.Contains("PHASE | bootstrap-ready-awaiting-required-tasks", content, StringComparison.Ordinal);
            Assert.Contains("INTEGRITY-FAIL", content, StringComparison.Ordinal);
            Assert.Contains("IntegrityCheck=current-pointer", content, StringComparison.Ordinal);
            Assert.Contains("Expected=<managed-path>", content, StringComparison.Ordinal);
            Assert.Contains("ActiveVersion=1.13.0-dev", content, StringComparison.Ordinal);
            Assert.Contains("ActivePath=<managed-path>", content, StringComparison.Ordinal);
            Assert.DoesNotContain("PHASE | completed", content, StringComparison.Ordinal);
            Assert.Contains("ERROR | InvalidOperationException", content, StringComparison.Ordinal);
            Assert.Contains("RUN-END | status=FAIL", content, StringComparison.Ordinal);
            Assert.Contains("EXIT | 1", content, StringComparison.Ordinal);
            Assert.All(
                content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                line => Assert.Contains($"runId={writer.RunId:D}", line, StringComparison.Ordinal));
            Assert.DoesNotContain("supersecret", content, StringComparison.Ordinal);
            Assert.DoesNotContain(root, content, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
