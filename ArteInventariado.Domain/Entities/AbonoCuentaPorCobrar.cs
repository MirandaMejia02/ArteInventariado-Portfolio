using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class AbonoCuentaPorCobrar
{
    public int AbonoCuentaPorCobrarId { get; set; }

    public string CodigoAbono { get; set; } = string.Empty;

    public Guid OperacionId { get; set; }

    public int CuentaPorCobrarId { get; set; }

    public CuentaPorCobrar CuentaPorCobrar { get; set; } = null!;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public int? CajaId { get; set; }

    public Caja? Caja { get; set; }

    public MetodoPago MetodoPago { get; set; }

    public long MontoCentavos { get; set; }

    public long AplicadoInteresCentavos { get; set; }

    public long AplicadoCapitalCentavos { get; set; }

    public long SaldoAnteriorCentavos { get; set; }

    public long SaldoNuevoCentavos { get; set; }

    public long InteresPendienteAnteriorCentavos { get; set; }

    public long InteresPendienteNuevoCentavos { get; set; }

    public long CapitalPendienteAnteriorCentavos { get; set; }

    public long CapitalPendienteNuevoCentavos { get; set; }

    public string? Referencia { get; set; }

    public string? Observaciones { get; set; }

    public DateTime FechaAbono { get; set; } = DateTime.UtcNow;
}
