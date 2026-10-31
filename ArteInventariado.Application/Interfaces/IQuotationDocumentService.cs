namespace ArteInventariado.Application.Interfaces;

public interface IQuotationDocumentService
{
    Task<string> GeneratePdfAsync(int cotizacionId, CancellationToken cancellationToken = default);
}
