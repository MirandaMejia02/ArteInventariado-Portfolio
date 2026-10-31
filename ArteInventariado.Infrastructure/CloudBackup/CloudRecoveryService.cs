using System.Globalization;
using System.Security.Cryptography;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// G2 read-only recovery orchestration.  Authorization and elevation are
/// checked before any protected staging is created.  The service never writes
/// the live database, never mutates station identity, and never exposes a
/// remote delete or restore operation.
/// </summary>
public sealed class CloudRecoveryService : ICloudRecoveryService
{
    private const long MaximumPackageBytes = 20L * 1024 * 1024 * 1024;
    private const int MaximumWarnings = 128;

    private readonly ArteInventariadoPathProvider _paths;
    private readonly CloudBackupConfigurationStore _configurationStore;
    private readonly BackupPackageEngine _packageEngine;
    private readonly ICloudRecoveryAuthorizationService _authorization;
    private readonly ICloudRecoveryAuthenticator _authenticator;
    private readonly Func<CloudBackupConfiguration, CloudRecoveryTemporaryCredentials, ICloudRecoveryObjectStore> _objectStoreFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _isElevated;
    private readonly Func<Guid?> _localInstallationIdProvider;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _migrationProvider;

    internal CloudRecoveryService(
        ArteInventariadoPathProvider paths,
        CloudBackupConfigurationStore configurationStore,
        BackupPackageEngine packageEngine,
        ICloudRecoveryAuthorizationService authorization,
        ICloudRecoveryAuthenticator authenticator,
        TimeProvider timeProvider)
        : this(
            paths,
            configurationStore,
            packageEngine,
            authorization,
            authenticator,
            (configuration, credentials) => new S3CloudRecoveryObjectStore(configuration, credentials),
            timeProvider,
            MachineWideElevation.IsAdministrator,
            () => GetConfiguredInstallationId(paths),
            GetCurrentMigrationsAsync)
    {
    }

    internal CloudRecoveryService(
        ArteInventariadoPathProvider paths,
        CloudBackupConfigurationStore configurationStore,
        BackupPackageEngine packageEngine,
        ICloudRecoveryAuthorizationService authorization,
        ICloudRecoveryAuthenticator authenticator,
        Func<CloudBackupConfiguration, CloudRecoveryTemporaryCredentials, ICloudRecoveryObjectStore> objectStoreFactory,
        TimeProvider timeProvider,
        Func<bool> isElevated,
        Func<Guid?> localInstallationIdProvider,
        Func<CancellationToken, Task<IReadOnlyList<string>>> migrationProvider)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _packageEngine = packageEngine ?? throw new ArgumentNullException(nameof(packageEngine));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _objectStoreFactory = objectStoreFactory ?? throw new ArgumentNullException(nameof(objectStoreFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        _localInstallationIdProvider = localInstallationIdProvider ?? throw new ArgumentNullException(nameof(localInstallationIdProvider));
        _migrationProvider = migrationProvider ?? throw new ArgumentNullException(nameof(migrationProvider));
    }

    public Task<CloudRecoveryCatalogResult> GetCatalogAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CancellationToken cancellationToken = default)
        => GetCatalogCoreAsync(authentication, scope, configurationOverride: null, cancellationToken);

