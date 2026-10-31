using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class PagoCorreccionVenta
{
    public int PagoCorreccionVentaId { get; set; }

    public int DevolucionId { get; set; }

    public Devolucion Devolucion { get; set; } = null!;

    public int VentaId { get; set; }

    public Venta Venta { get; set; } = null!;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public int? CajaId { get; set; }

    public Caja? Caja { get; set; }

    public MetodoPago MetodoPago { get; set; }

    public long MontoCentavos { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public string? Referencia { get; set; }

    public DateTime FechaPago { get; set; } = DateTime.UtcNow;
}
