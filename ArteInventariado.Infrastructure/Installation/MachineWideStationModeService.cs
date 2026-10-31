using System.Diagnostics;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Crosses the Windows elevation boundary only for the protected station
/// identity/mode operation. The normal WinForms process never writes the
/// machine-wide core file itself.
/// </summary>
public sealed class MachineWideStationModeService
{
    private readonly ArteInventariadoPathProvider _paths;
    private readonly Func<string, IReadOnlyList<string>, MachineWideElevationResult> _elevate;
    private readonly bool _allowSyntheticRoots;

    public MachineWideStationModeService(ArteInventariadoPathProvider paths)
        : this(paths, (executable, arguments) => MachineWideElevation.EnsureElevated(executable, arguments))
    {
    }

    internal MachineWideStationModeService(
        ArteInventariadoPathProvider paths,
        Func<string, IReadOnlyList<string>, MachineWideElevationResult> elevate,
        bool allowSyntheticRoots = false)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _elevate = elevate ?? throw new ArgumentNullException(nameof(elevate));
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public async Task ChangeModeAsync(
        InstallationMode mode,
        Guid? expectedInstallationId,
        InstallationMode? expectedCurrentMode,
        CancellationToken cancellationToken = default)
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException("El helper protegido solo aplica al layout machine-wide.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "El modo de instalacion no es valido.");
        }

        if (!expectedInstallationId.HasValue || expectedInstallationId.Value == Guid.Empty)
        {
            throw new InvalidDataException("La operacion protegida requiere la identidad actual de la estacion.");
        }

        if (!File.Exists(_paths.StationHelperPath))
        {
            throw new FileNotFoundException(
                "La instalacion machine-wide no contiene el helper protegido de station.",
                _paths.StationHelperPath);
        }

        MachineWideStationConfigurationHelper.ValidateManagedRoots(_paths, _allowSyntheticRoots);
        var arguments = new List<string>
        {
            MachineWideStationConfigurationHelper.WriteModeCommand,
            "--mode",
            mode.ToString(),
            "--machine-install-root",
            _paths.MachineInstallRoot,
            "--machine-data-root",
            _paths.MachineDataRoot,
            "--expected-installation-id",
            expectedInstallationId.Value.ToString("D")
        };
        if (expectedCurrentMode.HasValue)
        {
            arguments.Add("--expected-current-mode");
            arguments.Add(expectedCurrentMode.Value.ToString());
        }
        else
        {
            arguments.Add("--expected-current-mode");
            arguments.Add("UNCONFIGURED");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var elevation = _elevate(_paths.StationHelperPath, arguments);
        if (elevation.Outcome == MachineWideElevationOutcome.Cancelled)
        {
            throw new OperationCanceledException(
                "Windows cancelo la autorizacion para cambiar el modo de la estacion.",
                cancellationToken);
        }

        if (elevation.Outcome == MachineWideElevationOutcome.AlreadyElevated)
        {
            throw new InvalidOperationException(
                "El helper protegido no debe ejecutarse dentro del proceso de la aplicacion.");
        }

        var process = elevation.Process
            ?? throw new InvalidOperationException("Windows no devolvio el proceso helper elevado.");
        var exitCode = -1;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            exitCode = process.ExitCode;
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }

            throw;
        }
        finally
        {
            process.Dispose();
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"El helper protegido no pudo cambiar el modo de la estacion (codigo {exitCode}).");
        }
    }
}
