using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Caja
{
    public int CajaId { get; set; }

    public DateOnly FechaOperacion { get; set; }

    public DateTime FechaApertura { get; set; } = DateTime.UtcNow;

    public DateTime? FechaCierre { get; set; }

    public long MontoInicialCentavos { get; set; }

    public long MontoCierreCentavos { get; set; }

    public EstadoCaja Estado { get; set; } = EstadoCaja.ABIERTA;

    public int UsuarioAperturaId { get; set; }

    public Usuario UsuarioApertura { get; set; } = null!;

    public int? UsuarioCierreId { get; set; }

    public Usuario? UsuarioCierre { get; set; }

    public string? Observaciones { get; set; }

    public ICollection<Venta> Ventas { get; set; } = new List<Venta>();

    public ICollection<RetiroCaja> Retiros { get; set; } = new List<RetiroCaja>();

    public ICollection<AporteCaja> Aportes { get; set; } = new List<AporteCaja>();

    public ICollection<Gasto> Gastos { get; set; } = new List<Gasto>();

    public ICollection<EmployeePayment> EmployeePayments { get; set; } = new List<EmployeePayment>();
}
