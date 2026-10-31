using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public enum CashMovementType
{
    ENTRADA = 1,
    SALIDA = 2
}

public enum CashMovementOrigin
{
    VENTA = 1,
    ABONO_CLIENTE = 2,
    PEDIDO = 3,
    REEMBOLSO_PEDIDO = 4,
    COMPRA = 5,
    PAGO_PROVEEDOR = 6,
    GASTO = 7,
    RETIRO = 8,
    DEVOLUCION = 9,
    PAGO_EMPLEADO = 10,
    APORTE_CAJA = 11,
    AJUSTE_APERTURA_CAJA = 12,
    AJUSTE_CIERRE_CAJA = 13
}

public sealed record CashMovementFilter(
    DateTime Desde,
    DateTime Hasta,
    CashMovementType? Tipo = null,
    CashMovementOrigin? Origen = null,
    MetodoPago? Metodo = null,
    int? UsuarioId = null,
    string? Search = null);

public sealed record CashMovementRow(
    DateTime Fecha,
    CashMovementType Tipo,
    CashMovementOrigin Origen,
    string Documento,
    string Descripcion,
    MetodoPago? Metodo,
    long EntradaCentavos,
    long SalidaCentavos,
    bool AfectaEfectivo,
    int UsuarioId,
    string Usuario,
    string? Referencia,
    int? CajaId,
    long? EfectivoDespuesCentavos = null);

public sealed record CashAdvancedSummary(
    DateTime Desde,
    DateTime Hasta,
    long MontoAperturaCentavos,
    long VentasEfectivoCentavos,
    long AbonosClientesEfectivoCentavos,
    long AnticiposPedidosEfectivoCentavos,
    long AbonosPedidosEfectivoCentavos,
    long PagosFinalesPedidosEfectivoCentavos,
    long ReembolsosVentasEfectivoCentavos,
    long ReembolsosPedidosEfectivoCentavos,
    long PagosComprasEfectivoCentavos,
    long AbonosProveedoresEfectivoCentavos,
    long GastosCentavos,
    long RetirosCentavos,
    long TransferenciasRecibidasCentavos,
    long BitcoinRecibidoCentavos,
    long TransferenciasPagadasCentavos,
    long BitcoinPagadoCentavos,
    long EfectivoEsperadoCentavos,
    long? EfectivoContadoCentavos,
    long? DiferenciaCentavos,
    long PagosEmpleadosCentavos = 0,
    long AportesCajaCentavos = 0);

public sealed record CashMovementSearchResult(
    CashAdvancedSummary Summary,
    IReadOnlyList<CashMovementRow> Movements,
    IReadOnlyList<UserFilterOption> Users);

public sealed record UserFilterOption(int UsuarioId, string NombreUsuario);
