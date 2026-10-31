using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICustomOrderService
{
    Task<CustomOrderDetail> CreateDraftAsync(CustomOrderSaveRequest request, CancellationToken cancellationToken = default);
    Task<CustomOrderDetail> UpdateDraftAsync(CustomOrderSaveRequest request, CancellationToken cancellationToken = default);
    Task<CustomOrderDetail> ConfirmAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<CustomOrderDetail> ConvertQuotationAsync(int cotizacionId, DateTime fechaPrometida, CancellationToken cancellationToken = default);
    Task<CustomOrderDetail> ChangeStateAsync(CustomOrderStateChangeRequest request, CancellationToken cancellationToken = default);
    Task<CustomOrderDetail> DuplicateAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
}
