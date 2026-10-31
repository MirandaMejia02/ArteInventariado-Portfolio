using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Provisions the protected application administrator on a brand-new
/// installation. The password is supplied only in memory by the owner flow;
/// implementations must persist only its hash.
/// </summary>
public interface IPrincipalProvisioningService
{
    Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default);

    Task ProvisionAsync(
        PrincipalProvisioningRequest request,
        CancellationToken cancellationToken = default);
}
