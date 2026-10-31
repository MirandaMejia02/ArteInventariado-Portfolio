using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SupplierService : ISupplierService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly ISupplierQueryService _queryService;

    [ActivatorUtilitiesConstructor]
    public SupplierService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService,
        ISupplierQueryService queryService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _queryService = queryService;
    }

    internal SupplierService(AppDbContext context, ICurrentSession currentSession, IEmployeePermissionService permissionService, IAuditService auditService, ISupplierQueryService queryService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
        _queryService = queryService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion de proveedor.");

    public async Task<SupplierDetail> CreateAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDOR_CREAR, cancellationToken);
        ValidateRequest(request);

        var duplicates = await FindDuplicatesInternalAsync(request, cancellationToken);
        EnsureDuplicatesCanProceed(duplicates, request.ConfirmDuplicateWarning);

        var usuarioId = GetCurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var values = BuildNormalizedValues(request);
        var supplier = new Proveedor
        {
            CodigoProveedor = $"TMP-{Guid.NewGuid():N}"[..36],
            Tipo = request.Tipo,
            NombreRazonSocial = values.NombreRazonSocial,
            NombreNormalizado = values.NombreNormalizado,
            NombreContacto = values.NombreContacto,
            Telefono = values.Telefono,
            TelefonoNormalizado = values.TelefonoNormalizado,
            WhatsApp = values.WhatsApp,
            WhatsAppNormalizado = values.WhatsAppNormalizado,
            Correo = values.Correo,
            CorreoNormalizado = values.CorreoNormalizado,
            Direccion = values.Direccion,
            DocumentoIdentidad = values.DocumentoIdentidad,
            DocumentoIdentidadNormalizado = values.DocumentoIdentidadNormalizado,
            DocumentoFiscal = values.DocumentoFiscal,
            DocumentoFiscalNormalizado = values.DocumentoFiscalNormalizado,
            Notas = values.Notas,
            Estado = EstadoProveedor.ACTIVO,
            FechaCreacion = now,
            FechaActualizacion = now,
            UsuarioCreacionId = usuarioId,
            UsuarioActualizacionId = usuarioId
        };

        Context.Proveedores.Add(supplier);
        await Context.SaveChangesAsync(cancellationToken);
        supplier.CodigoProveedor = GenerateCode(supplier.Tipo, supplier.ProveedorId);
        await Context.SaveChangesAsync(cancellationToken);

        await RegisterAuditAsync(
            usuarioId,
            TipoBitacora.PROVEEDOR_CREADO,
            supplier.ProveedorId,
            $"Proveedor {supplier.CodigoProveedor} creado.",
            datosDespues: BuildAuditSnapshot(supplier),
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await _queryService.GetByIdAsync(supplier.ProveedorId, cancellationToken))!;
    }

    public async Task<SupplierDetail> UpdateAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDOR_EDITAR, cancellationToken);
        ValidateRequest(request);

        if (request.ProveedorId is null)
        {
            throw new ArgumentException("El proveedor a editar es obligatorio.", nameof(request));
        }

        var supplier = await Context.Proveedores
            .FirstOrDefaultAsync(p => p.ProveedorId == request.ProveedorId.Value, cancellationToken)
            ?? throw new InvalidOperationException("El proveedor no existe.");

        var duplicates = await FindDuplicatesInternalAsync(request, cancellationToken);
        EnsureDuplicatesCanProceed(duplicates, request.ConfirmDuplicateWarning);

        var before = BuildAuditSnapshot(supplier);
        var usuarioId = GetCurrentUserId();
        var values = BuildNormalizedValues(request);
        var changed =
            supplier.Tipo != request.Tipo ||
            supplier.NombreRazonSocial != values.NombreRazonSocial ||
            supplier.NombreNormalizado != values.NombreNormalizado ||
            supplier.NombreContacto != values.NombreContacto ||
            supplier.Telefono != values.Telefono ||
            supplier.TelefonoNormalizado != values.TelefonoNormalizado ||
            supplier.WhatsApp != values.WhatsApp ||
            supplier.WhatsAppNormalizado != values.WhatsAppNormalizado ||
            supplier.Correo != values.Correo ||
            supplier.CorreoNormalizado != values.CorreoNormalizado ||
            supplier.Direccion != values.Direccion ||
            supplier.DocumentoIdentidad != values.DocumentoIdentidad ||
            supplier.DocumentoIdentidadNormalizado != values.DocumentoIdentidadNormalizado ||
            supplier.DocumentoFiscal != values.DocumentoFiscal ||
            supplier.DocumentoFiscalNormalizado != values.DocumentoFiscalNormalizado ||
            supplier.Notas != values.Notas;

        if (!changed)
        {
            return (await _queryService.GetByIdAsync(supplier.ProveedorId, cancellationToken))!;
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        supplier.Tipo = request.Tipo;
        supplier.NombreRazonSocial = values.NombreRazonSocial;
        supplier.NombreNormalizado = values.NombreNormalizado;
        supplier.NombreContacto = values.NombreContacto;
        supplier.Telefono = values.Telefono;
        supplier.TelefonoNormalizado = values.TelefonoNormalizado;
        supplier.WhatsApp = values.WhatsApp;
        supplier.WhatsAppNormalizado = values.WhatsAppNormalizado;
        supplier.Correo = values.Correo;
        supplier.CorreoNormalizado = values.CorreoNormalizado;
        supplier.Direccion = values.Direccion;
        supplier.DocumentoIdentidad = values.DocumentoIdentidad;
        supplier.DocumentoIdentidadNormalizado = values.DocumentoIdentidadNormalizado;
        supplier.DocumentoFiscal = values.DocumentoFiscal;
        supplier.DocumentoFiscalNormalizado = values.DocumentoFiscalNormalizado;
        supplier.Notas = values.Notas;
        supplier.FechaActualizacion = DateTime.UtcNow;
        supplier.UsuarioActualizacionId = usuarioId;

        await Context.SaveChangesAsync(cancellationToken);
        await RegisterAuditAsync(
            usuarioId,
            TipoBitacora.PROVEEDOR_EDITADO,
            supplier.ProveedorId,
            $"Proveedor {supplier.CodigoProveedor} editado.",
            datosAntes: before,
            datosDespues: BuildAuditSnapshot(supplier),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await _queryService.GetByIdAsync(supplier.ProveedorId, cancellationToken))!;
    }

    public async Task SetStateAsync(
        SupplierStateChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureCurrentAdminAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo es obligatorio.", nameof(request));
        }

        var supplier = await Context.Proveedores
            .FirstOrDefaultAsync(p => p.ProveedorId == request.ProveedorId, cancellationToken)
            ?? throw new InvalidOperationException("El proveedor no existe.");

        if (supplier.Estado == request.Estado)
        {
            return;
        }

        var usuarioId = GetCurrentUserId();
        var previous = supplier.Estado;
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        supplier.Estado = request.Estado;
        supplier.FechaActualizacion = DateTime.UtcNow;
        supplier.UsuarioActualizacionId = usuarioId;
        supplier.MotivoDesactivacion = request.Estado == EstadoProveedor.INACTIVO
            ? request.Motivo.Trim()
            : null;
        supplier.FechaDesactivacion = request.Estado == EstadoProveedor.INACTIVO
            ? DateTime.UtcNow
            : null;

        await Context.SaveChangesAsync(cancellationToken);
        await RegisterAuditAsync(
            usuarioId,
            request.Estado == EstadoProveedor.ACTIVO ? TipoBitacora.PROVEEDOR_ACTIVADO : TipoBitacora.PROVEEDOR_DESACTIVADO,
            supplier.ProveedorId,
            $"Proveedor {supplier.CodigoProveedor} cambio de {previous} a {request.Estado}. Motivo: {request.Motivo.Trim()}",
            datosAntes: $"Estado={previous}",
            datosDespues: $"Estado={request.Estado}",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SupplierDuplicateCheckResult> FindDuplicatesAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDORES_ACCEDER, cancellationToken);
        ValidateRequest(request);
        return await FindDuplicatesInternalAsync(request, cancellationToken);
    }

    public async Task<SupplierProductSummary> AssociateProductAsync(
        SupplierProductAssociationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR, cancellationToken);
        ValidateAssociationRequest(request);
        var usuarioId = GetCurrentUserId();

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var supplier = await GetSupplierRequiredAsync(request.ProveedorId, cancellationToken);
        var productExists = await Context.Productos
            .AnyAsync(p => p.ProductoId == request.ProductoId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        var association = await Context.ProductoProveedores
            .FirstOrDefaultAsync(pp => pp.ProveedorId == request.ProveedorId && pp.ProductoId == request.ProductoId, cancellationToken);

        if (request.EsProveedorPrincipal)
        {
            await ClearOtherMainSuppliersAsync(
                request.ProductoId,
                association?.ProductoProveedorId ?? 0,
                cancellationToken);

            await Context.SaveChangesAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        if (association is null)
        {
            association = new ProductoProveedor
            {
                ProveedorId = request.ProveedorId,
                ProductoId = request.ProductoId,
                FechaCreacion = now
            };
            Context.ProductoProveedores.Add(association);
        }

        association.CodigoProductoProveedor = NormalizeOptional(request.CodigoProductoProveedor);
        association.EsProveedorPrincipal = request.EsProveedorPrincipal;
        association.Notas = NormalizeOptional(request.Notas);
        association.Activo = true;
        association.FechaActualizacion = now;
        await Context.SaveChangesAsync(cancellationToken);

        supplier.FechaActualizacion = now;
        supplier.UsuarioActualizacionId = usuarioId;
        await Context.SaveChangesAsync(cancellationToken);

        await RegisterAuditAsync(
            usuarioId,
            TipoBitacora.PROVEEDOR_PRODUCTO_ASOCIADO,
            supplier.ProveedorId,
            $"Producto {request.ProductoId} asociado a proveedor {supplier.CodigoProveedor}. Principal: {(association.EsProveedorPrincipal ? "Si" : "No")}.",
            datosDespues: $"ProductoId={request.ProductoId}; Principal={association.EsProveedorPrincipal}",
            cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await _queryService.GetProductsAsync(request.ProveedorId, cancellationToken))
            .First(p => p.ProductoProveedorId == association.ProductoProveedorId);
    }

    public async Task DeactivateProductAssociationAsync(
        int productoProveedorId,
        string motivo,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR, cancellationToken);
        if (productoProveedorId <= 0)
        {
            throw new ArgumentException("La asociacion es obligatoria.", nameof(productoProveedorId));
        }

        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));
        }

        var usuarioId = GetCurrentUserId();
        var association = await Context.ProductoProveedores
            .Include(pp => pp.Proveedor)
            .FirstOrDefaultAsync(pp => pp.ProductoProveedorId == productoProveedorId, cancellationToken)
            ?? throw new InvalidOperationException("La asociacion no existe.");

        if (!association.Activo)
        {
            return;
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        association.Activo = false;
        association.EsProveedorPrincipal = false;
        association.FechaActualizacion = DateTime.UtcNow;
        association.Proveedor.FechaActualizacion = association.FechaActualizacion;
        association.Proveedor.UsuarioActualizacionId = usuarioId;
        await Context.SaveChangesAsync(cancellationToken);

        await RegisterAuditAsync(
            usuarioId,
            TipoBitacora.PROVEEDOR_PRODUCTO_DESASOCIADO,
            association.ProveedorId,
            $"Producto {association.ProductoId} desasociado de proveedor {association.Proveedor.CodigoProveedor}. Motivo: {motivo.Trim()}",
            datosAntes: $"ProductoId={association.ProductoId}; Activo=True",
            datosDespues: $"ProductoId={association.ProductoId}; Activo=False",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetMainProductSupplierAsync(
        SupplierProductMainRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDOR_PRODUCTOS_GESTIONAR, cancellationToken);

        var usuarioId = GetCurrentUserId();
        var association = await Context.ProductoProveedores
            .Include(pp => pp.Proveedor)
            .FirstOrDefaultAsync(pp => pp.ProductoProveedorId == request.ProductoProveedorId, cancellationToken)
            ?? throw new InvalidOperationException("La asociacion no existe.");

        if (!association.Activo)
        {
            throw new InvalidOperationException("No se puede marcar como principal una asociacion inactiva.");
        }

        if (association.EsProveedorPrincipal == request.EsProveedorPrincipal)
        {
            return;
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        if (request.EsProveedorPrincipal)
        {
            await ClearOtherMainSuppliersAsync(association.ProductoId, association.ProductoProveedorId, cancellationToken);
        }

        association.EsProveedorPrincipal = request.EsProveedorPrincipal;
        association.FechaActualizacion = DateTime.UtcNow;
        association.Proveedor.FechaActualizacion = association.FechaActualizacion;
        association.Proveedor.UsuarioActualizacionId = usuarioId;
        await Context.SaveChangesAsync(cancellationToken);

        await RegisterAuditAsync(
            usuarioId,
            TipoBitacora.PROVEEDOR_PRINCIPAL_CAMBIADO,
            association.ProveedorId,
            $"Proveedor principal de producto {association.ProductoId} actualizado para {association.Proveedor.CodigoProveedor}: {(request.EsProveedorPrincipal ? "Si" : "No")}.",
            datosDespues: $"ProductoId={association.ProductoId}; Principal={association.EsProveedorPrincipal}",
            cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SupplierDuplicateCheckResult> FindDuplicatesInternalAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken)
    {
        var values = BuildNormalizedValues(request);
        var excludedId = request.ProveedorId;
        var candidates = await Context.Proveedores
            .AsNoTracking()
            .Where(p => excludedId == null || p.ProveedorId != excludedId.Value)
            .ToListAsync(cancellationToken);

        var duplicates = new List<SupplierDuplicateCandidate>();
        foreach (var supplier in candidates)
        {
            if (!string.IsNullOrWhiteSpace(values.DocumentoIdentidadNormalizado) &&
                supplier.DocumentoIdentidadNormalizado == values.DocumentoIdentidadNormalizado)
            {
                duplicates.Add(ToDuplicate(supplier, SupplierDuplicateKind.DocumentoIdentidad, blocking: true));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(values.DocumentoFiscalNormalizado) &&
                supplier.DocumentoFiscalNormalizado == values.DocumentoFiscalNormalizado)
            {
                duplicates.Add(ToDuplicate(supplier, SupplierDuplicateKind.DocumentoFiscal, blocking: true));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(values.CorreoNormalizado) &&
                supplier.CorreoNormalizado == values.CorreoNormalizado)
            {
                duplicates.Add(ToDuplicate(supplier, SupplierDuplicateKind.Correo, blocking: false));
            }

            if (!string.IsNullOrWhiteSpace(values.TelefonoNormalizado) &&
                supplier.TelefonoNormalizado == values.TelefonoNormalizado)
            {
                duplicates.Add(ToDuplicate(supplier, SupplierDuplicateKind.Telefono, blocking: false));
            }

            if (!string.IsNullOrWhiteSpace(values.WhatsAppNormalizado) &&
                supplier.WhatsAppNormalizado == values.WhatsAppNormalizado)
            {
                duplicates.Add(ToDuplicate(supplier, SupplierDuplicateKind.WhatsApp, blocking: false));
            }

            if (LooksLikeSimilarName(values.NombreNormalizado, supplier.NombreNormalizado))
            {
                duplicates.Add(ToDuplicate(supplier, SupplierDuplicateKind.NombreParecido, blocking: false));
            }
        }

        return new SupplierDuplicateCheckResult(
            duplicates
                .GroupBy(d => new { d.ProveedorId, d.Kind })
                .Select(g => g.First())
                .OrderByDescending(d => d.Blocking)
                .ThenBy(d => d.NombreRazonSocial)
                .ToList());
    }

    private static SupplierDuplicateCandidate ToDuplicate(
        Proveedor supplier,
        SupplierDuplicateKind kind,
        bool blocking)
    {
        return new SupplierDuplicateCandidate(
            supplier.ProveedorId,
            supplier.CodigoProveedor,
            supplier.NombreRazonSocial,
            supplier.Telefono,
            supplier.WhatsApp,
            supplier.Correo,
            supplier.DocumentoIdentidad,
            supplier.DocumentoFiscal,
            supplier.Estado,
            kind,
            blocking);
    }

    private static void EnsureDuplicatesCanProceed(
        SupplierDuplicateCheckResult duplicates,
        bool confirmedWarning)
    {
        if (duplicates.HasBlockingDuplicate)
        {
            throw new InvalidOperationException("Ya existe un proveedor o artesano con el mismo DUI/NIT/documento.");
        }

        if (duplicates.HasWarnings && !confirmedWarning)
        {
            throw new InvalidOperationException("Existen posibles proveedores duplicados. Revise y confirme para continuar.");
        }
    }

    private async Task ClearOtherMainSuppliersAsync(
        int productoId,
        int currentAssociationId,
        CancellationToken cancellationToken)
    {
        var others = await Context.ProductoProveedores
            .Where(pp =>
                pp.ProductoId == productoId &&
                pp.Activo &&
                pp.EsProveedorPrincipal &&
                pp.ProductoProveedorId != currentAssociationId)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            other.EsProveedorPrincipal = false;
            other.FechaActualizacion = DateTime.UtcNow;
        }
    }

    private async Task<Proveedor> GetSupplierRequiredAsync(
        int proveedorId,
        CancellationToken cancellationToken)
    {
        var supplier = await Context.Proveedores
            .FirstOrDefaultAsync(p => p.ProveedorId == proveedorId, cancellationToken)
            ?? throw new InvalidOperationException("El proveedor no existe.");

        if (supplier.Estado != EstadoProveedor.ACTIVO)
        {
            throw new InvalidOperationException("No se puede usar un proveedor inactivo para nuevas operaciones.");
        }

        return supplier;
    }

    private async Task EnsureCurrentAdminAsync(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var isAdmin = await Context.Usuarios
            .AsNoTracking()
            .AnyAsync(u => u.UsuarioId == currentUserId && u.Activo && u.Rol == RolUsuario.ADMIN, cancellationToken);

        if (!isAdmin)
        {
            throw new InvalidOperationException("Solo ADMIN puede activar o desactivar proveedores.");
        }
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    }

    private static void ValidateRequest(SupplierSaveRequest request)
    {
        if (!Enum.IsDefined(request.Tipo) || request.Tipo == 0)
        {
            throw new ArgumentException("El tipo de proveedor es obligatorio.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.NombreRazonSocial))
        {
            throw new ArgumentException("El nombre o razon comercial es obligatorio.", nameof(request));
        }

        ValidateLength(request.NombreRazonSocial, 180, "nombre");
        ValidateLength(request.NombreContacto, 180, "contacto");
        ValidateLength(request.Telefono, 40, "telefono");
        ValidateLength(request.WhatsApp, 40, "WhatsApp");
        ValidateLength(request.Correo, 180, "correo");
        ValidateLength(request.Direccion, 500, "direccion");
        ValidateLength(request.DocumentoIdentidad, 40, "DUI/documento");
        ValidateLength(request.DocumentoFiscal, 40, "NIT/documento fiscal");
        ValidateLength(request.Notas, 1000, "notas");

        if (!string.IsNullOrWhiteSpace(request.Correo) && !request.Correo.Contains('@'))
        {
            throw new ArgumentException("El correo electronico no tiene un formato valido.", nameof(request));
        }
    }

    private static void ValidateAssociationRequest(SupplierProductAssociationRequest request)
    {
        if (request.ProveedorId <= 0)
        {
            throw new ArgumentException("El proveedor es obligatorio.", nameof(request));
        }

        if (request.ProductoId <= 0)
        {
            throw new ArgumentException("El producto es obligatorio.", nameof(request));
        }

        ValidateLength(request.CodigoProductoProveedor, 80, "codigo del proveedor");
        ValidateLength(request.Notas, 500, "notas de asociacion");
    }

    private static void ValidateLength(string? value, int maxLength, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"El campo {fieldName} es demasiado largo.");
        }
    }

    private static SupplierNormalizedValues BuildNormalizedValues(SupplierSaveRequest request)
    {
        return new SupplierNormalizedValues(
            request.NombreRazonSocial.Trim(),
            CustomerTextNormalizer.NormalizeName(request.NombreRazonSocial),
            NormalizeOptional(request.NombreContacto),
            NormalizeOptional(request.Telefono),
            CustomerTextNormalizer.NormalizePhone(request.Telefono),
            NormalizeOptional(request.WhatsApp),
            CustomerTextNormalizer.NormalizePhone(request.WhatsApp),
            NormalizeOptional(request.Correo),
            CustomerTextNormalizer.NormalizeEmail(request.Correo),
            NormalizeOptional(request.Direccion),
            NormalizeOptional(request.DocumentoIdentidad),
            CustomerTextNormalizer.NormalizeDocument(request.DocumentoIdentidad),
            NormalizeOptional(request.DocumentoFiscal),
            CustomerTextNormalizer.NormalizeDocument(request.DocumentoFiscal),
            NormalizeOptional(request.Notas));
    }

    private static bool LooksLikeSimilarName(string candidate, string existing)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(existing))
        {
            return false;
        }

        if (candidate == existing)
        {
            return true;
        }

        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var existingTokens = existing.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (candidateTokens.Count == 0 || existingTokens.Count == 0)
        {
            return false;
        }

        var shared = candidateTokens.Intersect(existingTokens).Count();
        var ratio = shared / (double)Math.Max(candidateTokens.Count, existingTokens.Count);
        return shared >= 2 && ratio >= 0.67;
    }

    private static string GenerateCode(TipoProveedor tipo, int id)
    {
        return tipo switch
        {
            TipoProveedor.ARTESANO => $"ART-{id:000000}",
            _ => $"PRV-{id:000000}"
        };
    }

    private static string BuildAuditSnapshot(Proveedor supplier)
    {
        return string.Join("; ", [
            $"Codigo={supplier.CodigoProveedor}",
            $"Tipo={supplier.Tipo}",
            $"Nombre={supplier.NombreRazonSocial}",
            $"Contacto={supplier.NombreContacto}",
            $"Telefono={supplier.Telefono}",
            $"WhatsApp={supplier.WhatsApp}",
            $"Correo={supplier.Correo}",
            $"DUI={supplier.DocumentoIdentidad}",
            $"NIT={supplier.DocumentoFiscal}",
            $"Estado={supplier.Estado}"
        ]);
    }

    private async Task RegisterAuditAsync(
        int usuarioId,
        TipoBitacora type,
        int proveedorId,
        string description,
        string? datosAntes = null,
        string? datosDespues = null,
        CancellationToken cancellationToken = default)
    {
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                type,
                "proveedores",
                proveedorId,
                description,
                datosAntes,
                datosDespues),
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record SupplierNormalizedValues(
        string NombreRazonSocial,
        string NombreNormalizado,
        string? NombreContacto,
        string? Telefono,
        string? TelefonoNormalizado,
        string? WhatsApp,
        string? WhatsAppNormalizado,
        string? Correo,
        string? CorreoNormalizado,
        string? Direccion,
        string? DocumentoIdentidad,
        string? DocumentoIdentidadNormalizado,
        string? DocumentoFiscal,
        string? DocumentoFiscalNormalizado,
        string? Notas);
}
