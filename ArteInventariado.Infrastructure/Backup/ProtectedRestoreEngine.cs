using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Performs the destructive part of a machine-wide restore.  The class is
/// called by the dedicated elevated helper only; the normal WinForms process
/// uses <see cref="ProtectedRestoreService"/> to cross that UAC boundary.
/// </summary>
public sealed class ProtectedRestoreEngine
{
    public const string LockName = "Global\\ArteInventariado.ProtectedRestore";

    private readonly ArteInventariadoPathProvider _paths;
    private readonly BackupPackageEngine _packageEngine;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _isElevated;
    private readonly Func<IProtectedRestoreLock> _lockFactory;
    private readonly Action<string>? _phaseObserver;

    public ProtectedRestoreEngine(ArteInventariadoPathProvider paths)
        : this(paths, new BackupPackageEngine(), TimeProvider.System, MachineWideElevation.IsAdministrator)
    {
    }

    internal ProtectedRestoreEngine(
        ArteInventariadoPathProvider paths,
        BackupPackageEngine packageEngine,
        TimeProvider timeProvider,
        Func<bool> isElevated,
        Func<IProtectedRestoreLock>? lockFactory = null,
        Action<string>? phaseObserver = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _packageEngine = packageEngine ?? throw new ArgumentNullException(nameof(packageEngine));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        _lockFactory = lockFactory ?? (() => new ProtectedRestoreLock(LockName));
        _phaseObserver = phaseObserver;
    }

    public bool IsMachineWide => _paths.IsMachineWide;

