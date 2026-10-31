using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record CustomOrderPaymentRequest(
    int PedidoPersonalizadoId,
    Guid OperacionId,
    TipoPagoPedido TipoPago,
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia = null,
    string? Observaciones = null);

public sealed record CustomOrderRefundRequest(
    int PedidoPersonalizadoId,
    Guid OperacionId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    string Motivo,
    int? PagoPedidoPersonalizadoId = null,
    string? Referencia = null);

public sealed record CustomOrderProductMappingRequest(
    int DetallePedidoPersonalizadoId,
    int ProductoId,
    int? ProductoSkuId = null);

public sealed record CustomOrderConversionRequest(
    int PedidoPersonalizadoId,
    Guid OperacionId,
    IReadOnlyList<SalePaymentRequest> FinalPayments,
    bool DejarSaldoCredito = false,
    int PlazoCreditoDias = 0,
    string? ObservacionesCredito = null);

public sealed record CustomOrderFinanceSummary(
    int PedidoPersonalizadoId,
    string CodigoPedido,
    string Destinatario,
    EstadoFinancieroPedido EstadoFinanciero,
    long TotalCentavos,
    long TotalPagadoCentavos,
    long TotalReembolsadoCentavos,
    long PagoNetoCentavos,
    long SaldoPendienteCentavos,
    int? VentaId,
    string? NumeroVenta);

public sealed record CustomOrderPaymentResult(
    int PagoPedidoPersonalizadoId,
    string CodigoPago,
    Guid OperacionId,
    int PedidoPersonalizadoId,
    TipoPagoPedido TipoPago,
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia,
    DateTime Fecha,
    string Usuario,
    string? Observaciones,
    bool AfectaCaja,
    int? CajaId,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    int? VentaIdAplicada);

public sealed record CustomOrderRefundResult(
    int ReembolsoPedidoPersonalizadoId,
    string CodigoReembolso,
    Guid OperacionId,
    int PedidoPersonalizadoId,
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia,
    string Motivo,
    DateTime Fecha,
    string Usuario,
    bool AfectaCaja,
    int? CajaId,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos);

public sealed record CustomOrderReservationResult(
    int ReservaInventarioPedidoId,
    int DetallePedidoPersonalizadoId,
    int ProductoId,
    string CodigoProducto,
    string Producto,
    int CantidadReservada,
    int CantidadConsumida,
    int CantidadLiberada,
    EstadoReservaInventarioPedido Estado,
    DateTime FechaReserva,
    int? ProductoSkuId = null,
    string? CodigoSku = null,
    string? Variante = null,
    UbicacionInventario? Ubicacion = null);

public sealed record CustomOrderFinancialHistoryRow(
    DateTime Fecha,
    string Codigo,
    string Tipo,
    MetodoPago? MetodoPago,
    string? Referencia,
    long MontoCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    string Usuario,
    int? VentaId,
    string Estado,
    bool EsReembolso,
    int MovimientoId);

public sealed record CustomOrderConversionLine(
    int DetallePedidoPersonalizadoId,
    int? ProductoId,
    string? CodigoProducto,
    string Descripcion,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long DescuentoCentavos,
    long TotalCentavos,
    int StockActual,
    int StockReservadoPedido,
    int StockReservadoOtros,
    int StockDisponible,
    int? ProductoSkuId = null,
    string? CodigoSku = null,
    string? Variante = null);

public sealed record CustomOrderConversionPreview(
    CustomOrderFinanceSummary Finance,
    EstadoPedidoPersonalizado EstadoOperativo,
    int? ClienteId,
    bool ClienteActivo,
    bool PuedeCredito,
    IReadOnlyList<CustomOrderConversionLine> Lines,
    IReadOnlyList<CustomOrderReservationResult> Reservas,
    int LineasSinProducto);

public sealed record CustomOrderConversionResult(
    int PedidoPersonalizadoId,
    int VentaId,
    string NumeroVenta,
    long TotalCentavos,
    long PagosPreviosAplicadosCentavos,
    long PagoFinalCentavos,
    long CreditoCentavos,
    long CambioCentavos,
    int? CuentaPorCobrarId);

public sealed record CustomOrderPaymentDocumentData(
    string Codigo,
    string Pedido,
    string Destinatario,
    DateTime Fecha,
    string Tipo,
    MetodoPago Metodo,
    string? Referencia,
    long MontoCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    string Usuario,
    string? Observaciones,
    bool EsReembolso);
