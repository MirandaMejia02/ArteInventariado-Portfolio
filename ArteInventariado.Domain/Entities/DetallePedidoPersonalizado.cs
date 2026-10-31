namespace ArteInventariado.Domain.Entities;

public class DetallePedidoPersonalizado
{
    public int DetallePedidoPersonalizadoId { get; set; }
    public int PedidoPersonalizadoId { get; set; }
    public PedidoPersonalizado PedidoPersonalizado { get; set; } = null!;
    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public int? ProductoSkuId { get; set; }
    public ProductoSku? ProductoSku { get; set; }
    public string? CodigoProductoSnapshot { get; set; }
    public string? CodigoSkuSnapshot { get; set; }
    public string? NombreVarianteSnapshot { get; set; }
    public string NombreDescripcionSnapshot { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public long PrecioUnitarioCentavos { get; set; }
    public long DescuentoLineaCentavos { get; set; }
    public long SubtotalCentavos { get; set; }
    public string? Especificaciones { get; set; }
    public string? Color { get; set; }
    public string? Medidas { get; set; }
    public string? Material { get; set; }
    public string? TextoPersonalizado { get; set; }
    public string? Observaciones { get; set; }
    public ICollection<ReservaInventarioPedido> Reservas { get; set; } = new List<ReservaInventarioPedido>();
}
