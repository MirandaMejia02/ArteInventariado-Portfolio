using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class AdvancedReportQueryService : IAdvancedReportQueryService, IAdvancedReportFilterOptionsService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ISalesAnalyticsQueryService _sales;
    private readonly IFinancialAnalyticsQueryService _financial;
    private readonly ICommercialAnalyticsQueryService _commercial;
    private readonly ICurrentSession _session;
    private readonly IProductProfitabilityQueryService? _profitability;
    private readonly IInventoryValuationQueryService _inventoryValuation;

    [ActivatorUtilitiesConstructor]
    public AdvancedReportQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ISalesAnalyticsQueryService sales,
        IFinancialAnalyticsQueryService financial,
        ICommercialAnalyticsQueryService commercial,
        ICurrentSession session,
        IProductProfitabilityQueryService? profitability = null,
        IInventoryValuationQueryService? inventoryValuation = null)
    {
        _contextFactory = contextFactory;
        _sales = sales;
        _financial = financial;
        _commercial = commercial;
        _session = session;
        _profitability = profitability;
        _inventoryValuation = inventoryValuation ?? new InventoryValuationQueryService(contextFactory, session);
    }

    internal AdvancedReportQueryService(
        AppDbContext context,
        ISalesAnalyticsQueryService sales,
        IFinancialAnalyticsQueryService financial,
        ICommercialAnalyticsQueryService commercial,
        ICurrentSession session,
        IProductProfitabilityQueryService? profitability = null,
        IInventoryValuationQueryService? inventoryValuation = null)
    {
        _legacyContext = context;
        _sales = sales;
        _financial = financial;
        _commercial = commercial;
        _session = session;
        _profitability = profitability;
        _inventoryValuation = inventoryValuation ?? new InventoryValuationQueryService(context, session);
    }

    public async Task<AdvancedReportResult> QueryAsync(
        AdvancedReportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        var definition = AdvancedReportCatalog.Get(request.Kind);
        var filters = AppliedFilters(request);
        if (request.Kind == AdvancedReportKind.ProductProfitability)
        {
            if (_profitability is null)
            {
                throw new InvalidOperationException(
                    "El servicio de rentabilidad por producto no está configurado.");
            }

            var profitability = await _profitability.QueryAsync(new ProductProfitabilityRequest(
                request.Analytics,
                Search: request.Search,
                ProductId: request.ProductId,
                CategoryId: request.CategoryId,
                SubcategoryId: request.SubcategoryId,
                SubcategoryFilter: request.SubcategoryFilter,
                Sort: request.ProfitabilitySort,
                Descending: request.ProfitabilityDescending,
                TopLimit: request.ProfitabilityTop,
                SelectedProductIds: request.SelectedProductIds,
                Page: request.Page,
                PageSize: request.PageSize), cancellationToken);
            return ProductProfitabilityResult(definition, profitability, filters, request);
        }

        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await EnsureActiveAdminAsync(lease.Context, cancellationToken);

        // This reuses the canonical period factory and the active ADMIN guard.
        var sales = await _sales.GetSalesAsync(request.Analytics, cancellationToken);

        if (request.Kind == AdvancedReportKind.Sales)
        {
            return SalesResult(definition, sales, filters);
        }
        if (request.Kind == AdvancedReportKind.Products)
        {
            var products = await _sales.GetProductsAsync(request.Analytics, 100, cancellationToken);
            var productIds = products.MostSoldByUnits.Select(item => item.ProductId).Distinct().ToList();
            var metadata = await LoadProductSearchMetadataAsync(
                lease.Context,
                productIds,
                sales.Period,
                cancellationToken);
            return ProductResult(definition, sales.Period, products, metadata, request, filters);
        }
        if (request.Kind == AdvancedReportKind.Categories)
        {
            var categories = await _sales.GetCategoriesAsync(request.Analytics, cancellationToken);
            return CategoryResult(definition, sales.Period, categories, request, filters);
        }
        if (request.Kind is AdvancedReportKind.GrossProfit or AdvancedReportKind.Cash)
        {
            var financial = await _financial.GetAsync(request.Analytics, cancellationToken);
            return request.Kind == AdvancedReportKind.GrossProfit
                ? GrossProfitResult(definition, financial, filters)
                : CashResult(definition, financial, filters);
        }

        var data = await QueryOperationalAsync(
            lease.Context, request, sales.Period, cancellationToken);
        data = await EnrichWithApprovedAnalyticsAsync(request, data, cancellationToken);
        return Result(definition, sales.Period, request, filters, data);
    }

    public async Task<IReadOnlyList<AdvancedReportFilterOption>> GetOptionsAsync(
        AdvancedReportKind reportKind,
        AdvancedReportFilterKind filterKind,
        string? search = null,
        int limit = 75,
        CancellationToken cancellationToken = default)
    {
        var definition = AdvancedReportCatalog.Get(reportKind);
        if (!definition.Filters.Contains(filterKind) || !IsEntityFilter(filterKind))
        {
            return [];
        }
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "El límite de opciones debe estar entre 1 y 100.");
        }
        if (search?.Length > 100)
        {
            throw new ArgumentException("La búsqueda de opciones no puede superar 100 caracteres.", nameof(search));
        }

        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        if (reportKind == AdvancedReportKind.ProductProfitability)
        {
            if (_profitability is null)
            {
                throw new InvalidOperationException(
                    "El servicio de rentabilidad por producto no está configurado.");
            }

            await _profitability.EnsureAuthorizedAsync(cancellationToken);
        }
        else
        {
            await EnsureActiveAdminAsync(lease.Context, cancellationToken);
        }
        var context = lease.Context;
        var pattern = LikePattern(search);

        return filterKind switch
        {
            AdvancedReportFilterKind.Product => await ProductOptionsAsync(context, pattern, limit, cancellationToken),
            AdvancedReportFilterKind.Category => await CategoryOptionsAsync(context, pattern, limit, cancellationToken),
            AdvancedReportFilterKind.Subcategory => await SubcategoryOptionsAsync(context, pattern, limit, cancellationToken),
            AdvancedReportFilterKind.Customer => await CustomerOptionsAsync(context, pattern, limit, cancellationToken),
            AdvancedReportFilterKind.Supplier => await SupplierOptionsAsync(context, pattern, limit, cancellationToken),
            AdvancedReportFilterKind.User => await UserOptionsAsync(context, pattern, limit, cancellationToken),
            AdvancedReportFilterKind.CashRegister => await CashOptionsAsync(context, pattern, limit, cancellationToken),
            _ => []
        };
    }

    private static async Task<IReadOnlyList<AdvancedReportFilterOption>> ProductOptionsAsync(
        AppDbContext context,
        string? pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Productos.AsNoTracking();
        if (pattern is not null)
        {
            query = query.Where(item => EF.Functions.Like(item.CodigoInterno, pattern, "\\") ||
                EF.Functions.Like(item.Nombre, pattern, "\\") ||
                EF.Functions.Like(item.Categoria.Nombre, pattern, "\\") ||
                (item.Subcategoria != null && EF.Functions.Like(item.Subcategoria.Nombre, pattern, "\\")));
        }
        return await query.OrderBy(item => item.Nombre)
            .Take(limit)
            .Select(item => new AdvancedReportFilterOption(
                AdvancedReportFilterKind.Product, item.ProductoId, item.CodigoInterno, item.Nombre))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<int, ProductSearchMetadata>> LoadProductSearchMetadataAsync(
        AppDbContext context,
        IReadOnlyList<int> productIds,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<int, ProductSearchMetadata>();
        }

        var products = await context.Productos.AsNoTracking()
            .Where(product => productIds.Contains(product.ProductoId))
            .Select(product => new ProductSearchMetadata(
                product.ProductoId,
                product.CategoriaId,
                product.Categoria.Nombre,
                product.SubcategoriaId,
                product.Subcategoria != null ? product.Subcategoria.Nombre : "Sin subcategoría"))
            .ToListAsync(cancellationToken);
        return products.ToDictionary(product => product.ProductId);
    }

    private static async Task<IReadOnlyList<AdvancedReportFilterOption>> CategoryOptionsAsync(
        AppDbContext context,
        string? pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Categorias.AsNoTracking();
        if (pattern is not null)
        {
            query = query.Where(item => EF.Functions.Like(item.Nombre, pattern, "\\"));
        }
        return await query.OrderBy(item => item.Nombre)
            .Take(limit)
            .Select(item => new AdvancedReportFilterOption(
                AdvancedReportFilterKind.Category, item.CategoriaId, string.Empty, item.Nombre))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AdvancedReportFilterOption>> SubcategoryOptionsAsync(
        AppDbContext context,
        string? pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Subcategorias.AsNoTracking();
        if (pattern is not null)
        {
            query = query.Where(item => EF.Functions.Like(item.Nombre, pattern, "\\") ||
                EF.Functions.Like(item.Categoria.Nombre, pattern, "\\"));
        }

        var options = await query
            .OrderBy(item => item.Categoria.Nombre)
            .ThenBy(item => item.Nombre)
            .Take(limit)
            .Select(item => new AdvancedReportFilterOption(
                AdvancedReportFilterKind.Subcategory,
                item.SubcategoriaId,
                item.Categoria.Nombre,
                item.Nombre,
                item.CategoriaId))
            .ToListAsync(cancellationToken);

        var special = new List<AdvancedReportFilterOption>(2);
        if (MatchesSpecialOption(
                AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel,
                pattern))
        {
            special.Add(new AdvancedReportFilterOption(
                AdvancedReportFilterKind.Subcategory,
                AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedOptionId,
                string.Empty,
                AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel));
        }

        if (MatchesSpecialOption(
                AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel,
                pattern))
        {
            special.Add(new AdvancedReportFilterOption(
                AdvancedReportFilterKind.Subcategory,
                AnalyticsSubcategoryFilterOptions.HistoricalUnavailableOptionId,
                string.Empty,
                AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel));
        }

        return special.Concat(options).Take(limit).ToList();
    }

    private static async Task<IReadOnlyList<AdvancedReportFilterOption>> CustomerOptionsAsync(
        AppDbContext context,
        string? pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Clientes.AsNoTracking();
        if (pattern is not null)
        {
            query = query.Where(item => EF.Functions.Like(item.CodigoCliente, pattern, "\\") ||
                EF.Functions.Like(item.NombreCompleto, pattern, "\\"));
        }
        return await query.OrderBy(item => item.NombreCompleto)
            .Take(limit)
            .Select(item => new AdvancedReportFilterOption(
                AdvancedReportFilterKind.Customer, item.ClienteId, item.CodigoCliente, item.NombreCompleto))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AdvancedReportFilterOption>> SupplierOptionsAsync(
        AppDbContext context,
        string? pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Proveedores.AsNoTracking();
        if (pattern is not null)
        {
            query = query.Where(item => EF.Functions.Like(item.CodigoProveedor, pattern, "\\") ||
                EF.Functions.Like(item.NombreRazonSocial, pattern, "\\"));
        }
        return await query.OrderBy(item => item.NombreRazonSocial)
            .Take(limit)
            .Select(item => new AdvancedReportFilterOption(
                AdvancedReportFilterKind.Supplier, item.ProveedorId, item.CodigoProveedor, item.NombreRazonSocial))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AdvancedReportFilterOption>> UserOptionsAsync(
        AppDbContext context,
        string? pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Usuarios.AsNoTracking();
        if (pattern is not null)
        {
            query = query.Where(item => EF.Functions.Like(item.NombreUsuario, pattern, "\\") ||
                EF.Functions.Like(item.NombreCompleto, pattern, "\\"));
        }
        return await query.OrderBy(item => item.NombreCompleto)
            .Take(limit)
            .Select(item => new AdvancedReportFilterOption(
                AdvancedReportFilterKind.User, item.UsuarioId, item.NombreUsuario, item.NombreCompleto))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AdvancedReportFilterOption>> CashOptionsAsync(
        AppDbContext context,
        string? pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Cajas.AsNoTracking();
        if (pattern is not null)
        {
            if (!int.TryParse(pattern.Trim('%', '\\'), out var cashId))
            {
                return [];
            }
            query = query.Where(item => item.CajaId == cashId);
        }
        var rows = await query.OrderByDescending(item => item.FechaApertura)
            .Take(limit)
            .Select(item => new { item.CajaId, item.FechaOperacion, item.Estado })
            .ToListAsync(cancellationToken);
        return rows.Select(item => new AdvancedReportFilterOption(
            AdvancedReportFilterKind.CashRegister,
            item.CajaId,
            $"Caja #{item.CajaId}",
            $"{item.FechaOperacion:dd/MM/yyyy} · {item.Estado}"))
            .ToList();
    }

    private async Task EnsureActiveAdminAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var user = _session.UsuarioActual;
        if (user is null || user.Rol != RolUsuario.ADMIN)
        {
            throw new UnauthorizedAccessException("Los reportes avanzados son exclusivos de ADMIN.");
        }
        var activeAdmin = await context.Usuarios.AsNoTracking().AnyAsync(
            item => item.UsuarioId == user.UsuarioId && item.Activo && item.Rol == RolUsuario.ADMIN,
            cancellationToken);
        if (!activeAdmin)
        {
            throw new UnauthorizedAccessException("La sesión ADMIN no está activa.");
        }
    }

    private async Task<ReportData> EnrichWithApprovedAnalyticsAsync(
        AdvancedReportRequest request,
        ReportData data,
        CancellationToken cancellationToken)
    {
        if (request.Kind is AdvancedReportKind.Quotations or AdvancedReportKind.CustomOrders)
        {
            var report = await _commercial.GetAsync(request.Analytics, cancellationToken);
            return request.Kind == AdvancedReportKind.Quotations
                ? data with
                {
                    Summary =
                    [
                        S("Cotizaciones creadas", report.Quotations.Created.Value, AdvancedReportValueKind.Integer),
                        S("Importe cotizado", Money(report.Quotations.QuotedAmount.Value), AdvancedReportValueKind.Money),
                        S("Aceptadas", report.Quotations.Accepted, AdvancedReportValueKind.Integer),
                        S("Convertidas", report.Quotations.Converted, AdvancedReportValueKind.Integer),
                        S("Valor convertido", Money(report.Quotations.ConvertedValueCentavos), AdvancedReportValueKind.Money)
                    ],
                    CoverageNote = report.CoverageNote
                }
                : data with
                {
                    Summary =
                    [
                        S("Pedidos creados", report.Orders.Created.Value, AdvancedReportValueKind.Integer),
                        S("Anticipos", Money(report.Orders.AdvancesCentavos), AdvancedReportValueKind.Money),
                        S("Pagos", Money(report.Orders.PaymentsCentavos), AdvancedReportValueKind.Money),
                        S("Reembolsos", Money(report.Orders.RefundsCentavos), AdvancedReportValueKind.Money),
                        S("Saldo pendiente", Money(report.Orders.OutstandingBalanceCentavos), AdvancedReportValueKind.Money),
                        S("Retrasados", report.Orders.Delayed, AdvancedReportValueKind.Integer)
                    ],
                    CoverageNote = report.CoverageNote
                };
        }

        if (request.Kind is AdvancedReportKind.Expenses or AdvancedReportKind.Purchases or
            AdvancedReportKind.AccountsReceivable or AdvancedReportKind.AccountsPayable)
        {
            var report = await _financial.GetAsync(request.Analytics, cancellationToken);
            return request.Kind switch
            {
                AdvancedReportKind.Expenses => data with
                {
                    Summary = [S("Gastos operativos", Money(report.Finance.OperatingExpenses.Value), AdvancedReportValueKind.Money)]
                },
                AdvancedReportKind.Purchases => data with
                {
                    Summary =
                    [
                        S("Compras comprometidas", Money(report.Purchases.CommittedPurchases.Value), AdvancedReportValueKind.Money),
                        S("Mercancía recibida", Money(report.Purchases.ReceivedMerchandise.Value), AdvancedReportValueKind.Money),
                        S("Pagos a proveedores", Money(report.Purchases.SupplierPayments.Value), AdvancedReportValueKind.Money)
                    ],
                    CoverageNote = $"Cobertura histórica de recepciones: {report.Purchases.ReceptionSnapshotCoverage.Percentage:N2}%."
                },
                AdvancedReportKind.AccountsReceivable => data with
                {
                    Summary =
                    [
                        S("Saldo pendiente al cierre", Money(report.Receivables.OutstandingBalanceCentavos), AdvancedReportValueKind.Money),
                        S("Creadas en el período", Money(report.Receivables.CreatedCentavos), AdvancedReportValueKind.Money),
                        S("Pagos del período", Money(report.Receivables.PaymentsCentavos), AdvancedReportValueKind.Money)
                    ]
                },
                AdvancedReportKind.AccountsPayable => data with
                {
                    Summary =
                    [
                        S("Saldo pendiente al cierre", Money(report.Payables.OutstandingBalanceCentavos), AdvancedReportValueKind.Money),
                        S("Creadas en el período", Money(report.Payables.CreatedCentavos), AdvancedReportValueKind.Money),
                        S("Pagos del período", Money(report.Payables.PaymentsCentavos), AdvancedReportValueKind.Money)
                    ]
                },
                _ => data
            };
        }

        return data;
    }

    private async Task<ReportData> QueryOperationalAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        return request.Kind switch
        {
            AdvancedReportKind.Inventory => await InventoryAsync(context, request, period, cancellationToken),
            AdvancedReportKind.InventoryMovements => await InventoryMovementsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.PaymentMethods => await PaymentMethodsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Expenses => await ExpensesAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Withdrawals => await WithdrawalsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Purchases => await PurchasesAsync(context, request, period, cancellationToken),
            AdvancedReportKind.PurchaseReceptions => await PurchaseReceptionsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Suppliers => await SuppliersAsync(context, request, cancellationToken),
            AdvancedReportKind.Customers => await CustomersAsync(context, request, cancellationToken),
            AdvancedReportKind.AccountsReceivable => await AccountsReceivableAsync(context, request, period, cancellationToken),
            AdvancedReportKind.CustomerPayments => await CustomerPaymentsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.AccountsPayable => await AccountsPayableAsync(context, request, period, cancellationToken),
            AdvancedReportKind.SupplierPayments => await SupplierPaymentsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Returns => await ReturnsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Refunds => await RefundsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Cancellations => await CancellationsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.Quotations => await QuotationsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.CustomOrders => await CustomOrdersAsync(context, request, period, cancellationToken),
            AdvancedReportKind.CurrentInventoryValue => await InventoryValueAsync(request, cancellationToken),
            AdvancedReportKind.EmployeeOperations => await EmployeeOperationsAsync(context, request, period, cancellationToken),
            AdvancedReportKind.AdministrativeActivity => await AdministrativeActivityAsync(context, request, period, cancellationToken),
            _ => throw new NotSupportedException($"El reporte {request.Kind} no está implementado.")
        };
    }

    private static AdvancedReportResult SalesResult(
        AdvancedReportDefinition definition,
        SalesAnalyticsReport report,
        IReadOnlyList<string> filters)
    {
        var columns = Columns(
            ("period", "Período", AdvancedReportValueKind.Text, false),
            ("gross", "Ventas brutas", AdvancedReportValueKind.Money, true),
            ("discounts", "Descuentos", AdvancedReportValueKind.Money, true),
            ("returns", "Devoluciones", AdvancedReportValueKind.Money, true),
            ("net", "Ventas netas", AdvancedReportValueKind.Money, true),
            ("transactions", "Transacciones", AdvancedReportValueKind.Integer, true),
            ("units", "Unidades netas", AdvancedReportValueKind.Integer, true));
        var rows = report.Trend.Select(point => Row(
            ("period", point.Label),
            ("gross", Money(point.GrossSalesCentavos)),
            ("discounts", Money(point.DiscountsCentavos)),
            ("returns", Money(point.ReturnsCentavos)),
            ("net", Money(point.NetSalesCentavos)),
            ("transactions", point.Transactions),
            ("units", point.NetUnits))).ToList();
        var summary = new List<AdvancedReportSummaryItem>
        {
            S("Ventas brutas", Money(report.Summary.GrossSales.Value), AdvancedReportValueKind.Money),
            S("Descuentos", Money(report.Summary.Discounts.Value), AdvancedReportValueKind.Money),
            S("Devoluciones", Money(report.Summary.Returns.Value), AdvancedReportValueKind.Money),
            S("Ventas netas", Money(report.Summary.NetSales.Value), AdvancedReportValueKind.Money),
            S("Transacciones", report.Summary.Transactions.Value, AdvancedReportValueKind.Integer),
            S("Ticket promedio", Money(report.Summary.AverageTicket.Value), AdvancedReportValueKind.Money)
        };
        return new AdvancedReportResult(
            definition, report.Period, filters, columns, rows, rows.Count, summary,
            Coverage(report.Coverage), "No existen ventas en este período.");
    }

    private static AdvancedReportResult ProductResult(
        AdvancedReportDefinition definition,
        AnalyticsPeriodDefinition period,
        ProductAnalyticsReport report,
        IReadOnlyDictionary<int, ProductSearchMetadata> metadata,
        AdvancedReportRequest request,
        IReadOnlyList<string> filters)
    {
        IEnumerable<ProductAnalyticsRow> source = report.MostSoldByUnits;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            source = source.Where(row =>
                Contains(row.ProductCode, request.Search) ||
                Contains(row.ProductName, request.Search) ||
                metadata.TryGetValue(row.ProductId, out var product) &&
                (Contains(product.CategoryName, request.Search) ||
                    Contains(product.SubcategoryName, request.Search)));
        }
        if (request.ProductId.HasValue)
        {
            source = source.Where(row => row.ProductId == request.ProductId.Value);
        }
        if (request.CategoryId.HasValue)
        {
            source = source.Where(row => metadata.TryGetValue(row.ProductId, out var product) &&
                product.CategoryId == request.CategoryId.Value);
        }
        if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.ExplicitlyUnassigned)
        {
            source = source.Where(row => metadata.TryGetValue(row.ProductId, out var product) &&
                !product.SubcategoryId.HasValue);
        }
        else if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.HistoricalUnavailable)
        {
            // Product analytics uses the current catalog classification; no
            // historical-unavailable bucket exists for this current report.
            source = source.Where(_ => false);
        }
        else if (request.SubcategoryId.HasValue)
        {
            source = source.Where(row => metadata.TryGetValue(row.ProductId, out var product) &&
                product.SubcategoryId == request.SubcategoryId.Value);
        }
        var all = source.ToList();
        var page = all.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        var rows = page.Select(row => Row(
            ("code", row.ProductCode),
            ("product", row.ProductName),
            ("category", metadata.GetValueOrDefault(row.ProductId)?.CategoryName ?? "Sin snapshot de categoría"),
            ("subcategory", metadata.GetValueOrDefault(row.ProductId)?.SubcategoryName ?? "No disponible (histórico)"),
            ("units", row.NetUnits),
            ("net", Money(row.NetSalesCentavos)),
            ("returns", row.ReturnedUnits),
            ("stock", row.CurrentStock),
            ("days", row.DaysSinceLastSale),
            ("quality", Quality(row.SnapshotQuality)))).ToList();
        return new AdvancedReportResult(
            definition, period, filters,
            Columns(
                ("code", "Código", AdvancedReportValueKind.Text, false),
                ("product", "Producto", AdvancedReportValueKind.Text, false),
                ("category", "Categoría", AdvancedReportValueKind.Text, false),
                ("subcategory", "Subcategoría", AdvancedReportValueKind.Text, false),
                ("units", "Unidades netas", AdvancedReportValueKind.Integer, true),
                ("net", "Ventas netas", AdvancedReportValueKind.Money, true),
                ("returns", "Unidades devueltas", AdvancedReportValueKind.Integer, true),
                ("stock", "Stock actual", AdvancedReportValueKind.Integer, false),
                ("days", "Días desde última venta", AdvancedReportValueKind.Integer, false),
                ("quality", "Cobertura", AdvancedReportValueKind.Text, false)),
            rows, all.Count, [], Coverage(report.Coverage),
            EmptyMessage(request, "No existen productos para los filtros seleccionados."));
    }

    private static AdvancedReportResult ProductProfitabilityResult(
        AdvancedReportDefinition definition,
        ProductProfitabilityReport report,
        IReadOnlyList<string> filters,
        AdvancedReportRequest request)
    {
        var rows = report.Rows.Select(row => Row(
            ("productId", row.ProductId),
            ("code", row.ProductCode),
            ("product", row.ProductName),
            ("category", row.CategoryName),
            ("subcategory", row.SubcategoryName ?? "Sin subcategoría"),
            ("units", row.NetUnits),
            ("salePrice", row.RealizedSalePriceCentavos.HasValue
                ? Money(row.RealizedSalePriceCentavos.Value)
                : null),
            ("knownCost", row.KnownCostCentavos.HasValue
                ? Money(row.KnownCostCentavos.Value)
                : null),
            ("unitCost", row.AppliedUnitCostCentavos.HasValue
                ? Money(row.AppliedUnitCostCentavos.Value)
                : null),
            ("unitGain", row.UnitGainCentavos.HasValue
                ? Money(row.UnitGainCentavos.Value)
                : null),
            ("netSales", Money(row.NetSalesCentavos)),
            ("totalCost", row.TotalCostCentavos.HasValue
                ? Money(row.TotalCostCentavos.Value)
                : null),
            ("totalGain", row.TotalGainCentavos.HasValue
                ? Money(row.TotalGainCentavos.Value)
                : null),
            ("margin", row.MarginPercentage),
            ("coverage", row.CostCoveragePercentage),
            ("coverageState", CoverageState(row.CoverageState)),
            ("returns", row.ReturnedUnits))).ToList();

        var summary = new List<AdvancedReportSummaryItem>
        {
            S("Productos con ventas", report.ProductsWithSales, AdvancedReportValueKind.Integer),
            S("Ventas netas", Money(report.NetSalesCentavos), AdvancedReportValueKind.Money),
            S("Costo conocido", report.KnownCostCentavos.HasValue
                ? Money(report.KnownCostCentavos.Value)
                : null, AdvancedReportValueKind.Money),
            S("Ganancia calculable", report.CalculableGainCentavos.HasValue
                ? Money(report.CalculableGainCentavos.Value)
                : null, AdvancedReportValueKind.Money),
            S("Margen global", report.OverallMarginPercentage, AdvancedReportValueKind.Percentage),
            S("Cobertura de costo", report.CostCoveragePercentage, AdvancedReportValueKind.Percentage)
        };

        return new AdvancedReportResult(
            definition,
            report.Period,
            filters,
            Columns(
                ("code", "Código", AdvancedReportValueKind.Text, false),
                ("product", "Producto", AdvancedReportValueKind.Text, false),
                ("category", "Categoría", AdvancedReportValueKind.Text, false),
                ("subcategory", "Subcategoría", AdvancedReportValueKind.Text, false),
                ("units", "Unidades netas", AdvancedReportValueKind.Integer, true),
                ("salePrice", "Precio venta realizado", AdvancedReportValueKind.Money, false),
                ("knownCost", "Costo conocido", AdvancedReportValueKind.Money, true),
                ("unitCost", "Costo unitario aplicado", AdvancedReportValueKind.Money, false),
                ("unitGain", "Ganancia unitaria", AdvancedReportValueKind.Money, false),
                ("netSales", "Ventas netas", AdvancedReportValueKind.Money, true),
                ("totalCost", "Costo total", AdvancedReportValueKind.Money, true),
                ("totalGain", "Ganancia total", AdvancedReportValueKind.Money, true),
                ("margin", "Margen %", AdvancedReportValueKind.Percentage, false),
                ("coverage", "Cobertura costo", AdvancedReportValueKind.Percentage, false),
                ("coverageState", "Estado costo", AdvancedReportValueKind.Text, false),
                ("returns", "Unidades devueltas", AdvancedReportValueKind.Integer, true)),
            rows,
            report.TotalRows,
            summary,
            report.CoverageNote,
            EmptyMessage(request, "No existen productos para los filtros seleccionados."));
    }

    private static string CoverageState(ProductProfitabilityCoverageState state) => state switch
    {
        ProductProfitabilityCoverageState.Complete => "Completa",
        ProductProfitabilityCoverageState.Partial => "Parcial",
        ProductProfitabilityCoverageState.NoCoverage => "Sin cobertura",
        _ => "Sin ventas"
    };

    private static AdvancedReportResult CategoryResult(
        AdvancedReportDefinition definition,
        AnalyticsPeriodDefinition period,
        CategoryAnalyticsReport report,
        AdvancedReportRequest request,
        IReadOnlyList<string> filters)
    {
        var subcategories = report.Subcategories ?? [];
        if (subcategories.Count > 0)
        {
            IEnumerable<SubcategoryAnalyticsRow> source = subcategories;
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                source = source.Where(row => Contains(row.CategoryName, request.Search) ||
                    Contains(row.SubcategoryName, request.Search));
            }
            if (request.CategoryId.HasValue)
            {
                source = source.Where(row => row.CategoryIdSnapshot == request.CategoryId.Value);
            }
            if (request.SubcategoryFilter != AnalyticsSubcategoryFilter.All)
            {
                source = source.Where(row => MatchesSubcategoryFilter(row, request.SubcategoryFilter));
            }
            else if (request.SubcategoryId.HasValue)
            {
                source = source.Where(row => row.SubcategoryIdSnapshot == request.SubcategoryId.Value);
            }
            var allSubcategories = source.ToList();
            var rows = allSubcategories.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                .Select(row => Row(
                    ("category", row.CategoryName),
                    ("subcategory", row.SubcategoryName),
                    ("units", row.NetUnits),
                    ("net", Money(row.NetSalesCentavos)),
                    ("returns", row.ReturnedUnits),
                    ("participation", row.ParticipationPercentage),
                    ("quality", Quality(row.SnapshotQuality))))
                .ToList();
            return new AdvancedReportResult(
                definition, period, filters,
                Columns(
                    ("category", "Categoría", AdvancedReportValueKind.Text, false),
                    ("subcategory", "Subcategoría", AdvancedReportValueKind.Text, false),
                    ("units", "Unidades netas", AdvancedReportValueKind.Integer, true),
                    ("net", "Ventas netas", AdvancedReportValueKind.Money, true),
                    ("returns", "Unidades devueltas", AdvancedReportValueKind.Integer, true),
                    ("participation", "Participación", AdvancedReportValueKind.Percentage, false),
                    ("quality", "Cobertura", AdvancedReportValueKind.Text, false)),
                rows, allSubcategories.Count, [], Coverage(report.Coverage),
                EmptyMessage(request, "No existen subcategorías para los filtros seleccionados."));
        }

        var categorySource = report.Categories.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            categorySource = categorySource.Where(row => Contains(row.CategoryName, request.Search));
        }
        if (request.CategoryId.HasValue)
        {
            categorySource = categorySource.Where(row => row.CategoryIdSnapshot == request.CategoryId.Value);
        }
        if (request.SubcategoryFilter != AnalyticsSubcategoryFilter.All || request.SubcategoryId.HasValue)
        {
            // Without the detailed subcategory rows there is no safe way to
            // distinguish explicit absence from a pre-snapshot historical line.
            categorySource = Enumerable.Empty<CategoryAnalyticsRow>();
        }
        var all = categorySource.ToList();
        var categoryRows = all.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(row => Row(
                ("category", row.CategoryName),
                ("subcategory", "No disponible (histórico)"),
                ("units", row.NetUnits),
                ("net", Money(row.NetSalesCentavos)),
                ("returns", row.ReturnedUnits),
                ("participation", row.ParticipationPercentage),
                ("quality", Quality(row.SnapshotQuality))))
            .ToList();
        return new AdvancedReportResult(
            definition, period, filters,
            Columns(
                ("category", "Categoría", AdvancedReportValueKind.Text, false),
                ("subcategory", "Subcategoría", AdvancedReportValueKind.Text, false),
                ("units", "Unidades netas", AdvancedReportValueKind.Integer, true),
                ("net", "Ventas netas", AdvancedReportValueKind.Money, true),
                ("returns", "Unidades devueltas", AdvancedReportValueKind.Integer, true),
                ("participation", "Participación", AdvancedReportValueKind.Percentage, false),
                ("quality", "Cobertura", AdvancedReportValueKind.Text, false)),
            categoryRows, all.Count, [], Coverage(report.Coverage),
            EmptyMessage(request, "No existen categorías para los filtros seleccionados."));
    }

    private static bool MatchesSubcategoryFilter(
        SubcategoryAnalyticsRow row,
        AnalyticsSubcategoryFilter filter) => filter switch
        {
            AnalyticsSubcategoryFilter.ExplicitlyUnassigned =>
                !row.SubcategoryIdSnapshot.HasValue &&
                string.Equals(
                    row.SubcategoryName,
                    AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel,
                    StringComparison.Ordinal),
            AnalyticsSubcategoryFilter.HistoricalUnavailable =>
                string.Equals(
                    row.SubcategoryName,
                    AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel,
                    StringComparison.Ordinal),
            _ => true
        };

    private static AdvancedReportResult GrossProfitResult(
        AdvancedReportDefinition definition,
        FinancialAnalyticsReport report,
        IReadOnlyList<string> filters)
    {
        var rows = report.Finance.Trend.Select(point => Row(
            ("period", point.Label),
            ("sales", Money(point.CoveredNetSalesCentavos)),
            ("cost", Money(point.CostOfGoodsSoldCentavos)),
            ("profit", Money(point.GrossProfitCentavos)))).ToList();
        return new AdvancedReportResult(
            definition, report.Period, filters,
            Columns(
                ("period", "Período", AdvancedReportValueKind.Text, false),
                ("sales", "Ventas netas cubiertas", AdvancedReportValueKind.Money, true),
                ("cost", "Costo vendido", AdvancedReportValueKind.Money, true),
                ("profit", "Ganancia bruta", AdvancedReportValueKind.Money, true)),
            rows, rows.Count,
            [
                S("Ventas netas", Money(report.Finance.NetSales.Value), AdvancedReportValueKind.Money),
                S("Costo vendido", Money(report.Finance.CostOfGoodsSold.Value), AdvancedReportValueKind.Money),
                S("Ganancia bruta", Money(report.Finance.GrossProfit.Value), AdvancedReportValueKind.Money),
                S("Margen bruto", report.Finance.GrossMargin.Value, AdvancedReportValueKind.Percentage),
                S("Cobertura histórica", report.Finance.CostCoverage.Percentage, AdvancedReportValueKind.Percentage)
            ],
            $"Cobertura histórica exacta de costo: {report.Finance.CostCoverage.Percentage:N2}%.",
            "No existen operaciones con ganancia calculable en este período.");
    }

    private static AdvancedReportResult CashResult(
        AdvancedReportDefinition definition,
        FinancialAnalyticsReport report,
        IReadOnlyList<string> filters)
    {
        var cash = report.Cash;
        var rows = new List<AdvancedReportRow>
        {
            Row(
                ("opening", Money(cash.OpeningCashCentavos)),
                ("sales", Money(cash.CashSalesCentavos)),
                ("customers", Money(cash.CashCustomerPaymentsCentavos)),
                ("other", Money(cash.OtherCashInputsCentavos)),
                ("cashContributions", Money(cash.CashContributionsCentavos)),
                ("refunds", Money(cash.CashRefundsCentavos)),
                ("suppliers", Money(cash.SupplierCashPaymentsCentavos)),
                ("expenses", Money(cash.ExpensesCentavos)),
                ("employeePayments", Money(cash.EmployeePaymentsCentavos)),
                ("employeePaymentsOutsideCash", Money(cash.EmployeePaymentsOutsideCashCentavos)),
                ("withdrawals", Money(cash.WithdrawalsCentavos)),
                ("expected", Money(cash.ExpectedCashCentavos)),
                ("difference", cash.ClosingDifferenceCentavos.HasValue ? Money(cash.ClosingDifferenceCentavos.Value) : null))
        };
        return new AdvancedReportResult(
            definition, report.Period, filters,
            Columns(
                ("opening", "Apertura", AdvancedReportValueKind.Money, false),
                ("sales", "Ventas efectivo", AdvancedReportValueKind.Money, false),
                ("customers", "Cobros clientes", AdvancedReportValueKind.Money, false),
                ("other", "Otras entradas", AdvancedReportValueKind.Money, false),
                ("cashContributions", "Aportes para caja", AdvancedReportValueKind.Money, false),
                ("refunds", "Reembolsos", AdvancedReportValueKind.Money, false),
                ("suppliers", "Pagos proveedores", AdvancedReportValueKind.Money, false),
                ("expenses", "Gastos", AdvancedReportValueKind.Money, false),
                ("employeePayments", "Pagos empleados desde caja", AdvancedReportValueKind.Money, false),
                ("employeePaymentsOutsideCash", "Pagos empleados fuera de caja", AdvancedReportValueKind.Money, false),
                ("withdrawals", "Retiros", AdvancedReportValueKind.Money, false),
                ("expected", "Efectivo esperado", AdvancedReportValueKind.Money, false),
                ("difference", "Diferencia cierre", AdvancedReportValueKind.Money, false)),
            rows, 1,
            [S("Movimiento neto", Money(cash.NetMovementCentavos), AdvancedReportValueKind.Money)],
            null, "No existen movimientos de caja en este período.");
    }

    private static async Task<ReportData> InventoryAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.ProductoSkus.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(sku => EF.Functions.Like(sku.Producto.CodigoInterno, pattern, "\\") ||
                EF.Functions.Like(sku.CodigoSku, pattern, "\\") ||
                sku.NombreVariante != null && EF.Functions.Like(sku.NombreVariante, pattern, "\\") ||
                EF.Functions.Like(sku.Producto.Nombre, pattern, "\\") ||
                EF.Functions.Like(sku.Producto.Categoria.Nombre, pattern, "\\") ||
                (sku.Producto.Subcategoria != null && EF.Functions.Like(sku.Producto.Subcategoria.Nombre, pattern, "\\")));
        }
        if (request.ProductId.HasValue)
        {
            query = query.Where(sku => sku.ProductoId == request.ProductId.Value);
        }
        if (request.CategoryId.HasValue)
        {
            query = query.Where(sku => sku.Producto.CategoriaId == request.CategoryId.Value);
        }
        if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.ExplicitlyUnassigned)
        {
            query = query.Where(sku => sku.Producto.SubcategoriaId == null);
        }
        else if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.HistoricalUnavailable)
        {
            query = query.Where(_ => false);
        }
        else if (request.SubcategoryId.HasValue)
        {
            query = query.Where(sku => sku.Producto.SubcategoriaId == request.SubcategoryId.Value);
        }
        if (TryEnum<EstadoProducto>(request.State, out var state))
        {
            query = query.Where(sku => sku.Producto.Estado == state);
        }

        var inventory = await query
            .OrderBy(sku => sku.Producto.Nombre)
            .ThenBy(sku => sku.CodigoSku)
            .Select(sku => new InventoryFact(
                sku.ProductoSkuId,
                sku.ProductoId,
                sku.Producto.CodigoInterno,
                sku.CodigoSku,
                sku.Producto.Nombre,
                sku.NombreVariante,
                sku.Producto.Categoria.Nombre,
                sku.Producto.Subcategoria != null ? sku.Producto.Subcategoria.Nombre : "Sin subcategoría",
                sku.Producto.Estado,
                sku.Activo,
                sku.SaldosInventario.Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                    .Sum(balance => (int?)balance.ExistenciaFisica) ?? 0,
                sku.SaldosInventario.Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                    .Sum(balance => (int?)balance.Reservado) ?? 0,
                sku.SaldosInventario.Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
                    .Sum(balance => (int?)balance.ExistenciaFisica) ?? 0,
                sku.SaldosInventario.Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
                    .Sum(balance => (int?)balance.Reservado) ?? 0,
                sku.SaldosInventario.Where(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
                    .Sum(balance => (int?)balance.ExistenciaFisica) ?? 0,
                sku.SaldosInventario.Where(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
                    .Sum(balance => (int?)balance.Reservado) ?? 0,
                sku.MinimoExhibicionOverride ?? sku.Producto.StockMinimo,
                sku.CostoOverrideCentavos ?? sku.Producto.PrecioCompraCentavos,
                sku.DetallesVenta.Select(line => (DateTime?)line.Venta.FechaVenta).Max(),
                sku.Producto.DetallesVenta.Where(line => line.ProductoSkuId == null)
                    .Select(line => (DateTime?)line.Venta.FechaVenta).Max(),
                sku.MovimientosInventario.Select(movement => (DateTime?)movement.Fecha).Max(),
                sku.Producto.FechaCreacion))
            .ToListAsync(cancellationToken);

        var skuIds = inventory.Select(item => item.SkuId).ToList();
        var canonicalWaste = await context.MovimientosInventarioUbicacion.AsNoTracking()
            .Where(item => skuIds.Contains(item.ProductoSkuId) &&
                item.TipoOperacion == TipoOperacionInventario.MERMA &&
                item.Fecha >= period.UtcStart && item.Fecha < period.UtcEndExclusive)
            .GroupBy(item => item.ProductoSkuId)
            .Select(group => new
            {
                SkuId = group.Key,
                Units = group.Sum(item => item.Cantidad),
                Value = group.Sum(item => (long)item.Cantidad *
                    (item.ProductoSku.CostoOverrideCentavos ?? item.ProductoSku.Producto.PrecioCompraCentavos))
            })
            .ToDictionaryAsync(item => item.SkuId, cancellationToken);
        var returnWaste = await context.Mermas.AsNoTracking()
            .Where(item => item.FechaMerma >= period.UtcStart && item.FechaMerma < period.UtcEndExclusive)
            .GroupBy(item => item.ProductoSkuId)
            .Select(group => new
            {
                SkuId = group.Key,
                Units = group.Sum(item => item.Cantidad),
                Value = group.Sum(item => (long)item.Cantidad *
                    (item.ProductoSkuId.HasValue
                        ? item.ProductoSku!.CostoOverrideCentavos ?? item.Producto.PrecioCompraCentavos
                        : item.Producto.PrecioCompraCentavos))
            })
            .ToListAsync(cancellationToken);
        var exactReturnWaste = returnWaste.Where(item => item.SkuId.HasValue)
            .ToDictionary(item => item.SkuId!.Value);
        var legacyWasteUnits = returnWaste.Where(item => !item.SkuId.HasValue).Sum(item => item.Units);
        var legacyWasteValue = returnWaste.Where(item => !item.SkuId.HasValue).Sum(item => item.Value);

        var now = DateTime.UtcNow;
        var health = inventory.Select(item => ToInventoryHealth(
            item,
            canonicalWaste.GetValueOrDefault(item.SkuId)?.Units ?? 0,
            canonicalWaste.GetValueOrDefault(item.SkuId)?.Value ?? 0,
            exactReturnWaste.GetValueOrDefault(item.SkuId)?.Units ?? 0,
            exactReturnWaste.GetValueOrDefault(item.SkuId)?.Value ?? 0,
            now)).ToList();
        var page = health.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        var highest = health.OrderByDescending(item => item.Physical).ThenBy(item => item.SkuCode).FirstOrDefault();
        var lowest = health.OrderBy(item => item.Physical).ThenBy(item => item.SkuCode).FirstOrDefault();
        var physical = health.Sum(item => item.Physical);
        var totalWasteUnits = health.Sum(item => item.WasteUnits) + legacyWasteUnits;
        var totalWasteValue = health.Sum(item => item.WasteValueCentavos) + legacyWasteValue;

        return new ReportData(
            Columns(
                ("code", "Código producto", AdvancedReportValueKind.Text, false),
                ("sku", "SKU", AdvancedReportValueKind.Text, false),
                ("product", "Producto / variante", AdvancedReportValueKind.Text, false),
                ("category", "Categoría", AdvancedReportValueKind.Text, false),
                ("subcategory", "Subcategoría", AdvancedReportValueKind.Text, false),
                ("state", "Estado", AdvancedReportValueKind.Text, false),
                ("physical", "Físico", AdvancedReportValueKind.Integer, true),
                ("exhibition", "Exhibición", AdvancedReportValueKind.Integer, true),
                ("warehouse", "Bodega", AdvancedReportValueKind.Integer, true),
                ("unassigned", "Sin asignar", AdvancedReportValueKind.Integer, true),
                ("reserved", "Reservado", AdvancedReportValueKind.Integer, true),
                ("available", "Disponible", AdvancedReportValueKind.Integer, true),
                ("minimum", "Mínimo exhib.", AdvancedReportValueKind.Integer, false),
                ("situation", "Situación", AdvancedReportValueKind.Text, false),
                ("classification", "Clasificación", AdvancedReportValueKind.Text, false),
                ("belowMinimum", "Bajo mínimo", AdvancedReportValueKind.Boolean, false),
                ("waste", "Mermas período", AdvancedReportValueKind.Integer, true),
                ("wasteValue", "Valor mermas", AdvancedReportValueKind.Money, true),
                ("stagnant", "Inmovilizado 90d", AdvancedReportValueKind.Boolean, false),
                ("lastActivity", "Última actividad", AdvancedReportValueKind.DateTime, false),
                ("salesCoverage", "Cobertura venta SKU", AdvancedReportValueKind.Text, false),
                ("cost", "Costo efectivo", AdvancedReportValueKind.Money, false),
                ("value", "Valor físico", AdvancedReportValueKind.Money, true)),
            page.Select(item => Row(
                ("code", item.ProductCode),
                ("sku", item.SkuCode),
                ("product", item.DisplayName),
                ("category", item.Category),
                ("subcategory", item.Subcategory),
                ("state", item.State),
                ("physical", item.Physical),
                ("exhibition", item.Exhibition),
                ("warehouse", item.Warehouse),
                ("unassigned", item.Unassigned),
                ("reserved", item.Reserved),
                ("available", item.Available),
                ("minimum", item.Minimum),
                ("situation", item.Situation),
                ("classification", item.PendingClassification ? "Pendiente de clasificar" : "Clasificado"),
                ("belowMinimum", item.BelowMinimum),
                ("waste", item.WasteUnits),
                ("wasteValue", Money(item.WasteValueCentavos)),
                ("stagnant", item.Stagnant),
                ("lastActivity", item.LastActivityUtc),
                ("salesCoverage", item.SalesCoverage),
                ("cost", Money(item.CostCentavos)),
                ("value", Money(item.ValueCentavos)))).ToList(),
            health.Count,
            [
                S("Stock físico total", physical, AdvancedReportValueKind.Integer),
                S("Exhibición", health.Sum(item => item.Exhibition), AdvancedReportValueKind.Integer),
                S("Bodega", health.Sum(item => item.Warehouse), AdvancedReportValueKind.Integer),
                S("Sin asignar", health.Sum(item => item.Unassigned), AdvancedReportValueKind.Integer),
                S("Reservado", health.Sum(item => item.Reserved), AdvancedReportValueKind.Integer),
                S("Disponible", health.Sum(item => item.Available), AdvancedReportValueKind.Integer),
                S("Sin existencias", health.Count(item => item.Situation == "Sin existencias"), AdvancedReportValueKind.Integer),
                S("Sin disponibilidad", health.Count(item => item.Situation == "Sin disponibilidad"), AdvancedReportValueKind.Integer),
                S("Bajo mínimo", health.Count(item => item.BelowMinimum), AdvancedReportValueKind.Integer),
                S("Requiere reposición", health.Count(item => item.Situation == "Requiere reposición"), AdvancedReportValueKind.Integer),
                S("Pendiente de clasificar", health.Count(item => item.PendingClassification), AdvancedReportValueKind.Integer),
                S("Mermas del período", totalWasteUnits, AdvancedReportValueKind.Integer),
                S("Valor estimado de mermas", Money(totalWasteValue), AdvancedReportValueKind.Money),
                S("Stock inmovilizado (SKU)", health.Count(item => item.Stagnant), AdvancedReportValueKind.Integer),
                S("SKU sin ventas identificables", health.Count(item => item.NoIdentifiableSales), AdvancedReportValueKind.Integer),
                S("Mayor stock", highest is null ? "—" : $"{highest.SkuCode} · {highest.Physical:N0}"),
                S("Menor stock", lowest is null ? "—" : $"{lowest.SkuCode} · {lowest.Physical:N0}"),
                S("Distribución Exhibición", Percentage(health.Sum(item => item.Exhibition), physical), AdvancedReportValueKind.Percentage),
                S("Distribución Bodega", Percentage(health.Sum(item => item.Warehouse), physical), AdvancedReportValueKind.Percentage),
                S("Distribución Sin asignar", Percentage(health.Sum(item => item.Unassigned), physical), AdvancedReportValueKind.Percentage)
            ],
            "Fuente: inventario_saldos. Mermas valorizadas al costo vigente efectivo; " +
            $"{legacyWasteUnits:N0} unidades históricas sin SKU se conservan solo en el total. " +
            "Una venta histórica sin snapshot SKU no se atribuye a una variante.",
            "No existen SKU para los filtros seleccionados.");
    }

    private static async Task<ReportData> InventoryMovementsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var canonicalQuery = context.MovimientosInventarioUbicacion.AsNoTracking()
            .Where(item => item.Fecha >= period.UtcStart && item.Fecha < period.UtcEndExclusive);
        var legacyQuery = context.MovimientosInventario.AsNoTracking()
            .Where(item => item.FechaMovimiento >= period.UtcStart && item.FechaMovimiento < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            canonicalQuery = canonicalQuery.Where(item => EF.Functions.Like(item.ProductoSku.Producto.CodigoInterno, pattern, "\\") ||
                EF.Functions.Like(item.ProductoSku.CodigoSku, pattern, "\\") ||
                item.ProductoSku.NombreVariante != null && EF.Functions.Like(item.ProductoSku.NombreVariante, pattern, "\\") ||
                EF.Functions.Like(item.ProductoSku.Producto.Nombre, pattern, "\\") ||
                EF.Functions.Like(item.Motivo, pattern, "\\") ||
                EF.Functions.Like(item.Usuario.NombreUsuario, pattern, "\\") ||
                EF.Functions.Like(item.Usuario.NombreCompleto, pattern, "\\"));
            legacyQuery = legacyQuery.Where(item => EF.Functions.Like(item.Producto.CodigoInterno, pattern, "\\") ||
                EF.Functions.Like(item.Producto.Nombre, pattern, "\\") ||
                EF.Functions.Like(item.Motivo, pattern, "\\") ||
                EF.Functions.Like(item.Usuario.NombreUsuario, pattern, "\\") ||
                EF.Functions.Like(item.Usuario.NombreCompleto, pattern, "\\"));
        }
        if (request.ProductId.HasValue)
        {
            canonicalQuery = canonicalQuery.Where(item => item.ProductoSku.ProductoId == request.ProductId.Value);
            legacyQuery = legacyQuery.Where(item => item.ProductoId == request.ProductId.Value);
        }
        if (request.UserId.HasValue)
        {
            canonicalQuery = canonicalQuery.Where(item => item.UsuarioId == request.UserId.Value);
            legacyQuery = legacyQuery.Where(item => item.UsuarioId == request.UserId.Value);
        }
        if (!string.IsNullOrWhiteSpace(request.MovementType))
        {
            canonicalQuery = TryEnum<TipoOperacionInventario>(request.MovementType, out var canonicalType)
                ? canonicalQuery.Where(item => item.TipoOperacion == canonicalType)
                : canonicalQuery.Where(_ => false);
            legacyQuery = TryEnum<TipoMovimientoInventario>(request.MovementType, out var legacyType)
                ? legacyQuery.Where(item => item.TipoMovimiento == legacyType)
                : legacyQuery.Where(_ => false);
        }
        var canonical = await canonicalQuery.Select(item => new MovementFact(
            item.Fecha,
            item.ProductoSku.ProductoId,
            item.UsuarioId,
            item.ProductoSku.Producto.CodigoInterno,
            item.ProductoSku.CodigoSku,
            item.ProductoSku.NombreVariante,
            item.ProductoSku.Producto.Nombre,
            item.TipoOperacion.ToString(),
            item.UbicacionOrigen.HasValue ? item.UbicacionOrigen.Value.ToString() : null,
            item.UbicacionDestino.HasValue ? item.UbicacionDestino.Value.ToString() : null,
            item.Cantidad,
            null,
            null,
            item.Motivo,
            item.Usuario.NombreUsuario,
            true)).ToListAsync(cancellationToken);
        var legacy = await legacyQuery.Select(item => new MovementFact(
            item.FechaMovimiento,
            item.ProductoId,
            item.UsuarioId,
            item.Producto.CodigoInterno,
            "Sin snapshot SKU",
            null,
            item.Producto.Nombre,
            item.TipoMovimiento.ToString(),
            null,
            null,
            item.Cantidad,
            item.StockAnterior,
            item.StockNuevo,
            item.Motivo,
            item.Usuario.NombreUsuario,
            false)).ToListAsync(cancellationToken);
        var historical = legacy.Where(item => !canonical.Any(candidate =>
            candidate.ProductId == item.ProductId && candidate.UserId == item.UserId &&
            Math.Abs(candidate.Quantity) == Math.Abs(item.Quantity) &&
            string.Equals(candidate.Reason.Trim(), item.Reason.Trim(), StringComparison.OrdinalIgnoreCase) &&
            Math.Abs((candidate.DateUtc - item.DateUtc).TotalSeconds) < 2)).ToList();
        var all = canonical.Concat(historical).OrderByDescending(item => item.DateUtc).ToList();
        var page = all.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        return new ReportData(
            Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("code", "Código producto", AdvancedReportValueKind.Text, false),
                ("sku", "SKU", AdvancedReportValueKind.Text, false),
                ("product", "Producto / variante", AdvancedReportValueKind.Text, false),
                ("type", "Tipo", AdvancedReportValueKind.Text, false),
                ("origin", "Origen", AdvancedReportValueKind.Text, false),
                ("destination", "Destino", AdvancedReportValueKind.Text, false),
                ("quantity", "Cantidad", AdvancedReportValueKind.Integer, true),
                ("before", "Stock anterior", AdvancedReportValueKind.Integer, false),
                ("after", "Stock nuevo", AdvancedReportValueKind.Integer, false),
                ("reason", "Motivo", AdvancedReportValueKind.Text, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false),
                ("coverage", "Identidad", AdvancedReportValueKind.Text, false)),
            page.Select(item => Row(
                ("date", item.DateUtc), ("code", item.Code), ("sku", item.SkuCode),
                ("product", string.IsNullOrWhiteSpace(item.Variant) ? item.Product : $"{item.Product} · {item.Variant}"),
                ("type", item.Type), ("quantity", item.Quantity), ("before", item.Before),
                ("origin", item.Origin), ("destination", item.Destination),
                ("after", item.After), ("reason", item.Reason), ("user", item.User),
                ("coverage", item.Canonical ? "SKU exacto" : "Histórico sin SKU"))).ToList(),
            all.Count,
            [
                S("Operaciones canónicas", canonical.Count, AdvancedReportValueKind.Integer),
                S("Movimientos históricos sin duplicar", historical.Count, AdvancedReportValueKind.Integer),
                S("Mermas canónicas", canonical.Where(item => item.Type == TipoOperacionInventario.MERMA.ToString()).Sum(item => item.Quantity), AdvancedReportValueKind.Integer)
            ],
            "Los movimientos v1.10 provienen de movimientos_inventario_ubicacion. Las filas v1.9 se conservan sin atribuirles SKU ni ubicación.",
            "No existen movimientos para los filtros seleccionados.");
    }

    private static async Task<ReportData> PaymentMethodsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.PagosVenta.ExcludeVoidSalePayments(context).AsNoTracking()
            .Where(payment => payment.FechaPago >= period.UtcStart && payment.FechaPago < period.UtcEndExclusive && payment.AfectaCaja);
        if (TryEnum<MetodoPago>(request.PaymentMethod, out var method))
        {
            query = query.Where(payment => payment.MetodoPago == method);
        }
        var grouped = query.GroupBy(payment => payment.MetodoPago)
            .OrderBy(group => group.Key)
            .Select(group => new PaymentMethodFact(group.Key.ToString(), group.Count(), group.Sum(payment => payment.MontoCentavos)));
        return await PageAsync(
            grouped, request,
            Columns(
                ("method", "Método", AdvancedReportValueKind.Text, false),
                ("transactions", "Transacciones", AdvancedReportValueKind.Integer, true),
                ("amount", "Importe cobrado", AdvancedReportValueKind.Money, true)),
            item => Row(("method", item.Method), ("transactions", item.Transactions), ("amount", Money(item.AmountCentavos))),
            [S("Alcance", "Pagos de venta que afectan caja; anticipos aplicados con AfectaCaja=false quedan excluidos.")],
            "No existen pagos de venta para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> ExpensesAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.Gastos.AsNoTracking()
            .Where(item => item.FechaGasto >= period.UtcStart && item.FechaGasto < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.Concepto, pattern, "\\") ||
                (item.Observaciones != null && EF.Functions.Like(item.Observaciones, pattern, "\\")) ||
                (item.ResponsableOrigen != null && EF.Functions.Like(item.ResponsableOrigen, pattern, "\\")));
        }
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioId == request.UserId.Value);
        if (request.CashRegisterId.HasValue) query = query.Where(item => item.CajaId == request.CashRegisterId.Value);
        if (TryEnum<MetodoPago>(request.PaymentMethod, out var method)) query = query.Where(item => item.MetodoPago == method);
        var projected = query.OrderByDescending(item => item.FechaGasto).Select(item => new ExpenseFact(
            item.FechaGasto, item.CategoriaGasto != null ? item.CategoriaGasto.Nombre : "Sin categoría",
            item.Concepto, item.MontoCentavos, item.MetodoPago.HasValue ? item.MetodoPago.Value.ToString() : "-",
            item.CajaId, item.Usuario.NombreUsuario, item.Referencia,
            item.OrigenFondos == OrigenFondos.CAJA_FISICA ? "Caja física" :
                item.OrigenFondos == OrigenFondos.FONDOS_EXTERNOS_DUENO ? "Fondos externos del dueño" :
                item.OrigenFondos == OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA ? "Fondos del negocio fuera de caja" :
                "No especificado (histórico)",
            item.ResponsableOrigen,
            item.CajaId.HasValue && (!item.MetodoPago.HasValue || item.MetodoPago == MetodoPago.EFECTIVO)));
        return await PageAsync(
            projected, request,
            Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("category", "Categoría", AdvancedReportValueKind.Text, false),
                ("concept", "Concepto", AdvancedReportValueKind.Text, false),
                ("amount", "Monto", AdvancedReportValueKind.Money, true),
                ("method", "Método", AdvancedReportValueKind.Text, false),
                ("cash", "Caja", AdvancedReportValueKind.Integer, false),
                ("fundingOrigin", "Origen de fondos", AdvancedReportValueKind.Text, false),
                ("responsible", "Responsable / fuente", AdvancedReportValueKind.Text, false),
                ("cashEffect", "Afecta caja", AdvancedReportValueKind.Text, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false),
                ("reference", "Referencia", AdvancedReportValueKind.Text, false)),
            item => Row(("date", item.DateUtc), ("category", item.Category), ("concept", item.Concept),
                ("amount", Money(item.AmountCentavos)), ("method", item.Method), ("cash", item.CashId),
                ("fundingOrigin", item.FundingOrigin), ("responsible", item.Responsible),
                ("cashEffect", item.AffectsCash ? "Sí" : "No"), ("user", item.User), ("reference", item.Reference)),
            [], "No existen gastos para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> WithdrawalsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.RetirosCaja.AsNoTracking()
            .Where(item => item.FechaRetiro >= period.UtcStart && item.FechaRetiro < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.Motivo, pattern, "\\") ||
                EF.Functions.Like(item.Usuario.NombreUsuario, pattern, "\\") ||
                EF.Functions.Like(item.Usuario.NombreCompleto, pattern, "\\"));
        }
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioId == request.UserId.Value);
        if (request.CashRegisterId.HasValue) query = query.Where(item => item.CajaId == request.CashRegisterId.Value);
        var projected = query.OrderByDescending(item => item.FechaRetiro).Select(item => new WithdrawalFact(
            item.FechaRetiro, item.CajaId, item.MontoCentavos, item.Motivo, item.Usuario.NombreUsuario));
        return await PageAsync(
            projected, request,
            Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("cash", "Caja", AdvancedReportValueKind.Integer, false),
                ("amount", "Monto", AdvancedReportValueKind.Money, true),
                ("reason", "Motivo", AdvancedReportValueKind.Text, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false)),
            item => Row(("date", item.DateUtc), ("cash", item.CashId), ("amount", Money(item.AmountCentavos)),
                ("reason", item.Reason), ("user", item.User)),
            [S("Clasificación", "Retiros de caja; no se presentan como gasto operativo.")],
            "No existen retiros para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> PurchasesAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.Compras.AsNoTracking()
            .Where(item => item.FechaCompra >= period.UtcStart && item.FechaCompra < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoCompra, pattern, "\\") ||
                EF.Functions.Like(item.Proveedor.CodigoProveedor, pattern, "\\") ||
                EF.Functions.Like(item.Proveedor.NombreRazonSocial, pattern, "\\"));
        }
        if (request.SupplierId.HasValue) query = query.Where(item => item.ProveedorId == request.SupplierId.Value);
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioRegistroId == request.UserId.Value);
        if (TryEnum<EstadoFinancieroCompra>(request.State, out var financialState))
            query = query.Where(item => item.EstadoFinanciero == financialState);
        var projected = query.OrderByDescending(item => item.FechaCompra).Select(item => new PurchaseFact(
            item.FechaCompra, item.CodigoCompra, item.Proveedor.CodigoProveedor, item.Proveedor.NombreRazonSocial,
            item.EstadoRecepcion.ToString(), item.EstadoFinanciero.ToString(), item.TotalCentavos,
            item.TotalPagadoCentavos, item.SaldoPendienteCentavos, item.UsuarioRegistro.NombreUsuario));
        return await PageAsync(
            projected, request,
            Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("code", "Compra", AdvancedReportValueKind.Text, false),
                ("supplierCode", "Código proveedor", AdvancedReportValueKind.Text, false),
                ("supplier", "Proveedor", AdvancedReportValueKind.Text, false),
                ("reception", "Recepción", AdvancedReportValueKind.Text, false),
                ("financial", "Estado financiero", AdvancedReportValueKind.Text, false),
                ("total", "Total", AdvancedReportValueKind.Money, true),
                ("paid", "Pagado", AdvancedReportValueKind.Money, true),
                ("balance", "Saldo", AdvancedReportValueKind.Money, true),
                ("user", "Usuario", AdvancedReportValueKind.Text, false)),
            item => Row(("date", item.DateUtc), ("code", item.Code), ("supplierCode", item.SupplierCode),
                ("supplier", item.Supplier), ("reception", item.ReceptionState), ("financial", item.FinancialState),
                ("total", Money(item.TotalCentavos)), ("paid", Money(item.PaidCentavos)),
                ("balance", Money(item.BalanceCentavos)), ("user", item.User)),
            [], "No existen compras para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> PurchaseReceptionsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.RecepcionCompraDetalles.AsNoTracking()
            .Where(item => item.RecepcionCompra.FechaRecepcion >= period.UtcStart &&
                item.RecepcionCompra.FechaRecepcion < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.RecepcionCompra.CodigoRecepcion, pattern, "\\") ||
                EF.Functions.Like(item.RecepcionCompra.Compra.CodigoCompra, pattern, "\\") ||
                EF.Functions.Like(item.Producto.CodigoInterno, pattern, "\\") ||
                EF.Functions.Like(item.Producto.Nombre, pattern, "\\") ||
                (item.Producto.Subcategoria != null && EF.Functions.Like(item.Producto.Subcategoria.Nombre, pattern, "\\")) ||
                EF.Functions.Like(item.RecepcionCompra.Compra.Proveedor.CodigoProveedor, pattern, "\\") ||
                EF.Functions.Like(item.RecepcionCompra.Compra.Proveedor.NombreRazonSocial, pattern, "\\"));
        }
        if (request.SupplierId.HasValue)
            query = query.Where(item => item.RecepcionCompra.Compra.ProveedorId == request.SupplierId.Value);
        if (request.ProductId.HasValue) query = query.Where(item => item.ProductoId == request.ProductId.Value);
        if (request.CategoryId.HasValue) query = query.Where(item => item.Producto.CategoriaId == request.CategoryId.Value);
        if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.ExplicitlyUnassigned)
            query = query.Where(item => item.Producto.SubcategoriaId == null);
        else if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.HistoricalUnavailable)
            query = query.Where(_ => false);
        else if (request.SubcategoryId.HasValue)
            query = query.Where(item => item.Producto.SubcategoriaId == request.SubcategoryId.Value);
        if (request.UserId.HasValue)
            query = query.Where(item => item.RecepcionCompra.UsuarioRecepcionId == request.UserId.Value);
        var projected = query.OrderByDescending(item => item.RecepcionCompra.FechaRecepcion)
            .Select(item => new ReceptionFact(
                item.RecepcionCompra.FechaRecepcion,
                item.RecepcionCompra.CodigoRecepcion,
                item.RecepcionCompra.Compra.CodigoCompra,
                item.RecepcionCompra.Compra.Proveedor.NombreRazonSocial,
                item.Producto.CodigoInterno,
                item.Producto.Nombre,
                item.Producto.Categoria.Nombre,
                item.Producto.Subcategoria != null ? item.Producto.Subcategoria.Nombre : "Sin subcategoría",
                item.CantidadRecibida,
                item.CostoNetoUnitarioCentavos ?? item.CostoUnitarioCentavos,
                item.CantidadRecibida * (item.CostoNetoUnitarioCentavos ?? item.CostoUnitarioCentavos),
                item.RecepcionCompra.UsuarioRecepcion.NombreUsuario,
                item.CostoNetoUnitarioCentavos.HasValue));
        return await PageAsync(
            projected, request,
            Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("reception", "Recepción", AdvancedReportValueKind.Text, false),
                ("purchase", "Compra", AdvancedReportValueKind.Text, false),
                ("supplier", "Proveedor", AdvancedReportValueKind.Text, false),
                ("productCode", "Código producto", AdvancedReportValueKind.Text, false),
                ("product", "Producto", AdvancedReportValueKind.Text, false),
                ("category", "Categoría", AdvancedReportValueKind.Text, false),
                ("subcategory", "Subcategoría", AdvancedReportValueKind.Text, false),
                ("quantity", "Cantidad", AdvancedReportValueKind.Integer, true),
                ("unitCost", "Costo unitario neto", AdvancedReportValueKind.Money, false),
                ("value", "Valor recibido", AdvancedReportValueKind.Money, true),
                ("user", "Usuario", AdvancedReportValueKind.Text, false),
                ("coverage", "Cobertura", AdvancedReportValueKind.Text, false)),
            item => Row(("date", item.DateUtc), ("reception", item.ReceptionCode), ("purchase", item.PurchaseCode),
                ("supplier", item.Supplier), ("productCode", item.ProductCode), ("product", item.Product),
                ("category", item.Category), ("subcategory", item.Subcategory),
                ("quantity", item.Quantity), ("unitCost", Money(item.UnitCostCentavos)),
                ("value", Money(item.ValueCentavos)), ("user", item.User),
                ("coverage", item.ExactNetCost ? "Exacta" : "Costo bruto histórico")),
            [], "No existen recepciones para los filtros seleccionados.", cancellationToken,
            coverageNote: "La columna Cobertura identifica si existe costo unitario neto histórico exacto.");
    }

    private static async Task<ReportData> SuppliersAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        CancellationToken cancellationToken)
    {
        var query = context.Proveedores.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoProveedor, pattern, "\\") ||
                EF.Functions.Like(item.NombreRazonSocial, pattern, "\\") ||
                (item.NombreContacto != null && EF.Functions.Like(item.NombreContacto, pattern, "\\")) ||
                (item.Telefono != null && EF.Functions.Like(item.Telefono, pattern, "\\")) ||
                (item.Correo != null && EF.Functions.Like(item.Correo, pattern, "\\")));
        }
        if (request.SupplierId.HasValue) query = query.Where(item => item.ProveedorId == request.SupplierId.Value);
        if (TryEnum<EstadoProveedor>(request.State, out var state)) query = query.Where(item => item.Estado == state);
        var projected = query.OrderBy(item => item.NombreRazonSocial).Select(item => new SupplierFact(
            item.CodigoProveedor, item.NombreRazonSocial, item.Tipo.ToString(), item.Estado.ToString(),
            item.NombreContacto, item.Telefono, item.Correo, item.Productos.Count(product => product.Activo),
            item.Compras.Count, item.FechaCreacion));
        return await PageAsync(
            projected, request,
            Columns(
                ("code", "Código", AdvancedReportValueKind.Text, false),
                ("supplier", "Proveedor", AdvancedReportValueKind.Text, false),
                ("type", "Tipo", AdvancedReportValueKind.Text, false),
                ("state", "Estado", AdvancedReportValueKind.Text, false),
                ("contact", "Contacto", AdvancedReportValueKind.Text, false),
                ("phone", "Teléfono", AdvancedReportValueKind.Text, false),
                ("email", "Correo", AdvancedReportValueKind.Text, false),
                ("products", "Productos activos", AdvancedReportValueKind.Integer, true),
                ("purchases", "Compras", AdvancedReportValueKind.Integer, true),
                ("created", "Fecha de alta", AdvancedReportValueKind.DateTime, false)),
            item => Row(("code", item.Code), ("supplier", item.Supplier), ("type", item.Type),
                ("state", item.State), ("contact", item.Contact), ("phone", item.Phone), ("email", item.Email),
                ("products", item.Products), ("purchases", item.Purchases), ("created", item.CreatedUtc)),
            [], "No existen proveedores para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> CustomersAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        CancellationToken cancellationToken)
    {
        var query = context.Clientes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoCliente, pattern, "\\") ||
                EF.Functions.Like(item.NombreCompleto, pattern, "\\") ||
                (item.TelefonoCelular != null && EF.Functions.Like(item.TelefonoCelular, pattern, "\\")) ||
                (item.CorreoElectronico != null && EF.Functions.Like(item.CorreoElectronico, pattern, "\\")));
        }
        if (request.CustomerId.HasValue) query = query.Where(item => item.ClienteId == request.CustomerId.Value);
        if (TryEnum<EstadoCliente>(request.State, out var state)) query = query.Where(item => item.Estado == state);
        var projected = query.OrderBy(item => item.NombreCompleto).Select(item => new CustomerFact(
            item.CodigoCliente, item.NombreCompleto, item.Estado.ToString(), item.TelefonoCelular,
            item.CorreoElectronico, item.CreditoHabilitado, item.CreditoLimiteCentavos,
            item.Ventas.Count, item.CuentasPorCobrar.Count(account => account.SaldoActualCentavos > 0),
            item.CuentasPorCobrar.Sum(account => (long?)account.SaldoActualCentavos) ?? 0, item.FechaCreacion));
        return await PageAsync(
            projected, request,
            Columns(
                ("code", "Código", AdvancedReportValueKind.Text, false),
                ("customer", "Cliente", AdvancedReportValueKind.Text, false),
                ("state", "Estado", AdvancedReportValueKind.Text, false),
                ("phone", "Teléfono", AdvancedReportValueKind.Text, false),
                ("email", "Correo", AdvancedReportValueKind.Text, false),
                ("credit", "Crédito habilitado", AdvancedReportValueKind.Boolean, false),
                ("limit", "Límite de crédito", AdvancedReportValueKind.Money, false),
                ("sales", "Ventas", AdvancedReportValueKind.Integer, true),
                ("accounts", "Cuentas con saldo", AdvancedReportValueKind.Integer, true),
                ("balance", "Saldo CxC", AdvancedReportValueKind.Money, true),
                ("created", "Fecha de alta", AdvancedReportValueKind.DateTime, false)),
            item => Row(("code", item.Code), ("customer", item.Customer), ("state", item.State),
                ("phone", item.Phone), ("email", item.Email), ("credit", item.CreditEnabled),
                ("limit", item.CreditLimitCentavos.HasValue ? Money(item.CreditLimitCentavos.Value) : null),
                ("sales", item.Sales), ("accounts", item.OpenAccounts), ("balance", Money(item.BalanceCentavos)),
                ("created", item.CreatedUtc)),
            [], "No existen clientes para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> AccountsReceivableAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.CuentasPorCobrar.AsNoTracking()
            .Where(item => item.FechaCreacion >= period.UtcStart && item.FechaCreacion < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoCuenta, pattern, "\\") ||
                EF.Functions.Like(item.Cliente.CodigoCliente, pattern, "\\") ||
                EF.Functions.Like(item.Cliente.NombreCompleto, pattern, "\\") ||
                EF.Functions.Like(item.Venta.NumeroVenta, pattern, "\\"));
        }
        if (request.CustomerId.HasValue) query = query.Where(item => item.ClienteId == request.CustomerId.Value);
        if (TryEnum<EstadoCuentaPorCobrar>(request.State, out var state)) query = query.Where(item => item.Estado == state);
        var projected = query.OrderByDescending(item => item.FechaCreacion).Select(item => new ReceivableFact(
            item.FechaCreacion, item.CodigoCuenta, item.Venta.NumeroVenta, item.Cliente.CodigoCliente,
            item.Cliente.NombreCompleto, item.FechaVencimiento, item.Estado.ToString(), item.TotalOriginalCentavos,
            item.TotalPagadoCentavos, item.SaldoActualCentavos, item.InteresPorcentaje));
        return await PageAsync(
            projected, request, ObligationColumns("Cliente"),
            item => Row(("date", item.CreatedUtc), ("account", item.AccountCode), ("source", item.SaleNumber),
                ("counterpartyCode", item.CustomerCode), ("counterparty", item.Customer), ("due", item.DueUtc),
                ("state", item.State), ("original", Money(item.OriginalCentavos)), ("paid", Money(item.PaidCentavos)),
                ("balance", Money(item.BalanceCentavos)), ("interest", item.InterestPercentage)),
            [], "No existen cuentas por cobrar para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> CustomerPaymentsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.AbonosCuentaPorCobrar.AsNoTracking()
            .Where(item => item.FechaAbono >= period.UtcStart && item.FechaAbono < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoAbono, pattern, "\\") ||
                EF.Functions.Like(item.CuentaPorCobrar.CodigoCuenta, pattern, "\\") ||
                EF.Functions.Like(item.CuentaPorCobrar.Cliente.CodigoCliente, pattern, "\\") ||
                EF.Functions.Like(item.CuentaPorCobrar.Cliente.NombreCompleto, pattern, "\\"));
        }
        if (request.CustomerId.HasValue) query = query.Where(item => item.CuentaPorCobrar.ClienteId == request.CustomerId.Value);
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioId == request.UserId.Value);
        if (request.CashRegisterId.HasValue) query = query.Where(item => item.CajaId == request.CashRegisterId.Value);
        if (TryEnum<MetodoPago>(request.PaymentMethod, out var method)) query = query.Where(item => item.MetodoPago == method);
        var projected = query.OrderByDescending(item => item.FechaAbono).Select(item => new CustomerPaymentFact(
            item.FechaAbono, item.CodigoAbono, item.CuentaPorCobrar.CodigoCuenta,
            item.CuentaPorCobrar.Cliente.CodigoCliente, item.CuentaPorCobrar.Cliente.NombreCompleto,
            item.MetodoPago.ToString(), item.MontoCentavos, item.AplicadoCapitalCentavos,
            item.AplicadoInteresCentavos, item.SaldoNuevoCentavos, item.CajaId, item.Usuario.NombreUsuario, item.Referencia));
        return await PageAsync(
            projected, request, PaymentColumns("Cliente"),
            item => Row(("date", item.DateUtc), ("payment", item.PaymentCode), ("account", item.AccountCode),
                ("counterpartyCode", item.CustomerCode), ("counterparty", item.Customer), ("method", item.Method),
                ("amount", Money(item.AmountCentavos)), ("capital", Money(item.CapitalCentavos)),
                ("interest", Money(item.InterestCentavos)), ("balance", Money(item.BalanceCentavos)),
                ("cash", item.CashId), ("user", item.User), ("reference", item.Reference)),
            [], "No existen pagos de clientes para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> AccountsPayableAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.CuentasPorPagar.AsNoTracking()
            .Where(item => item.FechaCreacion >= period.UtcStart && item.FechaCreacion < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoCuenta, pattern, "\\") ||
                EF.Functions.Like(item.Proveedor.CodigoProveedor, pattern, "\\") ||
                EF.Functions.Like(item.Proveedor.NombreRazonSocial, pattern, "\\") ||
                EF.Functions.Like(item.Compra.CodigoCompra, pattern, "\\"));
        }
        if (request.SupplierId.HasValue) query = query.Where(item => item.ProveedorId == request.SupplierId.Value);
        if (TryEnum<EstadoCuentaPorPagar>(request.State, out var state)) query = query.Where(item => item.Estado == state);
        var projected = query.OrderByDescending(item => item.FechaCreacion).Select(item => new PayableFact(
            item.FechaCreacion, item.CodigoCuenta, item.Compra.CodigoCompra, item.Proveedor.CodigoProveedor,
            item.Proveedor.NombreRazonSocial, item.FechaVencimiento, item.Estado.ToString(), item.TotalOriginalCentavos,
            item.TotalPagadoCentavos, item.SaldoPendienteCentavos));
        return await PageAsync(
            projected, request, ObligationColumns("Proveedor"),
            item => Row(("date", item.CreatedUtc), ("account", item.AccountCode), ("source", item.PurchaseCode),
                ("counterpartyCode", item.SupplierCode), ("counterparty", item.Supplier), ("due", item.DueUtc),
                ("state", item.State), ("original", Money(item.OriginalCentavos)), ("paid", Money(item.PaidCentavos)),
                ("balance", Money(item.BalanceCentavos)), ("interest", null)),
            [], "No existen cuentas por pagar para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> SupplierPaymentsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var initial = context.PagosCompra.AsNoTracking()
            .Where(item => item.FechaPago >= period.UtcStart && item.FechaPago < period.UtcEndExclusive);
        var installments = context.AbonosCuentaPorPagar.AsNoTracking()
            .Where(item => item.FechaAbono >= period.UtcStart && item.FechaAbono < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            initial = initial.Where(item => EF.Functions.Like(item.CodigoPago, pattern, "\\") ||
                EF.Functions.Like(item.Compra.CodigoCompra, pattern, "\\") ||
                EF.Functions.Like(item.Compra.Proveedor.CodigoProveedor, pattern, "\\") ||
                EF.Functions.Like(item.Compra.Proveedor.NombreRazonSocial, pattern, "\\"));
            installments = installments.Where(item => EF.Functions.Like(item.CodigoAbono, pattern, "\\") ||
                EF.Functions.Like(item.CuentaPorPagar.CodigoCuenta, pattern, "\\") ||
                EF.Functions.Like(item.CuentaPorPagar.Proveedor.CodigoProveedor, pattern, "\\") ||
                EF.Functions.Like(item.CuentaPorPagar.Proveedor.NombreRazonSocial, pattern, "\\"));
        }
        if (request.SupplierId.HasValue)
        {
            initial = initial.Where(item => item.Compra.ProveedorId == request.SupplierId.Value);
            installments = installments.Where(item => item.CuentaPorPagar.ProveedorId == request.SupplierId.Value);
        }
        if (request.UserId.HasValue)
        {
            initial = initial.Where(item => item.UsuarioId == request.UserId.Value);
            installments = installments.Where(item => item.UsuarioId == request.UserId.Value);
        }
        if (request.CashRegisterId.HasValue)
        {
            initial = initial.Where(item => item.CajaId == request.CashRegisterId.Value);
            installments = installments.Where(item => item.CajaId == request.CashRegisterId.Value);
        }
        if (TryEnum<MetodoPago>(request.PaymentMethod, out var method))
        {
            initial = initial.Where(item => item.MetodoPago == method);
            installments = installments.Where(item => item.MetodoPago == method);
        }
        var total = await initial.CountAsync(cancellationToken) + await installments.CountAsync(cancellationToken);
        var take = checked(request.Page * request.PageSize);
        var initialRows = await initial.OrderByDescending(item => item.FechaPago).Take(take)
            .Select(item => new SupplierPaymentFact(
                item.FechaPago, item.CodigoPago,
                item.Compra.CuentaPorPagar != null ? item.Compra.CuentaPorPagar.CodigoCuenta : item.Compra.CodigoCompra,
                item.Compra.ProveedorId, item.Compra.Proveedor.CodigoProveedor, item.Compra.Proveedor.NombreRazonSocial,
                item.MetodoPago, item.MontoCentavos, item.MontoCentavos, 0, item.Compra.SaldoPendienteCentavos,
                item.CajaId, item.UsuarioId, item.Usuario.NombreUsuario, item.Referencia, "Pago inicial"))
            .ToListAsync(cancellationToken);
        var installmentRows = await installments.OrderByDescending(item => item.FechaAbono).Take(take)
            .Select(item => new SupplierPaymentFact(
                item.FechaAbono, item.CodigoAbono, item.CuentaPorPagar.CodigoCuenta,
                item.CuentaPorPagar.ProveedorId, item.CuentaPorPagar.Proveedor.CodigoProveedor,
                item.CuentaPorPagar.Proveedor.NombreRazonSocial, item.MetodoPago, item.MontoCentavos,
                item.MontoCentavos, 0, item.SaldoNuevoCentavos, item.CajaId, item.UsuarioId,
                item.Usuario.NombreUsuario, item.Referencia, "Abono"))
            .ToListAsync(cancellationToken);
        var page = initialRows.Concat(installmentRows).OrderByDescending(item => item.DateUtc)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        var columns = PaymentColumns("Proveedor").Concat([new AdvancedReportColumn("type", "Tipo")]).ToList();
        var rows = page.Select(item => Row(("date", item.DateUtc), ("payment", item.PaymentCode), ("account", item.AccountCode),
                ("counterpartyCode", item.SupplierCode), ("counterparty", item.Supplier), ("method", item.MethodValue.ToString()),
                ("amount", Money(item.AmountCentavos)), ("capital", Money(item.CapitalCentavos)),
                ("interest", Money(item.InterestCentavos)), ("balance", Money(item.BalanceCentavos)),
                ("cash", item.CashId), ("user", item.User), ("reference", item.Reference), ("type", item.Type))).ToList();
        return new ReportData(columns, rows, total, [], null,
            "No existen pagos a proveedores para los filtros seleccionados.");
    }

    private static async Task<ReportData> ReturnsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.Devoluciones.AsNoTracking()
            .Where(item => item.FechaDevolucion >= period.UtcStart && item.FechaDevolucion < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.NumeroDevolucion, pattern, "\\") ||
                EF.Functions.Like(item.Venta.NumeroVenta, pattern, "\\") ||
                EF.Functions.Like(item.Venta.ClienteNombreSnapshot, pattern, "\\") ||
                EF.Functions.Like(item.Motivo, pattern, "\\"));
        }
        if (request.CustomerId.HasValue) query = query.Where(item => item.Venta.ClienteId == request.CustomerId.Value);
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioId == request.UserId.Value);
        if (request.CashRegisterId.HasValue) query = query.Where(item => item.CajaId == request.CashRegisterId.Value);
        var projected = query.OrderByDescending(item => item.FechaDevolucion).Select(item => new ReturnFact(
            item.FechaDevolucion, item.NumeroDevolucion, item.Venta.NumeroVenta, item.Venta.ClienteNombreSnapshot,
            item.TipoCorreccion.ToString(), item.TotalDevueltoCentavos, item.Motivo, item.Usuario.NombreUsuario,
            item.Detalles.Sum(detail => (int?)detail.Cantidad) ?? 0));
        return await PageAsync(
            projected, request,
            Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("return", "Devolución", AdvancedReportValueKind.Text, false),
                ("sale", "Venta", AdvancedReportValueKind.Text, false),
                ("customer", "Cliente", AdvancedReportValueKind.Text, false),
                ("type", "Tipo", AdvancedReportValueKind.Text, false),
                ("units", "Unidades", AdvancedReportValueKind.Integer, true),
                ("amount", "Importe", AdvancedReportValueKind.Money, true),
                ("reason", "Motivo", AdvancedReportValueKind.Text, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false)),
            item => Row(("date", item.DateUtc), ("return", item.ReturnNumber), ("sale", item.SaleNumber),
                ("customer", item.Customer), ("type", item.Type), ("units", item.Units),
                ("amount", Money(item.AmountCentavos)), ("reason", item.Reason), ("user", item.User)),
            [], "No existen devoluciones para los filtros seleccionados.", cancellationToken);
    }

    private static async Task<ReportData> RefundsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var sales = context.Reembolsos.AsNoTracking()
            .Where(item => item.FechaReembolso >= period.UtcStart && item.FechaReembolso < period.UtcEndExclusive);
        var orders = context.ReembolsosPedidoPersonalizado.AsNoTracking()
            .Where(item => item.Fecha >= period.UtcStart && item.Fecha < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            sales = sales.Where(item => EF.Functions.Like(item.Devolucion.NumeroDevolucion, pattern, "\\") ||
                EF.Functions.Like(item.Devolucion.Venta.ClienteNombreSnapshot, pattern, "\\") ||
                EF.Functions.Like(item.Motivo, pattern, "\\"));
            orders = orders.Where(item => EF.Functions.Like(item.PedidoPersonalizado.CodigoPedido, pattern, "\\") ||
                EF.Functions.Like(item.PedidoPersonalizado.NombreDestinatarioSnapshot, pattern, "\\") ||
                EF.Functions.Like(item.Motivo, pattern, "\\"));
        }
        if (request.CustomerId.HasValue)
        {
            sales = sales.Where(item => item.Devolucion.Venta.ClienteId == request.CustomerId.Value);
            orders = orders.Where(item => item.PedidoPersonalizado.ClienteId == request.CustomerId.Value);
        }
        if (request.UserId.HasValue)
        {
            sales = sales.Where(item => item.UsuarioId == request.UserId.Value);
            orders = orders.Where(item => item.UsuarioId == request.UserId.Value);
        }
        if (request.CashRegisterId.HasValue)
        {
            sales = sales.Where(item => item.CajaId == request.CashRegisterId.Value);
            orders = orders.Where(item => item.CajaId == request.CashRegisterId.Value);
        }
        if (!string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            if (TryEnum<MetodoReembolso>(request.PaymentMethod, out var saleMethod))
                sales = sales.Where(item => item.MetodoReembolso == saleMethod);
            if (TryEnum<MetodoPago>(request.PaymentMethod, out var orderMethod))
                orders = orders.Where(item => item.MetodoPago == orderMethod);
        }
        var total = await sales.CountAsync(cancellationToken) + await orders.CountAsync(cancellationToken);
        var take = checked(request.Page * request.PageSize);
        var saleRows = await sales.OrderByDescending(item => item.FechaReembolso).Take(take)
            .Select(item => new RefundFact(
                item.FechaReembolso, "Venta", item.Devolucion.NumeroDevolucion, item.Devolucion.Venta.ClienteId,
                item.Devolucion.Venta.ClienteNombreSnapshot, item.MetodoReembolso.ToString(), item.MontoCentavos,
                item.Motivo, item.Usuario.NombreUsuario, item.UsuarioId, item.CajaId, item.Referencia))
            .ToListAsync(cancellationToken);
        var orderRows = await orders.OrderByDescending(item => item.Fecha).Take(take)
            .Select(item => new RefundFact(
                item.Fecha, "Pedido", item.PedidoPersonalizado.CodigoPedido, item.PedidoPersonalizado.ClienteId,
                item.PedidoPersonalizado.NombreDestinatarioSnapshot, item.MetodoPago.ToString(), item.MontoCentavos,
                item.Motivo, item.Usuario.NombreUsuario, item.UsuarioId, item.CajaId, item.Referencia))
            .ToListAsync(cancellationToken);
        var page = saleRows.Concat(orderRows).OrderByDescending(item => item.DateUtc)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        var columns = Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("sourceType", "Origen", AdvancedReportValueKind.Text, false),
                ("source", "Documento", AdvancedReportValueKind.Text, false),
                ("customer", "Cliente", AdvancedReportValueKind.Text, false),
                ("method", "Método", AdvancedReportValueKind.Text, false),
                ("amount", "Monto", AdvancedReportValueKind.Money, true),
                ("reason", "Motivo", AdvancedReportValueKind.Text, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false),
                ("cash", "Caja", AdvancedReportValueKind.Integer, false),
                ("reference", "Referencia", AdvancedReportValueKind.Text, false));
        var rows = page.Select(item => Row(("date", item.DateUtc), ("sourceType", item.SourceType), ("source", item.SourceCode),
                ("customer", item.Customer), ("method", item.Method), ("amount", Money(item.AmountCentavos)),
                ("reason", item.Reason), ("user", item.User), ("cash", item.CashId), ("reference", item.Reference))).ToList();
        return new ReportData(columns, rows, total, [], null,
            "No existen reembolsos para los filtros seleccionados.");
    }

    private static async Task<ReportData> CancellationsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var sales = context.Ventas.AsNoTracking()
            .Where(item => item.Estado == EstadoVenta.ANULADA && item.FechaVenta >= period.UtcStart && item.FechaVenta < period.UtcEndExclusive);
        var quotes = context.Cotizaciones.AsNoTracking()
            .Where(item => item.Estado == EstadoCotizacion.CANCELADA &&
                (item.FechaCierre ?? item.FechaActualizacion) >= period.UtcStart &&
                (item.FechaCierre ?? item.FechaActualizacion) < period.UtcEndExclusive);
        var orders = context.PedidosPersonalizados.AsNoTracking()
            .Where(item => item.Estado == EstadoPedidoPersonalizado.CANCELADO && item.FechaCancelacion.HasValue &&
                item.FechaCancelacion.Value >= period.UtcStart && item.FechaCancelacion.Value < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            sales = sales.Where(item => EF.Functions.Like(item.NumeroVenta, pattern, "\\") ||
                EF.Functions.Like(item.ClienteNombreSnapshot, pattern, "\\") ||
                (item.Observaciones != null && EF.Functions.Like(item.Observaciones, pattern, "\\")));
            quotes = quotes.Where(item => EF.Functions.Like(item.CodigoCotizacion, pattern, "\\") ||
                EF.Functions.Like(item.NombreDestinatarioSnapshot, pattern, "\\") ||
                (item.MotivoCancelacion != null && EF.Functions.Like(item.MotivoCancelacion, pattern, "\\")));
            orders = orders.Where(item => EF.Functions.Like(item.CodigoPedido, pattern, "\\") ||
                EF.Functions.Like(item.NombreDestinatarioSnapshot, pattern, "\\") ||
                (item.MotivoCancelacion != null && EF.Functions.Like(item.MotivoCancelacion, pattern, "\\")));
        }
        if (request.CustomerId.HasValue)
        {
            sales = sales.Where(item => item.ClienteId == request.CustomerId.Value);
            quotes = quotes.Where(item => item.ClienteId == request.CustomerId.Value);
            orders = orders.Where(item => item.ClienteId == request.CustomerId.Value);
        }
        if (request.UserId.HasValue)
        {
            sales = sales.Where(item => item.UsuarioId == request.UserId.Value);
            quotes = quotes.Where(item => item.UsuarioUltimaActualizacionId == request.UserId.Value);
            orders = orders.Where(item => item.UsuarioUltimaActualizacionId == request.UserId.Value);
        }
        var total = await sales.CountAsync(cancellationToken) + await quotes.CountAsync(cancellationToken) +
            await orders.CountAsync(cancellationToken);
        var take = checked(request.Page * request.PageSize);
        var saleRows = await sales.OrderByDescending(item => item.FechaVenta).Take(take)
            .Select(item => new CancellationFact(
                item.FechaVenta, "Venta anulada", item.NumeroVenta, item.ClienteId, item.ClienteNombreSnapshot,
                item.TotalCentavos, item.Observaciones ?? "Sin motivo registrado", item.UsuarioId, item.Usuario.NombreUsuario,
                "La venta no registra fecha de anulación separada; se agrupa por fecha de venta."))
            .ToListAsync(cancellationToken);
        var quoteRows = await quotes.OrderByDescending(item => item.FechaCierre ?? item.FechaActualizacion).Take(take)
            .Select(item => new CancellationFact(
                item.FechaCierre ?? item.FechaActualizacion, "Cotización cancelada", item.CodigoCotizacion,
                item.ClienteId, item.NombreDestinatarioSnapshot, item.TotalCentavos,
                item.MotivoCancelacion ?? "Sin motivo registrado", item.UsuarioUltimaActualizacionId,
                item.UsuarioUltimaActualizacion.NombreUsuario, null))
            .ToListAsync(cancellationToken);
        var orderRows = await orders.OrderByDescending(item => item.FechaCancelacion).Take(take)
            .Select(item => new CancellationFact(
                item.FechaCancelacion!.Value, "Pedido cancelado", item.CodigoPedido, item.ClienteId,
                item.NombreDestinatarioSnapshot, item.TotalEstimadoCentavos,
                item.MotivoCancelacion ?? "Sin motivo registrado", item.UsuarioUltimaActualizacionId,
                item.UsuarioUltimaActualizacion.NombreUsuario, null))
            .ToListAsync(cancellationToken);
        var page = saleRows.Concat(quoteRows).Concat(orderRows).OrderByDescending(item => item.DateUtc)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        var columns = Columns(
                ("date", "Fecha registrada", AdvancedReportValueKind.DateTime, false),
                ("type", "Tipo", AdvancedReportValueKind.Text, false),
                ("document", "Documento", AdvancedReportValueKind.Text, false),
                ("customer", "Cliente", AdvancedReportValueKind.Text, false),
                ("amount", "Importe", AdvancedReportValueKind.Money, true),
                ("reason", "Motivo", AdvancedReportValueKind.Text, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false),
                ("limitation", "Limitación", AdvancedReportValueKind.Text, false));
        var rows = page.Select(item => Row(("date", item.DateUtc), ("type", item.Type), ("document", item.Document),
                ("customer", item.Customer), ("amount", Money(item.AmountCentavos)), ("reason", item.Reason),
                ("user", item.User), ("limitation", item.Limitation))).ToList();
        return new ReportData(columns, rows, total, [],
            "Las ventas no tienen fecha de anulación independiente; esa limitación se conserva por fila.",
            "No existen anulaciones o cancelaciones para los filtros seleccionados.");
    }

    private static async Task<ReportData> QuotationsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.Cotizaciones.AsNoTracking()
            .Where(item => item.FechaCreacion >= period.UtcStart && item.FechaCreacion < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoCotizacion, pattern, "\\") ||
                EF.Functions.Like(item.NombreDestinatarioSnapshot, pattern, "\\"));
        }
        if (request.CustomerId.HasValue) query = query.Where(item => item.ClienteId == request.CustomerId.Value);
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioCreadorId == request.UserId.Value);
        if (TryEnum<EstadoCotizacion>(request.State, out var state)) query = query.Where(item => item.Estado == state);
        var projected = query.OrderByDescending(item => item.FechaCreacion).Select(item => new QuotationReportFact(
            item.FechaCreacion, item.CodigoCotizacion, item.Cliente != null ? item.Cliente.CodigoCliente : "-",
            item.NombreDestinatarioSnapshot, item.FechaVencimiento, item.Estado.ToString(), item.TotalCentavos,
            item.PedidoPersonalizado != null ? item.PedidoPersonalizado.CodigoPedido : null,
            item.HistorialEstados.Where(history => history.EstadoNuevo == EstadoCotizacion.ACEPTADA)
                .Select(history => (DateTime?)history.Fecha).Min(),
            item.HistorialEstados.Where(history => history.EstadoNuevo == EstadoCotizacion.CONVERTIDA)
                .Select(history => (DateTime?)history.Fecha).Min(),
            item.UsuarioCreador.NombreUsuario));
        return await PageAsync(
            projected, request,
            Columns(
                ("created", "Creada", AdvancedReportValueKind.DateTime, false),
                ("quotation", "Cotización", AdvancedReportValueKind.Text, false),
                ("customerCode", "Código cliente", AdvancedReportValueKind.Text, false),
                ("customer", "Cliente", AdvancedReportValueKind.Text, false),
                ("expires", "Vence", AdvancedReportValueKind.DateTime, false),
                ("state", "Estado", AdvancedReportValueKind.Text, false),
                ("amount", "Importe", AdvancedReportValueKind.Money, true),
                ("order", "Pedido convertido", AdvancedReportValueKind.Text, false),
                ("accepted", "Aceptada", AdvancedReportValueKind.DateTime, false),
                ("converted", "Convertida", AdvancedReportValueKind.DateTime, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false)),
            item => Row(("created", item.CreatedUtc), ("quotation", item.QuotationCode),
                ("customerCode", item.CustomerCode), ("customer", item.Customer), ("expires", item.ExpiresUtc),
                ("state", item.State), ("amount", Money(item.AmountCentavos)), ("order", item.OrderCode),
                ("accepted", item.AcceptedUtc), ("converted", item.ConvertedUtc), ("user", item.User)),
            [], "No existen cotizaciones en este período.", cancellationToken,
            coverageNote: "Aceptación y conversión usan exclusivamente el historial de estados registrado.");
    }

    private static async Task<ReportData> CustomOrdersAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.PedidosPersonalizados.AsNoTracking()
            .Where(item => item.FechaCreacion >= period.UtcStart && item.FechaCreacion < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.CodigoPedido, pattern, "\\") ||
                EF.Functions.Like(item.NombreDestinatarioSnapshot, pattern, "\\"));
        }
        if (request.CustomerId.HasValue) query = query.Where(item => item.ClienteId == request.CustomerId.Value);
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioCreadorId == request.UserId.Value);
        if (TryEnum<EstadoPedidoPersonalizado>(request.State, out var state)) query = query.Where(item => item.Estado == state);
        var projected = query.OrderByDescending(item => item.FechaCreacion).Select(item => new CustomOrderReportFact(
            item.FechaCreacion, item.CodigoPedido, item.Cotizacion != null ? item.Cotizacion.CodigoCotizacion : null,
            item.Cliente != null ? item.Cliente.CodigoCliente : "-", item.NombreDestinatarioSnapshot,
            item.FechaPrometida, item.FechaEntregaReal, item.Estado.ToString(), item.Estado,
            item.TotalEstimadoCentavos, item.TotalPagadoCentavos, item.TotalReembolsadoCentavos,
            item.SaldoPendienteCentavos,
            item.Pagos.Where(payment => payment.TipoPago == TipoPagoPedido.ANTICIPO)
                .Sum(payment => (long?)payment.MontoCentavos) ?? 0,
            item.Venta != null ? item.Venta.NumeroVenta : null, item.UsuarioCreador.NombreUsuario));
        var asOfDate = period.LocalEndExclusive.Date.AddDays(-1);
        return await PageAsync(
            projected, request,
            Columns(
                ("created", "Creado", AdvancedReportValueKind.DateTime, false),
                ("order", "Pedido", AdvancedReportValueKind.Text, false),
                ("quotation", "Cotización", AdvancedReportValueKind.Text, false),
                ("customerCode", "Código cliente", AdvancedReportValueKind.Text, false),
                ("customer", "Cliente", AdvancedReportValueKind.Text, false),
                ("promised", "Fecha prometida", AdvancedReportValueKind.DateTime, false),
                ("delivered", "Entrega real", AdvancedReportValueKind.DateTime, false),
                ("state", "Estado", AdvancedReportValueKind.Text, false),
                ("delayed", "Retrasado", AdvancedReportValueKind.Boolean, false),
                ("total", "Importe", AdvancedReportValueKind.Money, true),
                ("advance", "Anticipos", AdvancedReportValueKind.Money, true),
                ("paid", "Pagado", AdvancedReportValueKind.Money, true),
                ("refund", "Reembolsado", AdvancedReportValueKind.Money, true),
                ("balance", "Saldo", AdvancedReportValueKind.Money, true),
                ("sale", "Venta convertida", AdvancedReportValueKind.Text, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false)),
            item => Row(("created", item.CreatedUtc), ("order", item.OrderCode), ("quotation", item.QuotationCode),
                ("customerCode", item.CustomerCode), ("customer", item.Customer), ("promised", item.PromisedUtc),
                ("delivered", item.DeliveredUtc), ("state", item.StateText),
                ("delayed", IsDelayed(item, asOfDate)), ("total", Money(item.TotalCentavos)),
                ("advance", Money(item.AdvancesCentavos)), ("paid", Money(item.PaidCentavos)),
                ("refund", Money(item.RefundCentavos)), ("balance", Money(item.BalanceCentavos)),
                ("sale", item.SaleNumber), ("user", item.User)),
            [], "No hubo pedidos personalizados para los filtros seleccionados.", cancellationToken,
            coverageNote: "Retraso exige fecha prometida válida y excluye pedidos entregados o cancelados.");
    }

    private async Task<ReportData> InventoryValueAsync(
        AdvancedReportRequest request,
        CancellationToken cancellationToken)
    {
        var valuation = await _inventoryValuation.QueryAsync(
            new InventoryValuationRequest(
                request.Search,
                request.ProductId,
                request.CategoryId,
                request.SubcategoryId,
                request.SubcategoryFilter,
                request.State,
                request.Page,
                request.PageSize),
            cancellationToken);

        var totals = valuation.Totals;
        return new ReportData(
            Columns(
                ("code", "Código producto", AdvancedReportValueKind.Text, false),
                ("sku", "SKU", AdvancedReportValueKind.Text, false),
                ("product", "Producto", AdvancedReportValueKind.Text, false),
                ("variant", "Variante", AdvancedReportValueKind.Text, false),
                ("category", "Categoría", AdvancedReportValueKind.Text, false),
                ("subcategory", "Subcategoría", AdvancedReportValueKind.Text, false),
                ("state", "Estado producto/SKU", AdvancedReportValueKind.Text, false),
                ("stock", "Stock físico", AdvancedReportValueKind.Integer, true),
                ("reserved", "Reservado", AdvancedReportValueKind.Integer, true),
                ("available", "Disponible", AdvancedReportValueKind.Integer, true),
                ("exhibition", "En exhibición", AdvancedReportValueKind.Integer, true),
                ("warehouse", "En bodega", AdvancedReportValueKind.Integer, true),
                ("unassigned", "Sin asignar", AdvancedReportValueKind.Integer, true),
                ("cost", "Costo efectivo", AdvancedReportValueKind.Money, false),
                ("price", "Precio venta efectivo", AdvancedReportValueKind.Money, false),
                // "value" is retained as a stable key for existing consumers;
                // it is the same canonical investment amount.
                ("value", "Valor actual", AdvancedReportValueKind.Money, true),
                ("investment", "Inversión actual", AdvancedReportValueKind.Money, true),
                ("potential", "Venta potencial", AdvancedReportValueKind.Money, true),
                ("availableValue", "Venta disponible", AdvancedReportValueKind.Money, true),
                ("gain", "Ganancia bruta potencial", AdvancedReportValueKind.Money, true),
                ("margin", "Margen bruto potencial", AdvancedReportValueKind.Percentage, false),
                ("locationQuality", "Ubicación", AdvancedReportValueKind.Text, false),
                ("quality", "Calidad/cobertura", AdvancedReportValueKind.Text, false)),
            valuation.Rows.Select(row => Row(
                ("productId", row.ProductId),
                ("code", row.ProductCode),
                ("skuId", row.SkuId),
                ("sku", row.SkuCode ?? (row.IsLegacy ? "Sin SKU (histórico)" : "Sin SKU")),
                ("product", row.ProductName),
                ("variant", row.Variant ?? "—"),
                ("category", row.Category),
                ("subcategory", row.Subcategory),
                ("state", row.StateText),
                ("stock", row.PhysicalStock),
                ("reserved", row.ReservedStock),
                ("available", row.AvailableStock),
                ("exhibition", row.IsLegacy ? null : row.ExhibitionStock),
                ("warehouse", row.IsLegacy ? null : row.WarehouseStock),
                ("unassigned", row.IsLegacy ? null : row.UnassignedStock),
                ("cost", Money(row.CostEffectiveCentavos)),
                ("price", Money(row.SalePriceEffectiveCentavos)),
                ("value", Money(row.InvestmentCurrentCentavos)),
                ("investment", Money(row.InvestmentCurrentCentavos)),
                ("potential", Money(row.PotentialSalesValueCentavos)),
                ("availableValue", Money(row.AvailableSalesValueCentavos)),
                ("gain", Money(row.PotentialGrossProfitCentavos)),
                ("margin", row.PotentialGrossMarginPercentage),
                ("locationQuality", row.LocationQuality),
                ("quality", row.Quality))).ToList(),
            valuation.TotalRows,
            [
                S("Inversión actual", Money(totals.InvestmentCurrentCentavos), AdvancedReportValueKind.Money),
                S("Venta potencial física", Money(totals.PotentialSalesValueCentavos), AdvancedReportValueKind.Money),
                S("Venta disponible", Money(totals.AvailableSalesValueCentavos), AdvancedReportValueKind.Money),
                S("Ganancia bruta potencial", Money(totals.PotentialGrossProfitCentavos), AdvancedReportValueKind.Money),
                S("Margen bruto potencial", totals.PotentialGrossMarginPercentage, AdvancedReportValueKind.Percentage),
                S("Stock físico total", totals.PhysicalStock, AdvancedReportValueKind.Integer),
                S("Reservado total", totals.ReservedStock, AdvancedReportValueKind.Integer),
                S("Disponible total", totals.AvailableStock, AdvancedReportValueKind.Integer),
                S("Filas inactivas", totals.InactiveRows, AdvancedReportValueKind.Integer),
                S("Pendientes de ubicación", totals.PendingLocationRows, AdvancedReportValueKind.Integer),
                S("Filas legacy", totals.LegacyRows, AdvancedReportValueKind.Integer)
            ],
            valuation.CoverageNote,
            valuation.EmptyMessage);
    }

    private static async Task<ReportData> EmployeeOperationsAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.Usuarios.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.NombreUsuario, pattern, "\\") ||
                EF.Functions.Like(item.NombreCompleto, pattern, "\\"));
        }
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioId == request.UserId.Value);
        var projected = query.OrderBy(item => item.NombreCompleto).Select(item => new EmployeeOperationsFact(
            item.NombreUsuario,
            item.NombreCompleto,
            item.Rol.ToString(),
            item.Activo,
            item.Ventas.Count(sale => sale.FechaVenta >= period.UtcStart && sale.FechaVenta < period.UtcEndExclusive),
            item.Ventas.Where(sale => sale.FechaVenta >= period.UtcStart && sale.FechaVenta < period.UtcEndExclusive)
                .Sum(sale => (long?)sale.TotalCentavos) ?? 0,
            context.Devoluciones.Count(returned => returned.UsuarioId == item.UsuarioId &&
                returned.FechaDevolucion >= period.UtcStart && returned.FechaDevolucion < period.UtcEndExclusive),
            item.CajasAbiertas.Count(cash => cash.FechaApertura >= period.UtcStart && cash.FechaApertura < period.UtcEndExclusive),
            item.CajasCerradas.Count(cash => cash.FechaCierre.HasValue && cash.FechaCierre.Value >= period.UtcStart &&
                cash.FechaCierre.Value < period.UtcEndExclusive),
            item.Gastos.Count(expense => expense.FechaGasto >= period.UtcStart && expense.FechaGasto < period.UtcEndExclusive),
            item.RetirosCaja.Count(withdrawal => withdrawal.FechaRetiro >= period.UtcStart && withdrawal.FechaRetiro < period.UtcEndExclusive),
            item.Bitacoras.Count(log => log.FechaAccion >= period.UtcStart && log.FechaAccion < period.UtcEndExclusive)));
        return await PageAsync(
            projected, request,
            Columns(
                ("user", "Usuario", AdvancedReportValueKind.Text, false),
                ("name", "Nombre", AdvancedReportValueKind.Text, false),
                ("role", "Rol", AdvancedReportValueKind.Text, false),
                ("active", "Activo", AdvancedReportValueKind.Boolean, false),
                ("sales", "Ventas registradas", AdvancedReportValueKind.Integer, true),
                ("amount", "Importe vendido", AdvancedReportValueKind.Money, true),
                ("returns", "Devoluciones gestionadas", AdvancedReportValueKind.Integer, true),
                ("openCash", "Aperturas de caja", AdvancedReportValueKind.Integer, true),
                ("closeCash", "Cierres de caja", AdvancedReportValueKind.Integer, true),
                ("expenses", "Gastos registrados", AdvancedReportValueKind.Integer, true),
                ("withdrawals", "Retiros registrados", AdvancedReportValueKind.Integer, true),
                ("activity", "Eventos administrativos", AdvancedReportValueKind.Integer, true)),
            item => Row(("user", item.User), ("name", item.Name), ("role", item.Role), ("active", item.Active),
                ("sales", item.Sales), ("amount", Money(item.SalesAmountCentavos)), ("returns", item.Returns),
                ("openCash", item.CashOpenings), ("closeCash", item.CashClosings), ("expenses", item.Expenses),
                ("withdrawals", item.Withdrawals), ("activity", item.AdministrativeEvents)),
            [S("Contexto", "Indicadores operativos separados; no constituyen una clasificación de productividad.")],
            "No existen operaciones de empleados para los filtros seleccionados.", cancellationToken,
            coverageNote: "ADMIN únicamente. El importe vendido se presenta junto con transacciones y demás contexto operativo.");
    }

    private static async Task<ReportData> AdministrativeActivityAsync(
        AppDbContext context,
        AdvancedReportRequest request,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var query = context.Bitacora.AsNoTracking()
            .Where(item => item.FechaAccion >= period.UtcStart && item.FechaAccion < period.UtcEndExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePattern(request.Search)!;
            query = query.Where(item => EF.Functions.Like(item.Entidad, pattern, "\\") ||
                EF.Functions.Like(item.Descripcion, pattern, "\\") ||
                (item.Usuario != null && (EF.Functions.Like(item.Usuario.NombreUsuario, pattern, "\\") ||
                    EF.Functions.Like(item.Usuario.NombreCompleto, pattern, "\\"))));
        }
        if (request.UserId.HasValue) query = query.Where(item => item.UsuarioId == request.UserId.Value);
        var projected = query.OrderByDescending(item => item.FechaAccion).Select(item => new ActivityFact(
            item.FechaAccion, item.Usuario != null ? item.Usuario.NombreUsuario : "Sistema",
            item.TipoAccion.ToString(), item.Entidad, item.Descripcion));
        return await PageAsync(
            projected, request,
            Columns(
                ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
                ("user", "Usuario", AdvancedReportValueKind.Text, false),
                ("action", "Acción", AdvancedReportValueKind.Text, false),
                ("entity", "Entidad", AdvancedReportValueKind.Text, false),
                ("description", "Descripción", AdvancedReportValueKind.Text, false)),
            item => Row(("date", item.DateUtc), ("user", item.User), ("action", item.Action),
                ("entity", item.Entity), ("description", item.Description)),
            [S("Seguridad", "Se excluyen DatosAntes, DatosDespues y cualquier JSON interno.")],
            "No existe actividad administrativa para los filtros seleccionados.", cancellationToken);
    }

    private static AdvancedReportResult Result(
        AdvancedReportDefinition definition,
        AnalyticsPeriodDefinition period,
        AdvancedReportRequest request,
        IReadOnlyList<string> filters,
        ReportData data) => new(
            definition,
            period,
            filters,
            data.Columns,
            data.Rows,
            data.TotalRows,
            data.Summary,
            data.CoverageNote,
            EmptyMessage(request, data.EmptyMessage));

    private static async Task<ReportData> PageAsync<T>(
        IQueryable<T> query,
        AdvancedReportRequest request,
        IReadOnlyList<AdvancedReportColumn> columns,
        Func<T, AdvancedReportRow> map,
        IReadOnlyList<AdvancedReportSummaryItem> summary,
        string emptyMessage,
        CancellationToken cancellationToken,
        IReadOnlyList<AdvancedReportColumn>? extraColumns = null,
        string? coverageNote = null)
    {
        var total = await query.CountAsync(cancellationToken);
        var page = await query.Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var effectiveColumns = extraColumns is { Count: > 0 } ? columns.Concat(extraColumns).ToList() : columns;
        return new ReportData(effectiveColumns, page.Select(map).ToList(), total, summary, coverageNote, emptyMessage);
    }

    private static IReadOnlyList<AdvancedReportColumn> ObligationColumns(string counterparty) =>
        Columns(
            ("date", "Creada", AdvancedReportValueKind.DateTime, false),
            ("account", "Cuenta", AdvancedReportValueKind.Text, false),
            ("source", "Documento origen", AdvancedReportValueKind.Text, false),
            ("counterpartyCode", $"Código {counterparty.ToLowerInvariant()}", AdvancedReportValueKind.Text, false),
            ("counterparty", counterparty, AdvancedReportValueKind.Text, false),
            ("due", "Vencimiento", AdvancedReportValueKind.DateTime, false),
            ("state", "Estado", AdvancedReportValueKind.Text, false),
            ("original", "Total original", AdvancedReportValueKind.Money, true),
            ("paid", "Pagado", AdvancedReportValueKind.Money, true),
            ("balance", "Saldo", AdvancedReportValueKind.Money, true),
            ("interest", "Interés", AdvancedReportValueKind.Percentage, false));

    private static IReadOnlyList<AdvancedReportColumn> PaymentColumns(string counterparty) =>
        Columns(
            ("date", "Fecha", AdvancedReportValueKind.DateTime, false),
            ("payment", "Pago", AdvancedReportValueKind.Text, false),
            ("account", "Cuenta", AdvancedReportValueKind.Text, false),
            ("counterpartyCode", $"Código {counterparty.ToLowerInvariant()}", AdvancedReportValueKind.Text, false),
            ("counterparty", counterparty, AdvancedReportValueKind.Text, false),
            ("method", "Método", AdvancedReportValueKind.Text, false),
            ("amount", "Monto", AdvancedReportValueKind.Money, true),
            ("capital", "Capital", AdvancedReportValueKind.Money, true),
            ("interest", "Interés", AdvancedReportValueKind.Money, true),
            ("balance", "Saldo nuevo", AdvancedReportValueKind.Money, false),
            ("cash", "Caja", AdvancedReportValueKind.Integer, false),
            ("user", "Usuario", AdvancedReportValueKind.Text, false),
            ("reference", "Referencia", AdvancedReportValueKind.Text, false));

    private static IReadOnlyList<AdvancedReportColumn> Columns(
        params (string Key, string Title, AdvancedReportValueKind Kind, bool Total)[] columns) =>
        columns.Select(column => new AdvancedReportColumn(column.Key, column.Title, column.Kind, column.Total)).ToList();

    private static AdvancedReportRow Row(params (string Key, object? Value)[] values) =>
        new(values.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));

    private static AdvancedReportSummaryItem S(
        string label,
        object? value,
        AdvancedReportValueKind kind = AdvancedReportValueKind.Text) => new(label, value, kind);

    private static decimal Money(long centavos) => centavos / 100m;

    private static decimal? Money(long? centavos) => centavos.HasValue ? centavos.Value / 100m : null;

    private static decimal Money(decimal centavos) => centavos / 100m;

    private static decimal Percentage(long value, long total) => total <= 0
        ? 0
        : decimal.Round(value * 100m / total, 2, MidpointRounding.AwayFromZero);

    private static InventoryHealthFact ToInventoryHealth(
        InventoryFact item,
        int canonicalWasteUnits,
        long canonicalWasteValueCentavos,
        int returnWasteUnits,
        long returnWasteValueCentavos,
        DateTime nowUtc)
    {
        var exhibition = item.ExhibitionPhysical;
        var warehouse = item.WarehousePhysical;
        var unassigned = item.UnassignedPhysical;
        var physical = exhibition + warehouse + unassigned;
        var reserved = item.ExhibitionReserved + item.WarehouseReserved + item.UnassignedReserved;
        var available = physical - reserved;
        var exhibitionAvailable = item.ExhibitionPhysical - item.ExhibitionReserved;
        var warehouseAvailable = item.WarehousePhysical - item.WarehouseReserved;
        var situation = physical == 0
            ? "Sin existencias"
            : available == 0
                ? "Sin disponibilidad"
                : exhibitionAvailable <= item.Minimum && warehouseAvailable > 0
                    ? "Requiere reposición"
                    : "Disponible";
        var lastActivity = new DateTime?[]
            {
                item.LastExactSkuSaleUtc,
                item.LastLegacyProductSaleUtc,
                item.LastMovementUtc,
                item.ProductCreatedUtc
            }
            .Where(value => value.HasValue)
            .Max() ?? item.ProductCreatedUtc;
        var inactiveDays = Math.Max(0, (int)(nowUtc - lastActivity).TotalDays);
        var salesCoverage = item.LastExactSkuSaleUtc.HasValue
            ? "SKU exacto"
            : item.LastLegacyProductSaleUtc.HasValue
                ? "Histórico solo producto"
                : "Sin ventas identificables";

        return new InventoryHealthFact(
            item.ProductCode,
            item.SkuCode,
            string.IsNullOrWhiteSpace(item.Variant) ? item.Product : $"{item.Product} · {item.Variant}",
            item.Category,
            item.Subcategory,
            $"{item.ProductState} / SKU {(item.SkuActive ? "activo" : "inactivo")}",
            physical,
            exhibition,
            warehouse,
            unassigned,
            reserved,
            available,
            item.Minimum,
            situation,
            unassigned > 0,
            available <= item.Minimum,
            canonicalWasteUnits + returnWasteUnits,
            canonicalWasteValueCentavos + returnWasteValueCentavos,
            physical > 0 && inactiveDays >= 90,
            item.LastExactSkuSaleUtc is null && item.LastLegacyProductSaleUtc is null,
            lastActivity,
            salesCoverage,
            item.CostCentavos,
            InventoryValuationCalculator.Multiply(physical, item.CostCentavos) ?? 0);
    }

    private static bool Contains(string? value, string? term) =>
        !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(term) &&
        value.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string EmptyMessage(AdvancedReportRequest request, string fallback) =>
        !string.IsNullOrWhiteSpace(request.Search)
            ? $"No se encontraron resultados para '{request.Search.Trim()}'."
            : fallback;

    private static string? LikePattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static bool MatchesSpecialOption(string label, string? pattern)
    {
        if (pattern is null)
        {
            return true;
        }

        var search = pattern.Trim('%')
            .Replace("\\%", "%", StringComparison.Ordinal)
            .Replace("\\_", "_", StringComparison.Ordinal);
        return search.Length == 0 || label.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEntityFilter(AdvancedReportFilterKind filterKind) => filterKind is
            AdvancedReportFilterKind.Customer or
            AdvancedReportFilterKind.Product or
            AdvancedReportFilterKind.Category or
            AdvancedReportFilterKind.Subcategory or
            AdvancedReportFilterKind.Supplier or
        AdvancedReportFilterKind.User or
        AdvancedReportFilterKind.CashRegister;

    private static bool TryEnum<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value?.Trim(), true, out result);

    private static string Quality(HistoricalDataQuality quality) => quality switch
    {
        HistoricalDataQuality.Exact => "Exacta",
        HistoricalDataQuality.Partial => "Parcial",
        _ => "No disponible"
    };

    private static string Coverage(HistoricalCoverageSummary coverage) =>
        $"Cobertura histórica: producto {coverage.ProductSnapshots.Percentage:N2}%, " +
        $"categoría {coverage.CategorySnapshots.Percentage:N2}%, costo {coverage.CostSnapshots.Percentage:N2}%, " +
        $"descuento distribuido {coverage.GeneralDiscountSnapshots.Percentage:N2}%.";

    private static bool IsDelayed(CustomOrderReportFact item, DateTime asOfDate) =>
        item.PromisedUtc != default &&
        item.PromisedUtc.Date >= item.CreatedUtc.Date &&
        item.PromisedUtc.Date < asOfDate.Date &&
        item.State is not EstadoPedidoPersonalizado.ENTREGADO and not EstadoPedidoPersonalizado.CANCELADO;

    private static IReadOnlyList<string> AppliedFilters(AdvancedReportRequest request)
    {
        var filters = new List<string>();
        Add(filters, "Búsqueda", request.Search);
        AddEntity(filters, "Cliente", request.CustomerId, request.SelectedEntityDisplay);
        AddEntity(filters, "Producto", request.ProductId, request.SelectedEntityDisplay);
        AddEntity(filters, "Categoría", request.CategoryId, request.SelectedEntityDisplay);
        if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.ExplicitlyUnassigned)
        {
            Add(filters, "Subcategoría", AnalyticsSubcategoryFilterOptions.ExplicitlyUnassignedLabel);
        }
        else if (request.SubcategoryFilter == AnalyticsSubcategoryFilter.HistoricalUnavailable)
        {
            Add(filters, "Subcategoría", AnalyticsSubcategoryFilterOptions.HistoricalUnavailableLabel);
        }
        else
        {
            AddEntity(filters, "Subcategoría", request.SubcategoryId, request.SelectedEntityDisplay);
        }
        AddEntity(filters, "Proveedor", request.SupplierId, request.SelectedEntityDisplay);
        AddEntity(filters, "Usuario", request.UserId, request.SelectedEntityDisplay);
        Add(filters, "Estado", request.State);
        Add(filters, "Método de pago", request.PaymentMethod);
        AddEntity(filters, "Caja", request.CashRegisterId, request.SelectedEntityDisplay);
        Add(filters, "Tipo de movimiento", request.MovementType);
        if (request.Kind == AdvancedReportKind.ProductProfitability)
        {
            Add(filters, "Orden", request.ProfitabilitySort);
            Add(filters, "Dirección", request.ProfitabilityDescending ? "Descendente" : "Ascendente");
            Add(filters, "Límite", request.ProfitabilityTop.HasValue ? $"Top {request.ProfitabilityTop.Value}" : null);
            Add(filters, "Selección", request.SelectedProductIds is { Count: > 0 }
                ? $"{request.SelectedProductIds.Count} producto(s)"
                : null);
        }
        return filters.Count == 0 ? ["Sin filtros específicos"] : filters;
    }

    private static void Add(List<string> filters, string label, object? value)
    {
        if (value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
        {
            filters.Add($"{label}: {value}");
        }
    }

    private static void AddEntity(List<string> filters, string label, int? id, string? display)
    {
        if (!id.HasValue)
        {
            return;
        }

        filters.Add($"{label}: {(string.IsNullOrWhiteSpace(display) ? $"#{id.Value}" : display.Trim())}");
    }

    private static void Validate(AdvancedReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Analytics);
        _ = AdvancedReportCatalog.Get(request.Kind);
        if (request.Page < 1) throw new ArgumentOutOfRangeException(nameof(request.Page), "La página debe ser mayor que cero.");
        if (request.PageSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(request.PageSize), "El tamaño de página debe estar entre 1 y 1000.");
        if (request.Search?.Length > 200)
            throw new ArgumentException("La búsqueda no puede superar 200 caracteres.", nameof(request));
        if (request.Kind == AdvancedReportKind.ProductProfitability)
        {
            if (!Enum.IsDefined(request.ProfitabilitySort))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.ProfitabilitySort), "La ordenación de rentabilidad no es válida.");
            }
            if (request.ProfitabilityTop is <= 0 or > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.ProfitabilityTop), "El límite superior debe estar entre 1 y 1000.");
            }
        }
    }

    private sealed record ReportData(
        IReadOnlyList<AdvancedReportColumn> Columns,
        IReadOnlyList<AdvancedReportRow> Rows,
        int TotalRows,
        IReadOnlyList<AdvancedReportSummaryItem> Summary,
        string? CoverageNote,
        string EmptyMessage);

    private sealed record InventoryFact(
        int SkuId,
        int ProductId,
        string ProductCode,
        string SkuCode,
        string Product,
        string? Variant,
        string Category,
        string Subcategory,
        EstadoProducto ProductState,
        bool SkuActive,
        int ExhibitionPhysical,
        int ExhibitionReserved,
        int WarehousePhysical,
        int WarehouseReserved,
        int UnassignedPhysical,
        int UnassignedReserved,
        int Minimum,
        long CostCentavos,
        DateTime? LastExactSkuSaleUtc,
        DateTime? LastLegacyProductSaleUtc,
        DateTime? LastMovementUtc,
        DateTime ProductCreatedUtc);
    private sealed record InventoryHealthFact(
        string ProductCode,
        string SkuCode,
        string DisplayName,
        string Category,
        string Subcategory,
        string State,
        int Physical,
        int Exhibition,
        int Warehouse,
        int Unassigned,
        int Reserved,
        int Available,
        int Minimum,
        string Situation,
        bool PendingClassification,
        bool BelowMinimum,
        int WasteUnits,
        long WasteValueCentavos,
        bool Stagnant,
        bool NoIdentifiableSales,
        DateTime LastActivityUtc,
        string SalesCoverage,
        long CostCentavos,
        long ValueCentavos);
    private sealed record ProductSearchMetadata(
        int ProductId,
        int? CategoryId,
        string? CategoryName,
        int? SubcategoryId,
        string? SubcategoryName);
    private sealed record MovementFact(
        DateTime DateUtc,
        int ProductId,
        int UserId,
        string Code,
        string SkuCode,
        string? Variant,
        string Product,
        string Type,
        string? Origin,
        string? Destination,
        int Quantity,
        int? Before,
        int? After,
        string Reason,
        string User,
        bool Canonical);
    private sealed record PaymentMethodFact(string Method, int Transactions, long AmountCentavos);
    private sealed record ExpenseFact(DateTime DateUtc, string Category, string Concept, long AmountCentavos,
        string Method, int? CashId, string User, string? Reference, string FundingOrigin,
        string? Responsible, bool AffectsCash);
    private sealed record WithdrawalFact(DateTime DateUtc, int CashId, long AmountCentavos, string Reason, string User);
    private sealed record PurchaseFact(DateTime DateUtc, string Code, string SupplierCode, string Supplier,
        string ReceptionState, string FinancialState, long TotalCentavos, long PaidCentavos, long BalanceCentavos, string User);
    private sealed record ReceptionFact(DateTime DateUtc, string ReceptionCode, string PurchaseCode, string Supplier,
        string ProductCode, string Product, string Category, string Subcategory,
        int Quantity, long UnitCostCentavos, long ValueCentavos, string User, bool ExactNetCost);
    private sealed record SupplierFact(string Code, string Supplier, string Type, string State, string? Contact,
        string? Phone, string? Email, int Products, int Purchases, DateTime CreatedUtc);
    private sealed record CustomerFact(string Code, string Customer, string State, string? Phone, string? Email,
        bool CreditEnabled, long? CreditLimitCentavos, int Sales, int OpenAccounts, long BalanceCentavos, DateTime CreatedUtc);
    private sealed record ReceivableFact(DateTime CreatedUtc, string AccountCode, string SaleNumber, string CustomerCode,
        string Customer, DateTime DueUtc, string State, long OriginalCentavos, long PaidCentavos, long BalanceCentavos,
        decimal InterestPercentage);
    private sealed record PayableFact(DateTime CreatedUtc, string AccountCode, string PurchaseCode, string SupplierCode,
        string Supplier, DateTime DueUtc, string State, long OriginalCentavos, long PaidCentavos, long BalanceCentavos);
    private sealed record CustomerPaymentFact(DateTime DateUtc, string PaymentCode, string AccountCode, string CustomerCode,
        string Customer, string Method, long AmountCentavos, long CapitalCentavos, long InterestCentavos,
        long BalanceCentavos, int? CashId, string User, string? Reference);
    private sealed record SupplierPaymentFact(DateTime DateUtc, string PaymentCode, string AccountCode, int SupplierId,
        string SupplierCode, string Supplier, MetodoPago MethodValue, long AmountCentavos, long CapitalCentavos,
        long InterestCentavos, long BalanceCentavos, int? CashId, int UserId, string User, string? Reference, string Type);
    private sealed record ReturnFact(DateTime DateUtc, string ReturnNumber, string SaleNumber, string Customer,
        string Type, long AmountCentavos, string Reason, string User, int Units);
    private sealed record RefundFact(DateTime DateUtc, string SourceType, string SourceCode, int? CustomerId,
        string Customer, string Method, long AmountCentavos, string Reason, string User, int UserId, int? CashId, string? Reference);
    private sealed record CancellationFact(DateTime DateUtc, string Type, string Document, int? CustomerId,
        string Customer, long AmountCentavos, string Reason, int UserId, string User, string? Limitation);
    private sealed record QuotationReportFact(DateTime CreatedUtc, string QuotationCode, string CustomerCode,
        string Customer, DateTime ExpiresUtc, string State, long AmountCentavos, string? OrderCode,
        DateTime? AcceptedUtc, DateTime? ConvertedUtc, string User);
    private sealed record CustomOrderReportFact(DateTime CreatedUtc, string OrderCode, string? QuotationCode,
        string CustomerCode, string Customer, DateTime PromisedUtc, DateTime? DeliveredUtc, string StateText,
        EstadoPedidoPersonalizado State, long TotalCentavos, long PaidCentavos, long RefundCentavos,
        long BalanceCentavos, long AdvancesCentavos, string? SaleNumber, string User);
    private sealed record EmployeeOperationsFact(string User, string Name, string Role, bool Active, int Sales,
        long SalesAmountCentavos, int Returns, int CashOpenings, int CashClosings, int Expenses,
        int Withdrawals, int AdministrativeEvents);
    private sealed record ActivityFact(DateTime DateUtc, string User, string Action, string Entity, string Description);
}
