using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class MachineWideElevationTests
{
    [Fact]
    public void RunasStartInfoUsesValidatedArgumentsAndNoEnvironmentOverride()
    {
        var executable = Path.Combine(Path.GetTempPath(), "ArteInventariado-elevation-test.exe");
        var arguments = new[]
        {
            "--machine-install-root",
            Path.Combine(Path.GetTempPath(), "Program Files", "ArteInventariado"),
            "--machine-data-root",
            Path.Combine(Path.GetTempPath(), "ProgramData", "ArteInventariado"),
            "--provision-principal"
        };

        var startInfo = MachineWideElevation.CreateElevatedStartInfo(executable, arguments);

        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb, ignoreCase: true);
        Assert.Equal(Path.GetFullPath(executable), startInfo.FileName);
        Assert.Equal(arguments, startInfo.ArgumentList);
    }

    [Fact]
    public void RunasBoundaryRejectsEnvironmentOverridesBeforeProcessStart()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ARTE_PROVISION_PRINCIPAL"] = "1"
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            MachineWideElevation.CreateElevatedStartInfo(
                Path.Combine(Path.GetTempPath(), "missing-elevation-test.exe"),
                ["--provision-principal"],
                environment));

        Assert.Contains("variables de entorno", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureElevatedRejectsEnvironmentOverridesBeforeExecutableValidation()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ARTE_UPDATE_REDIRECTED"] = "1"
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            MachineWideElevation.EnsureElevated(
                Path.Combine(Path.GetTempPath(), "missing-elevation-test.exe"),
                [],
                environment));

        Assert.Contains("runas", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MachineWideUpdaterStartInfoRejectsEnvironmentOverrides()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-updater-uac-{Guid.NewGuid():N}");
        var provider = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(root, "Program Files", "ArteInventariado"),
            Path.Combine(root, "ProgramData", "ArteInventariado"),
            Path.Combine(root, "UserData", "ArteInventariado"));
        var paths = UpdateRuntimePaths.Create(provider);
        Directory.CreateDirectory(provider.MachineInstallRoot);
        File.WriteAllText(provider.UpdaterPath, "synthetic updater");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                UpdateLauncher.CreateUpdaterStartInfo(
                    paths,
                    provider.UpdaterPath,
                    ["--recover", "--no-relaunch"],
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ARTE_UPDATE_REDIRECTED"] = "1"
                    }));

            Assert.Contains("variables de entorno", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
