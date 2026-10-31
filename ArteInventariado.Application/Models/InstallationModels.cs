namespace ArteInventariado.Application.Models;

public enum InstallationMode
{
    Principal = 1,
    Consulta = 2
}

public enum StationConfigurationState
{
    Ready = 1,
    RequiresSetup = 2,
    Corrupt = 3,
    Unsupported = 4,
    Invalid = 5
}

public enum InstallationSetupKind
{
    NewInstallation = 1,
    ExistingInstallationUpgrade = 2,
    ConfigurationRecovery = 3,
    AlreadyConfigured = 4
}

public sealed record InstallationStatus(
    StationConfigurationState State,
    InstallationMode? Mode,
    bool IsFirstRunCompleted,
    Guid? InstallationId,
    int? SchemaVersion,
    string ConfigurationPath,
    string? Problem)
{
    public bool IsConfigured =>
        State == StationConfigurationState.Ready &&
        IsFirstRunCompleted &&
        Mode.HasValue;

    public bool AllowsOperationalWrites =>
        IsConfigured && Mode == InstallationMode.Principal;

    public string DisplayName => Mode switch
    {
        InstallationMode.Principal => "Modo Principal",
        InstallationMode.Consulta => "Modo Consulta",
        _ => "Estacion sin configurar"
    };
}

public sealed record InstallationSetupProfile(
    InstallationSetupKind Kind,
    InstallationStatus Status,
    bool IsProtectedAdministrator,
    string Message);

public sealed record CompleteInstallationSetupRequest(
    InstallationMode Mode,
    string Password,
    bool ConfirmationAccepted);

public sealed record ChangeInstallationModeRequest(
    InstallationMode Mode,
    string Password,
    bool ConfirmationAccepted);

public sealed record InstallationConfigurationResult(
    InstallationMode Mode,
    bool RestartRequired,
    string Message);
