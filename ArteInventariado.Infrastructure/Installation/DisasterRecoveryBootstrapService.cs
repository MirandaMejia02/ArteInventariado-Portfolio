using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// H2 orchestration for a genuinely fresh machine. G2 owns cloud
/// authentication/download/validation and H1 owns PRE_RESTORE, SQLite,
/// assets, promotion and rollback. This class is only the transition
/// coordinator and never creates a business principal before a validated
/// candidate exists.
/// </summary>
public sealed class DisasterRecoveryBootstrapService : IDisasterRecoveryBootstrapService
{
    private const int RecoveryOriginSchemaVersion = 1;
    private const string RecoveryOriginFileName = "recovery-origin.json";

    private readonly ArteInventariadoPathProvider _paths;
    private readonly IInstallationStateClassifier _classifier;
    private readonly Func<DisasterRecoveryCloudRequest, CancellationToken, Task<CloudRecoveryCatalogResult>> _catalog;
    private readonly Func<DisasterRecoveryCloudRequest, CloudRecoveryCatalogItem, CancellationToken, Task<CloudRecoveryCandidate>> _download;
    private readonly TimeProvider _timeProvider;
    private readonly Func<ProtectedRestoreRequest, CancellationToken, Task<ProtectedRestoreHelperResult>> _restore;
    private readonly Func<bool> _isElevated;
    private readonly bool _allowSyntheticRoots;
    private readonly DisasterRecoveryJournalStore _journalStore;

    public DisasterRecoveryBootstrapService(
        ArteInventariadoPathProvider paths,
        IInstallationStateClassifier classifier,
        CloudRecoveryService cloudRecovery,
        TimeProvider timeProvider)
        : this(
            paths,
            classifier,
            (request, cancellationToken) => cloudRecovery.GetCatalogForFreshMachineAsync(
                request.Authentication,
                new CloudRecoveryScope(request.SourceInstallationId, request.OwnerConfirmed),
                request.Configuration,
                cancellationToken),
            (request, selection, cancellationToken) => cloudRecovery.DownloadAndValidateForFreshMachineAsync(
                request.Authentication,
                new CloudRecoveryScope(request.SourceInstallationId, request.OwnerConfirmed),
                selection,
                request.Configuration,
                cancellationToken),
            timeProvider,
            (restoreRequest, cancellationToken) => new ProtectedRestoreEngine(paths)
                .ExecuteAsync(restoreRequest, cancellationToken),
            MachineWideElevation.IsAdministrator,
            allowSyntheticRoots: false)
    {
    }

