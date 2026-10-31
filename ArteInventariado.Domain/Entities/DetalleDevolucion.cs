using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class DetalleDevolucion
{
    public int DetalleDevolucionId { get; set; }

    public int DevolucionId { get; set; }

    public Devolucion Devolucion { get; set; } = null!;

    public int DetalleVentaId { get; set; }

    public DetalleVenta DetalleVenta { get; set; } = null!;

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int? ProductoSkuId { get; set; }

    public ProductoSku? ProductoSku { get; set; }

    public string? CodigoSkuSnapshot { get; set; }

    public string? NombreVarianteSnapshot { get; set; }

    public int Cantidad { get; set; }

    public long PrecioUnitarioCentavos { get; set; }

    public long DescuentoProporcionalCentavos { get; set; }

    public long SubtotalDevueltoCentavos { get; set; }

    public CondicionProductoDevuelto CondicionProducto { get; set; }

    public bool ReintegraStock { get; set; }

    public UbicacionInventario? UbicacionReintegro { get; set; }

    public int? MovimientoInventarioId { get; set; }

    public MovimientoInventario? MovimientoInventario { get; set; }
}
