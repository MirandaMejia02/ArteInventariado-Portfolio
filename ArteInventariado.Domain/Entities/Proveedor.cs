using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Proveedor
{
    public int ProveedorId { get; set; }

    public string CodigoProveedor { get; set; } = string.Empty;

    public TipoProveedor Tipo { get; set; } = TipoProveedor.PROVEEDOR;

    public string NombreRazonSocial { get; set; } = string.Empty;

    public string NombreNormalizado { get; set; } = string.Empty;

    public string? NombreContacto { get; set; }

    public string? Telefono { get; set; }

    public string? TelefonoNormalizado { get; set; }

    public string? WhatsApp { get; set; }

    public string? WhatsAppNormalizado { get; set; }

    public string? Correo { get; set; }

    public string? CorreoNormalizado { get; set; }

    public string? Direccion { get; set; }

    public string? DocumentoIdentidad { get; set; }

    public string? DocumentoIdentidadNormalizado { get; set; }

    public string? DocumentoFiscal { get; set; }

    public string? DocumentoFiscalNormalizado { get; set; }

    public string? Notas { get; set; }

    public EstadoProveedor Estado { get; set; } = EstadoProveedor.ACTIVO;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    public int UsuarioCreacionId { get; set; }

    public Usuario UsuarioCreacion { get; set; } = null!;

    public int? UsuarioActualizacionId { get; set; }

    public Usuario? UsuarioActualizacion { get; set; }

    public string? MotivoDesactivacion { get; set; }

    public DateTime? FechaDesactivacion { get; set; }

    public ICollection<ProductoProveedor> Productos { get; set; } = new List<ProductoProveedor>();

    public ICollection<Compra> Compras { get; set; } = new List<Compra>();

    public ICollection<HistorialCostoProducto> HistorialCostosProducto { get; set; } = new List<HistorialCostoProducto>();
}
