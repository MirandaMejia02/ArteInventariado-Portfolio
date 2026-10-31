using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class PedidoPersonalizado
{
    public int PedidoPersonalizadoId { get; set; }
    public string CodigoPedido { get; set; } = string.Empty;
    public int? CotizacionId { get; set; }
    public Cotizacion? Cotizacion { get; set; }
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public string NombreDestinatarioSnapshot { get; set; } = string.Empty;
    public string? TelefonoSnapshot { get; set; }
    public string? CorreoSnapshot { get; set; }
    public string? DireccionSnapshot { get; set; }
    public EstadoPedidoPersonalizado Estado { get; set; } = EstadoPedidoPersonalizado.BORRADOR;
    public PrioridadPedido Prioridad { get; set; } = PrioridadPedido.NORMAL;
    public DateTime FechaPedido { get; set; }
    public DateTime FechaPrometida { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFinalizacion { get; set; }
    public DateTime? FechaEntregaReal { get; set; }
    public long SubtotalCentavos { get; set; }
    public long DescuentoGeneralCentavos { get; set; }
    public long TotalEstimadoCentavos { get; set; }
    public EstadoFinancieroPedido EstadoFinanciero { get; set; } = EstadoFinancieroPedido.SIN_PAGO;
    public long TotalPagadoCentavos { get; set; }
    public long TotalReembolsadoCentavos { get; set; }
    public long SaldoPendienteCentavos { get; set; }
    public int? VentaId { get; set; }
    public Venta? Venta { get; set; }
    public Guid? OperacionConversionId { get; set; }
    public DateTime? FechaConversion { get; set; }
    public int? UsuarioConversionId { get; set; }
    public Usuario? UsuarioConversion { get; set; }
    public string? DescripcionGeneral { get; set; }
    public string? EspecificacionesGenerales { get; set; }
    public string? ObservacionesInternas { get; set; }
    public string? NotasCliente { get; set; }
    public int UsuarioCreadorId { get; set; }
    public Usuario UsuarioCreador { get; set; } = null!;
    public int UsuarioUltimaActualizacionId { get; set; }
    public Usuario UsuarioUltimaActualizacion { get; set; } = null!;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCancelacion { get; set; }
    public string? MotivoCancelacion { get; set; }
    public ICollection<DetallePedidoPersonalizado> Detalles { get; set; } = new List<DetallePedidoPersonalizado>();
    public ICollection<HistorialEstadoPedido> HistorialEstados { get; set; } = new List<HistorialEstadoPedido>();
    public ICollection<PagoPedidoPersonalizado> Pagos { get; set; } = new List<PagoPedidoPersonalizado>();
    public ICollection<ReembolsoPedidoPersonalizado> Reembolsos { get; set; } = new List<ReembolsoPedidoPersonalizado>();
    public ICollection<ReservaInventarioPedido> Reservas { get; set; } = new List<ReservaInventarioPedido>();
}
