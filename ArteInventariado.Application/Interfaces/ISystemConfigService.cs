using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ISystemConfigService
{
    Task<IReadOnlyList<SystemConfigSetting>> GetBasicSettingsAsync(CancellationToken cancellationToken = default);

    Task<bool> IsBitcoinEnabledAsync(CancellationToken cancellationToken = default);

    Task UpdateBasicSettingsAsync(UpdateSystemConfigRequest request, CancellationToken cancellationToken = default);
}
