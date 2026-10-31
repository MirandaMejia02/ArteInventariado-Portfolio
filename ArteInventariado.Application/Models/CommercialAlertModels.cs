using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public enum CommercialAlertType
{
    COTIZACION_VENCE_HOY = 1,
    COTIZACION_PROXIMA_VENCER = 2,
    COTIZACION_VENCIDA = 3,
    COTIZACION_SIN_RESPUESTA = 4,
    PEDIDO_ATRASADO = 10,
    PEDIDO_VENCE_HOY = 11,
    PEDIDO_PROXIMO_ENTREGAR = 12,
    PEDIDO_LISTO_ENTREGA = 13,
    PEDIDO_CONFIRMADO_SIN_INICIAR = 14,
    PEDIDO_PRODUCCION_SIN_ACTUALIZAR = 15,
    PEDIDO_ENTREGADO_CON_SALDO = 16,
    PEDIDO_CANCELADO_REEMBOLSO_PENDIENTE = 17,
    PEDIDO_LINEA_SIN_PRODUCTO = 18,
    PEDIDO_RESERVA_INCONSISTENTE = 19
}

public enum CommercialDocumentType
{
    COTIZACION = 1,
    PEDIDO = 2
}

public sealed record CommercialAlertFilter(
    CommercialAlertType? Tipo = null,
    string? Estado = null,
    bool UsarFechas = false,
    DateTime? Desde = null,
    DateTime? Hasta = null,
    string? Search = null);

public sealed record CommercialAlertRow(
    CommercialAlertType Tipo,
    CommercialDocumentType DocumentoTipo,
    int DocumentoId,
    string Documento,
    string ClienteDestinatario,
    DateTime FechaRelevante,
    string Estado,
    string Prioridad,
    long TotalCentavos,
    long PagadoCentavos,
    long SaldoCentavos,
    int DiasAtraso,
    string UsuarioResponsable,
    string Descripcion);

public sealed record CommercialAlertSummary(
    int CotizacionesVencidas,
    int PedidosAtrasados,
    int ListosParaEntrega,
    int SaldosPendientes,
    int ReembolsosPendientes,
    int Total);

public sealed record CommercialAlertSearchResult(
    CommercialAlertSummary Summary,
    IReadOnlyList<CommercialAlertRow> Alerts);
