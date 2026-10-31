namespace ArteInventariado.Domain.Entities;

public class AporteCaja
{
    public int AporteCajaId { get; set; }

    public int CajaId { get; set; }

    public Caja Caja { get; set; } = null!;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public long MontoCentavos { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaAporte { get; set; } = DateTime.UtcNow;
}
