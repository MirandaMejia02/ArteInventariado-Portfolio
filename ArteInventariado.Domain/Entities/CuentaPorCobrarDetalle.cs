namespace ArteInventariado.Domain.Entities;

public class CuentaPorCobrarDetalle
{
    public int CuentaPorCobrarDetalleId { get; set; }

    public int CuentaPorCobrarId { get; set; }

    public CuentaPorCobrar CuentaPorCobrar { get; set; } = null!;

    public int DetalleVentaId { get; set; }

    public DetalleVenta DetalleVenta { get; set; } = null!;

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public string ProductoCodigoSnapshot { get; set; } = string.Empty;

    public string ProductoNombreSnapshot { get; set; } = string.Empty;

    public int CantidadFinanciada { get; set; }

    public long PrecioUnitarioCentavos { get; set; }

    public long DescuentoLineaCentavos { get; set; }

    public long DescuentoGeneralProporcionalCentavos { get; set; }

    public long SubtotalFinanciadoCentavos { get; set; }

    public long CapitalPendienteCentavos { get; set; }
}
