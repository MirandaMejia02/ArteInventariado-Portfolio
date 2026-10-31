using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IInstallationManagementService
{
    Task<InstallationSetupProfile> GetSetupProfileAsync(
        CancellationToken cancellationToken = default);

    Task<InstallationConfigurationResult> CompleteSetupAsync(
        CompleteInstallationSetupRequest request,
        CancellationToken cancellationToken = default);

    Task<InstallationConfigurationResult> ChangeModeAsync(
        ChangeInstallationModeRequest request,
        CancellationToken cancellationToken = default);
}
