namespace ArteInventariado.Domain.Entities;

public class ProductoSku
{
    public int ProductoSkuId { get; set; }

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public string CodigoSku { get; set; } = string.Empty;

    public string? NombreVariante { get; set; }

    public bool Activo { get; set; } = true;

    public bool EsPrincipal { get; set; }

    public int? MinimoExhibicionOverride { get; set; }

    public long? PrecioVentaOverrideCentavos { get; set; }

    public long? CostoOverrideCentavos { get; set; }

    public ICollection<InventarioSaldo> SaldosInventario { get; set; } = new List<InventarioSaldo>();

    public ICollection<MovimientoInventarioUbicacion> MovimientosInventario { get; set; } = new List<MovimientoInventarioUbicacion>();

    public AlertaReposicionInventario? AlertaReposicion { get; set; }

    public ICollection<ProductoSkuAsset> Assets { get; set; } = new List<ProductoSkuAsset>();

    public ICollection<DetalleVenta> DetallesVenta { get; set; } = new List<DetalleVenta>();

    public ICollection<ProductoSkuBarcode> Barcodes { get; set; } = new List<ProductoSkuBarcode>();

    public long ResolverPrecioVentaCentavos() =>
        PrecioVentaOverrideCentavos ?? Producto.PrecioVentaCentavos;

    public long ResolverCostoCentavos() =>
        CostoOverrideCentavos ?? Producto.PrecioCompraCentavos;
}
