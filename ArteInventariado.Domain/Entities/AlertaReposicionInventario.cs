using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class AlertaReposicionInventario
{
    public int AlertaReposicionInventarioId { get; set; }

    public int ProductoSkuId { get; set; }

    public ProductoSku ProductoSku { get; set; } = null!;

    public EstadoAlertaReposicion Estado { get; set; }

    public DateTime? PospuestaHasta { get; set; }

    public string? MotivoDescarte { get; set; }

    public int UsuarioActualizacionId { get; set; }

    public Usuario UsuarioActualizacion { get; set; } = null!;

    public DateTime FechaCreacion { get; set; }

    public DateTime FechaActualizacion { get; set; }
}
