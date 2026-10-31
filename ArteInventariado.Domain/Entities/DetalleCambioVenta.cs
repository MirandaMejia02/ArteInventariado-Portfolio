using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class DetalleCambioVenta
{
    public int DetalleCambioVentaId { get; set; }

    public int DevolucionId { get; set; }

    public Devolucion Devolucion { get; set; } = null!;

    public int DetalleVentaOrigenId { get; set; }

    public DetalleVenta DetalleVentaOrigen { get; set; } = null!;

    public int ProductoNuevoId { get; set; }

    public Producto ProductoNuevo { get; set; } = null!;

    public int? ProductoSkuNuevoId { get; set; }

    public ProductoSku? ProductoSkuNuevo { get; set; }

    public int Cantidad { get; set; }

    public long PrecioUnitarioCentavos { get; set; }

    public long? CostoUnitarioCentavos { get; set; }

    public OrigenCostoHistorico OrigenCosto { get; set; } = OrigenCostoHistorico.DESCONOCIDO;

    public string? ProductoCodigoSnapshot { get; set; }

    public string? ProductoNombreSnapshot { get; set; }

    public string? CodigoSkuSnapshot { get; set; }

    public string? NombreVarianteSnapshot { get; set; }

    public int? CategoriaIdSnapshot { get; set; }

    public string? CategoriaNombreSnapshot { get; set; }

    public int? SubcategoriaIdSnapshot { get; set; }

    public string? SubcategoriaNombreSnapshot { get; set; }

    public long SubtotalCentavos { get; set; }

    public long DescuentoCentavos { get; set; }

    public long TotalCentavos { get; set; }

    public int MovimientoInventarioId { get; set; }

    public MovimientoInventario MovimientoInventario { get; set; } = null!;
}
