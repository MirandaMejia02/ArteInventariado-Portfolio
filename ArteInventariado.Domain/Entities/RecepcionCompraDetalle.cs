using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class RecepcionCompraDetalle
{
    public int RecepcionCompraDetalleId { get; set; }

    public int RecepcionCompraId { get; set; }

    public RecepcionCompra RecepcionCompra { get; set; } = null!;

    public int DetalleCompraId { get; set; }

    public DetalleCompra DetalleCompra { get; set; } = null!;

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int? ProductoSkuId { get; set; }

    public ProductoSku? ProductoSku { get; set; }

    public string? CodigoSkuSnapshot { get; set; }

    public string? NombreVarianteSnapshot { get; set; }

    public UbicacionInventario? UbicacionDestino { get; set; }

    public int CantidadRecibida { get; set; }

    public int StockAnterior { get; set; }

    public int StockNuevo { get; set; }

    public long CostoUnitarioCentavos { get; set; }

    public long? CostoNetoUnitarioCentavos { get; set; }

    public long? DescuentoAsignadoCentavos { get; set; }

    public long CostoPromedioAnteriorCentavos { get; set; }

    public long CostoPromedioNuevoCentavos { get; set; }

    public int MovimientoInventarioId { get; set; }

    public MovimientoInventario MovimientoInventario { get; set; } = null!;
}
