using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Starts a manual backup through the protected machine-wide runner. The
/// desktop process never writes into the protected backup directory.
/// </summary>
public interface IProtectedBackupService
{
    bool IsMachineWide { get; }

    Task<ProtectedBackupResult> CreateManualBackupAsync(
        CancellationToken cancellationToken = default);
}
