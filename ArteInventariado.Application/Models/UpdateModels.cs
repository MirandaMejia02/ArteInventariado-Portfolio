namespace ArteInventariado.Application.Models;

public enum UpdateState
{
    Idle = 1,
    Preparing = 2,
    BackupCreated = 3,
    ReadyToInstall = 4,
    Installing = 5,
    Verifying = 6,
    Success = 7,
    Failed = 8,
    RollbackRequired = 9,
    RollingBack = 10
}

/// <summary>
/// Describes the user-visible phase of an update transaction.  It is kept
/// separate from <see cref="UpdateState"/> because non-elevated machine-wide
/// staging intentionally has no canonical PRE_UPDATE state yet.
/// </summary>
public enum UpdatePreparationStage
{
    None = 0,
    PackageValidated = 1,
    Staged = 2,
    PreparationNotStarted = 3,
    PreUpdateCreated = 4,
    ReadyToInstall = 5,
    Rollback = 6
}

public sealed record UpdateManifest(
    string Version,
    string MinimumCompatibleVersion,
    string? MaximumCompatibleVersion,
    string PackagePath,
    string Sha256,
    long SizeBytes,
    string ReleaseNotes,
    DateTimeOffset? PublishedUtc = null,
    string? DeploymentTarget = null);

public static class UpdatePackageTargets
{
    public const string MachineWide = "MachineWide";
    public const string VisualDev = "VisualDev";
}

public sealed record UpdatePackageValidationResult(
    bool IsValid,
    UpdateManifest? Manifest,
    string PackagePath,
    IReadOnlyList<string> Errors,
    long? ExpandedSizeBytes = null);

public sealed record UpdateStateDocument(
    int SchemaVersion,
    UpdateState State,
    string SourceVersion,
    string TargetVersion,
    string PackagePath,
    string ManifestPath,
    string? PreUpdateBackupPath,
    string? PreviousVersionPath,
    string? CurrentVersionPath,
    string? LastError,
    DateTimeOffset UpdatedUtc,
    string? SourceApplicationPath = null,
    Guid? InstallationId = null,
    InstallationMode? StationMode = null,
    string? StationSettingsSha256 = null,
    bool DatabaseMayHaveChanged = false,
    string? StationSettingsBackupPath = null,
    Guid? TransactionId = null,
    UpdatePreparationStage PreparationStage = UpdatePreparationStage.None);

public sealed record UpdateStatus(
    UpdateState State,
    string InstalledVersion,
    string? AvailableVersion,
    string? ReleaseNotes,
    long? PackageSizeBytes,
    string? LastCheckedUtc,
    string? LastError,
    UpdatePreparationStage PreparationStage = UpdatePreparationStage.None,
    string? SourceVersion = null,
    Guid? TransactionId = null,
    string? StagingPath = null,
    bool CanInstall = false);

public sealed record UpdatePrepareResult(
    bool Succeeded,
    UpdateState State,
    string Message,
    string? PreUpdateBackupPath,
    UpdatePackageValidationResult Validation,
    string? StagingPath = null,
    UpdatePreparationStage PreparationStage = UpdatePreparationStage.None,
    string? SourceVersion = null,
    string? TargetVersion = null,
    Guid? TransactionId = null);

public static class UpdateStateSemantics
{
    public static UpdatePreparationStage PreparationStage(UpdateState state) => state switch
    {
        UpdateState.Preparing => UpdatePreparationStage.PreparationNotStarted,
        UpdateState.BackupCreated => UpdatePreparationStage.PreUpdateCreated,
        UpdateState.ReadyToInstall => UpdatePreparationStage.ReadyToInstall,
        UpdateState.RollbackRequired or UpdateState.RollingBack => UpdatePreparationStage.Rollback,
        _ => UpdatePreparationStage.None
    };

    public static UpdatePreparationStage PreparationStage(UpdateStateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.State == UpdateState.Preparing &&
            document.PreparationStage == UpdatePreparationStage.Staged)
        {
            return UpdatePreparationStage.Staged;
        }

        return document.State == UpdateState.Failed && IsRollback(document)
            ? UpdatePreparationStage.Rollback
            : PreparationStage(document.State);
    }

    public static bool IsRollback(UpdateStateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var error = document.LastError ?? string.Empty;
        return document.State is UpdateState.RollbackRequired or UpdateState.RollingBack ||
               (document.State == UpdateState.Failed &&
                (error.Contains("rollback", StringComparison.OrdinalIgnoreCase) ||
                 error.Contains("revert", StringComparison.OrdinalIgnoreCase)));
    }
}
