using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class SupplierQueryService : ISupplierQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public SupplierQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _permissionService = permissionService;
    }

    internal SupplierQueryService(AppDbContext context, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar proveedores.");

    public async Task<IReadOnlyList<SupplierSummary>> SearchAsync(
        SupplierFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDORES_ACCEDER, cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 250);
        var query = Context.Proveedores
            .AsNoTracking()
            .AsQueryable();

        if (filter.Tipo.HasValue)
        {
            query = query.Where(p => p.Tipo == filter.Tipo.Value);
        }

        if (filter.Estado.HasValue)
        {
            query = query.Where(p => p.Estado == filter.Estado.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            var normalizedName = CustomerTextNormalizer.NormalizeName(text);
            var normalizedPhone = CustomerTextNormalizer.NormalizePhone(text);
            var normalizedDocument = CustomerTextNormalizer.NormalizeDocument(text);
            var normalizedEmail = CustomerTextNormalizer.NormalizeEmail(text);

            query = query.Where(p =>
                p.CodigoProveedor.Contains(text) ||
                p.NombreRazonSocial.Contains(text) ||
                p.NombreNormalizado.Contains(normalizedName) ||
                (p.NombreContacto != null && p.NombreContacto.Contains(text)) ||
                (normalizedPhone != null && p.TelefonoNormalizado != null && p.TelefonoNormalizado.Contains(normalizedPhone)) ||
                (normalizedPhone != null && p.WhatsAppNormalizado != null && p.WhatsAppNormalizado.Contains(normalizedPhone)) ||
                (normalizedDocument != null && p.DocumentoIdentidadNormalizado != null && p.DocumentoIdentidadNormalizado.Contains(normalizedDocument)) ||
                (normalizedDocument != null && p.DocumentoFiscalNormalizado != null && p.DocumentoFiscalNormalizado.Contains(normalizedDocument)) ||
                (normalizedEmail != null && p.CorreoNormalizado != null && p.CorreoNormalizado.Contains(normalizedEmail)));
        }

        var rows = await query
            .OrderBy(p => p.NombreRazonSocial)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new SupplierSummary(
                p.ProveedorId,
                p.CodigoProveedor,
                p.Tipo,
                p.NombreRazonSocial,
                p.NombreContacto,
                p.Telefono,
                p.DocumentoIdentidad,
                p.DocumentoFiscal,
                p.Estado,
                p.Productos.Count(pp => pp.Activo),
                p.Productos
                    .Where(pp => pp.Activo)
                    .OrderByDescending(pp => pp.FechaUltimaCompra)
                    .Select(pp => pp.FechaUltimaCompra)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<SupplierDetail?> GetByIdAsync(
        int proveedorId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDORES_ACCEDER, cancellationToken);

        var supplier = await Context.Proveedores
            .AsNoTracking()
            .Include(p => p.UsuarioCreacion)
            .Include(p => p.UsuarioActualizacion)
            .Where(p => p.ProveedorId == proveedorId)
            .Select(p => new
            {
                p.ProveedorId,
                p.CodigoProveedor,
                p.Tipo,
                p.NombreRazonSocial,
                p.NombreContacto,
                p.Telefono,
                p.WhatsApp,
                p.Correo,
                p.Direccion,
                p.DocumentoIdentidad,
                p.DocumentoFiscal,
                p.Notas,
                p.Estado,
                p.FechaCreacion,
                p.FechaActualizacion,
                UsuarioCreacion = p.UsuarioCreacion.NombreUsuario,
                UsuarioActualizacion = p.UsuarioActualizacion == null ? null : p.UsuarioActualizacion.NombreUsuario,
                p.MotivoDesactivacion,
                p.FechaDesactivacion
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (supplier is null)
        {
            return null;
        }

        var products = await GetProductsInternalAsync(proveedorId, cancellationToken);
        var history = await _permissionService.HasPermissionAsync(
                PermisoEmpleado.PROVEEDOR_HISTORIAL_VER,
                cancellationToken: cancellationToken)
            ? await GetHistoryInternalAsync(proveedorId, cancellationToken)
            : [];

        return new SupplierDetail(
            supplier.ProveedorId,
            supplier.CodigoProveedor,
            supplier.Tipo,
            supplier.NombreRazonSocial,
            supplier.NombreContacto,
            supplier.Telefono,
            supplier.WhatsApp,
            supplier.Correo,
            supplier.Direccion,
            supplier.DocumentoIdentidad,
            supplier.DocumentoFiscal,
            supplier.Notas,
            supplier.Estado,
            supplier.FechaCreacion,
            supplier.FechaActualizacion,
            supplier.UsuarioCreacion,
            supplier.UsuarioActualizacion,
            supplier.MotivoDesactivacion,
            supplier.FechaDesactivacion,
            products,
            history);
    }

    public async Task<IReadOnlyList<SupplierProductSummary>> GetProductsAsync(
        int proveedorId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDORES_ACCEDER, cancellationToken);
        return await GetProductsInternalAsync(proveedorId, cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierHistoryEntry>> GetHistoryAsync(
        int proveedorId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.PROVEEDOR_HISTORIAL_VER, cancellationToken);
        return await GetHistoryInternalAsync(proveedorId, cancellationToken);
    }

    private async Task<IReadOnlyList<SupplierProductSummary>> GetProductsInternalAsync(
        int proveedorId,
        CancellationToken cancellationToken)
    {
        return await Context.ProductoProveedores
            .AsNoTracking()
            .Include(pp => pp.Producto)
            .ThenInclude(p => p.Categoria)
            .Include(pp => pp.Producto)
            .ThenInclude(p => p.Subcategoria)
            .Where(pp => pp.ProveedorId == proveedorId)
            .OrderByDescending(pp => pp.Activo)
            .ThenBy(pp => pp.Producto.Nombre)
            .Select(pp => new SupplierProductSummary(
                pp.ProductoProveedorId,
                pp.ProductoId,
                pp.Producto.CodigoInterno,
                pp.Producto.Nombre,
                pp.Producto.Categoria.Nombre,
                pp.CodigoProductoProveedor,
                pp.EsProveedorPrincipal,
                pp.UltimoCostoCentavos,
                pp.FechaUltimaCompra,
                pp.Activo,
                pp.Notas,
                pp.Producto.Subcategoria == null ? null : pp.Producto.Subcategoria.Nombre))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<SupplierHistoryEntry>> GetHistoryInternalAsync(
        int proveedorId,
        CancellationToken cancellationToken)
    {
        return await Context.Bitacora
            .AsNoTracking()
            .Include(b => b.Usuario)
            .Where(b => b.Entidad == "proveedores" && b.EntidadId == proveedorId)
            .OrderByDescending(b => b.FechaAccion)
            .Take(250)
            .Select(b => new SupplierHistoryEntry(
                b.FechaAccion,
                b.TipoAccion.ToString(),
                b.Usuario == null ? "Sistema" : b.Usuario.NombreUsuario,
                b.Descripcion))
            .ToListAsync(cancellationToken);
    }
}
