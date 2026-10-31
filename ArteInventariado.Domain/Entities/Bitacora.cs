using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Bitacora
{
    public int BitacoraId { get; set; }

    public int? UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    public TipoBitacora TipoAccion { get; set; }

    public string Entidad { get; set; } = string.Empty;

    public int? EntidadId { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public string? DatosAntes { get; set; }

    public string? DatosDespues { get; set; }

    public DateTime FechaAccion { get; set; } = DateTime.UtcNow;
}
