namespace ArteInventariado.Application.Models;

/// <summary>
/// Authoritative state used by the startup bootstrap.  This is deliberately
/// separate from <see cref="StationConfigurationState"/>: a station file by
/// itself does not prove that the local business installation is healthy.
/// </summary>
public enum InstallationLifecycleState
{
    FreshMachine = 1,
    ExistingValidInstallation = 2,
    ExistingDamagedInstallation = 3,
    MigrationCandidate = 4,
    RecoveryPending = 5,
    PrincipalProvisioningRequired = 6,
    /// <summary>
    /// The database and protected principal are valid, but the local station
    /// setup was interrupted and must be resumed by that principal.
    /// </summary>
    SetupPending = 7
}

public enum DisasterRecoveryChoice
{
    NewInstallation = 1,
    RecoverExisting = 2,
    Cancelled = 3
}

public enum DisasterRecoveryPhase
{
    CandidateSelected = 1,
    CandidateDownloaded = 2,
    DestinationPrepared = 3,
    RestoreInvoked = 4,
    Completed = 5,
    Cancelled = 6
}

public enum DisasterRecoveryOutcome
{
    Succeeded = 1,
    Cancelled = 2,
    Rejected = 3,
    Failed = 4,
    RecoveryPending = 5
}

/// <summary>
/// A read-only snapshot of all evidence considered by the H2 classifier.
/// It contains no database rows or credentials.
/// </summary>
public sealed record InstallationStateAssessment(
    InstallationLifecycleState State,
    InstallationStatus Station,
    bool StationFileExists,
    bool DatabaseFileExists,
    bool DatabaseValid,
    bool CurrentPointerValid,
    bool UpdateStatePresent,
    bool BootstrapMarkerPresent,
    bool LegacyInstallationDetected,
    bool RecoveryJournalPresent,
    string? Problem);

/// <summary>
/// Non-secret journal for an explicit disaster-recovery attempt.  Paths are
/// written only below the protected Recovery root and are revalidated before
/// use.  No cloud credential or password is persisted here.
/// </summary>
public sealed record DisasterRecoveryJournalDocument(
    int SchemaVersion,
    Guid OperationId,
    DateTimeOffset StartedUtc,
    DisasterRecoveryPhase Phase,
    Guid SourceInstallationId,
    Guid DestinationInstallationId,
    InstallationMode DestinationMode,
    Guid? BackupId,
    string? CandidatePath,
    string? CandidateSha256,
    string? OriginalStationBackupPath,
    bool OriginalStationFileExisted,
    bool DestinationDatabaseCreated,
    string? LastError = null);

public sealed record DisasterRecoveryCloudRequest(
    CloudBackupConfiguration Configuration,
    CloudRecoveryAuthenticationRequest Authentication,
    Guid SourceInstallationId,
    bool OwnerConfirmed = true);

public sealed record DisasterRecoveryRequest(
    DisasterRecoveryCloudRequest Cloud,
    CloudRecoveryCatalogItem Selection,
    InstallationMode DestinationMode,
    bool ConfirmationAccepted = false);

public sealed record DisasterRecoveryResult(
    DisasterRecoveryOutcome Outcome,
    string Message,
    Guid? SourceInstallationId = null,
    Guid? DestinationInstallationId = null,
    Guid? BackupId = null,
    CloudRecoveryCandidate? Candidate = null,
    ProtectedRestoreResult? Restore = null,
    IReadOnlyList<string>? Warnings = null);
