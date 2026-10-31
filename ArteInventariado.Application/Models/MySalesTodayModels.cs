using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

/// <summary>
/// Safe summary for the authenticated user's sales during the current business day.
/// It intentionally contains no purchase-cost, margin, credit-account, or cash-register data.
/// </summary>
public sealed record MySalesTodaySummary(
    int VentaId,
    string NumeroVenta,
    DateTime FechaVentaLocal,
    EstadoVenta Estado,
    long DescuentoCentavos,
    long TotalCentavos,
    string MetodosPago);

public sealed record MySalesTodayLine(
    string Producto,
    string CodigoSku,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long DescuentoCentavos,
    long TotalCentavos);

public sealed record MySalesTodayPayment(
    string Metodo,
    long MontoCentavos);

public sealed record MySalesTodayDetail(
    int VentaId,
    string NumeroVenta,
    DateTime FechaVentaLocal,
    EstadoVenta Estado,
    long SubtotalCentavos,
    long DescuentoCentavos,
    long TotalCentavos,
    IReadOnlyList<MySalesTodayLine> Lines,
    IReadOnlyList<MySalesTodayPayment> Payments);
