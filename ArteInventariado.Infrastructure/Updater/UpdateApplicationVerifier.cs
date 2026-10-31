using System.Diagnostics;

namespace ArteInventariado.Infrastructure.Updater;

public interface IUpdateApplicationVerifier
{
    Task<bool> VerifyAsync(
        string executablePath,
        UpdateRuntimePaths paths,
        CancellationToken cancellationToken = default);
}

public interface IUpdateDiskSpaceProbe
{
    long GetAvailableBytes(string path);
}

public sealed class DriveUpdateDiskSpaceProbe : IUpdateDiskSpaceProbe
{
    public long GetAvailableBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new IOException("No se pudo determinar la unidad de instalacion.");
        return new DriveInfo(root).AvailableFreeSpace;
    }
}

public sealed class ProcessUpdateApplicationVerifier : IUpdateApplicationVerifier
{
    private readonly TimeSpan _timeout;

    public ProcessUpdateApplicationVerifier(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(90);
    }

    public async Task<bool> VerifyAsync(
        string executablePath,
        UpdateRuntimePaths paths,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        startInfo.Environment["ARTE_UPDATE_VERIFY"] = "1";
        if (paths.PathProvider?.IsMachineWide == true)
        {
            startInfo.Environment.Remove(LocalApplicationDataPathProvider.VerificationRootEnvironmentVariable);
            startInfo.Environment[ArteInventariadoPathProvider.VerificationInstallRootEnvironmentVariable] =
                paths.PathProvider.MachineInstallRoot;
            startInfo.Environment[ArteInventariadoPathProvider.VerificationDataRootEnvironmentVariable] =
                paths.PathProvider.MachineDataRoot;
            startInfo.Environment[ArteInventariadoPathProvider.VerificationUserRootEnvironmentVariable] =
                paths.PathProvider.UserDataRoot;
        }
        else
        {
            startInfo.Environment.Remove(ArteInventariadoPathProvider.VerificationInstallRootEnvironmentVariable);
            startInfo.Environment.Remove(ArteInventariadoPathProvider.VerificationDataRootEnvironmentVariable);
            startInfo.Environment.Remove(ArteInventariadoPathProvider.VerificationUserRootEnvironmentVariable);
            startInfo.Environment[LocalApplicationDataPathProvider.VerificationRootEnvironmentVariable] =
                paths.ApplicationDataRoot;
        }
        startInfo.Environment["ARTE_UPDATE_REDIRECTED"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo iniciar la verificacion de la nueva version.");
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(_timeout, cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            TryTerminate(process);
            throw;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
