namespace ArteInventariado.Domain.Entities;

public class DetalleCompra
{
    public int DetalleCompraId { get; set; }

    public int CompraId { get; set; }

    public Compra Compra { get; set; } = null!;

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int? ProductoSkuId { get; set; }

    public ProductoSku? ProductoSku { get; set; }

    public string CodigoProductoSnapshot { get; set; } = string.Empty;

    public string NombreProductoSnapshot { get; set; } = string.Empty;

    public string? CodigoSkuSnapshot { get; set; }

    public string? NombreVarianteSnapshot { get; set; }

    public int CantidadComprada { get; set; }

    public int CantidadRecibida { get; set; }

    public long CostoUnitarioCentavos { get; set; }

    public long DescuentoLineaCentavos { get; set; }

    public long SubtotalCentavos { get; set; }

    public string? Observaciones { get; set; }

    public ICollection<RecepcionCompraDetalle> Recepciones { get; set; } = new List<RecepcionCompraDetalle>();
}
