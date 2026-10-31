using System.Text.RegularExpressions;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.CloudBackup;

public sealed record S3UploadPermissionContract(
    string BucketArn,
    string ObjectArn,
    IReadOnlySet<string> AllowedActions,
    IReadOnlySet<string> ForbiddenActions);

/// <summary>
/// Builds the IAM contract only; it never calls IAM or changes an AWS
/// account.  The resulting set is deliberately upload-only and scoped to one
/// bucket/prefix.
/// </summary>
public static class S3UploadPolicyBuilder
{
    private static readonly Regex BucketName = new(
        "^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static S3UploadPermissionContract Build(
        string bucketName,
        string keyPrefix)
    {
        if (string.IsNullOrWhiteSpace(bucketName) || !BucketName.IsMatch(bucketName))
        {
            throw new ArgumentException("El bucket no tiene un nombre S3 válido.", nameof(bucketName));
        }

        CloudBackupConfigurationStore.ValidatePrefix(keyPrefix);
        var bucketArn = $"arn:aws:s3:::{bucketName}";
        var objectArn = $"{bucketArn}/{keyPrefix}/*";
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "s3:PutObject",
            "s3:AbortMultipartUpload",
            "s3:ListMultipartUploadParts"
        };
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "s3:GetObject",
            "s3:GetObjectVersion",
            "s3:GetObjectAttributes",
            "s3:ListBucket",
            "s3:ListBucketMultipartUploads",
            "s3:DeleteObject",
            "s3:DeleteObjectVersion",
            "s3:RestoreObject",
            "s3:PutObjectAcl",
            "s3:PutObjectLegalHold",
            "s3:PutBucketPolicy",
            "s3:PutLifecycleConfiguration",
            "s3:PutBucketVersioning",
            "s3:PutObjectRetention",
            "s3:BypassGovernanceRetention",
            "iam:CreateUser",
            "iam:CreateAccessKey",
            "iam:PutUserPolicy"
        };

        return new S3UploadPermissionContract(bucketArn, objectArn, allowed, forbidden);
    }

    public static string CreatePolicyDocument(
        string bucketName,
        string keyPrefix)
    {
        var contract = Build(bucketName, keyPrefix);
        var actions = contract.AllowedActions.OrderBy(action => action, StringComparer.Ordinal).ToArray();
        return $$"""
            {
              "Version": "2012-10-17",
              "Statement": [
                {
                  "Sid": "ArteInventariadoUploadOnly",
                  "Effect": "Allow",
                  "Action": [{{string.Join(",", actions.Select(action => $"\"{action}\""))}}],
                  "Resource": "{{contract.ObjectArn}}"
                }
              ]
            }
            """;
    }
}

public static class CloudBackupObjectKey
{
    public static string CreateInstallationPrefix(string keyPrefix, Guid installationId)
    {
        CloudBackupConfigurationStore.ValidatePrefix(keyPrefix);
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("La identidad de instalación cloud no puede estar vacía.", nameof(installationId));
        }

        return $"{keyPrefix}/{installationId:D}";
    }

    public static string Create(
        string keyPrefix,
        Guid installationId,
        Guid backupId)
    {
        if (backupId == Guid.Empty)
        {
            throw new ArgumentException("El BackupId cloud no puede estar vacío.", nameof(backupId));
        }

        return $"{CreateInstallationPrefix(keyPrefix, installationId)}/{backupId:D}.aibak";
    }

    public static bool TryParse(
        string key,
        string keyPrefix,
        out Guid installationId,
        out Guid backupId)
    {
        installationId = Guid.Empty;
        backupId = Guid.Empty;
        if (!IsWithinPrefix(key, keyPrefix) ||
            !key.EndsWith(".aibak", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = key[(keyPrefix.Length + 1)..];
        var segments = relative.Split('/', StringSplitOptions.None);
        if (segments.Length != 2 ||
            !Guid.TryParseExact(segments[0], "D", out installationId) ||
            !Guid.TryParseExact(
                segments[1][..(segments[1].Length - ".aibak".Length)],
                "D",
                out backupId) ||
            backupId == Guid.Empty ||
            installationId == Guid.Empty)
        {
            installationId = Guid.Empty;
            backupId = Guid.Empty;
            return false;
        }

        return true;
    }

    public static bool IsWithinPrefix(string key, string keyPrefix)
    {
        try
        {
            CloudBackupConfigurationStore.ValidatePrefix(keyPrefix);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(key) &&
               key.StartsWith(keyPrefix + "/", StringComparison.Ordinal) &&
               !key.Contains("..", StringComparison.Ordinal) &&
               !key.Contains('\\') &&
               !key.Contains(':') &&
               !key.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..");
    }
}
