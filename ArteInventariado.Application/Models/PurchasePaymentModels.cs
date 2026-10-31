using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record PurchasePaymentLineRequest(
    Guid OperacionId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia = null,
    OrigenFondos OrigenFondos = ArteInventariado.Domain.Enums.OrigenFondos.CAJA_FISICA,
    string? ResponsableOrigen = null);

public sealed record RegisterInitialPurchasePaymentsRequest(
    int CompraId,
    DateTime FechaVencimiento,
    IReadOnlyList<PurchasePaymentLineRequest> Pagos,
    string? Observaciones = null,
    Guid? OperacionId = null);

public sealed record PurchasePaymentSummary(
    int PagoCompraId,
    string CodigoPago,
    Guid OperacionId,
    int CompraId,
    string CodigoCompra,
    string Proveedor,
    string Usuario,
    int? CajaId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia,
    DateTime FechaPago,
    string? Observaciones,
    bool EsPagoInicial,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null);

public sealed record PurchaseFinancialResult(
    int CompraId,
    string CodigoCompra,
    int CuentaPorPagarId,
    string CodigoCuenta,
    EstadoFinancieroCompra EstadoFinanciero,
    EstadoCuentaPorPagar EstadoCuenta,
    long TotalCentavos,
    long TotalPagadoCentavos,
    long SaldoPendienteCentavos,
    DateTime FechaVencimiento,
    IReadOnlyList<PurchasePaymentSummary> Pagos,
    bool EsReintentoIdempotente);

public sealed record RegisterSupplierPaymentRequest(
    Guid OperacionId,
    int CuentaPorPagarId,
    long MontoCentavos,
    MetodoPago MetodoPago,
    string? Referencia = null,
    string? Observaciones = null,
    OrigenFondos OrigenFondos = ArteInventariado.Domain.Enums.OrigenFondos.CAJA_FISICA,
    string? ResponsableOrigen = null);

public sealed record SupplierPaymentLineRequest(
    long MontoCentavos,
    MetodoPago MetodoPago,
    string? Referencia = null,
    OrigenFondos OrigenFondos = ArteInventariado.Domain.Enums.OrigenFondos.CAJA_FISICA,
    string? ResponsableOrigen = null,
    Guid? OperacionLineaId = null);

public sealed record RegisterSupplierPaymentBatchRequest(
    Guid OperacionId,
    int CuentaPorPagarId,
    IReadOnlyList<SupplierPaymentLineRequest> Pagos,
    string? Observaciones = null);

public sealed record SupplierPaymentResult(
    int AbonoCuentaPorPagarId,
    string CodigoAbono,
    Guid OperacionId,
    int CuentaPorPagarId,
    string CodigoCuenta,
    int CompraId,
    string CodigoCompra,
    int ProveedorId,
    string Proveedor,
    string Usuario,
    int? CajaId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    string? Referencia,
    string? Observaciones,
    DateTime FechaAbono,
    bool EsReintentoIdempotente,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null);

public sealed record SupplierPaymentBatchResult(
    Guid OperacionId,
    int CuentaPorPagarId,
    IReadOnlyList<SupplierPaymentResult> Pagos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    bool EsReintentoIdempotente);

public sealed record AccountsPayableFilter(
    string? Search = null,
    int? ProveedorId = null,
    EstadoCuentaPorPagar? Estado = null,
    DateTime? Desde = null,
    DateTime? Hasta = null,
    bool UsarFechas = false,
    int Page = 1,
    int PageSize = 100);

public sealed record AccountPayableSummary(
    int CuentaPorPagarId,
    string CodigoCuenta,
    int CompraId,
    string CodigoCompra,
    int ProveedorId,
    string CodigoProveedor,
    string Proveedor,
    DateTime FechaCompra,
    DateTime FechaVencimiento,
    long TotalOriginalCentavos,
    long TotalPagadoCentavos,
    long SaldoPendienteCentavos,
    EstadoCuentaPorPagar Estado,
    string Usuario,
    string? Observaciones);

public sealed record AccountsPayableStats(
    long SaldoTotalCentavos,
    int Abiertas,
    int Parciales,
    int Vencidas,
    int VencenHoy,
    int ProximasAVencer);

public sealed record SupplierPaymentHistoryFilter(
    DateTime? Desde = null,
    DateTime? Hasta = null,
    int? ProveedorId = null,
    int? CuentaPorPagarId = null,
    int? CompraId = null,
    MetodoPago? MetodoPago = null,
    string? Search = null);

public sealed record SupplierPaymentHistoryEntry(
    bool EsPagoInicial,
    int MovimientoId,
    string Codigo,
    Guid OperacionId,
    int CuentaPorPagarId,
    string CodigoCuenta,
    int CompraId,
    string CodigoCompra,
    int ProveedorId,
    string CodigoProveedor,
    string Proveedor,
    string Usuario,
    int? CajaId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    string? Referencia,
    string? Observaciones,
    DateTime Fecha,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null)
{
    public string Tipo => EsPagoInicial ? "PAGO INICIAL" : "ABONO";
}

public sealed record SupplierPaymentSummary(
    int AbonoCuentaPorPagarId,
    string CodigoAbono,
    Guid OperacionId,
    int CuentaPorPagarId,
    string CodigoCuenta,
    int CompraId,
    string CodigoCompra,
    int ProveedorId,
    string CodigoProveedor,
    string Proveedor,
    string Usuario,
    int? CajaId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    string? Referencia,
    string? Observaciones,
    DateTime FechaAbono,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null);

public sealed record AccountPayableDetail(
    AccountPayableSummary Summary,
    EstadoFinancieroCompra EstadoFinancieroCompra,
    IReadOnlyList<PurchasePaymentSummary> PagosIniciales,
    IReadOnlyList<SupplierPaymentSummary> Abonos);

public sealed record SupplierFinancialSummary(
    int ProveedorId,
    long SaldoTotalCentavos,
    int CuentasAbiertas,
    int CuentasVencidas,
    IReadOnlyList<AccountPayableSummary> CuentasRecientes);
