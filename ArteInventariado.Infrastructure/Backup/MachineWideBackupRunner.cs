using System.Diagnostics;
using System.Reflection;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

public enum MachineWideBackupRunOutcome
{
    Succeeded = 1,
    SkippedDisabled = 2,
    SkippedConcurrent = 3
}

public sealed record MachineWideBackupRunResult(
    MachineWideBackupRunOutcome Outcome,
    string? FilePath,
    string? Sha256,
    BackupManifest? Manifest,
    MachineWideBackupRetentionPlan? Retention,
    string Message,
    IReadOnlyList<string> Warnings);

internal interface IMachineWideBackupLock : IDisposable
{
    bool TryAcquire();
}

internal enum MachineWideBackupRunnerCheckpoint
{
    BeforePublish,
    AfterPublish,
    BeforeRetention,
    BeforeSuccessStatus
}

/// <summary>
/// Dedicated non-UI runner for the protected machine-wide backup task. It
/// has no session or application-permission dependency and therefore can run
/// as SYSTEM while nobody is logged in.
/// </summary>
public sealed class MachineWideBackupRunner
{
    public const string LockName = "Global\\ArteInventariado.BackupRunner";

    private readonly ArteInventariadoPathProvider _paths;
    private readonly BackupPackageEngine _engine;
    private readonly MachineWideBackupPolicyStore _policyStore;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _isAdministrator;
    private readonly Func<string, IMachineWideBackupLock> _lockFactory;
    private readonly bool _allowSyntheticRoots;
    private readonly MachineWideBackupAssetTrustBoundary _assetTrustBoundary;
    private readonly Action<MachineWideBackupRunnerCheckpoint> _checkpoint;

    public MachineWideBackupRunner(ArteInventariadoPathProvider paths)
        : this(
            paths,
            new BackupPackageEngine(),
            new MachineWideBackupPolicyStore(),
            TimeProvider.System,
            MachineWideElevation.IsAdministratorOrSystem,
            name => new NamedMachineWideBackupLock(name),
            allowSyntheticRoots: false)
    {
    }

