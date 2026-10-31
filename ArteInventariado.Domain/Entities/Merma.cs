using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Merma
{
    public int MermaId { get; set; }

    public int DevolucionId { get; set; }

    public Devolucion Devolucion { get; set; } = null!;

    public int DetalleDevolucionId { get; set; }

    public DetalleDevolucion DetalleDevolucion { get; set; } = null!;

    public int VentaId { get; set; }

    public Venta Venta { get; set; } = null!;

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int? ProductoSkuId { get; set; }

    public ProductoSku? ProductoSku { get; set; }

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public CondicionProductoDevuelto CondicionProducto { get; set; }

    public int Cantidad { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaMerma { get; set; } = DateTime.UtcNow;
}
