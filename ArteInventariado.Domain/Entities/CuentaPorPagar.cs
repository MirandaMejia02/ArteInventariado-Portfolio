using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class CuentaPorPagar
{
    public int CuentaPorPagarId { get; set; }
    public string CodigoCuenta { get; set; } = string.Empty;
    public int CompraId { get; set; }
    public Compra Compra { get; set; } = null!;
    public int ProveedorId { get; set; }
    public Proveedor Proveedor { get; set; } = null!;
    public int UsuarioCreadorId { get; set; }
    public Usuario UsuarioCreador { get; set; } = null!;
    public long TotalOriginalCentavos { get; set; }
    public long TotalPagadoCentavos { get; set; }
    public long SaldoPendienteCentavos { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaVencimiento { get; set; }
    public EstadoCuentaPorPagar Estado { get; set; } = EstadoCuentaPorPagar.ABIERTA;
    public DateTime? FechaPago { get; set; }
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    public string? Observaciones { get; set; }
    public ICollection<AbonoCuentaPorPagar> Abonos { get; set; } = new List<AbonoCuentaPorPagar>();
}
