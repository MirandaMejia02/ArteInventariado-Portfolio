using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Station;

public sealed class InstallationContext : IInstallationContext
{
    public const string WriteDeniedMessage =
        "Esta instalacion esta configurada en modo Consulta. Las operaciones de escritura estan deshabilitadas.";

    private readonly object _sync = new();
    private readonly LocalStationConfigurationStore _store;
    private InstallationStatus _status;

    internal InstallationContext(LocalStationConfigurationStore store)
    {
        _store = store;
        _status = store.Load().Status;
    }

    public InstallationStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public bool AllowsOperationalWrites => Status.AllowsOperationalWrites;

    public void EnsureInitialized()
    {
        lock (_sync)
        {
            if (_status.State == StationConfigurationState.RequiresSetup &&
                !File.Exists(_status.ConfigurationPath))
            {
                _status = _store.EnsureInitialized().Status;
            }
        }
    }

    public void EnsureOperationalWriteAllowed(string operationName)
    {
        if (AllowsOperationalWrites || InstallationWriteAuthorizationScope.IsAuthorized)
        {
            return;
        }

        var message = Status.State == StationConfigurationState.Ready
            ? WriteDeniedMessage
            : "Esta estacion debe completar o recuperar su configuracion local antes de registrar operaciones.";
        throw new InstallationWriteDeniedException(message, operationName);
    }
}

public sealed class InstallationWriteDeniedException : InvalidOperationException
{
    public InstallationWriteDeniedException(string message, string operationName)
        : base(message)
    {
        OperationName = operationName;
    }

    public string OperationName { get; }
}
