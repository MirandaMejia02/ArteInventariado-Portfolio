using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Compra
{
    public int CompraId { get; set; }

    public string CodigoCompra { get; set; } = string.Empty;

    public int ProveedorId { get; set; }

    public Proveedor Proveedor { get; set; } = null!;

    public int UsuarioRegistroId { get; set; }

    public Usuario UsuarioRegistro { get; set; } = null!;

    public DateTime FechaCompra { get; set; }

    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    public EstadoRecepcionCompra EstadoRecepcion { get; set; } = EstadoRecepcionCompra.PENDIENTE;

    public long SubtotalCentavos { get; set; }

    public long DescuentoCentavos { get; set; }

    public long TotalCentavos { get; set; }

    public EstadoFinancieroCompra EstadoFinanciero { get; set; } = EstadoFinancieroCompra.PENDIENTE;

    public long TotalPagadoCentavos { get; set; }

    public long SaldoPendienteCentavos { get; set; }

    public string? Observaciones { get; set; }

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    public ICollection<DetalleCompra> Detalles { get; set; } = new List<DetalleCompra>();

    public ICollection<RecepcionCompra> Recepciones { get; set; } = new List<RecepcionCompra>();

    public ICollection<PagoCompra> Pagos { get; set; } = new List<PagoCompra>();

    public CuentaPorPagar? CuentaPorPagar { get; set; }
}
