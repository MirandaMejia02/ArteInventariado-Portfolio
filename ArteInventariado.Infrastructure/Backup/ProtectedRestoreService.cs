using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Installation;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Application-facing boundary for the protected machine-wide restore.
/// Authorization and UAC orchestration happen here; the destructive work is
/// performed only by the signed, narrowly-scoped StationHelper process.
/// </summary>
public sealed class ProtectedRestoreService : IProtectedRestoreService
{
    private readonly ArteInventariadoPathProvider _paths;
    private readonly ICloudRecoveryAuthorizationService? _authorization;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly Func<string, CancellationToken, Task>? _reauthenticateForTests;
    private readonly Func<string, IReadOnlyList<string>, MachineWideElevationResult> _ensureElevated;
    private readonly IProtectedRestoreCandidateStageLauncher _stageLauncher;
    private readonly Func<CancellationToken, Task>? _authorizeStageForTests;
    private readonly Func<string, bool> _isSupportedLocalVolume;
    private readonly bool _allowSyntheticRoots;

    public ProtectedRestoreService(
        ArteInventariadoPathProvider paths,
        IServiceScopeFactory scopeFactory)
        : this(
            paths,
            null,
            (executable, arguments) => MachineWideElevation.EnsureElevated(executable, arguments),
            false,
            scopeFactory,
            stageLauncher: new ProtectedRestoreCandidateStageLauncher())
    {
    }

    internal ProtectedRestoreService(
        ArteInventariadoPathProvider paths,
        ICloudRecoveryAuthorizationService? authorization,
        Func<string, IReadOnlyList<string>, MachineWideElevationResult> ensureElevated,
        bool allowSyntheticRoots = false,
        IServiceScopeFactory? scopeFactory = null,
        Func<string, CancellationToken, Task>? reauthenticateForTests = null,
        IProtectedRestoreCandidateStageLauncher? stageLauncher = null,
        Func<CancellationToken, Task>? authorizeStageForTests = null,
        Func<string, bool>? isSupportedLocalVolume = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        if (authorization is null && scopeFactory is null)
        {
            throw new ArgumentNullException(nameof(authorization));
        }

        _authorization = authorization;
        _scopeFactory = scopeFactory;
        _reauthenticateForTests = reauthenticateForTests;
        _ensureElevated = ensureElevated ?? throw new ArgumentNullException(nameof(ensureElevated));
        _stageLauncher = stageLauncher ?? new ProtectedRestoreCandidateStageLauncher();
        _authorizeStageForTests = authorizeStageForTests;
        _isSupportedLocalVolume = isSupportedLocalVolume ?? ProtectedRestoreCandidateStager.IsSupportedLocalVolume;
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public bool IsMachineWide => _paths.IsMachineWide;

    public async Task<ProtectedRestoreCandidateStageResult> StageLocalCandidateAsync(
        string sourcePath,
        string expectedPackageSha256,
        CancellationToken cancellationToken = default)
    {
        if (!IsMachineWide)
        {
            return new ProtectedRestoreCandidateStageResult(
                ProtectedRestoreOutcome.Rejected,
                null,
                null,
                null,
                null,
                "La importacion protegida solo esta disponible en instalaciones machine-wide.");
        }

        await EnsureStageAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);
        ValidateExpectedSha256(expectedPackageSha256);

        var candidate = Path.GetFullPath((sourcePath ?? string.Empty).Trim());
        if (!candidate.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El archivo seleccionado debe tener extension .aibak.");
        }

        var managed = TryResolveManagedCandidate(candidate);
        if (managed.HasValue)
        {
            var existing = managed.Value;
            if (!MachineWideAssetPathGuard.TryGetRegularFile(
                    candidate,
                    existing.Root,
                    out var file,
                    out var issue) || file is null || file.Length <= 0)
            {
                throw new InvalidDataException($"El respaldo administrado no es un archivo regular disponible ({issue}).");
            }

            var managedHash = await BackupPackageEngine.ComputeSha256Async(candidate, cancellationToken).ConfigureAwait(false);
            if (!FixedEquals(managedHash, expectedPackageSha256))
            {
                throw new InvalidDataException("El respaldo cambió después de ser validado; vuelve a seleccionarlo.");
            }

            return new ProtectedRestoreCandidateStageResult(
                ProtectedRestoreOutcome.Succeeded,
                existing.Source,
                candidate,
                managedHash,
                file.Length,
                "El respaldo ya estaba en una raíz protegida; no fue necesario copiarlo.");
        }

        var source = ProtectedRestoreCandidateStager.ValidateSourcePathSyntax(sourcePath ?? string.Empty);
        if (!_isSupportedLocalVolume(source))
        {
            return new ProtectedRestoreCandidateStageResult(
                ProtectedRestoreOutcome.Rejected,
                null,
                null,
                null,
                null,
                "Selecciona el .aibak desde una unidad local o USB disponible. No se aceptan rutas de red.");
        }

        var sourceRoot = Path.GetPathRoot(source)
            ?? throw new InvalidDataException("No se pudo determinar la unidad del archivo seleccionado.");
        if (!MachineWideAssetPathGuard.TryGetRegularFile(source, sourceRoot, out var sourceFile, out var sourceIssue) ||
            sourceFile is null || sourceFile.Length <= 0)
        {
            throw new InvalidDataException($"El .aibak seleccionado no es un archivo local regular ({sourceIssue}).");
        }

        var sourceHash = await BackupPackageEngine.ComputeSha256Async(source, cancellationToken).ConfigureAwait(false);
        if (!FixedEquals(sourceHash, expectedPackageSha256))
        {
            throw new InvalidDataException("El archivo cambió después de validarlo; vuelve a seleccionarlo.");
        }

        var helper = ResolveHelperPath();
        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);
        var requestId = Guid.NewGuid();
        var stagedPath = ResolveStagedCandidatePath(requestId);
        var partialPath = Path.ChangeExtension(stagedPath, ".partial");
        if (File.Exists(stagedPath) || Directory.Exists(stagedPath) || File.Exists(partialPath) || Directory.Exists(partialPath))
        {
            throw new IOException("La ruta de importación ya existe; no se sobrescribió ningún archivo.");
        }

