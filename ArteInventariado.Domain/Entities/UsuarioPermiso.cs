using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class UsuarioPermiso
{
    public int UsuarioPermisoId { get; set; }

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public PermisoEmpleado Permiso { get; set; }

    public bool Habilitado { get; set; } = true;

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}
