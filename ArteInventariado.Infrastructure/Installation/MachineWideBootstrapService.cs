using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Prepares the machine-wide layout and migrates a v1.11 per-user
/// installation into it. The operation is explicit: the normal application
/// startup continues to use the legacy layout until a later installation/ACL
/// block enables machine-wide execution.
/// </summary>
public sealed class MachineWideBootstrapService
{
    private const int BootstrapSchemaVersion = 1;
    private const string StagingDirectoryName = "BootstrapStaging";
    private readonly BackupPackageEngine _backupEngine;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string>? _phaseObserver;
    private readonly MachineWideBootstrapLogWriter? _logWriter;

    public MachineWideBootstrapService(
        Action<string>? phaseObserver = null,
        MachineWideBootstrapLogWriter? logWriter = null)
        : this(new BackupPackageEngine(), TimeProvider.System, phaseObserver, logWriter)
    {
    }

    internal MachineWideBootstrapService(
        BackupPackageEngine backupEngine,
        TimeProvider timeProvider,
        Action<string>? phaseObserver = null,
        MachineWideBootstrapLogWriter? logWriter = null)
    {
        _backupEngine = backupEngine;
        _timeProvider = timeProvider;
        _phaseObserver = phaseObserver;
        _logWriter = logWriter;
    }

    /// <summary>
    /// Creates the machine-wide directory tree. When a published payload is
    /// supplied, its version and stable launcher/updater are installed using
    /// the same side-by-side conventions as the existing deployment bootstrap.
    /// No shortcuts, ACLs, UAC elevation or database initialization happen
    /// here.
    /// </summary>
    public async Task<MachineWideBootstrapResult> PrepareCleanInstallAsync(
        ArteInventariadoPathProvider target,
        string? publishedPayloadDirectory = null,
        CancellationToken cancellationToken = default,
        bool initializeStation = true)
    {
        EnsureMachineWideTarget(target);
        Phase("target-validated");
        EnsureTargetDirectories(target);
        Phase("roots-ready");

        // Resolve and validate the requested payload before trusting an
        // existing completion marker.  A marker only proves that some
        // previous installation completed; it does not prove that the
        // version requested by this invocation is the one installed now.
        string? payload = null;
        string? requestedVersion = null;
        if (!string.IsNullOrWhiteSpace(publishedPayloadDirectory))
        {
            (payload, requestedVersion) = ValidatePublishedPayload(publishedPayloadDirectory);
        }

        var marker = ReadMarker(target);
        if (marker is not null)
        {
            EnsureMarkerMatchesTarget(marker, target);
            var markerVersion = TryNormalizeVersion(marker.CurrentVersion);
            var readinessVersion = requestedVersion ?? markerVersion;
            if (readinessVersion is not null &&
                await IsInstalledStateReady(target, marker, readinessVersion, payload, cancellationToken))
            {
                Phase("already-ready");
                return AlreadyReady(target, marker);
            }

            // The marker is stale or incomplete (for example, the physical
            // ib2 installation with ib3 requested and no scheduled tasks).
            // Continue through the normal idempotent promotion path instead
            // of returning a false AlreadyReady result.
        }

        if (string.IsNullOrWhiteSpace(publishedPayloadDirectory))
        {
            if (initializeStation)
            {
                EnsureMachineWideStationInitialized(target);
                Phase("station-initialized");
            }
            Phase("directory-bootstrap-complete");
            return new MachineWideBootstrapResult(
                MachineWideBootstrapOutcome.CleanInstallPrepared,
                target.MachineInstallRoot,
                target.MachineDataRoot,
                target.UserDataRoot,
                false,
                null,
                null,
                []);
        }

        payload ??= Path.GetFullPath(publishedPayloadDirectory);
        Phase("payload-validating");
        RejectReparsePoint(payload);
        var executable = Path.Combine(payload, UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "El payload de instalacion no contiene ArteInventariado.WinForms.exe.",
                executable);
        }

