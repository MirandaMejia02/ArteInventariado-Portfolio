namespace ArteInventariado.Application.Models;

public sealed record SystemConfigSetting(
    string Clave,
    string Valor,
    string? Descripcion);

public sealed record UpdateSystemConfigRequest(
    IReadOnlyList<SystemConfigSetting> Settings);

public sealed record ChangeCurrentPasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public sealed record LocalReportSettings(
    string ReportsDirectory,
    string ConfigurationPath);

public sealed record UpdateLocalReportSettingsRequest(
    string ReportsDirectory);

public sealed record LocalPosSettings(
    bool QuickPosEnabled,
    string ConfigurationPath);

public sealed record UpdateLocalPosSettingsRequest(
    bool QuickPosEnabled);
