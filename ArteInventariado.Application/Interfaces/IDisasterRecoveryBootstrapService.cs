using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Fresh-machine recovery orchestration.  It composes the existing G2
/// read-only candidate service and H1 protected restore engine; it does not
/// expose a second restore implementation.
/// </summary>
public interface IDisasterRecoveryBootstrapService
{
    InstallationStateAssessment Classify();

    Task<CloudRecoveryCatalogResult> GetCatalogAsync(
        DisasterRecoveryCloudRequest request,
        CancellationToken cancellationToken = default);

    Task<DisasterRecoveryResult> RecoverAsync(
        DisasterRecoveryRequest request,
        CancellationToken cancellationToken = default);

    Task ReconcilePendingAsync(CancellationToken cancellationToken = default);
}
