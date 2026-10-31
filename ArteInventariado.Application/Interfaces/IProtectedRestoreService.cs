using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Protected, machine-wide restore boundary.  The implementation performs
/// authorization in the application layer and hands the actual write to the
/// narrowly-scoped elevated helper.  It intentionally exposes no arbitrary
/// database, archive or destination operation.
/// </summary>
public interface IProtectedRestoreService
{
    bool IsMachineWide { get; }

    /// <summary>
    /// Imports a user-selected local .aibak into the fixed protected recovery
    /// root. The elevated helper can only copy to that root; this does not
    /// authorize or perform a restore.
    /// </summary>
    Task<ProtectedRestoreCandidateStageResult> StageLocalCandidateAsync(
        string sourcePath,
        string expectedPackageSha256,
        CancellationToken cancellationToken = default);

    Task<ProtectedRestoreResult> RestoreAsync(
        ProtectedRestoreRequest request,
        string reauthenticationPassword,
        CancellationToken cancellationToken = default);

    Task<ProtectedRestoreRecoveryStatus> GetRecoveryStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the narrow elevated recovery helper when a previous restore
    /// left an unfinished journal. The normal application never performs the
    /// rollback in-process.
    /// </summary>
    Task<ProtectedRestoreResult?> RecoverPendingAsync(
        CancellationToken cancellationToken = default);
}
