namespace ArteInventariado.Application.Interfaces;

public interface ICustomOrderFinanceDocumentService
{
    Task<string> GeneratePaymentPdfAsync(int pagoPedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<string> GenerateRefundPdfAsync(int reembolsoPedidoPersonalizadoId, CancellationToken cancellationToken = default);
}
