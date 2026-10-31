using System.ComponentModel;
using System.Diagnostics;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class MachineWideUpdateDiagnosticsTests
{
    [Fact]
    public void DiagnosticsRecordStagePidTokenAndNativeCodeWithoutPersistingSecrets()
    {
        using var fixture = new Fixture();
        const string secret = "super-secret-value";
        var exception = new Win32Exception(5, $"Acceso denegado; password={secret}");

        MachineWideUpdateDiagnostics.Write(
            fixture.Paths,
            "espera-aplicacion",
            exception,
            executablePath: fixture.Paths.StableUpdaterPath,
            processId: 4321,
            tokenElevated: false,
            relatedPath: fixture.Paths.StatePath);

        var log = File.ReadAllText(MachineWideUpdateDiagnostics.GetLogPath(fixture.Paths));
        Assert.Contains("stage=espera-aplicacion", log, StringComparison.Ordinal);
        Assert.Contains("pid=4321", log, StringComparison.Ordinal);
        Assert.Contains("tokenElevated=false", log, StringComparison.Ordinal);
        Assert.Contains("nativeErrorCode=5", log, StringComparison.Ordinal);
        Assert.Contains("relatedPath=", log, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
    }

    [Fact]
    public void UacCancellationIsOnlyError1223()
    {
        Assert.True(MachineWideElevation.IsUacCancellation(new Win32Exception(1223)));
        Assert.False(MachineWideElevation.IsUacCancellation(new Win32Exception(5)));
    }

    [Fact]
    public void UacAcceptedMapsToRelaunchedElevatedWithoutRunningASecondInstaller()
    {
        var result = MachineWideElevation.MapProcessStart(Process.GetCurrentProcess);

        try
        {
            Assert.Equal(MachineWideElevationOutcome.RelaunchedElevated, result.Outcome);
            Assert.NotNull(result.Process);
        }
        finally
        {
            result.Process?.Dispose();
        }
    }

    [Fact]
    public void NonCancellationShellErrorRemainsObservable()
    {
        var error = Assert.Throws<Win32Exception>(() =>
            MachineWideElevation.MapProcessStart(
                () => throw new Win32Exception(5, "Acceso denegado")));

        Assert.Equal(5, error.NativeErrorCode);
    }

    [Fact]
    public void TokenElevationProbeDoesNotConflateMembershipAndElevation()
    {
        var available = MachineWideElevation.TryGetTokenElevation(out var elevated);

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(available);
            Assert.False(elevated);
            return;
        }

        Assert.True(available);
        Assert.IsType<bool>(elevated);
    }

    [Fact]
    public async Task MachineWideProcessWait_ObservesProcessExitWithoutOpeningProcessHandle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "/c ping 127.0.0.1 -n 3 > nul"
        });

        Assert.NotNull(child);
        await MachineWideProcessWait.WaitForProcessIdExitAsync(
            child!.Id,
            fixture.Paths,
            TimeSpan.FromSeconds(10),
            expectedExecutablePath: child.StartInfo.FileName);

        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task MachineWideProcessWait_DoesNotTreatTerminalStateAsProcessExit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        new UpdateStateStore(fixture.Paths.StatePath).Save(new ArteInventariado.Application.Models.UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            ArteInventariado.Application.Models.UpdateState.Success,
            "1.14.0",
            "1.14.1",
            Path.Combine(fixture.Root, "package.zip"),
            Path.Combine(fixture.Root, "manifest.json"),
            Path.Combine(fixture.Root, "PRE_UPDATE.aibak"),
            Path.Combine(fixture.Root, "previous", "ArteInventariado.WinForms.exe"),
            Path.Combine(fixture.Root, "current", "ArteInventariado.WinForms.exe"),
            null,
            DateTimeOffset.UtcNow));

        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "/c ping 127.0.0.1 -n 6 > nul"
        });

        Assert.NotNull(child);
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            MachineWideProcessWait.WaitForProcessIdExitAsync(
                child!.Id,
                fixture.Paths,
                TimeSpan.FromMilliseconds(250),
                expectedExecutablePath: child.StartInfo.FileName));

        Assert.Contains("PID", error.Message, StringComparison.OrdinalIgnoreCase);
        try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch { }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-update-diagnostics-tests-{Guid.NewGuid():N}");

        public UpdateRuntimePaths Paths { get; }

        public Fixture()
        {
            var provider = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(Root, "install"),
                Path.Combine(Root, "data"),
                Path.Combine(Root, "user"));
            Paths = UpdateRuntimePaths.Create(provider);
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.StableUpdaterPath)!);
            File.WriteAllText(Paths.StableUpdaterPath, "synthetic updater");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