    internal MachineWideBackupRunner(
        ArteInventariadoPathProvider paths,
        BackupPackageEngine engine,
        MachineWideBackupPolicyStore policyStore,
        TimeProvider timeProvider,
        Func<bool> isAdministrator,
        Func<string, IMachineWideBackupLock> lockFactory,
        bool allowSyntheticRoots = false,
        Action<MachineWideBackupRunnerCheckpoint>? checkpoint = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _policyStore = policyStore ?? throw new ArgumentNullException(nameof(policyStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        _lockFactory = lockFactory ?? throw new ArgumentNullException(nameof(lockFactory));
        _allowSyntheticRoots = allowSyntheticRoots;
        _checkpoint = checkpoint ?? (_ => { });
        _assetTrustBoundary = new MachineWideBackupAssetTrustBoundary(
            paths.ProductBusinessAssetsRoot,
            paths.AssetsRoot);
    }

    public async Task<MachineWideBackupRunResult> RunAsync(
        BackupKind backupKind = BackupKind.Automatic,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = DateTimeOffset.UtcNow;
        var stage = "inicializacion";
        try
        {
            if (backupKind is not BackupKind.Automatic and not BackupKind.Manual ||
                backupKind == BackupKind.Manual && (!operationId.HasValue || operationId == Guid.Empty) ||
                backupKind == BackupKind.Automatic && operationId.HasValue)
            {
                throw new ArgumentException("El tipo de respaldo y el identificador de operación no forman una solicitud válida.");
            }

            // Register the attempt before any protected validation, policy
            // read, clock provider or mutex construction can fail.  This is
            // best effort so a broken root cannot turn diagnostics into a
            // second exception.
            if (_paths.IsMachineWide)
            {
                WriteEvent(
                    "BACKUP_RUNNER_STARTED",
                    null,
                    $"Inicio del respaldo protegido en {attempt:O}; tipo={backupKind}; " +
                    $"operación={operationId?.ToString("D") ?? "programada"}; etapa=inicializacion.");
            }

            stage = "reloj";
            attempt = _timeProvider.GetUtcNow();

            stage = "validacion-layout";
            EnsureMachineWide();
            MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);
            ValidateBackupRoots();

            stage = "autorizacion";
            if (!_isAdministrator())
            {
                throw new UnauthorizedAccessException(
                    "El BackupRunner machine-wide debe ejecutarse como SYSTEM o administrador.");
            }

            stage = "carga-politica";
            var policy = _policyStore.Load(_paths);
            if (!policy.Enabled && backupKind == BackupKind.Automatic)
            {
                return new MachineWideBackupRunResult(
                    MachineWideBackupRunOutcome.SkippedDisabled,
                    null,
                    null,
                    null,
                    null,
                    "La política protegida de respaldos está deshabilitada.",
                    []);
            }

            MachineWideBackupRunResult result;
            stage = "adquisicion-lock";
            using (var operationLock = _lockFactory(LockName))
            {
                if (!operationLock.TryAcquire())
                {
                    WriteEvent("BACKUP_RUNNER_SKIPPED_CONCURRENT", null, "Ya existe otro BackupRunner en ejecución.");
                    if (backupKind == BackupKind.Manual)
                    {
                        throw new InvalidOperationException("Ya hay otro respaldo protegido en curso; no se inició un segundo respaldo manual.");
                    }

                    return new MachineWideBackupRunResult(
                        MachineWideBackupRunOutcome.SkippedConcurrent,
                        null,
                        null,
                        null,
                        null,
                        "Ya existe otro respaldo protegido en ejecución.",
                        []);
                }

                stage = "creacion-validacion-publicacion";
                result = await CreateAndPromoteAsync(policy, attempt, backupKind, cancellationToken);
                stage = "liberacion-lock";
            }

            // A successful backup is not definitive until the native lock has
            // released its owner thread and the whole lock scope has been
            // disposed.  This keeps status/exit code coherent if cleanup
            // reports a release or shutdown failure.
            stage = "finalizacion-status";
            _checkpoint(MachineWideBackupRunnerCheckpoint.BeforeSuccessStatus);
            var resultSize = result.FilePath is null ? (long?)null : new FileInfo(result.FilePath).Length;
            var previousStatus = _policyStore.LoadStatus(_paths);
            SaveStatus(new MachineWideBackupStatus(
                MachineWideBackupPolicyStore.CurrentSchemaVersion,
                attempt,
                attempt,
                Path.GetFileName(result.FilePath),
                resultSize,
                result.Sha256,
                null,
                result.Retention?.DailyGenerationsKept ?? previousStatus.RetainedDaily,
                result.Retention?.WeeklyGenerationsKept ?? previousStatus.RetainedWeekly,
                result.Retention?.MonthlyGenerationsKept ?? previousStatus.RetainedMonthly,
                result.Manifest?.ContentStatus == BackupContentStatus.DegradedAssets
                    ? MachineWideBackupStatusKind.SuccessDegradedAssets
                    : MachineWideBackupStatusKind.SuccessHealthy,
                result.Manifest?.OmittedAssetCount ?? result.Manifest?.Assets.Count(asset => asset.WasMissing) ?? 0,
                operationId,
                backupKind));
            var successDescription = result.FilePath is null
                ? "Respaldo protegido completado."
                : $"Respaldo protegido publicado: {Path.GetFileName(result.FilePath)}; tamano={resultSize ?? 0}; sha256={result.Sha256}.";
            if (result.Retention is { } retention)
            {
                successDescription +=
                    $" Retención: diarios={retention.DailyGenerationsKept}, " +
                    $"semanales={retention.WeeklyGenerationsKept}, " +
                    $"mensuales={retention.MonthlyGenerationsKept}, " +
                    $"eliminados={retention.DeletableFiles.Count}.";
            }
            else
            {
                successDescription += " Retención automática no aplicada a este respaldo manual.";
            }

            WriteEvent("BACKUP_RUNNER_SUCCEEDED", result.Manifest?.SourceInstallationMode, successDescription);
            if (result.Retention is { } appliedRetention)
            {
                WriteEvent(
                    "BACKUP_RETENTION_APPLIED",
                    result.Manifest?.SourceInstallationMode,
                    $"Retención protegida: diarios={appliedRetention.DailyGenerationsKept}, " +
                    $"semanales={appliedRetention.WeeklyGenerationsKept}, " +
                    $"mensuales={appliedRetention.MonthlyGenerationsKept}, " +
                    $"eliminados={appliedRetention.DeletableFiles.Count}.");
            }
            return result;
        }
        catch (OperationCanceledException ex)
        {
            SaveFailure(
                attempt,
                $"Operación cancelada en etapa '{stage}' antes de completar el respaldo protegido.",
                operationId,
                backupKind);
            MachineWideBackupDiagnostics.RecordRunnerFailure(
                _paths,
                stage,
                ex);
            WriteEvent(
                "BACKUP_RUNNER_CANCELLED",
                null,
                $"El respaldo protegido fue cancelado en etapa '{stage}'.");

            throw;
        }
        catch (Exception ex)
        {
            var diagnostic = $"Fallo en etapa '{stage}': {ex.GetType().Name}.";
            SaveFailure(attempt, diagnostic, operationId, backupKind);
            MachineWideBackupDiagnostics.RecordRunnerFailure(_paths, stage, ex);
            WriteEvent("BACKUP_RUNNER_FAILED", null, diagnostic);

            throw;
        }
    }

    internal async Task<MachineWideBackupRetentionPlan> ApplyRetentionForSmokeAsync(
        MachineWideBackupPolicy policy,
        string validationRoot,
        CancellationToken cancellationToken = default)
    {
        var migrations = await GetCurrentMigrationsAsync(cancellationToken);
        var candidates = await ReadValidAutomaticCandidatesAsync(
            policy,
            migrations,
            validationRoot,
            cancellationToken);
        return ApplyRetention(policy, candidates);
    }

    private async Task<MachineWideBackupRunResult> CreateAndPromoteAsync(
        MachineWideBackupPolicy policy,
        DateTimeOffset attempt,
        BackupKind backupKind,
        CancellationToken cancellationToken)
    {
        var station = new LocalStationConfigurationStore(_paths).Load().Status;
        if (!station.IsConfigured || !station.InstallationId.HasValue || !station.Mode.HasValue)
        {
            throw new InvalidOperationException(
                "La estación machine-wide debe estar configurada antes de crear respaldos protegidos.");
        }

        var backupId = Guid.NewGuid();
        var stagingBase = Path.Combine(_paths.ProtectedBackupsRoot, ".staging");
        var stagingRoot = Path.Combine(
            stagingBase,
            backupId.ToString("N"));
        EnsureNoReparsePoint(_paths.ProtectedBackupsRoot);
        EnsureNoReparsePoint(stagingBase);
        Directory.CreateDirectory(stagingBase);
        Directory.CreateDirectory(stagingRoot);
        EnsureNoReparsePoint(stagingRoot);

        try
        {
            var migrations = await GetCurrentMigrationsAsync(cancellationToken);
            var snapshot = Path.Combine(stagingRoot, BackupPackageEngine.DatabaseEntryName);
            await _engine.CreateSnapshotAsync(_paths.DatabasePath, snapshot, cancellationToken);
            var databaseErrors = await _engine.ValidateDatabaseAsync(
                snapshot,
                migrations,
                // A protected backup may run before application startup has
                // applied pending migrations. Accept only a valid historical
                // migration prefix; never bypass integrity or future-schema
                // compatibility checks.
                allowOlderSchema: true,
                cancellationToken);
            if (databaseErrors.Count > 0)
            {
                throw new InvalidDataException(
                    $"El snapshot SQLite no superó las validaciones. {string.Join(" ", databaseErrors)}");
            }

            var productAssets = await _engine.ReadProductAssetsAsync(snapshot, cancellationToken);
            var managedSources = await _engine.ReadManagedAssetSourcesAsync(snapshot, cancellationToken);
            var preparation = await _assetTrustBoundary.PrepareAsync(
                productAssets,
                managedSources,
                stagingRoot,
                cancellationToken);
            var assets = preparation.Assets;
            var warnings = preparation.Warnings.ToList();

            var databaseHash = await BackupPackageEngine.ComputeSha256Async(snapshot, cancellationToken);
            var appliedMigrations = await _engine.ReadAppliedMigrationsAsync(snapshot, cancellationToken);
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                backupId,
                attempt,
                attempt.ToLocalTime(),
                ResolveApplicationVersion(),
                "SQLite",
                appliedMigrations,
                appliedMigrations.LastOrDefault(),
                databaseHash,
                new FileInfo(snapshot).Length,
                station.InstallationId.Value,
                station.Mode.Value,
                backupKind,
                assets,
                preparation.ContentStatus,
                preparation.OmittedAssetCount);

            var name = backupKind == BackupKind.Manual
                ? $"ArteInventariado_{attempt.UtcDateTime:yyyyMMdd_HHmmss}_Manual_{backupId:N}.aibak"
                : $"ArteInventariado_{attempt.UtcDateTime:yyyyMMdd_HHmmss}_{backupId:N}.aibak";
            var stagedPackage = Path.Combine(stagingRoot, name);
            await _engine.CreatePackageAsync(
                stagingRoot,
                snapshot,
                manifest,
                stagedPackage,
                cancellationToken);

            var validation = await _engine.ValidatePackageAsync(
                stagedPackage,
                migrations,
                Path.Combine(stagingRoot, "validation"),
                cancellationToken);
            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidDataException(
                    $"El respaldo protegido no superó la validación final. {string.Join(" ", validation.Errors)}");
            }

            var finalPath = Path.Combine(policy.DestinationDirectory, name);
            EnsureNoReparsePoint(policy.DestinationDirectory);
            if (File.Exists(finalPath))
            {
                throw new IOException("Ya existe un respaldo protegido con el mismo identificador.");
            }

            _checkpoint(MachineWideBackupRunnerCheckpoint.BeforePublish);

            // Both paths are on ProtectedBackups, so File.Move is the atomic
            // promotion boundary. No final name exists while the ZIP is
            // incomplete.
            File.Move(stagedPackage, finalPath);
            try
            {
                _checkpoint(MachineWideBackupRunnerCheckpoint.AfterPublish);
                var promotedValidation = await _engine.ValidatePackageAsync(
                    finalPath,
                    migrations,
                    Path.Combine(stagingRoot, "promoted-validation"),
                    cancellationToken);
                if (!promotedValidation.IsValid || promotedValidation.Manifest is null)
                {
                    throw new InvalidDataException(
                        $"El respaldo promovido no superó la validación. {string.Join(" ", promotedValidation.Errors)}");
                }
            }
            catch
            {
                TryDeleteFile(finalPath);
                throw;
            }

            var hash = await BackupPackageEngine.ComputeSha256Async(finalPath, cancellationToken);
            _checkpoint(MachineWideBackupRunnerCheckpoint.BeforeRetention);
            MachineWideBackupRetentionPlan? retention = null;
            if (backupKind == BackupKind.Automatic)
            {
                var candidates = await ReadValidAutomaticCandidatesAsync(
                    policy,
                    migrations,
                    Path.Combine(stagingRoot, "retention"),
                    cancellationToken);
                retention = EnsurePromotedBackupIsKept(
                    ApplyRetention(policy, candidates),
                    finalPath);
                warnings.AddRange(DeleteRetainedFiles(retention));
            }
            return new MachineWideBackupRunResult(
                MachineWideBackupRunOutcome.Succeeded,
                finalPath,
                hash,
                manifest,
                retention,
                "Respaldo protegido creado, validado y publicado.",
                warnings);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            TryDeleteDirectory(Path.Combine(_paths.ProtectedBackupsRoot, ".staging"));
        }
    }

    private async Task<IReadOnlyList<AutomaticBackupCandidate>> ReadValidAutomaticCandidatesAsync(
        MachineWideBackupPolicy policy,
        IReadOnlyList<string> migrations,
        string validationRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(policy.DestinationDirectory))
        {
            return [];
        }

        Directory.CreateDirectory(validationRoot);
        var candidates = new List<AutomaticBackupCandidate>();
        foreach (var path in Directory.EnumerateFiles(
                     policy.DestinationDirectory,
                     "*.aibak",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validationDirectory = Path.Combine(validationRoot, Guid.NewGuid().ToString("N"));
            try
            {
                EnsureNoReparsePoint(path);
                var validation = await _engine.ValidatePackageAsync(
                    path,
                    migrations,
                    validationDirectory,
                    cancellationToken);
                if (validation.IsValid && validation.Manifest?.Kind == BackupKind.Automatic)
                {
                    candidates.Add(new AutomaticBackupCandidate(path, validation.Manifest));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                WriteEvent(
                    "BACKUP_RETENTION_SKIPPED_FILE",
                    null,
                    $"Retención omitió {Path.GetFileName(path)}: {ex.GetType().Name}.");
            }
            finally
            {
                TryDeleteDirectory(validationDirectory);
            }
        }

        return candidates;
    }

    private MachineWideBackupRetentionPlan ApplyRetention(
        MachineWideBackupPolicy policy,
        IReadOnlyList<AutomaticBackupCandidate> candidates) =>
        MachineWideBackupRetention.BuildPlan(candidates, policy);

    private static MachineWideBackupRetentionPlan EnsurePromotedBackupIsKept(
        MachineWideBackupRetentionPlan plan,
        string promotedPath)
    {
        if (plan.Keeps(promotedPath))
        {
            return plan;
        }

        // Two valid runs can legitimately share the same TimeProvider tick
        // (for example after a missed task is retried immediately). Never let
        // a tie-breaker delete the package that was just created and verified.
        var kept = new HashSet<string>(plan.KeptFiles, StringComparer.OrdinalIgnoreCase)
        {
            promotedPath
        };
        var deletable = plan.DeletableFiles
            .Where(path => !string.Equals(path, promotedPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return plan with { KeptFiles = kept, DeletableFiles = deletable };
    }

    private IReadOnlyList<string> DeleteRetainedFiles(MachineWideBackupRetentionPlan plan)
    {
        var warnings = new List<string>();
        foreach (var path in plan.DeletableFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A retention error must never remove or invalidate the last
                // known-good package or make an otherwise valid backup fail.
                warnings.Add($"No se pudo limpiar un respaldo anterior ({ex.GetType().Name}).");
            }
        }

        return warnings;
    }

    private async Task<IReadOnlyList<string>> GetCurrentMigrationsAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection.ToString())
            .Options;
        await using var context = new AppDbContext(options);
        cancellationToken.ThrowIfCancellationRequested();
        return context.Database.GetMigrations().ToList();
    }

    private void EnsureMachineWide()
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException(
                "El BackupRunner requiere una instalación machine-wide protegida.");
        }
    }

    private void ValidateBackupRoots()
    {
        // The general machine-wide guard validates the installation, data and
        // station roots. Backups additionally traverse the live database and
        // both canonical asset trees, so validate those roots before opening
        // any file. This also rejects a symlink/reparse inserted below an
        // otherwise trusted machine-wide root.
        foreach (var path in new[]
        {
            _paths.MachineInstallRoot,
            _paths.MachineDataRoot,
            _paths.DataRoot,
            _paths.DatabasePath,
            _paths.AssetsRoot,
            _paths.BusinessAssetsRoot,
            _paths.ProductBusinessAssetsRoot,
            _paths.ProtectedBackupsRoot,
            Path.Combine(_paths.ProtectedBackupsRoot, ".staging"),
            _paths.ProtectedBackupPolicyPath,
            _paths.ProtectedBackupStatusPath
        })
        {
            EnsureNoReparsePoint(path);
        }
    }

    private void SaveStatus(MachineWideBackupStatus status)
    {
        try
        {
            _policyStore.SaveStatus(_paths, status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            WriteEvent("BACKUP_STATUS_WRITE_FAILED", null, $"No se pudo registrar el estado: {ex.GetType().Name}.");
            throw;
        }
    }

    private void SaveFailure(
        DateTimeOffset attempt,
        string error,
        Guid? operationId,
        BackupKind backupKind)
    {
        if (!_paths.IsMachineWide)
        {
            return;
        }

        try
        {
            var previous = _policyStore.LoadStatus(_paths);
            SaveStatus(previous with
            {
                LastAttemptUtc = attempt,
                LastError = error,
                LastOutcome = MachineWideBackupStatusKind.Failure,
                LastOmittedAssetCount = 0,
                LastOperationId = operationId,
                LastBackupKind = backupKind
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            WriteEvent("BACKUP_STATUS_WRITE_FAILED", null, $"No se pudo registrar el fallo: {ex.GetType().Name}.");
        }
    }

    private void WriteEvent(string eventName, InstallationMode? mode, string description)
    {
        try
        {
            new StationEventLog(_paths).Write(eventName, "SYSTEM", mode, description);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ResolveApplicationVersion()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var productVersion = FileVersionInfo.GetVersionInfo(processPath).ProductVersion;
            if (!string.IsNullOrWhiteSpace(productVersion))
            {
                return productVersion;
            }
        }

        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
    }

    private static void EnsureNoReparsePoint(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"La ruta del respaldo contiene un reparse point: '{current}'.");
                }
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
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

internal sealed class NamedMachineWideBackupLock : IMachineWideBackupLock
{
    private static readonly TimeSpan OwnerShutdownTimeout = TimeSpan.FromSeconds(10);
    private readonly Mutex? _mutex;
    private readonly ManualResetEventSlim _acquisitionCompleted = new(initialState: false);
    private readonly ManualResetEventSlim _releaseRequested = new(initialState: false);
    private Thread? _ownerThread;
    private Exception? _acquisitionException;
    private Exception? _releaseException;
    private bool _acquired;
    private bool _disposed;

    public NamedMachineWideBackupLock(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(initiallyOwned: false, name);
        }
    }

    public bool TryAcquire()
    {
        if (_mutex is null)
        {
            return true;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ownerThread is not null)
        {
            throw new InvalidOperationException("El lock de respaldo solo puede adquirirse una vez.");
        }

        // A Windows Mutex is owned by the thread that acquired it. RunAsync
        // crosses await boundaries, so releasing it from the continuation can
        // throw after the backup and SuccessHealthy status were already saved.
        // Keep acquisition and release on one dedicated thread while the
        // asynchronous backup runs elsewhere.
        _ownerThread = new Thread(OwnMutex)
        {
            IsBackground = true,
            Name = "ArteInventariado.BackupRunner.Lock"
        };
        _ownerThread.Start();
        _acquisitionCompleted.Wait();
        if (_acquisitionException is not null)
        {
            throw new InvalidOperationException(
                "No se pudo adquirir el lock global de respaldos.",
                _acquisitionException);
        }

        return _acquired;
    }

    private void OwnMutex()
    {
        try
        {
            try
            {
                _acquired = _mutex!.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                _acquired = true;
            }
            catch (Exception ex)
            {
                _acquisitionException = ex;
            }
            finally
            {
                _acquisitionCompleted.Set();
            }

            if (_acquired)
            {
                try
                {
                    _releaseRequested.Wait();
                    _mutex!.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    // Never let an exception escape from the owner thread.
                    // Dispose observes it after Join and turns the run into a
                    // non-successful process result.
                    _releaseException = ex;
                }
            }
        }
        finally
        {
            _acquired = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var ownerStopped = _ownerThread is null;
        try
        {
            if (_ownerThread is not null && ReferenceEquals(Thread.CurrentThread, _ownerThread))
            {
                throw new InvalidOperationException(
                    "El owner del lock no puede disponer el Mutex desde su propio hilo.");
            }

            _releaseRequested.Set();
            if (_ownerThread is not null)
            {
                ownerStopped = _ownerThread.Join(OwnerShutdownTimeout);
                if (!ownerStopped)
                {
                    throw new TimeoutException(
                        "El hilo propietario del lock global no terminó durante el cleanup.");
                }
            }

            if (_releaseException is not null)
            {
                throw new InvalidOperationException(
                    "No se pudo liberar el lock global de respaldos.",
                    _releaseException);
            }
        }
        finally
        {
            if (ownerStopped)
            {
                _mutex?.Dispose();
                _acquisitionCompleted.Dispose();
                _releaseRequested.Dispose();
            }
        }
    }
}
