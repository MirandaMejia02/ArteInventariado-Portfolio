using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.StationHelper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(argument => string.Equals(argument, "--restore", StringComparison.OrdinalIgnoreCase)))
            {
                return RunRestore(ParseRestore(args));
            }

            if (args.Any(argument => string.Equals(argument, "--restore-recover", StringComparison.OrdinalIgnoreCase)))
            {
                return RunRecovery(ParseRecovery(args));
            }

            if (args.Any(argument => string.Equals(argument, "--stage-restore-candidate", StringComparison.OrdinalIgnoreCase)))
            {
                return RunStageRestoreCandidate(ParseStageRestoreCandidate(args));
            }

            var parsed = Parse(args);
            if (!MachineWideElevation.IsAdministrator())
            {
                throw new UnauthorizedAccessException(
                    "El helper protegido debe ejecutarse elevado mediante UAC.");
            }

            var paths = ArteInventariadoPathProvider.CreateMachineWide(
                parsed.InstallRoot,
                parsed.DataRoot,
                ArteInventariadoPathProvider.CreateMachineWideDefault().UserDataRoot);
            var mode = ParseMode(parsed.Mode);
            var expectedMode = string.Equals(parsed.ExpectedCurrentMode, "UNCONFIGURED", StringComparison.OrdinalIgnoreCase)
                ? (InstallationMode?)null
                : ParseMode(parsed.ExpectedCurrentMode);
            if (!Guid.TryParse(parsed.ExpectedInstallationId, out var expectedInstallationId))
            {
                throw new ArgumentException("La identidad esperada de la estacion no es valida.");
            }

            MachineWideStationConfigurationHelper.WriteMode(
                paths,
                mode,
                expectedInstallationId,
                expectedMode);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or TimeoutException or PlatformNotSupportedException)
        {
            // The caller only needs a non-zero result; details stay out of the
            // normal user-facing UI and can be diagnosed from the initiating
            // operation's error message.
            return 2;
        }
    }

    private static int RunRestore(RestoreArguments parsed)
    {
        EnsureElevated();
        WaitForParentExit(parsed.WaitPid);

        var paths = CreateMachineWidePaths(parsed.InstallRoot, parsed.DataRoot);
        var result = new ProtectedRestoreEngine(paths)
            .ExecuteAsync(
                new ProtectedRestoreRequest(
                    parsed.CandidatePath,
                    parsed.Source,
                    parsed.ExpectedBackupId,
                    parsed.ExpectedSourceInstallationId,
                    parsed.ExpectedPackageSha256,
                    parsed.ExpectedContentStatus,
                    ConfirmationAccepted: true))
            .GetAwaiter()
            .GetResult();

        Console.WriteLine($"Protected restore: {result.Outcome}. {result.Message}");
        return result.Outcome switch
        {
            ProtectedRestoreOutcome.Succeeded => 0,
            ProtectedRestoreOutcome.Failed when result.RolledBack => 3,
            _ => 4
        };
    }

    private static int RunRecovery(RecoveryArguments parsed)
    {
        EnsureElevated();
        var paths = CreateMachineWidePaths(parsed.InstallRoot, parsed.DataRoot);
        var result = new ProtectedRestoreEngine(paths)
            .RecoverPendingAsync()
            .GetAwaiter()
            .GetResult();

        if (result is null)
        {
            Console.WriteLine("Protected restore recovery: no hay operaciones pendientes.");
            return 0;
        }

        Console.WriteLine($"Protected restore recovery: {result.Outcome}. {result.Message}");
        return result.Outcome == ProtectedRestoreOutcome.Failed && result.RolledBack ? 3 :
            result.Outcome == ProtectedRestoreOutcome.RecoveryRequired ? 4 : 0;
    }

    private static int RunStageRestoreCandidate(StageRestoreCandidateArguments parsed)
    {
        EnsureElevated();
        var paths = CreateMachineWidePaths(parsed.InstallRoot, parsed.DataRoot);
        var staged = new ProtectedRestoreCandidateStager()
            .StageAsync(
                paths,
                parsed.SourcePath,
                parsed.RequestId,
                parsed.ExpectedPackageSha256)
            .GetAwaiter()
            .GetResult();

        Console.WriteLine($"Protected restore candidate staged: {staged.SizeBytes} bytes, SHA-256 {staged.Sha256}.");
        return 0;
    }

    private static void EnsureElevated()
    {
        if (!MachineWideElevation.IsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El helper protegido debe ejecutarse elevado mediante UAC.");
        }
    }

    private static ArteInventariadoPathProvider CreateMachineWidePaths(string installRoot, string dataRoot)
    {
        return ArteInventariadoPathProvider.CreateMachineWide(
            installRoot,
            dataRoot,
            ArteInventariadoPathProvider.CreateMachineWideDefault().UserDataRoot);
    }

    private static void WaitForParentExit(int? processId)
    {
        if (!processId.HasValue)
        {
            return;
        }

        if (processId.Value <= 0 || processId.Value == Environment.ProcessId)
        {
            throw new ArgumentException("El proceso de origen del restore no es valido.");
        }

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId.Value);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException("La aplicacion de origen no se cerro dentro del tiempo esperado.");
    }

    private static RestoreArguments ParseRestore(IReadOnlyList<string> args)
    {
        var values = ParseOperationArguments(
            args,
            "--restore",
            [
                "--machine-install-root", "--machine-data-root", "--candidate-path", "--source",
                "--expected-backup-id", "--expected-source-installation-id", "--expected-package-sha256",
                "--expected-content-status", "--wait-pid"
            ]);

        var source = ParseEnum<ProtectedRestoreCandidateSource>(GetRequired(values, "--source"), "la fuente del candidato");
        var candidatePath = GetRequired(values, "--candidate-path");
        if (!Path.IsPathFullyQualified(candidatePath))
        {
            throw new ArgumentException("El candidato debe ser una ruta absoluta.");
        }

        return new RestoreArguments(
            GetRequired(values, "--machine-install-root"),
            GetRequired(values, "--machine-data-root"),
            candidatePath,
            source,
            ParseOptionalGuid(values, "--expected-backup-id"),
            ParseOptionalGuid(values, "--expected-source-installation-id"),
            ParseOptionalSha256(values, "--expected-package-sha256"),
            ParseOptionalEnum<BackupContentStatus>(values, "--expected-content-status", "el estado de contenido"),
            ParseOptionalInt(values, "--wait-pid"));
    }

    private static RecoveryArguments ParseRecovery(IReadOnlyList<string> args)
    {
        var values = ParseOperationArguments(
            args,
            "--restore-recover",
            ["--machine-install-root", "--machine-data-root"]);
        return new RecoveryArguments(
            GetRequired(values, "--machine-install-root"),
            GetRequired(values, "--machine-data-root"));
    }

    private static StageRestoreCandidateArguments ParseStageRestoreCandidate(IReadOnlyList<string> args)
    {
        var values = ParseOperationArguments(
            args,
            "--stage-restore-candidate",
            ["--machine-install-root", "--machine-data-root", "--source-path", "--request-id", "--expected-package-sha256"]);
        var sourcePath = GetRequired(values, "--source-path");
        if (!Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException("El origen del candidato debe ser una ruta absoluta.");
        }

        var requestIdText = GetRequired(values, "--request-id");
        if (!Guid.TryParseExact(requestIdText, "N", out var requestId) || requestId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de importación no es válido.");
        }

        var expectedHash = ParseOptionalSha256(values, "--expected-package-sha256")
            ?? throw new ArgumentException("Falta el SHA-256 esperado.");
        return new StageRestoreCandidateArguments(
            GetRequired(values, "--machine-install-root"),
            GetRequired(values, "--machine-data-root"),
            sourcePath,
            requestId,
            expectedHash);
    }

    private static Dictionary<string, string> ParseOperationArguments(
        IReadOnlyList<string> args,
        string command,
        IEnumerable<string> valueArguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var commandSeen = false;
        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (string.Equals(name, command, StringComparison.OrdinalIgnoreCase))
            {
                if (commandSeen)
                {
                    throw new ArgumentException("El comando del helper esta duplicado.");
                }

                commandSeen = true;
                continue;
            }

            if (!valueArguments.Contains(name))
            {
                throw new ArgumentException("El helper recibió un argumento no reconocido.");
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Falta el valor de {name}.");
            }

            if (!values.TryAdd(name, args[++index]))
            {
                throw new ArgumentException($"El argumento {name} esta duplicado.");
            }
        }

        if (!commandSeen)
        {
            throw new ArgumentException("El helper no recibió una operación reconocida.");
        }

        return values;
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Falta el argumento requerido {name}.");
        }

        return value;
    }

    private static Guid? ParseOptionalGuid(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value)) return null;
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException($"El valor de {name} no es un GUID valido.");
        }

        return parsed;
    }

    private static string? ParseOptionalSha256(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value)) return null;
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"El valor de {name} no es SHA-256.");
        }

        return value;
    }

    private static int? ParseOptionalInt(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value)) return null;
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"El valor de {name} no es un entero valido.");
        }

        return parsed;
    }

    private static T? ParseOptionalEnum<T>(IReadOnlyDictionary<string, string> values, string name, string description)
        where T : struct, Enum
    {
        if (!values.TryGetValue(name, out var value)) return null;
        return ParseEnum<T>(value, description);
    }

    private static T ParseEnum<T>(string value, string description)
        where T : struct, Enum
    {
        if (!Enum.TryParse<T>(value, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException($"{description} no es valida.");
        }

        return parsed;
    }

    private static ParsedArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (string.Equals(name, MachineWideStationConfigurationHelper.WriteModeCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (!values.TryAdd(name, string.Empty))
                {
                    throw new ArgumentException("El comando del helper esta duplicado.");
                }

                continue;
            }

            if (name is not "--mode" and not "--machine-install-root" and not "--machine-data-root" and
                not "--expected-installation-id" and not "--expected-current-mode")
            {
                throw new ArgumentException("El helper recibió un argumento no reconocido.");
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Falta el valor de {name}.");
            }

            if (!values.TryAdd(name, args[++index]))
            {
                throw new ArgumentException($"El argumento {name} esta duplicado.");
            }
        }

        if (!values.ContainsKey(MachineWideStationConfigurationHelper.WriteModeCommand) ||
            !values.TryGetValue("--mode", out var mode) ||
            !values.TryGetValue("--machine-install-root", out var installRoot) ||
            !values.TryGetValue("--machine-data-root", out var dataRoot) ||
            !values.TryGetValue("--expected-installation-id", out var expectedId) ||
            !values.TryGetValue("--expected-current-mode", out var expectedMode))
        {
            throw new ArgumentException("El helper no recibió todos los argumentos protegidos requeridos.");
        }

        return new ParsedArguments(mode, installRoot, dataRoot, expectedId, expectedMode);
    }

    private static InstallationMode ParseMode(string value)
    {
        if (!Enum.TryParse<InstallationMode>(value, ignoreCase: false, out var mode) || !Enum.IsDefined(mode))
        {
            throw new ArgumentException("El modo de la estacion no es valido.");
        }

        return mode;
    }

    private sealed record ParsedArguments(
        string Mode,
        string InstallRoot,
        string DataRoot,
        string ExpectedInstallationId,
        string ExpectedCurrentMode);

    private sealed record RestoreArguments(
        string InstallRoot,
        string DataRoot,
        string CandidatePath,
        ProtectedRestoreCandidateSource Source,
        Guid? ExpectedBackupId,
        Guid? ExpectedSourceInstallationId,
        string? ExpectedPackageSha256,
        BackupContentStatus? ExpectedContentStatus,
        int? WaitPid);

    private sealed record RecoveryArguments(string InstallRoot, string DataRoot);

    private sealed record StageRestoreCandidateArguments(
        string InstallRoot,
        string DataRoot,
        string SourcePath,
        Guid RequestId,
        string ExpectedPackageSha256);
}
