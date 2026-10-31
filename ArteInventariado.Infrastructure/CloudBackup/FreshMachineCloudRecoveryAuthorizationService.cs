using ArteInventariado.Application.Interfaces;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Authorization used only by the explicit, UAC-elevated H2 process.  A
/// normal installation never registers this implementation.  It proves the
/// machine is still classified as FreshMachine and that Windows elevation was
/// consented before G2 is allowed to contact S3.
/// </summary>
public sealed class FreshMachineCloudRecoveryAuthorizationService : ICloudRecoveryAuthorizationService
{
    private readonly IInstallationStateClassifier _classifier;
    private readonly Func<bool> _isElevated;

    public FreshMachineCloudRecoveryAuthorizationService(
        IInstallationStateClassifier classifier)
        : this(classifier, MachineWideElevation.IsAdministrator)
    {
    }

    internal FreshMachineCloudRecoveryAuthorizationService(
        IInstallationStateClassifier classifier,
        Func<bool> isElevated)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
    }

    public Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isElevated())
        {
            throw new UnauthorizedAccessException(
                "La recuperación de una máquina nueva requiere consentimiento UAC elevado.");
        }

        var assessment = _classifier.Classify();
        if (assessment.State != Application.Models.InstallationLifecycleState.FreshMachine)
        {
            throw new UnauthorizedAccessException(
                "El bypass de disaster recovery solo está disponible en una máquina clasificada como FreshMachine.");
        }

        return Task.CompletedTask;
    }
}
