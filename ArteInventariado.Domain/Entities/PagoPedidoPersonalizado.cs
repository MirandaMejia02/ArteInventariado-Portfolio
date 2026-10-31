using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class PagoPedidoPersonalizado
{
    public int PagoPedidoPersonalizadoId { get; set; }
    public string CodigoPago { get; set; } = string.Empty;
    public Guid OperacionId { get; set; }
    public int PedidoPersonalizadoId { get; set; }
    public PedidoPersonalizado PedidoPersonalizado { get; set; } = null!;
    public TipoPagoPedido TipoPago { get; set; }
    public MetodoPago MetodoPago { get; set; }
    public long MontoCentavos { get; set; }
    public string? Referencia { get; set; }
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string? Observaciones { get; set; }
    public bool AfectaCaja { get; set; }
    public int? CajaId { get; set; }
    public Caja? Caja { get; set; }
    public long SaldoAnteriorCentavos { get; set; }
    public long SaldoNuevoCentavos { get; set; }
    public int? VentaIdAplicada { get; set; }
    public Venta? VentaAplicada { get; set; }
    public DateTime? FechaAplicacionVenta { get; set; }
    public DateTime FechaCreacion { get; set; }
    public ICollection<ReembolsoPedidoPersonalizado> Reembolsos { get; set; } = new List<ReembolsoPedidoPersonalizado>();
}
