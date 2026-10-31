using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService? _permissions;
    private readonly IInstallationContext? _installation;

    [ActivatorUtilitiesConstructor]
    public ProductService(
        IDbContextFactory<AppDbContext> contextFactory,
        IAuditService auditService,
        ICurrentSession currentSession,
        IEmployeePermissionService permissions,
        IInstallationContext installation)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _currentSession = currentSession;
        _permissions = permissions;
        _installation = installation;
    }

    internal ProductService(
        AppDbContext context,
        IAuditService auditService,
        ICurrentSession currentSession,
        IEmployeePermissionService? permissions = null)
    {
        _legacyContext = context;
        _auditService = auditService;
        _currentSession = currentSession;
        _permissions = permissions;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para el producto.");

    public async Task<IReadOnlyList<ProductSummary>> GetAllAsync(
        ProductFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var includeCosts = await CanReadCostsAsync(cancellationToken);
        var query = BaseProductQuery();

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                var pattern = LikePattern(filter.SearchText);
                query = query.Where(p =>
                    EF.Functions.Like(p.Nombre, pattern, "\\") ||
                    EF.Functions.Like(p.CodigoInterno, pattern, "\\") ||
                    EF.Functions.Like(p.Categoria.Nombre, pattern, "\\") ||
                    (p.Subcategoria != null && EF.Functions.Like(p.Subcategoria.Nombre, pattern, "\\")) ||
                    p.Skus.Any(sku => EF.Functions.Like(sku.CodigoSku, pattern, "\\") ||
                        (sku.NombreVariante != null && EF.Functions.Like(sku.NombreVariante, pattern, "\\"))));
            }

            if (filter.Estado.HasValue)
            {
                query = query.Where(p => p.Estado == filter.Estado.Value);
            }

            if (filter.CategoriaId.HasValue)
            {
                query = query.Where(p => p.CategoriaId == filter.CategoriaId.Value);
            }

            if (filter.SubcategoriaId.HasValue)
            {
                query = query.Where(p => p.SubcategoriaId == filter.SubcategoriaId.Value);
            }

            if (filter.SoloBajoStock)
            {
                query = query.Where(p =>
                    p.Skus.SelectMany(sku => sku.SaldosInventario).Sum(balance => (int?)balance.ExistenciaFisica) <= p.StockMinimo);
            }
        }

        return await query
            .OrderBy(p => p.Nombre)
            .Select(p => ToSummaryProjection(p, includeCosts))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductSummary>> GetAvailableForSaleAsync(
        ProductFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var products = await GetAllAsync(filter, cancellationToken);
        return products.Select(product => product with { StockActual = product.StockDisponible }).ToList();
    }

    private static string LikePattern(string searchText)
    {
        var escaped = searchText.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    public async Task<IReadOnlyList<ProductSummary>> SearchByNameAsync(
        string nombre,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return [];
        }

        var searchText = nombre.Trim();
        var includeCosts = await CanReadCostsAsync(cancellationToken);

        return await BaseProductQuery()
            .Where(p =>
                p.Nombre.Contains(searchText) ||
                p.CodigoInterno.Contains(searchText) ||
                p.Categoria.Nombre.Contains(searchText) ||
                (p.Subcategoria != null && p.Subcategoria.Nombre.Contains(searchText)) ||
                p.Skus.Any(sku => sku.CodigoSku.Contains(searchText) ||
                    (sku.NombreVariante != null && sku.NombreVariante.Contains(searchText))))
            .OrderBy(p => p.Nombre)
            .Select(p => ToSummaryProjection(p, includeCosts))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductSummary?> GetByCodigoInternoAsync(
        string codigoInterno,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(codigoInterno))
        {
            return null;
        }

        var codigo = codigoInterno.Trim();
        var includeCosts = await CanReadCostsAsync(cancellationToken);

        return await BaseProductQuery()
            .Where(p => p.CodigoInterno == codigo)
            .Select(p => ToSummaryProjection(p, includeCosts))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductSummary>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var includeCosts = await CanReadCostsAsync(cancellationToken);
        return await BaseProductQuery()
            .Where(p => p.Skus.SelectMany(sku => sku.SaldosInventario)
            .Sum(balance => (int?)balance.ExistenciaFisica) <= p.StockMinimo)
            .OrderBy(p => p.Nombre)
            .Select(p => ToSummaryProjection(p, includeCosts))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductSummary> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Crear producto", cancellationToken);

        var precioCompraCentavos = request.PrecioCompraCentavos
            ?? throw new ArgumentException("El precio de compra es obligatorio.", nameof(request.PrecioCompraCentavos));
        if (!await CanEditPurchaseCostAsync(cancellationToken) && precioCompraCentavos != 0)
        {
            throw new InvalidOperationException(
                "No tiene permiso para asignar el costo de compra. El producto debe crearse con costo $0.00.");
        }

        var precioVentaCentavos = request.PrecioVentaCentavos
            ?? throw new ArgumentException("El precio de venta es obligatorio.", nameof(request.PrecioVentaCentavos));
        var stockActual = request.StockActual
            ?? throw new ArgumentException("La cantidad inicial es obligatoria.", nameof(request.StockActual));
        var nombre = ValidarProductoBase(
            request.Nombre,
            precioCompraCentavos,
            precioVentaCentavos,
            request.StockMinimo);

        if (stockActual < 0)
        {
            throw new ArgumentException("El stock actual no puede ser negativo.", nameof(request));
        }

        if (!Enum.IsDefined(request.Modo))
        {
            throw new ArgumentException("El modo del producto no es valido.", nameof(request));
        }

        if (!Enum.IsDefined(request.UbicacionInicial))
        {
            throw new ArgumentException(
                "La ubicacion inicial no es valida.",
                nameof(request.UbicacionInicial));
        }

        if (!request.CategoriaId.HasValue && request.SubcategoriaId.HasValue)
        {
            throw new InvalidOperationException(
                "La categoria predeterminada 'Sin categoria' no puede tener subcategorias.");
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var codigo = string.IsNullOrWhiteSpace(request.CodigoInterno)
            ? await GenerateCodigoInternoAsync(cancellationToken)
            : request.CodigoInterno.Trim();

        await EnsureCodigoUnicoAsync(codigo, null, cancellationToken);

        var categoriaId = request.CategoriaId ??
            await GetDefaultCategoryIdAsync(cancellationToken);

        await EnsureCategoriaActivaAsync(categoriaId, cancellationToken);
        await EnsureSubcategoriaCompatibleAsync(
            categoriaId,
            request.SubcategoriaId,
            allowExistingInactive: false,
            cancellationToken: cancellationToken);

        var producto = new Producto
        {
            CodigoInterno = codigo,
            Modo = request.Modo,
            Nombre = nombre,
            Descripcion = NormalizarTextoOpcional(request.Descripcion),
            CategoriaId = categoriaId,
            SubcategoriaId = request.SubcategoriaId,
            ImagenLocalPath = NormalizarTextoOpcional(request.ImagenLocalPath),
            PrecioCompraCentavos = precioCompraCentavos,
            PrecioVentaCentavos = precioVentaCentavos,
            StockMinimo = request.StockMinimo,
            Estado = EstadoProducto.ACTIVO,
            FechaCreacion = DateTime.UtcNow,
            Skus =
            {
                new ProductoSku
                {
                    CodigoSku = codigo,
                    Activo = true,
                    EsPrincipal = true
                }
            }
        };

        var primarySku = producto.Skus.Single();
        foreach (var balance in InventoryStockEngine.CreateInitialBalances(
                     primarySku,
                     stockActual,
                     request.UbicacionInicial))
        {
            primarySku.SaldosInventario.Add(balance);
        }

        Context.Productos.Add(producto);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaProductoAsync(
            TipoBitacora.PRODUCTO_CREADO,
            producto.ProductoId,
            $"Producto '{producto.Nombre}' creado con codigo {producto.CodigoInterno}.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await GetProductoSummaryRequiredAsync(producto.ProductoId, cancellationToken);
    }

    public async Task<ProductSummary> UpdateAsync(
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Editar producto", cancellationToken);

        if (string.IsNullOrWhiteSpace(request.CodigoInterno))
        {
            throw new ArgumentException("El codigo interno es obligatorio.", nameof(request));
        }

        var producto = await Context.Productos.Include(product => product.Skus)
            .FirstOrDefaultAsync(p => p.ProductoId == request.ProductoId, cancellationToken);

        if (producto is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        if (!await CanEditPurchaseCostAsync(cancellationToken) &&
            request.PrecioCompraCentavos != producto.PrecioCompraCentavos)
        {
            throw new InvalidOperationException(
                "No tiene permiso para cambiar el costo de compra del producto.");
        }

        var nombre = ValidarProductoBase(
            request.Nombre,
            request.PrecioCompraCentavos,
            request.PrecioVentaCentavos,
            request.StockMinimo);

        var codigo = request.CodigoInterno.Trim();
        await EnsureCodigoUnicoAsync(codigo, producto.ProductoId, cancellationToken);
        await EnsureCategoriaActivaAsync(request.CategoriaId, cancellationToken);
        await EnsureSubcategoriaCompatibleAsync(
            request.CategoriaId,
            request.SubcategoriaId,
            allowExistingInactive: producto.CategoriaId == request.CategoriaId &&
                producto.SubcategoriaId.HasValue &&
                producto.SubcategoriaId == request.SubcategoriaId,
            cancellationToken: cancellationToken);
        ValidateModeChange(producto, request.Modo);

        producto.CodigoInterno = codigo;
        producto.Modo = request.Modo;
        producto.Nombre = nombre;
        producto.Descripcion = NormalizarTextoOpcional(request.Descripcion);
        producto.CategoriaId = request.CategoriaId;
        producto.SubcategoriaId = request.SubcategoriaId;
        producto.ImagenLocalPath = NormalizarTextoOpcional(request.ImagenLocalPath);
        producto.PrecioCompraCentavos = request.PrecioCompraCentavos;
        producto.PrecioVentaCentavos = request.PrecioVentaCentavos;
        producto.StockMinimo = request.StockMinimo;
        producto.FechaModificacion = DateTime.UtcNow;

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaProductoAsync(
            TipoBitacora.PRODUCTO_EDITADO,
            producto.ProductoId,
            $"Producto '{producto.Nombre}' editado.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await GetProductoSummaryRequiredAsync(producto.ProductoId, cancellationToken);
    }

    public async Task SetEstadoAsync(
        int productoId,
        EstadoProducto estado,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Cambiar estado de producto", cancellationToken);
        if (estado is not EstadoProducto.ACTIVO and not EstadoProducto.INACTIVO)
        {
            throw new ArgumentException("El estado del producto no es valido.", nameof(estado));
        }

        var producto = await Context.Productos
            .FirstOrDefaultAsync(p => p.ProductoId == productoId, cancellationToken);

        if (producto is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        if (producto.Estado == estado)
        {
            return;
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        producto.Estado = estado;
        producto.FechaModificacion = DateTime.UtcNow;
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaProductoAsync(
            estado == EstadoProducto.ACTIVO
                ? TipoBitacora.PRODUCTO_ACTIVADO
                : TipoBitacora.PRODUCTO_DESACTIVADO,
            producto.ProductoId,
            estado == EstadoProducto.ACTIVO
                ? $"Producto '{producto.Nombre}' activado."
                : $"Producto '{producto.Nombre}' desactivado.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(int productoId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWriteAllowedAsync("Eliminar producto", cancellationToken);
        var producto = await Context.Productos.Include(product => product.Skus)
            .FirstOrDefaultAsync(p => p.ProductoId == productoId, cancellationToken);

        if (producto is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        var skuIds = producto.Skus.Select(sku => sku.ProductoSkuId).ToList();
        var tieneHistorial = await Context.DetalleVentas.AnyAsync(d => d.ProductoId == productoId, cancellationToken) ||
            await Context.MovimientosInventario.AnyAsync(m => m.ProductoId == productoId, cancellationToken) ||
            await Context.MovimientosInventarioUbicacion.AnyAsync(m => skuIds.Contains(m.ProductoSkuId), cancellationToken) ||
            await Context.ReservasInventarioPedido.AnyAsync(r => r.ProductoId == productoId, cancellationToken) ||
            await Context.DetalleCompras.AnyAsync(d => d.ProductoId == productoId, cancellationToken) ||
            await Context.DetalleCotizaciones.AnyAsync(d => d.ProductoId == productoId, cancellationToken) ||
            await Context.DetallePedidosPersonalizados.AnyAsync(d => d.ProductoId == productoId, cancellationToken) ||
            await Context.ProductoAssets.AnyAsync(asset => asset.ProductoId == productoId, cancellationToken) ||
            await Context.ProductoSkuAssets.AnyAsync(asset => skuIds.Contains(asset.ProductoSkuId), cancellationToken) ||
            await Context.ProductoSkuBarcodes.AnyAsync(barcode => skuIds.Contains(barcode.ProductoSkuId), cancellationToken);

        if (tieneHistorial)
        {
            throw new InvalidOperationException("El producto tiene historial. No se puede eliminar; debe desactivarse.");
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        Context.Productos.Remove(producto);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaProductoAsync(
            TipoBitacora.PRODUCTO_ELIMINADO,
            producto.ProductoId,
            $"Producto '{producto.Nombre}' eliminado.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<Producto> BaseProductQuery()
    {
        return Context.Productos
            .AsNoTracking()
            .Include(p => p.Categoria)
            .Include(p => p.Subcategoria)
            .Include(p => p.Assets)
            .Include(p => p.Skus).ThenInclude(sku => sku.SaldosInventario)
            .Include(p => p.Skus).ThenInclude(sku => sku.Assets)
            .Include(p => p.Skus).ThenInclude(sku => sku.Barcodes);
    }

    private async Task<ProductSummary> GetProductoSummaryRequiredAsync(
        int productoId,
        CancellationToken cancellationToken)
    {
        var producto = await BaseProductQuery()
            .Where(p => p.ProductoId == productoId)
            .Select(p => ToSummaryProjection(p, includeCosts: true))
            .FirstOrDefaultAsync(cancellationToken);

        return producto ?? throw new InvalidOperationException("El producto no existe.");
    }

    private static ProductSummary ToSummaryProjection(Producto producto, bool includeCosts)
    {
        var balances = producto.Skus.SelectMany(sku => sku.SaldosInventario).ToList();
        var hasCanonicalBalance = balances.Count > 0;
        var physical = hasCanonicalBalance
            ? balances.Sum(balance => balance.ExistenciaFisica)
            : producto.StockActual;
        var available = hasCanonicalBalance
            ? balances.Sum(balance => balance.Disponible)
            : producto.StockActual;

        return new ProductSummary(
            producto.ProductoId,
            producto.CodigoInterno,
            producto.Nombre,
            producto.Descripcion,
            producto.CategoriaId,
            producto.Categoria.Nombre,
            producto.ImagenLocalPath,
            includeCosts ? producto.PrecioCompraCentavos : 0,
            producto.PrecioVentaCentavos,
            physical,
            producto.StockMinimo,
            producto.Estado,
            producto.FechaCreacion,
            producto.FechaModificacion,
            producto.Modo,
            available,
            balances
                .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION).Sum(balance => balance.ExistenciaFisica),
            balances
                .Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA).Sum(balance => balance.ExistenciaFisica),
            balances
                .Where(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR).Sum(balance => balance.ExistenciaFisica),
            balances.Sum(balance => balance.Reservado),
            producto.Assets.Where(asset => asset.EsPrincipal).Select(asset => asset.RutaRelativa).FirstOrDefault()
                ?? producto.Skus.Where(sku => sku.EsPrincipal).SelectMany(sku => sku.Assets)
                    .Where(asset => asset.EsPrincipal).Select(asset => asset.RutaRelativa).FirstOrDefault(),
            ProductBarcodeStatusResolver.Resolve(producto.Skus.Select(sku =>
                (sku.Activo, sku.Barcodes.Any(barcode => barcode.Activo)))),
            includeCosts,
            producto.SubcategoriaId,
            producto.Subcategoria?.Nombre,
            !string.IsNullOrWhiteSpace(producto.ImagenLocalPath) ||
            producto.Assets.Count > 0 ||
            producto.Skus.Any(sku => sku.Assets.Count > 0));
    }

    private async Task<bool> CanReadCostsAsync(CancellationToken cancellationToken) =>
        _permissions is null ||
        await _permissions.HasPermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR, cancellationToken: cancellationToken);

    private async Task<bool> CanEditPurchaseCostAsync(CancellationToken cancellationToken) =>
        _permissions is null ||
        await _permissions.HasPermissionAsync(
            PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR,
            cancellationToken: cancellationToken);

    private async Task<string> GenerateCodigoInternoAsync(CancellationToken cancellationToken)
    {
        var ultimoId = await Context.Productos
            .AsNoTracking()
            .OrderByDescending(p => p.ProductoId)
            .Select(p => p.ProductoId)
            .FirstOrDefaultAsync(cancellationToken);

        var candidatoNumero = ultimoId + 1;

        while (true)
        {
            var codigo = $"PRD-{candidatoNumero:000000}";
            var existe = await Context.Productos
                .AnyAsync(p => p.CodigoInterno == codigo, cancellationToken) ||
                await Context.ProductoSkus
                    .AnyAsync(sku => sku.CodigoSku == codigo, cancellationToken);

            if (!existe)
            {
                return codigo;
            }

            candidatoNumero++;
        }
    }

    private async Task<int> GetDefaultCategoryIdAsync(CancellationToken cancellationToken)
    {
        var categoria = await Context.Categorias
            .FirstOrDefaultAsync(c => c.Nombre == CategoryService.DefaultCategoryName, cancellationToken);

        if (categoria is not null)
        {
            return categoria.CategoriaId;
        }

        categoria = new Categoria
        {
            Nombre = CategoryService.DefaultCategoryName,
            Descripcion = "Categoria predeterminada para productos sin categoria.",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };

        Context.Categorias.Add(categoria);
        await Context.SaveChangesAsync(cancellationToken);

        return categoria.CategoriaId;
    }

    private async Task EnsureCategoriaActivaAsync(int categoriaId, CancellationToken cancellationToken)
    {
        var existe = await Context.Categorias
            .AnyAsync(c => c.CategoriaId == categoriaId && c.Activa, cancellationToken);

        if (!existe)
        {
            throw new InvalidOperationException("La categoria no existe o esta inactiva.");
        }
    }

    private async Task EnsureSubcategoriaCompatibleAsync(
        int categoriaId,
        int? subcategoriaId,
        bool allowExistingInactive,
        CancellationToken cancellationToken)
    {
        if (!subcategoriaId.HasValue)
        {
            return;
        }

        var categoria = await Context.Categorias
            .AsNoTracking()
            .FirstOrDefaultAsync(category => category.CategoriaId == categoriaId, cancellationToken)
            ?? throw new InvalidOperationException("La categoria no existe.");

        if (string.Equals(
                categoria.Nombre.Trim(),
                CategoryService.DefaultCategoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La categoria 'Sin categoria' no puede tener subcategorias.");
        }

        var subcategoria = await Context.Subcategorias
            .AsNoTracking()
            .FirstOrDefaultAsync(
                subcategory => subcategory.SubcategoriaId == subcategoriaId.Value,
                cancellationToken)
            ?? throw new InvalidOperationException("La subcategoria no existe.");

        if (subcategoria.CategoriaId != categoriaId)
        {
            throw new InvalidOperationException(
                "La subcategoria no pertenece a la categoria seleccionada.");
        }

        if (!subcategoria.Activa && !allowExistingInactive)
        {
            throw new InvalidOperationException(
                "La subcategoria esta inactiva y no puede asignarse a un producto nuevo o editado.");
        }
    }

    private async Task EnsureCodigoUnicoAsync(
        string codigo,
        int? productoIdActual,
        CancellationToken cancellationToken)
    {
        var codigoNormalizado = codigo.ToUpper();
        var existe = await Context.Productos
            .AnyAsync(
                p => p.CodigoInterno.ToUpper() == codigoNormalizado &&
                    (!productoIdActual.HasValue || p.ProductoId != productoIdActual.Value),
                cancellationToken);

        existe = existe || await Context.ProductoSkus
            .AnyAsync(
                sku => sku.CodigoSku.ToUpper() == codigoNormalizado &&
                    (!productoIdActual.HasValue || sku.ProductoId != productoIdActual.Value),
                cancellationToken);

        if (existe)
        {
            throw new InvalidOperationException("Ya existe un producto con ese codigo interno.");
        }
    }

    private async Task RegistrarAuditoriaProductoAsync(
        TipoBitacora tipoAccion,
        int productoId,
        string descripcion,
        CancellationToken cancellationToken)
    {
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                GetUsuarioActualId(),
                tipoAccion,
                "productos",
                productoId,
                descripcion),
            cancellationToken);
    }

    private static string ValidarProductoBase(
        string? nombre,
        long precioCompraCentavos,
        long precioVentaCentavos,
        int stockMinimo)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException("El nombre del producto es obligatorio.", nameof(nombre));
        }

        if (precioCompraCentavos < 0)
        {
            throw new ArgumentException("El precio de compra no puede ser negativo.", nameof(precioCompraCentavos));
        }

        if (precioVentaCentavos < 0)
        {
            throw new ArgumentException("El precio de venta no puede ser negativo.", nameof(precioVentaCentavos));
        }

        if (stockMinimo < 0)
        {
            throw new ArgumentException("El stock minimo no puede ser negativo.", nameof(stockMinimo));
        }

        return nombre.Trim();
    }

    private int GetUsuarioActualId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para administrar productos.");
    }

    private static string? NormalizarTextoOpcional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task EnsureWriteAllowedAsync(string operation, CancellationToken cancellationToken)
    {
        _installation?.EnsureOperationalWriteAllowed(operation);
        if (_permissions is not null)
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR, cancellationToken);
    }

    private static void ValidateModeChange(Producto product, ModoProducto requested)
    {
        if (!Enum.IsDefined(requested))
            throw new ArgumentException("El modo del producto no es valido.", nameof(requested));
        if (product.Modo == requested) return;
        if (requested != ModoProducto.POR_VARIANTES && product.Skus.Count > 1)
            throw new InvalidOperationException("No se puede salir de POR_VARIANTES mientras existan varios SKU.");
    }
}
