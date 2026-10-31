using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;
using System.Text.RegularExpressions;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Exchanges owner-provided bootstrap credentials plus an MFA code for a
/// short-lived recovery session.  Nothing is written to disk and the STS
/// client is disposed immediately after the exchange.
/// </summary>
public sealed class AwsStsCloudRecoveryAuthenticator : ICloudRecoveryAuthenticator
{
    private static readonly Regex MfaCodePattern = new("^[0-9]{6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<CloudRecoveryTemporaryCredentials> AuthenticateAsync(
        CloudRecoveryAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var credentials = new BasicAWSCredentials(
            request.BootstrapAccessKeyId,
            request.BootstrapSecretAccessKey);
        var config = new AmazonSecurityTokenServiceConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(request.RegionSystemName)
        };

        using var client = new AmazonSecurityTokenServiceClient(credentials, config);
        var response = await client.AssumeRoleAsync(
            new AssumeRoleRequest
            {
                RoleArn = request.RoleArn,
                RoleSessionName = request.SessionName ?? $"ArteInventariadoRecovery-{Guid.NewGuid():N}",
                SerialNumber = request.MfaSerialNumber,
                TokenCode = request.MfaTokenCode,
                ExternalId = request.ExternalId,
                DurationSeconds = request.DurationSeconds
            },
            cancellationToken);

        var temporary = response.Credentials;
        if (temporary is null ||
            string.IsNullOrWhiteSpace(temporary.AccessKeyId) ||
            string.IsNullOrWhiteSpace(temporary.SecretAccessKey) ||
            string.IsNullOrWhiteSpace(temporary.SessionToken) ||
            temporary.Expiration == default)
        {
            throw new InvalidDataException("STS no devolvió credenciales temporales completas.");
        }

        return new CloudRecoveryTemporaryCredentials(
            temporary.AccessKeyId,
            temporary.SecretAccessKey,
            temporary.SessionToken,
            new DateTimeOffset(temporary.Expiration.ToUniversalTime(), TimeSpan.Zero));
    }

    internal static void Validate(CloudRecoveryAuthenticationRequest? request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.BootstrapAccessKeyId) ||
            string.IsNullOrWhiteSpace(request.BootstrapSecretAccessKey) ||
            request.BootstrapAccessKeyId.Length > 256 ||
            request.BootstrapSecretAccessKey.Length > 2048)
        {
            throw new InvalidDataException("La credencial bootstrap de recuperación no es válida.");
        }

        if (string.IsNullOrWhiteSpace(request.RoleArn) ||
            request.RoleArn.Length > 2048 ||
            !request.RoleArn.StartsWith("arn:aws:iam::", StringComparison.Ordinal))
        {
            throw new InvalidDataException("El role ARN de recuperación no es válido.");
        }

        if (string.IsNullOrWhiteSpace(request.MfaSerialNumber) ||
            request.MfaSerialNumber.Length > 2048 ||
            !request.MfaSerialNumber.StartsWith("arn:aws:iam::", StringComparison.Ordinal))
        {
            throw new InvalidDataException("El dispositivo MFA de recuperación no es válido.");
        }

        if (string.IsNullOrWhiteSpace(request.MfaTokenCode) || !MfaCodePattern.IsMatch(request.MfaTokenCode))
        {
            throw new InvalidDataException("El código MFA debe contener seis dígitos.");
        }

        if (string.IsNullOrWhiteSpace(request.RegionSystemName) ||
            request.RegionSystemName.Length > 64 ||
            request.RegionSystemName.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException("La región AWS de recuperación no es válida.");
        }

        if (request.ExternalId is { Length: > 1024 } ||
            request.SessionName is { Length: > 128 } ||
            request.DurationSeconds is < 900 or > 3600)
        {
            throw new InvalidDataException("Los parámetros de sesión STS no son válidos.");
        }

        if (request.SessionName is not null &&
            (request.SessionName.Length == 0 || request.SessionName.Any(char.IsWhiteSpace)))
        {
            throw new InvalidDataException("El nombre de sesión STS no es válido.");
        }
    }
}

/// <summary>
/// Small explicit UAC boundary for callers (WinForms/H) that need to relaunch
/// a recovery operation elevated.  The recovery service itself refuses to
/// touch protected staging unless it is already running with an elevated
/// token, so a cancelled prompt cannot change installation state.
/// </summary>
public sealed class CloudRecoveryUacCoordinator
{
    private readonly Func<
        string,
        IReadOnlyList<string>,
        IReadOnlyDictionary<string, string>?,
        MachineWideElevationResult> _ensureElevated;

    public CloudRecoveryUacCoordinator()
        : this(MachineWideElevation.EnsureElevated)
    {
    }

    internal CloudRecoveryUacCoordinator(
        Func<
            string,
            IReadOnlyList<string>,
            IReadOnlyDictionary<string, string>?,
            MachineWideElevationResult> ensureElevated)
    {
        _ensureElevated = ensureElevated ?? throw new ArgumentNullException(nameof(ensureElevated));
    }

    public MachineWideElevationResult EnsureElevated(
        string executablePath,
        IReadOnlyList<string> continuationArguments,
        IReadOnlyDictionary<string, string>? environment = null) =>
        _ensureElevated(executablePath, continuationArguments, environment);
}
