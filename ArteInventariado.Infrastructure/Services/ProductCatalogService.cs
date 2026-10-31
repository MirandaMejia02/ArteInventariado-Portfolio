using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class ProductCatalogService : IProductCatalogService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IProductService _products;
    private readonly IEmployeePermissionService? _permissions;
    private readonly IInstallationContext? _installation;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;

    [ActivatorUtilitiesConstructor]
    public ProductCatalogService(IDbContextFactory<AppDbContext> contextFactory, IProductService products,
        IEmployeePermissionService permissions, IInstallationContext installation,
        ICurrentSession session, IAuditService audit)
    {
        _contextFactory = contextFactory;
        _products = products;
        _permissions = permissions;
        _installation = installation;
        _session = session;
        _audit = audit;
    }

    internal ProductCatalogService(
        AppDbContext context,
        IProductService products,
        ICurrentSession session,
        IAuditService audit,
        IEmployeePermissionService? permissions = null)
    {
        _legacyContext = context;
        _products = products;
        _permissions = permissions;
        _session = session;
        _audit = audit;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para el catalogo.");

    public async Task<ProductCatalogDetail?> GetDetailAsync(int productoId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var includeCosts = await CanReadCostsAsync(cancellationToken);
        var product = (await _products.GetAllAsync(cancellationToken: cancellationToken))
            .SingleOrDefault(item => item.ProductoId == productoId);
        if (product is null) return null;
        var skus = await Context.ProductoSkus.AsNoTracking().Include(sku => sku.Producto)
            .Include(sku => sku.SaldosInventario)
            .Where(sku => sku.ProductoId == productoId)
            .OrderByDescending(sku => sku.EsPrincipal).ThenBy(sku => sku.CodigoSku)
            .ToListAsync(cancellationToken);
        return new ProductCatalogDetail(product, skus.Select(sku => ToSummary(sku, includeCosts)).ToList());
    }

    public async Task<IReadOnlyList<ProductSummary>> GetByLocationAsync(
        UbicacionInventario location, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        if (!Enum.IsDefined(location)) throw new ArgumentException("La ubicacion no es valida.", nameof(location));
        var all = await _products.GetAllAsync(cancellationToken: cancellationToken);
        return location switch
        {
            UbicacionInventario.EXHIBICION => all.Where(product => product.StockExhibicion > 0).ToList(),
            UbicacionInventario.BODEGA => all.Where(product => product.StockBodega > 0).ToList(),
            _ => all.Where(product => product.StockSinAsignar > 0).ToList()
        };
    }

    public async Task<ProductSkuSummary> CreateSkuAsync(CreateProductSkuRequest request, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Crear SKU", cancellationToken);
        ValidateOverrides(request.PrecioVentaOverrideCentavos, request.CostoOverrideCentavos, request.MinimoExhibicionOverride);
        if (request.CostoOverrideCentavos.HasValue &&
            !await CanEditPurchaseCostAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "No tiene permiso para asignar el costo de compra del SKU.");
        }

        var code = RequiredCode(request.CodigoSku);
        if (await Context.Productos.AnyAsync(product => product.CodigoInterno.ToUpper() == code.ToUpper(), cancellationToken) ||
            await Context.ProductoSkus.AnyAsync(sku => sku.CodigoSku.ToUpper() == code.ToUpper(), cancellationToken))
            throw new InvalidOperationException("El codigo SKU ya esta en uso.");
        var product = await Context.Productos.Include(item => item.Skus)
            .SingleOrDefaultAsync(item => item.ProductoId == request.ProductoId, cancellationToken)
            ?? throw new InvalidOperationException("El producto no existe.");
        if (product.Modo != ModoProducto.POR_VARIANTES)
            throw new InvalidOperationException("Solo los productos POR_VARIANTES admiten SKU adicionales.");
        var sku = new ProductoSku
        {
            ProductoId = product.ProductoId, CodigoSku = code, NombreVariante = Optional(request.NombreVariante),
            Activo = true, EsPrincipal = false, PrecioVentaOverrideCentavos = request.PrecioVentaOverrideCentavos,
            CostoOverrideCentavos = request.CostoOverrideCentavos, MinimoExhibicionOverride = request.MinimoExhibicionOverride
        };
        foreach (var balance in InventoryStockEngine.CreateInitialBalances(sku, 0)) sku.SaldosInventario.Add(balance);
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        Context.ProductoSkus.Add(sku);
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.PRODUCTO_SKU_CREADO, sku.ProductoSkuId, $"SKU '{sku.CodigoSku}' creado.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        sku.Producto = product;
        return ToSummary(sku);
    }

    public async Task<ProductSkuSummary> UpdateSkuAsync(UpdateProductSkuRequest request, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Editar SKU", cancellationToken);
        ValidateOverrides(request.PrecioVentaOverrideCentavos, request.CostoOverrideCentavos, request.MinimoExhibicionOverride);
        var sku = await Context.ProductoSkus.Include(item => item.Producto).Include(item => item.SaldosInventario)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == request.ProductoSkuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");
        if (request.CostoOverrideCentavos != sku.CostoOverrideCentavos &&
            !await CanEditPurchaseCostAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "No tiene permiso para cambiar el costo de compra del SKU.");
        }

        sku.NombreVariante = Optional(request.NombreVariante);
        sku.PrecioVentaOverrideCentavos = request.PrecioVentaOverrideCentavos;
        sku.CostoOverrideCentavos = request.CostoOverrideCentavos;
        sku.MinimoExhibicionOverride = request.MinimoExhibicionOverride;
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.PRODUCTO_SKU_EDITADO, sku.ProductoSkuId, $"SKU '{sku.CodigoSku}' editado; su codigo estable se conservo.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(sku);
    }

    public async Task SetSkuActiveAsync(int productoSkuId, bool active, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Cambiar estado de SKU", cancellationToken);
        var sku = await Context.ProductoSkus.Include(item => item.SaldosInventario)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");
        if (!active && sku.EsPrincipal) throw new InvalidOperationException("El SKU principal no puede desactivarse.");
        if (!active && sku.SaldosInventario.Any(balance => balance.ExistenciaFisica > 0 || balance.Reservado > 0))
            throw new InvalidOperationException("No se puede desactivar un SKU con existencia o reservas.");
        if (sku.Activo == active) return;
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        sku.Activo = active;
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.PRODUCTO_SKU_ESTADO_MODIFICADO, sku.ProductoSkuId,
            $"SKU '{sku.CodigoSku}' {(active ? "activado" : "desactivado")}.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteSkuAsync(int productoSkuId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Eliminar SKU", cancellationToken);
        var sku = await Context.ProductoSkus.Include(item => item.SaldosInventario).Include(item => item.Assets)
            .Include(item => item.Barcodes)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");
        if (sku.EsPrincipal) throw new InvalidOperationException("El SKU principal es permanente.");
        var hasHistory = sku.SaldosInventario.Any(balance => balance.ExistenciaFisica != 0 || balance.Reservado != 0) ||
            sku.Assets.Count > 0 ||
            sku.Barcodes.Count > 0 ||
            await Context.MovimientosInventarioUbicacion.AnyAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken) ||
            await Context.ReservasInventarioPedido.AnyAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken) ||
            await Context.DetalleVentas.AnyAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken) ||
            await Context.DetalleCompras.AnyAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken) ||
            await Context.DetallePedidosPersonalizados.AnyAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken) ||
            await Context.DetalleDevoluciones.AnyAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken) ||
            await Context.DetallesCambioVenta.AnyAsync(item =>
                item.DetalleVentaOrigen.ProductoSkuId == productoSkuId || item.ProductoSkuNuevoId == productoSkuId,
                cancellationToken) ||
            await Context.Mermas.AnyAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken);
        if (hasHistory) throw new InvalidOperationException("El SKU tiene inventario, imagenes o historia; debe desactivarse.");
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        Context.ProductoSkus.Remove(sku);
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(TipoBitacora.PRODUCTO_SKU_ELIMINADO, productoSkuId, $"SKU '{sku.CodigoSku}' eliminado sin historia.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureWriteAllowedAsync(string operation, CancellationToken cancellationToken)
    {
        _installation?.EnsureOperationalWriteAllowed(operation);
        if (_permissions is not null) await _permissions.EnsurePermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR, cancellationToken);
    }

    private async Task AuditAsync(TipoBitacora type, int id, string description, CancellationToken cancellationToken)
    {
        var userId = _session.UsuarioActual?.UsuarioId ?? throw new InvalidOperationException("Debe existir una sesion activa.");
        await _audit.RegistrarAsync(new AuditEntryRequest(userId, type, "producto_skus", id, description), cancellationToken);
    }

    private static ProductSkuSummary ToSummary(ProductoSku sku, bool includeCosts = true)
    {
        int At(UbicacionInventario location) => sku.SaldosInventario.Where(balance => balance.Ubicacion == location)
            .Sum(balance => balance.ExistenciaFisica);
        return new ProductSkuSummary(sku.ProductoSkuId, sku.ProductoId, sku.CodigoSku, sku.NombreVariante,
            sku.Activo, sku.EsPrincipal, sku.MinimoExhibicionOverride, sku.PrecioVentaOverrideCentavos,
            includeCosts ? sku.CostoOverrideCentavos : null,
            sku.PrecioVentaOverrideCentavos ?? sku.Producto.PrecioVentaCentavos,
            includeCosts ? sku.CostoOverrideCentavos ?? sku.Producto.PrecioCompraCentavos : 0,
            At(UbicacionInventario.EXHIBICION), At(UbicacionInventario.BODEGA), At(UbicacionInventario.SIN_ASIGNAR),
            sku.SaldosInventario.Sum(balance => balance.Reservado),
            sku.SaldosInventario.Sum(balance => balance.ExistenciaFisica),
            sku.SaldosInventario.Sum(balance => balance.Disponible),
            includeCosts);
    }

    private async Task<bool> CanReadCostsAsync(CancellationToken cancellationToken) =>
        _permissions is null ||
        await _permissions.HasPermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR, cancellationToken: cancellationToken);

    private async Task<bool> CanEditPurchaseCostAsync(CancellationToken cancellationToken) =>
        _permissions is null ||
        await _permissions.HasPermissionAsync(
            PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR,
            cancellationToken: cancellationToken);

    private static string RequiredCode(string? value) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("El codigo SKU es obligatorio.", nameof(value)) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateOverrides(long? price, long? cost, int? minimum)
    {
        if (price < 0 || cost < 0 || minimum < 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Precio, costo y minimo no pueden ser negativos.");
    }
}
