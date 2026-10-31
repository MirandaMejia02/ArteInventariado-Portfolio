namespace ArteInventariado.Domain.Entities;

public class HistorialCostoProducto
{
    public int HistorialCostoProductoId { get; set; }

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int ProveedorId { get; set; }

    public Proveedor Proveedor { get; set; } = null!;

    public int CompraId { get; set; }

    public Compra Compra { get; set; } = null!;

    public int DetalleCompraId { get; set; }

    public DetalleCompra DetalleCompra { get; set; } = null!;

    public int RecepcionCompraId { get; set; }

    public RecepcionCompra RecepcionCompra { get; set; } = null!;

    public long CostoAnteriorCentavos { get; set; }

    public long CostoRecibidoCentavos { get; set; }

    public long CostoPromedioNuevoCentavos { get; set; }

    public int StockAnterior { get; set; }

    public int CantidadRecibida { get; set; }

    public int StockNuevo { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public string? Observaciones { get; set; }
}
