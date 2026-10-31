using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class Usuario
{
    public int UsuarioId { get; set; }

    public string NombreCompleto { get; set; } = string.Empty;

    public string NombreUsuario { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public RolUsuario Rol { get; set; } = RolUsuario.EMPLEADO;

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? UltimoLogin { get; set; }

    public ICollection<Venta> Ventas { get; set; } = new List<Venta>();

    public ICollection<Caja> CajasAbiertas { get; set; } = new List<Caja>();

    public ICollection<Caja> CajasCerradas { get; set; } = new List<Caja>();

    public ICollection<RetiroCaja> RetirosCaja { get; set; } = new List<RetiroCaja>();

    public ICollection<AporteCaja> AportesCaja { get; set; } = new List<AporteCaja>();

    public ICollection<Gasto> Gastos { get; set; } = new List<Gasto>();

    public ICollection<MovimientoInventario> MovimientosInventario { get; set; } = new List<MovimientoInventario>();

    public ICollection<Bitacora> Bitacoras { get; set; } = new List<Bitacora>();

    public ICollection<ImportacionExcel> ImportacionesExcel { get; set; } = new List<ImportacionExcel>();

    public ICollection<UsuarioPermiso> Permisos { get; set; } = new List<UsuarioPermiso>();

    public ICollection<Proveedor> ProveedoresCreados { get; set; } = new List<Proveedor>();

    public ICollection<Proveedor> ProveedoresActualizados { get; set; } = new List<Proveedor>();

    public ICollection<Compra> ComprasRegistradas { get; set; } = new List<Compra>();

    public ICollection<RecepcionCompra> RecepcionesCompra { get; set; } = new List<RecepcionCompra>();

    public ICollection<HistorialCostoProducto> HistorialCostosProducto { get; set; } = new List<HistorialCostoProducto>();

    public ICollection<Cotizacion> CotizacionesCreadas { get; set; } = new List<Cotizacion>();

    public ICollection<Cotizacion> CotizacionesActualizadas { get; set; } = new List<Cotizacion>();

    public ICollection<HistorialEstadoCotizacion> HistorialCotizaciones { get; set; } = new List<HistorialEstadoCotizacion>();

    public ICollection<PedidoPersonalizado> PedidosPersonalizadosCreados { get; set; } = new List<PedidoPersonalizado>();

    public ICollection<PedidoPersonalizado> PedidosPersonalizadosActualizados { get; set; } = new List<PedidoPersonalizado>();

    public ICollection<HistorialEstadoPedido> HistorialPedidosPersonalizados { get; set; } = new List<HistorialEstadoPedido>();
    public ICollection<EmployeeLoginEvent> LoginEvents { get; set; } = new List<EmployeeLoginEvent>();

    public ICollection<EmployeePayment> EmployeePayments { get; set; } = new List<EmployeePayment>();
}
