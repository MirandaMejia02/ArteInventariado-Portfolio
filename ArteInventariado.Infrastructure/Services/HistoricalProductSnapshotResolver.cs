using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

internal static class HistoricalProductSnapshotResolver
{
    public static async Task<IReadOnlyDictionary<int, OrigenCostoHistorico>> ResolveCostOriginsAsync(
        AppDbContext context,
        IReadOnlyCollection<Producto> products,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return new Dictionary<int, OrigenCostoHistorico>();
        }

        var productIds = products.Select(product => product.ProductoId).Distinct().ToList();
        var history = await context.HistorialCostosProducto
            .AsNoTracking()
            .Where(item => productIds.Contains(item.ProductoId))
            .OrderByDescending(item => item.Fecha)
            .ThenByDescending(item => item.HistorialCostoProductoId)
            .Select(item => new
            {
                item.ProductoId,
                item.CostoPromedioNuevoCentavos
            })
            .ToListAsync(cancellationToken);

        var latestByProduct = history
            .GroupBy(item => item.ProductoId)
            .ToDictionary(group => group.Key, group => group.First().CostoPromedioNuevoCentavos);

        return products.ToDictionary(
            product => product.ProductoId,
            product => latestByProduct.TryGetValue(product.ProductoId, out var latestCost)
                && latestCost == product.PrecioCompraCentavos
                    ? OrigenCostoHistorico.COINCIDE_ULTIMA_RECEPCION
                    : OrigenCostoHistorico.PRODUCTO_ACTUAL);
    }
}
