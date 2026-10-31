using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Venta
{
    public int VentaId { get; set; }

    public string NumeroVenta { get; set; } = string.Empty;

    public int CajaId { get; set; }

    public Caja Caja { get; set; } = null!;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public string? UsuarioNombreSnapshot { get; set; }

    public int? ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    public string ClienteNombreSnapshot { get; set; } = "Consumidor final";

    public DateTime FechaVenta { get; set; } = DateTime.UtcNow;

    public long SubtotalCentavos { get; set; }

    public long DescuentoCentavos { get; set; }

    public string? DescuentoMotivo { get; set; }

    public long TotalCentavos { get; set; }

    public long PagadoCentavos { get; set; }

    public long CambioCentavos { get; set; }

    public EstadoVenta Estado { get; set; } = EstadoVenta.COMPLETADA;

    public string? Observaciones { get; set; }

    public ICollection<DetalleVenta> Detalles { get; set; } = new List<DetalleVenta>();

    public ICollection<PagoVenta> Pagos { get; set; } = new List<PagoVenta>();

    public ICollection<MovimientoInventario> MovimientosInventario { get; set; } = new List<MovimientoInventario>();

    public CuentaPorCobrar? CuentaPorCobrar { get; set; }
}
