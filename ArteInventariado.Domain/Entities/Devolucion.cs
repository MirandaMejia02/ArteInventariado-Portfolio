using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Devolucion
{
    public int DevolucionId { get; set; }

    public Guid? OperacionId { get; set; }

    public string NumeroDevolucion { get; set; } = string.Empty;

    public int VentaId { get; set; }

    public Venta Venta { get; set; } = null!;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public int? CajaId { get; set; }

    public Caja? Caja { get; set; }

    public DateTime FechaDevolucion { get; set; } = DateTime.UtcNow;

    public TipoCorreccionVenta TipoCorreccion { get; set; } = TipoCorreccionVenta.DEVOLUCION;

    public bool EsErrorRegistroSinOperacionReal { get; set; }

    public long TotalDevueltoCentavos { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public string? Observaciones { get; set; }

    public ICollection<DetalleDevolucion> Detalles { get; set; } = new List<DetalleDevolucion>();

    public ICollection<DetalleCambioVenta> DetallesCambio { get; set; } = new List<DetalleCambioVenta>();

    public ICollection<PagoCorreccionVenta> PagosCorreccion { get; set; } = new List<PagoCorreccionVenta>();

    public ICollection<Merma> Mermas { get; set; } = new List<Merma>();

    public ICollection<Reembolso> Reembolsos { get; set; } = new List<Reembolso>();
}
