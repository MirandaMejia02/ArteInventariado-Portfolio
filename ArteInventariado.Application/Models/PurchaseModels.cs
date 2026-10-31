using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record PurchaseLineRequest(
    int ProductoId,
    int CantidadComprada,
    long CostoUnitarioCentavos,
    long DescuentoLineaCentavos = 0,
    string? Observaciones = null,
    int? ProductoSkuId = null);

public sealed record CreatePurchaseRequest(
    int ProveedorId,
    DateTime FechaCompra,
    IReadOnlyList<PurchaseLineRequest> Detalles,
    long DescuentoCentavos = 0,
    string? Observaciones = null);

public sealed record UpdatePurchaseRequest(
    int CompraId,
    int ProveedorId,
    DateTime FechaCompra,
    IReadOnlyList<PurchaseLineRequest> Detalles,
    long DescuentoCentavos = 0,
    string? Observaciones = null);

public sealed record PurchaseReceptionLineRequest(
    int DetalleCompraId,
    int CantidadRecibida,
    UbicacionInventario UbicacionDestino = UbicacionInventario.SIN_ASIGNAR);

public sealed record RegisterPurchaseReceptionRequest(
    int CompraId,
    Guid OperacionId,
    DateTime? FechaRecepcion,
    IReadOnlyList<PurchaseReceptionLineRequest> Detalles,
    string? Observaciones = null);

public sealed record PurchaseFilter(
    string? Search = null,
    EstadoRecepcionCompra? EstadoRecepcion = null,
    DateTime? Desde = null,
    DateTime? Hasta = null,
    bool UsarFechas = false,
    int Page = 1,
    int PageSize = 100);

public sealed record PurchaseSummary(
    int CompraId,
    string CodigoCompra,
    DateTime FechaCompra,
    int ProveedorId,
    string CodigoProveedor,
    string Proveedor,
    EstadoRecepcionCompra EstadoRecepcion,
    int Productos,
    long TotalCentavos,
    int CantidadComprada,
    int CantidadRecibida,
    int CantidadPendiente,
    string UsuarioRegistro,
    EstadoFinancieroCompra EstadoFinanciero = EstadoFinancieroCompra.PENDIENTE,
    long TotalPagadoCentavos = 0,
    long SaldoPendienteCentavos = 0,
    int? CuentaPorPagarId = null);

public sealed record PurchaseLineSummary(
    int DetalleCompraId,
    int ProductoId,
    string CodigoProducto,
    string Producto,
    int CantidadComprada,
    int CantidadRecibida,
    int CantidadPendiente,
    long CostoUnitarioCentavos,
    long DescuentoLineaCentavos,
    long SubtotalCentavos,
    string? Observaciones,
    int? ProductoSkuId = null,
    string? CodigoSku = null,
    string? Variante = null);

public sealed record PurchaseReceptionLineSummary(
    int RecepcionCompraDetalleId,
    int DetalleCompraId,
    int ProductoId,
    string CodigoProducto,
    string Producto,
    int CantidadRecibida,
    int StockAnterior,
    int StockNuevo,
    long CostoUnitarioCentavos,
    long CostoPromedioAnteriorCentavos,
    long CostoPromedioNuevoCentavos,
    int MovimientoInventarioId,
    long? CostoNetoUnitarioCentavos = null,
    long? DescuentoAsignadoCentavos = null,
    int? ProductoSkuId = null,
    string? CodigoSku = null,
    string? Variante = null,
    UbicacionInventario? UbicacionDestino = null);

public sealed record PurchaseReceptionSummary(
    int RecepcionCompraId,
    string CodigoRecepcion,
    Guid OperacionId,
    int CompraId,
    string CodigoCompra,
    DateTime FechaRecepcion,
    string UsuarioRecepcion,
    string? Observaciones,
    IReadOnlyList<PurchaseReceptionLineSummary> Detalles);

public sealed record CostHistoryRow(
    int HistorialCostoProductoId,
    DateTime Fecha,
    int ProductoId,
    string CodigoProducto,
    string Producto,
    int ProveedorId,
    string Proveedor,
    int CompraId,
    string CodigoCompra,
    long CostoAnteriorCentavos,
    long CostoRecibidoCentavos,
    long CostoPromedioNuevoCentavos,
    int StockAnterior,
    int CantidadRecibida,
    int StockNuevo,
    string Usuario,
    string? Observaciones);

public sealed record PurchaseDetail(
    int CompraId,
    string CodigoCompra,
    int ProveedorId,
    string CodigoProveedor,
    string Proveedor,
    DateTime FechaCompra,
    DateTime FechaRegistro,
    EstadoRecepcionCompra EstadoRecepcion,
    long SubtotalCentavos,
    long DescuentoCentavos,
    long TotalCentavos,
    string? Observaciones,
    string UsuarioRegistro,
    IReadOnlyList<PurchaseLineSummary> Detalles,
    IReadOnlyList<PurchaseReceptionSummary> Recepciones,
    IReadOnlyList<CostHistoryRow> HistorialCostos,
    EstadoFinancieroCompra EstadoFinanciero = EstadoFinancieroCompra.PENDIENTE,
    long TotalPagadoCentavos = 0,
    long SaldoPendienteCentavos = 0,
    int? CuentaPorPagarId = null);

public sealed record PurchaseStats(
    int Pendientes,
    int Parciales,
    int Recibidas,
    long TotalCompradoCentavos,
    long TotalPagadoCentavos = 0,
    long SaldoPendienteCentavos = 0);
