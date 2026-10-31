using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class MovimientoInventarioUbicacion
{
    public int MovimientoInventarioUbicacionId { get; set; }

    public Guid OperacionId { get; set; }

    public int ProductoSkuId { get; set; }

    public ProductoSku ProductoSku { get; set; } = null!;

    public TipoOperacionInventario TipoOperacion { get; set; }

    public UbicacionInventario? UbicacionOrigen { get; set; }

    public UbicacionInventario? UbicacionDestino { get; set; }

    public int Cantidad { get; set; }

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public string Motivo { get; set; } = string.Empty;

    public string? Nota { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
