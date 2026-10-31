using System.Text;

namespace ArteInventariado.Infrastructure.Updater;

/// <summary>
/// Synthetic, side-effect-free smoke for the machine-wide updater boundary.
/// It exercises temporary roots and command construction without touching the
/// real Program Files/ProgramData layout or invoking UAC.
/// </summary>
public static class MachineWideUpdaterSmokeHarness
{
    public static int Run(string? outputPath = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado-machine-update-smoke-{Guid.NewGuid():N}");
        var evidence = outputPath is null ? null : Path.GetFullPath(outputPath);
        var lines = new List<string>();
        try
        {
            var provider = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"),
                Path.Combine(root, "user"));
            var paths = UpdateRuntimePaths.Create(provider);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.StableUpdaterPath)!);
            File.WriteAllText(paths.StableUpdaterPath, "synthetic updater", Encoding.UTF8);
            var handoff = Path.Combine(paths.StagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(handoff);
            var expectedApplication = Path.Combine(
                paths.GetVersionDirectory("1.14.0"),
                UpdateLaunchTargetResolver.ApplicationExecutableName);
            Directory.CreateDirectory(Path.GetDirectoryName(expectedApplication)!);
            File.WriteAllText(expectedApplication, "synthetic application", Encoding.UTF8);

            var arguments = new[]
            {
                "--no-relaunch",
                "--state", paths.StatePath,
                "--wait-pid", "1234",
                "--machine-install-root", provider.MachineInstallRoot,
                "--machine-data-root", provider.MachineDataRoot,
                "--user-data-root", provider.UserDataRoot,
                "--handoff", handoff,
                "--wait-executable", expectedApplication
            };
            MachineWideUpdateGuard.ValidateUpdaterArguments(arguments, paths, allowSyntheticRoots: true);
            var startInfo = UpdateLauncher.CreateUpdaterStartInfo(
                paths,
                paths.StableUpdaterPath,
                ["--no-relaunch", "--state", paths.StatePath, "--wait-pid", "1234", "--handoff", handoff, "--wait-executable", expectedApplication]);
            Require(startInfo.UseShellExecute, "machine-wide no usa ShellExecute.");
            Require(string.Equals(startInfo.Verb, "runas", StringComparison.OrdinalIgnoreCase), "machine-wide no usa Verb=runas.");
            Require(startInfo.ArgumentList.Contains("--machine-install-root"), "faltan raíces explícitas en el arranque.");
            Require(startInfo.ArgumentList.Contains(handoff), "falta el handoff de staging en el arranque.");
            Require(startInfo.ArgumentList.Contains(expectedApplication), "falta el ejecutable esperado del handoff.");
            Require(!startInfo.ArgumentList.Any(argument => argument.Contains("&", StringComparison.Ordinal)), "se detectó una concatenación insegura de argumentos.");
            lines.Add("runas/UseShellExecute/ArgumentList=OK");

            var traversal = arguments.ToArray();
            traversal[Array.IndexOf(traversal, "--state") + 1] = Path.Combine(root, "..", "outside", "state.json");
            AssertRejected(() => MachineWideUpdateGuard.ValidateUpdaterArguments(traversal, paths, allowSyntheticRoots: true), "state traversal");

            var unknown = arguments.Append("--execute").ToArray();
            AssertRejected(() => MachineWideUpdateGuard.ValidateUpdaterArguments(unknown, paths, allowSyntheticRoots: true), "argumento no reconocido");

            var outsideHandoff = arguments.ToArray();
            outsideHandoff[Array.IndexOf(outsideHandoff, "--handoff") + 1] = Path.Combine(root, "outside");
            AssertRejected(() => MachineWideUpdateGuard.ValidateUpdaterArguments(outsideHandoff, paths, allowSyntheticRoots: true), "handoff fuera de UserDataRoot");

            var wrongExecutable = Path.Combine(root, "other.exe");
            File.WriteAllText(wrongExecutable, "not the stable updater", Encoding.UTF8);
            AssertRejected(() => UpdateLauncher.CreateUpdaterStartInfo(paths, wrongExecutable, []), "ejecutable fuera del layout");
            lines.Add("boundary/traversal/executable=OK");

            WriteEvidence(evidence, lines, "OK");
            return 0;
        }
        catch (Exception ex)
        {
            lines.Add($"ERROR={ex.Message.ReplaceLineEndings(" ")}");
            WriteEvidence(evidence, lines, "FAILED");
            return 1;
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

    private static void AssertRejected(Action action, string scenario)
    {
        var rejected = false;
        try
        {
            action();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            rejected = true;
        }

        if (!rejected)
        {
            throw new InvalidDataException($"El smoke aceptó {scenario}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static void WriteEvidence(string? path, IReadOnlyList<string> lines, string result)
    {
        if (path is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, [$"RESULT={result}", .. lines]);
    }
}
