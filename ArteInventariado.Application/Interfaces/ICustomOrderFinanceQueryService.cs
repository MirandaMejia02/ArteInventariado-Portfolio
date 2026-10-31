using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICustomOrderFinanceQueryService
{
    Task<CustomOrderFinanceSummary?> GetSummaryAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomOrderFinancialHistoryRow>> GetHistoryAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomOrderReservationResult>> GetReservationsAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<CustomOrderPaymentDocumentData?> GetPaymentDocumentAsync(int pagoPedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<CustomOrderPaymentDocumentData?> GetRefundDocumentAsync(int reembolsoPedidoPersonalizadoId, CancellationToken cancellationToken = default);
}
