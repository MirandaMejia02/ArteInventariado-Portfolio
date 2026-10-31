using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class MovimientoInventario
{
    public int MovimientoInventarioId { get; set; }

    public int ProductoId { get; set; }

    public Producto Producto { get; set; } = null!;

    public int? VentaId { get; set; }

    public Venta? Venta { get; set; }

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public TipoMovimientoInventario TipoMovimiento { get; set; }

    public int Cantidad { get; set; }

    public int StockAnterior { get; set; }

    public int StockNuevo { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaMovimiento { get; set; } = DateTime.UtcNow;
}
