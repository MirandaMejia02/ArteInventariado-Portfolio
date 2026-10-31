using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Producto
{
    public int ProductoId { get; set; }

    public string CodigoInterno { get; set; } = string.Empty;

    public ModoProducto Modo { get; set; } = ModoProducto.SIMPLE;

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public int CategoriaId { get; set; }

    public Categoria Categoria { get; set; } = null!;

    public int? SubcategoriaId { get; set; }

    public Subcategoria? Subcategoria { get; set; }

    public string? ImagenLocalPath { get; set; }

    public long PrecioCompraCentavos { get; set; }

    public long PrecioVentaCentavos { get; set; }

    public int StockActual { get; set; }

    public int StockMinimo { get; set; }

    public EstadoProducto Estado { get; set; } = EstadoProducto.ACTIVO;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaModificacion { get; set; }

    public ICollection<DetalleVenta> DetallesVenta { get; set; } = new List<DetalleVenta>();

    public ICollection<MovimientoInventario> MovimientosInventario { get; set; } = new List<MovimientoInventario>();

    public ICollection<ProductoProveedor> Proveedores { get; set; } = new List<ProductoProveedor>();

    public ICollection<DetalleCompra> DetallesCompra { get; set; } = new List<DetalleCompra>();

    public ICollection<RecepcionCompraDetalle> RecepcionesCompra { get; set; } = new List<RecepcionCompraDetalle>();

    public ICollection<HistorialCostoProducto> HistorialCostosProducto { get; set; } = new List<HistorialCostoProducto>();

    public ICollection<DetalleCotizacion> DetallesCotizacion { get; set; } = new List<DetalleCotizacion>();

    public ICollection<DetallePedidoPersonalizado> DetallesPedidosPersonalizados { get; set; } = new List<DetallePedidoPersonalizado>();

    public ICollection<ProductoSku> Skus { get; set; } = new List<ProductoSku>();

    public ICollection<ProductoAsset> Assets { get; set; } = new List<ProductoAsset>();
}
