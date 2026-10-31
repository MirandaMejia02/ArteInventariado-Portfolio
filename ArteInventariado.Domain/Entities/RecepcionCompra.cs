namespace ArteInventariado.Domain.Entities;

public class RecepcionCompra
{
    public int RecepcionCompraId { get; set; }

    public string CodigoRecepcion { get; set; } = string.Empty;

    public Guid OperacionId { get; set; }

    public int CompraId { get; set; }

    public Compra Compra { get; set; } = null!;

    public int UsuarioRecepcionId { get; set; }

    public Usuario UsuarioRecepcion { get; set; } = null!;

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    public string? Observaciones { get; set; }

    public ICollection<RecepcionCompraDetalle> Detalles { get; set; } = new List<RecepcionCompraDetalle>();
}
