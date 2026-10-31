using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class CuentaPorCobrar
{
    public int CuentaPorCobrarId { get; set; }

    public string CodigoCuenta { get; set; } = string.Empty;

    public int VentaId { get; set; }

    public Venta Venta { get; set; } = null!;

    public int ClienteId { get; set; }

    public Cliente Cliente { get; set; } = null!;

    public int CajaId { get; set; }

    public Caja Caja { get; set; } = null!;

    public int UsuarioRegistroId { get; set; }

    public Usuario UsuarioRegistro { get; set; } = null!;

    public long CapitalFinanciadoCentavos { get; set; }

    public decimal InteresPorcentaje { get; set; }

    public long InteresCalculadoCentavos { get; set; }

    public long TotalOriginalCentavos { get; set; }

    public long TotalPagadoCentavos { get; set; }

    public long SaldoActualCentavos { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime FechaVencimiento { get; set; }

    public EstadoCuentaPorCobrar Estado { get; set; } = EstadoCuentaPorCobrar.ABIERTA;

    public DateTime? FechaPago { get; set; }

    public DateTime? FechaAnulacion { get; set; }

    public string? Observaciones { get; set; }

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    public ICollection<CuentaPorCobrarDetalle> Detalles { get; set; } = new List<CuentaPorCobrarDetalle>();

    public ICollection<AbonoCuentaPorCobrar> Abonos { get; set; } = new List<AbonoCuentaPorCobrar>();
}
