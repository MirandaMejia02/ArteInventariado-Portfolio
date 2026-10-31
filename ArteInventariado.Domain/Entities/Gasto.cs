using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Gasto
{
    public int GastoId { get; set; }

    public int? CajaId { get; set; }

    public Caja? Caja { get; set; }

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public int? CategoriaGastoId { get; set; }

    public CategoriaGasto? CategoriaGasto { get; set; }

    public string Concepto { get; set; } = string.Empty;

    public long MontoCentavos { get; set; }

    public DateTime FechaGasto { get; set; } = DateTime.UtcNow;

    public string? Observaciones { get; set; }

    public MetodoPago? MetodoPago { get; set; }

    public string? Referencia { get; set; }

    /// <summary>Null for historical expenses whose operation identity was not captured.</summary>
    public Guid? OperacionId { get; set; }

    /// <summary>Null for historical expenses whose funding source was not captured.</summary>
    public OrigenFondos? OrigenFondos { get; set; }

    /// <summary>Non-sensitive display snapshot for an external payer/source.</summary>
    public string? ResponsableOrigen { get; set; }
}
