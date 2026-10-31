using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class PagoCompra
{
    public int PagoCompraId { get; set; }
    public string CodigoPago { get; set; } = string.Empty;
    public Guid OperacionId { get; set; }
    /// <summary>Batch identifier. Null means a legacy single-line payment.</summary>
    public Guid? OperacionLoteId { get; set; }
    public int CompraId { get; set; }
    public Compra Compra { get; set; } = null!;
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public int? CajaId { get; set; }
    public Caja? Caja { get; set; }
    public MetodoPago MetodoPago { get; set; }
    /// <summary>Null on historical rows whose funding source was never captured.</summary>
    public OrigenFondos? OrigenFondos { get; set; }
    /// <summary>Non-sensitive display snapshot for an external payer/source.</summary>
    public string? ResponsableOrigen { get; set; }
    public long MontoCentavos { get; set; }
    public string? Referencia { get; set; }
    public DateTime FechaPago { get; set; } = DateTime.UtcNow;
    public string? Observaciones { get; set; }
    public bool EsPagoInicial { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
