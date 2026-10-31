using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IStationSettingsService
{
    Task<LocalReportSettings> GetReportSettingsAsync(CancellationToken cancellationToken = default);

    Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken = default);

    Task<LocalPosSettings> GetPosSettingsAsync(CancellationToken cancellationToken = default);

    Task<LocalPeripheralSettings> GetPeripheralSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LocalPeripheralSettings(false, null, false, null, false, null, string.Empty));

    Task UpdateReportSettingsAsync(
        UpdateLocalReportSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task UpdatePosSettingsAsync(
        UpdateLocalPosSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task UpdatePeripheralSettingsAsync(
        UpdateLocalPeripheralSettingsRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La configuracion de perifericos no esta disponible en esta implementacion.");
}
