using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class AbonoCuentaPorPagar
{
    public int AbonoCuentaPorPagarId { get; set; }
    public string CodigoAbono { get; set; } = string.Empty;
    public Guid OperacionId { get; set; }
    /// <summary>Batch identifier. Null means a legacy single-line payment.</summary>
    public Guid? OperacionLoteId { get; set; }
    public int CuentaPorPagarId { get; set; }
    public CuentaPorPagar CuentaPorPagar { get; set; } = null!;
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
    public DateTime FechaAbono { get; set; } = DateTime.UtcNow;
    public string? Observaciones { get; set; }
    public long SaldoAnteriorCentavos { get; set; }
    public long SaldoNuevoCentavos { get; set; }
}
