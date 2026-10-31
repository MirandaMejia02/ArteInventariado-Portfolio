using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

internal static class ProductProfitabilityFactLoader
{
    public static async Task<IReadOnlyList<HistoricalProfitabilityMovement>> LoadAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var saleLines = await context.DetalleVentas.AsNoTracking()
            .Where(line => line.Venta.Estado == EstadoVenta.COMPLETADA &&
                line.Venta.FechaVenta >= period.UtcStart &&
                line.Venta.FechaVenta < period.UtcEndExclusive)
            .OrderBy(line => line.VentaId)
            .ThenBy(line => line.DetalleVentaId)
            .Select(line => new SaleLineFact(
                line.DetalleVentaId,
                line.VentaId,
                line.Venta.FechaVenta,
                line.ProductoId,
                line.ProductoCodigoSnapshot,
                line.ProductoNombreSnapshot,
                line.CodigoSkuSnapshot,
                line.CategoriaIdSnapshot,
                line.CategoriaNombreSnapshot,
                line.SubcategoriaIdSnapshot,
                line.SubcategoriaNombreSnapshot,
                line.Cantidad,
                line.TotalCentavos,
                line.DescuentoCentavos,
                line.DescuentoGeneralProporcionalCentavos,
                line.Venta.DescuentoCentavos,
                line.CostoUnitarioCentavos,
                line.OrigenCosto))
            .ToListAsync(cancellationToken);

        var movements = NormalizeSaleDiscounts(saleLines)
            .Select(line => new HistoricalProfitabilityMovement(
                line.DateUtc,
                line.ProductId,
                line.ProductCode,
                line.ProductName,
                line.SkuCode,
                line.CategoryId,
                line.CategoryName,
                line.SubcategoryId,
                line.SubcategoryName,
                line.Quantity,
                line.NetSalesCentavos,
                line.IsCovered ? checked(line.Quantity * line.CostCentavos!.Value) : 0,
                line.CostCentavos,
                line.IsCovered,
                IsReturn: false,
                ReintegratesStock: false))
            .ToList();