    /// <summary>
    /// H2-only entry point.  The fresh-machine dialog keeps the owner-supplied
    /// non-secret configuration in memory; it must not be written to the
    /// protected configuration store before a candidate has been selected.
    /// Authorization/elevation and installation scoping remain identical to
    /// the normal G2 path.
    /// </summary>
    public Task<CloudRecoveryCatalogResult> GetCatalogForFreshMachineAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CloudBackupConfiguration configuration,
        CancellationToken cancellationToken = default)
        => GetCatalogCoreAsync(authentication, scope, configuration, cancellationToken);

    private async Task<CloudRecoveryCatalogResult> GetCatalogCoreAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CloudBackupConfiguration? configurationOverride,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAndScopedAsync(scope, cancellationToken);
        var configuration = LoadConfiguration(configurationOverride);
        var credentials = await AuthenticateAsync(authentication, cancellationToken);
        using var objectStore = _objectStoreFactory(configuration, credentials);

        var prefix = CloudBackupObjectKey.CreateInstallationPrefix(
            configuration.KeyPrefix,
            scope.InstallationId);
        var remoteObjects = await objectStore.ListVersionsAsync(
            configuration.BucketName,
            prefix,
            cancellationToken);
        var warnings = new List<string>();
        var items = new List<CloudRecoveryCatalogItem>();

        foreach (var remote in remoteObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (remote.IsDeleteMarker ||
                !CloudBackupObjectKey.TryParse(
                    remote.Key,
                    configuration.KeyPrefix,
                    out var installationId,
                    out var backupId) ||
                installationId != scope.InstallationId ||
                !remote.Key.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (remote.SizeBytes is <= 0 or > MaximumPackageBytes)
            {
                warnings.Add($"Se omitió un objeto cloud con tamaño inválido ({backupId:N}).");
                continue;
            }

            CloudRemoteObjectMetadata metadata;
            try
            {
                metadata = await objectStore.GetMetadataAsync(
                    configuration.BucketName,
                    remote.Key,
                    remote.VersionId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"No se pudo leer metadata del backup {backupId:N} ({ex.GetType().Name}).");
                continue;
            }

            if (!IsMetadataConsistent(
                    metadata,
                    configuration.BucketName,
                    remote.Key,
                    remote.VersionId,
                    remote.SizeBytes,
                    installationId,
                    backupId,
                    out var consistencyError))
            {
                warnings.Add($"Se omitió metadata inconsistente del backup {backupId:N}: {consistencyError}");
                continue;
            }

            var item = CreateCatalogItem(remote, metadata, installationId, backupId);
            items.Add(item);
        }

        var ordered = items
            .OrderByDescending(item => item.CreatedUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.VersionId, StringComparer.Ordinal)
            .ToArray();
        var healthy = ordered
            .Where(item => item.ContentStatus == BackupContentStatus.Healthy)
            .ToArray();
        var degraded = ordered
            .Where(item => item.ContentStatus == BackupContentStatus.DegradedAssets)
            .ToArray();

        return new CloudRecoveryCatalogResult(
            ordered,
            healthy.FirstOrDefault(),
            healthy.Length == 0 ? degraded.FirstOrDefault() ?? ordered.FirstOrDefault() : null,
            warnings.Take(MaximumWarnings).ToArray());
    }

    public Task<CloudRecoveryCandidate> DownloadAndValidateAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CloudRecoveryCatalogItem selection,
        CancellationToken cancellationToken = default)
        => DownloadAndValidateCoreAsync(authentication, scope, selection, configurationOverride: null, cancellationToken);

    /// <summary>H2 counterpart of <see cref="DownloadAndValidateAsync"/>.</summary>
    public Task<CloudRecoveryCandidate> DownloadAndValidateForFreshMachineAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CloudRecoveryCatalogItem selection,
        CloudBackupConfiguration configuration,
        CancellationToken cancellationToken = default)
        => DownloadAndValidateCoreAsync(authentication, scope, selection, configuration, cancellationToken);

    private async Task<CloudRecoveryCandidate> DownloadAndValidateCoreAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CloudRecoveryCatalogItem selection,
        CloudBackupConfiguration? configurationOverride,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAndScopedAsync(scope, cancellationToken);
        var configuration = LoadConfiguration(configurationOverride);
        ArgumentNullException.ThrowIfNull(selection);
        ValidateSelection(selection, scope.InstallationId, configuration.KeyPrefix);
        var credentials = await AuthenticateAsync(authentication, cancellationToken);
        using var objectStore = _objectStoreFactory(configuration, credentials);

        var metadata = await objectStore.GetMetadataAsync(
            configuration.BucketName,
            selection.Key,
            selection.VersionId,
            cancellationToken);
        if (!IsMetadataConsistent(
                metadata,
                configuration.BucketName,
                selection.Key,
                selection.VersionId,
                selection.SizeBytes,
                selection.InstallationId,
                selection.BackupId,
                out var consistencyError))
        {
            throw new InvalidDataException($"El objeto cloud cambió o no es consistente: {consistencyError}");
        }

        var expectedPackageSha = NormalizeSha256(
            selection.PackageSha256 ?? GetMetadataValue(metadata.Metadata, "arte-package-sha256"));
        var remoteChecksum = NormalizeSha256(metadata.ChecksumSha256);
        if (expectedPackageSha is null && remoteChecksum is null)
        {
            throw new InvalidDataException(
                "El objeto cloud no proporciona checksum verificable para recuperación.");
        }

        var staging = CreateStagingDirectory();
        var partialPath = Path.Combine(staging, "package.aibak.partial");
        var packagePath = Path.Combine(staging, "package.aibak");
        try
        {
            await using (var output = new FileStream(
                             partialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await objectStore.DownloadAsync(
                    configuration.BucketName,
                    selection.Key,
                    selection.VersionId,
                    output,
                    cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            var downloadedSize = new FileInfo(partialPath).Length;
            if (downloadedSize != metadata.SizeBytes || downloadedSize != selection.SizeBytes)
            {
                throw new InvalidDataException("El tamaño descargado no coincide con el objeto catalogado.");
            }

            File.Move(partialPath, packagePath);
            var packageSha = await BackupPackageEngine.ComputeSha256Async(packagePath, cancellationToken);
            if (expectedPackageSha is not null &&
                !FixedEquals(packageSha, expectedPackageSha))
            {
                throw new InvalidDataException("El checksum SHA-256 del paquete descargado no coincide.");
            }

            // A FULL_OBJECT checksum is directly comparable with the package
            // hash.  COMPOSITE checksums are still verified by the S3 SDK while
            // reading the response; the canonical package hash above is kept
            // in the candidate for H and future reconciliation.
            if (remoteChecksum is not null &&
                IsFullObjectChecksum(metadata.ChecksumType) &&
                !FixedEquals(packageSha, remoteChecksum))
            {
                throw new InvalidDataException("El checksum remoto del objeto no coincide con el paquete.");
            }

            var validationDirectory = Path.Combine(staging, "validated");
            var migrations = await _migrationProvider(cancellationToken);
            var validation = await _packageEngine.ValidatePackageAsync(
                packagePath,
                migrations,
                validationDirectory,
                cancellationToken);
            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidDataException(
                    "El .aibak cloud no superó la validación completa: " +
                    string.Join(" ", validation.Errors));
            }

            var manifest = validation.Manifest;
            if (manifest.BackupId != selection.BackupId ||
                manifest.SourceInstallationId != selection.InstallationId ||
                manifest.Kind != BackupKind.Automatic)
            {
                throw new InvalidDataException(
                    "El manifest no corresponde al BackupId, instalación o tipo esperado.");
            }

            var contentStatus = ParseContentStatus(GetMetadataValue(metadata.Metadata, "arte-content-status"));
            if (contentStatus.HasValue && contentStatus.Value != manifest.ContentStatus)
            {
                throw new InvalidDataException("El estado Healthy/DegradedAssets remoto no coincide con el manifest.");
            }

            var omitted = manifest.OmittedAssetCount ?? manifest.Assets.Count(asset => asset.WasMissing);
            return new CloudRecoveryCandidate(
                selection.InstallationId,
                selection.BackupId,
                packagePath,
                selection.Key,
                metadata.VersionId ?? selection.VersionId,
                manifest.CreatedUtc,
                manifest.ContentStatus,
                omitted,
                downloadedSize,
                packageSha,
                remoteChecksum,
                metadata.ChecksumType,
                validation.Warnings.ToArray());
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(staging);
            throw;
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public async Task CleanupCandidateAsync(
        CloudRecoveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        await _authorization.EnsureAuthorizedAsync(cancellationToken);
        EnsureMachineWideAndElevated();
        CleanupCandidateCore(candidate, cancellationToken);
    }

    /// <summary>
    /// Cleans the protected candidate after H2 has materialized the
    /// destination station.  The normal G2 authorization intentionally cannot
    /// be used here because a successful bootstrap is no longer
    /// <c>FreshMachine</c>.  The path guard is therefore repeated locally and
    /// no cloud/API call is made.
    /// </summary>
    internal Task CleanupCandidateAfterBootstrapAsync(
        CloudRecoveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        EnsureMachineWideAndElevated();
        CleanupCandidateCore(candidate, cancellationToken);
        return Task.CompletedTask;
    }

    private void CleanupCandidateCore(
        CloudRecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var stagingRoot = Path.GetFullPath(_paths.CloudRecoveryStagingRoot);
        var packagePath = Path.GetFullPath(candidate.PackagePath);
        if (!IsWithin(packagePath, stagingRoot))
        {
            throw new InvalidDataException("El candidato queda fuera del staging cloud protegido.");
        }

        EnsureNoReparsePoint(stagingRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var candidateDirectory = Directory.GetParent(packagePath)?.FullName;
        if (candidateDirectory is null || !IsWithin(candidateDirectory, stagingRoot))
        {
            throw new InvalidDataException("La carpeta del candidato no es válida.");
        }

        EnsureNoReparsePoint(candidateDirectory);
        TryDeleteDirectory(candidateDirectory);
    }

    private async Task EnsureAuthorizedAndScopedAsync(
        CloudRecoveryScope scope,
        CancellationToken cancellationToken)
    {
        await _authorization.EnsureAuthorizedAsync(cancellationToken);
        EnsureMachineWideAndElevated();
        ValidateScope(scope);
    }

    private void EnsureMachineWideAndElevated()
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException(
                "La recuperación cloud requiere el layout machine-wide protegido.");
        }

        if (!_isElevated())
        {
            throw new UnauthorizedAccessException(
                "La recuperación cloud requiere una operación elevada autorizada por UAC.");
        }
    }

    private void ValidateScope(CloudRecoveryScope scope)
    {
        if (scope.InstallationId == Guid.Empty)
        {
            throw new ArgumentException("La instalación cloud no puede estar vacía.", nameof(scope));
        }

        var localInstallationId = _localInstallationIdProvider();
        if ((!localInstallationId.HasValue || localInstallationId.Value != scope.InstallationId) &&
            !scope.OwnerConfirmed)
        {
            throw new UnauthorizedAccessException(
                "La selección de una instalación distinta requiere confirmación explícita del propietario.");
        }
    }

    private CloudBackupConfiguration LoadConfiguration(CloudBackupConfiguration? configurationOverride = null)
    {
        if (configurationOverride is not null)
        {
            CloudBackupConfigurationStore.Validate(configurationOverride);
            if (!configurationOverride.Enabled)
            {
                throw new InvalidOperationException(
                    "La recuperación cloud no está habilitada para esta operación.");
            }

            return configurationOverride;
        }

        var configuration = _configurationStore.LoadOptional();
        if (configuration is null || !configuration.Enabled)
        {
            throw new InvalidOperationException(
                "La recuperación cloud no está configurada en esta instalación.");
        }

        return configuration;
    }

    private async Task<CloudRecoveryTemporaryCredentials> AuthenticateAsync(
        CloudRecoveryAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        var temporary = await _authenticator.AuthenticateAsync(request, cancellationToken);
        if (temporary is null || temporary.ExpiresUtc <= _timeProvider.GetUtcNow())
        {
            throw new UnauthorizedAccessException("La sesión cloud de recuperación no es válida o ya expiró.");
        }

        return temporary;
    }

    private CloudRecoveryCatalogItem CreateCatalogItem(
        CloudRemoteObjectInfo remote,
        CloudRemoteObjectMetadata metadata,
        Guid installationId,
        Guid backupId)
    {
        var contentStatus = ParseContentStatus(GetMetadataValue(metadata.Metadata, "arte-content-status"));
        var omitted = ParseNonNegativeInt(GetMetadataValue(metadata.Metadata, "arte-omitted-assets"));
        var packageSha = NormalizeSha256(GetMetadataValue(metadata.Metadata, "arte-package-sha256"));
        var created = ParseDateTimeOffset(GetMetadataValue(metadata.Metadata, "arte-created-utc"))
            ?? metadata.LastModifiedUtc
            ?? remote.LastModifiedUtc;
        var metadataBackupId = ParseGuid(GetMetadataValue(metadata.Metadata, "arte-backup-id"));
        var metadataInstallationId = ParseGuid(GetMetadataValue(metadata.Metadata, "arte-source-installation-id"));
        var complete = metadataBackupId == backupId &&
                       metadataInstallationId == installationId &&
                       contentStatus.HasValue &&
                       packageSha is not null;

        return new CloudRecoveryCatalogItem(
            installationId,
            backupId,
            remote.Key,
            remote.VersionId ?? metadata.VersionId,
            remote.IsLatest,
            created,
            metadata.SizeBytes,
            contentStatus,
            omitted,
            packageSha,
            NormalizeSha256(metadata.ChecksumSha256),
            metadata.ChecksumType,
            complete);
    }

    private static bool IsMetadataConsistent(
        CloudRemoteObjectMetadata metadata,
        string expectedBucket,
        string expectedKey,
        string? expectedVersionId,
        long expectedSize,
        Guid installationId,
        Guid backupId,
        out string error)
    {
        if (!string.Equals(metadata.BucketName, expectedBucket, StringComparison.Ordinal) ||
            !string.Equals(metadata.Key, expectedKey, StringComparison.Ordinal) ||
            metadata.SizeBytes != expectedSize ||
            (expectedVersionId is not null &&
             !string.Equals(metadata.VersionId, expectedVersionId, StringComparison.Ordinal)))
        {
            error = "bucket, clave, versión o tamaño no coinciden";
            return false;
        }

        var metadataBackupId = ParseGuid(GetMetadataValue(metadata.Metadata, "arte-backup-id"));
        if (metadataBackupId.HasValue && metadataBackupId.Value != backupId)
        {
            error = "BackupId remoto distinto";
            return false;
        }

        var metadataInstallationId = ParseGuid(GetMetadataValue(metadata.Metadata, "arte-source-installation-id"));
        if (metadataInstallationId.HasValue && metadataInstallationId.Value != installationId)
        {
            error = "installationId remoto distinto";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void ValidateSelection(
        CloudRecoveryCatalogItem selection,
        Guid scopeInstallationId,
        string keyPrefix)
    {
        if (selection.InstallationId != scopeInstallationId ||
            selection.InstallationId == Guid.Empty ||
            selection.BackupId == Guid.Empty ||
            !CloudBackupObjectKey.TryParse(
                selection.Key,
                keyPrefix,
                out var keyInstallationId,
                out var keyBackupId) ||
            keyInstallationId != selection.InstallationId ||
            keyBackupId != selection.BackupId)
        {
            throw new InvalidDataException("La selección cloud no pertenece a la instalación autorizada.");
        }

        if (string.IsNullOrWhiteSpace(selection.Key) ||
            selection.Key.Contains("..", StringComparison.Ordinal) ||
            selection.Key.Contains('\\') ||
            selection.Key.Contains(':') ||
            !selection.Key.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase) ||
            selection.SizeBytes is <= 0 or > MaximumPackageBytes ||
            (selection.VersionId is { Length: 0 or > 1024 }))
        {
            throw new InvalidDataException("La selección cloud no es segura.");
        }
    }

    private string CreateStagingDirectory()
    {
        EnsureNoReparsePoint(_paths.MachineDataRoot);
        Directory.CreateDirectory(_paths.CloudRecoveryStagingRoot);
        EnsureNoReparsePoint(_paths.CloudRecoveryStagingRoot);
        var staging = Path.Combine(_paths.CloudRecoveryStagingRoot, Guid.NewGuid().ToString("N"));
        if (!IsWithin(staging, _paths.CloudRecoveryStagingRoot))
        {
            throw new InvalidDataException("El staging cloud queda fuera de su raíz autorizada.");
        }

        Directory.CreateDirectory(staging);
        EnsureNoReparsePoint(staging);
        return staging;
    }

    private static Guid? GetConfiguredInstallationId(ArteInventariadoPathProvider paths)
    {
        var status = new LocalStationConfigurationStore(paths).Load().Status;
        return status.IsConfigured && status.InstallationId.HasValue
            ? status.InstallationId
            : null;
    }

    private static async Task<IReadOnlyList<string>> GetCurrentMigrationsAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var context = new AppDbContext(options);
        cancellationToken.ThrowIfCancellationRequested();
        return context.Database.GetMigrations().ToList();
    }

    private static string? GetMetadataValue(
        IReadOnlyDictionary<string, string> metadata,
        string name)
    {
        if (metadata.TryGetValue(name, out var value))
        {
            return value;
        }

        var prefixed = "x-amz-meta-" + name;
        return metadata.TryGetValue(prefixed, out value) ? value : null;
    }

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var result) && result != Guid.Empty ? result : null;

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result)
            ? result
            : null;

    private static int? ParseNonNegativeInt(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0
            ? result
            : null;

    private static BackupContentStatus? ParseContentStatus(string? value) =>
        Enum.TryParse<BackupContentStatus>(value, ignoreCase: true, out var result) &&
        Enum.IsDefined(result)
            ? result
            : null;

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 64 &&
               trimmed.All(Uri.IsHexDigit)
            ? trimmed.ToUpperInvariant()
            : null;
    }

    private static bool IsFullObjectChecksum(string? checksumType) =>
        string.IsNullOrWhiteSpace(checksumType) ||
        string.Equals(checksumType, "FULL_OBJECT", StringComparison.OrdinalIgnoreCase);

    private static bool FixedEquals(string left, string right)
    {
        try
        {
            var leftBytes = Convert.FromHexString(left);
            var rightBytes = Convert.FromHexString(right);
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoint(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("El staging cloud contiene un reparse point.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
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
