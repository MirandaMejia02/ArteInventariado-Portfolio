using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Coordinates the explicit owner provisioning flow for the machine-wide
/// cloud uploader.  It validates the local target, optionally confirms the
/// AWS identity through STS, and delegates persistence to the existing DPAPI
/// provisioner.  It never uploads a probe object and never stores credentials
/// in a non-DPAPI location.
/// </summary>
public sealed class CloudBackupProvisioningService
{
    private readonly ArteInventariadoPathProvider _paths;
    private readonly CloudBackupProvisioningAuthorizationService? _authorization;
    private readonly Func<
        CloudBackupConfiguration,
        CloudBackupCredential,
        CancellationToken,
        Task<CloudBackupProvisioningIdentity>> _identityProbe;
    private readonly bool _allowSyntheticRoots;

    public CloudBackupProvisioningService(
        ArteInventariadoPathProvider paths,
        CloudBackupProvisioningAuthorizationService authorization)
        : this(paths, ProbeAwsIdentityAsync, allowSyntheticRoots: false, authorization)
    {
    }

    internal CloudBackupProvisioningService(
        ArteInventariadoPathProvider paths,
        Func<
            CloudBackupConfiguration,
            CloudBackupCredential,
            CancellationToken,
            Task<CloudBackupProvisioningIdentity>> identityProbe,
        bool allowSyntheticRoots = false,
        CloudBackupProvisioningAuthorizationService? authorization = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _identityProbe = identityProbe ?? throw new ArgumentNullException(nameof(identityProbe));
        _allowSyntheticRoots = allowSyntheticRoots;
        _authorization = authorization;
    }

    /// <summary>
    /// Validates the machine-wide roots, station identity, target contract and
    /// credential, then performs the optional no-upload STS identity check.
    /// The returned object contains no secret material.
    /// </summary>
    public async Task<CloudBackupProvisioningCheck> ValidateAsync(
        CloudBackupConfiguration configuration,
        CloudBackupCredential credential,
        CloudBackupProvisioningAuthorizationGrant authorizationGrant,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthorizedAsync(authorizationGrant, cancellationToken).ConfigureAwait(false);
        ValidateLocal(configuration, credential);
        var identity = await _identityProbe(configuration, credential, cancellationToken)
            .ConfigureAwait(false);
        return new CloudBackupProvisioningCheck(
            identity,
            TargetValidated: true,
            WritePermissionVerified: false,
            WritePermissionNote:
                "PutObject no se prueba durante el aprovisionamiento: hacerlo crearía un objeto remoto. " +
                "El CloudBackupRunner validará la respuesta real de la primera subida sin afectar el backup local.");
    }

    /// <summary>
    /// Persists the validated configuration and credential through the
    /// existing administrative DPAPI boundary.  This method is deliberately
    /// synchronous so the caller cannot accidentally continue before the
    /// protected files are flushed.
    /// </summary>
    public void Provision(
        CloudBackupConfiguration configuration,
        CloudBackupCredential credential,
        CloudBackupProvisioningAuthorizationGrant authorizationGrant)
    {
        EnsureAuthorizedAsync(authorizationGrant, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ValidateLocal(configuration, credential);
        new CloudBackupProvisioner().Provision(_paths, configuration, credential);
    }

    private Task EnsureAuthorizedAsync(
        CloudBackupProvisioningAuthorizationGrant authorizationGrant,
        CancellationToken cancellationToken)
    {
        if (_authorization is not null)
        {
            return _authorization.EnsureGrantAsync(authorizationGrant, cancellationToken);
        }

        // The synthetic constructor is limited to deterministic unit tests;
        // a production instance always receives the authorization service from
        // DI and cannot use an unbound grant.
        if (!_allowSyntheticRoots)
        {
            throw new UnauthorizedAccessException(
                "El aprovisionamiento cloud no tiene una autorización de administrador válida.");
        }

        ArgumentNullException.ThrowIfNull(authorizationGrant);
        return Task.CompletedTask;
    }

    internal void ValidateLocal(
        CloudBackupConfiguration configuration,
        CloudBackupCredential credential)
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException(
                "El aprovisionamiento cloud requiere el layout machine-wide protegido.");
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);
        var station = new LocalStationConfigurationStore(_paths).Load().Status;
        if (!station.IsConfigured || !station.InstallationId.HasValue || station.InstallationId.Value == Guid.Empty)
        {
            throw new InvalidDataException(
                "La estación debe tener una installationId configurada antes de activar el uploader cloud.");
        }

        CloudBackupConfigurationStore.Validate(configuration);
        DpapiCloudBackupSecretStore.Validate(credential);
        _ = S3UploadPolicyBuilder.Build(configuration.BucketName, configuration.KeyPrefix);
    }

    private static async Task<CloudBackupProvisioningIdentity> ProbeAwsIdentityAsync(
        CloudBackupConfiguration configuration,
        CloudBackupCredential credential,
        CancellationToken cancellationToken)
    {
        // STS GetCallerIdentity is an identity/account check. It does not
        // upload, read, list or delete an S3 object and requires no extra
        // permission on the IAM principal.
        CloudBackupConfigurationStore.Validate(configuration);
        DpapiCloudBackupSecretStore.Validate(credential);

        try
        {
            var credentials = new BasicAWSCredentials(
                credential.AccessKeyId,
                credential.SecretAccessKey);
            var clientConfiguration = new AmazonSecurityTokenServiceConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(configuration.RegionSystemName)
            };
            using var client = new AmazonSecurityTokenServiceClient(credentials, clientConfiguration);
            var response = await client.GetCallerIdentityAsync(
                    new GetCallerIdentityRequest(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response.Account) ||
                string.IsNullOrWhiteSpace(response.Arn) ||
                string.IsNullOrWhiteSpace(response.UserId))
            {
                throw new InvalidDataException("STS no devolvió una identidad AWS completa.");
            }

            return new CloudBackupProvisioningIdentity(
                response.Account,
                response.Arn,
                response.UserId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is AmazonClientException or InvalidDataException)
        {
            // Do not surface the SDK message: it may include request details
            // that should not be copied to logs or screenshots alongside a
            // credential entry.
            throw new InvalidOperationException(
                "No se pudo validar la identidad AWS con la credencial proporcionada.",
                ex);
        }
    }
}
