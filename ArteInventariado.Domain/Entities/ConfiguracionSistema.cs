namespace ArteInventariado.Domain.Entities;

public class ConfiguracionSistema
{
    public int ConfiguracionSistemaId { get; set; }

    public string Clave { get; set; } = string.Empty;

    public string Valor { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public DateTime FechaModificacion { get; set; } = DateTime.UtcNow;
}
