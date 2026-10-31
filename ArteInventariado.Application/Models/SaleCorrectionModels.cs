using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record SaleCorrectionSearchResult(
    int VentaId,
    string NumeroVenta,
    DateTime FechaVenta,
    string Usuario,
    long TotalCentavos,
    EstadoVenta Estado,
    IReadOnlyList<SaleCorrectionLine> Lines,
    IReadOnlyList<SaleCorrectionPayment> Payments);

public sealed record SaleCorrectionLine(
    int DetalleVentaId,
    int ProductoId,
    string CodigoInterno,
    string Producto,
    int CantidadVendida,
    int CantidadDevuelta,
    int CantidadDisponible,
    long PrecioUnitarioCentavos,
    long DescuentoCentavos,
    long TotalLineaCentavos,
    int? ProductoSkuId = null,
    string? CodigoSku = null,
    string? Variante = null);

public sealed record SaleCorrectionPayment(
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia);

public sealed record SaleCorrectionSalesFilter(
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null,
    string? NumeroVenta = null,
    int? UsuarioId = null,
    int Limit = 50);

public sealed record SaleCorrectionSaleSummary(
    int VentaId,
    DateTime FechaVenta,
    string NumeroVenta,
    string Usuario,
    long TotalCentavos,
    string MetodosPago,
    EstadoVenta Estado);

public sealed record RegisterReturnRequest(
    int VentaId,
    string Motivo,
    IReadOnlyList<RegisterReturnLineRequest> Lines,
    RegisterRefundRequest? Reembolso = null,
    string? Observaciones = null,
    Guid? OperacionId = null);

public sealed record RegisterReturnLineRequest(
    int DetalleVentaId,
    int Cantidad,
    CondicionProductoDevuelto CondicionProducto,
    bool ReintegraStock);

public sealed record RegisterRefundRequest(
    MetodoReembolso MetodoReembolso,
    long MontoCentavos,
    string Motivo,
    string? Referencia = null);

public sealed record RegisterSaleChangeRequest(
    int VentaId,
    int DetalleVentaId,
    int Cantidad,
    int ProductoNuevoId,
    CondicionProductoDevuelto CondicionProductoDevuelto,
    bool ReintegraStock,
    string Motivo,
    RegisterCorrectionPaymentRequest? CobroAdicional = null,
    RegisterRefundRequest? Reembolso = null,
    string? Observaciones = null,
    bool FueraPolitica = false,
    int? ProductoNuevoSkuId = null,
    Guid? OperacionId = null);

public sealed record RegisterCorrectionPaymentRequest(
    MetodoPago MetodoPago,
    long MontoCentavos,
    string Motivo,
    string? Referencia = null);

public sealed record SaleChangeResult(
    int DevolucionId,
    string NumeroCorreccion,
    long ValorProductoDevueltoCentavos,
    long ValorProductoNuevoCentavos,
    long DiferenciaCentavos,
    int? PagoCorreccionVentaId,
    int? ReembolsoId);

public sealed record RegisterSaleCancellationRequest(
    int VentaId,
    string Motivo,
    IReadOnlyList<RegisterSaleCancellationLineRequest> Lines,
    RegisterRefundRequest? Reembolso = null,
    string? Observaciones = null,
    Guid? OperacionId = null,
    bool EsErrorRegistroSinOperacionReal = false);

public sealed record RegisterSaleCancellationLineRequest(
    int DetalleVentaId,
    int Cantidad,
    CondicionProductoDevuelto CondicionProducto,
    bool ReintegraStock);

public sealed record SaleCancellationResult(
    int DevolucionId,
    string NumeroCorreccion,
    long TotalAnuladoCentavos,
    int? ReembolsoId,
    long ReembolsoCentavos);

public sealed record ReturnResult(
    int DevolucionId,
    string NumeroDevolucion,
    long TotalDevueltoCentavos,
    int? ReembolsoId,
    long ReembolsoCentavos);

public sealed record ReturnHistoryFilter(
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null,
    string? NumeroVenta = null,
    string? NumeroDevolucion = null);

public sealed record ReturnHistorySummary(
    int DevolucionId,
    string NumeroDevolucion,
    string NumeroVenta,
    DateTime FechaDevolucion,
    string Usuario,
    TipoCorreccionVenta TipoCorreccion,
    long TotalDevueltoCentavos,
    long TotalReembolsadoCentavos,
    string Motivo);

public sealed record ReturnHistoryDetail(
    int DevolucionId,
    string NumeroDevolucion,
    string NumeroVenta,
    TipoCorreccionVenta TipoCorreccion,
    IReadOnlyList<ReturnHistoryLine> Lines,
    IReadOnlyList<ReturnHistoryChangeLine> ChangeLines,
    IReadOnlyList<ReturnHistoryRefund> Refunds);

public sealed record ReturnHistoryLine(
    string Producto,
    string CodigoInterno,
    int Cantidad,
    CondicionProductoDevuelto CondicionProducto,
    bool ReintegraStock,
    long SubtotalDevueltoCentavos);

public sealed record ReturnHistoryRefund(
    MetodoReembolso MetodoReembolso,
    long MontoCentavos,
    string Motivo,
    string? Referencia,
    DateTime FechaReembolso,
    string Usuario);

public sealed record ReturnHistoryChangeLine(
    string ProductoNuevo,
    string CodigoInterno,
    int Cantidad,
    long TotalCentavos);
