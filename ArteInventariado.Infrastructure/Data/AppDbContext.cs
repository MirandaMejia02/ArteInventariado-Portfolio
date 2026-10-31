using ArteInventariado.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<ProductoProveedor> ProductoProveedores => Set<ProductoProveedor>();
    public DbSet<Compra> Compras => Set<Compra>();
    public DbSet<DetalleCompra> DetalleCompras => Set<DetalleCompra>();
    public DbSet<RecepcionCompra> RecepcionesCompra => Set<RecepcionCompra>();
    public DbSet<RecepcionCompraDetalle> RecepcionCompraDetalles => Set<RecepcionCompraDetalle>();
    public DbSet<HistorialCostoProducto> HistorialCostosProducto => Set<HistorialCostoProducto>();
    public DbSet<PagoCompra> PagosCompra => Set<PagoCompra>();
    public DbSet<CuentaPorPagar> CuentasPorPagar => Set<CuentaPorPagar>();
    public DbSet<AbonoCuentaPorPagar> AbonosCuentaPorPagar => Set<AbonoCuentaPorPagar>();
    public DbSet<Cotizacion> Cotizaciones => Set<Cotizacion>();
    public DbSet<DetalleCotizacion> DetalleCotizaciones => Set<DetalleCotizacion>();
    public DbSet<HistorialEstadoCotizacion> HistorialEstadoCotizaciones => Set<HistorialEstadoCotizacion>();
    public DbSet<PedidoPersonalizado> PedidosPersonalizados => Set<PedidoPersonalizado>();
    public DbSet<DetallePedidoPersonalizado> DetallePedidosPersonalizados => Set<DetallePedidoPersonalizado>();
    public DbSet<HistorialEstadoPedido> HistorialEstadoPedidos => Set<HistorialEstadoPedido>();
    public DbSet<PagoPedidoPersonalizado> PagosPedidoPersonalizado => Set<PagoPedidoPersonalizado>();
    public DbSet<ReembolsoPedidoPersonalizado> ReembolsosPedidoPersonalizado => Set<ReembolsoPedidoPersonalizado>();
    public DbSet<ReservaInventarioPedido> ReservasInventarioPedido => Set<ReservaInventarioPedido>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Subcategoria> Subcategorias => Set<Subcategoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<ProductoSku> ProductoSkus => Set<ProductoSku>();
    public DbSet<ProductoSkuBarcode> ProductoSkuBarcodes => Set<ProductoSkuBarcode>();
    public DbSet<ProductoAsset> ProductoAssets => Set<ProductoAsset>();
    public DbSet<ProductoSkuAsset> ProductoSkuAssets => Set<ProductoSkuAsset>();
    public DbSet<InventarioSaldo> InventarioSaldos => Set<InventarioSaldo>();
    public DbSet<MovimientoInventarioUbicacion> MovimientosInventarioUbicacion => Set<MovimientoInventarioUbicacion>();
    public DbSet<AlertaReposicionInventario> AlertasReposicionInventario => Set<AlertaReposicionInventario>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<DetalleVenta> DetalleVentas => Set<DetalleVenta>();
    public DbSet<PagoVenta> PagosVenta => Set<PagoVenta>();
    public DbSet<CuentaPorCobrar> CuentasPorCobrar => Set<CuentaPorCobrar>();
    public DbSet<CuentaPorCobrarDetalle> CuentasPorCobrarDetalle => Set<CuentaPorCobrarDetalle>();
    public DbSet<AbonoCuentaPorCobrar> AbonosCuentaPorCobrar => Set<AbonoCuentaPorCobrar>();
    public DbSet<Devolucion> Devoluciones => Set<Devolucion>();
    public DbSet<DetalleDevolucion> DetalleDevoluciones => Set<DetalleDevolucion>();
    public DbSet<DetalleCambioVenta> DetallesCambioVenta => Set<DetalleCambioVenta>();
    public DbSet<Reembolso> Reembolsos => Set<Reembolso>();
    public DbSet<PagoCorreccionVenta> PagosCorreccionVenta => Set<PagoCorreccionVenta>();
    public DbSet<Merma> Mermas => Set<Merma>();
    public DbSet<Caja> Cajas => Set<Caja>();
    public DbSet<RetiroCaja> RetirosCaja => Set<RetiroCaja>();
    public DbSet<AporteCaja> AportesCaja => Set<AporteCaja>();
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<CategoriaGasto> CategoriasGasto => Set<CategoriaGasto>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<Bitacora> Bitacora => Set<Bitacora>();
    public DbSet<ConfiguracionSistema> ConfiguracionSistema => Set<ConfiguracionSistema>();
    public DbSet<ImportacionExcel> ImportacionesExcel => Set<ImportacionExcel>();
    public DbSet<UsuarioPermiso> UsuarioPermisos => Set<UsuarioPermiso>();
    public DbSet<EmployeeLoginEvent> EmployeeLoginEvents => Set<EmployeeLoginEvent>();
    public DbSet<EmployeePayment> EmployeePayments => Set<EmployeePayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
