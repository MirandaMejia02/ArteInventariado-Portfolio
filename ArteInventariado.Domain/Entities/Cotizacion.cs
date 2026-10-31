using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Cotizacion
{
    public int CotizacionId { get; set; }
    public string CodigoCotizacion { get; set; } = string.Empty;
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public string NombreDestinatarioSnapshot { get; set; } = string.Empty;
    public string? TelefonoSnapshot { get; set; }
    public string? CorreoSnapshot { get; set; }
    public string? DireccionSnapshot { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public EstadoCotizacion Estado { get; set; } = EstadoCotizacion.BORRADOR;
    public long SubtotalCentavos { get; set; }
    public long DescuentoGeneralCentavos { get; set; }
    public long TotalCentavos { get; set; }
    public string? CondicionesComerciales { get; set; }
    public string? Observaciones { get; set; }
    public int UsuarioCreadorId { get; set; }
    public Usuario UsuarioCreador { get; set; } = null!;
    public int UsuarioUltimaActualizacionId { get; set; }
    public Usuario UsuarioUltimaActualizacion { get; set; } = null!;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaEmision { get; set; }
    public DateTime? FechaCierre { get; set; }
    public string? MotivoCancelacion { get; set; }
    public ICollection<DetalleCotizacion> Detalles { get; set; } = new List<DetalleCotizacion>();
    public ICollection<HistorialEstadoCotizacion> HistorialEstados { get; set; } = new List<HistorialEstadoCotizacion>();
    public PedidoPersonalizado? PedidoPersonalizado { get; set; }
}
