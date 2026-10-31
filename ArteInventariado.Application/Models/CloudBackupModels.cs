namespace ArteInventariado.Application.Models;

/// <summary>
/// Persisted state for the protected off-site transport.  The state is
/// intentionally metadata-only: it never contains cloud credentials or the
/// contents of a backup package.
/// </summary>
public enum CloudBackupUploadStatus
{
    Pending = 0,
    Uploading = 1,
    Uploaded = 2,
    Failed = 3,
    UploadUnknown = 4,
    Quarantined = 5
}

public enum CloudBackupRunOutcome
{
    Succeeded = 1,
    SkippedDisabled = 2,
    SkippedConcurrent = 3,
    NoCandidates = 4,
    Failed = 5
}

public sealed record CloudBackupConfiguration(
    int SchemaVersion,
    bool Enabled,
    string BucketName,
    string RegionSystemName,
    string KeyPrefix,
    long MultipartThresholdBytes = 100L * 1024 * 1024,
    long MultipartPartSizeBytes = 16L * 1024 * 1024);

public sealed record CloudBackupCredential(
    string AccessKeyId,
    string SecretAccessKey)
{
    public override string ToString() => "CloudBackupCredential { secrets redacted }";
}

/// <summary>
/// Non-secret identity returned by the optional provisioning preflight.  It
/// is never persisted and is safe to show to the owner after STS validates the
/// supplied upload credential.
/// </summary>
public sealed record CloudBackupProvisioningIdentity(
    string AccountId,
    string Arn,
    string UserId)
{
    public override string ToString() =>
        $"CloudBackupProvisioningIdentity {{ AccountId = {AccountId}, Arn = {Arn}, UserId = {UserId} }}";
}

/// <summary>
/// Result of the no-upload provisioning preflight.  S3 write permission is
/// deliberately not claimed: proving PutObject with an upload-only identity
/// would require creating a remote object, which the provisioning flow does
/// not do.
/// </summary>
public sealed record CloudBackupProvisioningCheck(
    CloudBackupProvisioningIdentity Identity,
    bool TargetValidated,
    bool WritePermissionVerified,
    string WritePermissionNote);

/// <summary>
/// Short-lived, one-use authorization issued only after the protected
/// administrator reauthenticates.  It contains no password or cloud secret;
/// the proof is checked against the persisted administrator verifier by the
/// elevated process before the provisioning form can be used.
/// </summary>
public sealed record CloudBackupProvisioningAuthorizationGrant(
    int PrincipalUserId,
    Guid InstallationId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Proof);

public sealed record CloudBackupUploadedPart(
    int PartNumber,
    string ETag,
    string? ChecksumSha256,
    long SizeBytes);

public sealed record CloudBackupUploadState(
    Guid BackupId,
    string Key,
    string PackageSha256,
    long PackageSizeBytes,
    BackupContentStatus ContentStatus,
    CloudBackupUploadStatus Status,
    DateTimeOffset LastAttemptUtc,
    DateTimeOffset? ConfirmedUtc,
    string? VersionId,
    string? UploadId,
    IReadOnlyList<CloudBackupUploadedPart> Parts,
    string? LastError);

public sealed record CloudBackupUploadStateDocument(
    int SchemaVersion,
    IReadOnlyList<CloudBackupUploadState> Entries);

public sealed record CloudBackupRunResult(
    CloudBackupRunOutcome Outcome,
    IReadOnlyList<CloudBackupUploadState> States,
    string Message,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Provider-neutral object requests used by the S3 adapter and deterministic
/// fakes.  File paths are supplied only by the protected runner after it has
/// validated the package and its root.
/// </summary>
public sealed record CloudObjectUploadRequest(
    string BucketName,
    string Key,
    string FilePath,
    long SizeBytes,
    string Sha256,
    IReadOnlyDictionary<string, string> Metadata,
    bool PreventOverwrite = true);

public sealed record CloudMultipartUploadRequest(
    string BucketName,
    string Key,
    string FilePath,
    long SizeBytes,
    string Sha256,
    long PartSizeBytes,
    string? UploadId,
    IReadOnlyList<CloudBackupUploadedPart> CompletedParts,
    IReadOnlyDictionary<string, string> Metadata,
    bool PreventOverwrite = true);

public sealed record CloudObjectUploadReceipt(
    string BucketName,
    string Key,
    long SizeBytes,
    string? Sha256,
    string? VersionId,
    string? ETag,
    string? ChecksumType = null);

/// <summary>
/// Raised when the provider refuses an upload because the deterministic key
/// already exists.  It is deliberately non-retryable: the upload-only
/// principal cannot read the existing object to reconcile it safely.
/// </summary>
public sealed class CloudBackupWriteConflictException : IOException
{
    public CloudBackupWriteConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed record CloudMultipartUploadSession(
    string BucketName,
    string Key,
    string UploadId,
    bool PreventOverwrite = true,
    long SizeBytes = 0,
    string? Sha256 = null);

public sealed record CloudMultipartPartRequest(
    string BucketName,
    string Key,
    string UploadId,
    int PartNumber,
    string FilePath,
    long FilePosition,
    long PartSizeBytes,
    string Sha256);

public sealed record CloudMultipartPartReceipt(
    int PartNumber,
    string ETag,
    string? Sha256,
    long SizeBytes);
