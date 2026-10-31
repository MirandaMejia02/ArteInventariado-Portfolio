using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IInstallationContext
{
    InstallationStatus Status { get; }

    bool AllowsOperationalWrites { get; }

    void EnsureInitialized();

    void EnsureOperationalWriteAllowed(string operationName);
}
