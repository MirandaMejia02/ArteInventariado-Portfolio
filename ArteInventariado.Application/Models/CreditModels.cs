using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record CreditPolicySettings(
    long DefaultLimitCentavos,
    int DefaultMaxActiveAccounts,
    int DefaultTermDays,
    int EmployeeMaxTermDays,
    int AdminMaxTermDays,
    decimal DefaultInterestPercent,
    int DaysPastDueForMoroso,
    bool AllowCreditWithOutstandingBalance);

public sealed record UpdateCreditPolicySettingsRequest(
    long DefaultLimitCentavos,
    int DefaultMaxActiveAccounts,
    int DefaultTermDays,
    int EmployeeMaxTermDays,
    int AdminMaxTermDays,
    decimal DefaultInterestPercent,
    int DaysPastDueForMoroso,
    bool AllowCreditWithOutstandingBalance);

public sealed record CustomerCreditPolicy(
    int ClienteId,
    bool CreditoHabilitado,
    string? CreditoBloqueoMotivo,
    long? LimiteCentavos,
    int? MaximoCuentasActivas,
    int? PlazoMaximoDias,
    bool? PermitirSaldoVigente,
    decimal? InteresPorcentaje,
    string? UsuarioActualizacion,
    DateTime? FechaActualizacion);

public sealed record UpdateCustomerCreditPolicyRequest(
    int ClienteId,
    bool CreditoHabilitado,
    string? CreditoBloqueoMotivo,
    long? LimiteCentavos,
    int? MaximoCuentasActivas,
    int? PlazoMaximoDias,
    bool? PermitirSaldoVigente,
    decimal? InteresPorcentaje);

public sealed record EffectiveCreditPolicy(
    int ClienteId,
    long LimitCentavos,
    int MaxActiveAccounts,
    int MaxTermDays,
    bool AllowOutstandingBalance,
    decimal InterestPercent,
    int DaysPastDueForMoroso);

public sealed record CustomerCreditSummary(
    int ClienteId,
    string CodigoCliente,
    string NombreCompleto,
    bool ClienteActivo,
    bool CreditoHabilitado,
    string? CreditoBloqueoMotivo,
    SituacionCrediticia Situacion,
    long SaldoActualCentavos,
    long LimiteCentavos,
    long CreditoDisponibleCentavos,
    int CuentasActivas,
    int MaximoCuentasActivas,
    DateTime? ProximoVencimiento,
    EffectiveCreditPolicy Politica);

public sealed record AccountReceivableFilter(
    int? ClienteId = null,
    EstadoCuentaPorCobrar? Estado = null,
    string? Search = null,
    bool IncludeClosed = false);

public sealed record AccountReceivableSummary(
    int CuentaPorCobrarId,
    string CodigoCuenta,
    int VentaId,
    string NumeroVenta,
    int ClienteId,
    string CodigoCliente,
    string Cliente,
    DateTime FechaCreacion,
    long CapitalCentavos,
    long InteresCentavos,
    long TotalOriginalCentavos,
    long TotalPagadoCentavos,
    long SaldoCentavos,
    DateTime FechaVencimiento,
    EstadoCuentaPorCobrar Estado,
    SituacionCrediticia Situacion);

public sealed record AccountReceivableDetail(
    AccountReceivableSummary Summary,
    string UsuarioRegistro,
    int CajaId,
    string? Observaciones,
    IReadOnlyList<AccountReceivableLineDetail> Lines);

public sealed record AccountReceivableLineDetail(
    int CuentaPorCobrarDetalleId,
    int DetalleVentaId,
    int ProductoId,
    string Codigo,
    string Producto,
    int CantidadFinanciada,
    long PrecioUnitarioCentavos,
    long DescuentoLineaCentavos,
    long DescuentoGeneralProporcionalCentavos,
    long SubtotalFinanciadoCentavos,
    long CapitalPendienteCentavos);
