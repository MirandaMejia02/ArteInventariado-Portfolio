using System.Diagnostics;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Backup;

internal sealed record ProtectedBackupProcessResult(bool UacCancelled, int? ExitCode);

internal interface IProtectedBackupProcessLauncher
{
    Task<ProtectedBackupProcessResult> LaunchAndWaitAsync(
        string executablePath,
        UpdateRuntimePaths runtimePaths,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed class ProtectedBackupProcessLauncher : IProtectedBackupProcessLauncher
{
    public async Task<ProtectedBackupProcessResult> LaunchAndWaitAsync(
        string executablePath,
        UpdateRuntimePaths runtimePaths,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        if (MachineWideElevation.IsAdministrator())
        {
            process = StartNormally(executablePath, arguments);
        }
        else
        {
            var elevation = MachineWideElevation.EnsureElevated(executablePath, arguments);
            if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
            {
                return new ProtectedBackupProcessResult(UacCancelled: true, ExitCode: null);
            }

            process = elevation.Outcome == MachineWideElevationOutcome.RelaunchedElevated
                ? elevation.Process
                : StartNormally(executablePath, arguments);
        }

        if (process is null)
        {
            throw new InvalidOperationException("Windows no devolvió el proceso del BackupRunner elevado.");
        }

        using (process)
        {
            // Once UAC has accepted the request, do not abandon observation of
            // a helper that continues writing the protected backup. The UI
            // disables cancellation for this short, explicit operation.
            await MachineWideProcessWait.WaitForExitAsync(
                process,
                runtimePaths,
                TimeSpan.FromHours(2),
                CancellationToken.None).ConfigureAwait(false);
            return new ProtectedBackupProcessResult(UacCancelled: false, process.ExitCode);
        }
    }

    private static Process StartNormally(string executablePath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows no pudo iniciar el BackupRunner protegido.");
    }
}

/// <summary>
/// Application boundary for explicit manual backups in a machine-wide
/// installation. It asks the protected BackupRunner to publish the package;
/// the interactive WinForms process never writes into ProtectedBackups.
/// </summary>
public sealed class ProtectedBackupService : IProtectedBackupService
{
    private readonly ArteInventariadoPathProvider _paths;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IProtectedBackupProcessLauncher _launcher;
    private readonly Func<CancellationToken, Task>? _authorizeForTests;
    private readonly bool _allowSyntheticRoots;

    internal ProtectedBackupService(
        ArteInventariadoPathProvider paths,
        IServiceScopeFactory scopeFactory,
        IProtectedBackupProcessLauncher launcher)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    internal ProtectedBackupService(
        ArteInventariadoPathProvider paths,
        Func<CancellationToken, Task> authorize,
        IProtectedBackupProcessLauncher launcher,
        bool allowSyntheticRoots = false)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _authorizeForTests = authorize ?? throw new ArgumentNullException(nameof(authorize));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public bool IsMachineWide => _paths.IsMachineWide;

    public async Task<ProtectedBackupResult> CreateManualBackupAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsMachineWide)
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Rejected,
                Guid.Empty,
                "El respaldo protegido solo está disponible en instalaciones machine-wide.");
        }

        await EnsureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);

        var runnerPath = Path.GetFullPath(_paths.BackupRunnerPath);
        if (!MachineWideAssetPathGuard.TryGetRegularFile(
                runnerPath,
                _paths.MachineInstallRoot,
                out _,
                out var runnerIssue))
        {
            throw new FileNotFoundException(
                $"El BackupRunner protegido no es un ejecutable regular confiable ({runnerIssue}).",
                runnerPath);
        }

        var operationId = Guid.NewGuid();
        var processResult = await _launcher.LaunchAndWaitAsync(
            runnerPath,
            UpdateRuntimePaths.Create(_paths),
            BuildManualArguments(operationId),
            cancellationToken).ConfigureAwait(false);
        if (processResult.UacCancelled)
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Cancelled,
                operationId,
                "UAC fue cancelado. No se inició el respaldo protegido.");
        }

        var status = new MachineWideBackupPolicyStore().LoadStatus(_paths);
        if (status.LastOperationId != operationId ||
            status.LastBackupKind != BackupKind.Manual)
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Failed,
                operationId,
                processResult.ExitCode == 0
                    ? "El BackupRunner terminó, pero no dejó un resultado correlacionado; el respaldo no se confirma."
                    : $"El BackupRunner terminó con código {processResult.ExitCode}; no se confirmó el resultado del respaldo.");
        }

        if (processResult.ExitCode != 0 ||
            status.LastOutcome is not MachineWideBackupStatusKind.SuccessHealthy and
                not MachineWideBackupStatusKind.SuccessDegradedAssets)
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Failed,
                operationId,
                status.LastError ?? $"El BackupRunner terminó con código {processResult.ExitCode}; el respaldo no se confirmó.");
        }

        var fileName = status.LastSuccessfulFileName;
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !fileName.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase) ||
            status.LastSuccessfulSizeBytes is not > 0 ||
            string.IsNullOrWhiteSpace(status.LastSuccessfulSha256) ||
            status.LastSuccessfulSha256.Length != 64 ||
            !status.LastSuccessfulSha256.All(Uri.IsHexDigit))
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Failed,
                operationId,
                "El estado protegido no contiene una referencia y un SHA-256 válidos para el respaldo manual.");
        }

        var finalPath = Path.Combine(_paths.ProtectedBackupsRoot, fileName);
        if (!MachineWideAssetPathGuard.TryGetRegularFile(
                finalPath,
                _paths.ProtectedBackupsRoot,
                out var package,
                out var packageIssue) ||
            package is null)
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Failed,
                operationId,
                $"El archivo reportado por BackupRunner no está disponible como archivo protegido regular ({packageIssue}).");
        }

        if (package.Length != status.LastSuccessfulSizeBytes.Value)
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Failed,
                operationId,
                "El tamaño del archivo protegido no coincide con el estado correlacionado de BackupRunner.");
        }

        var observedSha256 = await BackupPackageEngine.ComputeSha256Async(finalPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(observedSha256, status.LastSuccessfulSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ProtectedBackupResult(
                ProtectedBackupOutcome.Failed,
                operationId,
                "El SHA-256 del archivo protegido no coincide con el estado correlacionado de BackupRunner.");
        }

        return new ProtectedBackupResult(
            ProtectedBackupOutcome.Succeeded,
            operationId,
            status.LastOutcome == MachineWideBackupStatusKind.SuccessDegradedAssets
                ? "Respaldo manual protegido creado y validado con advertencias de assets omitidos."
                : "Respaldo manual protegido creado y validado.",
            finalPath,
            status.LastSuccessfulSha256,
            package.Length);
    }

    internal static IReadOnlyList<string> BuildManualArguments(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de operación no puede estar vacío.", nameof(operationId));
        }

        return ["--manual", "--request-id", operationId.ToString("D")];
    }

    private async Task EnsureAuthorizedAsync(CancellationToken cancellationToken)
    {
        if (_scopeFactory is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IEmployeePermissionService>()
                .EnsurePermissionAsync(PermisoEmpleado.RESPALDO_CREAR, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_authorizeForTests is null)
        {
            throw new InvalidOperationException("No está configurada la autorización del respaldo protegido.");
        }

        await _authorizeForTests(cancellationToken).ConfigureAwait(false);
    }
}
