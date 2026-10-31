namespace ArteInventariado.Domain.Entities;

public class CategoriaGasto
{
    public int CategoriaGastoId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public bool Activa { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<Gasto> Gastos { get; set; } = new List<Gasto>();
}
