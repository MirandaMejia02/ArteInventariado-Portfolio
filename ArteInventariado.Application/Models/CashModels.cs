using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record CashSummary(
    int? CajaId,
    DateOnly FechaOperacion,
    EstadoCaja? Estado,
    long MontoInicialCentavos,
    long TotalVentasCentavos,
    long TotalRetirosCentavos,
    long TotalGastosCentavos,
    long TotalReembolsosCentavos,
    long TotalEsperadoCentavos,
    long? MontoCierreCentavos,
    DateTime? FechaApertura,
    DateTime? FechaCierre,
    string? UsuarioApertura,
    string? UsuarioCierre,
    long TotalPagosProveedoresCentavos = 0,
    long VentasEfectivoCentavos = 0,
    long AbonosClientesEfectivoCentavos = 0,
    long AnticiposPedidosEfectivoCentavos = 0,
    long AbonosPedidosEfectivoCentavos = 0,
    long PagosFinalesPedidosEfectivoCentavos = 0,
    long ReembolsosVentasEfectivoCentavos = 0,
    long ReembolsosPedidosEfectivoCentavos = 0,
    long PagosComprasEfectivoCentavos = 0,
    long AbonosProveedoresEfectivoCentavos = 0,
    long TransferenciasRecibidasCentavos = 0,
    long BitcoinRecibidoCentavos = 0,
    long TransferenciasPagadasCentavos = 0,
    long BitcoinPagadoCentavos = 0,
    long TotalPagosEmpleadosCentavos = 0,
    long TotalAportesCajaCentavos = 0)
{
    public long? DiferenciaCentavos => MontoCierreCentavos.HasValue
        ? MontoCierreCentavos.Value - TotalEsperadoCentavos
        : null;
}

public sealed record CashDailySummary(
    DateOnly FechaOperacion,
    int CantidadSesiones,
    long AperturaInicialCentavos,
    long EntradasFisicasCentavos,
    long SalidasFisicasCentavos,
    long? AjustesEntreSesionesCentavos,
    long? EfectivoEsperadoAcumuladoCentavos,
    long? SumaSaldosPorSesionCentavos,
    long? UltimoCierreContadoCentavos,
    long? DiferenciaFinalCentavos,
    bool TieneCierreIntermedioSinConteo);

public sealed record CashExpectedBalanceSummary(
    DateOnly FechaOperacion,
    int CantidadSesiones,
    long? EfectivoEsperadoCentavos,
    bool TieneCierreIntermedioSinConteo);

public sealed record OpenCashRequest(
    long MontoInicialCentavos,
    string? Observaciones = null,
    int? CajaCierreAnteriorId = null,
    bool ConfirmoEfectivoIntacto = false,
    string? MotivoVariacionCierreAnterior = null);

public sealed record CashOpeningReference(
    int CajaId,
    DateOnly FechaOperacion,
    long MontoCierreCentavos,
    int CantidadSesionesIncluidas = 1);

public sealed record CloseCashRequest(
    long MontoCierreCentavos,
    string? Observaciones = null,
    string? MotivoImprevisto = null);

public sealed record CashRecoveryStatus(
    CashSummary Cash,
    bool IsFromPreviousDay,
    int AdditionalOpenCashSessions);

public sealed record ContinueCashRequest(
    int CajaId,
    Guid OperationId);

public sealed record RegularizeCashRequest(
    int CajaId,
    long CountedCashCentavos,
    string Reason,
    string Password,
    Guid OperationId);

public sealed record RegularizeClosedCashDayRequest(
    DateOnly FechaOperacion,
    long CountedCashCentavos,
    string Reason,
    string Password,
    Guid OperationId);

public sealed record RegisterWithdrawalRequest(
    long MontoCentavos,
    string Motivo);

public sealed record RegisterCashContributionRequest(
    long MontoCentavos,
    string Motivo);

public sealed record RegisterExpenseRequest(
    long MontoCentavos,
    string Concepto,
    string? Observaciones = null,
    int? CategoriaGastoId = null,
    MetodoPago? MetodoPago = MetodoPago.EFECTIVO,
    string? Referencia = null,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null,
    Guid OperacionId = default);

public sealed record ExpenseCategoryOption(int CategoriaGastoId, string Nombre);

public sealed record CashWithdrawalSummary(
    int RetiroCajaId,
    int CajaId,
    int UsuarioId,
    long MontoCentavos,
    string Motivo,
    DateTime FechaRetiro,
    string Usuario);

public sealed record CashContributionSummary(
    int AporteCajaId,
    int CajaId,
    int UsuarioId,
    long MontoCentavos,
    string Motivo,
    DateTime FechaAporte,
    string Usuario);

public sealed record CashExpenseSummary(
    int GastoId,
    int? CajaId,
    int UsuarioId,
    long MontoCentavos,
    string Concepto,
    DateTime FechaGasto,
    string Usuario,
    string? Observaciones,
    string Categoria = "Sin categoría",
    MetodoPago? MetodoPago = null,
    string? Referencia = null,
    OrigenFondos? OrigenFondos = null,
    string? ResponsableOrigen = null,
    bool AfectaCaja = false);

public sealed record CashRefundSummary(
    int ReembolsoId,
    int? CajaId,
    int UsuarioId,
    long MontoCentavos,
    string Motivo,
    DateTime FechaReembolso,
    string Usuario,
    string MetodoReembolso,
    string? Referencia);

public sealed record CashNotificationEvent(
    string TipoEvento,
    int? CajaId,
    string Descripcion,
    DateTime FechaEvento);
