using System.Security.Cryptography;
using System.Text;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Elevated, upload-only transport for already validated protected backups.
/// The local BackupRunner remains the source of packages; this process only
/// reads .aibak files from ProtectedBackups and never opens the live database
/// or asset trees.
/// </summary>
public sealed class CloudBackupRunner
{
    public const string LockName = "Global\\ArteInventariado.CloudBackupRunner";

    private readonly ArteInventariadoPathProvider _paths;
    private readonly BackupPackageEngine _packageEngine;
    private readonly CloudBackupConfigurationStore _configurationStore;
    private readonly CloudBackupUploadStateStore _stateStore;
    private readonly ICloudBackupSecretStore _secretStore;
    private readonly Func<CloudBackupConfiguration, CloudBackupCredential, ICloudBackupObjectStore> _objectStoreFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _isAdministratorOrSystem;
    private readonly Func<string, ICloudBackupLock> _lockFactory;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _migrationProvider;
    private readonly Func<Guid?> _installationIdProvider;
    private readonly bool _allowSyntheticRoots;

    public CloudBackupRunner(ArteInventariadoPathProvider paths)
        : this(
            paths,
            new BackupPackageEngine(),
            new CloudBackupConfigurationStore(paths),
            new CloudBackupUploadStateStore(paths),
            new DpapiCloudBackupSecretStore(paths),
            (configuration, credential) => new S3CloudBackupObjectStore(configuration, credential),
            TimeProvider.System,
            MachineWideElevation.IsAdministratorOrSystem,
            name => new NamedCloudBackupLock(name),
            GetCurrentMigrationsAsync,
            () => GetConfiguredInstallationId(paths),
            allowSyntheticRoots: false)
    {
    }

