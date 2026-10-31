using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IReceiptDocumentService
{
    Task<ReceiptData> BuildCurrentAsync(SaleResult sale, string cashier, CancellationToken cancellationToken = default);
    Task<ReceiptData?> GetHistoricalAsync(int saleId, CancellationToken cancellationToken = default);
    Task RegisterReprintAsync(int saleId, CancellationToken cancellationToken = default);
}
