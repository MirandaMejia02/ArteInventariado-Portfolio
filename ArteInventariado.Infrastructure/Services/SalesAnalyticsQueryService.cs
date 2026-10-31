using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class SalesAnalyticsQueryService : ISalesAnalyticsQueryService
{
    private const string SnapshotStartKey = "analytics.snapshots_ventas_desde_utc";
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly TimeProvider _timeProvider;

    [ActivatorUtilitiesConstructor]
    public SalesAnalyticsQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _timeProvider = timeProvider;
    }

    internal SalesAnalyticsQueryService(
        AppDbContext context,
        ICurrentSession currentSession,
        TimeProvider timeProvider)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _timeProvider = timeProvider;
    }

    public async Task<SalesAnalyticsReport> GetSalesAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = AnalyticsPeriodFactory.Create(request.Period, _timeProvider.GetUtcNow(), timeZone);
        var comparedPeriod = CreateComparedPeriod(period, request.Comparison, timeZone);
        var current = await LoadFactsAsync(context, period, cancellationToken);
        var compared = await LoadFactsAsync(context, comparedPeriod, cancellationToken);
        var currentMetrics = CalculateMetrics(current);
        var comparedMetrics = CalculateMetrics(compared);

        return new SalesAnalyticsReport(
            period,
            comparedPeriod,
            BuildSummary(currentMetrics, comparedMetrics),
            await BuildCoverageAsync(context, current, cancellationToken),
            BuildTrend(current, period, timeZone));
    }

    public async Task<ProductAnalyticsReport> GetProductsAsync(
        AnalyticsQueryRequest request,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < SalesAnalyticsQueryLimits.MinimumProducts or > SalesAnalyticsQueryLimits.MaximumProducts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"El limite debe estar entre {SalesAnalyticsQueryLimits.MinimumProducts} y {SalesAnalyticsQueryLimits.MaximumProducts}.");
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = AnalyticsPeriodFactory.Create(request.Period, _timeProvider.GetUtcNow(), timeZone);
        var facts = await LoadFactsAsync(context, period, cancellationToken);
        var products = await context.Productos
            .AsNoTracking()
            .Select(product => new CurrentProduct(
                product.ProductoId,
                product.CodigoInterno,
                product.Nombre,
                product.Skus.SelectMany(sku => sku.SaldosInventario).Sum(balance => balance.ExistenciaFisica),
                product.Estado))
            .ToListAsync(cancellationToken);
        var allTimeLastSales = await LoadAllTimeLastSalesAsync(context, cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone).DateTime);
        var rows = BuildProductRows(facts, products, allTimeLastSales, today, timeZone);
        var deliveredIds = facts.SaleLines.Select(line => line.ProductId)
            .Concat(facts.ChangeLines.Select(line => line.ProductId))
            .ToHashSet();
        var withoutSales = products
            .Where(product => product.State == EstadoProducto.ACTIVO && !deliveredIds.Contains(product.ProductId))
            .Select(product => new ProductWithoutSalesRow(
                product.ProductId,
                product.Code,
                product.Name,
                product.Stock,
                CalculateDaysSince(allTimeLastSales.GetValueOrDefault(product.ProductId), today, timeZone)))
            .OrderByDescending(product => product.DaysSinceLastSale ?? int.MaxValue)
            .ThenBy(product => product.ProductName)
            .Take(limit)
            .ToList();

        return new ProductAnalyticsReport(
            period,
            rows.OrderByDescending(row => row.NetUnits).ThenBy(row => row.ProductName).Take(limit).ToList(),
            rows.OrderByDescending(row => row.NetSalesCentavos).ThenBy(row => row.ProductName).Take(limit).ToList(),
            rows.Where(row => row.ReturnedUnits > 0).OrderByDescending(row => row.ReturnedUnits).ThenBy(row => row.ProductName).Take(limit).ToList(),
            withoutSales,
            await BuildCoverageAsync(context, facts, cancellationToken));
    }

    public async Task<ProductTrendReport> GetProductTrendAsync(
        int productId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId));
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = AnalyticsPeriodFactory.Create(request.Period, _timeProvider.GetUtcNow(), timeZone);
        var facts = await LoadFactsAsync(context, period, cancellationToken);
        var product = await context.Productos
            .AsNoTracking()
            .Where(item => item.ProductoId == productId)
            .Select(item => new { item.CodigoInterno, item.Nombre })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("El producto seleccionado no existe.");
        var filtered = facts.ForProduct(productId);

        return new ProductTrendReport(
            productId,
            filtered.LatestProductCode ?? product.CodigoInterno,
            filtered.LatestProductName ?? product.Nombre,
            period,
            BuildProductTrend(filtered, period, timeZone));
    }

    public async Task<CategoryAnalyticsReport> GetCategoriesAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);
        var period = AnalyticsPeriodFactory.Create(request.Period, _timeProvider.GetUtcNow(), timeZone);
        var facts = await LoadFactsAsync(context, period, cancellationToken);
        var categoryMovements = BuildCategoryMovements(facts);
        var subcategoryMovements = BuildSubcategoryMovements(facts);
        if (request.CategoryId.HasValue)
        {
            categoryMovements = categoryMovements
                .Where(item => item.CategoryId == request.CategoryId.Value)
                .ToList();
            subcategoryMovements = subcategoryMovements
                .Where(item => item.CategoryId == request.CategoryId.Value)
                .ToList();
        }
        if (request.SubcategoryFilter != AnalyticsSubcategoryFilter.All)
        {
            categoryMovements = categoryMovements
                .Where(item => MatchesSubcategoryFilter(
                    item.SubcategoryId,
                    item.SubcategoryName,
                    item.HasSubcategorySnapshot,
                    request.SubcategoryFilter))
                .ToList();
            subcategoryMovements = subcategoryMovements
                .Where(item => MatchesSubcategoryFilter(
                    item.SubcategoryId,
                    item.SubcategoryName,
                    item.ExactSnapshot,
                    request.SubcategoryFilter))
                .ToList();
        }
        else if (request.SubcategoryId.HasValue)
        {
            categoryMovements = categoryMovements
                .Where(item => item.SubcategoryId == request.SubcategoryId.Value)
                .ToList();
            subcategoryMovements = subcategoryMovements
                .Where(item => item.SubcategoryId == request.SubcategoryId.Value)
                .ToList();
        }
        var grouped = categoryMovements
            .GroupBy(item => new { item.CategoryId, item.CategoryName })
            .Select(group => new
            {
                group.Key.CategoryId,
                group.Key.CategoryName,
                NetUnits = group.Sum(item => item.NetUnits),
                NetSales = group.Sum(item => item.NetSalesCentavos),
                ReturnedUnits = group.Sum(item => item.ReturnedUnits),
                Exact = group.All(item => item.ExactSnapshot)
            })
            .OrderByDescending(item => item.NetSales)
            .ThenBy(item => item.CategoryName)
            .ToList();
        var positiveTotal = grouped.Where(item => item.NetSales > 0).Sum(item => item.NetSales);
        var rows = grouped.Select(item => new CategoryAnalyticsRow(
            item.CategoryId,
            item.CategoryName,
            item.NetUnits,
            item.NetSales,
            item.ReturnedUnits,
            positiveTotal <= 0 || item.NetSales <= 0
                ? 0
                : decimal.Round(item.NetSales * 100m / positiveTotal, 2, MidpointRounding.AwayFromZero),
            item.Exact ? HistoricalDataQuality.Exact : HistoricalDataQuality.Unavailable)).ToList();
        var trend = BuildCategoryTrend(categoryMovements, period, timeZone);
        var subcategoryGrouped = subcategoryMovements
            .GroupBy(item => new
            {
                item.SubcategoryId,
                item.SubcategoryName,
                item.CategoryId,
                item.CategoryName
            })
            .Select(group => new
            {
                group.Key.SubcategoryId,
                group.Key.SubcategoryName,
                group.Key.CategoryId,
                group.Key.CategoryName,
                NetUnits = group.Sum(item => item.NetUnits),
                NetSales = group.Sum(item => item.NetSalesCentavos),
                ReturnedUnits = group.Sum(item => item.ReturnedUnits),
                Exact = group.All(item => item.ExactSnapshot)
            })
            .OrderByDescending(item => item.NetSales)
            .ThenBy(item => item.CategoryName)
            .ThenBy(item => item.SubcategoryName)
            .ToList();
        var subcategoryPositiveTotal = subcategoryGrouped
            .Where(item => item.NetSales > 0)
            .Sum(item => item.NetSales);
        var subcategories = subcategoryGrouped.Select(item => new SubcategoryAnalyticsRow(
            item.SubcategoryId,
            item.SubcategoryName,
            item.CategoryId,
            item.CategoryName,
            item.NetUnits,
            item.NetSales,
            item.ReturnedUnits,
            subcategoryPositiveTotal <= 0 || item.NetSales <= 0
                ? 0
                : decimal.Round(item.NetSales * 100m / subcategoryPositiveTotal, 2, MidpointRounding.AwayFromZero),
            item.Exact ? HistoricalDataQuality.Exact : HistoricalDataQuality.Unavailable)).ToList();

        return new CategoryAnalyticsReport(
            period,
            rows,
            trend,
            await BuildCoverageAsync(context, facts, cancellationToken),
            subcategories);
    }

    private static SalesAnalyticsSummary BuildSummary(PeriodMetrics current, PeriodMetrics compared)
    {
        return new SalesAnalyticsSummary(
            Metric(current.GrossSales, compared.GrossSales),
            Metric(current.Discounts, compared.Discounts),
            Metric(current.Returns, compared.Returns),
            Metric(current.NetSales, compared.NetSales),
            Metric(current.Transactions, compared.Transactions),
            Metric(current.AverageTicket, compared.AverageTicket),
            Metric(current.NetUnits, compared.NetUnits));
    }

    private static AnalyticsMetricResult Metric(long current, long previous) =>
        new(current, MetricComparisonCalculator.Calculate(current, previous));

    private static PeriodMetrics CalculateMetrics(AnalyticsFacts facts)
    {
        var grossSales = facts.Sales.Sum(sale => sale.SubtotalCentavos)
            + facts.ChangeLines.Sum(line => line.SubtotalCentavos);
        var discounts = facts.Sales.Sum(sale => sale.DiscountCentavos)
            + facts.ChangeLines.Sum(line => line.DiscountCentavos);
        var returns = facts.Returns.Sum(item => item.AmountCentavos);
        var netSales = facts.Sales.Sum(sale => sale.TotalCentavos)
            - returns
            + facts.ChangeLines.Sum(line => line.NetCentavos);
        var transactions = facts.Sales.Count(sale => sale.State == EstadoVenta.COMPLETADA);
        var netUnits = facts.SaleLines.Sum(line => line.Quantity)
            - facts.ReturnLines.Sum(line => line.Quantity)
            + facts.ChangeLines.Sum(line => line.Quantity);
        var averageTicket = transactions == 0
            ? 0
            : decimal.ToInt64(decimal.Round(netSales / (decimal)transactions, 0, MidpointRounding.AwayFromZero));
        return new PeriodMetrics(grossSales, discounts, returns, netSales, transactions, averageTicket, netUnits);
    }

    private static IReadOnlyList<AnalyticsSeriesPoint> BuildTrend(
        AnalyticsFacts facts,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone)
    {
        var buckets = EnumerateBuckets(period, timeZone)
            .ToDictionary(bucket => bucket.LocalKey, bucket => new MutableBucket(bucket));

        foreach (var sale in facts.Sales)
        {
            var bucket = GetBucket(buckets, sale.DateUtc, period.Grouping, timeZone);
            bucket.GrossSales += sale.SubtotalCentavos;
            bucket.Discounts += sale.DiscountCentavos;
            bucket.NetSales += sale.TotalCentavos;
            if (sale.State == EstadoVenta.COMPLETADA)
            {
                bucket.Transactions++;
            }
        }

        foreach (var line in facts.SaleLines)
        {
            GetBucket(buckets, line.DateUtc, period.Grouping, timeZone).NetUnits += line.Quantity;
        }

        foreach (var item in facts.Returns)
        {
            var bucket = GetBucket(buckets, item.DateUtc, period.Grouping, timeZone);
            bucket.Returns += item.AmountCentavos;
            bucket.NetSales -= item.AmountCentavos;
        }

        foreach (var line in facts.ReturnLines)
        {
            GetBucket(buckets, line.DateUtc, period.Grouping, timeZone).NetUnits -= line.Quantity;
        }

        foreach (var line in facts.ChangeLines)
        {
            var bucket = GetBucket(buckets, line.DateUtc, period.Grouping, timeZone);
            bucket.GrossSales += line.SubtotalCentavos;
            bucket.Discounts += line.DiscountCentavos;
            bucket.NetSales += line.NetCentavos;
            bucket.NetUnits += line.Quantity;
        }

        return buckets.Values
            .OrderBy(bucket => bucket.Definition.LocalKey)
            .Select(bucket => new AnalyticsSeriesPoint(
                bucket.Definition.Start,
                bucket.Definition.Label,
                bucket.GrossSales,
                bucket.Discounts,
                bucket.Returns,
                bucket.NetSales,
                bucket.Transactions,
                bucket.NetUnits))
            .ToList();
    }

    private static IReadOnlyList<ProductAnalyticsRow> BuildProductRows(
        AnalyticsFacts facts,
        IReadOnlyList<CurrentProduct> products,
        IReadOnlyDictionary<int, DateTime> allTimeLastSales,
        DateOnly today,
        TimeZoneInfo timeZone)
    {
        var currentById = products.ToDictionary(product => product.ProductId);
        var movements = new List<ProductMovement>();
        movements.AddRange(facts.SaleLines.Select(line => new ProductMovement(
            line.ProductId, line.ProductCode, line.ProductName, line.DateUtc, line.Quantity,
            line.NetCentavos, 0, line.HasProductSnapshot)));
        movements.AddRange(facts.ReturnLines.Select(line => new ProductMovement(
            line.ProductId, line.ProductCode, line.ProductName, line.DateUtc, -line.Quantity,
            -line.AmountCentavos, line.Quantity, line.HasProductSnapshot)));
        movements.AddRange(facts.ChangeLines.Select(line => new ProductMovement(
            line.ProductId, line.ProductCode, line.ProductName, line.DateUtc, line.Quantity,
            line.NetCentavos, 0, line.HasProductSnapshot)));

        return movements
            .GroupBy(item => item.ProductId)
            .Select(group =>
            {
                var latest = group.OrderByDescending(item => item.DateUtc).First();
                var current = currentById.GetValueOrDefault(group.Key);
                var exactCount = group.Count(item => item.ExactSnapshot);
                var quality = exactCount == group.Count()
                    ? HistoricalDataQuality.Exact
                    : exactCount == 0 ? HistoricalDataQuality.Unavailable : HistoricalDataQuality.Partial;
                return new ProductAnalyticsRow(
                    group.Key,
                    latest.ProductCode ?? $"ID-{group.Key}",
                    latest.ProductName ?? $"Producto #{group.Key} (sin snapshot)",
                    group.Sum(item => item.NetUnits),
                    group.Sum(item => item.NetSalesCentavos),
                    group.Sum(item => item.ReturnedUnits),
                    current?.Stock ?? 0,
                    CalculateDaysSince(allTimeLastSales.GetValueOrDefault(group.Key), today, timeZone),
                    quality);
            })
            .ToList();
    }

    private static IReadOnlyList<AnalyticsSeriesPoint> BuildProductTrend(
        AnalyticsFacts facts,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone)
    {
        var buckets = EnumerateBuckets(period, timeZone)
            .ToDictionary(bucket => bucket.LocalKey, bucket => new MutableBucket(bucket));

        foreach (var line in facts.SaleLines)
        {
            var bucket = GetBucket(buckets, line.DateUtc, period.Grouping, timeZone);
            bucket.GrossSales += line.SubtotalCentavos;
            bucket.Discounts += line.LineDiscountCentavos + (line.EffectiveGeneralDiscountCentavos ?? line.GeneralDiscountCentavos ?? 0);
            bucket.NetSales += line.NetCentavos;
            bucket.NetUnits += line.Quantity;
        }

        foreach (var line in facts.ReturnLines)
        {
            var bucket = GetBucket(buckets, line.DateUtc, period.Grouping, timeZone);
            bucket.Returns += line.AmountCentavos;
            bucket.NetSales -= line.AmountCentavos;
            bucket.NetUnits -= line.Quantity;
        }

        foreach (var line in facts.ChangeLines)
        {
            var bucket = GetBucket(buckets, line.DateUtc, period.Grouping, timeZone);
            bucket.GrossSales += line.SubtotalCentavos;
            bucket.Discounts += line.DiscountCentavos;
            bucket.NetSales += line.NetCentavos;
            bucket.NetUnits += line.Quantity;
        }

        return buckets.Values
            .OrderBy(bucket => bucket.Definition.LocalKey)
            .Select(bucket => new AnalyticsSeriesPoint(
                bucket.Definition.Start,
                bucket.Definition.Label,
                bucket.GrossSales,
                bucket.Discounts,
                bucket.Returns,
                bucket.NetSales,
                0,
                bucket.NetUnits))
            .ToList();
    }

    private static IReadOnlyList<CategoryMovement> BuildCategoryMovements(AnalyticsFacts facts)
    {
        var movements = new List<CategoryMovement>();
        movements.AddRange(facts.SaleLines.Select(line => new CategoryMovement(
            line.DateUtc,
            line.CategoryId,
            line.SubcategoryId,
            NormalizeSubcategory(line.SubcategoryId, line.SubcategoryName),
            NormalizeCategory(line.CategoryName),
            line.Quantity,
            line.NetCentavos,
            0,
            line.HasCategorySnapshot,
            line.HasSubcategorySnapshot)));
        movements.AddRange(facts.ReturnLines.Select(line => new CategoryMovement(
            line.DateUtc,
            line.CategoryId,
            line.SubcategoryId,
            NormalizeSubcategory(line.SubcategoryId, line.SubcategoryName),
            NormalizeCategory(line.CategoryName),
            -line.Quantity,
            -line.AmountCentavos,
            line.Quantity,
            line.HasCategorySnapshot,
            line.HasSubcategorySnapshot)));
        movements.AddRange(facts.ChangeLines.Select(line => new CategoryMovement(
            line.DateUtc,
            line.CategoryId,
            line.SubcategoryId,
            NormalizeSubcategory(line.SubcategoryId, line.SubcategoryName),
            NormalizeCategory(line.CategoryName),
            line.Quantity,
            line.NetCentavos,
            0,
            line.HasCategorySnapshot,
            line.HasSubcategorySnapshot)));
        return movements;
    }

    private static IReadOnlyList<SubcategoryMovement> BuildSubcategoryMovements(AnalyticsFacts facts)
    {
        var movements = new List<SubcategoryMovement>();
        movements.AddRange(facts.SaleLines.Select(line => new SubcategoryMovement(
            line.DateUtc,
            line.SubcategoryId,
            NormalizeSubcategory(line.SubcategoryId, line.SubcategoryName),
            line.CategoryId,
            NormalizeCategory(line.CategoryName),
            line.Quantity,
            line.NetCentavos,
            0,
            line.HasSubcategorySnapshot)));
        movements.AddRange(facts.ReturnLines.Select(line => new SubcategoryMovement(
            line.DateUtc,
            line.SubcategoryId,
            NormalizeSubcategory(line.SubcategoryId, line.SubcategoryName),
            line.CategoryId,
            NormalizeCategory(line.CategoryName),
            -line.Quantity,
            -line.AmountCentavos,
            line.Quantity,
            line.HasSubcategorySnapshot)));
        movements.AddRange(facts.ChangeLines.Select(line => new SubcategoryMovement(
            line.DateUtc,
            line.SubcategoryId,
            NormalizeSubcategory(line.SubcategoryId, line.SubcategoryName),
            line.CategoryId,
            NormalizeCategory(line.CategoryName),
            line.Quantity,
            line.NetCentavos,
            0,
            line.HasSubcategorySnapshot)));
        return movements;
    }

    private static IReadOnlyList<CategoryTrendPoint> BuildCategoryTrend(
        IReadOnlyList<CategoryMovement> movements,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone)
    {
        var bucketDefinitions = EnumerateBuckets(period, timeZone).ToDictionary(item => item.LocalKey);
        return movements
            .GroupBy(item => new
            {
                Bucket = NormalizeLocalBucket(item.DateUtc, period.Grouping, timeZone),
                item.CategoryId,
                item.CategoryName
            })
            .OrderBy(group => group.Key.Bucket)
            .ThenBy(group => group.Key.CategoryName)
            .Select(group =>
            {
                var bucket = bucketDefinitions[group.Key.Bucket];
                return new CategoryTrendPoint(
                    bucket.Start,
                    bucket.Label,
                    group.Key.CategoryId,
                    group.Key.CategoryName,
                    group.Sum(item => item.NetUnits),
                    group.Sum(item => item.NetSalesCentavos));
            })
            .ToList();
    }

    private async Task<HistoricalCoverageSummary> BuildCoverageAsync(
        AppDbContext context,
        AnalyticsFacts facts,
        CancellationToken cancellationToken)
    {
        var allHistoricalLines = facts.SaleLines.Cast<IHistoricalLine>()
            .Concat(facts.ChangeLines)
            .ToList();
        var snapshotStartText = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(item => item.Clave == SnapshotStartKey)
            .Select(item => item.Valor)
            .SingleOrDefaultAsync(cancellationToken);
        DateTime? snapshotStart = DateTime.TryParse(
            snapshotStartText,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : null;

        return new HistoricalCoverageSummary(
            snapshotStart,
            Coverage("Snapshot de producto", allHistoricalLines.Count(item => item.HasProductSnapshot), allHistoricalLines.Count),
            Coverage("Categoria historica", allHistoricalLines.Count(item => item.HasCategorySnapshot), allHistoricalLines.Count),
            Coverage("Costo historico", allHistoricalLines.Count(item => item.HasCostSnapshot), allHistoricalLines.Count),
            Coverage(
                "Descuento general distribuido",
                facts.SaleLines.Count(item => item.HasStoredGeneralDiscount),
                facts.SaleLines.Count));
    }

    private static HistoricalCoverageMetric Coverage(string name, int complete, int total)
    {
        var percentage = total == 0
            ? 0
            : decimal.Round(complete * 100m / total, 2, MidpointRounding.AwayFromZero);
        var quality = total == 0 || complete == 0
            ? HistoricalDataQuality.Unavailable
            : complete == total ? HistoricalDataQuality.Exact : HistoricalDataQuality.Partial;
        return new HistoricalCoverageMetric(name, complete, total, percentage, quality);
    }

    private static async Task<AnalyticsFacts> LoadFactsAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var sales = await context.Ventas
            .AsNoTracking()
            .Where(sale => sale.FechaVenta >= period.UtcStart && sale.FechaVenta < period.UtcEndExclusive)
            .Select(sale => new SaleFact(
                sale.VentaId,
                sale.FechaVenta,
                sale.SubtotalCentavos,
                sale.DescuentoCentavos,
                sale.TotalCentavos,
                sale.Estado))
            .ToListAsync(cancellationToken);
        var saleLines = await context.DetalleVentas
            .AsNoTracking()
            .Where(line => line.Venta.FechaVenta >= period.UtcStart && line.Venta.FechaVenta < period.UtcEndExclusive)
            .OrderBy(line => line.VentaId)
            .ThenBy(line => line.DetalleVentaId)
            .Select(line => new SaleLineFact(
                line.DetalleVentaId,
                line.VentaId,
                line.Venta.FechaVenta,
                line.ProductoId,
                line.ProductoCodigoSnapshot,
                line.ProductoNombreSnapshot,
                line.CategoriaIdSnapshot,
                line.CategoriaNombreSnapshot,
                line.SubcategoriaIdSnapshot,
                line.SubcategoriaNombreSnapshot,
                line.Cantidad,
                line.SubtotalCentavos,
                line.DescuentoCentavos,
                line.DescuentoGeneralProporcionalCentavos,
                line.TotalCentavos,
                line.CostoUnitarioCentavos,
                line.OrigenCosto,
                line.Venta.DescuentoCentavos))
            .ToListAsync(cancellationToken);
        saleLines = NormalizeGeneralDiscounts(saleLines);
        var returns = await context.Devoluciones
            .AsNoTracking()
            .Where(item => item.FechaDevolucion >= period.UtcStart && item.FechaDevolucion < period.UtcEndExclusive)
            .Select(item => new ReturnFact(item.DevolucionId, item.FechaDevolucion, item.TotalDevueltoCentavos))
            .ToListAsync(cancellationToken);
        var returnLines = await context.DetalleDevoluciones
            .AsNoTracking()
            .Where(line => line.Devolucion.FechaDevolucion >= period.UtcStart &&
                line.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .Select(line => new ReturnLineFact(
                line.DetalleDevolucionId,
                line.Devolucion.FechaDevolucion,
                line.ProductoId,
                line.DetalleVenta.ProductoCodigoSnapshot,
                line.DetalleVenta.ProductoNombreSnapshot,
                line.DetalleVenta.CategoriaIdSnapshot,
                line.DetalleVenta.CategoriaNombreSnapshot,
                line.DetalleVenta.SubcategoriaIdSnapshot,
                line.DetalleVenta.SubcategoriaNombreSnapshot,
                line.Cantidad,
                line.SubtotalDevueltoCentavos,
                line.DetalleVenta.CostoUnitarioCentavos,
                line.DetalleVenta.OrigenCosto))
            .ToListAsync(cancellationToken);
        var changeLines = await context.DetallesCambioVenta
            .AsNoTracking()
            .Where(line => line.Devolucion.FechaDevolucion >= period.UtcStart &&
                line.Devolucion.FechaDevolucion < period.UtcEndExclusive)
            .Select(line => new ChangeLineFact(
                line.DetalleCambioVentaId,
                line.Devolucion.FechaDevolucion,
                line.ProductoNuevoId,
                line.ProductoCodigoSnapshot,
                line.ProductoNombreSnapshot,
                line.CategoriaIdSnapshot,
                line.CategoriaNombreSnapshot,
                line.SubcategoriaIdSnapshot,
                line.SubcategoriaNombreSnapshot,
                line.Cantidad,
                line.SubtotalCentavos,
                line.DescuentoCentavos,
                line.TotalCentavos,
                line.CostoUnitarioCentavos,
                line.OrigenCosto))
            .ToListAsync(cancellationToken);

        return new AnalyticsFacts(sales, saleLines, returns, returnLines, changeLines);
    }

    private static List<SaleLineFact> NormalizeGeneralDiscounts(List<SaleLineFact> lines)
    {
        var normalized = new List<SaleLineFact>(lines.Count);
        foreach (var sale in lines.GroupBy(line => line.SaleId))
        {
            var ordered = sale.OrderBy(line => line.SaleLineId).ToList();
            if (ordered.All(line => line.GeneralDiscountCentavos.HasValue))
            {
                normalized.AddRange(ordered);
                continue;
            }

            var generalDiscount = Math.Max(0, ordered[0].SaleDiscountCentavos - ordered.Sum(line => line.LineDiscountCentavos));
            var allocations = ProportionalAmountAllocator.Allocate(
                generalDiscount,
                ordered.Select(line => line.LineNetBeforeGeneralCentavos).ToList());
            normalized.AddRange(ordered.Select((line, index) => line with
            {
                EffectiveGeneralDiscountCentavos = allocations[index]
            }));
        }

        return normalized.OrderBy(line => line.SaleId).ThenBy(line => line.SaleLineId).ToList();
    }

    private static async Task<IReadOnlyDictionary<int, DateTime>> LoadAllTimeLastSalesAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var sales = await context.DetalleVentas
            .AsNoTracking()
            .GroupBy(line => line.ProductoId)
            .Select(group => new { ProductId = group.Key, Date = group.Max(line => line.Venta.FechaVenta) })
            .ToListAsync(cancellationToken);
        var changes = await context.DetallesCambioVenta
            .AsNoTracking()
            .GroupBy(line => line.ProductoNuevoId)
            .Select(group => new { ProductId = group.Key, Date = group.Max(line => line.Devolucion.FechaDevolucion) })
            .ToListAsync(cancellationToken);

        return sales.Concat(changes)
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Date));
    }

    private static int? CalculateDaysSince(DateTime dateUtc, DateOnly today, TimeZoneInfo timeZone)
    {
        if (dateUtc == default)
        {
            return null;
        }

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc), timeZone));
        return Math.Max(0, today.DayNumber - localDate.DayNumber);
    }

    private static IReadOnlyList<BucketDefinition> EnumerateBuckets(
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone)
    {
        var start = NormalizeLocalBucket(period.UtcStart, period.Grouping, timeZone);
        var endLocal = TimeZoneInfo.ConvertTime(new DateTimeOffset(period.UtcEndExclusive, TimeSpan.Zero), timeZone).DateTime;
        var result = new List<BucketDefinition>();
        for (var current = start; current < endLocal; current = AddBucket(current, period.Grouping))
        {
            var offset = timeZone.GetUtcOffset(current);
            result.Add(new BucketDefinition(
                current,
                new DateTimeOffset(current, offset),
                FormatBucketLabel(current, period.Grouping)));
        }

        return result;
    }

    private static MutableBucket GetBucket(
        IReadOnlyDictionary<DateTime, MutableBucket> buckets,
        DateTime dateUtc,
        AnalyticsGrouping grouping,
        TimeZoneInfo timeZone)
    {
        var key = NormalizeLocalBucket(dateUtc, grouping, timeZone);
        return buckets.TryGetValue(key, out var bucket)
            ? bucket
            : throw new InvalidOperationException("El movimiento no pertenece a un bloque del periodo solicitado.");
    }

    private static DateTime NormalizeLocalBucket(DateTime dateUtc, AnalyticsGrouping grouping, TimeZoneInfo timeZone)
    {
        var utc = DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
        return grouping switch
        {
            AnalyticsGrouping.Hour => new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0),
            AnalyticsGrouping.Day => new DateTime(local.Year, local.Month, local.Day),
            AnalyticsGrouping.Month => new DateTime(local.Year, local.Month, 1),
            AnalyticsGrouping.Year => new DateTime(local.Year, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(grouping))
        };
    }

    private static DateTime AddBucket(DateTime value, AnalyticsGrouping grouping) => grouping switch
    {
        AnalyticsGrouping.Hour => value.AddHours(1),
        AnalyticsGrouping.Day => value.AddDays(1),
        AnalyticsGrouping.Month => value.AddMonths(1),
        AnalyticsGrouping.Year => value.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(grouping))
    };

    private static string FormatBucketLabel(DateTime value, AnalyticsGrouping grouping) => grouping switch
    {
        AnalyticsGrouping.Hour => value.ToString("HH:mm"),
        AnalyticsGrouping.Day => value.ToString("dd/MM"),
        AnalyticsGrouping.Month => value.ToString("MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("es-SV")),
        AnalyticsGrouping.Year => value.ToString("yyyy"),
        _ => value.ToString("d")
    };

    private static AnalyticsPeriodDefinition CreateComparedPeriod(
        AnalyticsPeriodDefinition period,
        AnalyticsComparisonKind comparison,
        TimeZoneInfo timeZone) => comparison switch
    {
        AnalyticsComparisonKind.PreviousEquivalent => AnalyticsPeriodFactory.CreatePreviousEquivalent(period, timeZone),
        AnalyticsComparisonKind.SamePeriodPreviousYear => AnalyticsPeriodFactory.CreateSamePeriodPreviousYear(period, timeZone),
        _ => throw new ArgumentOutOfRangeException(nameof(comparison))
    };

    private async Task EnsureActiveAdminAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var user = _currentSession.UsuarioActual;
        if (user is null || !_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede consultar la analitica comercial.");
        }

        var active = await context.Usuarios.AsNoTracking().AnyAsync(
            item => item.UsuarioId == user.UsuarioId && item.Activo && item.Rol == RolUsuario.ADMIN,
            cancellationToken);
        if (!active)
        {
            throw new InvalidOperationException("La sesion ADMIN ya no se encuentra activa.");
        }
    }

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

    private ValueTask<AppDbContextLease> CreateContextAsync(CancellationToken cancellationToken) =>
        AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);

    private static string NormalizeCategory(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Sin categoria historica" : value;

    private static string NormalizeSubcategory(int? id, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "No disponible (histórico)"
            : value.Trim();

    private static bool MatchesSubcategoryFilter(
        int? subcategoryId,
        string subcategoryName,
        bool hasSnapshot,
        AnalyticsSubcategoryFilter filter) => filter switch
        {
            AnalyticsSubcategoryFilter.ExplicitlyUnassigned =>
                hasSnapshot && !subcategoryId.HasValue &&
                string.Equals(
                    subcategoryName,
                    AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel,
                    StringComparison.Ordinal),
            AnalyticsSubcategoryFilter.HistoricalUnavailable =>
                !hasSnapshot,
            _ => true
        };

    private interface IHistoricalLine
    {
        bool HasProductSnapshot { get; }
        bool HasCategorySnapshot { get; }
        bool HasSubcategorySnapshot { get; }
        bool HasCostSnapshot { get; }
    }

    private sealed record SaleFact(
        int SaleId,
        DateTime DateUtc,
        long SubtotalCentavos,
        long DiscountCentavos,
        long TotalCentavos,
        EstadoVenta State);

    private sealed record SaleLineFact(
        int SaleLineId,
        int SaleId,
        DateTime DateUtc,
        int ProductId,
        string? ProductCode,
        string? ProductName,
        int? CategoryId,
        string? CategoryName,
        int? SubcategoryId,
        string? SubcategoryName,
        int Quantity,
        long SubtotalCentavos,
        long LineDiscountCentavos,
        long? GeneralDiscountCentavos,
        long LineNetBeforeGeneralCentavos,
        long? CostCentavos,
        OrigenCostoHistorico CostOrigin,
        long SaleDiscountCentavos,
        long? EffectiveGeneralDiscountCentavos = null) : IHistoricalLine, IHistoricalProduct
    {
        public bool HasProductSnapshot => !string.IsNullOrWhiteSpace(ProductCode) && !string.IsNullOrWhiteSpace(ProductName);
        public bool HasCategorySnapshot => CategoryId.HasValue && !string.IsNullOrWhiteSpace(CategoryName);
        public bool HasSubcategorySnapshot => !string.IsNullOrWhiteSpace(SubcategoryName);
        public bool HasCostSnapshot => CostCentavos.HasValue && CostOrigin != OrigenCostoHistorico.DESCONOCIDO;
        public bool HasStoredGeneralDiscount => GeneralDiscountCentavos.HasValue;
        public long NetCentavos => LineNetBeforeGeneralCentavos - (EffectiveGeneralDiscountCentavos ?? GeneralDiscountCentavos ?? 0);
    }

    private sealed record ReturnFact(int ReturnId, DateTime DateUtc, long AmountCentavos);

    private sealed record ReturnLineFact(
        int ReturnLineId,
        DateTime DateUtc,
        int ProductId,
        string? ProductCode,
        string? ProductName,
        int? CategoryId,
        string? CategoryName,
        int? SubcategoryId,
        string? SubcategoryName,
        int Quantity,
        long AmountCentavos,
        long? CostCentavos,
        OrigenCostoHistorico CostOrigin) : IHistoricalLine
    {
        public bool HasProductSnapshot => !string.IsNullOrWhiteSpace(ProductCode) && !string.IsNullOrWhiteSpace(ProductName);
        public bool HasCategorySnapshot => CategoryId.HasValue && !string.IsNullOrWhiteSpace(CategoryName);
        public bool HasSubcategorySnapshot => !string.IsNullOrWhiteSpace(SubcategoryName);
        public bool HasCostSnapshot => CostCentavos.HasValue && CostOrigin != OrigenCostoHistorico.DESCONOCIDO;
    }

    private sealed record ChangeLineFact(
        int ChangeLineId,
        DateTime DateUtc,
        int ProductId,
        string? ProductCode,
        string? ProductName,
        int? CategoryId,
        string? CategoryName,
        int? SubcategoryId,
        string? SubcategoryName,
        int Quantity,
        long SubtotalCentavos,
        long DiscountCentavos,
        long NetCentavos,
        long? CostCentavos,
        OrigenCostoHistorico CostOrigin) : IHistoricalLine
    {
        public bool HasProductSnapshot => !string.IsNullOrWhiteSpace(ProductCode) && !string.IsNullOrWhiteSpace(ProductName);
        public bool HasCategorySnapshot => CategoryId.HasValue && !string.IsNullOrWhiteSpace(CategoryName);
        public bool HasSubcategorySnapshot => !string.IsNullOrWhiteSpace(SubcategoryName);
        public bool HasCostSnapshot => CostCentavos.HasValue && CostOrigin != OrigenCostoHistorico.DESCONOCIDO;
    }

    private sealed record AnalyticsFacts(
        IReadOnlyList<SaleFact> Sales,
        IReadOnlyList<SaleLineFact> SaleLines,
        IReadOnlyList<ReturnFact> Returns,
        IReadOnlyList<ReturnLineFact> ReturnLines,
        IReadOnlyList<ChangeLineFact> ChangeLines)
    {
        public string? LatestProductCode => SaleLines.Cast<IHistoricalProduct>()
            .Concat(ChangeLines.Select(line => new HistoricalProductAdapter(line.ProductCode, line.ProductName, line.DateUtc)))
            .OrderByDescending(line => line.DateUtc)
            .Select(line => line.ProductCode)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        public string? LatestProductName => SaleLines.Cast<IHistoricalProduct>()
            .Concat(ChangeLines.Select(line => new HistoricalProductAdapter(line.ProductCode, line.ProductName, line.DateUtc)))
            .OrderByDescending(line => line.DateUtc)
            .Select(line => line.ProductName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        public AnalyticsFacts ForProduct(int productId) => new(
            Sales,
            SaleLines.Where(line => line.ProductId == productId).ToList(),
            Returns,
            ReturnLines.Where(line => line.ProductId == productId).ToList(),
            ChangeLines.Where(line => line.ProductId == productId).ToList());
    }

    private interface IHistoricalProduct
    {
        string? ProductCode { get; }
        string? ProductName { get; }
        DateTime DateUtc { get; }
    }

    private sealed record HistoricalProductAdapter(string? ProductCode, string? ProductName, DateTime DateUtc) : IHistoricalProduct;

    private sealed record PeriodMetrics(
        long GrossSales,
        long Discounts,
        long Returns,
        long NetSales,
        int Transactions,
        long AverageTicket,
        int NetUnits);

    private sealed record CurrentProduct(int ProductId, string Code, string Name, int Stock, EstadoProducto State);
    private sealed record ProductMovement(
        int ProductId,
        string? ProductCode,
        string? ProductName,
        DateTime DateUtc,
        int NetUnits,
        long NetSalesCentavos,
        int ReturnedUnits,
        bool ExactSnapshot);
    private sealed record CategoryMovement(
        DateTime DateUtc,
        int? CategoryId,
        int? SubcategoryId,
        string SubcategoryName,
        string CategoryName,
        int NetUnits,
        long NetSalesCentavos,
        int ReturnedUnits,
        bool ExactSnapshot,
        bool HasSubcategorySnapshot);
    private sealed record SubcategoryMovement(
        DateTime DateUtc,
        int? SubcategoryId,
        string SubcategoryName,
        int? CategoryId,
        string CategoryName,
        int NetUnits,
        long NetSalesCentavos,
        int ReturnedUnits,
        bool ExactSnapshot);
    private sealed record BucketDefinition(DateTime LocalKey, DateTimeOffset Start, string Label);

    private sealed class MutableBucket(BucketDefinition definition)
    {
        public BucketDefinition Definition { get; } = definition;
        public long GrossSales { get; set; }
        public long Discounts { get; set; }
        public long Returns { get; set; }
        public long NetSales { get; set; }
        public int Transactions { get; set; }
        public int NetUnits { get; set; }
    }
}
