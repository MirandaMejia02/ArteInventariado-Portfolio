using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICustomOrderFinanceService
{
    Task<CustomOrderPaymentResult> RegisterPaymentAsync(CustomOrderPaymentRequest request, CancellationToken cancellationToken = default);
    Task<CustomOrderRefundResult> RegisterRefundAsync(CustomOrderRefundRequest request, CancellationToken cancellationToken = default);
    Task MapProductAsync(CustomOrderProductMappingRequest request, CancellationToken cancellationToken = default);
    Task<CustomOrderConversionPreview> GetConversionPreviewAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<CustomOrderConversionResult> ConvertToSaleAsync(CustomOrderConversionRequest request, CancellationToken cancellationToken = default);
}