        var returns = await context.DetalleDevoluciones.AsNoTracking()
            .Where(line => line.DetalleVenta.Venta.Estado == EstadoVenta.COMPLETADA &&
                line.Devolucion.FechaDevolucion >= period.UtcStart &&
                line.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .Select(line => new ReturnLineFact(
                line.Devolucion.FechaDevolucion,
                line.ProductoId,
                line.DetalleVenta.ProductoCodigoSnapshot,
                line.DetalleVenta.ProductoNombreSnapshot,
                line.CodigoSkuSnapshot ?? line.DetalleVenta.CodigoSkuSnapshot,
                line.DetalleVenta.CategoriaIdSnapshot,
                line.DetalleVenta.CategoriaNombreSnapshot,
                line.DetalleVenta.SubcategoriaIdSnapshot,
                line.DetalleVenta.SubcategoriaNombreSnapshot,
                line.Cantidad,
                line.SubtotalDevueltoCentavos,
                line.ReintegraStock,
                line.DetalleVenta.CostoUnitarioCentavos,
                line.DetalleVenta.OrigenCosto))
            .ToListAsync(cancellationToken);

        movements.AddRange(returns.Select(line =>
        {
            var costIsCovered = line.CostCentavos.HasValue &&
                line.CostOrigin != OrigenCostoHistorico.DESCONOCIDO;
            var costEffect = costIsCovered && line.ReintegratesStock
                ? checked(-line.Quantity * line.CostCentavos!.Value)
                : 0;
            return new HistoricalProfitabilityMovement(
                line.DateUtc,
                line.ProductId,
                line.ProductCode,
                line.ProductName,
                line.SkuCode,
                line.CategoryId,
                line.CategoryName,
                line.SubcategoryId,
                line.SubcategoryName,
                -line.Quantity,
                -line.AmountCentavos,
                costEffect,
                line.CostCentavos,
                costIsCovered,
                IsReturn: true,
                ReintegratesStock: line.ReintegratesStock);
        }));

        var changes = await context.DetallesCambioVenta.AsNoTracking()
            .Where(line => line.DetalleVentaOrigen.Venta.Estado == EstadoVenta.COMPLETADA &&
                line.Devolucion.FechaDevolucion >= period.UtcStart &&
                line.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .Select(line => new ChangeLineFact(
                line.Devolucion.FechaDevolucion,
                line.ProductoNuevoId,
                line.ProductoCodigoSnapshot,
                line.ProductoNombreSnapshot,
                line.CodigoSkuSnapshot,
                line.CategoriaIdSnapshot,
                line.CategoriaNombreSnapshot,
                line.SubcategoriaIdSnapshot,
                line.SubcategoriaNombreSnapshot,
                line.Cantidad,
                line.TotalCentavos,
                line.CostoUnitarioCentavos,
                line.OrigenCosto))
            .ToListAsync(cancellationToken);

        movements.AddRange(changes.Select(line =>
        {
            var costIsCovered = line.CostCentavos.HasValue &&
                line.CostOrigin != OrigenCostoHistorico.DESCONOCIDO;
            return new HistoricalProfitabilityMovement(
                line.DateUtc,
                line.ProductId,
                line.ProductCode,
                line.ProductName,
                line.SkuCode,
                line.CategoryId,
                line.CategoryName,
                line.SubcategoryId,
                line.SubcategoryName,
                line.Quantity,
                line.NetSalesCentavos,
                costIsCovered ? checked(line.Quantity * line.CostCentavos!.Value) : 0,
                line.CostCentavos,
                costIsCovered,
                IsReturn: false,
                ReintegratesStock: false);
        }));

        return movements;
    }

    private static List<SaleLineFact> NormalizeSaleDiscounts(List<SaleLineFact> lines)
    {
        var result = new List<SaleLineFact>(lines.Count);
        foreach (var sale in lines.GroupBy(line => line.SaleId))
        {
            var ordered = sale.OrderBy(line => line.SaleLineId).ToList();
            if (ordered.Count == 0)
            {
                continue;
            }

            if (ordered.All(line => line.StoredGeneralDiscountCentavos.HasValue))
            {
                result.AddRange(ordered.Select(line => line with
                {
                    EffectiveGeneralDiscountCentavos = line.StoredGeneralDiscountCentavos!.Value
                }));
                continue;
            }

            var generalDiscount = Math.Max(
                0,
                ordered[0].SaleDiscountCentavos - ordered.Sum(line => line.LineDiscountCentavos));
            var allocations = ProportionalAmountAllocator.Allocate(
                generalDiscount,
                ordered.Select(line => line.LineNetBeforeGeneralCentavos).ToList());
            result.AddRange(ordered.Select((line, index) => line with
            {
                EffectiveGeneralDiscountCentavos = allocations[index]
            }));
        }

        return result;
    }

    private sealed record SaleLineFact(
        int SaleLineId,
        int SaleId,
        DateTime DateUtc,
        int ProductId,
        string? ProductCode,
        string? ProductName,
        string? SkuCode,
        int? CategoryId,
        string? CategoryName,
        int? SubcategoryId,
        string? SubcategoryName,
        int Quantity,
        long LineNetBeforeGeneralCentavos,
        long LineDiscountCentavos,
        long? StoredGeneralDiscountCentavos,
        long SaleDiscountCentavos,
        long? CostCentavos,
        OrigenCostoHistorico CostOrigin,
        long EffectiveGeneralDiscountCentavos = 0)
    {
        public bool IsCovered => CostCentavos.HasValue &&
            CostOrigin != OrigenCostoHistorico.DESCONOCIDO;

        public long NetSalesCentavos =>
            LineNetBeforeGeneralCentavos - EffectiveGeneralDiscountCentavos;
    }

    private sealed record ReturnLineFact(
        DateTime DateUtc,
        int ProductId,
        string? ProductCode,
        string? ProductName,
        string? SkuCode,
        int? CategoryId,
        string? CategoryName,
        int? SubcategoryId,
        string? SubcategoryName,
        int Quantity,
        long AmountCentavos,
        bool ReintegratesStock,
        long? CostCentavos,
        OrigenCostoHistorico CostOrigin);

    private sealed record ChangeLineFact(
        DateTime DateUtc,
        int ProductId,
        string? ProductCode,
        string? ProductName,
        string? SkuCode,
        int? CategoryId,
        string? CategoryName,
        int? SubcategoryId,
        string? SubcategoryName,
        int Quantity,
        long NetSalesCentavos,
        long? CostCentavos,
        OrigenCostoHistorico CostOrigin);
}

internal sealed record HistoricalProfitabilityMovement(
    DateTime DateUtc,
    int ProductId,
    string? ProductCode,
    string? ProductName,
    string? SkuCode,
    int? CategoryId,
    string? CategoryName,
    int? SubcategoryId,
    string? SubcategoryName,
    int Quantity,
    long NetSalesCentavos,
    long CostEffectCentavos,
    long? CostCentavos,
    bool HasValidCost,
    bool IsReturn,
    bool ReintegratesStock);
