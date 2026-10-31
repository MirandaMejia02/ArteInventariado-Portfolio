using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record RegisterSaleRequest(
    IReadOnlyList<SaleLineRequest> Lines,
    IReadOnlyList<SalePaymentRequest> Payments,
    long DescuentoGeneralCentavos = 0,
    string? DescuentoGeneralMotivo = null,
    string? Observaciones = null,
    int? ClienteId = null,
    CreditSaleRequest? Credit = null);

public sealed record SaleLineRequest(
    int ProductoId,
    int Cantidad,
    long DescuentoCentavos = 0,
    string? DescuentoMotivo = null,
    int? ProductoSkuId = null);

public sealed record SalePaymentRequest(
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia = null);

public sealed record CreditSaleRequest(
    int ClienteId,
    int PlazoDias,
    IReadOnlyList<CreditSaleLineRequest> Lines,
    string? Observaciones = null);

public sealed record CreditSaleLineRequest(
    int ProductoId,
    int CantidadFinanciada,
    int? ProductoSkuId = null);

public sealed record SaleResult(
    int VentaId,
    string NumeroVenta,
    int CajaId,
    int UsuarioId,
    int? ClienteId,
    string ClienteNombreSnapshot,
    DateTime FechaVenta,
    long SubtotalCentavos,
    long DescuentoCentavos,
    long TotalCentavos,
    long TotalPagadoCentavos,
    long CambioCentavos,
    IReadOnlyList<SaleLineResult> Lines,
    IReadOnlyList<SalePaymentResult> Payments,
    SaleCreditResult? Credit = null);

public sealed record SaleLineResult(
    int DetalleVentaId,
    int ProductoId,
    string CodigoInterno,
    string ProductoNombre,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long SubtotalCentavos,
    long DescuentoCentavos,
    long TotalCentavos,
    int? ProductoSkuId = null,
    string? CodigoSku = null,
    string? NombreVariante = null);

public sealed record SalePaymentResult(
    int PagoVentaId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia);

public sealed record SaleCreditResult(
    int CuentaPorCobrarId,
    string CodigoCuenta,
    long MontoContadoObligatorioCentavos,
    long AbonoInicialCreditoCentavos,
    long CapitalFinanciadoCentavos,
    decimal InteresPorcentaje,
    long InteresCalculadoCentavos,
    long TotalCuentaCentavos,
    long SaldoInicialCentavos,
    DateTime FechaVencimiento,
    IReadOnlyList<SaleCreditLineResult> Lines);

public sealed record SaleCreditLineResult(
    int CuentaPorCobrarDetalleId,
    int DetalleVentaId,
    int ProductoId,
    string ProductoCodigo,
    string ProductoNombre,
    int CantidadFinanciada,
    long SubtotalFinanciadoCentavos,
    long CapitalPendienteCentavos);
