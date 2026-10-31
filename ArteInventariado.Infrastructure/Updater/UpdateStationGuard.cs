using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateStationGuard
{
    public async Task<(string BackupPath, string Sha256)> CaptureAsync(
        string stationSettingsPath,
        string updateRoot,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(stationSettingsPath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("No existe station-settings.json para proteger la identidad local.", source);
        }

        var directory = Path.Combine(Path.GetFullPath(updateRoot), "StationSnapshots");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"station-{UpdateVersion.Sanitize(targetVersion)}-{Guid.NewGuid():N}.json");
        await CopyFileAsync(source, destination, cancellationToken);
        var sourceHash = await UpdatePackageValidator.ComputeSha256Async(source, cancellationToken);
        var copyHash = await UpdatePackageValidator.ComputeSha256Async(destination, cancellationToken);
        if (!sourceHash.Equals(copyHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidDataException("La copia protectora de station-settings.json no coincide con el original.");
        }

        return (destination, sourceHash);
    }

    public async Task EnsurePreservedAsync(
        UpdateStateDocument state,
        string stationSettingsPath,
        CancellationToken cancellationToken = default)
    {
        if (!state.InstallationId.HasValue || !state.StationMode.HasValue ||
            string.IsNullOrWhiteSpace(state.StationSettingsSha256))
        {
            throw new InvalidDataException("El estado no contiene la identidad local protegida.");
        }

        var path = Path.GetFullPath(stationSettingsPath);
        if (!File.Exists(path))
        {
            throw new InvalidDataException("station-settings.json desaparecio durante la actualizacion.");
        }

        var station = new Station.LocalStationConfigurationStore(path).Load().Status;
        if (!station.IsConfigured || station.InstallationId != state.InstallationId || station.Mode != state.StationMode)
        {
            throw new InvalidDataException("La identidad o el modo de la estacion cambio durante la actualizacion.");
        }

        var hash = await UpdatePackageValidator.ComputeSha256Async(path, cancellationToken);
        if (!hash.Equals(state.StationSettingsSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("station-settings.json fue modificado durante la actualizacion.");
        }
    }

    public async Task RestoreAsync(
        UpdateStateDocument state,
        string stationSettingsPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state.StationSettingsBackupPath) ||
            !File.Exists(state.StationSettingsBackupPath))
        {
            throw new FileNotFoundException(
                "No existe la copia protectora de station-settings.json.",
                state.StationSettingsBackupPath);
        }

        var expected = state.StationSettingsSha256
            ?? throw new InvalidDataException("El estado no contiene el SHA-256 de station-settings.json.");
        var backupHash = await UpdatePackageValidator.ComputeSha256Async(state.StationSettingsBackupPath, cancellationToken);
        if (!backupHash.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La copia protectora de station-settings.json no supera SHA-256.");
        }

        var target = Path.GetFullPath(stationSettingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + $".{Guid.NewGuid():N}.restore";
        try
        {
            await CopyFileAsync(state.StationSettingsBackupPath, temporary, cancellationToken);
            if (File.Exists(target))
            {
                File.Replace(temporary, target, null);
            }
            else
            {
                File.Move(temporary, target);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        await EnsurePreservedAsync(state, target, cancellationToken);
    }

    public void DeleteSnapshot(UpdateStateDocument state)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(state.StationSettingsBackupPath) &&
                File.Exists(state.StationSettingsBackupPath))
            {
                File.Delete(state.StationSettingsBackupPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}
