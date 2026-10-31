using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class ProductProfitabilityQueryService : IProductProfitabilityQueryService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissions;
    private readonly TimeProvider _timeProvider;

    [ActivatorUtilitiesConstructor]
    public ProductProfitabilityQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissions,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissions = permissions;
        _timeProvider = timeProvider;
    }

    internal ProductProfitabilityQueryService(
        AppDbContext context,
        ICurrentSession currentSession,
        IEmployeePermissionService permissions,
        TimeProvider timeProvider)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissions = permissions;
        _timeProvider = timeProvider;
    }

    public async Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsureAuthorizedAsync(lease.Context, cancellationToken);
    }

    public async Task<ProductProfitabilityReport> QueryAsync(
        ProductProfitabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var context = lease.Context;
        await EnsureAuthorizedAsync(context, cancellationToken);

        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = AnalyticsPeriodFactory.Create(
            request.Analytics.Period,
            _timeProvider.GetUtcNow(),
            timeZone);
        var movements = await ProductProfitabilityFactLoader.LoadAsync(
            context,
            period,
            cancellationToken);

        var products = await context.Productos.AsNoTracking()
            .Select(product => new CurrentProduct(
                product.ProductoId,
                product.CodigoInterno,
                product.Nombre,
                product.CategoriaId,
                product.Categoria.Nombre,
                product.SubcategoriaId,
                product.Subcategoria != null ? product.Subcategoria.Nombre : "Sin subcategoría"))
            .ToListAsync(cancellationToken);
        var skuRows = await context.ProductoSkus.AsNoTracking()
            .Select(sku => new CurrentSku(sku.ProductoId, sku.CodigoSku))
            .ToListAsync(cancellationToken);

        var currentProducts = products.ToDictionary(product => product.ProductId);
        var currentSkus = skuRows
            .GroupBy(sku => sku.ProductId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(item => item.Code)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(code => code)
                    .ToList());

        var rows = BuildRows(currentProducts, currentSkus, movements);
        var filtered = ApplyFilters(rows, movements, request);
        var all = ApplyTopLimit(ApplySort(filtered, request), request.TopLimit).ToList();
        var page = all
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return BuildReport(period, page, all);
    }

    private async Task EnsureAuthorizedAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var sessionUser = _currentSession.UsuarioActual;
        if (sessionUser is null)
        {
            throw new UnauthorizedAccessException(
                "Debe iniciar sesión para consultar la rentabilidad por producto.");
        }

        var user = await context.Usuarios.AsNoTracking()
            .Where(item => item.UsuarioId == sessionUser.UsuarioId)
            .Select(item => new { item.Rol, item.Activo })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || !user.Activo)
        {
            throw new UnauthorizedAccessException("La sesión actual no está activa.");
        }

        if (user.Rol == RolUsuario.ADMIN)
        {
            return;
        }

        if (user.Rol != RolUsuario.EMPLEADO)
        {
            throw new UnauthorizedAccessException(
                "El rol actual no puede consultar la rentabilidad por producto.");
        }

        var canReadReports = await _permissions.HasPermissionAsync(
            PermisoEmpleado.REPORTES_COMERCIALES_VER,
            cancellationToken: cancellationToken);
        var canReadCosts = await _permissions.HasPermissionAsync(
            PermisoEmpleado.COSTOS_VER,
            cancellationToken: cancellationToken);
        if (!canReadReports || !canReadCosts)
        {
            throw new UnauthorizedAccessException(
                "El empleado necesita los permisos de reportes comerciales y ver costos.");
        }
    }

    private static IReadOnlyList<ProductProfitabilityRow> BuildRows(
        IReadOnlyDictionary<int, CurrentProduct> currentProducts,
        IReadOnlyDictionary<int, IReadOnlyList<string>> currentSkus,
        IReadOnlyList<HistoricalProfitabilityMovement> movements)
    {
        var productIds = currentProducts.Keys
            .Concat(movements.Select(item => item.ProductId))
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        var movementsByProduct = movements
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return productIds.Select(productId =>
        {
            currentProducts.TryGetValue(productId, out var current);
            var productMovements = movementsByProduct.GetValueOrDefault(productId) ?? [];
            var saleMovements = productMovements
                .Where(item => !item.IsReturn)
                .ToList();
            var latest = productMovements
                .OrderByDescending(item => item.DateUtc)
                .FirstOrDefault();
            var latestWithCategory = productMovements
                .Where(item => !string.IsNullOrWhiteSpace(item.CategoryName))
                .OrderByDescending(item => item.DateUtc)
                .FirstOrDefault();
            var code = FirstNonEmpty(
                latest?.ProductCode,
                current?.Code,
                $"ID-{productId}");
            var name = FirstNonEmpty(
                latest?.ProductName,
                current?.Name,
                $"Producto #{productId} (sin snapshot)");
            var category = FirstNonEmpty(
                latestWithCategory?.CategoryName,
                current?.CategoryName,
                "Sin categoría");
            var categoryId = latestWithCategory?.CategoryId ?? current?.CategoryId;
            var latestWithSubcategory = productMovements
                .Where(item => !string.IsNullOrWhiteSpace(item.SubcategoryName))
                .OrderByDescending(item => item.DateUtc)
                .FirstOrDefault();
            var subcategory = latestWithSubcategory?.SubcategoryName ??
                (productMovements.Count > 0
                    ? "No disponible (histórico)"
                    : current?.SubcategoryName ?? "Sin subcategoría");
            var subcategoryId = latestWithSubcategory?.SubcategoryId ??
                (productMovements.Count == 0 ? current?.SubcategoryId : null);
            var skuCodes = (currentSkus.GetValueOrDefault(productId) ?? [])
                .Concat(productMovements.Select(item => item.SkuCode ?? string.Empty))
                .Where(codeValue => !string.IsNullOrWhiteSpace(codeValue))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(codeValue => codeValue)
                .ToList();
            var netUnits = productMovements.Sum(item => item.Quantity);
            var netSales = productMovements.Sum(item => item.NetSalesCentavos);
            var returnedUnits = productMovements
                .Where(item => item.IsReturn)
                .Sum(item => Math.Max(0, -item.Quantity));
            var coveredUnits = productMovements
                .Where(item => item.HasValidCost)
                .Sum(item => item.Quantity);
            var knownCost = netUnits > 0 && coveredUnits > 0 &&
                productMovements.Any(item => item.HasValidCost)
                ? productMovements
                    .Where(item => item.HasValidCost)
                    .Sum(item => item.CostEffectCentavos)
                : (long?)null;
            var hasUnknownPositiveMovement = productMovements.Any(item =>
                item.Quantity > 0 && !item.HasValidCost);

            var state = netUnits <= 0
                ? ProductProfitabilityCoverageState.NoSales
                : !hasUnknownPositiveMovement && coveredUnits == netUnits
                    ? ProductProfitabilityCoverageState.Complete
                    : coveredUnits <= 0
                        ? ProductProfitabilityCoverageState.NoCoverage
                        : ProductProfitabilityCoverageState.Partial;
            var coverage = netUnits <= 0
                ? (decimal?)null
                : decimal.Round(Math.Clamp(coveredUnits * 100m / netUnits, 0m, 100m), 2,
                    MidpointRounding.AwayFromZero);
            var realizedPrice = netUnits > 0
                ? decimal.Round(netSales / (decimal)netUnits, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            var appliedCost = knownCost.HasValue && coveredUnits > 0
                ? decimal.Round(knownCost.Value / (decimal)coveredUnits, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            var totalCost = state == ProductProfitabilityCoverageState.Complete
                ? knownCost
                : (long?)null;
            var totalGain = state == ProductProfitabilityCoverageState.Complete && totalCost.HasValue
                ? checked(netSales - totalCost.Value)
                : (long?)null;
            var unitGain = totalGain.HasValue && netUnits > 0
                ? decimal.Round(totalGain.Value / (decimal)netUnits, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            var margin = totalGain.HasValue && netSales > 0
                ? decimal.Round(totalGain.Value * 100m / netSales, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            return new ProductProfitabilityRow(
                productId,
                code,
                name,
                category,
                netUnits,
                returnedUnits,
                realizedPrice,
                appliedCost,
                netSales,
                knownCost,
                totalCost,
                unitGain,
                totalGain,
                margin,
                coverage,
                state,
                categoryId,
                skuCodes,
                subcategoryId,
                subcategory,
                saleMovements.Count == 0 ? null : saleMovements.Min(item => (DateTime?)item.DateUtc),
                saleMovements.Count == 0 ? null : saleMovements.Max(item => (DateTime?)item.DateUtc));
        }).ToList();
    }

    private static IEnumerable<ProductProfitabilityRow> ApplyFilters(
        IEnumerable<ProductProfitabilityRow> source,
        IReadOnlyList<HistoricalProfitabilityMovement> movements,
        ProductProfitabilityRequest request)
    {
        var filtered = source;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            filtered = filtered.Where(row =>
                Contains(row.ProductCode, search) ||
                Contains(row.ProductName, search) ||
                Contains(row.CategoryName, search) ||
                Contains(row.SubcategoryName, search) ||
                row.SkuCodes?.Any(code => Contains(code, search)) == true);
        }
        if (request.ProductId.HasValue)
        {
            filtered = filtered.Where(row => row.ProductId == request.ProductId.Value);
        }
        if (request.CategoryId.HasValue)
        {
            filtered = filtered.Where(row =>
                row.CategoryId == request.CategoryId.Value ||
                movements.Any(item => item.ProductId == row.ProductId &&
                    item.CategoryId == request.CategoryId.Value));
        }
        if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.ExplicitlyUnassigned)
        {
            filtered = filtered.Where(row =>
                !row.SubcategoryId.HasValue &&
                string.Equals(
                    row.SubcategoryName,
                    AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel,
                    StringComparison.Ordinal));
        }
        else if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.HistoricalUnavailable)
        {
            filtered = filtered.Where(row =>
                !row.SubcategoryId.HasValue &&
                string.Equals(
                    row.SubcategoryName,
                    AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel,
                    StringComparison.Ordinal));
        }
        else if (request.SubcategoryId.HasValue)
        {
            filtered = filtered.Where(row =>
                row.SubcategoryId == request.SubcategoryId.Value ||
                movements.Any(item => item.ProductId == row.ProductId &&
                    item.SubcategoryId == request.SubcategoryId.Value));
        }
        if (request.SelectedProductIds is { Count: > 0 } selected)
        {
            var selectedIds = selected.ToHashSet();
            filtered = filtered.Where(row => selectedIds.Contains(row.ProductId));
        }

        return filtered;
    }

    private static IEnumerable<ProductProfitabilityRow> ApplyTopLimit(
        IEnumerable<ProductProfitabilityRow> source,
        int? topLimit) => topLimit.HasValue
            ? source.Where(row => row.NetUnits > 0).Take(topLimit.Value)
            : source;

    private static IEnumerable<ProductProfitabilityRow> ApplySort(
        IEnumerable<ProductProfitabilityRow> source,
        ProductProfitabilityRequest request)
    {
        var rows = source.ToList();
        Func<ProductProfitabilityRow, bool> hasSortValue = request.Sort switch
        {
            ProductProfitabilitySort.TotalGain => row => row.TotalGainCentavos.HasValue,
            ProductProfitabilitySort.Margin => row => row.MarginPercentage.HasValue,
            _ => _ => true
        };
        Func<ProductProfitabilityRow, decimal> sortValue = request.Sort switch
        {
            ProductProfitabilitySort.NetSales => row => row.NetSalesCentavos,
            ProductProfitabilitySort.TotalGain => row => row.TotalGainCentavos ?? 0,
            ProductProfitabilitySort.Margin => row => row.MarginPercentage ?? 0,
            _ => row => row.NetUnits
        };
        var ordered = request.Descending
            ? rows.Where(hasSortValue).OrderByDescending(sortValue)
            : rows.Where(hasSortValue).OrderBy(sortValue);
        var missing = rows.Where(row => !hasSortValue(row))
            .OrderBy(row => row.ProductName)
            .ThenBy(row => row.ProductCode);
        return ordered
            .ThenBy(row => row.ProductName)
            .ThenBy(row => row.ProductCode)
            .Concat(missing);
    }

    private static ProductProfitabilityReport BuildReport(
        AnalyticsPeriodDefinition period,
        IReadOnlyList<ProductProfitabilityRow> page,
        IReadOnlyList<ProductProfitabilityRow> all)
    {
        var withSales = all.Where(row => row.NetUnits > 0).ToList();
        var complete = all.Where(row => row.CoverageState == ProductProfitabilityCoverageState.Complete).ToList();
        var knownCostRows = all.Where(row => row.KnownCostCentavos.HasValue).ToList();
        var totalUnits = withSales.Sum(row => row.NetUnits);
        var coveredUnits = withSales.Sum(row =>
            row.CostCoveragePercentage.HasValue
                ? row.NetUnits * row.CostCoveragePercentage.Value / 100m
                : 0m);
        var coverage = totalUnits == 0
            ? (decimal?)null
            : decimal.Round(Math.Clamp(coveredUnits * 100m / totalUnits, 0m, 100m), 2,
                MidpointRounding.AwayFromZero);
        var calculableGain = complete.Any(row => row.TotalGainCentavos.HasValue)
            ? complete.Sum(row => row.TotalGainCentavos!.Value)
            : (long?)null;
        var netSales = all.Sum(row => row.NetSalesCentavos);
        var overallMargin = calculableGain.HasValue && coverage == 100m && netSales > 0
            ? decimal.Round(calculableGain.Value * 100m / netSales, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;
        var knownCost = knownCostRows.Any()
            ? knownCostRows.Sum(row => row.KnownCostCentavos!.Value)
            : (long?)null;
        var completeCount = all.Count(row => row.CoverageState == ProductProfitabilityCoverageState.Complete);
        var partialCount = all.Count(row => row.CoverageState == ProductProfitabilityCoverageState.Partial);
        var noCoverageCount = all.Count(row => row.CoverageState == ProductProfitabilityCoverageState.NoCoverage);
        var noSalesCount = all.Count(row => row.CoverageState == ProductProfitabilityCoverageState.NoSales);
        var coverageText = coverage.HasValue ? $"{coverage.Value:N2}%" : "sin ventas";

        return new ProductProfitabilityReport(
            period,
            page,
            all.Count,
            withSales.Count,
            netSales,
            knownCost,
            calculableGain,
            overallMargin,
            coverage,
            completeCount,
            partialCount,
                noCoverageCount,
                noSalesCount,
                $"Costo histórico por línea; cobertura de costo {coverageText}. " +
            $"Completa: {completeCount:N0}, parcial: {partialCount:N0}, " +
            $"sin cobertura: {noCoverageCount:N0}, sin ventas: {noSalesCount:N0}. " +
            "Ganancia y margen solo se muestran con cobertura completa; el costo conocido parcial se conserva.");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static async Task<TimeZoneInfo> ResolveBusinessTimeZoneAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var configured = await context.ConfiguracionSistema.AsNoTracking()
            .Where(item => item.Clave == DashboardQueryService.BusinessTimeZoneKey)
            .Select(item => item.Valor)
            .SingleOrDefaultAsync(cancellationToken);
        var id = string.IsNullOrWhiteSpace(configured)
            ? DashboardQueryService.DefaultBusinessTimeZoneId
            : configured.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static void Validate(ProductProfitabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Analytics);
        if (request.Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Page),
                "La página debe ser mayor que cero.");
        }
        if (request.PageSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PageSize),
                "El tamaño de página debe estar entre 1 y 1000.");
        }
        if (request.Search?.Length > 200)
        {
            throw new ArgumentException(
                "La búsqueda no puede superar 200 caracteres.", nameof(request));
        }
        if (request.TopLimit is <= 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TopLimit),
                "El límite superior debe estar entre 1 y 1000.");
        }
        if (!Enum.IsDefined(request.Sort))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Sort),
                "La ordenación de rentabilidad no es válida.");
        }
    }

    private sealed record CurrentProduct(
        int ProductId,
        string Code,
        string Name,
        int CategoryId,
        string CategoryName,
        int? SubcategoryId,
        string SubcategoryName);

    private sealed record CurrentSku(int ProductId, string Code);
}
