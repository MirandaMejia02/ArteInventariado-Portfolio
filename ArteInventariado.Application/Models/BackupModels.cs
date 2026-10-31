using System.Text.Json.Serialization;

namespace ArteInventariado.Application.Models;

public enum BackupKind
{
    Manual = 1,
    Automatic = 2,
    PreRestore = 3,
    PreUpdate = 4
}

public enum BackupFrequency
{
    Daily = 1,
    Weekly = 2,
    EveryFifteenDays = 3,
    Monthly = 4
}

/// <summary>
/// Describes whether a backup contains every referenced asset or whether
/// one or more untrusted/missing assets had to be omitted.  The zero value is
/// intentionally Healthy so manifests written before this field existed
/// remain compatible when they are read back.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupContentStatus
{
    Healthy = 0,
    DegradedAssets = 1
}

/// <summary>
/// Non-sensitive category explaining why an asset was not copied.  The
/// category is safe to persist in a manifest; raw database paths are not.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupAssetIssueKind
{
    Missing = 1,
    OutsideCanonicalRoot = 2,
    ReparsePoint = 3,
    Traversal = 4,
    InvalidPath = 5,
    Changed = 6
}

public sealed record BackupSettings(
    bool AutomaticEnabled,
    string DestinationDirectory,
    BackupFrequency Frequency,
    int AutomaticRetentionCount,
    DateTimeOffset? LastAutomaticBackupUtc,
    string? LastAutomaticError,
    DateTimeOffset? NextAutomaticBackupUtc);

public sealed record UpdateBackupSettingsRequest(
    bool AutomaticEnabled,
    string DestinationDirectory,
    BackupFrequency Frequency,
    int AutomaticRetentionCount);

public sealed record BackupAssetManifest(
    int ProductId,
    string PackagePath,
    string OriginalFileName,
    long SizeBytes,
    string Sha256,
    bool WasMissing,
    ProductAssetOwnerKind? OwnerKind = null,
    int? AssetId = null,
    int? OwnerId = null,
    string? ManagedRelativePath = null,
    BackupAssetIssueKind? IssueKind = null);

public sealed record BackupManifest(
    int BackupFormatVersion,
    Guid BackupId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset CreatedLocal,
    string ApplicationVersion,
    string DatabaseProvider,
    IReadOnlyList<string> AppliedMigrations,
    string? LastMigration,
    string DatabaseSha256,
    long DatabaseSizeBytes,
    Guid SourceInstallationId,
    InstallationMode SourceInstallationMode,
    BackupKind Kind,
    IReadOnlyList<BackupAssetManifest> Assets,
    BackupContentStatus ContentStatus = BackupContentStatus.Healthy,
    int? OmittedAssetCount = null);

public sealed record BackupProgress(string Stage, int Percent, string Message);

public sealed record CreateBackupRequest(
    BackupKind Kind,
    string? DestinationDirectory = null,
    string? TargetVersion = null);

public sealed record BackupResult(
    Guid BackupId,
    string FilePath,
    BackupManifest Manifest,
    IReadOnlyList<string> Warnings);

public enum ProtectedBackupOutcome
{
    Succeeded = 1,
    Cancelled = 2,
    Failed = 3,
    Rejected = 4
}

public sealed record ProtectedBackupResult(
    ProtectedBackupOutcome Outcome,
    Guid OperationId,
    string Message,
    string? FilePath = null,
    string? Sha256 = null,
    long? SizeBytes = null);

public sealed record BackupValidationResult(
    bool IsValid,
    string FilePath,
    BackupManifest? Manifest,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record BackupHistoryItem(
    string FilePath,
    DateTimeOffset? CreatedUtc,
    BackupKind? Kind,
    long SizeBytes,
    string? SourceInstallationMode,
    bool IsValid,
    string Status);

public sealed record PrepareRestoreRequest(
    string BackupFilePath,
    string Password,
    bool ConfirmationAccepted);

public sealed record PrepareRestoreResult(
    Guid RestoreId,
    BackupManifest Manifest,
    string PreventiveBackupPath,
    bool RestartRequired,
    bool RequiresConsultaToPrincipalWarning,
    string Message);

public sealed record PendingRestoreStatus(
    bool Exists,
    Guid? RestoreId,
    DateTimeOffset? RequestedUtc,
    string? Problem);

public sealed record RestoreStartupResult(
    bool HadPendingRestore,
    bool Succeeded,
    bool RolledBack,
    string Message);
