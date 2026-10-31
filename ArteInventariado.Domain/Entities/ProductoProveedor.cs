namespace ArteInventariado.Domain.Entities;

public class ProductoProveedor
{
    public int ProductoProveedorId { get; set; }

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int ProveedorId { get; set; }

    public Proveedor Proveedor { get; set; } = null!;

    public string? CodigoProductoProveedor { get; set; }

    public bool EsProveedorPrincipal { get; set; }

    public long? UltimoCostoCentavos { get; set; }

    public DateTime? FechaUltimaCompra { get; set; }

    public bool Activo { get; set; } = true;

    public string? Notas { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}
