using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Narrow object-store contract for the upload-only G1 boundary.  There is no
/// object-read, delete, lifecycle, restore, or bucket-administration operation
/// here by design.  Listing is limited to parts of an in-progress multipart
/// upload so a persisted upload can resume without downloading an object.
/// </summary>
public interface ICloudBackupObjectStore : IDisposable
{
    Task<CloudObjectUploadReceipt> PutObjectAsync(
        CloudObjectUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<CloudMultipartUploadSession> InitiateMultipartUploadAsync(
        CloudMultipartUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<CloudMultipartPartReceipt> UploadPartAsync(
        CloudMultipartPartRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudMultipartPartReceipt>> ListMultipartPartsAsync(
        CloudMultipartUploadSession session,
        CancellationToken cancellationToken = default);

    Task<CloudObjectUploadReceipt> CompleteMultipartUploadAsync(
        CloudMultipartUploadSession session,
        IReadOnlyList<CloudBackupUploadedPart> parts,
        CancellationToken cancellationToken = default);

    Task AbortMultipartUploadAsync(
        CloudMultipartUploadSession session,
        CancellationToken cancellationToken = default);
}
