using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class PagoVenta
{
    public int PagoVentaId { get; set; }

    public int VentaId { get; set; }

    public Venta Venta { get; set; } = null!;

    public MetodoPago MetodoPago { get; set; }

    public long MontoCentavos { get; set; }

    public string? Referencia { get; set; }

    public DateTime FechaPago { get; set; } = DateTime.UtcNow;

    public OrigenPagoVenta Origen { get; set; } = OrigenPagoVenta.VENTA;

    public int? PagoPedidoPersonalizadoId { get; set; }

    public PagoPedidoPersonalizado? PagoPedidoPersonalizado { get; set; }

    public bool AfectaCaja { get; set; } = true;
}
