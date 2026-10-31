using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class HistorialEstadoPedido
{
    public int HistorialEstadoPedidoId { get; set; }
    public int PedidoPersonalizadoId { get; set; }
    public PedidoPersonalizado PedidoPersonalizado { get; set; } = null!;
    public EstadoPedidoPersonalizado? EstadoAnterior { get; set; }
    public EstadoPedidoPersonalizado EstadoNuevo { get; set; }
    public string? Motivo { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
