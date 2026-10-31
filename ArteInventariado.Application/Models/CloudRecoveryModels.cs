using System.Text.Json.Serialization;

namespace ArteInventariado.Application.Models;

/// <summary>
/// Ephemeral owner-provided bootstrap data used only to request a recovery
/// session.  It is a request object, never persisted by ArteInventariado.
/// </summary>
public sealed record CloudRecoveryAuthenticationRequest(
    [property: JsonIgnore]
    string BootstrapAccessKeyId,
    [property: JsonIgnore]
    string BootstrapSecretAccessKey,
    [property: JsonIgnore]
    string RoleArn,
    [property: JsonIgnore]
    string MfaSerialNumber,
    [property: JsonIgnore]
    string MfaTokenCode,
    string RegionSystemName,
    string? ExternalId = null,
    string? SessionName = null,
    int DurationSeconds = 900)
{
    public override string ToString() => "CloudRecoveryAuthenticationRequest { secrets redacted }";
}

/// <summary>
/// Short-lived STS credentials kept in memory for one recovery operation.
/// </summary>
public sealed record CloudRecoveryTemporaryCredentials(
    [property: JsonIgnore]
    string AccessKeyId,
    [property: JsonIgnore]
    string SecretAccessKey,
    [property: JsonIgnore]
    string SessionToken,
    DateTimeOffset ExpiresUtc)
{
    public override string ToString() => "CloudRecoveryTemporaryCredentials { secrets redacted }";
}

/// <summary>
/// Explicit installation scope selected by the authenticated owner.  A
/// caller must acknowledge a non-local installation before it can be
/// enumerated; the service never lists the bucket root.
/// </summary>
public sealed record CloudRecoveryScope(
    Guid InstallationId,
    bool OwnerConfirmed);

public sealed record CloudRemoteObjectInfo(
    string Key,
    string? VersionId,
    bool IsLatest,
    bool IsDeleteMarker,
    long SizeBytes,
    DateTimeOffset? LastModifiedUtc,
    string? ETag);

public sealed record CloudRemoteObjectMetadata(
    string BucketName,
    string Key,
    string? VersionId,
    long SizeBytes,
    DateTimeOffset? LastModifiedUtc,
    string? ETag,
    string? ChecksumSha256,
    string? ChecksumType,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Remote metadata shown in the recovery catalog.  Full package validation
/// is intentionally deferred until a package is downloaded.
/// </summary>
public sealed record CloudRecoveryCatalogItem(
    Guid InstallationId,
    Guid BackupId,
    string Key,
    string? VersionId,
    bool IsLatest,
    DateTimeOffset? CreatedUtc,
    long SizeBytes,
    BackupContentStatus? ContentStatus,
    int? OmittedAssetCount,
    string? PackageSha256,
    string? RemoteChecksumSha256,
    string? RemoteChecksumType,
    bool MetadataComplete);

public sealed record CloudRecoveryCatalogResult(
    IReadOnlyList<CloudRecoveryCatalogItem> Items,
    CloudRecoveryCatalogItem? RecommendedHealthy,
    CloudRecoveryCatalogItem? RecommendedFallback,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A validated, protected local package handed to the future restore block.
/// It contains no credentials and does not perform restoration itself.
/// </summary>
public sealed record CloudRecoveryCandidate(
    Guid InstallationId,
    Guid BackupId,
    string PackagePath,
    string Key,
    string? VersionId,
    DateTimeOffset? CreatedUtc,
    BackupContentStatus ContentStatus,
    int OmittedAssetCount,
    long SizeBytes,
    string PackageSha256,
    string? RemoteChecksumSha256,
    string? RemoteChecksumType,
    IReadOnlyList<string> ValidationWarnings);

/// <summary>
/// Explicitly documents that the recovery authentication request must not be
/// serialized into application state.  The attribute also prevents accidental
/// JSON export if a diagnostic object is ever inspected by tooling.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CloudRecoveryOperationState
{
    Ready = 1,
    Cancelled = 2,
    Completed = 3
}
