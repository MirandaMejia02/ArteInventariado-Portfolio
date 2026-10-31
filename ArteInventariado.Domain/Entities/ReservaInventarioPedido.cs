using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class ReservaInventarioPedido
{
    public int ReservaInventarioPedidoId { get; set; }
    public int PedidoPersonalizadoId { get; set; }
    public PedidoPersonalizado PedidoPersonalizado { get; set; } = null!;
    public int DetallePedidoPersonalizadoId { get; set; }
    public DetallePedidoPersonalizado DetallePedidoPersonalizado { get; set; } = null!;
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public int ProductoSkuId { get; set; }
    public ProductoSku ProductoSku { get; set; } = null!;
    public UbicacionInventario Ubicacion { get; set; }
    public int CantidadReservada { get; set; }
    public int CantidadConsumida { get; set; }
    public int CantidadLiberada { get; set; }
    public EstadoReservaInventarioPedido Estado { get; set; }
    public DateTime FechaReserva { get; set; }
    public DateTime? FechaConsumo { get; set; }
    public DateTime? FechaLiberacion { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string? Motivo { get; set; }
}
