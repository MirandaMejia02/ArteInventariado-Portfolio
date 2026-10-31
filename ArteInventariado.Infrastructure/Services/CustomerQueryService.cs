using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomerQueryService : ICustomerQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissionService;

    [ActivatorUtilitiesConstructor]
    public CustomerQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        IEmployeePermissionService permissionService)
    {
        _contextFactory = contextFactory;
        _permissionService = permissionService;
    }

    internal CustomerQueryService(AppDbContext context, IEmployeePermissionService permissionService)
    {
        _legacyContext = context;
        _permissionService = permissionService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para consultar clientes.");

    public async Task<IReadOnlyList<CustomerSummary>> SearchAsync(
        CustomerFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER, cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 250);
        var query = Context.Clientes
            .AsNoTracking()
            .AsQueryable();

        if (filter.Estado.HasValue)
        {
            query = query.Where(c => c.Estado == filter.Estado.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            var normalizedName = CustomerTextNormalizer.NormalizeName(text);
            var normalizedPhone = CustomerTextNormalizer.NormalizePhone(text);
            var normalizedDocument = CustomerTextNormalizer.NormalizeDocument(text);
            var normalizedEmail = CustomerTextNormalizer.NormalizeEmail(text);

            query = query.Where(c =>
                c.CodigoCliente.Contains(text) ||
                c.NombreCompleto.Contains(text) ||
                c.NombreNormalizado.Contains(normalizedName) ||
                (normalizedPhone != null && c.TelefonoNormalizado != null && c.TelefonoNormalizado.Contains(normalizedPhone)) ||
                (normalizedDocument != null && c.DocumentoNormalizado != null && c.DocumentoNormalizado.Contains(normalizedDocument)) ||
                (normalizedEmail != null && c.CorreoNormalizado != null && c.CorreoNormalizado.Contains(normalizedEmail)));
        }

        var customers = await query
            .OrderBy(c => c.NombreCompleto)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.ClienteId,
                c.CodigoCliente,
                c.NombreCompleto,
                c.TelefonoCelular,
                c.DocumentoIdentidad,
                c.Estado,
                UltimaCompra = c.Ventas
                    .OrderByDescending(v => v.FechaVenta)
                    .Select(v => (DateTime?)v.FechaVenta)
                    .FirstOrDefault(),
                TotalCompras = c.Ventas.Count
            })
            .ToListAsync(cancellationToken);

        return customers
            .Select(c => new CustomerSummary(
                c.ClienteId,
                c.CodigoCliente,
                c.NombreCompleto,
                c.TelefonoCelular,
                c.DocumentoIdentidad,
                c.Estado,
                c.UltimaCompra,
                c.TotalCompras))
            .ToList();
    }

    public async Task<CustomerDetail?> GetByIdAsync(
        int clienteId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER, cancellationToken);

        return await Context.Clientes
            .AsNoTracking()
            .Include(c => c.UsuarioCreacion)
            .Include(c => c.UsuarioActualizacion)
            .Where(c => c.ClienteId == clienteId)
            .Select(c => new CustomerDetail(
                c.ClienteId,
                c.CodigoCliente,
                c.NombreCompleto,
                c.TelefonoCelular,
                c.WhatsApp,
                c.CorreoElectronico,
                c.Direccion,
                c.DocumentoIdentidad,
                c.Notas,
                c.Estado,
                c.FechaCreacion,
                c.FechaActualizacion,
                c.UsuarioCreacion.NombreUsuario,
                c.UsuarioActualizacion == null ? null : c.UsuarioActualizacion.NombreUsuario))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CustomerSaleSelection?> GetSaleSelectionAsync(
        int clienteId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER, cancellationToken);

        return await Context.Clientes
            .AsNoTracking()
            .Where(c => c.ClienteId == clienteId)
            .Select(c => new CustomerSaleSelection(
                c.ClienteId,
                c.CodigoCliente,
                c.NombreCompleto,
                c.Estado == EstadoCliente.ACTIVO))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerPurchaseHistoryRow>> GetPurchaseHistoryAsync(
        int clienteId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTE_HISTORIAL_VER, cancellationToken);

        var rows = await Context.Ventas
            .AsNoTracking()
            .Include(v => v.Usuario)
            .Include(v => v.Pagos)
            .Where(v => v.ClienteId == clienteId)
            .OrderByDescending(v => v.FechaVenta)
            .Take(500)
            .ToListAsync(cancellationToken);

        return rows
            .Select(v => new CustomerPurchaseHistoryRow(
                v.VentaId,
                v.FechaVenta,
                v.NumeroVenta,
                v.Usuario.NombreUsuario,
                v.TotalCentavos,
                string.Join(", ", v.Pagos
                    .OrderBy(p => p.MetodoPago)
                    .Select(p => p.MetodoPago.ToString())
                    .Distinct()),
                v.Estado))
            .ToList();
    }

    public async Task<SalesHistoryDetail?> GetPurchaseDetailAsync(
        int clienteId,
        int ventaId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTE_HISTORIAL_VER, cancellationToken);

        var sale = await Context.Ventas
            .AsNoTracking()
            .Include(v => v.Usuario)
            .Include(v => v.Detalles)
            .ThenInclude(d => d.Producto)
            .Include(v => v.Pagos)
            .FirstOrDefaultAsync(v => v.ClienteId == clienteId && v.VentaId == ventaId, cancellationToken);

        if (sale is null)
        {
            return null;
        }

        var lines = sale.Detalles
            .OrderBy(d => d.DetalleVentaId)
            .Select(d => new SalesHistoryLine(
                d.Producto.Nombre,
                d.Producto.CodigoInterno,
                d.Cantidad,
                d.PrecioUnitarioCentavos,
                d.DescuentoCentavos,
                d.SubtotalCentavos,
                d.TotalCentavos))
            .ToList();

        var payments = sale.Pagos
            .OrderBy(p => p.PagoVentaId)
            .Select(p => new SalesHistoryPayment(
                p.MetodoPago,
                p.MontoCentavos,
                p.Referencia))
            .ToList();

        var totalPagado = sale.PagadoCentavos > 0
            ? sale.PagadoCentavos
            : sale.Pagos.Sum(p => p.MontoCentavos);

        return new SalesHistoryDetail(
            sale.VentaId,
            sale.NumeroVenta,
            sale.FechaVenta,
            sale.Usuario.NombreCompleto,
            sale.ClienteNombreSnapshot,
            sale.SubtotalCentavos,
            sale.DescuentoCentavos,
            sale.TotalCentavos,
            totalPagado,
            sale.CambioCentavos,
            lines,
            payments);
    }
}
