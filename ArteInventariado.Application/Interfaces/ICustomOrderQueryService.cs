using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICustomOrderQueryService
{
    Task<IReadOnlyList<CustomOrderSummary>> SearchAsync(CustomOrderFilter filter, CancellationToken cancellationToken = default);
    Task<CustomOrderDetail?> GetByIdAsync(int pedidoPersonalizadoId, CancellationToken cancellationToken = default);
    Task<CustomOrderStats> GetStatsAsync(CustomOrderFilter filter, CancellationToken cancellationToken = default);
}