    public async Task<ProtectedRestoreHelperResult> ExecuteAsync(
        ProtectedRestoreRequest request,
        CancellationToken cancellationToken = default,
        bool allowSyntheticRoots = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = Guid.NewGuid();
        ProtectedRestoreJournalStore? journalStore = null;
        string? operationDirectory = null;
        ProtectedRestoreJournalDocument? journal = null;
        BackupManifest? candidateManifest = null;

        try
        {
            ValidateRequest(request);
            EnsureMachineWideAndElevated(allowSyntheticRoots);
            using var operationLock = _lockFactory();
            if (!operationLock.TryAcquire())
            {
                throw new InvalidOperationException("Ya existe una restauracion protegida en curso.");
            }

            var station = ReadStationStatus();
            var migrations = GetCurrentMigrations();
            journalStore = new ProtectedRestoreJournalStore(_paths.RecoveryRoot);
            operationDirectory = journalStore.CreateOperationDirectory(operationId);

            _phaseObserver?.Invoke("candidate-validate");
            var candidate = ValidateCandidatePath(request);
            var candidateHashBefore = await BackupPackageEngine.ComputeSha256Async(candidate, cancellationToken);
            var candidateValidationDirectory = Path.Combine(operationDirectory, "candidate-validation");
            var validation = await _packageEngine.ValidatePackageAsync(
                candidate,
                migrations,
                candidateValidationDirectory,
                cancellationToken);
            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidDataException(
                    $"El candidato de restauracion no es valido. {string.Join(" ", validation.Errors)}");
            }

            candidateManifest = validation.Manifest;
            ValidateCandidateManifest(request, candidateManifest);
            var candidateHashAfter = await BackupPackageEngine.ComputeSha256Async(candidate, cancellationToken);
            if (!FixedEquals(candidateHashBefore, candidateHashAfter))
            {
                throw new InvalidDataException("El candidato cambio mientras se validaba (TOCTOU).");
            }

            if (!string.IsNullOrWhiteSpace(request.ExpectedPackageSha256) &&
                !FixedEquals(request.ExpectedPackageSha256, candidateHashAfter))
            {
                throw new InvalidDataException("El checksum del candidato no coincide con la seleccion original.");
            }

            _phaseObserver?.Invoke("pre-restore");
            var preRestorePath = await CreatePreRestoreAsync(
                operationDirectory,
                station,
                migrations,
                operationId,
                cancellationToken);

            // Revalidate the package immediately before any destructive
            // promotion. The initial validation protects the manifest; this
            // second hash closes the candidate-side TOCTOU window while the
            // PRE_RESTORE is being built.
            var candidateHashBeforeConsume = await BackupPackageEngine.ComputeSha256Async(candidate, cancellationToken);
            if (!FixedEquals(candidateHashAfter, candidateHashBeforeConsume))
            {
                throw new InvalidDataException("El candidato cambio antes de ser consumido.");
            }

            _phaseObserver?.Invoke("stage-candidate");
            var stagedDatabase = Path.Combine(operationDirectory, "candidate.db");
            await BackupPackageEngine.CopyFileAsync(
                Path.Combine(candidateValidationDirectory, BackupPackageEngine.DatabaseEntryName),
                stagedDatabase,
                cancellationToken);
            await _packageEngine.MigrateStagedDatabaseAsync(stagedDatabase, cancellationToken);
            EnsureValidDatabase(stagedDatabase, migrations, allowOlderSchema: false);
            var stagedAssetsRoot = Path.Combine(operationDirectory, "candidate-assets");
            await StageAssetsAsync(
                stagedDatabase,
                candidateValidationDirectory,
                candidateManifest,
                stagedAssetsRoot,
                cancellationToken);
            EnsureValidDatabase(stagedDatabase, migrations, allowOlderSchema: false);

            journal = new ProtectedRestoreJournalDocument(
                ProtectedRestoreJournalStore.CurrentSchemaVersion,
                operationId,
                _timeProvider.GetUtcNow(),
                ProtectedRestoreJournalPhase.Prepared,
                request.Source,
                candidate,
                candidateHashAfter,
                candidateManifest.BackupId,
                candidateManifest.SourceInstallationId,
                candidateManifest.ContentStatus,
                preRestorePath,
                stagedDatabase,
                stagedAssetsRoot,
                station.InstallationId!.Value,
                station.Mode!.Value,
                ComputeOptionalHash(_paths.StationCorePath)
                    ?? throw new InvalidDataException("La identidad protegida de la estacion no esta disponible."),
                ComputeOptionalHash(_paths.CurrentStatePath),
                ComputeOptionalHash(_paths.UpdateStatePath),
                []);
            journalStore.Save(operationDirectory, journal);

            await PromoteAsync(journalStore, operationDirectory, journal, candidateManifest, migrations, cancellationToken);
            WriteEvent("RESTORE_COMPLETED", station.Mode, $"Restauracion protegida {operationId} completada.");
            return new ProtectedRestoreHelperResult(
                operationId,
                ProtectedRestoreOutcome.Succeeded,
                candidateManifest.ContentStatus == BackupContentStatus.DegradedAssets
                    ? "Restauracion completada; el respaldo tenia assets degradados."
                    : "Restauracion completada y validada correctamente.",
                RolledBack: false,
                preRestorePath,
                candidateManifest.ContentStatus == BackupContentStatus.DegradedAssets
                    ? ["El respaldo se marco como DegradedAssets; algunos assets no estaban disponibles."]
                    : []);
        }
        catch (OperationCanceledException)
        {
            if (operationDirectory is not null && journal is null)
            {
                TryDeleteDirectory(operationDirectory);
            }

            throw;
        }
        catch (Exception error)
        {
            var message = error.Message;
            if (journal is null || journalStore is null || operationDirectory is null)
            {
                if (operationDirectory is not null)
                {
                    TryDeleteDirectory(operationDirectory);
                }

                return new ProtectedRestoreHelperResult(
                    operationId,
                    ProtectedRestoreOutcome.Rejected,
                    message,
                    RolledBack: false,
                    PreRestorePath: null);
            }

            WriteEvent("RESTORE_FAILED", ReadStationStatus().Mode, $"Restauracion protegida {operationId} fallo: {error.GetType().Name}.");
            try
            {
                var migrations = GetCurrentMigrations();
                await RollbackAsync(journalStore, operationDirectory, journal, migrations, CancellationToken.None);
                WriteEvent("RESTORE_ROLLBACK_COMPLETED", ReadStationStatus().Mode, $"Rollback protegido {operationId} completado.");
                return new ProtectedRestoreHelperResult(
                    operationId,
                    ProtectedRestoreOutcome.Failed,
                    $"La restauracion fallo y el estado anterior fue recuperado. {message}",
                    RolledBack: true,
                    journal.PreRestorePath);
            }
            catch (Exception rollbackError)
            {
                var recovery = journal with
                {
                    Phase = ProtectedRestoreJournalPhase.RecoveryRequired,
                    LastError = $"Restore: {error.GetType().Name}; rollback: {rollbackError.GetType().Name}"
                };
                try
                {
                    journalStore.Save(operationDirectory, recovery);
                }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // Keep the original recovery failure; no unsafe fallback is attempted.
                }

                WriteEvent("RESTORE_ROLLBACK_FAILED", ReadStationStatus().Mode, $"Rollback protegido {operationId} fallo; RecoveryRequired.");
                return new ProtectedRestoreHelperResult(
                    operationId,
                    ProtectedRestoreOutcome.RecoveryRequired,
                    $"La restauracion y el rollback fallaron. RecoveryRequired. {rollbackError.Message}",
                    RolledBack: false,
                    journal.PreRestorePath);
            }
        }
    }

    /// <summary>
    /// Reconciles unfinished journals.  Any phase before Succeeded is rolled
    /// back deterministically; RecoveryRequired is never retried blindly.
    /// </summary>
    public async Task<ProtectedRestoreHelperResult?> RecoverPendingAsync(
        CancellationToken cancellationToken = default,
        bool allowSyntheticRoots = false)
    {
        EnsureMachineWideAndElevated(allowSyntheticRoots);
        using var operationLock = _lockFactory();
        if (!operationLock.TryAcquire())
        {
            throw new InvalidOperationException("Ya existe una restauracion protegida en curso.");
        }

        var store = new ProtectedRestoreJournalStore(_paths.RecoveryRoot);
        foreach (var operationDirectory in store.EnumerateOperationDirectories())
        {
            ProtectedRestoreJournalDocument journal;
            try
            {
                journal = store.Load(operationDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                continue;
            }

            if (journal.Phase is ProtectedRestoreJournalPhase.Succeeded or ProtectedRestoreJournalPhase.RolledBack)
            {
                continue;
            }

            if (journal.Phase == ProtectedRestoreJournalPhase.RecoveryRequired)
            {
                return new ProtectedRestoreHelperResult(
                    journal.OperationId,
                    ProtectedRestoreOutcome.RecoveryRequired,
                    "Existe una restauracion pendiente marcada como RecoveryRequired.",
                    RolledBack: false,
                    journal.PreRestorePath);
            }

            try
            {
                EnsureJournalReferencesSafe(journal, operationDirectory);
                EnsureJournalBelongsToCurrentInstallation(journal);
                await RollbackAsync(store, operationDirectory, journal, GetCurrentMigrations(), cancellationToken);
                WriteEvent("RESTORE_CRASH_RECOVERY", ReadStationStatus().Mode, $"Rollback determinista de restore {journal.OperationId} completado.");
                return new ProtectedRestoreHelperResult(
                    journal.OperationId,
                    ProtectedRestoreOutcome.Failed,
                    "Se detecto una restauracion incompleta y el estado anterior fue recuperado.",
                    RolledBack: true,
                    journal.PreRestorePath);
            }
            catch (Exception ex)
            {
                var recovery = journal with
                {
                    Phase = ProtectedRestoreJournalPhase.RecoveryRequired,
                    LastError = $"Crash recovery: {ex.GetType().Name}"
                };
                try { store.Save(operationDirectory, recovery); }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidDataException) { }
                return new ProtectedRestoreHelperResult(
                    journal.OperationId,
                    ProtectedRestoreOutcome.RecoveryRequired,
                    $"No fue posible completar el rollback determinista. RecoveryRequired. {ex.Message}",
                    RolledBack: false,
                    journal.PreRestorePath);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the protected journal without attempting recovery. This is safe
    /// for the non-elevated application and is used only to decide whether to
    /// invoke the narrow helper at startup.
    /// </summary>
    public Task<ProtectedRestoreRecoveryStatus> GetRecoveryStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_paths.IsMachineWide)
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

                return Task.FromResult(new ProtectedRestoreRecoveryStatus(
                    true,
                    journal.OperationId,
                    journal.Phase,
                    journal.LastError));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                return Task.FromResult(new ProtectedRestoreRecoveryStatus(
                    true,
                    null,
                    null,
                    "Existe un journal de restore no legible."));
            }
        }

        return Task.FromResult(new ProtectedRestoreRecoveryStatus(false, null, null, null));
    }

    private async Task PromoteAsync(
        ProtectedRestoreJournalStore store,
        string operationDirectory,
        ProtectedRestoreJournalDocument journal,
        BackupManifest candidateManifest,
        IReadOnlyList<string> migrations,
        CancellationToken cancellationToken)
    {
        EnsureJournalBelongsToCurrentInstallation(journal);
        var promoted = journal with { Phase = ProtectedRestoreJournalPhase.PromotingAssets };
        store.Save(operationDirectory, promoted);
        _phaseObserver?.Invoke("promote-assets");
        var createdAssets = await PromoteAssetsAsync(journal.StagedAssetsRoot, cancellationToken);

        promoted = promoted with
        {
            Phase = ProtectedRestoreJournalPhase.PromotingDatabase,
            CreatedAssetPaths = createdAssets
        };
        store.Save(operationDirectory, promoted);
        _phaseObserver?.Invoke("promote-database");
        SqliteConnection.ClearAllPools();
        EnsureDatabaseUnlocked();
        DeleteDatabaseSidecars(_paths.DatabasePath);
        var replaced = Path.Combine(operationDirectory, "replaced-current.db");
        if (File.Exists(_paths.DatabasePath))
        {
            File.Replace(journal.StagedDatabasePath, _paths.DatabasePath, replaced, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(journal.StagedDatabasePath, _paths.DatabasePath);
        }

        DeleteDatabaseSidecars(_paths.DatabasePath);
        promoted = promoted with { Phase = ProtectedRestoreJournalPhase.Validating };
        store.Save(operationDirectory, promoted);
        _phaseObserver?.Invoke("validate-production");
        EnsureValidDatabase(_paths.DatabasePath, migrations, allowOlderSchema: false);
        await ValidatePromotedAssetsAsync(_paths.DatabasePath, candidateManifest, cancellationToken);
        EnsureJournalBelongsToCurrentInstallation(journal);
        promoted = promoted with { Phase = ProtectedRestoreJournalPhase.Succeeded };
        store.Save(operationDirectory, promoted);
        TryDeleteDirectory(Path.Combine(operationDirectory, "candidate-validation"));
        TryDeleteDirectory(journal.StagedAssetsRoot);
        TryDeleteFile(replaced);
    }

    private async Task RollbackAsync(
        ProtectedRestoreJournalStore store,
        string operationDirectory,
        ProtectedRestoreJournalDocument journal,
        IReadOnlyList<string> migrations,
        CancellationToken cancellationToken)
    {
        _phaseObserver?.Invoke("rollback");
        EnsureJournalReferencesSafe(journal, operationDirectory);
        EnsureJournalBelongsToCurrentInstallation(journal);
        var rollbackValidation = Path.Combine(operationDirectory, "rollback-validation");
        var validation = await _packageEngine.ValidatePackageAsync(
            journal.PreRestorePath,
            migrations,
            rollbackValidation,
            cancellationToken);
        if (!validation.IsValid || validation.Manifest is null || validation.Manifest.Kind != BackupKind.PreRestore)
        {
            throw new InvalidDataException("El PRE_RESTORE no se puede validar para rollback.");
        }

        var rollbackDb = Path.Combine(operationDirectory, "rollback.db");
        await BackupPackageEngine.CopyFileAsync(
            Path.Combine(rollbackValidation, BackupPackageEngine.DatabaseEntryName),
            rollbackDb,
            cancellationToken);
        await _packageEngine.MigrateStagedDatabaseAsync(rollbackDb, cancellationToken);
        EnsureValidDatabase(rollbackDb, migrations, allowOlderSchema: false);
        var rollbackAssets = Path.Combine(operationDirectory, "rollback-assets");
        await StageAssetsAsync(
            rollbackDb,
            rollbackValidation,
            validation.Manifest,
            rollbackAssets,
            cancellationToken);
        EnsureValidDatabase(rollbackDb, migrations, allowOlderSchema: false);

        var rolling = journal with { Phase = ProtectedRestoreJournalPhase.PromotingAssets };
        store.Save(operationDirectory, rolling);
        var rollbackCreatedAssets = await PromoteAssetsAsync(rollbackAssets, cancellationToken);
        rolling = rolling with
        {
            Phase = ProtectedRestoreJournalPhase.PromotingDatabase,
            CreatedAssetPaths = rollbackCreatedAssets
        };
        store.Save(operationDirectory, rolling);
        SqliteConnection.ClearAllPools();
        EnsureDatabaseUnlocked();
        DeleteDatabaseSidecars(_paths.DatabasePath);
        var replaced = Path.Combine(operationDirectory, "rollback-replaced-current.db");
        if (File.Exists(_paths.DatabasePath))
        {
            File.Replace(rollbackDb, _paths.DatabasePath, replaced, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(rollbackDb, _paths.DatabasePath);
        }

        DeleteDatabaseSidecars(_paths.DatabasePath);
        EnsureValidDatabase(_paths.DatabasePath, migrations, allowOlderSchema: false);
        await ValidatePromotedAssetsAsync(_paths.DatabasePath, validation.Manifest, cancellationToken);
        // A candidate and PRE_RESTORE can legitimately refer to the same
        // canonical target (for example, identical content hashes).  Never
        // remove a target that the rollback package still needs, otherwise
        // the restored DB would point at a deleted asset after validation.
        var rollbackTargets = GetAssetTargets(rollbackAssets)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DeleteCreatedAssets(
            (journal.CreatedAssetPaths ?? [])
            .Where(path => !rollbackTargets.Contains(Path.GetFullPath(path)))
            .ToArray());
        DeleteCreatedAssets(
            GetAssetTargets(journal.StagedAssetsRoot)
                .Where(path => !rollbackTargets.Contains(Path.GetFullPath(path)))
                .ToArray());
        TryDeleteFile(replaced);
        var completed = rolling with
        {
            Phase = ProtectedRestoreJournalPhase.RolledBack,
            CreatedAssetPaths = []
        };
        store.Save(operationDirectory, completed);
        TryDeleteDirectory(rollbackValidation);
        TryDeleteDirectory(rollbackAssets);
        TryDeleteFile(rollbackDb);
    }

    private async Task<string> CreatePreRestoreAsync(
        string operationDirectory,
        InstallationStatus station,
        IReadOnlyList<string> migrations,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var buildRoot = Path.Combine(operationDirectory, "pre-restore-build");
        EnsureDirectorySafe(buildRoot, _paths.RecoveryRoot);
        var snapshot = Path.Combine(buildRoot, BackupPackageEngine.DatabaseEntryName);
        await _packageEngine.CreateSnapshotAsync(_paths.DatabasePath, snapshot, cancellationToken);
        EnsureValidDatabase(snapshot, migrations, allowOlderSchema: false);

        Directory.CreateDirectory(_paths.ProductBusinessAssetsRoot);
        Directory.CreateDirectory(_paths.AssetsRoot);
        var productAssets = await _packageEngine.ReadProductAssetsAsync(snapshot, cancellationToken);
        var managedAssets = await _packageEngine.ReadManagedAssetSourcesAsync(snapshot, cancellationToken);
        var trust = new MachineWideBackupAssetTrustBoundary(
            _paths.ProductBusinessAssetsRoot,
            _paths.AssetsRoot);
        var preparation = await trust.PrepareAsync(
            productAssets,
            managedAssets,
            buildRoot,
            cancellationToken);
        if (preparation.ContentStatus != BackupContentStatus.Healthy)
        {
            throw new InvalidDataException(
                "No se inicio el restore porque no fue posible crear un PRE_RESTORE Healthy del estado actual.");
        }

        var applied = await _packageEngine.ReadAppliedMigrationsAsync(snapshot, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var manifest = new BackupManifest(
            BackupPackageEngine.CurrentFormatVersion,
            operationId,
            now,
            now.ToLocalTime(),
            ResolveApplicationVersion(),
            "SQLite",
            applied,
            applied.LastOrDefault(),
            await BackupPackageEngine.ComputeSha256Async(snapshot, cancellationToken),
            new FileInfo(snapshot).Length,
            station.InstallationId!.Value,
            station.Mode!.Value,
            BackupKind.PreRestore,
            preparation.Assets,
            preparation.ContentStatus,
            preparation.OmittedAssetCount);
        var packagePath = Path.Combine(operationDirectory, "PRE_RESTORE.aibak");
        await _packageEngine.CreatePackageAsync(buildRoot, snapshot, manifest, packagePath, cancellationToken);
        var validation = await _packageEngine.ValidatePackageAsync(
            packagePath,
            migrations,
            Path.Combine(operationDirectory, "pre-restore-validation"),
            cancellationToken);
        if (!validation.IsValid || validation.Manifest?.Kind != BackupKind.PreRestore ||
            validation.Manifest.ContentStatus != BackupContentStatus.Healthy)
        {
            throw new InvalidDataException("El PRE_RESTORE no supero la validacion completa.");
        }

        TryDeleteDirectory(buildRoot);
        TryDeleteDirectory(Path.Combine(operationDirectory, "pre-restore-validation"));
        return packagePath;
    }

    private async Task StageAssetsAsync(
        string stagedDatabase,
        string extractedDirectory,
        BackupManifest manifest,
        string stagedAssetsRoot,
        CancellationToken cancellationToken)
    {
        EnsureDirectorySafe(stagedAssetsRoot, _paths.RecoveryRoot);
        var updates = new List<(int ProductId, string? ImagePath)>();
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (asset.WasMissing)
            {
                if (asset.OwnerKind is null)
                {
                    updates.Add((asset.ProductId, null));
                }

                continue;
            }

            var source = ResolveExtractedPath(extractedDirectory, asset.PackagePath);
            if (!File.Exists(source))
            {
                throw new InvalidDataException($"Falta el asset validado '{asset.PackagePath}'.");
            }

            if (asset.OwnerKind is null)
            {
                var extension = NormalizeExtension(Path.GetExtension(asset.PackagePath));
                var fileName = asset.Sha256.ToLowerInvariant() + extension;
                var liveTarget = Path.Combine(_paths.ProductBusinessAssetsRoot, fileName);
                var stagedTarget = Path.Combine(stagedAssetsRoot, "BusinessAssets", "Products", fileName);
                await CopyStagedAssetAsync(source, stagedTarget, asset.SizeBytes, asset.Sha256, cancellationToken);
                updates.Add((asset.ProductId, liveTarget));
                copied.Add(stagedTarget);
            }
            else
            {
                if (asset.OwnerKind is not ProductAssetOwnerKind.Product and not ProductAssetOwnerKind.Sku ||
                    string.IsNullOrWhiteSpace(asset.ManagedRelativePath) ||
                    !MachineWideAssetPathGuard.IsSafeRelativePath(asset.ManagedRelativePath, out var relative))
                {
                    throw new InvalidDataException("El asset administrado contiene una ruta relativa insegura.");
                }

                var expectedDirectory = asset.OwnerKind == ProductAssetOwnerKind.Product ? "Products" : "Skus";
                if (!MachineWideAssetPathGuard.TryResolveManagedPath(
                        asset.ManagedRelativePath,
                        _paths.AssetsRoot,
                        expectedDirectory,
                        out var liveTarget,
                        out relative,
                        out _))
                {
                    throw new InvalidDataException("El asset administrado queda fuera del root canonico.");
                }

                var stagedTarget = Path.Combine(
                    stagedAssetsRoot,
                    "Assets",
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!copied.Contains(stagedTarget))
                {
                    await CopyStagedAssetAsync(source, stagedTarget, asset.SizeBytes, asset.Sha256, cancellationToken);
                    copied.Add(stagedTarget);
                }
            }
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = stagedDatabase,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var updateData in updates)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE productos SET imagen_local_path = $path WHERE producto_id = $id;";
            update.Parameters.AddWithValue("$path", (object?)updateData.ImagePath ?? DBNull.Value);
            update.Parameters.AddWithValue("$id", updateData.ProductId);
            var affected = await update.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                throw new InvalidDataException($"El producto {updateData.ProductId} del manifest no existe en la base restaurada.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> PromoteAssetsAsync(
        string stagedAssetsRoot,
        CancellationToken cancellationToken)
    {
        EnsureDirectorySafe(stagedAssetsRoot, _paths.RecoveryRoot);
        var created = new List<string>();
        var mappings = new[]
        {
            (Source: Path.Combine(stagedAssetsRoot, "BusinessAssets", "Products"), Target: _paths.ProductBusinessAssetsRoot),
            (Source: Path.Combine(stagedAssetsRoot, "Assets"), Target: _paths.AssetsRoot)
        };
        foreach (var mapping in mappings)
        {
            if (!Directory.Exists(mapping.Source))
            {
                continue;
            }

            EnsureNoReparsePoints(mapping.Source, stagedAssetsRoot);
            Directory.CreateDirectory(mapping.Target);
            EnsureNoReparsePoints(mapping.Target, _paths.MachineDataRoot);
            foreach (var source in Directory.EnumerateFiles(mapping.Source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(source, mapping.Source);
                var relative = Path.GetRelativePath(mapping.Source, source);
                if (!MachineWideAssetPathGuard.IsSafeRelativePath(relative, out var normalized))
                {
                    throw new InvalidDataException("El staging de assets contiene una ruta insegura.");
                }

                var target = Path.GetFullPath(Path.Combine(
                    mapping.Target,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!MachineWideAssetPathGuard.IsWithin(target, mapping.Target))
                {
                    throw new InvalidDataException("El asset queda fuera de su root canonico.");
                }

                var sourceHash = await BackupPackageEngine.ComputeSha256Async(source, cancellationToken);
                var sourceSize = new FileInfo(source).Length;
                if (File.Exists(target))
                {
                    EnsureNoReparsePoints(target, mapping.Target);
                    var existingHash = await BackupPackageEngine.ComputeSha256Async(target, cancellationToken);
                    if (new FileInfo(target).Length == sourceSize && FixedEquals(existingHash, sourceHash))
                    {
                        continue;
                    }

                    // Replacing an existing canonical asset is part of a
                    // restore (the candidate may legitimately contain a
                    // newer image).  The target was checked as a regular
                    // non-reparse file; write a verified temporary sibling
                    // and atomically replace it so a partial copy is never
                    // exposed.  PRE_RESTORE supplies the source for rollback.
                    var temporary = Path.Combine(
                        Path.GetDirectoryName(target)!,
                        $".{Path.GetFileName(target)}.restore-{Guid.NewGuid():N}.tmp");
                    try
                    {
                        await BackupPackageEngine.CopyFileAsync(source, temporary, cancellationToken);
                        var copiedSize = new FileInfo(temporary).Length;
                        var promotedHash = await BackupPackageEngine.ComputeSha256Async(temporary, cancellationToken);
                        if (copiedSize != sourceSize || !FixedEquals(promotedHash, sourceHash))
                        {
                            throw new InvalidDataException($"El asset '{normalized}' cambio durante la promocion.");
                        }

                        File.Replace(temporary, target, null, ignoreMetadataErrors: true);
                    }
                    finally
                    {
                        TryDeleteFile(temporary);
                    }

                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                EnsureNoReparsePoints(Path.GetDirectoryName(target)!, mapping.Target);
                await BackupPackageEngine.CopyFileAsync(source, target, cancellationToken);
                var copiedHash = await BackupPackageEngine.ComputeSha256Async(target, cancellationToken);
                if (!FixedEquals(copiedHash, sourceHash))
                {
                    TryDeleteFile(target);
                    throw new InvalidDataException($"El asset '{normalized}' cambio durante la promocion.");
                }

                created.Add(target);
            }
        }

        return created;
    }

    private async Task ValidatePromotedAssetsAsync(
        string databasePath,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var asset in manifest.Assets.Where(asset => !asset.WasMissing))
        {
            string target;
            if (asset.OwnerKind is null)
            {
                target = Path.Combine(
                    _paths.ProductBusinessAssetsRoot,
                    asset.Sha256.ToLowerInvariant() + NormalizeExtension(Path.GetExtension(asset.PackagePath)));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(asset.ManagedRelativePath) ||
                    !MachineWideAssetPathGuard.IsSafeRelativePath(asset.ManagedRelativePath, out var relative))
                {
                    throw new InvalidDataException("El asset administrado restaurado tiene una ruta invalida.");
                }

                var expected = asset.OwnerKind == ProductAssetOwnerKind.Product ? "Products" : "Skus";
                if (!MachineWideAssetPathGuard.TryResolveManagedPath(
                        asset.ManagedRelativePath,
                        _paths.AssetsRoot,
                        expected,
                        out target,
                        out relative,
                        out _))
                {
                    throw new InvalidDataException("El asset administrado restaurado queda fuera del root.");
                }
            }

            if (!MachineWideAssetPathGuard.TryGetRegularFile(
                    target,
                    asset.OwnerKind is null ? _paths.ProductBusinessAssetsRoot : _paths.AssetsRoot,
                    out var file,
                    out _))
            {
                throw new InvalidDataException("Falta un asset canonico despues de la promocion.");
            }

            if (file!.Length != asset.SizeBytes ||
                !FixedEquals(await BackupPackageEngine.ComputeSha256Async(target, cancellationToken), asset.Sha256))
            {
                throw new InvalidDataException("El asset restaurado no coincide con el checksum del manifest.");
            }
        }

        var productAssets = manifest.Assets
            .Where(asset => asset.OwnerKind is null)
            .Select(asset => new
            {
                asset.ProductId,
                Expected = asset.WasMissing
                    ? null
                    : Path.Combine(_paths.ProductBusinessAssetsRoot,
                        asset.Sha256.ToLowerInvariant() + NormalizeExtension(Path.GetExtension(asset.PackagePath)))
            })
            .ToList();
        if (productAssets.Count == 0)
        {
            return;
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        foreach (var asset in productAssets)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT imagen_local_path FROM productos WHERE producto_id = $id;";
            command.Parameters.AddWithValue("$id", asset.ProductId);
            var rawActual = await command.ExecuteScalarAsync(cancellationToken);
            var actual = rawActual is null or DBNull ? null : rawActual.ToString();
            if (!PathEqualsNullable(actual, asset.Expected))
            {
                throw new InvalidDataException($"La referencia de imagen del producto {asset.ProductId} no coincide con los assets restaurados.");
            }
        }
    }

    private string ValidateCandidatePath(ProtectedRestoreRequest request)
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
            !PathEqualsNullable(Path.GetDirectoryName(candidate), root) ||
            !candidate.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El candidato no pertenece a una raiz de restore autorizada.");
        }

        if (!MachineWideAssetPathGuard.TryGetRegularFile(candidate, root, out _, out var issue))
        {
            throw new InvalidDataException($"El candidato no es un archivo regular valido ({issue}).");
        }

        return candidate;
    }

    private static void ValidateCandidateManifest(
        ProtectedRestoreRequest request,
        BackupManifest manifest)
    {
        if (manifest.Kind is not BackupKind.Manual and not BackupKind.Automatic)
        {
            throw new InvalidDataException("Solo se pueden restaurar respaldos Manual o Automatico.");
        }

        if (manifest.BackupId == Guid.Empty || manifest.SourceInstallationId == Guid.Empty)
        {
            throw new InvalidDataException("El manifest no tiene identidad de respaldo valida.");
        }

        if (request.ExpectedBackupId.HasValue && request.ExpectedBackupId.Value != manifest.BackupId)
        {
            throw new InvalidDataException("El BackupId del candidato no coincide con la seleccion original.");
        }

        if (request.ExpectedSourceInstallationId.HasValue &&
            request.ExpectedSourceInstallationId.Value != manifest.SourceInstallationId)
        {
            throw new InvalidDataException("La identidad de origen del candidato no coincide con la seleccion original.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedPackageSha256))
        {
            if (!IsSha256(request.ExpectedPackageSha256))
            {
                throw new InvalidDataException("El checksum esperado del candidato no es valido.");
            }
        }

        if (request.ExpectedContentStatus.HasValue && request.ExpectedContentStatus.Value != manifest.ContentStatus)
        {
            throw new InvalidDataException("El estado de contenido del candidato no coincide con la seleccion original.");
        }
    }

    private void EnsureJournalBelongsToCurrentInstallation(ProtectedRestoreJournalDocument journal)
    {
        var station = ReadStationStatus();
        if (!station.IsConfigured || station.InstallationId != journal.DestinationInstallationId || station.Mode != journal.DestinationMode)
        {
            throw new InvalidDataException("La identidad o el modo de la estacion destino cambiaron durante el restore.");
        }

        EnsureHashUnchanged(_paths.StationCorePath, journal.StationCoreSha256, "station core");
        EnsureOptionalHashUnchanged(_paths.CurrentStatePath, journal.CurrentPointerSha256, "CURRENT");
        EnsureOptionalHashUnchanged(_paths.UpdateStatePath, journal.UpdateStateSha256, "estado del updater");
    }

    /// <summary>
    /// Journals are protected state, but recovery must still treat their
    /// serialized paths as untrusted input.  A damaged or tampered journal
    /// must never make the elevated helper read a PRE_RESTORE or staged file
    /// outside its own Recovery operation, nor delete an arbitrary asset.
    /// </summary>
    private void EnsureJournalReferencesSafe(
        ProtectedRestoreJournalDocument journal,
        string operationDirectory)
    {
        var operation = Path.GetFullPath(operationDirectory);
        var restoreRoot = Path.Combine(_paths.RecoveryRoot, "Restore");
        if (!MachineWideAssetPathGuard.IsWithin(operation, restoreRoot) ||
            !PathEqualsNullable(Directory.GetParent(operation)?.FullName, restoreRoot))
        {
            throw new InvalidDataException("La operacion de restore no pertenece a Recovery\\Restore.");
        }

        EnsureOperationPath(journal.PreRestorePath, operation, "PRE_RESTORE");
        EnsureOperationPath(journal.StagedDatabasePath, operation, "DB staged");
        EnsureOperationPath(journal.StagedAssetsRoot, operation, "assets staged");

        var candidateRoot = journal.Source switch
        {
            ProtectedRestoreCandidateSource.LocalProtectedBackup => _paths.ProtectedBackupsRoot,
            ProtectedRestoreCandidateSource.CloudRecoveryCandidate => _paths.CloudRecoveryStagingRoot,
            ProtectedRestoreCandidateSource.LocalStagedBackup => _paths.LocalRestoreStagingRoot,
            _ => throw new InvalidDataException("El journal declara una fuente de restore no reconocida.")
        };
        var candidate = Path.GetFullPath(journal.CandidatePath);
        if (!MachineWideAssetPathGuard.IsWithin(candidate, candidateRoot) ||
            (journal.Source is ProtectedRestoreCandidateSource.LocalProtectedBackup or ProtectedRestoreCandidateSource.LocalStagedBackup) &&
            !PathEqualsNullable(Path.GetDirectoryName(candidate), candidateRoot) ||
            !candidate.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase) ||
            !MachineWideAssetPathGuard.TryEnsureNoReparsePoints(candidate, candidateRoot, out _))
        {
            throw new InvalidDataException("El candidato del journal queda fuera de una raiz autorizada.");
        }

        if (!IsSha256(journal.CandidateSha256) || !IsSha256(journal.StationCoreSha256) ||
            journal.CurrentPointerSha256 is not null && !IsSha256(journal.CurrentPointerSha256) ||
            journal.UpdateStateSha256 is not null && !IsSha256(journal.UpdateStateSha256))
        {
            throw new InvalidDataException("El journal contiene hashes invalidos.");
        }

        foreach (var assetPath in journal.CreatedAssetPaths ?? [])
        {
            var full = Path.GetFullPath(assetPath);
            var allowedRoot = MachineWideAssetPathGuard.IsWithin(full, _paths.ProductBusinessAssetsRoot)
                ? _paths.ProductBusinessAssetsRoot
                : MachineWideAssetPathGuard.IsWithin(full, _paths.AssetsRoot)
                    ? _paths.AssetsRoot
                    : null;
            if (allowedRoot is null ||
                !MachineWideAssetPathGuard.TryEnsureNoReparsePoints(full, allowedRoot, out _))
            {
                throw new InvalidDataException("El journal contiene un asset fuera de los roots canonicos.");
            }
        }
    }

    private static void EnsureOperationPath(string path, string operationDirectory, string description)
    {
        var full = Path.GetFullPath(path);
        if (!MachineWideAssetPathGuard.IsWithin(full, operationDirectory) ||
            !MachineWideAssetPathGuard.TryEnsureNoReparsePoints(full, operationDirectory, out _))
        {
            throw new InvalidDataException($"La referencia {description} queda fuera de la operacion de restore.");
        }
    }

    private InstallationStatus ReadStationStatus()
    {
        var status = new LocalStationConfigurationStore(_paths).Load().Status;
        if (!status.IsConfigured || !status.InstallationId.HasValue || !status.Mode.HasValue)
        {
            throw new InvalidOperationException("La estacion machine-wide debe estar configurada antes de restaurar.");
        }

        return status;
    }

    private void EnsureMachineWideAndElevated(bool allowSyntheticRoots)
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException("El restore protegido requiere una instalacion machine-wide.");
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, allowSyntheticRoots);
        if (!_isElevated())
        {
            throw new UnauthorizedAccessException("El helper de restore debe ejecutarse elevado mediante UAC.");
        }
    }

    private IReadOnlyList<string> GetCurrentMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = _paths.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString())
            .Options;
        using var context = new AppDbContext(options);
        return context.Database.GetMigrations().ToList();
    }

    private void EnsureValidDatabase(
        string path,
        IReadOnlyList<string> migrations,
        bool allowOlderSchema)
    {
        var errors = _packageEngine.ValidateDatabaseAsync(
            path,
            migrations,
            allowOlderSchema,
            CancellationToken.None).GetAwaiter().GetResult();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", errors));
        }
    }

    private void EnsureDatabaseUnlocked()
    {
        if (!File.Exists(_paths.DatabasePath))
        {
            throw new FileNotFoundException("No existe la base de datos activa.", _paths.DatabasePath);
        }

        try
        {
            using var stream = new FileStream(
                _paths.DatabasePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.SequentialScan);
        }
        catch (IOException ex)
        {
            throw new IOException("La base de datos sigue abierta por otro proceso; el restore fue abortado.", ex);
        }
    }

    private async Task CopyStagedAssetAsync(
        string source,
        string target,
        long expectedSize,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        EnsureDirectorySafe(Path.GetDirectoryName(target)!, _paths.RecoveryRoot);
        if (File.Exists(target))
        {
            if (new FileInfo(target).Length != expectedSize ||
                !FixedEquals(await BackupPackageEngine.ComputeSha256Async(target, cancellationToken), expectedHash))
            {
                throw new InvalidDataException("El staging contiene un asset duplicado con metadata distinta.");
            }

            return;
        }

        await BackupPackageEngine.CopyFileAsync(source, target, cancellationToken);
        if (new FileInfo(target).Length != expectedSize ||
            !FixedEquals(await BackupPackageEngine.ComputeSha256Async(target, cancellationToken), expectedHash))
        {
            TryDeleteFile(target);
            throw new InvalidDataException("El asset cambio durante el staging.");
        }
    }

    private void DeleteCreatedAssets(IReadOnlyList<string>? paths)
    {
        if (paths is null)
        {
            return;
        }

        foreach (var path in paths)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!MachineWideAssetPathGuard.IsWithin(full, _paths.MachineDataRoot))
                {
                    continue;
                }

                var allowed = MachineWideAssetPathGuard.IsWithin(full, _paths.ProductBusinessAssetsRoot) ||
                              MachineWideAssetPathGuard.IsWithin(full, _paths.AssetsRoot);
                if (allowed && File.Exists(full))
                {
                    TryDeleteFile(full);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }
        }
    }

    private IReadOnlyList<string> GetAssetTargets(string stagedAssetsRoot)
    {
        var result = new List<string>();
        foreach (var mapping in new[]
                 {
                     (Source: Path.Combine(stagedAssetsRoot, "BusinessAssets", "Products"), Target: _paths.ProductBusinessAssetsRoot),
                     (Source: Path.Combine(stagedAssetsRoot, "Assets"), Target: _paths.AssetsRoot)
                 })
        {
            if (!Directory.Exists(mapping.Source)) continue;
            foreach (var source in Directory.EnumerateFiles(mapping.Source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(mapping.Source, source);
                if (MachineWideAssetPathGuard.IsSafeRelativePath(relative, out var normalized))
                {
                    result.Add(Path.GetFullPath(Path.Combine(mapping.Target, normalized.Replace('/', Path.DirectorySeparatorChar))));
                }
            }
        }

        return result;
    }

    private static string ResolveExtractedPath(string extractionRoot, string packagePath)
    {
        if (!MachineWideAssetPathGuard.IsSafeRelativePath(packagePath, out var normalized))
        {
            throw new InvalidDataException("La ruta de asset extraida no es segura.");
        }

        var path = Path.GetFullPath(Path.Combine(
            extractionRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!MachineWideAssetPathGuard.IsWithin(path, extractionRoot))
        {
            throw new InvalidDataException("La ruta de asset extraida queda fuera del staging.");
        }

        EnsureNoReparsePoints(path, extractionRoot);
        return path;
    }

    private static void EnsureDirectorySafe(string path, string root)
    {
        var full = Path.GetFullPath(path);
        if (!MachineWideAssetPathGuard.IsWithin(full, root))
        {
            throw new InvalidDataException("El directorio de staging queda fuera de Recovery.");
        }

        Directory.CreateDirectory(full);
        EnsureNoReparsePoints(full, root);
    }

    private static void EnsureNoReparsePoints(string path, string root)
    {
        if (!MachineWideAssetPathGuard.IsWithin(path, root))
        {
            throw new InvalidDataException("La ruta administrada queda fuera de su root.");
        }

        if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(path, root, out var issue))
        {
            throw new InvalidDataException($"La ruta contiene un reparse point o no es accesible ({issue}).");
        }
    }

    private void EnsureHashUnchanged(string path, string expected, string description)
    {
        var actual = ComputeOptionalHash(path);
        if (!FixedEquals(actual, expected))
        {
            throw new InvalidDataException($"El {description} cambio durante el restore.");
        }
    }

    private void EnsureOptionalHashUnchanged(string path, string? expected, string description)
    {
        var actual = ComputeOptionalHash(path);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"El {description} cambio durante el restore.");
        }
    }

    private static string? ComputeOptionalHash(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return BackupPackageEngine.ComputeSha256Async(path, CancellationToken.None).GetAwaiter().GetResult();
    }

    private void WriteEvent(string eventName, InstallationMode? mode, string description)
    {
        try
        {
            new StationEventLog(_paths, _timeProvider).Write(eventName, "admin", mode, description);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ValidateRequest(ProtectedRestoreRequest request)
    {
        if (!request.ConfirmationAccepted)
        {
            throw new InvalidOperationException("Debe confirmar el reemplazo de la base de datos actual.");
        }

        if (string.IsNullOrWhiteSpace(request.CandidatePath))
        {
            throw new ArgumentException("El candidato de restore es obligatorio.", nameof(request));
        }

        if (!Enum.IsDefined(request.Source))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Source));
        }

        if (request.Source == ProtectedRestoreCandidateSource.CloudRecoveryCandidate &&
            (!request.ExpectedBackupId.HasValue || !request.ExpectedSourceInstallationId.HasValue ||
             string.IsNullOrWhiteSpace(request.ExpectedPackageSha256) ||
             !IsSha256(request.ExpectedPackageSha256)))
        {
            throw new InvalidDataException("Un CloudRecoveryCandidate debe incluir BackupId, instalacion y SHA-256 esperados.");
        }

        if (request.Source == ProtectedRestoreCandidateSource.LocalStagedBackup &&
            (string.IsNullOrWhiteSpace(request.ExpectedPackageSha256) || !IsSha256(request.ExpectedPackageSha256)))
        {
            throw new InvalidDataException("Un LocalStagedBackup debe incluir el SHA-256 esperado de la importacion.");
        }
    }

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool FixedEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToUpperInvariant()),
            Encoding.ASCII.GetBytes(right.ToUpperInvariant()));
    }

    private static bool PathEqualsNullable(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Length > 12 ||
            extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
        {
            return ".bin";
        }

        return extension.ToLowerInvariant();
    }

    private static string ResolveApplicationVersion()
    {
        var process = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(process) && File.Exists(process))
        {
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(process).ProductVersion;
            if (!string.IsNullOrWhiteSpace(version)) return version;
        }

        return typeof(ProtectedRestoreEngine).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static void DeleteDatabaseSidecars(string path)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            TryDeleteFile(path + suffix);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
}

internal interface IProtectedRestoreLock : IDisposable
{
    bool TryAcquire();
}

internal sealed class ProtectedRestoreLock : IProtectedRestoreLock
{
    // A named Mutex is thread-affine: an async restore can resume on a
    // different thread and ReleaseMutex would then throw
    // SynchronizationLockException. Semaphore has the same cross-process
    // exclusion without imposing thread ownership.
    private readonly Semaphore? _semaphore;
    private bool _acquired;

    public ProtectedRestoreLock(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            _semaphore = new Semaphore(1, 1, name);
        }
    }

    public bool TryAcquire()
    {
        if (_semaphore is null)
        {
            _acquired = true;
            return true;
        }

        try
        {
            _acquired = _semaphore.WaitOne(TimeSpan.Zero);
            return _acquired;
        }
        catch (AbandonedMutexException)
        {
            _acquired = true;
            return true;
        }
    }

    public void Dispose()
    {
        if (_acquired)
        {
            _semaphore?.Release();
            _acquired = false;
        }

        _semaphore?.Dispose();
    }
}
