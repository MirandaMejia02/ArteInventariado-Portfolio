using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Read-only S3 adapter for G2.  It uses the short-lived STS session and
/// validates every bucket/key scope before making a provider call.
/// </summary>
public sealed class S3CloudRecoveryObjectStore : ICloudRecoveryObjectStore
{
    private readonly IAmazonS3 _client;
    private readonly CloudBackupConfiguration _configuration;
    private bool _disposed;

    public S3CloudRecoveryObjectStore(
        CloudBackupConfiguration configuration,
        CloudRecoveryTemporaryCredentials credentials)
        : this(
            configuration,
            new AmazonS3Client(
                new SessionAWSCredentials(
                    credentials?.AccessKeyId ?? throw new ArgumentNullException(nameof(credentials)),
                    credentials.SecretAccessKey,
                    credentials.SessionToken),
                CreateClientConfiguration(configuration)))
    {
        ValidateCredentials(credentials);
    }

    internal S3CloudRecoveryObjectStore(
        CloudBackupConfiguration configuration,
        IAmazonS3 client)
    {
        CloudBackupConfigurationStore.Validate(configuration with { Enabled = true });
        _configuration = configuration;
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<CloudRemoteObjectInfo>> ListVersionsAsync(
        string bucketName,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidatePrefix(bucketName, prefix);

        var result = new List<CloudRemoteObjectInfo>();
        string? keyMarker = null;
        string? versionIdMarker = null;
        do
        {
            var response = await _client.ListVersionsAsync(
                new ListVersionsRequest
                {
                    BucketName = bucketName,
                    Prefix = prefix,
                    KeyMarker = keyMarker,
                    VersionIdMarker = versionIdMarker
                },
                cancellationToken);

            result.AddRange(response.Versions.Select(version => new CloudRemoteObjectInfo(
                version.Key,
                version.VersionId,
                version.IsLatest,
                version.IsDeleteMarker,
                version.Size,
                version.LastModified == default
                    ? null
                    : new DateTimeOffset(version.LastModified.ToUniversalTime(), TimeSpan.Zero),
                version.ETag)));

            keyMarker = response.IsTruncated ? response.NextKeyMarker : null;
            versionIdMarker = response.IsTruncated ? response.NextVersionIdMarker : null;
        }
        while (keyMarker is not null || versionIdMarker is not null);

        return result;
    }

    public async Task<CloudRemoteObjectMetadata> GetMetadataAsync(
        string bucketName,
        string key,
        string? versionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(bucketName, key);
        ValidateVersionId(versionId);

        var response = await _client.GetObjectMetadataAsync(
            new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = key,
                VersionId = versionId,
                ChecksumMode = ChecksumMode.ENABLED
            },
            cancellationToken);

        var metadata = response.Metadata.Keys
            .Select(key => new KeyValuePair<string, string>(NormalizeMetadataKey(key), response.Metadata[key]))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

        return new CloudRemoteObjectMetadata(
            bucketName,
            key,
            response.VersionId,
            response.ContentLength,
            response.LastModified == default
                ? null
                : new DateTimeOffset(response.LastModified.ToUniversalTime(), TimeSpan.Zero),
            response.ETag,
            DecodeChecksum(response.ChecksumSHA256),
            response.ChecksumType?.Value,
            metadata);
    }

    public async Task DownloadAsync(
        string bucketName,
        string key,
        string? versionId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination);
        ValidateKey(bucketName, key);
        ValidateVersionId(versionId);

        using var response = await _client.GetObjectAsync(
            new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                VersionId = versionId,
                ChecksumMode = ChecksumMode.ENABLED
            },
            cancellationToken);
        await response.ResponseStream.CopyToAsync(destination, cancellationToken);
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

    private void ValidatePrefix(string bucketName, string prefix)
    {
        if (!string.Equals(bucketName, _configuration.BucketName, StringComparison.Ordinal) ||
            !CloudBackupObjectKey.IsWithinPrefix(prefix + "/placeholder.aibak", _configuration.KeyPrefix) ||
            !prefix.StartsWith(_configuration.KeyPrefix + "/", StringComparison.Ordinal) ||
            prefix.Contains("..", StringComparison.Ordinal) ||
            prefix.Contains('\\') ||
            prefix.Contains(':'))
        {
            throw new InvalidDataException("El prefijo S3 queda fuera del destino cloud autorizado.");
        }
    }

    private void ValidateKey(string bucketName, string key)
    {
        if (!string.Equals(bucketName, _configuration.BucketName, StringComparison.Ordinal) ||
            !CloudBackupObjectKey.IsWithinPrefix(key, _configuration.KeyPrefix) ||
            !key.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La clave S3 queda fuera del destino cloud autorizado.");
        }
    }

    private static void ValidateVersionId(string? versionId)
    {
        if (versionId is not null &&
            (versionId.Length == 0 || versionId.Length > 1024 || versionId.Any(char.IsControl)))
        {
            throw new InvalidDataException("El identificador de versión S3 no es válido.");
        }
    }

    private static AmazonS3Config CreateClientConfiguration(CloudBackupConfiguration configuration)
    {
        CloudBackupConfigurationStore.Validate(configuration with { Enabled = true });
        return new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(configuration.RegionSystemName),
            ForcePathStyle = false
        };
    }

    private static void ValidateCredentials(CloudRecoveryTemporaryCredentials? credentials)
    {
        if (credentials is null ||
            string.IsNullOrWhiteSpace(credentials.AccessKeyId) ||
            string.IsNullOrWhiteSpace(credentials.SecretAccessKey) ||
            string.IsNullOrWhiteSpace(credentials.SessionToken) ||
            credentials.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidDataException("Las credenciales temporales de recuperación no son válidas.");
        }
    }

    private static string? DecodeChecksum(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.ToHexString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string NormalizeMetadataKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return normalized.StartsWith("x-amz-meta-", StringComparison.Ordinal)
            ? normalized["x-amz-meta-".Length..]
            : normalized;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
