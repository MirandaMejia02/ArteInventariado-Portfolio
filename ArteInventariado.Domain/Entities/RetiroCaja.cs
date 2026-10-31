namespace ArteInventariado.Domain.Entities;

public class RetiroCaja
{
    public int RetiroCajaId { get; set; }

    public int CajaId { get; set; }

    public Caja Caja { get; set; } = null!;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public long MontoCentavos { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaRetiro { get; set; } = DateTime.UtcNow;
}