        var version = requestedVersion ?? UpdateVersion.Sanitize(
            FileVersionInfo.GetVersionInfo(executable).ProductVersion
            ?? throw new InvalidDataException("El ejecutable publicado no declara ProductVersion."));
        Phase("payload-version-validated");
        var created = new List<string>();
        var stagingRoot = CreateStagingRoot(target);
        var previousPointerBytes = TryReadFileBytes(target.CurrentStatePath);
        var replacedStableFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Phase("staging-created");
        try
        {
            var stagedInstall = Path.Combine(stagingRoot, "Install");
            var stagedData = Path.Combine(stagingRoot, "Data");
            var stagedVersion = Path.Combine(stagedInstall, "Versions", version);
            await CopyDirectorySafelyAsync(payload, stagedVersion, cancellationToken);
            Phase("payload-staged");
            await CopyStableComponentAsync(
                payload,
                StableDeploymentBootstrap.LauncherExecutableName,
                Path.Combine(stagedInstall, StableDeploymentBootstrap.LauncherExecutableName),
                cancellationToken);
            await CopyStableComponentAsync(
                payload,
                StableDeploymentBootstrap.UpdaterExecutableName,
                Path.Combine(stagedInstall, StableDeploymentBootstrap.UpdaterExecutableName),
                cancellationToken);
            await CopyOptionalStableComponentAsync(
                payload,
                ArteInventariadoPathProvider.StationHelperFileName,
                Path.Combine(stagedInstall, ArteInventariadoPathProvider.StationHelperFileName),
                cancellationToken);
            await CopyOptionalStableComponentAsync(
                payload,
                ArteInventariadoPathProvider.BackupRunnerFileName,
                Path.Combine(stagedInstall, ArteInventariadoPathProvider.BackupRunnerFileName),
                cancellationToken);
            await CopyOptionalStableComponentAsync(
                payload,
                ArteInventariadoPathProvider.CloudBackupRunnerFileName,
                Path.Combine(stagedInstall, ArteInventariadoPathProvider.CloudBackupRunnerFileName),
                cancellationToken);
            foreach (var supportFileName in StableEntryPointInstaller.RunnerRuntimeSupportFileNames)
            {
                await CopyOptionalStableComponentAsync(
                    payload,
                    supportFileName,
                    Path.Combine(stagedInstall, supportFileName),
                    cancellationToken);
            }
            Phase("stable-components-staged");

            var stagedPointer = Path.Combine(stagedData, "UpdateState", ArteInventariadoPathProvider.CurrentStateFileName);
            var stagedExecutable = Path.Combine(target.GetVersionDirectory(version), UpdateLaunchTargetResolver.ApplicationExecutableName);
            new UpdatePointerStore(stagedPointer).Save(version, stagedExecutable, null, _timeProvider.GetUtcNow());
            Phase("pointer-staged");

            await PromoteDirectoryAsync(
                stagedInstall,
                target.MachineInstallRoot,
                created,
                cancellationToken,
                replaceableRoot: target.MachineInstallRoot,
                replacedFiles: replacedStableFiles,
                atomicVersionDirectories: true);
            Phase("versions-promoted");
            await PromoteDirectoryAsync(
                stagedData,
                target.MachineDataRoot,
                created,
                cancellationToken,
                skipCurrentPointer: true);
            await PromoteCurrentPointerAsync(
                stagedPointer,
                target,
                created,
                cancellationToken);
            Phase("state-promoted");
            EnsureMachineWideStationInitialized(target);
            Phase("station-initialized");
            ValidateInstalledPointer(target, version, null);
            Phase("pointer-validated");
            await WriteMarkerAsync(
                target,
                new MachineWideBootstrapMarker(
                    BootstrapSchemaVersion,
                    null,
                    null,
                    null,
                    version,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
            Phase("marker-written");

            Phase("completed");
            return new MachineWideBootstrapResult(
                MachineWideBootstrapOutcome.CleanInstallPrepared,
                target.MachineInstallRoot,
                target.MachineDataRoot,
                target.UserDataRoot,
                false,
                null,
                null,
                created);
        }
        catch
        {
            RestoreFileBytes(target.CurrentStatePath, previousPointerBytes);
            RestoreReplacedFiles(replacedStableFiles);
            RemoveCreatedFiles(created);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            TryDeleteEmptyDirectory(Path.GetDirectoryName(stagingRoot)!);
        }
    }

    /// <summary>
    /// Migrates the selected legacy v1.11 installation. The source is never
    /// moved or deleted. Existing target files are accepted only when their
    /// verified content is identical; conflicting installations fail closed.
    /// </summary>
    public async Task<MachineWideBootstrapResult> MigrateLegacyAsync(
        ArteInventariadoPathProvider legacy,
        ArteInventariadoPathProvider target,
        bool migrateValidBackups = true,
        IReadOnlyList<string>? currentMigrations = null,
        CancellationToken cancellationToken = default,
        string? publishedPayloadDirectory = null)
    {
        EnsureLegacySource(legacy);
        EnsureMachineWideTarget(target);
        EnsureRootsDoNotOverlap(legacy, target);
        EnsureTargetDirectories(target);

        // When a published payload is supplied, it is the requested version
        // for the migrated installation (not merely an optional helper
        // source). Validate it before reading any existing target marker so a
        // retry cannot promote an untrusted or inconsistent payload.
        (string PayloadRoot, string Version)? publishedPayload = string.IsNullOrWhiteSpace(publishedPayloadDirectory)
            ? null
            : ValidatePublishedPayload(publishedPayloadDirectory);
        var requestedVersion = publishedPayload?.Version;
        var payloadRoot = publishedPayload?.PayloadRoot;

        var existingMarker = ReadMarker(target);
        if (existingMarker is not null)
        {
            EnsureMarkerMatchesTarget(existingMarker, target);
            var sourceFingerprint = ComputeRootFingerprint(legacy.MachineDataRoot);
            if (!string.Equals(existingMarker.SourceLegacyFingerprint, sourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El destino ya fue preparado desde otra instalacion legacy; no se mezclaran instalaciones.");
            }

            var markerVersion = TryNormalizeVersion(existingMarker.CurrentVersion);
            if (markerVersion is not null &&
                await IsInstalledStateReady(
                    target,
                    existingMarker,
                    requestedVersion ?? markerVersion,
                    payloadRoot,
                    cancellationToken))
            {
                return AlreadyReady(target, existingMarker);
            }

            // An incomplete marker (for example, a valid installation whose
            // required scheduled task was removed) is repaired by continuing
            // through the verified migration/promotion path.
        }

        var station = LoadReadyStation(legacy.StationSettingsPath);
        EnsureTargetIdentityCompatible(target, station);
        var pointer = LoadValidPointer(legacy);
        var sourceState = LoadMigratableUpdateState(legacy.UpdateStatePath);
        var migrations = currentMigrations ?? ReadCurrentMigrations();
        var created = new List<string>();
        var ignoredBackups = new List<string>();
        var stagingRoot = CreateStagingRoot(target);
        var previousPointerBytes = TryReadFileBytes(target.CurrentStatePath);
        var replacedStableFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stagedInstall = Path.Combine(stagingRoot, "Install");
            var stagedData = Path.Combine(stagingRoot, "Data");
            var stagedDatabase = Path.Combine(stagedData, "Data", ArteInventariadoPathProvider.DatabaseFileName);

            RejectReparsePoint(legacy.DatabasePath);
            await _backupEngine.CreateSnapshotAsync(
                legacy.DatabasePath,
                stagedDatabase,
                cancellationToken);
            // A legacy database can be valid while it is behind the payload's
            // schema. Validate the prefix first, migrate only the isolated
            // snapshot, then require an exact schema before promotion.
            await ValidateDatabaseAsync(
                stagedDatabase,
                migrations,
                cancellationToken,
                allowOlderSchema: true);
            await MigrateStagedDatabaseAsync(stagedDatabase, cancellationToken);
            await ValidateDatabaseAsync(
                stagedDatabase,
                migrations,
                cancellationToken,
                allowOlderSchema: false);

            var stagedStation = Path.Combine(stagedData, "Station", ArteInventariadoPathProvider.StationSettingsFileName);
            await WriteStationCoreCopyAsync(
                legacy.StationSettingsPath,
                stagedStation,
                station.InstallationId!.Value,
                station.Mode!.Value,
                cancellationToken);
            ValidateStationCopy(stagedStation, station.InstallationId!.Value, station.Mode!.Value);

            // The legacy file contains both identity and operational
            // preferences. Machine-wide keeps the identity in the protected
            // core path and writes a settings-only document to the user-
            // writable operational directory.
            var stagedOperationalStation = Path.Combine(
                stagedData,
                "Station",
                ArteInventariadoPathProvider.StationOperationalDirectoryName,
                ArteInventariadoPathProvider.StationSettingsFileName);
            await WriteOperationalStationCopyAsync(
                legacy.StationSettingsPath,
                stagedOperationalStation,
                legacy,
                target,
                cancellationToken);

            if (Directory.Exists(legacy.AssetsRoot))
            {
                await CopyDirectorySafelyAsync(
                    legacy.AssetsRoot,
                    Path.Combine(stagedData, "Assets"),
                    cancellationToken);
            }

            if (Directory.Exists(legacy.ProductBusinessAssetsRoot))
            {
                var stagedProductAssetsRoot = Path.Combine(stagedData, "BusinessAssets", "Products");
                await CopyDirectorySafelyAsync(
                    legacy.ProductBusinessAssetsRoot,
                    stagedProductAssetsRoot,
                    cancellationToken);
                await _backupEngine.CanonicalizeLegacyProductAssetPathsAsync(
                    stagedDatabase,
                    legacy.ProductBusinessAssetsRoot,
                    stagedProductAssetsRoot,
                    target.ProductBusinessAssetsRoot,
                    cancellationToken);
            }

            var legacyCurrentVersion = UpdateVersion.Sanitize(pointer.Version);
            var currentVersion = requestedVersion ?? legacyCurrentVersion;
            var currentSourceDirectory = Path.GetDirectoryName(pointer.ExecutablePath)
                ?? throw new InvalidDataException("CURRENT no tiene directorio de version.");
            if (payloadRoot is not null)
            {
                await CopyDirectorySafelyAsync(
                    payloadRoot,
                    Path.Combine(stagedInstall, "Versions", currentVersion),
                    cancellationToken);
            }
            else
            {
                await CopyDirectorySafelyAsync(
                    currentSourceDirectory,
                    Path.Combine(stagedInstall, "Versions", currentVersion),
                    cancellationToken);
            }

            // A supplied payload is an actual upgrade. Preserve the legacy
            // current version as PREVIOUS so rollback remains possible. With
            // no payload, retain the legacy CURRENT/PREVIOUS relationship.
            var previousVersion = payloadRoot is not null &&
                                  !string.Equals(currentVersion, legacyCurrentVersion, StringComparison.OrdinalIgnoreCase)
                ? legacyCurrentVersion
                : GetPreviousVersion(pointer);
            if (!string.IsNullOrWhiteSpace(previousVersion) &&
                !string.Equals(previousVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
            {
                var previousDirectory = string.Equals(previousVersion, legacyCurrentVersion, StringComparison.OrdinalIgnoreCase)
                    ? currentSourceDirectory
                    : Path.GetDirectoryName(pointer.PreviousExecutablePath!)
                    ?? throw new InvalidDataException("PREVIOUS no tiene directorio de version.");
                await CopyDirectorySafelyAsync(
                    previousDirectory,
                    Path.Combine(stagedInstall, "Versions", previousVersion),
                    cancellationToken);
            }

            if (payloadRoot is not null)
            {
                await CopyStableComponentAsync(
                    payloadRoot,
                    StableDeploymentBootstrap.LauncherExecutableName,
                    Path.Combine(stagedInstall, StableDeploymentBootstrap.LauncherExecutableName),
                    cancellationToken);
                await CopyStableComponentAsync(
                    payloadRoot,
                    StableDeploymentBootstrap.UpdaterExecutableName,
                    Path.Combine(stagedInstall, StableDeploymentBootstrap.UpdaterExecutableName),
                    cancellationToken);
            }
            else
            {
                await CopyStableComponentFromLegacyAsync(
                    legacy,
                    pointer,
                    StableDeploymentBootstrap.LauncherExecutableName,
                    Path.Combine(stagedInstall, StableDeploymentBootstrap.LauncherExecutableName),
                    cancellationToken);
                await CopyStableComponentFromLegacyAsync(
                    legacy,
                    pointer,
                    StableDeploymentBootstrap.UpdaterExecutableName,
                    Path.Combine(stagedInstall, StableDeploymentBootstrap.UpdaterExecutableName),
                    cancellationToken);
            }
            var stagedHelper = Path.Combine(
                stagedInstall,
                ArteInventariadoPathProvider.StationHelperFileName);
            _ = await CopyOptionalStableComponentFromPayloadAsync(
                    payloadRoot,
                    ArteInventariadoPathProvider.StationHelperFileName,
                    stagedHelper,
                    cancellationToken);
            var stagedBackupRunner = Path.Combine(
                stagedInstall,
                ArteInventariadoPathProvider.BackupRunnerFileName);
            _ = await CopyOptionalStableComponentFromPayloadAsync(
                    payloadRoot,
                    ArteInventariadoPathProvider.BackupRunnerFileName,
                    stagedBackupRunner,
                    cancellationToken);
            var stagedCloudBackupRunner = Path.Combine(
                stagedInstall,
                ArteInventariadoPathProvider.CloudBackupRunnerFileName);
            _ = await CopyOptionalStableComponentFromPayloadAsync(
                    payloadRoot,
                    ArteInventariadoPathProvider.CloudBackupRunnerFileName,
                    stagedCloudBackupRunner,
                    cancellationToken);
            foreach (var supportFileName in StableEntryPointInstaller.RunnerRuntimeSupportFileNames)
            {
                _ = await CopyOptionalStableComponentFromPayloadAsync(
                    payloadRoot,
                    supportFileName,
                    Path.Combine(stagedInstall, supportFileName),
                    cancellationToken);
            }

            var targetCurrent = Path.Combine(
                target.GetVersionDirectory(currentVersion),
                UpdateLaunchTargetResolver.ApplicationExecutableName);
            var targetPrevious = string.IsNullOrWhiteSpace(previousVersion)
                ? null
                : Path.Combine(
                    target.GetVersionDirectory(previousVersion!),
                    Path.GetFileName(pointer.PreviousExecutablePath!));
            var stagedPointer = Path.Combine(stagedData, "UpdateState", ArteInventariadoPathProvider.CurrentStateFileName);
            new UpdatePointerStore(stagedPointer).Save(
                currentVersion,
                targetCurrent,
                targetPrevious,
                pointer.UpdatedUtc);

            var mappedState = await StageUpdateStateAsync(
                sourceState,
                legacy,
                target,
                stagedInstall,
                stagedData,
                cancellationToken);

            if (migrateValidBackups && Directory.Exists(legacy.ProtectedBackupsRoot))
            {
                await StageValidBackupsAsync(
                    legacy,
                    stagedData,
                    migrations,
                    station.InstallationId!.Value,
                    station.Mode!.Value,
                    ignoredBackups,
                    cancellationToken);
            }

            Phase("staged");
            await PromoteDirectoryAsync(
                stagedInstall,
                target.MachineInstallRoot,
                created,
                cancellationToken,
                replaceableRoot: target.MachineInstallRoot,
                replacedFiles: replacedStableFiles,
                atomicVersionDirectories: true);
            await PromoteDirectoryAsync(
                stagedData,
                target.MachineDataRoot,
                created,
                cancellationToken,
                skipCurrentPointer: true);
            await PromoteCurrentPointerAsync(
                stagedPointer,
                target,
                created,
                cancellationToken);
            ValidateInstalledPointer(target, currentVersion, targetPrevious);
            await ValidateDatabaseAsync(target.DatabasePath, migrations, cancellationToken);
            ValidateStationCopy(target, station.InstallationId!.Value, station.Mode!.Value);

            if (mappedState is not null)
            {
                // StageUpdateStateAsync already wrote the target-relative state;
                // this branch documents that no legacy absolute path remains.
                EnsureNoLegacyReferences(mappedState, legacy.MachineDataRoot);
            }

            await WriteMarkerAsync(
                target,
                new MachineWideBootstrapMarker(
                    BootstrapSchemaVersion,
                    station.InstallationId,
                    station.Mode,
                    ComputeRootFingerprint(legacy.MachineDataRoot),
                    currentVersion,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
            Phase("completed");

            return new MachineWideBootstrapResult(
                MachineWideBootstrapOutcome.Migrated,
                target.MachineInstallRoot,
                target.MachineDataRoot,
                target.UserDataRoot,
                true,
                station.InstallationId,
                station.Mode,
                created,
                ignoredBackups);
        }
        catch
        {
            RestoreFileBytes(target.CurrentStatePath, previousPointerBytes);
            RestoreReplacedFiles(replacedStableFiles);
            RemoveCreatedFiles(created);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            TryDeleteEmptyDirectory(Path.GetDirectoryName(stagingRoot)!);
        }
    }

    /// <summary>
    /// Convenience overload for the explicit v1.11 profile on the current
    /// Windows user. Callers that need test or staged roots should use the
    /// provider-to-provider overload above.
    /// </summary>
    public Task<MachineWideBootstrapResult> MigrateLegacyAsync(
        ArteInventariadoPathProvider target,
        bool migrateValidBackups = true,
        IReadOnlyList<string>? currentMigrations = null,
        CancellationToken cancellationToken = default) =>
        MigrateLegacyAsync(
            ArteInventariadoPathProvider.CreateLegacyDefault(),
            target,
            migrateValidBackups,
            currentMigrations,
            cancellationToken);

    private async Task<UpdateStateDocument?> StageUpdateStateAsync(
        UpdateStateDocument? state,
        ArteInventariadoPathProvider legacy,
        ArteInventariadoPathProvider target,
        string stagedInstall,
        string stagedData,
        CancellationToken cancellationToken)
    {
        if (state is null)
        {
            return null;
        }

        var source = await MapStatePathAsync(
            state.SourceApplicationPath,
            legacy,
            target,
            stagedInstall,
            stagedData,
            required: false,
            cancellationToken);
        var previous = await MapStatePathAsync(
            state.PreviousVersionPath,
            legacy,
            target,
            stagedInstall,
            stagedData,
            required: false,
            cancellationToken);
        var current = await MapStatePathAsync(
            state.CurrentVersionPath,
            legacy,
            target,
            stagedInstall,
            stagedData,
            required: false,
            cancellationToken);
        var package = await MapStatePathAsync(
            state.PackagePath,
            legacy,
            target,
            stagedInstall,
            stagedData,
            required: false,
            cancellationToken);
        var manifest = await MapStatePathAsync(
            state.ManifestPath,
            legacy,
            target,
            stagedInstall,
            stagedData,
            required: false,
            cancellationToken);
        var preUpdate = await MapStatePathAsync(
            state.PreUpdateBackupPath,
            legacy,
            target,
            stagedInstall,
            stagedData,
            required: false,
            cancellationToken);
        var stationBackup = await MapStatePathAsync(
            state.StationSettingsBackupPath,
            legacy,
            target,
            stagedInstall,
            stagedData,
            required: false,
            cancellationToken);

        var mapped = state with
        {
            PackagePath = package ?? string.Empty,
            ManifestPath = manifest ?? string.Empty,
            PreUpdateBackupPath = preUpdate,
            PreviousVersionPath = previous,
            CurrentVersionPath = current,
            SourceApplicationPath = source,
            StationSettingsBackupPath = stationBackup
        };

        var stagedState = Path.Combine(stagedData, "UpdateState", ArteInventariadoPathProvider.UpdateStateFileName);
        new UpdateStateStore(stagedState).Save(mapped);
        return mapped;
    }

    private async Task<string?> MapStatePathAsync(
        string? sourcePath,
        ArteInventariadoPathProvider legacy,
        ArteInventariadoPathProvider target,
        string stagedInstall,
        string stagedData,
        bool required,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            if (required)
            {
                throw new FileNotFoundException("Falta un archivo referenciado por update-state.json.", source);
            }

            return null;
        }

        string destination;
        string stagedDestination;
        if (IsWithin(source, legacy.VersionsRoot))
        {
            var relative = SafeRelativePath(legacy.VersionsRoot, source);
            destination = Path.Combine(target.VersionsRoot, relative);
            stagedDestination = Path.Combine(stagedInstall, "Versions", relative);
        }
        else if (IsWithin(source, legacy.ProtectedBackupsRoot))
        {
            var relative = SafeRelativePath(legacy.ProtectedBackupsRoot, source);
            destination = Path.Combine(target.ProtectedBackupsRoot, relative);
            stagedDestination = Path.Combine(stagedData, "ProtectedBackups", relative);
        }
        else if (IsWithin(source, legacy.UpdateStateRoot))
        {
            var relative = SafeRelativePath(legacy.UpdateStateRoot, source);
            destination = Path.Combine(target.UpdateStateRoot, relative);
            stagedDestination = Path.Combine(stagedData, "UpdateState", relative);
        }
        else
        {
            // Package paths selected from Downloads or another external
            // location must not survive migration as absolute legacy/user
            // references. They are optional once the update is not active.
            if (required)
            {
                throw new InvalidDataException(
                    $"update-state.json referencia una ruta fuera del layout administrado: '{source}'.");
            }

            return null;
        }

        await CopyFileToStageAsync(source, stagedDestination, cancellationToken);
        return Path.GetFullPath(destination);
    }

    private async Task StageValidBackupsAsync(
        ArteInventariadoPathProvider legacy,
        string stagedData,
        IReadOnlyList<string> migrations,
        Guid installationId,
        InstallationMode stationMode,
        ICollection<string> ignoredBackups,
        CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(legacy.ProtectedBackupsRoot, "*.aibak", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(file);
            var validationDirectory = Path.Combine(stagedData, "BackupValidation", Guid.NewGuid().ToString("N"));
            var validation = await _backupEngine.ValidatePackageAsync(
                file,
                migrations,
                validationDirectory,
                cancellationToken);
            TryDeleteDirectory(validationDirectory);
            if (!validation.IsValid ||
                validation.Manifest?.SourceInstallationId != installationId ||
                validation.Manifest.SourceInstallationMode != stationMode)
            {
                ignoredBackups.Add(Path.GetFileName(file));
                continue;
            }

            var name = Path.GetFileName(file);
            var destination = Path.Combine(stagedData, "ProtectedBackups", name);
            await CopyFileToStageAsync(file, destination, cancellationToken);
        }
    }

    private static IReadOnlyList<string> ReadCurrentMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new AppDbContext(options);
        return context.Database.GetMigrations().ToArray();
    }

    private async Task ValidateDatabaseAsync(
        string databasePath,
        IReadOnlyList<string> migrations,
        CancellationToken cancellationToken,
        bool allowOlderSchema = false)
    {
        var errors = await _backupEngine.ValidateDatabaseAsync(
            databasePath,
            migrations,
            allowOlderSchema,
            cancellationToken);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", errors));
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite quick_check fallo: {result ?? "sin resultado"}.");
        }
    }

    private static InstallationStatus LoadReadyStation(string path)
    {
        RejectReparsePoint(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("La instalacion legacy no contiene station-settings.json.", path);
        }

        var file = new FileInfo(path);
        if (file.Length is <= 0 or > 64 * 1024)
        {
            throw new InvalidDataException("station-settings.json tiene un tamano invalido.");
        }

        StationConfigurationDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<StationConfigurationDocument>(
                File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("station-settings.json no contiene JSON valido.", ex);
        }

        if (document is null ||
            document.SchemaVersion is < 1 or > LocalStationConfigurationStore.CurrentSchemaVersion ||
            document.InstallationId == Guid.Empty ||
            !document.IsFirstRunCompleted ||
            !Enum.TryParse<InstallationMode>(document.InstallationMode, true, out var mode) ||
            !Enum.IsDefined(mode))
        {
            throw new InvalidDataException(
                "La instalacion legacy no tiene una identidad y modo de estacion validos.");
        }

        return new InstallationStatus(
            StationConfigurationState.Ready,
            mode,
            true,
            document.InstallationId,
            document.SchemaVersion,
            Path.GetFullPath(path),
            null);
    }

    private static UpdateVersionPointer LoadValidPointer(ArteInventariadoPathProvider legacy)
    {
        RejectReparsePoint(legacy.CurrentStatePath);
        var pointer = new UpdatePointerStore(legacy.CurrentStatePath).Load()
            ?? throw new InvalidDataException("La instalacion legacy no tiene current.json.");
        var resolver = new UpdateLaunchTargetResolver(UpdateRuntimePaths.Create(legacy));
        _ = resolver.ResolveCurrent(pointer);
        _ = resolver.ResolvePrevious(pointer);
        return pointer;
    }

    private static UpdateStateDocument? LoadMigratableUpdateState(string path)
    {
        RejectReparsePoint(path);
        if (!File.Exists(path))
        {
            return null;
        }

        var state = new UpdateStateStore(path).Load();
        if (UpdateRecoveryPolicy.RequiresRecovery(state))
        {
            throw new InvalidOperationException(
                "La instalacion legacy tiene una actualizacion incompleta. Ejecute recovery antes de migrar.");
        }

        return state;
    }

    private static string? GetPreviousVersion(UpdateVersionPointer pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer.PreviousExecutablePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(pointer.PreviousExecutablePath)
            ?? throw new InvalidDataException("PREVIOUS no tiene directorio de version.");
        return UpdateVersion.Sanitize(Path.GetFileName(directory));
    }

    private static void ValidateInstalledPointer(
        ArteInventariadoPathProvider target,
        string currentVersion,
        string? previousExecutablePath)
    {
        var paths = UpdateRuntimePaths.Create(target);
        var pointer = new UpdatePointerStore(paths.CurrentPointerPath).Load()
            ?? throw new InvalidDataException("No se pudo promover current.json.");
        var resolver = new UpdateLaunchTargetResolver(paths);
        var current = resolver.ResolveCurrent(pointer);
        if (!string.Equals(pointer.Version, currentVersion, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(pointer.PreviousExecutablePath, previousExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("current.json promovido no coincide con la migracion.");
        }

        _ = current;
        _ = resolver.ResolvePrevious(pointer);
    }

    private static void ValidateStationCopy(string path, Guid installationId, InstallationMode mode)
    {
        RejectReparsePoint(path);
        var status = new LocalStationConfigurationStore(path).Load().Status;
        if (!status.IsConfigured || status.InstallationId != installationId || status.Mode != mode)
        {
            throw new InvalidDataException("La copia de station-settings.json no preserva identidad o modo.");
        }
    }

    private static void ValidateStationCopy(
        ArteInventariadoPathProvider target,
        Guid installationId,
        InstallationMode mode)
    {
        RejectReparsePoint(target.StationSettingsPath);
        RejectReparsePoint(target.StationOperationalSettingsPath);
        var result = new LocalStationConfigurationStore(target).Load();
        var status = result.Status;
        if (!status.IsConfigured || status.InstallationId != installationId || status.Mode != mode)
        {
            throw new InvalidDataException("La copia machine-wide de station no preserva identidad o modo.");
        }
    }

    private static void EnsureTargetIdentityCompatible(
        ArteInventariadoPathProvider target,
        InstallationStatus source)
    {
        RejectReparsePoint(target.StationSettingsPath);
        if (!File.Exists(target.StationSettingsPath))
        {
            return;
        }

        var existing = new LocalStationConfigurationStore(target).Load().Status;
        if (!existing.IsConfigured)
        {
            if (existing.State == StationConfigurationState.RequiresSetup &&
                !File.Exists(target.DatabasePath) &&
                !File.Exists(target.CurrentStatePath))
            {
                return;
            }

            throw new InvalidDataException("El destino contiene una configuracion de estacion no valida.");
        }

        if (existing.InstallationId != source.InstallationId || existing.Mode != source.Mode)
        {
            throw new InvalidDataException("El destino pertenece a otra instalacion o modo de estacion.");
        }
    }

    private static void EnsureNoLegacyReferences(UpdateStateDocument state, string legacyRoot)
    {
        var root = Path.GetFullPath(legacyRoot);
        var values = new[]
        {
            state.PackagePath,
            state.ManifestPath,
            state.PreUpdateBackupPath,
            state.PreviousVersionPath,
            state.CurrentVersionPath,
            state.SourceApplicationPath,
            state.StationSettingsBackupPath
        };
        if (values.Any(value => !string.IsNullOrWhiteSpace(value) && IsWithin(Path.GetFullPath(value), root)))
        {
            throw new InvalidDataException("update-state.json conserva una ruta absoluta del layout legacy.");
        }
    }

    private static void EnsureLegacySource(ArteInventariadoPathProvider legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        if (legacy.IsMachineWide)
        {
            throw new ArgumentException("La fuente de migracion debe usar el layout legacy.", nameof(legacy));
        }

        RejectReparsePoint(legacy.MachineDataRoot);
        if (!Directory.Exists(legacy.MachineDataRoot))
        {
            throw new DirectoryNotFoundException("No existe la instalacion legacy indicada.");
        }
    }

    private static void EnsureMachineWideTarget(ArteInventariadoPathProvider target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsMachineWide)
        {
            throw new ArgumentException("El destino debe usar el layout machine-wide.", nameof(target));
        }
    }

    private static void EnsureRootsDoNotOverlap(
        ArteInventariadoPathProvider legacy,
        ArteInventariadoPathProvider target)
    {
        var roots = new[]
        {
            legacy.MachineDataRoot,
            target.MachineInstallRoot,
            target.MachineDataRoot
        };
        for (var i = 0; i < roots.Length; i++)
        {
            for (var j = i + 1; j < roots.Length; j++)
            {
                if (IsWithin(roots[i], roots[j]) || IsWithin(roots[j], roots[i]))
                {
                    throw new InvalidDataException("Las raices de origen y destino se superponen.");
                }
            }
        }
    }

    private static void EnsureTargetDirectories(ArteInventariadoPathProvider target)
    {
        foreach (var directory in new[]
        {
            target.MachineInstallRoot,
            target.VersionsRoot,
            target.MachineDataRoot,
            target.DataRoot,
            target.AssetsRoot,
            target.BusinessAssetsRoot,
            target.ProductBusinessAssetsRoot,
            target.StationRoot,
            target.StationOperationalRoot,
            target.UpdateStateRoot,
            target.StationSnapshotsRoot,
            target.RecoveryRoot,
            target.UpdateLogsRoot,
            target.LogsRoot,
            target.MaintenanceRoot,
            target.ProtectedBackupsRoot,
            target.UserDataRoot,
            target.CacheRoot,
            target.ThumbnailRoot
        })
        {
            EnsureDirectory(directory);
        }
    }

    private static string CreateStagingRoot(ArteInventariadoPathProvider target)
    {
        var root = Path.Combine(target.MaintenanceRoot, StagingDirectoryName);
        EnsureDirectory(root);
        var staging = Path.Combine(root, Guid.NewGuid().ToString("N"));
        EnsureDirectory(staging);
        return staging;
    }

    private static async Task CopyStableComponentFromLegacyAsync(
        ArteInventariadoPathProvider legacy,
        UpdateVersionPointer pointer,
        string name,
        string destination,
        CancellationToken cancellationToken)
    {
        var source = Path.Combine(legacy.MachineInstallRoot, name);
        if (!File.Exists(source))
        {
            var versionDirectory = Path.GetDirectoryName(pointer.ExecutablePath)!;
            source = Path.Combine(versionDirectory, name);
        }

        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"La instalacion legacy no contiene {name}.", source);
        }

        await CopyFileToStageAsync(source, destination, cancellationToken);
    }

    private static async Task CopyStableComponentAsync(
        string payload,
        string name,
        string destination,
        CancellationToken cancellationToken)
    {
        var source = Path.Combine(payload, name);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"El payload no contiene {name}.", source);
        }

