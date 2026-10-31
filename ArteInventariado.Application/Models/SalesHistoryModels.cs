using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record SalesHistoryFilter(
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null,
    string? NumeroVenta = null,
    int? UsuarioId = null,
    MetodoPago? MetodoPago = null);

public sealed record SalesHistorySummary(
    int VentaId,
    DateTime FechaVenta,
    string NumeroVenta,
    string Usuario,
    string Cliente,
    long TotalCentavos,
    long DescuentoCentavos,
    string MetodosPago,
    int CajaId,
    EstadoVenta Estado,
    bool TieneCredito = false);

public sealed record SalesHistoryDetail(
    int VentaId,
    string NumeroVenta,
    DateTime FechaVenta,
    string Usuario,
    string Cliente,
    long SubtotalCentavos,
    long DescuentoCentavos,
    long TotalCentavos,
    long TotalPagadoCentavos,
    long CambioCentavos,
    IReadOnlyList<SalesHistoryLine> Lines,
    IReadOnlyList<SalesHistoryPayment> Payments,
    SalesHistoryCredit? Credit = null);

public sealed record SalesHistoryLine(
    string Producto,
    string Codigo,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long DescuentoCentavos,
    long SubtotalCentavos,
    long TotalCentavos,
    string? ProductoCodigoHistorico = null,
    string? CodigoSkuHistorico = null,
    string? VarianteHistorica = null,
    int? ProductoIdReferencia = null,
    int? ProductoSkuIdReferencia = null);

public sealed record SalesHistoryPayment(
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia);

public sealed record SalesHistoryCredit(
    int CuentaPorCobrarId,
    string CodigoCuenta,
    long CapitalFinanciadoCentavos,
    decimal InteresPorcentaje,
    long InteresCalculadoCentavos,
    long TotalCuentaCentavos,
    long SaldoActualCentavos,
    DateTime FechaVencimiento,
    IReadOnlyList<SalesHistoryCreditLine> Lines);

public sealed record SalesHistoryCreditLine(
    string Producto,
    string Codigo,
    int CantidadFinanciada,
    long SubtotalFinanciadoCentavos,
    long CapitalPendienteCentavos);