    internal DisasterRecoveryBootstrapService(
        ArteInventariadoPathProvider paths,
        IInstallationStateClassifier classifier,
        Func<DisasterRecoveryCloudRequest, CancellationToken, Task<CloudRecoveryCatalogResult>> catalog,
        Func<DisasterRecoveryCloudRequest, CloudRecoveryCatalogItem, CancellationToken, Task<CloudRecoveryCandidate>> download,
        TimeProvider timeProvider,
        Func<ProtectedRestoreRequest, CancellationToken, Task<ProtectedRestoreHelperResult>> restore,
        Func<bool> isElevated,
        bool allowSyntheticRoots = false)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _download = download ?? throw new ArgumentNullException(nameof(download));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        _allowSyntheticRoots = allowSyntheticRoots;
        _journalStore = new DisasterRecoveryJournalStore(_paths.RecoveryRoot);
    }

    public InstallationStateAssessment Classify() => _classifier.Classify();

    public async Task<CloudRecoveryCatalogResult> GetCatalogAsync(
        DisasterRecoveryCloudRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCloudRequest(request);
        EnsureFreshMachine();
        EnsureElevated();
        return await _catalog(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DisasterRecoveryResult> RecoverAsync(
        DisasterRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateRequest(request);
            EnsureFreshMachine();
            EnsureElevated();
            MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);

            var pending = _journalStore.Load();
            if (pending is not null &&
                pending.Phase is not DisasterRecoveryPhase.Completed and not DisasterRecoveryPhase.Cancelled)
            {
                throw new InvalidOperationException("Ya existe un bootstrap de disaster recovery pendiente.");
            }

            var operationId = Guid.NewGuid();
            var originalStationExisted = File.Exists(_paths.StationCorePath);
            var originalStationBackup = CaptureOriginalStation(operationId);
            var destinationId = CreateDestinationIdentity(request.Cloud.SourceInstallationId);
            var journal = new DisasterRecoveryJournalDocument(
                DisasterRecoveryJournalStore.CurrentSchemaVersion,
                operationId,
                _timeProvider.GetUtcNow(),
                DisasterRecoveryPhase.CandidateSelected,
                request.Cloud.SourceInstallationId,
                destinationId,
                request.DestinationMode,
                request.Selection.BackupId,
                null,
                null,
                originalStationBackup,
                originalStationExisted,
                false);
            _journalStore.Save(journal);

            CloudRecoveryCandidate? candidate = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidate = await _download(request.Cloud, request.Selection, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate.InstallationId != request.Cloud.SourceInstallationId ||
                    candidate.BackupId != request.Selection.BackupId)
                {
                    throw new InvalidDataException("El candidato cloud no corresponde a la instalación y backup seleccionados.");
                }

                journal = journal with
                {
                    Phase = DisasterRecoveryPhase.CandidateDownloaded,
                    CandidatePath = candidate.PackagePath,
                    CandidateSha256 = candidate.PackageSha256
                };
                _journalStore.Save(journal);

                PrepareDestination(destinationId, request.DestinationMode, request.Cloud.SourceInstallationId);
                journal = journal with
                {
                    Phase = DisasterRecoveryPhase.DestinationPrepared,
                    DestinationDatabaseCreated = File.Exists(_paths.DatabasePath)
                };
                _journalStore.Save(journal);

                var restoreRequest = new ProtectedRestoreRequest(
                    candidate.PackagePath,
                    ProtectedRestoreCandidateSource.CloudRecoveryCandidate,
                    candidate.BackupId,
                    candidate.InstallationId,
                    candidate.PackageSha256,
                    candidate.ContentStatus,
                    ConfirmationAccepted: true);
                journal = journal with { Phase = DisasterRecoveryPhase.RestoreInvoked };
                _journalStore.Save(journal);
                var restoreHelper = await _restore(restoreRequest, cancellationToken).ConfigureAwait(false);
                var restoreResult = ToPublicResult(restoreHelper);

                if (restoreHelper.Outcome == ProtectedRestoreOutcome.RecoveryRequired)
                {
                    return new DisasterRecoveryResult(
                        DisasterRecoveryOutcome.RecoveryPending,
                        "La restauración requiere recuperación protegida antes de activar la estación.",
                        request.Cloud.SourceInstallationId,
                        destinationId,
                        candidate.BackupId,
                        candidate,
                        restoreResult,
                        restoreHelper.Warnings);
                }

                if (restoreHelper.Outcome != ProtectedRestoreOutcome.Succeeded)
                {
                    await RollbackFreshPreparationAsync(originalStationBackup, journal, cancellationToken)
                        .ConfigureAwait(false);
                    ClearJournalAndBackup(originalStationBackup);
                    return new DisasterRecoveryResult(
                        DisasterRecoveryOutcome.Failed,
                        restoreHelper.Message,
                        request.Cloud.SourceInstallationId,
                        destinationId,
                        candidate.BackupId,
                        candidate,
                        restoreResult,
                        restoreHelper.Warnings);
                }

                var completedCandidate = candidate
                    ?? throw new InvalidDataException("La recuperación terminó sin candidato validado.");
                EnsureDestinationStation(destinationId, request.DestinationMode);
                if (!HasProtectedPrincipal())
                {
                    return new DisasterRecoveryResult(
                        DisasterRecoveryOutcome.RecoveryPending,
                        "El backup restaurado no contiene el Administrador Principal original. La instalación permanece en diagnóstico protegido.",
                        request.Cloud.SourceInstallationId,
                        destinationId,
                        completedCandidate.BackupId,
                        completedCandidate,
                        restoreResult,
                        restoreHelper.Warnings);
                }
                WriteRecoveryOrigin(request.Cloud.SourceInstallationId, destinationId, completedCandidate.BackupId);
                journal = journal with { Phase = DisasterRecoveryPhase.Completed };
                _journalStore.Save(journal);

                var warnings = new List<string>(restoreHelper.Warnings ?? []);
                try
                {
                    if (candidate is not null)
                    {
                        await CleanupCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    warnings.Add("El candidato validado quedó retenido para limpieza protegida posterior.");
                }

                ClearJournalAndBackup(originalStationBackup);
                return new DisasterRecoveryResult(
                    DisasterRecoveryOutcome.Succeeded,
                    completedCandidate.ContentStatus == BackupContentStatus.DegradedAssets
                        ? "Recuperación completada; el backup tenía assets degradados. Reinicie para iniciar sesión."
                        : "Recuperación completada y validada. Reinicie para iniciar sesión con el Administrador Principal original.",
                    request.Cloud.SourceInstallationId,
                    destinationId,
                    completedCandidate.BackupId,
                    completedCandidate,
                    restoreResult with { Warnings = warnings },
                    warnings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (candidate is not null)
                {
                    try { await CleanupCandidateAsync(candidate, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or InvalidDataException) { }
                }

                await RollbackFreshPreparationAsync(originalStationBackup, journal, CancellationToken.None)
                    .ConfigureAwait(false);
                ClearJournalAndBackup(originalStationBackup);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            return new DisasterRecoveryResult(
                DisasterRecoveryOutcome.Cancelled,
                "La recuperación fue cancelada; la instalación fresca permanece sin datos comerciales.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DisasterRecoveryResult(DisasterRecoveryOutcome.Rejected, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException)
        {
            return new DisasterRecoveryResult(DisasterRecoveryOutcome.Failed, ex.Message);
        }
    }

    public async Task ReconcilePendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var journal = _journalStore.Load();
        if (journal is null || journal.Phase is DisasterRecoveryPhase.Completed or DisasterRecoveryPhase.Cancelled)
        {
            return;
        }

        EnsureMachineWide();
        EnsureElevated();
        if (journal.Phase == DisasterRecoveryPhase.RestoreInvoked)
        {
            var station = new LocalStationConfigurationStore(_paths).Load().Status;
            if (station.IsConfigured &&
                station.InstallationId == journal.DestinationInstallationId &&
                station.Mode == journal.DestinationMode &&
                File.Exists(_paths.DatabasePath))
            {
                var h1Journals = new ProtectedRestoreJournalStore(_paths.RecoveryRoot)
                    .EnumerateOperationDirectories()
                    .Select(path => TryLoadRestoreJournal(_paths.RecoveryRoot, path))
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToArray();
                var h1Pending = h1Journals.Any(item =>
                    item.Phase is not ProtectedRestoreJournalPhase.Succeeded and not ProtectedRestoreJournalPhase.RolledBack);
                var h1Completed = h1Journals.Any(item =>
                    item.Phase == ProtectedRestoreJournalPhase.Succeeded &&
                    item.CandidateBackupId == journal.BackupId);
                if (!h1Pending && h1Completed)
                {
                    if (!HasProtectedPrincipal())
                    {
                        return;
                    }

                    WriteRecoveryOrigin(journal.SourceInstallationId, journal.DestinationInstallationId, journal.BackupId);
                    TryDeleteCandidate(journal.CandidatePath);
                    ClearJournalAndBackup(journal.OriginalStationBackupPath);
                    return;
                }

                // H1 remains the sole owner of PRE_RESTORE/rollback. Do not
                // delete its staged state while the protected journal is
                // still active; the next elevated H1 startup will reconcile it.
                return;
            }
        }

        var current = new LocalStationConfigurationStore(_paths).Load().Status;
        if (current.InstallationId == journal.DestinationInstallationId)
        {
            await RollbackFreshPreparationAsync(journal.OriginalStationBackupPath, journal, cancellationToken)
                .ConfigureAwait(false);
        }

        TryDeleteCandidate(journal.CandidatePath);
        ClearJournalAndBackup(journal.OriginalStationBackupPath);
    }

    private void ValidateCloudRequest(DisasterRecoveryCloudRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CloudBackupConfigurationStore.Validate(request.Configuration);
        if (!request.Configuration.Enabled ||
            request.SourceInstallationId == Guid.Empty ||
            !request.OwnerConfirmed)
        {
            throw new ArgumentException(
                "La recuperación requiere configuración cloud habilitada, instalación origen y confirmación explícita.",
                nameof(request));
        }
    }

    private void ValidateRequest(DisasterRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCloudRequest(request.Cloud);
        ArgumentNullException.ThrowIfNull(request.Selection);
        if (request.Selection.InstallationId != request.Cloud.SourceInstallationId ||
            request.Selection.BackupId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Selection.Key))
        {
            throw new InvalidDataException("La selección cloud no corresponde a la instalación confirmada.");
        }

        if (!Enum.IsDefined(request.DestinationMode) || !request.ConfirmationAccepted)
        {
            throw new ArgumentException(
                "Debe confirmarse explícitamente el modo de la nueva estación y la operación de recuperación.",
                nameof(request));
        }
    }

    private void EnsureFreshMachine()
    {
        EnsureMachineWide();
        var assessment = _classifier.Classify();
        if (assessment.State != InstallationLifecycleState.FreshMachine)
        {
            throw new UnauthorizedAccessException(
                $"El disaster bootstrap solo está disponible en FreshMachine (estado actual: {assessment.State}).");
        }
    }

    private void EnsureMachineWide()
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException("El disaster bootstrap requiere el layout machine-wide protegido.");
        }
    }

    private void EnsureElevated()
    {
        if (!_isElevated())
        {
            throw new UnauthorizedAccessException("El disaster bootstrap requiere consentimiento UAC elevado.");
        }
    }

    private Guid CreateDestinationIdentity(Guid sourceInstallationId)
    {
        var station = new LocalStationConfigurationStore(_paths);
        var current = station.Load();
        if (current.Status.IsConfigured)
        {
            throw new InvalidOperationException("La estación ya está configurada; no es una FreshMachine.");
        }

        var destination = current.Status.InstallationId.GetValueOrDefault();
        if (destination == Guid.Empty || destination == sourceInstallationId)
        {
            destination = Guid.NewGuid();
            station.SetFreshIdentity(destination);
        }

        return destination;
    }

    private string? CaptureOriginalStation(Guid operationId)
    {
        if (!File.Exists(_paths.StationCorePath))
        {
            return null;
        }

        EnsurePathWithin(_paths.StationCorePath, _paths.StationRoot);
        EnsureNoReparse(_paths.StationRoot);
        Directory.CreateDirectory(_paths.RecoveryRoot);
        var directory = Path.Combine(_paths.RecoveryRoot, "DisasterRecovery");
        EnsurePathWithin(directory, _paths.RecoveryRoot);
        Directory.CreateDirectory(directory);
        EnsureNoReparse(directory);
        var backup = Path.Combine(directory, $"original-station-{operationId:N}.json");
        File.Copy(_paths.StationCorePath, backup, overwrite: false);
        return backup;
    }

    private void PrepareDestination(Guid destinationId, InstallationMode mode, Guid sourceInstallationId)
    {
        if (destinationId == sourceInstallationId)
        {
            throw new InvalidDataException("La identidad de destino debe ser distinta de la identidad origen.");
        }

        var station = new LocalStationConfigurationStore(_paths);
        var status = station.Load().Status;
        if (status.InstallationId != destinationId || status.IsConfigured)
        {
            throw new InvalidDataException("La identidad protegida de la estación cambió antes de preparar recovery.");
        }

        if (_allowSyntheticRoots)
        {
            station.SaveConfigured(mode);
        }
        else
        {
            MachineWideStationConfigurationHelper.WriteMode(
                _paths,
                mode,
                destinationId,
                expectedCurrentMode: null,
                allowSyntheticRoots: false);
        }

        if (File.Exists(_paths.DatabasePath))
        {
            throw new InvalidDataException("La máquina fresca ya contiene una base de datos; no se reemplaza automáticamente.");
        }

        Directory.CreateDirectory(_paths.DataRoot);
        EnsureNoReparse(_paths.DataRoot);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = _paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString())
            .Options;
        using var context = new AppDbContext(options);
        context.Database.Migrate();
    }

    private void EnsureDestinationStation(Guid destinationId, InstallationMode mode)
    {
        var status = new LocalStationConfigurationStore(_paths).Load().Status;
        if (!status.IsConfigured || status.InstallationId != destinationId || status.Mode != mode)
        {
            throw new InvalidDataException("La estación restaurada no conserva la identidad de destino y el modo confirmado.");
        }
    }

    private bool HasProtectedPrincipal()
    {
        try
        {
            if (!File.Exists(_paths.DatabasePath))
            {
                return false;
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = _paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString())
                .Options;
            using var context = new AppDbContext(options);
            return context.Usuarios.AsNoTracking().Any(user =>
                user.Activo &&
                user.NombreUsuario == "admin" &&
                user.Rol == ArteInventariado.Domain.Enums.RolUsuario.ADMIN);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private void WriteRecoveryOrigin(Guid sourceInstallationId, Guid destinationInstallationId, Guid? backupId)
    {
        EnsurePathWithin(_paths.BootstrapStatePath, _paths.MaintenanceRoot);
        Directory.CreateDirectory(_paths.MaintenanceRoot);
        EnsureNoReparse(_paths.MaintenanceRoot);
        var path = Path.Combine(_paths.MaintenanceRoot, RecoveryOriginFileName);
        var document = new RecoveryOriginDocument(
            RecoveryOriginSchemaVersion,
            sourceInstallationId,
            destinationInstallationId,
            backupId,
            _timeProvider.GetUtcNow());
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task RollbackFreshPreparationAsync(
        string? originalStationBackup,
        DisasterRecoveryJournalDocument journal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var station = new LocalStationConfigurationStore(_paths).Load().Status;
        if ((journal.DestinationDatabaseCreated ||
             station.InstallationId == journal.DestinationInstallationId) &&
            File.Exists(_paths.DatabasePath))
        {
            DeleteSqliteFamily(_paths.DatabasePath);
        }

        if (originalStationBackup is not null && File.Exists(originalStationBackup))
        {
            EnsurePathWithin(originalStationBackup, _paths.RecoveryRoot);
            File.Copy(originalStationBackup, _paths.StationCorePath, overwrite: true);
        }
        else if (File.Exists(_paths.StationCorePath))
        {
            File.Delete(_paths.StationCorePath);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void ClearJournalAndBackup(string? originalStationBackup)
    {
        try
        {
            if (originalStationBackup is not null && File.Exists(originalStationBackup))
            {
                EnsurePathWithin(originalStationBackup, _paths.RecoveryRoot);
                File.Delete(originalStationBackup);
            }
        }
        finally
        {
            _journalStore.Clear();
        }
    }

    private async Task CleanupCandidateAsync(
        CloudRecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var stagingRoot = Path.GetFullPath(_paths.CloudRecoveryStagingRoot);
        var packagePath = Path.GetFullPath(candidate.PackagePath);
        EnsurePathWithin(packagePath, stagingRoot);
        EnsureNoReparse(stagingRoot);
        var directory = Directory.GetParent(packagePath)?.FullName
            ?? throw new InvalidDataException("El candidato no tiene una carpeta válida.");
        EnsurePathWithin(directory, stagingRoot);
        EnsureNoReparse(directory);
        cancellationToken.ThrowIfCancellationRequested();
        TryDeleteDirectory(directory);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void TryDeleteCandidate(string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath)) return;
        try
        {
            var root = Path.GetFullPath(_paths.CloudRecoveryStagingRoot);
            var package = Path.GetFullPath(candidatePath);
            EnsurePathWithin(package, root);
            EnsureNoReparse(root);
            var directory = Directory.GetParent(package)?.FullName;
            if (directory is null) return;
            EnsurePathWithin(directory, root);
            EnsureNoReparse(directory);
            TryDeleteDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }
    }

    private static ProtectedRestoreResult ToPublicResult(ProtectedRestoreHelperResult result) => new(
        result.OperationId,
        result.Outcome,
        result.Message,
        Manifest: null,
        result.PreRestorePath,
        RestartRequired: result.Outcome == ProtectedRestoreOutcome.Succeeded,
        result.RolledBack,
        result.Warnings);

    private static void DeleteSqliteFamily(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static ProtectedRestoreJournalDocument? TryLoadRestoreJournal(string recoveryRoot, string directory)
    {
        try
        {
            return new ProtectedRestoreJournalStore(recoveryRoot).Load(directory);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsurePathWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La ruta de recovery queda fuera de la raíz autorizada.");
        }
    }

    private static void EnsureNoReparse(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("La ruta de recovery contiene un reparse point.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record RecoveryOriginDocument(
        int SchemaVersion,
        Guid SourceInstallationId,
        Guid DestinationInstallationId,
        Guid? BackupId,
        DateTimeOffset CompletedUtc);
}
