using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class PurchaseService : IPurchaseService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly IPurchaseQueryService _queryService;

    [ActivatorUtilitiesConstructor]
    public PurchaseService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService,
        IPurchaseQueryService queryService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _queryService = queryService;
    }

    internal PurchaseService(
        AppDbContext context,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService,
        IPurchaseQueryService queryService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _queryService = queryService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion de compra.");

    public async Task<PurchaseDetail> CreateAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRA_CREAR, cancellationToken);
        ValidateRequestShape(request.ProveedorId, request.Detalles, request.DescuentoCentavos);

        var usuarioId = GetCurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var supplier = await GetActiveSupplierRequiredAsync(request.ProveedorId, cancellationToken);
        var products = await LoadActiveProductsAsync(request.Detalles, cancellationToken);
        var lineDrafts = BuildLineDrafts(request.Detalles, products);
        var subtotal = lineDrafts.Sum(d => d.SubtotalCentavos);
        ValidatePurchaseDiscount(request.DescuentoCentavos, subtotal);

        var now = DateTime.UtcNow;
        var purchase = new Compra
        {
            CodigoCompra = $"TMP-{Guid.NewGuid():N}"[..36],
            ProveedorId = supplier.ProveedorId,
            UsuarioRegistroId = usuarioId,
            FechaCompra = request.FechaCompra,
            FechaRegistro = now,
            EstadoRecepcion = EstadoRecepcionCompra.PENDIENTE,
            SubtotalCentavos = subtotal,
            DescuentoCentavos = request.DescuentoCentavos,
            TotalCentavos = subtotal - request.DescuentoCentavos,
            EstadoFinanciero = EstadoFinancieroCompra.PENDIENTE,
            TotalPagadoCentavos = 0,
            SaldoPendienteCentavos = subtotal - request.DescuentoCentavos,
            Observaciones = NormalizeOptional(request.Observaciones),
            FechaActualizacion = now
        };

        foreach (var draft in lineDrafts)
        {
            purchase.Detalles.Add(CreateDetail(draft));
        }

        Context.Compras.Add(purchase);
        await Context.SaveChangesAsync(cancellationToken);
        purchase.CodigoCompra = GeneratePurchaseCode(purchase.CompraId);
        var account = new CuentaPorPagar
        {
            CodigoCuenta = $"CPP-{purchase.CompraId:000000}",
            CompraId = purchase.CompraId,
            ProveedorId = supplier.ProveedorId,
            UsuarioCreadorId = usuarioId,
            TotalOriginalCentavos = purchase.TotalCentavos,
            TotalPagadoCentavos = 0,
            SaldoPendienteCentavos = purchase.TotalCentavos,
            FechaCreacion = now,
            FechaVencimiento = request.FechaCompra.Date.AddDays(30),
            Estado = EstadoCuentaPorPagar.ABIERTA,
            FechaActualizacion = now,
            Observaciones = "Cuenta creada automaticamente al registrar la compra. Vencimiento inicial: 30 dias."
        };
        Context.CuentasPorPagar.Add(account);
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CUENTA_PAGAR_CREADA,
                "cuentas_por_pagar",
                account.CuentaPorPagarId,
                $"Cuenta {account.CodigoCuenta} creada para compra {purchase.CodigoCompra} por {FormatCentavos(account.TotalOriginalCentavos)}."),
            cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.COMPRA_CREADA,
                "compras",
                purchase.CompraId,
                $"Compra {purchase.CodigoCompra} registrada para proveedor {supplier.CodigoProveedor}.",
                DatosDespues: BuildPurchaseAuditSnapshot(purchase)),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await _queryService.GetByIdAsync(purchase.CompraId, cancellationToken))!;
    }

    public async Task<PurchaseDetail> UpdateAsync(
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.COMPRA_EDITAR, cancellationToken);
        ValidateRequestShape(request.ProveedorId, request.Detalles, request.DescuentoCentavos);

        var purchase = await Context.Compras
            .Include(c => c.Detalles)
            .Include(c => c.Recepciones)
            .Include(c => c.CuentaPorPagar)
                .ThenInclude(c => c!.Abonos)
            .Include(c => c.Pagos)
            .FirstOrDefaultAsync(c => c.CompraId == request.CompraId, cancellationToken)
            ?? throw new InvalidOperationException("La compra no existe.");

        if (purchase.Recepciones.Count > 0 || purchase.Detalles.Any(d => d.CantidadRecibida > 0))
        {
            throw new InvalidOperationException("No se puede editar una compra que ya tiene recepciones.");
        }

        if (purchase.Pagos.Count > 0 || purchase.CuentaPorPagar?.Abonos.Count > 0 || purchase.TotalPagadoCentavos > 0)
        {
            throw new InvalidOperationException("No se puede editar una compra que ya tiene pagos o abonos registrados.");
        }

        var usuarioId = GetCurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var supplier = await GetActiveSupplierRequiredAsync(request.ProveedorId, cancellationToken);
        var products = await LoadActiveProductsAsync(request.Detalles, cancellationToken);
        var lineDrafts = BuildLineDrafts(request.Detalles, products);
        var subtotal = lineDrafts.Sum(d => d.SubtotalCentavos);
        ValidatePurchaseDiscount(request.DescuentoCentavos, subtotal);
        var before = BuildPurchaseAuditSnapshot(purchase);

        purchase.ProveedorId = supplier.ProveedorId;
        purchase.FechaCompra = request.FechaCompra;
        purchase.SubtotalCentavos = subtotal;
        purchase.DescuentoCentavos = request.DescuentoCentavos;
        purchase.TotalCentavos = subtotal - request.DescuentoCentavos;
        purchase.TotalPagadoCentavos = 0;
        purchase.SaldoPendienteCentavos = purchase.TotalCentavos;
        purchase.EstadoFinanciero = EstadoFinancieroCompra.PENDIENTE;
        purchase.Observaciones = NormalizeOptional(request.Observaciones);
        purchase.FechaActualizacion = DateTime.UtcNow;

        Context.DetalleCompras.RemoveRange(purchase.Detalles);
        purchase.Detalles.Clear();
        foreach (var draft in lineDrafts)
        {
            purchase.Detalles.Add(CreateDetail(draft));
        }

        if (purchase.CuentaPorPagar is not null)
        {
            purchase.CuentaPorPagar.ProveedorId = supplier.ProveedorId;
            purchase.CuentaPorPagar.TotalOriginalCentavos = purchase.TotalCentavos;
            purchase.CuentaPorPagar.TotalPagadoCentavos = 0;
            purchase.CuentaPorPagar.SaldoPendienteCentavos = purchase.TotalCentavos;
            purchase.CuentaPorPagar.Estado = EstadoCuentaPorPagar.ABIERTA;
            purchase.CuentaPorPagar.FechaActualizacion = DateTime.UtcNow;
        }

        await Context.SaveChangesAsync(cancellationToken);
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.COMPRA_EDITADA,
                "compras",
                purchase.CompraId,
                $"Compra {purchase.CodigoCompra} editada.",
                before,
                BuildPurchaseAuditSnapshot(purchase)),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await _queryService.GetByIdAsync(purchase.CompraId, cancellationToken))!;
    }

    private async Task<Proveedor> GetActiveSupplierRequiredAsync(
        int proveedorId,
        CancellationToken cancellationToken)
    {
        if (proveedorId <= 0)
        {
            throw new ArgumentException("El proveedor es obligatorio.");
        }

        var supplier = await Context.Proveedores
            .FirstOrDefaultAsync(p => p.ProveedorId == proveedorId, cancellationToken)
            ?? throw new InvalidOperationException("El proveedor no existe.");

        if (supplier.Estado != EstadoProveedor.ACTIVO)
        {
            throw new InvalidOperationException("No se pueden registrar compras para un proveedor inactivo.");
        }

        return supplier;
    }

    private async Task<IReadOnlyDictionary<int, Producto>> LoadActiveProductsAsync(
        IReadOnlyList<PurchaseLineRequest> lines,
        CancellationToken cancellationToken)
    {
        var productIds = lines.Select(l => l.ProductoId).Distinct().ToList();
        var products = await Context.Productos
            .Include(p => p.Skus)
            .Where(p => productIds.Contains(p.ProductoId))
            .ToDictionaryAsync(p => p.ProductoId, cancellationToken);

        foreach (var productId in productIds)
        {
            if (!products.TryGetValue(productId, out var product))
            {
                throw new InvalidOperationException($"El producto {productId} no existe.");
            }

            if (product.Estado != EstadoProducto.ACTIVO)
            {
                throw new InvalidOperationException($"El producto '{product.Nombre}' no esta activo.");
            }

            if (product.Skus.Count == 0)
            {
                var sku = await InventoryStockEngine.GetOrCreatePrimarySkuAsync(
                    Context, product.ProductoId, cancellationToken);
                if (!product.Skus.Contains(sku))
                    product.Skus.Add(sku);
            }
        }

        return products;
    }

    private static IReadOnlyList<PurchaseLineDraft> BuildLineDrafts(
        IReadOnlyList<PurchaseLineRequest> lines,
        IReadOnlyDictionary<int, Producto> products)
    {
        var drafts = new List<PurchaseLineDraft>();
        foreach (var line in lines)
        {
            if (line.CantidadComprada <= 0)
            {
                throw new ArgumentException("La cantidad comprada debe ser mayor a cero.");
            }

            if (line.CostoUnitarioCentavos < 0)
            {
                throw new ArgumentException("El costo unitario no puede ser negativo.");
            }

            if (line.DescuentoLineaCentavos < 0)
            {
                throw new ArgumentException("El descuento de linea no puede ser negativo.");
            }

            var gross = checked(line.CantidadComprada * line.CostoUnitarioCentavos);
            if (line.DescuentoLineaCentavos > gross)
            {
                throw new ArgumentException("El descuento de linea no puede superar el subtotal de la linea.");
            }

            var product = products[line.ProductoId];
            var sku = ResolvePurchaseSku(product, line.ProductoSkuId);
            drafts.Add(new PurchaseLineDraft(
                product,
                sku,
                line.CantidadComprada,
                line.CostoUnitarioCentavos,
                line.DescuentoLineaCentavos,
                gross - line.DescuentoLineaCentavos,
                NormalizeOptional(line.Observaciones)));
        }

        var duplicatedSku = drafts.GroupBy(draft => draft.Sku.ProductoSkuId > 0
                ? $"id:{draft.Sku.ProductoSkuId}"
                : $"new:{draft.Producto.ProductoId}:{draft.Sku.CodigoSku}",
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatedSku is not null)
            throw new InvalidOperationException("No se puede repetir el mismo SKU dentro de la compra.");

        return drafts;
    }

    private static ProductoSku ResolvePurchaseSku(Producto product, int? requestedSkuId)
    {
        var active = product.Skus.Where(sku => sku.Activo).ToList();
        if (requestedSkuId.HasValue)
        {
            return active.SingleOrDefault(sku => sku.ProductoSkuId == requestedSkuId.Value)
                ?? throw new InvalidOperationException("El SKU seleccionado no pertenece al producto o esta inactivo.");
        }

        if (product.Modo == ModoProducto.POR_VARIANTES && active.Count > 1)
            throw new InvalidOperationException($"Debe seleccionar el SKU exacto para '{product.Nombre}'.");

        return active.FirstOrDefault(sku => sku.EsPrincipal)
            ?? active.SingleOrDefault()
            ?? throw new InvalidOperationException($"El producto '{product.Nombre}' no tiene un SKU activo.");
    }

    private static DetalleCompra CreateDetail(PurchaseLineDraft draft)
    {
        return new DetalleCompra
        {
            ProductoId = draft.Producto.ProductoId,
            ProductoSku = draft.Sku,
            CodigoProductoSnapshot = draft.Producto.CodigoInterno,
            NombreProductoSnapshot = draft.Producto.Nombre,
            CodigoSkuSnapshot = draft.Sku.CodigoSku,
            NombreVarianteSnapshot = draft.Sku.NombreVariante,
            CantidadComprada = draft.CantidadComprada,
            CantidadRecibida = 0,
            CostoUnitarioCentavos = draft.CostoUnitarioCentavos,
            DescuentoLineaCentavos = draft.DescuentoLineaCentavos,
            SubtotalCentavos = draft.SubtotalCentavos,
            Observaciones = draft.Observaciones
        };
    }

    private static void ValidateRequestShape(
        int proveedorId,
        IReadOnlyList<PurchaseLineRequest> lines,
        long discountCentavos)
    {
        if (proveedorId <= 0)
        {
            throw new ArgumentException("El proveedor es obligatorio.");
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("La compra debe tener al menos un producto.");
        }

        if (discountCentavos < 0)
        {
            throw new ArgumentException("El descuento de compra no puede ser negativo.");
        }
    }

    private static void ValidatePurchaseDiscount(long discountCentavos, long subtotalCentavos)
    {
        if (discountCentavos > subtotalCentavos)
        {
            throw new ArgumentException("El descuento de compra no puede superar el subtotal.");
        }
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    }

    private static string GeneratePurchaseCode(int id)
    {
        return $"CMP-{id:000000}";
    }

    private static string BuildPurchaseAuditSnapshot(Compra purchase)
    {
        return string.Join("; ", [
            $"Codigo={purchase.CodigoCompra}",
            $"ProveedorId={purchase.ProveedorId}",
            $"Fecha={purchase.FechaCompra:yyyy-MM-dd}",
            $"EstadoRecepcion={purchase.EstadoRecepcion}",
            $"Subtotal={purchase.SubtotalCentavos}",
            $"Descuento={purchase.DescuentoCentavos}",
            $"Total={purchase.TotalCentavos}",
            $"EstadoFinanciero={purchase.EstadoFinanciero}",
            $"Pagado={purchase.TotalPagadoCentavos}",
            $"Saldo={purchase.SaldoPendienteCentavos}",
            $"Productos={purchase.Detalles.Count}"
        ]);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FormatCentavos(long centavos) => (centavos / 100m).ToString("$0.00");

    private sealed record PurchaseLineDraft(
        Producto Producto,
        ProductoSku Sku,
        int CantidadComprada,
        long CostoUnitarioCentavos,
        long DescuentoLineaCentavos,
        long SubtotalCentavos,
        string? Observaciones);
}
