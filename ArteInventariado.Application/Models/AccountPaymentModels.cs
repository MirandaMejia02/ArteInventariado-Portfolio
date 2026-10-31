using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record RegisterAccountPaymentRequest(
    Guid OperacionId,
    int CuentaPorCobrarId,
    long MontoCentavos,
    MetodoPago MetodoPago,
    string? Referencia = null,
    string? Observaciones = null);

public sealed record RegisterAccountPaymentResult(
    int AbonoCuentaPorCobrarId,
    string CodigoAbono,
    Guid OperacionId,
    int CuentaPorCobrarId,
    string CodigoCuenta,
    int ClienteId,
    string Cliente,
    int VentaId,
    string NumeroVenta,
    int UsuarioId,
    string Usuario,
    int? CajaId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    long AplicadoInteresCentavos,
    long AplicadoCapitalCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    long InteresPendienteAnteriorCentavos,
    long InteresPendienteNuevoCentavos,
    long CapitalPendienteAnteriorCentavos,
    long CapitalPendienteNuevoCentavos,
    string? Referencia,
    string? Observaciones,
    DateTime FechaAbono,
    bool EsReintentoIdempotente);