        await CopyFileToStageAsync(source, destination, cancellationToken);
    }

    private static async Task CopyOptionalStableComponentAsync(
        string payload,
        string name,
        string destination,
        CancellationToken cancellationToken)
    {
        var source = Path.Combine(payload, name);
        if (File.Exists(source))
        {
            await CopyFileToStageAsync(source, destination, cancellationToken);
        }
    }

    private static async Task<bool> CopyOptionalStableComponentFromPayloadAsync(
        string? payload,
        string name,
        string destination,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var payloadRoot = Path.GetFullPath(payload);
        if (!Path.IsPathFullyQualified(payloadRoot) ||
            payloadRoot.StartsWith("\\\\", StringComparison.Ordinal) ||
            payloadRoot.StartsWith("//", StringComparison.Ordinal) ||
            payloadRoot.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("El payload del helper debe ser una ruta local absoluta.");
        }

        RejectReparsePoint(payloadRoot);
        var source = Path.Combine(payloadRoot, name);
        if (!File.Exists(source))
        {
            return false;
        }

        RejectReparsePoint(source);
        await CopyFileToStageAsync(source, destination, cancellationToken);
        return true;
    }

    private static void EnsureMachineWideStationInitialized(ArteInventariadoPathProvider target)
    {
        var store = new LocalStationConfigurationStore(target);
        if (!File.Exists(target.StationSettingsPath))
        {
            store.EnsureInitialized();
        }
    }

    private static async Task WriteOperationalStationCopyAsync(
        string source,
        string destination,
        ArteInventariadoPathProvider legacy,
        ArteInventariadoPathProvider target,
        CancellationToken cancellationToken)
    {
        var document = await ReadStationDocumentAsync(source, cancellationToken);

        var backup = document.Backup is null
            ? null
            : document.Backup with
            {
                DestinationDirectory = MapLegacyBackupDestination(
                    document.Backup.DestinationDirectory,
                    legacy.ProtectedBackupsRoot,
                    target.ProtectedBackupsRoot)
            };
        var operational = new StationOperationalConfigurationDocument(
            LocalStationConfigurationStore.CurrentSchemaVersion,
            backup,
            document.Reports,
            document.Pos,
            document.Peripherals);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("La ruta operativa de station no tiene directorio.");
        Directory.CreateDirectory(directory);
        await WriteJsonAtomicallyAsync(destination, operational, cancellationToken);
    }

    private static string MapLegacyBackupDestination(
        string destination,
        string legacyBackupRoot,
        string targetBackupRoot)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return destination;
        }

        try
        {
            var full = Path.GetFullPath(destination.Trim());
            if (!IsWithin(full, legacyBackupRoot))
            {
                return destination;
            }

            var relative = Path.GetRelativePath(legacyBackupRoot, full);
            return string.Equals(relative, ".", StringComparison.Ordinal)
                ? Path.GetFullPath(targetBackupRoot)
                : Path.GetFullPath(Path.Combine(targetBackupRoot, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return destination;
        }
    }

    private static async Task WriteStationCoreCopyAsync(
        string source,
        string destination,
        Guid installationId,
        InstallationMode mode,
        CancellationToken cancellationToken)
    {
        var document = await ReadStationDocumentAsync(source, cancellationToken);
        if (document.InstallationId != installationId ||
            !string.Equals(document.InstallationMode, mode.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !document.IsFirstRunCompleted)
        {
            throw new InvalidDataException("station-settings.json no coincide con la identidad o modo verificados.");
        }

        var core = new StationCoreConfigurationDocument(
            LocalStationConfigurationStore.CurrentSchemaVersion,
            installationId,
            mode.ToString(),
            true);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("La ruta protegida de station no tiene directorio.");
        Directory.CreateDirectory(directory);
        await WriteJsonAtomicallyAsync(destination, core, cancellationToken);
    }

    private static async Task<StationConfigurationDocument> ReadStationDocumentAsync(
        string source,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(source);
        var sourceInfo = new FileInfo(source);
        if (sourceInfo.Length is <= 0 or > 64 * 1024)
        {
            throw new InvalidDataException("station-settings.json tiene un tamano invalido.");
        }

        var document = JsonSerializer.Deserialize<StationConfigurationDocument>(
            await File.ReadAllTextAsync(source, cancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return document
            ?? throw new InvalidDataException("station-settings.json no contiene una configuracion valida.");
    }

    private static async Task WriteJsonAtomicallyAsync(
        string destination,
        object document,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(
                    document,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        WriteIndented = true
                    }),
                cancellationToken);
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task CopyDirectorySafelyAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(sourceDirectory);
        EnsureDirectory(destinationDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(entry);
            var destination = Path.Combine(destinationDirectory, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                await CopyDirectorySafelyAsync(entry, destination, cancellationToken);
            }
            else
            {
                await CopyFileToStageAsync(entry, destination, cancellationToken);
            }
        }
    }

    private static async Task CopyFileToStageAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(source);
        EnsureDirectory(Path.GetDirectoryName(destination)!);
        RejectReparsePoint(destination);
        if (File.Exists(destination))
        {
            if (await FilesEqualAsync(source, destination, cancellationToken))
            {
                return;
            }

            throw new IOException($"Existe un archivo distinto en la ruta de destino '{destination}'.");
        }

        await BackupPackageEngine.CopyFileAsync(source, destination, cancellationToken);
        if (!await FilesEqualAsync(source, destination, cancellationToken))
        {
            try { File.Delete(destination); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw new IOException($"La copia verificada no coincide con el origen: '{source}'.");
        }
    }

    private static async Task PromoteDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        ICollection<string> createdFiles,
        CancellationToken cancellationToken,
        bool skipCurrentPointer = false,
        string? replaceableRoot = null,
        IDictionary<string, byte[]>? replacedFiles = null,
        bool atomicVersionDirectories = false)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        RejectReparsePoint(sourceDirectory);
        EnsureDirectory(destinationDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(entry);
            var destination = Path.Combine(destinationDirectory, Path.GetFileName(entry));
            if (skipCurrentPointer &&
                string.Equals(
                    Path.GetFileName(destination),
                    ArteInventariadoPathProvider.CurrentStateFileName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(destination) ?? string.Empty),
                    "UpdateState",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                // A version directory is the unit observed by the launcher.
                // Populate it only in an isolated sibling and rename that
                // sibling into Versions atomically. Recursive file copies
                // directly below Versions would expose an incomplete payload
                // to a launcher that reads current.json concurrently.
                if (atomicVersionDirectories &&
                    string.Equals(
                        Path.GetFileName(sourceDirectory),
                        "Versions",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        Path.GetFileName(destinationDirectory),
                        "Versions",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await PromoteVersionDirectoryAsync(
                        entry,
                        destination,
                        createdFiles,
                        cancellationToken);
                    continue;
                }

                await PromoteDirectoryAsync(
                    entry,
                    destination,
                    createdFiles,
                    cancellationToken,
                    skipCurrentPointer,
                    replaceableRoot,
                    replacedFiles,
                    atomicVersionDirectories);
                continue;
            }

            if (File.Exists(destination))
            {
                RejectReparsePoint(destination);
                if (!await FilesEqualAsync(entry, destination, cancellationToken))
                {
                    if (replaceableRoot is not null &&
                        replacedFiles is not null &&
                        IsReplaceableStableFile(destination, replaceableRoot))
                    {
                        if (!replacedFiles.ContainsKey(Path.GetFullPath(destination)))
                        {
                            replacedFiles[Path.GetFullPath(destination)] = File.ReadAllBytes(destination);
                        }

                        await ReplaceFileAtomicallyAsync(entry, destination, cancellationToken);
                        continue;
                    }

                    throw new IOException($"El destino ya contiene un archivo distinto: '{destination}'.");
                }

                continue;
            }

            await BackupPackageEngine.CopyFileAsync(entry, destination, cancellationToken);
            if (!await FilesEqualAsync(entry, destination, cancellationToken))
            {
                try { File.Delete(destination); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                throw new IOException($"La promocion verificada no coincide con el staging: '{destination}'.");
            }
            createdFiles.Add(Path.GetFullPath(destination));
        }
    }

    private static async Task MigrateStagedDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
                ForeignKeys = true,
                DefaultTimeout = 30
            }.ToString())
            .Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private static async Task PromoteVersionDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        ICollection<string> createdFiles,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(sourceDirectory);
        var parent = Path.GetDirectoryName(destinationDirectory)
            ?? throw new InvalidOperationException("La version no tiene directorio padre.");
        EnsureDirectory(parent);
        RejectReparsePoint(parent);

        if (Directory.Exists(destinationDirectory))
        {
            RejectReparsePoint(destinationDirectory);
            if (await DirectoriesEqualAsync(sourceDirectory, destinationDirectory, cancellationToken))
            {
                // Safe retry: a previous run completed the atomic rename but
                // failed before publishing current.json.
                return;
            }

            throw new IOException(
                "La version destino ya existe y no coincide con el staging verificado.");
        }

        // Create the temporary directory beside the final version so the
        // final Directory.Move is a same-volume atomic rename even when the
        // general bootstrap staging root lives under ProgramData.
        var promotedStaging = Path.Combine(
            parent,
            $".staging-promote-{Guid.NewGuid():N}");
        try
        {
            await CopyDirectorySafelyAsync(
                sourceDirectory,
                promotedStaging,
                cancellationToken);
            RejectReparsePoint(promotedStaging);
            Directory.Move(promotedStaging, destinationDirectory);
            createdFiles.Add(Path.GetFullPath(destinationDirectory));
        }
        finally
        {
            TryDeleteDirectory(promotedStaging);
        }
    }

    private static async Task<bool> DirectoriesEqualAsync(
        string leftDirectory,
        string rightDirectory,
        CancellationToken cancellationToken)
    {
        var leftFiles = EnumerateFilesWithoutReparse(leftDirectory)
            .Select(path => (FullPath: path, RelativePath: Path.GetRelativePath(leftDirectory, path)))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rightFiles = EnumerateFilesWithoutReparse(rightDirectory)
            .Select(path => (FullPath: path, RelativePath: Path.GetRelativePath(rightDirectory, path)))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!leftFiles.Select(item => item.RelativePath)
                .SequenceEqual(
                    rightFiles.Select(item => item.RelativePath),
                    StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 0; index < leftFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await FilesEqualAsync(
                    leftFiles[index].FullPath,
                    rightFiles[index].FullPath,
                    cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparse(string directory)
    {
        RejectReparsePoint(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectReparsePoint(entry);
            if (Directory.Exists(entry))
            {
                foreach (var nested in EnumerateFilesWithoutReparse(entry))
                {
                    yield return nested;
                }
            }
            else if (File.Exists(entry))
            {
                yield return entry;
            }
        }
    }

    private static bool IsReplaceableStableFile(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(fullPath)?.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return new[]
        {
            StableDeploymentBootstrap.LauncherExecutableName,
            StableDeploymentBootstrap.UpdaterExecutableName,
            ArteInventariadoPathProvider.StationHelperFileName,
            ArteInventariadoPathProvider.BackupRunnerFileName,
            ArteInventariadoPathProvider.CloudBackupRunnerFileName
        }
        .Concat(StableEntryPointInstaller.RunnerRuntimeSupportFileNames)
        .Contains(Path.GetFileName(fullPath), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task ReplaceFileAtomicallyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await BackupPackageEngine.CopyFileAsync(source, temporary, cancellationToken);
            if (!await FilesEqualAsync(source, temporary, cancellationToken))
            {
                throw new IOException("El reemplazo staged de un componente estable no coincide con el origen.");
            }

            File.Replace(temporary, destination, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static Task PromoteCurrentPointerAsync(
        string stagedPointerPath,
        ArteInventariadoPathProvider target,
        ICollection<string> createdFiles,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(stagedPointerPath);
        if (!File.Exists(stagedPointerPath))
        {
            throw new InvalidDataException("El staging no contiene current.json.");
        }

        var candidate = new UpdatePointerStore(stagedPointerPath).Load()
            ?? throw new InvalidDataException("El current.json staged no es válido.");
        ValidatePointerCandidate(target, candidate);

        var destination = target.CurrentStatePath;
        var existing = new UpdatePointerStore(destination).Load();
        if (existing is not null && PointersEquivalent(existing, candidate))
        {
            // UpdatedUtc is bookkeeping, not installation identity. Keep the
            // existing pointer so a retry does not manufacture a conflict.
            return Task.CompletedTask;
        }

        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("current.json no tiene directorio de destino.");
        EnsureDirectory(directory);
        RejectReparsePoint(destination);
        cancellationToken.ThrowIfCancellationRequested();
        var destinationExisted = File.Exists(destination);
        // UpdatePointerStore performs the same-volume temp write, flushes the
        // complete JSON document, and atomically replaces current.json.  Do
        // not copy a staged pointer directly with a non-durable write here.
        new UpdatePointerStore(destination).Save(
            candidate.Version,
            candidate.ExecutablePath,
            candidate.PreviousExecutablePath,
            candidate.UpdatedUtc);
        if (!destinationExisted)
        {
            createdFiles.Add(Path.GetFullPath(destination));
        }

        return Task.CompletedTask;
    }

    private static void ValidatePointerCandidate(
        ArteInventariadoPathProvider target,
        UpdateVersionPointer pointer)
    {
        var expectedCurrent = Path.Combine(
            target.GetVersionDirectory(pointer.Version),
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!string.Equals(
                Path.GetFullPath(pointer.ExecutablePath),
                Path.GetFullPath(expectedCurrent),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El current.json staged apunta fuera de la versión promovida.");
        }

        if (!string.IsNullOrWhiteSpace(pointer.PreviousExecutablePath))
        {
            var previous = Path.GetFullPath(pointer.PreviousExecutablePath);
            if (!IsWithin(previous, target.VersionsRoot) ||
                !string.Equals(
                    Path.GetFileName(previous),
                    UpdateLaunchTargetResolver.ApplicationExecutableName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("El previous.json staged apunta fuera de Versions.");
            }
        }
    }

    private static bool PointersEquivalent(
        UpdateVersionPointer left,
        UpdateVersionPointer right) =>
        string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.PreviousExecutablePath, right.PreviousExecutablePath, StringComparison.OrdinalIgnoreCase);

    private static byte[]? TryReadFileBytes(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void RestoreFileBytes(string path, byte[]? content)
    {
        try
        {
            if (content is null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("El puntero no tiene directorio de restauracion.");
            EnsureDirectory(directory);
            var temporary = Path.Combine(
                directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.restore.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserve the original installation error. The updater's next
            // recovery pass will validate the pointer before using it.
        }
    }

    private static void RestoreReplacedFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        foreach (var pair in files)
        {
            try
            {
                File.WriteAllBytes(pair.Key, pair.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the original installation error. A later recovery
                // pass validates stable binaries before launching them.
            }
        }
    }

    private static async Task<bool> FilesEqualAsync(
        string left,
        string right,
        CancellationToken cancellationToken)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        var leftHash = await BackupPackageEngine.ComputeSha256Async(left, cancellationToken);
        var rightHash = await BackupPackageEngine.ComputeSha256Async(right, cancellationToken);
        return string.Equals(leftHash, rightHash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteMarkerAsync(
        ArteInventariadoPathProvider target,
        MachineWideBootstrapMarker marker,
        CancellationToken cancellationToken)
    {
        var path = target.BootstrapStatePath;
        RejectReparsePoint(path);
        EnsureDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static MachineWideBootstrapMarker? ReadMarker(ArteInventariadoPathProvider target)
    {
        var path = target.BootstrapStatePath;
        RejectReparsePoint(path);
        if (!File.Exists(path))
        {
            return null;
        }

        var file = new FileInfo(path);
        if (file.Length is <= 0 or > 64 * 1024)
        {
            throw new InvalidDataException("El estado del bootstrap machine-wide tiene un tamano invalido.");
        }

        var marker = JsonSerializer.Deserialize<MachineWideBootstrapMarker>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (marker is null || marker.SchemaVersion != BootstrapSchemaVersion)
        {
            throw new InvalidDataException("El estado del bootstrap machine-wide no es compatible.");
        }

        return marker;
    }

    private static (string PayloadRoot, string Version) ValidatePublishedPayload(string sourceDirectory)
    {
        var payload = Path.GetFullPath(sourceDirectory);
        if (!Path.IsPathFullyQualified(payload) ||
            payload.StartsWith("\\\\", StringComparison.Ordinal) ||
            payload.StartsWith("//", StringComparison.Ordinal) ||
            payload.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("El payload de instalacion debe ser una ruta local absoluta.");
        }

        RejectReparsePoint(payload);
        var executable = Path.Combine(payload, UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "El payload de instalacion no contiene ArteInventariado.WinForms.exe.",
                executable);
        }

        var productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion;
        var version = UpdateVersion.Sanitize(productVersion
            ?? throw new InvalidDataException("El ejecutable publicado no declara ProductVersion."));
        return (payload, version);
    }

    private static string? TryNormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return UpdateVersion.Sanitize(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task<bool> IsInstalledStateReady(
        ArteInventariadoPathProvider target,
        MachineWideBootstrapMarker marker,
        string requestedVersion,
        string? payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedVersion = UpdateVersion.Sanitize(requestedVersion);
            if (!string.Equals(
                    TryNormalizeVersion(marker.CurrentVersion),
                    normalizedVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var versionDirectory = target.GetVersionDirectory(normalizedVersion);
            if (!Directory.Exists(versionDirectory))
            {
                return false;
            }

            if (payload is not null)
            {
                RejectReparsePoint(payload);
                RejectReparsePoint(versionDirectory);
                if (!await DirectoriesEqualAsync(payload, versionDirectory, cancellationToken))
                {
                    // Let the normal promotion path report the immutable
                    // same-version content conflict instead of claiming that
                    // a different payload is already ready.
                    return false;
                }
            }

            var pointer = new UpdatePointerStore(target.CurrentStatePath).Load();
            if (pointer is null ||
                !string.Equals(pointer.Version, normalizedVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var resolver = new UpdateLaunchTargetResolver(UpdateRuntimePaths.Create(target));
            _ = resolver.ResolveCurrent(pointer);

            var station = new LocalStationConfigurationStore(target).Load().Status;
            if (station.State is StationConfigurationState.Corrupt or
                StationConfigurationState.Invalid or
                StationConfigurationState.Unsupported ||
                station.InstallationId is null ||
                station.InstallationId == Guid.Empty)
            {
                return false;
            }

            if (marker.InstallationId is not null && station.InstallationId != marker.InstallationId)
            {
                return false;
            }

            var requiredStableComponents = new[]
            {
                StableDeploymentBootstrap.LauncherExecutableName,
                StableDeploymentBootstrap.UpdaterExecutableName
            }
            .Concat(
                new[]
                {
                    ArteInventariadoPathProvider.StationHelperFileName,
                    ArteInventariadoPathProvider.BackupRunnerFileName,
                    ArteInventariadoPathProvider.CloudBackupRunnerFileName
                }
                .Where(name => payload is not null
                    ? File.Exists(Path.Combine(payload, name))
                    : File.Exists(Path.Combine(target.MachineInstallRoot, name))))
            .ToArray();

            foreach (var name in requiredStableComponents)
            {
                var stablePath = Path.Combine(target.MachineInstallRoot, name);
                if (!HasProductVersion(stablePath, normalizedVersion))
                {
                    return false;
                }
            }

            // A task is part of the machine-wide readiness contract whenever
            // its stable runner exists.  Query Task Scheduler itself rather
            // than trusting a previous log entry.
            if (File.Exists(target.ProtectedBackupPolicyPath) || File.Exists(target.BackupRunnerPath))
            {
                if (!File.Exists(target.BackupRunnerPath) ||
                    !MachineWideScheduledTaskVerifier.VerifyDetailed(
                        target,
                        MachineWideBackupPolicyStore.DefaultTaskName,
                        target.BackupRunnerPath,
                        _logWriter,
                        phase: "bootstrap-readiness-backup").Verified)
                {
                    return false;
                }
            }

            if (File.Exists(target.CloudBackupConfigurationPath) || File.Exists(target.CloudBackupRunnerPath))
            {
                if (!File.Exists(target.CloudBackupRunnerPath) ||
                    !MachineWideScheduledTaskVerifier.VerifyDetailed(
                        target,
                        ArteInventariado.Infrastructure.CloudBackup.MachineWideCloudBackupTaskScheduler.DefaultTaskName,
                        target.CloudBackupRunnerPath,
                        _logWriter,
                        phase: "bootstrap-readiness-cloud").Verified)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    private static bool HasProductVersion(string path, string expectedVersion)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var actual = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        return actual is not null &&
               UpdateVersion.TryParse(actual, out var parsed) &&
               parsed.CompareTo(UpdateVersion.Parse(expectedVersion)) == 0;
    }

    private static void EnsureMarkerMatchesTarget(
        MachineWideBootstrapMarker marker,
        ArteInventariadoPathProvider target)
    {
        if (marker.InstallationId is null)
        {
            return;
        }

        var status = new LocalStationConfigurationStore(target).Load().Status;
        if (status.InstallationId != marker.InstallationId)
        {
            throw new InvalidDataException("El destino machine-wide no coincide con la identidad marcada.");
        }

    }

    private static MachineWideBootstrapResult AlreadyReady(
        ArteInventariadoPathProvider target,
        MachineWideBootstrapMarker marker) =>
        new(
            MachineWideBootstrapOutcome.AlreadyReady,
            target.MachineInstallRoot,
            target.MachineDataRoot,
            target.UserDataRoot,
            !string.IsNullOrWhiteSpace(marker.SourceLegacyFingerprint),
            marker.InstallationId,
            marker.StationMode,
            []);

    private static string ComputeRootFingerprint(string root)
    {
        var canonical = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string SafeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("Una ruta administrada queda fuera de su raiz.");
        }

        return relative;
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

    private static void EnsureDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full))
        {
            throw new IOException($"La ruta esperada como directorio es un archivo: '{full}'.");
        }

        if (Directory.Exists(full))
        {
            RejectReparsePoint(full);
            return;
        }

        Directory.CreateDirectory(full);
        RejectReparsePoint(full);
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"La ruta contiene un reparse point no permitido: '{path}'.");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }

        // File.GetAttributes can fail for a dangling or inaccessible reparse
        // point; do not silently follow such a path.
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidDataException($"No se pudo inspeccionar la ruta administrada: '{path}'.", ex);
        }
    }

    private static void RemoveCreatedFiles(IEnumerable<string> files)
    {
        foreach (var file in files.Reverse())
        {
            try
            {
                if (Directory.Exists(file))
                {
                    Directory.Delete(file, recursive: true);
                }
                else if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
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

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Phase(string name) => _phaseObserver?.Invoke(name);
}

public enum MachineWideBootstrapOutcome
{
    CleanInstallPrepared = 1,
    Migrated = 2,
    AlreadyReady = 3
}

public sealed record MachineWideBootstrapResult(
    MachineWideBootstrapOutcome Outcome,
    string MachineInstallRoot,
    string MachineDataRoot,
    string UserDataRoot,
    bool LegacyDetected,
    Guid? InstallationId,
    InstallationMode? StationMode,
    IReadOnlyList<string> CopiedFiles,
    IReadOnlyList<string> IgnoredBackups)
{
    public MachineWideBootstrapResult(
        MachineWideBootstrapOutcome outcome,
        string machineInstallRoot,
        string machineDataRoot,
        string userDataRoot,
        bool legacyDetected,
        Guid? installationId,
        InstallationMode? stationMode,
        IReadOnlyList<string> copiedFiles)
        : this(
            outcome,
            machineInstallRoot,
            machineDataRoot,
            userDataRoot,
            legacyDetected,
            installationId,
            stationMode,
            copiedFiles,
            [])
    {
    }
}

internal sealed record MachineWideBootstrapMarker(
    int SchemaVersion,
    Guid? InstallationId,
    InstallationMode? StationMode,
    string? SourceLegacyFingerprint,
    string? CurrentVersion,
    DateTimeOffset CompletedUtc);
