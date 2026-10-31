using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IUpdateService
{
    Task<UpdateStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<UpdatePackageValidationResult> ValidatePackageAsync(
        string manifestPath,
        string packagePath,
        CancellationToken cancellationToken = default);

    Task<UpdatePrepareResult> PrepareAsync(
        string manifestPath,
        string packagePath,
        CancellationToken cancellationToken = default);

    string GetUpdaterExecutablePath();
}
