using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class HistorialEstadoCotizacion
{
    public int HistorialEstadoCotizacionId { get; set; }
    public int CotizacionId { get; set; }
    public Cotizacion Cotizacion { get; set; } = null!;
    public EstadoCotizacion? EstadoAnterior { get; set; }
    public EstadoCotizacion EstadoNuevo { get; set; }
    public string? Motivo { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
