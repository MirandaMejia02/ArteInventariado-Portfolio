using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record CustomOrderLineRequest(
    int? ProductoId,
    string? Descripcion,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long DescuentoLineaCentavos = 0,
    string? Especificaciones = null,
    string? Color = null,
    string? Medidas = null,
    string? Material = null,
    string? TextoPersonalizado = null,
    string? Observaciones = null,
    int? ProductoSkuId = null);

public sealed record CustomOrderSaveRequest(
    int? PedidoPersonalizadoId,
    int? ClienteId,
    string? NombreDestinatario,
    string? Telefono,
    string? Correo,
    string? Direccion,
    DateTime FechaPedido,
    DateTime FechaPrometida,
    PrioridadPedido Prioridad,
    long DescuentoGeneralCentavos,
    string? DescripcionGeneral,
    string? EspecificacionesGenerales,
    string? ObservacionesInternas,
    string? NotasCliente,
    IReadOnlyList<CustomOrderLineRequest> Detalles);

public sealed record CustomOrderStateChangeRequest(
    int PedidoPersonalizadoId,
    EstadoPedidoPersonalizado Estado,
    DateTime FechaEfectiva,
    string? Motivo = null);

public sealed record CustomOrderFilter(
    string? Search = null,
    EstadoPedidoPersonalizado? Estado = null,
    PrioridadPedido? Prioridad = null,
    bool UsarFechas = false,
    DateTime? Desde = null,
    DateTime? Hasta = null,
    bool SoloAtrasados = false,
    int? ClienteId = null,
    int Page = 1,
    int PageSize = 100);

public sealed record CustomOrderSummary(
    int PedidoPersonalizadoId,
    string CodigoPedido,
    string? CodigoCotizacion,
    DateTime FechaPedido,
    DateTime FechaPrometida,
    string Destinatario,
    EstadoPedidoPersonalizado Estado,
    PrioridadPedido Prioridad,
    int Lineas,
    long TotalEstimadoCentavos,
    int DiasRestantes,
    bool Atrasado,
    string Usuario,
    EstadoFinancieroPedido EstadoFinanciero = EstadoFinancieroPedido.SIN_PAGO,
    long TotalPagadoCentavos = 0,
    long TotalReembolsadoCentavos = 0,
    long SaldoPendienteCentavos = 0,
    int? VentaId = null);

public sealed record CustomOrderLineDetail(
    int DetallePedidoPersonalizadoId,
    int? ProductoId,
    string? Codigo,
    string Descripcion,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long DescuentoLineaCentavos,
    long SubtotalCentavos,
    string? Especificaciones,
    string? Color,
    string? Medidas,
    string? Material,
    string? TextoPersonalizado,
    string? Observaciones,
    int? ProductoSkuId = null,
    string? CodigoSku = null,
    string? Variante = null);

public sealed record CustomOrderStatusHistoryRow(
    int HistorialEstadoPedidoId,
    EstadoPedidoPersonalizado? EstadoAnterior,
    EstadoPedidoPersonalizado EstadoNuevo,
    string? Motivo,
    string Usuario,
    DateTime Fecha);

public sealed record CustomOrderDetail(
    int PedidoPersonalizadoId,
    string CodigoPedido,
    int? CotizacionId,
    string? CodigoCotizacion,
    int? ClienteId,
    string? CodigoCliente,
    string Destinatario,
    string? Telefono,
    string? Correo,
    string? Direccion,
    EstadoPedidoPersonalizado Estado,
    PrioridadPedido Prioridad,
    DateTime FechaPedido,
    DateTime FechaPrometida,
    DateTime? FechaInicio,
    DateTime? FechaFinalizacion,
    DateTime? FechaEntregaReal,
    long SubtotalCentavos,
    long DescuentoGeneralCentavos,
    long TotalEstimadoCentavos,
    string? DescripcionGeneral,
    string? EspecificacionesGenerales,
    string? ObservacionesInternas,
    string? NotasCliente,
    string UsuarioCreador,
    string UsuarioUltimaActualizacion,
    DateTime FechaCreacion,
    DateTime FechaActualizacion,
    DateTime? FechaCancelacion,
    string? MotivoCancelacion,
    IReadOnlyList<CustomOrderLineDetail> Detalles,
    IReadOnlyList<CustomOrderStatusHistoryRow> Historial,
    EstadoFinancieroPedido EstadoFinanciero = EstadoFinancieroPedido.SIN_PAGO,
    long TotalPagadoCentavos = 0,
    long TotalReembolsadoCentavos = 0,
    long SaldoPendienteCentavos = 0,
    int? VentaId = null,
    string? NumeroVenta = null);

public sealed record CustomOrderStats(
    int Borradores,
    int Confirmados,
    int EnProduccion,
    int ListosParaEntrega,
    int Atrasados);
