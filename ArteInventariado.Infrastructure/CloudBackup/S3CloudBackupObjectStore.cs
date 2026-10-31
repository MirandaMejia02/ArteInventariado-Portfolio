using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using System.Security.Cryptography;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Amazon S3 adapter for the narrow upload-only object-store contract.  It
/// never exposes delete, lifecycle, bucket-policy, or restore operations.
/// </summary>
public sealed class S3CloudBackupObjectStore : ICloudBackupObjectStore
{
    private readonly IAmazonS3 _client;
    private readonly CloudBackupConfiguration _configuration;
    private bool _disposed;

    public S3CloudBackupObjectStore(
        CloudBackupConfiguration configuration,
        CloudBackupCredential credential)
        : this(
            configuration,
            new AmazonS3Client(
                new BasicAWSCredentials(
                    credential?.AccessKeyId ?? throw new ArgumentNullException(nameof(credential)),
                    credential.SecretAccessKey),
                CreateClientConfiguration(configuration)))
    {
        DpapiCloudBackupSecretStore.Validate(credential);
    }

    internal S3CloudBackupObjectStore(
        CloudBackupConfiguration configuration,
        IAmazonS3 client)
    {
        CloudBackupConfigurationStore.Validate(configuration);
        _configuration = configuration;
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<CloudObjectUploadReceipt> PutObjectAsync(
        CloudObjectUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTarget(request.BucketName, request.Key);
        ValidateFile(request.FilePath, request.SizeBytes, request.Sha256);

        var put = new PutObjectRequest
        {
            BucketName = request.BucketName,
            Key = request.Key,
            FilePath = request.FilePath,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            ChecksumSHA256 = ToBase64Sha256(request.Sha256),
            IfNoneMatch = request.PreventOverwrite ? "*" : null
        };
        AddMetadata(put.Metadata, request.Metadata);
        PutObjectResponse response;
        try
        {
            response = await _client.PutObjectAsync(put, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (IsConditionalWriteFailure(ex))
        {
            throw new CloudBackupWriteConflictException(
                "S3 rechazó el PUT porque la clave determinística ya existe.",
                ex);
        }
        return new CloudObjectUploadReceipt(
            request.BucketName,
            request.Key,
            request.SizeBytes,
            FromBase64OrNull(response.ChecksumSHA256),
            response.VersionId,
            response.ETag,
            response.ChecksumType);
    }

    public async Task<CloudMultipartUploadSession> InitiateMultipartUploadAsync(
        CloudMultipartUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTarget(request.BucketName, request.Key);
        ValidateFile(request.FilePath, request.SizeBytes, request.Sha256);
        ValidatePartSize(request.PartSizeBytes);

        if (!string.IsNullOrWhiteSpace(request.UploadId))
        {
            return new CloudMultipartUploadSession(
                request.BucketName,
                request.Key,
                request.UploadId,
                request.PreventOverwrite,
                request.SizeBytes,
                request.Sha256);
        }

        var initiate = new InitiateMultipartUploadRequest
        {
            BucketName = request.BucketName,
            Key = request.Key,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            ChecksumType = Amazon.S3.ChecksumType.COMPOSITE
        };
        AddMetadata(initiate.Metadata, request.Metadata);
        var response = await _client.InitiateMultipartUploadAsync(initiate, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.UploadId))
        {
            throw new InvalidDataException("S3 no devolvió un UploadId multipart.");
        }

        return new CloudMultipartUploadSession(
            request.BucketName,
            request.Key,
            response.UploadId,
            request.PreventOverwrite,
            request.SizeBytes,
            request.Sha256);
    }

    public async Task<CloudMultipartPartReceipt> UploadPartAsync(
        CloudMultipartPartRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTarget(request.BucketName, request.Key);
        if (string.IsNullOrWhiteSpace(request.UploadId) ||
            request.PartNumber is < 1 or > 10_000 ||
            request.FilePosition < 0 ||
            request.PartSizeBytes <= 0)
        {
            throw new ArgumentException("La parte multipart no es válida.", nameof(request));
        }

        ValidateFileSegment(request.FilePath, request.FilePosition, request.PartSizeBytes, request.Sha256);
        var upload = new UploadPartRequest
        {
            BucketName = request.BucketName,
            Key = request.Key,
            UploadId = request.UploadId,
            PartNumber = request.PartNumber,
            FilePath = request.FilePath,
            FilePosition = request.FilePosition,
            PartSize = request.PartSizeBytes,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            ChecksumSHA256 = ToBase64Sha256(request.Sha256)
        };
        var response = await _client.UploadPartAsync(upload, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.ETag))
        {
            throw new InvalidDataException("S3 no devolvió ETag para la parte multipart.");
        }

        return new CloudMultipartPartReceipt(
            request.PartNumber,
            response.ETag,
            FromBase64OrNull(response.ChecksumSHA256),
            request.PartSizeBytes);
    }

    public async Task<IReadOnlyList<CloudMultipartPartReceipt>> ListMultipartPartsAsync(
        CloudMultipartUploadSession session,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTarget(session.BucketName, session.Key);
        if (string.IsNullOrWhiteSpace(session.UploadId))
        {
            return [];
        }

        var response = await _client.ListPartsAsync(
            new ListPartsRequest
            {
                BucketName = session.BucketName,
                Key = session.Key,
                UploadId = session.UploadId
            },
            cancellationToken);
        return response.Parts
            .Select(part => new CloudMultipartPartReceipt(
                part.PartNumber,
                part.ETag,
                FromBase64OrNull(part.ChecksumSHA256),
                part.Size))
            .ToArray();
    }

    public async Task<CloudObjectUploadReceipt> CompleteMultipartUploadAsync(
        CloudMultipartUploadSession session,
        IReadOnlyList<CloudBackupUploadedPart> parts,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTarget(session.BucketName, session.Key);
        if (string.IsNullOrWhiteSpace(session.UploadId) || parts is null || parts.Count == 0)
        {
            throw new ArgumentException("La sesión multipart no es válida.", nameof(session));
        }

        var orderedParts = parts
            .OrderBy(part => part.PartNumber)
            .ToArray();
        var compositeSha256 = ComputeCompositeSha256(orderedParts);
        var objectSize = session.SizeBytes > 0
            ? session.SizeBytes
            : orderedParts.Sum(part => part.SizeBytes);
        var complete = new CompleteMultipartUploadRequest
        {
            BucketName = session.BucketName,
            Key = session.Key,
            UploadId = session.UploadId,
            IfNoneMatch = session.PreventOverwrite ? "*" : null,
            ChecksumSHA256 = ToBase64Sha256(compositeSha256),
            ChecksumType = Amazon.S3.ChecksumType.COMPOSITE,
            MpuObjectSize = objectSize,
            PartETags = orderedParts
                .Select(part => new PartETag(part.PartNumber, part.ETag)
                {
                    ChecksumSHA256 = ToBase64Sha256(part.ChecksumSha256!)
                })
                .ToList()
        };
        CompleteMultipartUploadResponse response;
        try
        {
            response = await _client.CompleteMultipartUploadAsync(complete, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (IsConditionalWriteFailure(ex))
        {
            throw new CloudBackupWriteConflictException(
                "S3 rechazó CompleteMultipartUpload porque la clave determinística ya existe.",
                ex);
        }
        return new CloudObjectUploadReceipt(
            session.BucketName,
            session.Key,
            objectSize,
            FromBase64OrNull(response.ChecksumSHA256),
            response.VersionId,
            response.ETag,
            response.ChecksumType);
    }

    public async Task AbortMultipartUploadAsync(
        CloudMultipartUploadSession session,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTarget(session.BucketName, session.Key);
        if (string.IsNullOrWhiteSpace(session.UploadId))
        {
            return;
        }

        await _client.AbortMultipartUploadAsync(
            new AbortMultipartUploadRequest
            {
                BucketName = session.BucketName,
                Key = session.Key,
                UploadId = session.UploadId
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    private static AmazonS3Config CreateClientConfiguration(CloudBackupConfiguration configuration)
    {
        CloudBackupConfigurationStore.Validate(configuration);
        return new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(configuration.RegionSystemName),
            ForcePathStyle = false
        };
    }

    private void ValidateTarget(string bucketName, string key)
    {
        if (!string.Equals(bucketName, _configuration.BucketName, StringComparison.Ordinal) ||
            !CloudBackupObjectKey.IsWithinPrefix(key, _configuration.KeyPrefix) ||
            !key.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El bucket o la clave S3 quedan fuera del destino cloud autorizado.");
        }
    }

    private static void ValidateFile(string path, long expectedSize, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("El paquete cloud no existe.", path);
        }

        var file = new FileInfo(path);
        if (file.Length != expectedSize || string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new InvalidDataException("El paquete cloud cambió antes de la subida.");
        }
    }

    private static void ValidateFileSegment(
        string path,
        long filePosition,
        long partSize,
        string expectedSha256)
    {
        ValidateFile(path, new FileInfo(path).Length, expectedSha256);
        if (filePosition > new FileInfo(path).Length || partSize > new FileInfo(path).Length - filePosition)
        {
            throw new InvalidDataException("La parte multipart queda fuera del archivo.");
        }
    }

    private static void ValidatePartSize(long partSizeBytes)
    {
        if (partSizeBytes is < 5L * 1024 * 1024 or > 5L * 1024 * 1024 * 1024)
        {
            throw new InvalidDataException("El tamaño de parte multipart no es compatible con S3.");
        }
    }

    private static void AddMetadata(
        MetadataCollection metadata,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Key) || string.IsNullOrWhiteSpace(value.Value))
            {
                continue;
            }

            metadata[value.Key] = value.Value;
        }
    }

    private static string ToBase64Sha256(string hex)
    {
        try
        {
            var bytes = Convert.FromHexString(hex);
            if (bytes.Length != 32)
            {
                throw new InvalidDataException("El SHA-256 no tiene 32 bytes.");
            }

            return Convert.ToBase64String(bytes);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("El SHA-256 del paquete no es hexadecimal.", ex);
        }
    }

    private static string? FromBase64OrNull(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            return Convert.ToHexString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string ComputeCompositeSha256(IReadOnlyList<CloudBackupUploadedPart> parts)
    {
        if (parts.Count == 0 || parts.Any(part => string.IsNullOrWhiteSpace(part.ChecksumSha256)))
        {
            throw new InvalidDataException("Faltan checksums SHA-256 de partes multipart.");
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
            throw new InvalidDataException("El checksum SHA-256 de una parte multipart no es hexadecimal.", ex);
        }
    }

    private static bool IsConditionalWriteFailure(AmazonS3Exception exception) =>
        exception.StatusCode is
            System.Net.HttpStatusCode.PreconditionFailed or
            System.Net.HttpStatusCode.Conflict;
}
