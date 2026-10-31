using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record QuotationLineRequest(
    int? ProductoId,
    string? Descripcion,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long DescuentoLineaCentavos = 0,
    string? Observaciones = null);

public sealed record QuotationSaveRequest(
    int? CotizacionId,
    int? ClienteId,
    string? NombreDestinatario,
    string? Telefono,
    string? Correo,
    string? Direccion,
    DateTime Fecha,
    DateTime FechaVencimiento,
    long DescuentoGeneralCentavos,
    string? CondicionesComerciales,
    string? Observaciones,
    IReadOnlyList<QuotationLineRequest> Detalles);

public sealed record QuotationStateChangeRequest(
    int CotizacionId,
    EstadoCotizacion Estado,
    string? Motivo = null);

public sealed record QuotationFilter(
    string? Search = null,
    EstadoCotizacion? Estado = null,
    bool UsarFechas = false,
    DateTime? Desde = null,
    DateTime? Hasta = null,
    int Page = 1,
    int PageSize = 100);

public sealed record QuotationSummary(
    int CotizacionId,
    string CodigoCotizacion,
    DateTime Fecha,
    DateTime FechaVencimiento,
    EstadoCotizacion Estado,
    int? ClienteId,
    string Destinatario,
    int Lineas,
    long TotalCentavos,
    string UsuarioCreador,
    DateTime FechaActualizacion);

public sealed record QuotationLineDetail(
    int DetalleCotizacionId,
    int? ProductoId,
    string? Codigo,
    string Descripcion,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long DescuentoLineaCentavos,
    long SubtotalCentavos,
    string? Observaciones);

public sealed record QuotationStatusHistoryRow(
    int HistorialEstadoCotizacionId,
    EstadoCotizacion? EstadoAnterior,
    EstadoCotizacion EstadoNuevo,
    string? Motivo,
    string Usuario,
    DateTime Fecha);

public sealed record QuotationDetail(
    int CotizacionId,
    string CodigoCotizacion,
    int? ClienteId,
    string? CodigoCliente,
    string Destinatario,
    string? Telefono,
    string? Correo,
    string? Direccion,
    DateTime Fecha,
    DateTime FechaVencimiento,
    EstadoCotizacion Estado,
    long SubtotalCentavos,
    long DescuentoGeneralCentavos,
    long TotalCentavos,
    string? CondicionesComerciales,
    string? Observaciones,
    string UsuarioCreador,
    string UsuarioUltimaActualizacion,
    DateTime FechaCreacion,
    DateTime FechaActualizacion,
    DateTime? FechaEmision,
    DateTime? FechaCierre,
    string? MotivoCancelacion,
    IReadOnlyList<QuotationLineDetail> Detalles,
    IReadOnlyList<QuotationStatusHistoryRow> Historial);

public sealed record QuotationStats(
    int Borradores,
    int Emitidas,
    int Aceptadas,
    int Vencidas);
