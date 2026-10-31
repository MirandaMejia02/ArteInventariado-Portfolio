using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArteInventariado.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private static readonly JsonSerializerOptions AdjustmentJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService? _permissionService;
    private readonly IInstallationContext? _installationContext;
    private readonly IAdminReauthenticationService? _adminReauthenticationService;

    [ActivatorUtilitiesConstructor]
    public InventoryService(IDbContextFactory<AppDbContext> contextFactory, IAuditService auditService,
        ICurrentSession currentSession, IEmployeePermissionService permissionService,
        IInstallationContext installationContext,
        IAdminReauthenticationService adminReauthenticationService)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _installationContext = installationContext;
        _adminReauthenticationService = adminReauthenticationService;
    }

    internal InventoryService(
        AppDbContext context,
        IAuditService auditService,
        ICurrentSession currentSession,
        IInstallationContext? installationContext = null,
        IAdminReauthenticationService? adminReauthenticationService = null)
    {
        _legacyContext = context;
        _auditService = auditService;
        _currentSession = currentSession;
        _installationContext = installationContext;
        _adminReauthenticationService = adminReauthenticationService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para inventario.");

    public async Task<InventoryMovementSummary> AdjustStockAsync(AdjustStockRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        _installationContext?.EnsureOperationalWriteAllowed("inventario.ajuste.legacy");
        throw new InvalidOperationException(
            "El ajuste por producto fue retirado. Use el ajuste por SKU y ubicacion con reautenticacion.");
    }

    public async Task<InventoryAdjustmentResult> AdjustPhysicalCountAsync(
        AdjustInventoryCountRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        _installationContext?.EnsureOperationalWriteAllowed("inventario.ajuste");
        ValidateAdjustmentRequest(request);
        await EnsureAdjustmentAuthorizationAsync(request.ReauthenticationPassword, cancellationToken);

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var existingMovement = await Context.MovimientosInventarioUbicacion
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperacionId == request.OperacionId, cancellationToken);
        if (existingMovement is not null)
        {
            return ExistingAdjustmentResult(existingMovement, request);
        }

        var existingNoOp = await FindExistingNoOpAdjustmentAsync(request, cancellationToken);
        if (existingNoOp is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existingNoOp;
        }

        var userId = RequiredUserId();
        var sku = await Context.ProductoSkus
            .Include(item => item.Producto)
            .Include(item => item.SaldosInventario)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == request.ProductoSkuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");
        var balance = sku.SaldosInventario.SingleOrDefault(item => item.Ubicacion == request.Ubicacion)
            ?? throw new InvalidOperationException("El SKU no tiene un saldo canonico para la ubicacion seleccionada.");

        var previous = balance.ExistenciaFisica;
        var difference = checked(request.CantidadContada - previous);
        var now = DateTime.UtcNow;
        var physicalBefore = sku.SaldosInventario.Sum(item => item.ExistenciaFisica);
        var physicalAfter = checked(physicalBefore + difference);
        var metadata = new AdjustmentOperationMetadata(
            request.OperacionId,
            request.ProductoSkuId,
            sku.CodigoSku,
            request.Ubicacion,
            previous,
            request.CantidadContada,
            difference,
            balance.Reservado,
            request.Motivo.Trim(),
            now);

        if (difference == 0)
        {
            AddAdjustmentAudit(userId, sku, metadata);
            await Context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToAdjustmentResult(metadata, null);
        }

        var movementNote = JsonSerializer.Serialize(metadata, AdjustmentJsonOptions);
        InventoryStockEngine.SetAbsolutePhysical(
            Context,
            sku,
            request.Ubicacion,
            request.CantidadContada,
            userId,
            request.Motivo,
            now,
            request.OperacionId,
            movementNote);

        var legacyMovement = new MovimientoInventario
        {
            ProductoId = sku.ProductoId,
            UsuarioId = userId,
            TipoMovimiento = TipoMovimientoInventario.AJUSTE,
            Cantidad = difference,
            StockAnterior = physicalBefore,
            StockNuevo = physicalAfter,
            Motivo = request.Motivo.Trim(),
            FechaMovimiento = now
        };
        Context.MovimientosInventario.Add(legacyMovement);
        AddAdjustmentAudit(userId, sku, metadata);
        await Context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var savedMovement = await Context.MovimientosInventarioUbicacion
            .AsNoTracking()
            .SingleAsync(item => item.OperacionId == request.OperacionId, cancellationToken);
        return ToAdjustmentResult(metadata, savedMovement.MovimientoInventarioUbicacionId);
    }

    public async Task<IReadOnlyList<InventoryMovementSummary>> GetMovementsByProductAsync(int productoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        return await Context.MovimientosInventario.AsNoTracking().Include(item => item.Producto)
            .Where(item => item.ProductoId == productoId).OrderByDescending(item => item.FechaMovimiento)
            .Select(item => ToSummaryProjection(item)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryBalanceSummary>> GetBalancesByProductAsync(int productoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        return await GetBalancesQuery(productoId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryBalanceSummary>> TransferAsync(TransferInventoryRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWritePermissionAsync(PermisoEmpleado.INVENTARIO_GESTIONAR, cancellationToken);
        ValidateOperationId(request.OperacionId);
        var productId = await RequireProductIdAsync(request.ProductoSkuId, cancellationToken);
        var existing = await Context.MovimientosInventarioUbicacion.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperacionId == request.OperacionId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ProductoSkuId != request.ProductoSkuId ||
                existing.TipoOperacion != TipoOperacionInventario.TRANSFERENCIA ||
                existing.UbicacionOrigen != request.Origen ||
                existing.UbicacionDestino != request.Destino ||
                existing.Cantidad != request.Cantidad)
                throw new InvalidOperationException("El identificador de operacion ya fue usado con otros datos.");
            return await GetBalancesQuery(productId).ToListAsync(cancellationToken);
        }

        var userId = RequiredUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var sku = await InventoryStockEngine.GetSkuAsync(Context, request.ProductoSkuId, cancellationToken);
        InventoryStockEngine.Transfer(Context, sku, request.OperacionId, request.Origen, request.Destino,
            request.Cantidad, userId, request.Motivo, request.Nota, DateTime.UtcNow);
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(userId, TipoBitacora.INVENTARIO_TRANSFERIDO, "movimientos_inventario_ubicacion",
            request.ProductoSkuId, $"Transferencia de {request.Cantidad} '{sku.CodigoSku}' de {request.Origen} a {request.Destino}. Motivo: {request.Motivo.Trim()}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetBalancesQuery(productId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryBalanceSummary>> RegisterWasteAsync(RegisterInventoryWasteRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWritePermissionAsync(PermisoEmpleado.MERMA_REGISTRAR, cancellationToken);
        ValidateOperationId(request.OperacionId);
        var productId = await RequireProductIdAsync(request.ProductoSkuId, cancellationToken);
        var existing = await Context.MovimientosInventarioUbicacion.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperacionId == request.OperacionId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ProductoSkuId != request.ProductoSkuId ||
                existing.TipoOperacion != TipoOperacionInventario.MERMA ||
                existing.UbicacionOrigen != request.Ubicacion ||
                existing.Cantidad != request.Cantidad)
                throw new InvalidOperationException("El identificador de operacion ya fue usado con otros datos.");
            return await GetBalancesQuery(productId).ToListAsync(cancellationToken);
        }

        var userId = RequiredUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var sku = await InventoryStockEngine.GetSkuAsync(Context, request.ProductoSkuId, cancellationToken);
        InventoryStockEngine.RemovePhysical(Context, sku, request.Cantidad, userId,
            TipoOperacionInventario.MERMA, request.Motivo, DateTime.UtcNow,
            request.Ubicacion, request.OperacionId, request.Nota);
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(userId, TipoBitacora.INVENTARIO_MERMA_REGISTRADA, "movimientos_inventario_ubicacion",
            request.ProductoSkuId, $"Merma de {request.Cantidad} '{sku.CodigoSku}' en {request.Ubicacion}. Motivo: {request.Motivo.Trim()}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetBalancesQuery(productId).ToListAsync(cancellationToken);
    }

    public async Task SetExhibitionMinimumAsync(SetExhibitionMinimumRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWritePermissionAsync(PermisoEmpleado.INVENTARIO_GESTIONAR, cancellationToken);
        if (request.MinimoOverride < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "El minimo no puede ser negativo.");
        var userId = RequiredUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var sku = await Context.ProductoSkus.SingleOrDefaultAsync(item => item.ProductoSkuId == request.ProductoSkuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");
        sku.MinimoExhibicionOverride = request.MinimoOverride;
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(userId, TipoBitacora.INVENTARIO_MINIMO_MODIFICADO, "producto_skus", sku.ProductoSkuId,
            $"Minimo de exhibicion: {(request.MinimoOverride?.ToString() ?? "valor por producto")}.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReplenishmentSummary>> GetReplenishmentAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var skus = await Context.ProductoSkus.AsNoTracking().Include(item => item.Producto)
            .Include(item => item.SaldosInventario).Include(item => item.AlertaReposicion)
            .Where(item => item.Activo).OrderBy(item => item.Producto.Nombre).ThenBy(item => item.CodigoSku)
            .ToListAsync(cancellationToken);
        return skus.Select(ToReplenishmentSummary).ToList();
    }

    public async Task PostponeReplenishmentAsync(PostponeReplenishmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PospuestaHasta <= DateTime.UtcNow)
            throw new ArgumentException("La fecha para posponer debe ser futura.", nameof(request));
        await UpdateReplenishmentDecisionAsync(request.ProductoSkuId, EstadoAlertaReposicion.POSPUESTA,
            request.PospuestaHasta, null, TipoBitacora.REPOSICION_POSPUESTA, cancellationToken);
    }

    public async Task DismissReplenishmentAsync(DismissReplenishmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Motivo))
            throw new ArgumentException("El motivo para descartar la alerta es obligatorio.", nameof(request));
        await UpdateReplenishmentDecisionAsync(request.ProductoSkuId, EstadoAlertaReposicion.DESCARTADA,
            null, request.Motivo.Trim(), TipoBitacora.REPOSICION_DESCARTADA, cancellationToken);
    }

    public async Task<IReadOnlyList<StagnantInventorySummary>> GetStagnantInventoryAsync(StagnantInventoryQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        if (query.DiasSinMovimiento <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "Los dias deben ser mayores que cero.");
        var cutoff = query.FechaCorte ?? DateTime.UtcNow;
        var skus = await Context.ProductoSkus.AsNoTracking().Include(item => item.Producto)
            .Include(item => item.SaldosInventario).Include(item => item.MovimientosInventario)
            .Where(item => item.Activo).ToListAsync(cancellationToken);
        var productIds = skus.Select(item => item.ProductoId).Distinct().ToList();
        var lastSales = await Context.DetalleVentas.AsNoTracking()
            .Where(item => productIds.Contains(item.ProductoId))
            .GroupBy(item => item.ProductoId)
            .Select(group => new
            {
                ProductId = group.Key,
                LastSale = group.Max(item => item.Venta.FechaVenta)
            })
            .ToDictionaryAsync(item => item.ProductId, item => item.LastSale, cancellationToken);
        return skus.Select(sku =>
            {
                var physical = sku.SaldosInventario.Sum(balance => balance.ExistenciaFisica);
                var lastMovement = sku.MovimientosInventario.Select(item => (DateTime?)item.Fecha).Max();
                var lastSale = lastSales.TryGetValue(sku.ProductoId, out var saleDate)
                    ? saleDate
                    : (DateTime?)null;
                var last = new[] { lastMovement, lastSale, sku.Producto.FechaCreacion }
                    .Where(item => item.HasValue)
                    .Max()!.Value;
                var days = Math.Max(0, (int)(cutoff - last).TotalDays);
                return new StagnantInventorySummary(sku.ProductoSkuId, sku.ProductoId, sku.CodigoSku,
                    sku.Producto.Nombre, physical, last, days, physical > 0 && days >= query.DiasSinMovimiento);
            }).Where(item => item.RequiereRevision).OrderByDescending(item => item.DiasSinMovimiento)
            .ThenBy(item => item.ProductoNombre).ToList();
    }

    public async Task<IReadOnlyList<InventoryReconciliationSummary>> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var products = await Context.Productos.AsNoTracking().Select(product => new
        {
            product.ProductoId, product.StockActual,
            Canonical = product.Skus.SelectMany(sku => sku.SaldosInventario).Sum(balance => (int?)balance.ExistenciaFisica) ?? 0
        }).OrderBy(item => item.ProductoId).ToListAsync(cancellationToken);
        return products.Select(item => new InventoryReconciliationSummary(item.ProductoId, item.Canonical,
            item.StockActual, item.Canonical == item.StockActual)).ToList();
    }

    private async Task UpdateReplenishmentDecisionAsync(int skuId, EstadoAlertaReposicion state,
        DateTime? postponedUntil, string? dismissReason, TipoBitacora auditType, CancellationToken cancellationToken)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureWritePermissionAsync(PermisoEmpleado.INVENTARIO_GESTIONAR, cancellationToken);
        var userId = RequiredUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var sku = await Context.ProductoSkus
            .Include(item => item.Producto)
            .Include(item => item.SaldosInventario)
            .Include(item => item.AlertaReposicion)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == skuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");
        if (ToReplenishmentSummary(sku).Estado != EstadoDisponibilidadInventario.REQUIERE_REPOSICION)
            throw new InvalidOperationException("El SKU no requiere reposicion actualmente.");
        var now = DateTime.UtcNow;
        var alert = sku.AlertaReposicion ?? new AlertaReposicionInventario { ProductoSku = sku, FechaCreacion = now };
        alert.Estado = state;
        alert.PospuestaHasta = postponedUntil;
        alert.MotivoDescarte = dismissReason;
        alert.UsuarioActualizacionId = userId;
        alert.FechaActualizacion = now;
        if (sku.AlertaReposicion is null) Context.AlertasReposicionInventario.Add(alert);
        await Context.SaveChangesAsync(cancellationToken);
        await AuditAsync(userId, auditType, "alertas_reposicion_inventario", alert.AlertaReposicionInventarioId,
            state == EstadoAlertaReposicion.POSPUESTA
                ? $"Reposicion del SKU {sku.CodigoSku} pospuesta hasta {postponedUntil:O}."
                : $"Reposicion del SKU {sku.CodigoSku} descartada. Motivo: {dismissReason}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<InventoryBalanceSummary> GetBalancesQuery(int productId) =>
        Context.InventarioSaldos.AsNoTracking().Where(balance => balance.ProductoSku.ProductoId == productId)
            .OrderBy(balance => balance.ProductoSku.CodigoSku).ThenBy(balance => balance.Ubicacion)
            .Select(balance => new InventoryBalanceSummary(balance.ProductoSkuId, balance.ProductoSku.ProductoId,
                balance.ProductoSku.CodigoSku, balance.ProductoSku.Producto.Nombre, balance.Ubicacion,
                balance.ExistenciaFisica, balance.Reservado, balance.ExistenciaFisica - balance.Reservado, balance.Version));

    private static ReplenishmentSummary ToReplenishmentSummary(ProductoSku sku)
    {
        var physical = sku.SaldosInventario.Sum(item => item.ExistenciaFisica);
        var reserved = sku.SaldosInventario.Sum(item => item.Reservado);
        var available = physical - reserved;
        var exhibition = sku.SaldosInventario.Where(item => item.Ubicacion == UbicacionInventario.EXHIBICION).Sum(item => item.Disponible);
        var warehouse = sku.SaldosInventario.Where(item => item.Ubicacion == UbicacionInventario.BODEGA).Sum(item => item.Disponible);
        var minimum = sku.MinimoExhibicionOverride ?? sku.Producto.StockMinimo;
        var state = physical == 0 ? EstadoDisponibilidadInventario.SIN_EXISTENCIAS
            : available == 0 ? EstadoDisponibilidadInventario.SIN_DISPONIBILIDAD
            : exhibition <= minimum && warehouse > 0 ? EstadoDisponibilidadInventario.REQUIERE_REPOSICION
            : EstadoDisponibilidadInventario.DISPONIBLE;
        var decision = sku.AlertaReposicion?.Estado;
        if (decision == EstadoAlertaReposicion.POSPUESTA && sku.AlertaReposicion!.PospuestaHasta <= DateTime.UtcNow)
            decision = EstadoAlertaReposicion.PENDIENTE;
        return new(sku.ProductoSkuId, sku.ProductoId, sku.CodigoSku, sku.Producto.Nombre, minimum,
            physical, reserved, available, exhibition, warehouse, state,
            state == EstadoDisponibilidadInventario.REQUIERE_REPOSICION
                ? decision ?? EstadoAlertaReposicion.PENDIENTE
                : EstadoAlertaReposicion.RESUELTA,
            sku.AlertaReposicion?.PospuestaHasta, sku.AlertaReposicion?.MotivoDescarte);
    }

    private async Task EnsureWritePermissionAsync(PermisoEmpleado permission, CancellationToken cancellationToken)
    {
        if (_permissionService is not null)
        {
            await _permissionService.EnsurePermissionAsync(permission, cancellationToken);
            return;
        }
        var userId = RequiredUserId();
        var allowed = await Context.Usuarios.AsNoTracking().Where(user => user.UsuarioId == userId && user.Activo)
            .AnyAsync(user => user.Rol == RolUsuario.ADMIN || user.Rol == RolUsuario.EMPLEADO &&
                user.Permisos.Any(item => item.Permiso == permission && item.Habilitado), cancellationToken);
        if (!allowed) throw new InvalidOperationException("No tiene permiso para modificar inventario.");
    }

    private int RequiredUserId() => _currentSession.UsuarioActual?.UsuarioId
        ?? throw new InvalidOperationException("Debe existir una sesion activa para operar inventario.");

    private Task<int> RequireProductIdAsync(int skuId, CancellationToken cancellationToken) =>
        Context.ProductoSkus.Where(item => item.ProductoSkuId == skuId).Select(item => item.ProductoId)
            .SingleAsync(cancellationToken);

    private Task AuditAsync(int userId, TipoBitacora type, string entity, int entityId, string detail,
        CancellationToken cancellationToken) => _auditService.RegistrarAsync(
            new AuditEntryRequest(userId, type, entity, entityId, detail), cancellationToken);

    private async Task<InventoryMovementSummary> GetMovementSummaryRequiredAsync(int movementId,
        CancellationToken cancellationToken) => await Context.MovimientosInventario.AsNoTracking()
        .Include(item => item.Producto).Where(item => item.MovimientoInventarioId == movementId)
        .Select(item => ToSummaryProjection(item)).FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("El movimiento de inventario no existe.");

    private async Task EnsureAdjustmentAuthorizationAsync(
        string password,
        CancellationToken cancellationToken)
    {
        await EnsureWritePermissionAsync(PermisoEmpleado.INVENTARIO_GESTIONAR, cancellationToken);
        var current = _currentSession.UsuarioActual;
        if (current is null || current.Rol != RolUsuario.ADMIN)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede ajustar existencias.");
        }

        if (_adminReauthenticationService is null)
        {
            throw new InvalidOperationException("La reautenticacion protegida no esta disponible.");
        }

        var verifiedUserId = await _adminReauthenticationService.VerifyCurrentAdminAsync(password, cancellationToken);
        if (verifiedUserId != current.UsuarioId)
        {
            throw new InvalidOperationException("La reautenticacion no corresponde al usuario de la sesion.");
        }
    }

    private static void ValidateAdjustmentRequest(AdjustInventoryCountRequest request)
    {
        ValidateOperationId(request.OperacionId);
        if (request.ProductoSkuId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ProductoSkuId));
        }

        if (request.Ubicacion is not UbicacionInventario.EXHIBICION and
            not UbicacionInventario.BODEGA and
            not UbicacionInventario.SIN_ASIGNAR)
        {
            throw new ArgumentException("La ubicacion del ajuste no es valida.", nameof(request));
        }

        if (request.CantidadContada < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.CantidadContada),
                "La cantidad contada no puede ser negativa.");
        }

        if (string.IsNullOrWhiteSpace(request.Motivo) || request.Motivo.Trim().Length > 500)
        {
            throw new ArgumentException("El motivo es obligatorio y no puede superar 500 caracteres.", nameof(request));
        }
    }

    private async Task<InventoryAdjustmentResult?> FindExistingNoOpAdjustmentAsync(
        AdjustInventoryCountRequest request,
        CancellationToken cancellationToken)
    {
        var operationText = request.OperacionId.ToString();
        var snapshots = await Context.Bitacora
            .AsNoTracking()
            .Where(item => item.TipoAccion == TipoBitacora.INVENTARIO_AJUSTADO &&
                item.DatosDespues != null && item.DatosDespues.Contains(operationText))
            .OrderByDescending(item => item.BitacoraId)
            .Select(item => item.DatosDespues!)
            .ToListAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<AdjustmentOperationMetadata>(snapshot, AdjustmentJsonOptions);
                if (metadata?.OperacionId == request.OperacionId)
                {
                    ValidateAdjustmentPayload(metadata, request);
                    return ToAdjustmentResult(metadata, null);
                }
            }
            catch (JsonException)
            {
                // Las entradas antiguas de auditoria no son operaciones de este contrato.
            }
        }

        return null;
    }

    private static InventoryAdjustmentResult ExistingAdjustmentResult(
        MovimientoInventarioUbicacion existing,
        AdjustInventoryCountRequest request)
    {
        if (existing.TipoOperacion != TipoOperacionInventario.AJUSTE ||
            existing.ProductoSkuId != request.ProductoSkuId ||
            (existing.UbicacionOrigen != request.Ubicacion &&
             existing.UbicacionDestino != request.Ubicacion) ||
            string.IsNullOrWhiteSpace(existing.Nota))
        {
            throw new InvalidOperationException("El identificador de operacion ya fue usado con datos distintos.");
        }

        AdjustmentOperationMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<AdjustmentOperationMetadata>(existing.Nota!, AdjustmentJsonOptions);
        }
        catch (JsonException)
        {
            metadata = null;
        }

        if (metadata is null)
            throw new InvalidOperationException("El identificador de operacion ya fue usado con datos distintos.");

        ValidateAdjustmentPayload(metadata, request);

        return ToAdjustmentResult(metadata, existing.MovimientoInventarioUbicacionId);
    }

    private static void ValidateAdjustmentPayload(
        AdjustmentOperationMetadata metadata,
        AdjustInventoryCountRequest request)
    {
        if (metadata.OperacionId != request.OperacionId ||
            metadata.ProductoSkuId != request.ProductoSkuId ||
            metadata.Ubicacion != request.Ubicacion ||
            metadata.CantidadContada != request.CantidadContada ||
            !string.Equals(metadata.Motivo, request.Motivo.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("El identificador de operacion ya fue usado con datos distintos.");
        }
    }

    private void AddAdjustmentAudit(
        int userId,
        ProductoSku sku,
        AdjustmentOperationMetadata metadata)
    {
        var before = JsonSerializer.Serialize(new
        {
            metadata.OperacionId,
            metadata.ProductoSkuId,
            metadata.CodigoSku,
            metadata.Ubicacion,
            ExistenciaFisica = metadata.ExistenciaAnterior,
            metadata.Reservado,
            Disponible = metadata.ExistenciaAnterior - metadata.Reservado,
            metadata.Motivo
        });
        var after = JsonSerializer.Serialize(metadata, AdjustmentJsonOptions);
        Context.Bitacora.Add(AuditEntryFactory.Create(new AuditEntryRequest(
            userId,
            TipoBitacora.INVENTARIO_AJUSTADO,
            "inventario_saldos",
            sku.ProductoSkuId,
            $"Ajuste de conteo de '{sku.CodigoSku}' en {metadata.Ubicacion}: " +
            $"{metadata.ExistenciaAnterior} -> {metadata.CantidadContada} " +
            $"(diferencia {metadata.Diferencia}). Motivo: {metadata.Motivo}",
            DatosAntes: before,
            DatosDespues: after)));
    }

    private static InventoryAdjustmentResult ToAdjustmentResult(
        AdjustmentOperationMetadata metadata,
        int? movementId) => new(
            metadata.OperacionId,
            metadata.ProductoSkuId,
            metadata.CodigoSku,
            metadata.Ubicacion,
            metadata.ExistenciaAnterior,
            metadata.CantidadContada,
            metadata.Diferencia,
            metadata.Reservado,
            metadata.CantidadContada - metadata.Reservado,
            movementId,
            metadata.Fecha);

    private sealed record AdjustmentOperationMetadata(
        Guid OperacionId,
        int ProductoSkuId,
        string CodigoSku,
        UbicacionInventario Ubicacion,
        int ExistenciaAnterior,
        int CantidadContada,
        int Diferencia,
        int Reservado,
        string Motivo,
        DateTime Fecha);

    private static InventoryMovementSummary ToSummaryProjection(MovimientoInventario item) =>
        new(item.MovimientoInventarioId, item.ProductoId, item.Producto.CodigoInterno, item.Producto.Nombre,
            item.UsuarioId, item.TipoMovimiento, item.Cantidad, item.StockAnterior, item.StockNuevo,
            item.Motivo, item.FechaMovimiento);

    private static void ValidateOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("La operacion requiere un identificador idempotente.", nameof(operationId));
    }
}
