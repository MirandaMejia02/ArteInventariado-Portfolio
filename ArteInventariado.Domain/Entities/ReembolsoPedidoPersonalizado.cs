using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class ReembolsoPedidoPersonalizado
{
    public int ReembolsoPedidoPersonalizadoId { get; set; }
    public string CodigoReembolso { get; set; } = string.Empty;
    public Guid OperacionId { get; set; }
    public int PedidoPersonalizadoId { get; set; }
    public PedidoPersonalizado PedidoPersonalizado { get; set; } = null!;
    public int? PagoPedidoPersonalizadoId { get; set; }
    public PagoPedidoPersonalizado? PagoPedidoPersonalizado { get; set; }
    public MetodoPago MetodoPago { get; set; }
    public long MontoCentavos { get; set; }
    public string? Referencia { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public bool AfectaCaja { get; set; }
    public int? CajaId { get; set; }
    public Caja? Caja { get; set; }
    public long SaldoAnteriorCentavos { get; set; }
    public long SaldoNuevoCentavos { get; set; }
    public DateTime FechaCreacion { get; set; }
}