    internal CloudBackupRunner(
        ArteInventariadoPathProvider paths,
        BackupPackageEngine packageEngine,
        CloudBackupConfigurationStore configurationStore,
        CloudBackupUploadStateStore stateStore,
        ICloudBackupSecretStore secretStore,
        Func<CloudBackupConfiguration, CloudBackupCredential, ICloudBackupObjectStore> objectStoreFactory,
        TimeProvider timeProvider,
        Func<bool> isAdministratorOrSystem,
        Func<string, ICloudBackupLock> lockFactory,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? migrationProvider = null,
        Func<Guid?>? installationIdProvider = null,
        bool allowSyntheticRoots = false)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _packageEngine = packageEngine ?? throw new ArgumentNullException(nameof(packageEngine));
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _objectStoreFactory = objectStoreFactory ?? throw new ArgumentNullException(nameof(objectStoreFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _isAdministratorOrSystem = isAdministratorOrSystem ?? throw new ArgumentNullException(nameof(isAdministratorOrSystem));
        _lockFactory = lockFactory ?? throw new ArgumentNullException(nameof(lockFactory));
        _migrationProvider = migrationProvider ?? GetCurrentMigrationsAsync;
        _installationIdProvider = installationIdProvider ?? (() => new LocalStationConfigurationStore(paths).Load().Status.InstallationId);
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public async Task<CloudBackupRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        EnsureMachineWide();
        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);
        ValidateCloudRoots();
        if (!_isAdministratorOrSystem())
        {
            throw new UnauthorizedAccessException(
                "El CloudBackupRunner machine-wide debe ejecutarse como SYSTEM o administrador.");
        }

        var configuration = _configurationStore.LoadOptional();
        if (configuration is null || !configuration.Enabled)
        {
            return Result(
                CloudBackupRunOutcome.SkippedDisabled,
                [],
                "La subida cloud protegida está deshabilitada o aún no fue provisionada.",
                []);
        }

        var credential = _secretStore.LoadOptional();
        if (credential is null)
        {
            return Result(
                CloudBackupRunOutcome.Failed,
                [],
                "La configuración cloud está habilitada pero no existe una credencial provisionada.",
                ["Provisione la credencial mediante una operación administrativa."]);
        }

        using var operationLock = _lockFactory(LockName);
        if (!operationLock.TryAcquire())
        {
            return Result(
                CloudBackupRunOutcome.SkippedConcurrent,
                [],
                "Ya existe otra subida cloud protegida en ejecución.",
                []);
        }

        var stateDocument = _stateStore.Load();
        var states = stateDocument.Entries.ToDictionary(entry => entry.BackupId);
        var warnings = new List<string>();
        var installationId = _installationIdProvider();
        if (!installationId.HasValue || installationId.Value == Guid.Empty)
        {
            return Result(
                CloudBackupRunOutcome.Failed,
                states.Values.ToArray(),
                "La estación machine-wide no tiene una installationId válida.",
                ["No se procesaron paquetes cloud sin identidad local."]);
        }

        IReadOnlyList<string> migrations;
        try
        {
            migrations = await _migrationProvider(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Result(
                CloudBackupRunOutcome.Failed,
                states.Values.ToArray(),
                "No se pudo obtener la versión de migraciones instalada.",
                [$"Migraciones no disponibles ({ex.GetType().Name})."]);
        }

        var candidates = EnumerateProtectedPackages(warnings);
        if (candidates.Count == 0)
        {
            return Result(
                CloudBackupRunOutcome.NoCandidates,
                states.Values.ToArray(),
                "No hay respaldos protegidos pendientes para subir.",
                warnings);
        }

        var processed = 0;
        var failures = 0;
        foreach (var packagePath in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processedResult = await ProcessCandidateAsync(
                packagePath,
                installationId.Value,
                migrations,
                configuration,
                credential,
                states,
                warnings,
                cancellationToken);
            processed++;
            if (!processedResult)
            {
                failures++;
            }
        }

        PersistState(states.Values, warnings);
        var outcome = processed > 0 && failures == 0
            ? CloudBackupRunOutcome.Succeeded
            : failures < processed
                ? CloudBackupRunOutcome.Succeeded
                : CloudBackupRunOutcome.Failed;
        return Result(
            outcome,
            states.Values.ToArray(),
            outcome == CloudBackupRunOutcome.Succeeded
                ? "La subida cloud protegida terminó; se conservaron los paquetes locales."
                : "La subida cloud protegida no pudo completar ningún candidato.",
            warnings);
    }

    private async Task<bool> ProcessCandidateAsync(
        string packagePath,
        Guid installationId,
        IReadOnlyList<string> migrations,
        CloudBackupConfiguration configuration,
        CloudBackupCredential credential,
        IDictionary<Guid, CloudBackupUploadState> states,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var validationDirectory = Path.Combine(
            _paths.CloudBackupValidationRoot,
            Guid.NewGuid().ToString("N"));
        BackupValidationResult validation;
        try
        {
            EnsurePackagePath(packagePath);
            validation = await _packageEngine.ValidatePackageAsync(
                packagePath,
                migrations,
                validationDirectory,
                cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Se omitió un paquete cloud inválido ({Path.GetFileName(packagePath)}; {ex.GetType().Name}).");
            TryDeleteDirectory(validationDirectory);
            return false;
        }

        try
        {
            if (!validation.IsValid || validation.Manifest is null ||
                validation.Manifest.Kind != BackupKind.Automatic ||
                validation.Manifest.SourceInstallationId != installationId)
            {
                warnings.Add($"Se omitió un paquete cloud que no es un automático válido de esta estación ({Path.GetFileName(packagePath)}).");
                MarkQuarantinedIfIdentifiable(packagePath, validation.Manifest, states, warnings);
                return false;
            }

            var manifest = validation.Manifest;
            var fileInfo = new FileInfo(packagePath);
            var packageSha = await BackupPackageEngine.ComputeSha256Async(packagePath, cancellationToken);
            var key = CloudBackupObjectKey.Create(configuration.KeyPrefix, installationId, manifest.BackupId);
            states.TryGetValue(manifest.BackupId, out var existing);
            if (existing is not null &&
                (!string.Equals(existing.Key, key, StringComparison.Ordinal) ||
                 existing.PackageSizeBytes != fileInfo.Length ||
                 !string.Equals(existing.PackageSha256, packageSha, StringComparison.OrdinalIgnoreCase) ||
                 existing.ContentStatus != manifest.ContentStatus))
            {
                warnings.Add($"Se rechazó un paquete que reutiliza BackupId con contenido distinto ({manifest.BackupId:N}).");
                states[manifest.BackupId] = CreateState(
                    manifest,
                    key,
                    packageSha,
                    fileInfo.Length,
                    CloudBackupUploadStatus.Quarantined,
                    _timeProvider.GetUtcNow(),
                    null,
                    existing.VersionId,
                    existing.UploadId,
                    existing.Parts ?? [],
                    "El BackupId ya estaba asociado a otro contenido.");
                return false;
            }

            if (existing is not null && existing.Status == CloudBackupUploadStatus.Uploaded)
            {
                return true;
            }

            using var objectStore = _objectStoreFactory(configuration, credential);

            states[manifest.BackupId] = CreateState(
                manifest,
                key,
                packageSha,
                fileInfo.Length,
                CloudBackupUploadStatus.Uploading,
                _timeProvider.GetUtcNow(),
                null,
                existing?.VersionId,
                existing?.UploadId,
                existing?.Parts ?? [],
                null);
            PersistState(states.Values, warnings);

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["arte-format"] = "aibak-v1",
                ["arte-backup-id"] = manifest.BackupId.ToString("D"),
                // G2 uses these non-sensitive fields to identify and verify
                // a remote package without granting the uploader read access.
                ["arte-package-sha256"] = packageSha,
                ["arte-database-sha256"] = manifest.DatabaseSha256,
                ["arte-created-utc"] = manifest.CreatedUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["arte-source-installation-id"] = manifest.SourceInstallationId.ToString("D"),
                ["arte-content-status"] = manifest.ContentStatus.ToString(),
                ["arte-omitted-assets"] = (manifest.OmittedAssetCount ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            CloudObjectUploadReceipt receipt;
            if (fileInfo.Length < configuration.MultipartThresholdBytes)
            {
                receipt = await PutObjectWithRetryAsync(
                    objectStore,
                    new CloudObjectUploadRequest(
                        configuration.BucketName,
                        key,
                        packagePath,
                        fileInfo.Length,
                        packageSha,
                        metadata,
                        PreventOverwrite: true),
                    cancellationToken);
            }
            else
            {
                receipt = await UploadMultipartAsync(
                    objectStore,
                    configuration,
                    manifest,
                    packagePath,
                    fileInfo.Length,
                    packageSha,
                    key,
                    metadata,
                    states,
                    cancellationToken);
            }

            if (fileInfo.Length < configuration.MultipartThresholdBytes)
            {
                ValidateUploadReceipt(
                    receipt,
                    configuration.BucketName,
                    key,
                    fileInfo.Length,
                    packageSha,
                    expectedChecksumType: "FULL_OBJECT");
            }

            states[manifest.BackupId] = CreateState(
                manifest,
                key,
                packageSha,
                fileInfo.Length,
                CloudBackupUploadStatus.Uploaded,
                _timeProvider.GetUtcNow(),
                _timeProvider.GetUtcNow(),
                receipt.VersionId,
                states[manifest.BackupId].UploadId,
                states[manifest.BackupId].Parts,
                null);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var manifest = validation.Manifest;
            if (manifest is not null && states.TryGetValue(manifest.BackupId, out var current))
            {
                var unknown = current.Status == CloudBackupUploadStatus.Uploading ||
                              ex is CloudUploadUnknownException;
                states[manifest.BackupId] = current with
                {
                    Status = unknown ? CloudBackupUploadStatus.UploadUnknown : CloudBackupUploadStatus.Failed,
                    LastAttemptUtc = _timeProvider.GetUtcNow(),
                    LastError = unknown
                        ? "La confirmación del upload quedó ambigua; se reconciliará en el siguiente intento."
                        : $"Fallo de tipo {ex.GetType().Name}."
                };
            }

            warnings.Add($"No se completó la subida cloud de {Path.GetFileName(packagePath)} ({ex.GetType().Name}).");
            return false;
        }
        finally
        {
            TryDeleteDirectory(validationDirectory);
        }
    }

    private async Task<CloudObjectUploadReceipt> UploadMultipartAsync(
        ICloudBackupObjectStore objectStore,
        CloudBackupConfiguration configuration,
        BackupManifest manifest,
        string packagePath,
        long fileSize,
        string packageSha,
        string key,
        IReadOnlyDictionary<string, string> metadata,
        IDictionary<Guid, CloudBackupUploadState> states,
        CancellationToken cancellationToken)
    {
        states.TryGetValue(manifest.BackupId, out var prior);
        var requested = new CloudMultipartUploadRequest(
            configuration.BucketName,
            key,
            packagePath,
            fileSize,
            packageSha,
            configuration.MultipartPartSizeBytes,
            prior?.UploadId,
            prior?.Parts ?? [],
            metadata,
            PreventOverwrite: true);
        CloudMultipartUploadSession session;
        try
        {
            session = await objectStore.InitiateMultipartUploadAsync(requested, cancellationToken);
        }
        catch (Exception ex) when (prior?.UploadId is not null && ex is IOException or InvalidOperationException)
        {
            session = await objectStore.InitiateMultipartUploadAsync(requested with { UploadId = null }, cancellationToken);
        }

        var remoteParts = await ExecuteWithRetryAsync(
            token => objectStore.ListMultipartPartsAsync(session, token),
            cancellationToken);
        var remoteByNumber = remoteParts.ToDictionary(part => part.PartNumber);
        var persistedByNumber = (prior?.Parts ?? []).ToDictionary(part => part.PartNumber);
        var totalParts = checked((int)((fileSize + configuration.MultipartPartSizeBytes - 1) / configuration.MultipartPartSizeBytes));
        var completed = new List<CloudBackupUploadedPart>(totalParts);
        for (var partNumber = 1; partNumber <= totalParts; partNumber++)
        {
            var position = (partNumber - 1L) * configuration.MultipartPartSizeBytes;
            var size = Math.Min(configuration.MultipartPartSizeBytes, fileSize - position);
            var sha = await ComputeSegmentSha256Async(packagePath, position, size, cancellationToken);
            if (remoteByNumber.TryGetValue(partNumber, out var remotePart) &&
                remotePart.SizeBytes == size &&
                string.Equals(remotePart.Sha256, sha, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(remotePart.ETag))
            {
                completed.Add(new CloudBackupUploadedPart(partNumber, remotePart.ETag, sha, size));
                continue;
            }

            if (persistedByNumber.TryGetValue(partNumber, out var persistedPart) &&
                persistedPart.SizeBytes == size &&
                string.Equals(persistedPart.ChecksumSha256, sha, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(persistedPart.ETag))
            {
                // The provider did not report the persisted part. Re-uploading
                // is safer than trusting local metadata for completion.
            }

            var part = await ExecuteWithRetryAsync(
                token => objectStore.UploadPartAsync(
                    new CloudMultipartPartRequest(
                        configuration.BucketName,
                        key,
                        session.UploadId,
                        partNumber,
                        packagePath,
                        position,
                        size,
                        sha),
                    token),
                cancellationToken);
            if (part.SizeBytes != size ||
                string.IsNullOrWhiteSpace(part.ETag) ||
                string.IsNullOrWhiteSpace(part.Sha256) ||
                !string.Equals(part.Sha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                throw new CloudUploadUnknownException(
                    $"La respuesta de la parte multipart {partNumber} no confirmó tamaño y SHA-256.");
            }

            completed.Add(new CloudBackupUploadedPart(part.PartNumber, part.ETag, sha, part.SizeBytes));
            states[manifest.BackupId] = states[manifest.BackupId] with
            {
                UploadId = session.UploadId,
                Parts = completed.ToArray(),
                Status = CloudBackupUploadStatus.Uploading,
                LastAttemptUtc = _timeProvider.GetUtcNow()
            };
            PersistState(states.Values, []);
        }

        states[manifest.BackupId] = states[manifest.BackupId] with
        {
            UploadId = session.UploadId,
            Parts = completed.ToArray(),
            Status = CloudBackupUploadStatus.Uploading,
            LastAttemptUtc = _timeProvider.GetUtcNow()
        };
        PersistState(states.Values, []);
        var receipt = await ExecuteWithRetryAsync(
            token => objectStore.CompleteMultipartUploadAsync(session, completed, token),
            cancellationToken);
        ValidateUploadReceipt(
            receipt,
            configuration.BucketName,
            key,
            fileSize,
            ComputeCompositeSha256(completed),
            expectedChecksumType: "COMPOSITE");
        return receipt;
    }

    private async Task<CloudObjectUploadReceipt> PutObjectWithRetryAsync(
        ICloudBackupObjectStore objectStore,
        CloudObjectUploadRequest request,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await objectStore.PutObjectAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < 2)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(200L * (1 << attempt)), cancellationToken);
            }
        }

        throw last ?? new IOException("El PUT cloud no pudo completarse.");
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < 2)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(200L * (1 << attempt)), cancellationToken);
            }
        }

        throw last ?? new IOException("La operación cloud no pudo completarse.");
    }

    private static bool IsRetryable(Exception exception) =>
        exception is not (InvalidDataException or ArgumentException or UnauthorizedAccessException or NotSupportedException or CloudBackupWriteConflictException or CloudUploadUnknownException);

    private static void ValidateUploadReceipt(
        CloudObjectUploadReceipt receipt,
        string expectedBucketName,
        string expectedKey,
        long expectedSizeBytes,
        string expectedSha256,
        string? expectedChecksumType = null)
    {
        if (!string.Equals(receipt.BucketName, expectedBucketName, StringComparison.Ordinal) ||
            !string.Equals(receipt.Key, expectedKey, StringComparison.Ordinal) ||
            receipt.SizeBytes != expectedSizeBytes ||
            string.IsNullOrWhiteSpace(receipt.Sha256) ||
            !string.Equals(receipt.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(receipt.VersionId) ||
            (expectedChecksumType is not null &&
             !string.Equals(receipt.ChecksumType, expectedChecksumType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CloudUploadUnknownException(
                "La respuesta de subida no confirmó de forma inequívoca bucket, clave, tamaño, SHA-256 y versión.");
        }
    }

    private static string ComputeCompositeSha256(IReadOnlyList<CloudBackupUploadedPart> parts)
    {
        if (parts.Count == 0 || parts.Any(part => string.IsNullOrWhiteSpace(part.ChecksumSha256)))
        {
            throw new CloudUploadUnknownException(
                "No se pudo calcular el checksum compuesto de las partes multipart.");
        }

        try
        {
            var concatenated = parts
                .OrderBy(part => part.PartNumber)
                .SelectMany(part => Convert.FromHexString(part.ChecksumSha256!))
                .ToArray();
            return Convert.ToHexString(SHA256.HashData(concatenated));
        }
        catch (FormatException ex)
        {
            throw new CloudUploadUnknownException(
                "El checksum SHA-256 de una parte multipart no es hexadecimal.",
                ex);
        }
    }

    private IReadOnlyList<string> EnumerateProtectedPackages(ICollection<string> warnings)
    {
        if (!Directory.Exists(_paths.ProtectedBackupsRoot))
        {
            return [];
        }

        EnsureNoReparsePoint(_paths.ProtectedBackupsRoot);
        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(_paths.ProtectedBackupsRoot, "*.aibak", SearchOption.TopDirectoryOnly))
        {
            try
            {
                EnsurePackagePath(path);
                result.Add(path);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Se omitió un respaldo fuera de la raíz protegida ({ex.GetType().Name}).");
            }
        }

        return result;
    }

    private void MarkQuarantinedIfIdentifiable(
        string packagePath,
        BackupManifest? manifest,
        IDictionary<Guid, CloudBackupUploadState> states,
        ICollection<string> warnings)
    {
        if (manifest is null || manifest.BackupId == Guid.Empty || manifest.SourceInstallationId == Guid.Empty)
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(packagePath);
            var hash = BackupPackageEngine.ComputeSha256Async(packagePath, CancellationToken.None).GetAwaiter().GetResult();
            var key = CloudBackupObjectKey.Create("quarantine", manifest.SourceInstallationId, manifest.BackupId);
            states[manifest.BackupId] = CreateState(
                manifest,
                key,
                hash,
                fileInfo.Length,
                CloudBackupUploadStatus.Quarantined,
                _timeProvider.GetUtcNow(),
                null,
                null,
                null,
                [],
                "El paquete no superó la validación de BackupPackageEngine.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            warnings.Add($"No se pudo registrar el paquete inválido ({ex.GetType().Name}).");
        }
    }

    private void PersistState(IEnumerable<CloudBackupUploadState> states, ICollection<string> warnings)
    {
        try
        {
            _stateStore.Save(new CloudBackupUploadStateDocument(
                CloudBackupUploadStateStore.CurrentSchemaVersion,
                states.OrderBy(entry => entry.BackupId).ToArray()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            warnings.Add($"No se pudo persistir el estado cloud ({ex.GetType().Name}).");
        }
    }

    private CloudBackupRunResult Result(
        CloudBackupRunOutcome outcome,
        IReadOnlyList<CloudBackupUploadState> states,
        string message,
        IReadOnlyList<string> warnings) =>
        new(outcome, states, message, warnings);

    private static CloudBackupUploadState CreateState(
        BackupManifest manifest,
        string key,
        string packageSha,
        long packageSize,
        CloudBackupUploadStatus status,
        DateTimeOffset attemptUtc,
        DateTimeOffset? confirmedUtc,
        string? versionId,
        string? uploadId,
        IReadOnlyList<CloudBackupUploadedPart> parts,
        string? error) =>
        new(
            manifest.BackupId,
            key,
            packageSha,
            packageSize,
            manifest.ContentStatus,
            status,
            attemptUtc,
            confirmedUtc,
            versionId,
            uploadId,
            parts,
            error);

    private void EnsurePackagePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!IsWithin(full, _paths.ProtectedBackupsRoot) ||
            !full.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El paquete cloud queda fuera de ProtectedBackups.");
        }

        EnsureNoReparsePoint(full);
        var file = new FileInfo(full);
        if (file.Length <= 0)
        {
            throw new InvalidDataException("El paquete cloud está vacío.");
        }
    }

    private void ValidateCloudRoots()
    {
        foreach (var path in new[]
        {
            _paths.MachineInstallRoot,
            _paths.MachineDataRoot,
            _paths.ProtectedBackupsRoot,
            _paths.CloudBackupRoot,
            _paths.CloudBackupValidationRoot,
            _paths.CloudBackupConfigurationPath,
            _paths.CloudBackupUploadStatePath,
            _paths.CloudBackupCredentialsRoot,
            _paths.CloudBackupCredentialPath
        })
        {
            EnsureNoReparsePoint(path);
        }
    }

    private void EnsureMachineWide()
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException("El CloudBackupRunner requiere el layout machine-wide protegido.");
        }
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

    private static Guid? GetConfiguredInstallationId(ArteInventariadoPathProvider paths)
    {
        var status = new LocalStationConfigurationStore(paths).Load().Status;
        return status.IsConfigured && status.InstallationId.HasValue
            ? status.InstallationId
            : null;
    }

    private static async Task<string> ComputeSegmentSha256Async(
        string path,
        long position,
        long length,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = position;
        var buffer = new byte[128 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("El paquete cambió durante el cálculo de una parte multipart.");
            }

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void EnsureNoReparsePoint(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"La ruta cloud contiene un reparse point: '{current}'.");
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
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

    private sealed class CloudUploadUnknownException : IOException
    {
        public CloudUploadUnknownException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}

internal interface ICloudBackupLock : IDisposable
{
    bool TryAcquire();
}

internal sealed class NamedCloudBackupLock : ICloudBackupLock
{
    private readonly Mutex _mutex;
    private bool _acquired;

    public NamedCloudBackupLock(string name)
    {
        _mutex = new Mutex(initiallyOwned: false, name);
    }

    public bool TryAcquire()
    {
        try
        {
            _acquired = _mutex.WaitOne(TimeSpan.Zero);
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
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }
}