        var launch = await _stageLauncher.LaunchAndWaitAsync(
            helper,
            ArteInventariado.Infrastructure.Updater.UpdateRuntimePaths.Create(_paths),
            BuildStageArguments(requestId, source, expectedPackageSha256),
            cancellationToken).ConfigureAwait(false);
        if (launch.UacCancelled)
        {
            return new ProtectedRestoreCandidateStageResult(
                ProtectedRestoreOutcome.Cancelled,
                null,
                null,
                null,
                null,
                "UAC fue cancelado. No se importó el archivo y la base de datos no fue modificada.");
        }

        if (!MachineWideAssetPathGuard.TryGetRegularFile(
                stagedPath,
                _paths.LocalRestoreStagingRoot,
                out var stagedFile,
                out var stagedIssue) || stagedFile is null || stagedFile.Length <= 0)
        {
            throw new InvalidDataException(
                $"El helper terminó sin publicar una copia protegida completa ({stagedIssue}). La base de datos no fue modificada.");
        }

        var stagedHash = await BackupPackageEngine.ComputeSha256Async(stagedPath, CancellationToken.None).ConfigureAwait(false);
        if (!FixedEquals(stagedHash, expectedPackageSha256) || stagedFile.Length != sourceFile.Length)
        {
            throw new InvalidDataException("La copia protegida no coincide con el archivo validado; no puede restaurarse.");
        }

