using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Canonical, read-only valuation of current inventory. All consumers use this
/// query so physical stock, reservations and money calculations cannot drift
/// between analytics, advanced reports and exports.
/// </summary>
public sealed class InventoryValuationQueryService : IInventoryValuationQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _session;

    public InventoryValuationQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession session)
    {
        _contextFactory = contextFactory;
        _session = session;
    }

    internal InventoryValuationQueryService(
        AppDbContext context,
        ICurrentSession session)
    {
        _legacyContext = context;
        _session = session;
    }

    public async Task<InventoryValuationReport> QueryAsync(
        InventoryValuationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory,
            _legacyContext,
            cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);

        var products = await context.Productos
            .AsNoTracking()
            .AsSplitQuery()
            .Include(product => product.Categoria)
            .Include(product => product.Subcategoria)
            .Include(product => product.Skus)
                .ThenInclude(sku => sku.SaldosInventario)
            .OrderBy(product => product.Categoria.Nombre)
            .ThenBy(product => product.Nombre)
            .ToListAsync(cancellationToken);

        var allRows = BuildRows(products);
        var filtered = ApplyFilters(allRows, request).ToList();
        var totals = InventoryValuationCalculator.Summarize(filtered);
        var byCategory = GroupBy(
            filtered,
            row => row.CategoryId?.ToString() ?? $"none:{row.Category}",
            row => row.Category);
        var bySubcategory = GroupBy(
            filtered,
            row => row.SubcategoryId?.ToString() ?? "unassigned",
            row => row.Subcategory);
        var page = filtered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new InventoryValuationReport(
            page,
            filtered.Count,
            totals,
            byCategory,
            bySubcategory,
            BuildCoverageNote(totals),
            "No existe inventario para los filtros seleccionados.");
    }

    private static IReadOnlyList<InventoryValuationRow> BuildRows(
        IReadOnlyList<Producto> products)
    {
        var rows = new List<InventoryValuationRow>();
        var hasAnyCanonicalBalances = products
            .SelectMany(product => product.Skus)
            .SelectMany(sku => sku.SaldosInventario)
            .Any();
        foreach (var product in products)
        {
            // Legacy fallback is used only when the database has no canonical
            // balances at all. Once the canonical table is populated, every
            // current SKU remains visible (including an explicit zero row) and
            // Product.StockActual is never mixed into the total.
            if (!hasAnyCanonicalBalances)
            {
                if (product.Skus.Count == 0)
                {
                    rows.Add(CreateLegacyRow(product));
                }
                else
                {
                    // A legacy product quantity cannot be assigned to a SKU.
                    // Keep it as one explicit legacy row, then expose every
                    // current SKU as a zero-valued catalog row.
                    if (product.StockActual != 0)
                    {
                        rows.Add(CreateLegacyRow(product));
                    }
                    rows.AddRange(product.Skus.Select(sku => CreateSkuRow(product, sku)));
                }
                continue;
            }

            if (product.Skus.Count == 0)
            {
                // A product without a SKU is still part of the catalog, but
                // its compatibility StockActual projection must not be read
                // once canonical balances exist elsewhere in the database.
                rows.Add(CreateCatalogOnlyRow(product));
                continue;
            }

            // Keep every SKU visible. A SKU without a balance row is a valid
            // zero row when another SKU of the same product is canonical.
            rows.AddRange(product.Skus.Select(sku => CreateSkuRow(product, sku)));
        }

        return rows;
    }

    private static InventoryValuationRow CreateSkuRow(
        Producto product,
        ProductoSku sku)
    {
        var exhibition = sku.SaldosInventario
            .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
            .ToList();
        var warehouse = sku.SaldosInventario
            .Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
            .ToList();
        var unassigned = sku.SaldosInventario
            .Where(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
            .ToList();

        return new InventoryValuationRow(
            product.ProductoId,
            sku.ProductoSkuId,
            product.CodigoInterno,
            sku.CodigoSku,
            product.Nombre,
            sku.NombreVariante,
            product.CategoriaId,
            CategoryName(product),
            product.Categoria?.Activa ?? false,
            product.SubcategoriaId,
            SubcategoryName(product),
            product.Subcategoria?.Activa,
            product.Estado,
            sku.Activo,
            exhibition.Sum(balance => balance.ExistenciaFisica),
            exhibition.Sum(balance => balance.Reservado),
            warehouse.Sum(balance => balance.ExistenciaFisica),
            warehouse.Sum(balance => balance.Reservado),
            unassigned.Sum(balance => balance.ExistenciaFisica),
            unassigned.Sum(balance => balance.Reservado),
            null,
            sku.CostoOverrideCentavos ?? product.PrecioCompraCentavos,
            sku.PrecioVentaOverrideCentavos ?? product.PrecioVentaCentavos);
    }

    private static InventoryValuationRow CreateLegacyRow(Producto product) =>
        new(
            product.ProductoId,
            null,
            product.CodigoInterno,
            null,
            product.Nombre,
            null,
            product.CategoriaId,
            CategoryName(product),
            product.Categoria?.Activa ?? false,
            product.SubcategoriaId,
            SubcategoryName(product),
            product.Subcategoria?.Activa,
            product.Estado,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            product.StockActual,
            product.PrecioCompraCentavos,
            product.PrecioVentaCentavos);

    private static InventoryValuationRow CreateCatalogOnlyRow(Producto product) =>
        new(
            product.ProductoId,
            null,
            product.CodigoInterno,
            null,
            product.Nombre,
            null,
            product.CategoriaId,
            CategoryName(product),
            product.Categoria?.Activa ?? false,
            product.SubcategoriaId,
            SubcategoryName(product),
            product.Subcategoria?.Activa,
            product.Estado,
            true,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            product.PrecioCompraCentavos,
            product.PrecioVentaCentavos);

    private static IEnumerable<InventoryValuationRow> ApplyFilters(
        IEnumerable<InventoryValuationRow> source,
        InventoryValuationRequest request)
    {
        var query = source;
        if (request.ProductId.HasValue)
        {
            query = query.Where(row => row.ProductId == request.ProductId.Value);
        }
        if (request.CategoryId.HasValue)
        {
            query = query.Where(row => row.CategoryId == request.CategoryId.Value);
        }
        if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.ExplicitlyUnassigned)
        {
            query = query.Where(row => row.SubcategoryId is null);
        }
        else if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.HistoricalUnavailable)
        {
            // Current inventory has current classification only. Historical
            // snapshot absence is intentionally not projected here.
            query = query.Where(_ => false);
        }
        else if (request.SubcategoryId.HasValue)
        {
            query = query.Where(row => row.SubcategoryId == request.SubcategoryId.Value);
        }
        if (TryEnum(request.State, out EstadoProducto state))
        {
            query = query.Where(row => row.ProductState == state);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(row =>
                Contains(row.ProductCode, search) ||
                Contains(row.SkuCode, search) ||
                Contains(row.Variant, search) ||
                Contains(row.ProductName, search) ||
                Contains(row.Category, search) ||
                Contains(row.Subcategory, search));
        }

        return query
            .OrderBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.ProductName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.ProductCode, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.SkuCode, StringComparer.CurrentCultureIgnoreCase);
    }

    private static IReadOnlyList<InventoryValuationGroupTotals> GroupBy(
        IReadOnlyList<InventoryValuationRow> rows,
        Func<InventoryValuationRow, string> key,
        Func<InventoryValuationRow, string> name) =>
        rows.GroupBy(row => new { Key = key(row), Name = name(row) })
            .OrderBy(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new InventoryValuationGroupTotals(
                group.Key.Key,
                group.Key.Name,
                InventoryValuationCalculator.Summarize(group.ToList())))
            .ToList();

    private static string BuildCoverageNote(InventoryValuationTotals totals)
    {
        var parts = new List<string>
        {
            "Fuente: inventario_saldos; existencia física por SKU y ubicación × costo/precio efectivo vigente.",
            $"Filas: {totals.RowCount:N0}."
        };
        if (totals.LegacyRows > 0)
        {
            parts.Add($"Legacy sin SKU/ubicación: {totals.LegacyRows:N0}; disponible no reconstruible.");
        }
        if (totals.InactiveRows > 0)
        {
            parts.Add($"Filas de producto inactivo: {totals.InactiveRows:N0}.");
        }
        if (totals.InactiveSkuRows > 0)
        {
            parts.Add($"SKU inactivos: {totals.InactiveSkuRows:N0}.");
        }
        if (totals.InactiveSubcategoryRows > 0)
        {
            parts.Add($"Subcategorías inactivas: {totals.InactiveSubcategoryRows:N0}.");
        }
        if (totals.PendingLocationRows > 0)
        {
            parts.Add($"Pendientes de ubicación: {totals.PendingLocationRows:N0}.");
        }
        if (totals.ZeroSalePriceRows > 0)
        {
            parts.Add($"Precio de venta cero: {totals.ZeroSalePriceRows:N0}; margen no calculable para esas filas.");
        }
        if (totals.NonCalculableRows > 0)
        {
            parts.Add($"Valores no calculables: {totals.NonCalculableRows:N0}; no se inventan costos ni precios.");
        }
        return string.Join(" ", parts);
    }

    private async Task EnsureActiveAdminAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var user = _session.UsuarioActual;
        if (user is null || user.Rol != RolUsuario.ADMIN)
        {
            throw new UnauthorizedAccessException(
                "La valoración del inventario es exclusiva de ADMIN.");
        }

        var activeAdmin = await context.Usuarios.AsNoTracking().AnyAsync(
            item => item.UsuarioId == user.UsuarioId &&
                item.Activo &&
                item.Rol == RolUsuario.ADMIN,
            cancellationToken);
        if (!activeAdmin)
        {
            throw new UnauthorizedAccessException("La sesión ADMIN no está activa.");
        }
    }

    private static void Validate(InventoryValuationRequest request)
    {
        if (request.Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Page));
        }
        if (request.PageSize is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PageSize));
        }
        if (request.Search?.Length > 100)
        {
            throw new ArgumentException(
                "La búsqueda de inventario no puede superar 100 caracteres.",
                nameof(request.Search));
        }
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true;

    private static bool TryEnum<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed);

    private static string CategoryName(Producto product) =>
        string.IsNullOrWhiteSpace(product.Categoria?.Nombre)
            ? "Sin categoría"
            : product.Categoria.Nombre;

    private static string SubcategoryName(Producto product) =>
        string.IsNullOrWhiteSpace(product.Subcategoria?.Nombre)
            ? "Sin subcategoría"
            : product.Subcategoria!.Nombre;
}
