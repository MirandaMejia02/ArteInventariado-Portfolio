using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class DetalleVenta
{
    public int DetalleVentaId { get; set; }

    public int VentaId { get; set; }

    public Venta Venta { get; set; } = null!;

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int? ProductoSkuId { get; set; }

    public ProductoSku? ProductoSku { get; set; }

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

    /// <summary>
    /// Subcategoría capturada al momento de la operación.  Se mantiene nullable
    /// para conservar la semántica de registros históricos anteriores a la
    /// captura de esta dimensión.
    /// </summary>
    public int? SubcategoriaIdSnapshot { get; set; }

    public string? SubcategoriaNombreSnapshot { get; set; }

    public long SubtotalCentavos { get; set; }

    public long DescuentoCentavos { get; set; }

    public long? DescuentoGeneralProporcionalCentavos { get; set; }

    public string? DescuentoMotivo { get; set; }

    public long TotalCentavos { get; set; }
}