        return new ProtectedRestoreCandidateStageResult(
            ProtectedRestoreOutcome.Succeeded,
            ProtectedRestoreCandidateSource.LocalStagedBackup,
            stagedPath,
            stagedHash,
            stagedFile.Length,
            "Importación protegida completada. La base de datos sigue intacta; confirma Restaurar para iniciar el restore.");
    }

    public async Task<ProtectedRestoreResult> RestoreAsync(
        ProtectedRestoreRequest request,
        string reauthenticationPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsMachineWide)
        {
            return new ProtectedRestoreResult(
                Guid.Empty,
                ProtectedRestoreOutcome.Rejected,
                "La restauracion protegida solo esta disponible en instalaciones machine-wide.");
        }

        ValidateLaunchRequest(request, reauthenticationPassword);
        await EnsureAuthorizedAsync(reauthenticationPassword, cancellationToken);
        ValidateCandidatePath(request);
        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);

        var elevation = _ensureElevated(
            ResolveHelperPath(),
            BuildRestoreArguments(request));
        return elevation.Outcome switch
        {
            MachineWideElevationOutcome.RelaunchedElevated => new ProtectedRestoreResult(
                Guid.Empty,
                ProtectedRestoreOutcome.RelaunchedElevated,
                "Se solicito UAC para completar la restauracion protegida. La aplicacion se cerrara mientras el helper valida y aplica el respaldo.",
                RestartRequired: true),
            MachineWideElevationOutcome.Cancelled => new ProtectedRestoreResult(
                Guid.Empty,
                ProtectedRestoreOutcome.Cancelled,
                "La restauracion protegida fue cancelada en UAC. La instalacion actual no fue modificada."),
            MachineWideElevationOutcome.AlreadyElevated => new ProtectedRestoreResult(
                Guid.Empty,
                ProtectedRestoreOutcome.Rejected,
                "El flujo protegido debe ejecutarse mediante el helper dedicado; la aplicacion no reemplaza la base en proceso."),
            _ => throw new InvalidOperationException("Resultado UAC no reconocido.")
        };
    }

    public Task<ProtectedRestoreRecoveryStatus> GetRecoveryStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsMachineWide)
        {
            return Task.FromResult(new ProtectedRestoreRecoveryStatus(false, null, null, null));
        }

        var store = new ProtectedRestoreJournalStore(_paths.RecoveryRoot);
        foreach (var operationDirectory in store.EnumerateOperationDirectories())
        {
            try
            {
                var journal = store.Load(operationDirectory);
                if (journal.Phase is ProtectedRestoreJournalPhase.Succeeded or ProtectedRestoreJournalPhase.RolledBack)
                {
                    continue;
                }

                var problem = journal.Phase == ProtectedRestoreJournalPhase.RecoveryRequired
                    ? journal.LastError ?? "La restauracion requiere intervencion protegida."
                    : null;
                return Task.FromResult(new ProtectedRestoreRecoveryStatus(
                    true,
                    journal.OperationId,
                    journal.Phase,
                    problem));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                return Task.FromResult(new ProtectedRestoreRecoveryStatus(
                    true,
                    null,
                    null,
                    "Existe un journal de restore no legible; no se iniciara una aplicacion automatica."));
            }
        }

        return Task.FromResult(new ProtectedRestoreRecoveryStatus(false, null, null, null));
    }

    public async Task<ProtectedRestoreResult?> RecoverPendingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsMachineWide)
        {
            return null;
        }

        var status = await GetRecoveryStatusAsync(cancellationToken);
        if (!status.Pending)
        {
            return null;
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);
        var elevation = _ensureElevated(
            ResolveHelperPath(),
            [
                "--restore-recover",
                "--machine-install-root", _paths.MachineInstallRoot,
                "--machine-data-root", _paths.MachineDataRoot
            ]);
        return elevation.Outcome switch
        {
            MachineWideElevationOutcome.RelaunchedElevated => new ProtectedRestoreResult(
                status.OperationId ?? Guid.Empty,
                ProtectedRestoreOutcome.RelaunchedElevated,
                "Se solicito UAC para recuperar de forma determinista el restore incompleto.",
                RestartRequired: true),
            MachineWideElevationOutcome.Cancelled => new ProtectedRestoreResult(
                status.OperationId ?? Guid.Empty,
                ProtectedRestoreOutcome.Cancelled,
                "La recuperacion fue cancelada en UAC. No se reemplazo la instalacion actual."),
            MachineWideElevationOutcome.AlreadyElevated => new ProtectedRestoreResult(
                status.OperationId ?? Guid.Empty,
                ProtectedRestoreOutcome.Rejected,
                "La recuperacion debe ejecutarse mediante el helper dedicado."),
            _ => throw new InvalidOperationException("Resultado UAC no reconocido.")
        };
    }

    internal IReadOnlyList<string> BuildRestoreArguments(ProtectedRestoreRequest request)
    {
        var arguments = new List<string>
        {
            "--restore",
            "--machine-install-root", _paths.MachineInstallRoot,
            "--machine-data-root", _paths.MachineDataRoot,
            "--candidate-path", Path.GetFullPath(request.CandidatePath.Trim()),
            "--source", request.Source.ToString()
        };

        AddOptional(arguments, "--expected-backup-id", request.ExpectedBackupId?.ToString("D"));
        AddOptional(arguments, "--expected-source-installation-id", request.ExpectedSourceInstallationId?.ToString("D"));
        AddOptional(arguments, "--expected-package-sha256", request.ExpectedPackageSha256);
        AddOptional(arguments, "--expected-content-status", request.ExpectedContentStatus?.ToString());
        AddOptional(arguments, "--wait-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return arguments;
    }

    internal IReadOnlyList<string> BuildStageArguments(Guid requestId, string sourcePath, string expectedPackageSha256)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de importación no es válido.", nameof(requestId));
        }

        ValidateExpectedSha256(expectedPackageSha256);
        var source = Path.GetFullPath(sourcePath.Trim());
        return
        [
            "--stage-restore-candidate",
            "--machine-install-root", _paths.MachineInstallRoot,
            "--machine-data-root", _paths.MachineDataRoot,
            "--source-path", source,
            "--request-id", requestId.ToString("N"),
            "--expected-package-sha256", expectedPackageSha256.ToUpperInvariant()
        ];
    }

    internal string ResolveStagedCandidatePath(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de importación no es válido.", nameof(requestId));
        }

        return Path.Combine(_paths.LocalRestoreStagingRoot, $"{requestId:N}.aibak");
    }

    private string ResolveHelperPath()
    {
        var helper = Path.GetFullPath(_paths.StationHelperPath);
        if (!MachineWideAssetPathGuard.TryGetRegularFile(helper, _paths.MachineInstallRoot, out _, out var issue))
        {
            throw new FileNotFoundException($"No existe el helper protegido ({issue}).", helper);
        }

        return helper;
    }

    private async Task EnsureAuthorizedAsync(
        string reauthenticationPassword,
        CancellationToken cancellationToken)
    {
        if (_scopeFactory is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<ICloudRecoveryAuthorizationService>()
                .EnsureAuthorizedAsync(cancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<IEmployeePermissionService>()
                .EnsurePermissionAsync(PermisoEmpleado.RESPALDO_RESTAURAR, cancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<IAdminReauthenticationService>()
                .VerifyCurrentAdminAsync(reauthenticationPassword, cancellationToken);
            return;
        }

        if (_authorization is null || _reauthenticateForTests is null)
        {
            throw new InvalidOperationException("La autorización y reautenticación del restore protegido no están configuradas.");
        }

        await _authorization.EnsureAuthorizedAsync(cancellationToken);
        await _reauthenticateForTests(reauthenticationPassword, cancellationToken);
    }

    private async Task EnsureStageAuthorizedAsync(CancellationToken cancellationToken)
    {
        if (_scopeFactory is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<ICloudRecoveryAuthorizationService>()
                .EnsureAuthorizedAsync(cancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<IEmployeePermissionService>()
                .EnsurePermissionAsync(PermisoEmpleado.RESPALDO_RESTAURAR, cancellationToken);
            return;
        }

        if (_authorizeStageForTests is null)
        {
            throw new InvalidOperationException("La autorización de importación protegida no está configurada.");
        }

        await _authorizeStageForTests(cancellationToken);
    }

    private (ProtectedRestoreCandidateSource Source, string Root)? TryResolveManagedCandidate(string candidate)
    {
        var protectedRoot = Path.GetFullPath(_paths.ProtectedBackupsRoot);
        if (MachineWideAssetPathGuard.IsWithin(candidate, protectedRoot) &&
            PathEquals(Path.GetDirectoryName(candidate), protectedRoot))
        {
            return (ProtectedRestoreCandidateSource.LocalProtectedBackup, protectedRoot);
        }

        var cloudRoot = Path.GetFullPath(_paths.CloudRecoveryStagingRoot);
        if (MachineWideAssetPathGuard.IsWithin(candidate, cloudRoot))
        {
            return (ProtectedRestoreCandidateSource.CloudRecoveryCandidate, cloudRoot);
        }

        return null;
    }

    private static void ValidateExpectedSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("El SHA-256 esperado debe tener 64 dígitos hexadecimales.");
        }
    }

    private static bool FixedEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

    private static bool PathEquals(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateLaunchRequest(
        ProtectedRestoreRequest request,
        string reauthenticationPassword)
    {
        if (!request.ConfirmationAccepted)
        {
            throw new InvalidOperationException("Debe confirmar el reemplazo de la base de datos actual.");
        }

        if (string.IsNullOrWhiteSpace(reauthenticationPassword))
        {
            throw new ArgumentException("La contraseña actual del Administrador Principal es obligatoria.", nameof(reauthenticationPassword));
        }

        if (string.IsNullOrWhiteSpace(request.CandidatePath) || Path.IsPathRooted(request.CandidatePath.Trim()) is false)
        {
            throw new ArgumentException("El candidato de restore debe ser una ruta absoluta.", nameof(request));
        }

        if (!Enum.IsDefined(request.Source))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Source));
        }

        if (request.ExpectedPackageSha256 is not null &&
            (request.ExpectedPackageSha256.Length != 64 || !request.ExpectedPackageSha256.All(Uri.IsHexDigit)))
        {
            throw new InvalidDataException("El checksum esperado no tiene formato SHA-256.");
        }

        if (request.Source == ProtectedRestoreCandidateSource.LocalStagedBackup &&
            string.IsNullOrWhiteSpace(request.ExpectedPackageSha256))
        {
            throw new InvalidDataException("La copia local importada requiere el SHA-256 esperado.");
        }
    }

    private void ValidateCandidatePath(ProtectedRestoreRequest request)
    {
        var candidate = Path.GetFullPath(request.CandidatePath.Trim());
        var root = request.Source switch
        {
            ProtectedRestoreCandidateSource.LocalProtectedBackup => _paths.ProtectedBackupsRoot,
            ProtectedRestoreCandidateSource.CloudRecoveryCandidate => _paths.CloudRecoveryStagingRoot,
            ProtectedRestoreCandidateSource.LocalStagedBackup => _paths.LocalRestoreStagingRoot,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Source))
        };
        if (!MachineWideAssetPathGuard.IsWithin(candidate, root) ||
            (request.Source is ProtectedRestoreCandidateSource.LocalProtectedBackup or ProtectedRestoreCandidateSource.LocalStagedBackup) &&
            !string.Equals(
                Path.GetDirectoryName(candidate)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) ||
            !candidate.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El archivo seleccionado no está dentro de una raíz autorizada para restauración protegida.");
        }

        if (!MachineWideAssetPathGuard.TryGetRegularFile(candidate, root, out _, out var issue))
        {
            throw new InvalidDataException($"El archivo seleccionado no es un respaldo protegido regular ({issue}).");
        }
    }

    private static void AddOptional(ICollection<string> arguments, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add(name);
            arguments.Add(value);
        }
    }
}
