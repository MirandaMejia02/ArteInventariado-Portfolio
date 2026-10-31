using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Cliente
{
    public int ClienteId { get; set; }

    public string CodigoCliente { get; set; } = string.Empty;

    public string NombreCompleto { get; set; } = string.Empty;

    public string NombreNormalizado { get; set; } = string.Empty;

    public string? TelefonoCelular { get; set; }

    public string? TelefonoNormalizado { get; set; }

    public string? WhatsApp { get; set; }

    public string? CorreoElectronico { get; set; }

    public string? CorreoNormalizado { get; set; }

    public string? Direccion { get; set; }

    public string? DocumentoIdentidad { get; set; }

    public string? DocumentoNormalizado { get; set; }

    public string? Notas { get; set; }

    public EstadoCliente Estado { get; set; } = EstadoCliente.ACTIVO;

    public bool CreditoHabilitado { get; set; } = true;

    public string? CreditoBloqueoMotivo { get; set; }

    public long? CreditoLimiteCentavos { get; set; }

    public int? CreditoMaximoCuentasActivas { get; set; }

    public int? CreditoPlazoMaximoDias { get; set; }

    public bool? CreditoPermitirSaldoVigente { get; set; }

    public decimal? CreditoInteresPorcentaje { get; set; }

    public int? CreditoUsuarioActualizacionId { get; set; }

    public Usuario? CreditoUsuarioActualizacion { get; set; }

    public DateTime? CreditoFechaActualizacion { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    public int UsuarioCreacionId { get; set; }

    public Usuario UsuarioCreacion { get; set; } = null!;

    public int? UsuarioActualizacionId { get; set; }

    public Usuario? UsuarioActualizacion { get; set; }

    public ICollection<Venta> Ventas { get; set; } = new List<Venta>();

    public ICollection<CuentaPorCobrar> CuentasPorCobrar { get; set; } = new List<CuentaPorCobrar>();

    public ICollection<Cotizacion> Cotizaciones { get; set; } = new List<Cotizacion>();

    public ICollection<PedidoPersonalizado> PedidosPersonalizados { get; set; } = new List<PedidoPersonalizado>();
}
