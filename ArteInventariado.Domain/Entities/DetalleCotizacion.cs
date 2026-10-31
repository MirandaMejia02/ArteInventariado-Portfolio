namespace ArteInventariado.Domain.Entities;

public class DetalleCotizacion
{
    public int DetalleCotizacionId { get; set; }
    public int CotizacionId { get; set; }
    public Cotizacion Cotizacion { get; set; } = null!;
    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public string? CodigoProductoSnapshot { get; set; }
    public string NombreDescripcionSnapshot { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public long PrecioUnitarioCentavos { get; set; }
    public long DescuentoLineaCentavos { get; set; }
    public long SubtotalCentavos { get; set; }
    public string? Observaciones { get; set; }
}
