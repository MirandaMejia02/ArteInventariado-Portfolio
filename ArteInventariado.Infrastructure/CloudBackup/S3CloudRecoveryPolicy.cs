using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// IAM contract for the separate recovery identity.  It is read-only and
/// scoped to one installation prefix; it never contains uploader, delete,
/// lifecycle, IAM or Object Lock bypass permissions.
/// </summary>
public sealed record S3RecoveryPermissionContract(
    string BucketArn,
    string ObjectArn,
    string InstallationPrefix,
    IReadOnlySet<string> AllowedActions,
    IReadOnlySet<string> ForbiddenActions);

public static class S3RecoveryPolicyBuilder
{
    private static readonly Regex BucketName = new(
        "^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static S3RecoveryPermissionContract Build(
        string bucketName,
        string keyPrefix,
        Guid installationId)
    {
        if (string.IsNullOrWhiteSpace(bucketName) || !BucketName.IsMatch(bucketName))
        {
            throw new ArgumentException("El bucket no tiene un nombre S3 válido.", nameof(bucketName));
        }

        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("La instalación de recuperación no puede estar vacía.", nameof(installationId));
        }

        CloudBackupConfigurationStore.ValidatePrefix(keyPrefix);
        var bucketArn = $"arn:aws:s3:::{bucketName}";
        var installationPrefix = CloudBackupObjectKey.CreateInstallationPrefix(keyPrefix, installationId);
        var objectArn = $"{bucketArn}/{installationPrefix}/*";
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "s3:ListBucket",
            "s3:ListBucketVersions",
            "s3:GetObject",
            "s3:GetObjectVersion"
        };
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "s3:PutObject",
            "s3:CreateMultipartUpload",
            "s3:UploadPart",
            "s3:CompleteMultipartUpload",
            "s3:AbortMultipartUpload",
            "s3:DeleteObject",
            "s3:DeleteObjectVersion",
            "s3:RestoreObject",
            "s3:PutObjectAcl",
            "s3:PutObjectLegalHold",
            "s3:PutObjectRetention",
            "s3:BypassGovernanceRetention",
            "s3:PutBucketPolicy",
            "s3:PutLifecycleConfiguration",
            "s3:PutBucketVersioning",
            "iam:CreateUser",
            "iam:CreateAccessKey",
            "iam:PutUserPolicy"
        };

        return new S3RecoveryPermissionContract(
            bucketArn,
            objectArn,
            installationPrefix,
            allowed,
            forbidden);
    }

    public static string CreatePolicyDocument(
        string bucketName,
        string keyPrefix,
        Guid installationId)
    {
        var contract = Build(bucketName, keyPrefix, installationId);
        var listActions = new[] { "s3:ListBucket", "s3:ListBucketVersions" };
        var readActions = new[] { "s3:GetObject", "s3:GetObjectVersion" };
        var prefix = contract.InstallationPrefix + "/*";

        var document = new
        {
            Version = "2012-10-17",
            Statement = new object[]
            {
                new
                {
                    Sid = "ArteInventariadoRecoveryListAuthorizedPrefix",
                    Effect = "Allow",
                    Action = listActions,
                    Resource = contract.BucketArn,
                    Condition = new { StringLike = new Dictionary<string, string[]> { ["s3:prefix"] = [contract.InstallationPrefix, prefix] } }
                },
                new
                {
                    Sid = "ArteInventariadoRecoveryReadAuthorizedObjects",
                    Effect = "Allow",
                    Action = readActions,
                    Resource = contract.ObjectArn
                }
            }
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }
}
