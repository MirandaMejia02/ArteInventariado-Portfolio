namespace ArteInventariado.Application.Interfaces;

public interface IReportPathProvider
{
    Task<string> GetRootDirectoryAsync(CancellationToken cancellationToken = default);

    Task<string> GetDirectoryAsync(
        string category,
        CancellationToken cancellationToken = default);
}
