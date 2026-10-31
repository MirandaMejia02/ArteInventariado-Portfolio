using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record AccountPaymentPreviewRequest(
    int CuentaPorCobrarId,
    long MontoCentavos,
    MetodoPago MetodoPago,
    string? Referencia = null);

public sealed record AccountPaymentPreview(
    int CuentaPorCobrarId,
    string CodigoCuenta,
    int ClienteId,
    string CodigoCliente,
    string Cliente,
    int VentaId,
    string NumeroVenta,
    EstadoCuentaPorCobrar EstadoActual,
    SituacionCrediticia SituacionActual,
    DateTime FechaVencimiento,
    long CapitalOriginalCentavos,
    long InteresOriginalCentavos,
    long TotalOriginalCentavos,
    long TotalPagadoCentavos,
    long InteresPendienteCentavos,
    long CapitalPendienteCentavos,
    long SaldoAnteriorCentavos,
    long MontoCentavos,
    long AplicadoInteresCentavos,
    long AplicadoCapitalCentavos,
    long SaldoNuevoCentavos,
    EstadoCuentaPorCobrar EstadoResultante,
    int? CajaId,
    string? CajaDescripcion);

public sealed record AccountPaymentHistoryFilter(
    DateTime? Desde = null,
    DateTime? Hasta = null,
    int? ClienteId = null,
    int? CuentaPorCobrarId = null,
    int? VentaId = null,
    MetodoPago? MetodoPago = null,
    string? CodigoAbono = null,
    string? Search = null);

public sealed record AccountPaymentSummary(
    int AbonoCuentaPorCobrarId,
    string CodigoAbono,
    Guid OperacionId,
    int CuentaPorCobrarId,
    string CodigoCuenta,
    int ClienteId,
    string CodigoCliente,
    string Cliente,
    int VentaId,
    string NumeroVenta,
    string Usuario,
    int? CajaId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    long AplicadoInteresCentavos,
    long AplicadoCapitalCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    string? Referencia,
    string? Observaciones,
    DateTime FechaAbono);

public sealed record AccountPaymentDetail(
    AccountPaymentSummary Summary,
    EstadoCuentaPorCobrar EstadoCuenta,
    SituacionCrediticia SituacionCuenta,
    DateTime FechaVencimiento,
    long CapitalOriginalCentavos,
    long InteresOriginalCentavos,
    long TotalOriginalCentavos,
    long TotalPagadoCuentaCentavos,
    long InteresPendienteAnteriorCentavos,
    long InteresPendienteNuevoCentavos,
    long CapitalPendienteAnteriorCentavos,
    long CapitalPendienteNuevoCentavos);
