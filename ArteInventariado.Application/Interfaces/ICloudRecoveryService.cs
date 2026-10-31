using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Read-only cloud recovery boundary for the future restore workflow.
/// Implementations must authorize the protected administrator, authenticate
/// with short-lived recovery credentials, scope every request to one
/// installation and return only validated local candidates.  No method
/// mutates the live database or exposes remote deletion.
/// </summary>
public interface ICloudRecoveryService
{
    Task<CloudRecoveryCatalogResult> GetCatalogAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CancellationToken cancellationToken = default);

    Task<CloudRecoveryCandidate> DownloadAndValidateAsync(
        CloudRecoveryAuthenticationRequest authentication,
        CloudRecoveryScope scope,
        CloudRecoveryCatalogItem selection,
        CancellationToken cancellationToken = default);

    Task CleanupCandidateAsync(
        CloudRecoveryCandidate candidate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application authorization boundary.  The implementation is deliberately
/// stricter than the ADMIN role: only the existing protected principal
/// (username <c>admin</c>) may start cloud recovery.
/// </summary>
public interface ICloudRecoveryAuthorizationService
{
    Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Exchanges owner-supplied MFA/bootstrap data for temporary STS credentials.</summary>
public interface ICloudRecoveryAuthenticator
{
    Task<CloudRecoveryTemporaryCredentials> AuthenticateAsync(
        CloudRecoveryAuthenticationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Narrow read-only provider contract used by G2.  It intentionally has no
/// Put, multipart, delete, lifecycle or bucket-administration operation.
/// </summary>
public interface ICloudRecoveryObjectStore : IDisposable
{
    Task<IReadOnlyList<CloudRemoteObjectInfo>> ListVersionsAsync(
        string bucketName,
        string prefix,
        CancellationToken cancellationToken = default);

    Task<CloudRemoteObjectMetadata> GetMetadataAsync(
        string bucketName,
        string key,
        string? versionId,
        CancellationToken cancellationToken = default);

    Task DownloadAsync(
        string bucketName,
        string key,
        string? versionId,
        Stream destination,
        CancellationToken cancellationToken = default);
}
