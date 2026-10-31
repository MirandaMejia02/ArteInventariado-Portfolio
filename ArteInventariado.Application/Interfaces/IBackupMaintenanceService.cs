using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IBackupMaintenanceService
{
    Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<BackupSettings> UpdateSettingsAsync(
        UpdateBackupSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<BackupResult> CreateBackupAsync(
        CreateBackupRequest request,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BackupResult> CreatePreUpdateBackupAsync(
        string targetVersion,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BackupResult?> RunAutomaticBackupIfDueAsync(
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BackupValidationResult> ValidateBackupAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupHistoryItem>> GetHistoryAsync(
        CancellationToken cancellationToken = default);

    Task<PrepareRestoreResult> PrepareRestoreAsync(
        PrepareRestoreRequest request,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IPendingRestoreProcessor
{
    PendingRestoreStatus GetStatus();

    RestoreStartupResult ProcessPendingRestore();
}
