namespace ArteInventariado.Domain.Entities;

public class Subcategoria
{
    public int SubcategoriaId { get; set; }

    public int CategoriaId { get; set; }

    public Categoria Categoria { get; set; } = null!;

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public bool Activa { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaModificacion { get; set; }

    public ICollection<Producto> Productos { get; set; } = new List<Producto>();
}
