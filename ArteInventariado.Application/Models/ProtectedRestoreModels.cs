using System.Text.Json.Serialization;

namespace ArteInventariado.Application.Models;

public enum ProtectedRestoreCandidateSource
{
    LocalProtectedBackup = 1,
    CloudRecoveryCandidate = 2,
    LocalStagedBackup = 3
}

public enum ProtectedRestoreOutcome
{
    Succeeded = 1,
    RelaunchedElevated = 2,
    Cancelled = 3,
    Rejected = 4,
    Failed = 5,
    RecoveryRequired = 6
}

public enum ProtectedRestoreJournalPhase
{
    Prepared = 1,
    PromotingAssets = 2,
    PromotingDatabase = 3,
    Validating = 4,
    Succeeded = 5,
    RolledBack = 6,
    RecoveryRequired = 7
}

public sealed record ProtectedRestoreRequest(
    string CandidatePath,
    ProtectedRestoreCandidateSource Source,
    Guid? ExpectedBackupId = null,
    Guid? ExpectedSourceInstallationId = null,
    string? ExpectedPackageSha256 = null,
    BackupContentStatus? ExpectedContentStatus = null,
    bool ConfirmationAccepted = false);

public sealed record ProtectedRestoreResult(
    Guid OperationId,
    ProtectedRestoreOutcome Outcome,
    string Message,
    BackupManifest? Manifest = null,
    string? PreRestorePath = null,
    bool RestartRequired = false,
    bool RolledBack = false,
    IReadOnlyList<string>? Warnings = null);

/// <summary>
/// Result of importing a user-selected local .aibak into the protected machine
/// recovery area. This operation only copies a package; it never replaces
/// the live database.
/// </summary>
public sealed record ProtectedRestoreCandidateStageResult(
    ProtectedRestoreOutcome Outcome,
    ProtectedRestoreCandidateSource? Source,
    string? CandidatePath,
    string? PackageSha256,
    long? SizeBytes,
    string Message);

public sealed record ProtectedRestoreRecoveryStatus(
    bool Pending,
    Guid? OperationId,
    ProtectedRestoreJournalPhase? Phase,
    string? Problem);

/// <summary>
/// The journal is persisted only under the protected recovery root.  It does
/// not contain credentials or commercial data; paths are accepted only after
/// the engine has restricted them to the machine-wide roots.
/// </summary>
public sealed record ProtectedRestoreJournalDocument(
    int SchemaVersion,
    Guid OperationId,
    DateTimeOffset StartedUtc,
    ProtectedRestoreJournalPhase Phase,
    ProtectedRestoreCandidateSource Source,
    string CandidatePath,
    string CandidateSha256,
    Guid CandidateBackupId,
    Guid CandidateSourceInstallationId,
    BackupContentStatus CandidateContentStatus,
    string PreRestorePath,
    string StagedDatabasePath,
    string StagedAssetsRoot,
    Guid DestinationInstallationId,
    InstallationMode DestinationMode,
    string StationCoreSha256,
    string? CurrentPointerSha256,
    string? UpdateStateSha256,
    IReadOnlyList<string> CreatedAssetPaths,
    string? LastError = null);

public sealed record ProtectedRestoreHelperResult(
    Guid OperationId,
    ProtectedRestoreOutcome Outcome,
    string Message,
    bool RolledBack,
    string? PreRestorePath,
    IReadOnlyList<string>? Warnings = null);
