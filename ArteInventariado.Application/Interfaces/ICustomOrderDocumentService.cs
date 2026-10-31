namespace ArteInventariado.Application.Interfaces;

public interface ICustomOrderDocumentService
{
    Task<string> GeneratePdfAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
}
