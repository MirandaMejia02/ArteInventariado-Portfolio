using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class FinancialAnalyticsQueryService : IFinancialAnalyticsQueryService
{
    internal const string ReceptionSnapshotStartKey = "analytics.snapshots_recepciones_desde_utc";
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ISalesAnalyticsQueryService _salesAnalytics;
    private readonly ICurrentSession _currentSession;
    private readonly IInventoryValuationQueryService _inventoryValuation;

    public FinancialAnalyticsQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ISalesAnalyticsQueryService salesAnalytics,
        ICurrentSession currentSession)
        : this(
            contextFactory,
            salesAnalytics,
            currentSession,
            new InventoryValuationQueryService(contextFactory, currentSession))
    {
    }

    [ActivatorUtilitiesConstructor]
    public FinancialAnalyticsQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ISalesAnalyticsQueryService salesAnalytics,
        ICurrentSession currentSession,
        IInventoryValuationQueryService inventoryValuation)
    {
        _contextFactory = contextFactory;
        _salesAnalytics = salesAnalytics;
        _currentSession = currentSession;
        _inventoryValuation = inventoryValuation;
    }

    public async Task<FinancialAnalyticsReport> GetAsync(
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // SalesAnalyticsQueryService remains the only canonical source for net sales.
        var sales = await _salesAnalytics.GetSalesAsync(request, cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureActiveAdminAsync(context, cancellationToken);
        var timeZone = await ResolveBusinessTimeZoneAsync(context, cancellationToken);

        var current = await LoadPeriodFactsAsync(context, sales.Period, timeZone, cancellationToken);
        var compared = await LoadPeriodFactsAsync(context, sales.ComparedPeriod, timeZone, cancellationToken);
        var inventoryValuation = await _inventoryValuation.QueryAsync(
            new InventoryValuationRequest(
                CategoryId: request.CategoryId,
                SubcategoryId: request.SubcategoryId,
                SubcategoryFilter: request.SubcategoryFilter),
            cancellationToken);
        var currentInventoryValue = inventoryValuation.Totals.InvestmentCurrentCentavos ?? 0;
        var receptionSnapshotStart = await LoadReceptionSnapshotStartAsync(context, cancellationToken);

        var finance = BuildFinance(sales, current, compared);
        var purchases = new PurchaseInvestmentAnalytics(
            MoneyMetric(current.CommittedPurchasesCentavos, compared.CommittedPurchasesCentavos),
            MoneyMetric(current.ReceivedInventoryCentavos, compared.ReceivedInventoryCentavos),
            MoneyMetric(current.SupplierPaymentsCentavos, compared.SupplierPaymentsCentavos),
            currentInventoryValue,
            Coverage(
                "Snapshot neto de recepcion",
                current.CoveredReceptionRecords,
                current.TotalReceptionRecords,
                receptionSnapshotStart),
            inventoryValuation.Totals);

        return new FinancialAnalyticsReport(
            sales.Period,
            sales.ComparedPeriod,
            finance,
            current.Cash,
            purchases,
            current.Receivables,
            current.Payables);
    }

    private static FinancialOverviewAnalytics BuildFinance(
        SalesAnalyticsReport sales,
        PeriodFacts current,
        PeriodFacts compared)
    {
        var coverage = Coverage(
            "Costo historico",
            current.CoveredCostRecords,
            current.TotalCostRecords,
            sales.Coverage.ReliableSnapshotsFromUtc);
        return new FinancialOverviewAnalytics(
            sales.Summary.NetSales,
            MoneyMetric(current.CoveredNetSalesCentavos, compared.CoveredNetSalesCentavos),
            MoneyMetric(current.CostOfGoodsSoldCentavos, compared.CostOfGoodsSoldCentavos),
            MoneyMetric(current.GrossProfitCentavos, compared.GrossProfitCentavos),
            DecimalMetric(current.GrossMarginPercentage, compared.GrossMarginPercentage),
            MoneyMetric(current.OperatingExpensesCentavos, compared.OperatingExpensesCentavos),
            MoneyMetric(current.ReceivedInventoryCentavos, compared.ReceivedInventoryCentavos),
            coverage,
            current.Trend,
            BuildExpenseCategories(current.Expenses),
            current.Expenses);
    }

    private static IReadOnlyList<ExpenseCategoryAnalyticsRow> BuildExpenseCategories(
        IReadOnlyList<ExpenseAnalyticsRow> expenses)
    {
        var total = expenses.Sum(expense => expense.AmountCentavos);
        return expenses.GroupBy(expense => expense.Category)
            .Select(group => new ExpenseCategoryAnalyticsRow(
                group.Key,
                group.Sum(expense => expense.AmountCentavos),
                total == 0
                    ? 0
                    : decimal.Round(group.Sum(expense => expense.AmountCentavos) * 100m / total, 2)))
            .OrderByDescending(row => row.AmountCentavos)
            .ThenBy(row => row.Category)
            .ToList();
    }

    private static async Task<PeriodFacts> LoadPeriodFactsAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profitFacts = await LoadProfitFactsAsync(context, period, cancellationToken);
        var expenses = await context.Gastos.AsNoTracking()
            .Where(expense => expense.FechaGasto >= period.UtcStart && expense.FechaGasto < period.UtcEndExclusive)
            .OrderByDescending(expense => expense.FechaGasto)
            .Select(expense => new ExpenseAnalyticsRow(
                expense.GastoId,
                expense.FechaGasto,
                expense.CategoriaGasto != null ? expense.CategoriaGasto.Nombre : "Sin categoría",
                expense.Usuario.NombreUsuario,
                expense.MetodoPago.HasValue ? expense.MetodoPago.Value.ToString() : "Método no especificado",
                expense.Referencia,
                expense.Concepto,
                expense.MontoCentavos,
                expense.OrigenFondos == OrigenFondos.CAJA_FISICA ? "Caja física" :
                    expense.OrigenFondos == OrigenFondos.FONDOS_EXTERNOS_DUENO ? "Fondos externos del dueño" :
                    expense.OrigenFondos == OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA ? "Fondos del negocio fuera de caja" :
                    "No especificado (histórico)",
                expense.ResponsableOrigen,
                expense.CajaId.HasValue && (!expense.MetodoPago.HasValue || expense.MetodoPago == MetodoPago.EFECTIVO)))
            .ToListAsync(cancellationToken);

        var committedPurchases = await context.Compras.AsNoTracking()
            .Where(purchase => purchase.FechaCompra >= period.UtcStart && purchase.FechaCompra < period.UtcEndExclusive)
            .SumAsync(purchase => (long?)purchase.TotalCentavos, cancellationToken) ?? 0;
        var receptionFacts = await context.RecepcionCompraDetalles.AsNoTracking()
            .Where(detail => detail.RecepcionCompra.FechaRecepcion >= period.UtcStart &&
                detail.RecepcionCompra.FechaRecepcion < period.UtcEndExclusive)
            .Select(detail => new ReceptionFact(
                detail.RecepcionCompra.FechaRecepcion,
                detail.CantidadRecibida,
                detail.CostoUnitarioCentavos,
                detail.CostoNetoUnitarioCentavos,
                detail.DescuentoAsignadoCentavos))
            .ToListAsync(cancellationToken);
        var receivedInventory = receptionFacts
            .Where(fact => fact.IsCovered)
            .Sum(fact => checked(fact.Quantity * fact.GrossUnitCostCentavos - fact.AssignedDiscountCentavos!.Value));

        var purchasePayments = await context.PagosCompra.AsNoTracking()
            .Where(payment => payment.FechaPago >= period.UtcStart && payment.FechaPago < period.UtcEndExclusive)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;
        var supplierInstallments = await context.AbonosCuentaPorPagar.AsNoTracking()
            .Where(payment => payment.FechaAbono >= period.UtcStart && payment.FechaAbono < period.UtcEndExclusive)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;

        var cash = await LoadCashAsync(context, period, cancellationToken);
        var receivables = await LoadReceivablesAsync(context, period, timeZone, cancellationToken);
        var payables = await LoadPayablesAsync(context, period, timeZone, cancellationToken);

        var coveredSales = profitFacts.Where(fact => fact.IsCovered).Sum(fact => fact.NetSalesCentavos);
        var cost = profitFacts.Where(fact => fact.IsCovered).Sum(fact => fact.CostEffectCentavos);
        var grossProfit = checked(coveredSales - cost);
        var buckets = CreateBuckets(period, timeZone);
        var trend = buckets.Select(bucket =>
        {
            var bucketProfit = profitFacts.Where(fact => IsInBucket(fact.DateUtc, bucket, period.Grouping, timeZone)).ToList();
            var bucketCoveredSales = bucketProfit.Where(fact => fact.IsCovered).Sum(fact => fact.NetSalesCentavos);
            var bucketCost = bucketProfit.Where(fact => fact.IsCovered).Sum(fact => fact.CostEffectCentavos);
            var bucketExpenses = expenses.Where(expense => IsInBucket(expense.DateUtc, bucket, period.Grouping, timeZone))
                .Sum(expense => expense.AmountCentavos);
            var bucketInvestment = receptionFacts.Where(fact => fact.IsCovered &&
                    IsInBucket(fact.DateUtc, bucket, period.Grouping, timeZone))
                .Sum(fact => checked(fact.Quantity * fact.GrossUnitCostCentavos - fact.AssignedDiscountCentavos!.Value));
            return new FinancialAnalyticsPoint(
                bucket.BucketStart,
                bucket.Label,
                bucketCoveredSales,
                bucketCost,
                checked(bucketCoveredSales - bucketCost),
                bucketExpenses,
                bucketInvestment);
        }).ToList();

        return new PeriodFacts(
            coveredSales,
            cost,
            grossProfit,
            coveredSales == 0 ? null : decimal.Round(grossProfit * 100m / coveredSales, 2),
            profitFacts.Count(fact => fact.IsCovered),
            profitFacts.Count,
            expenses.Sum(expense => expense.AmountCentavos),
            committedPurchases,
            receivedInventory,
            purchasePayments + supplierInstallments,
            receptionFacts.Count(fact => fact.IsCovered),
            receptionFacts.Count,
            trend,
            expenses,
            cash,
            receivables,
            payables);
    }

    private static async Task<List<ProfitFact>> LoadProfitFactsAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var movements = await ProductProfitabilityFactLoader.LoadAsync(
            context,
            period,
            cancellationToken);
        return movements.Select(movement => new ProfitFact(
            movement.DateUtc,
            movement.NetSalesCentavos,
            movement.CostEffectCentavos,
            movement.HasValidCost)).ToList();
    }

    private static async Task<CashAnalyticsSummary> LoadCashAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var boxes = await context.Cajas.AsNoTracking()
            .Where(box => box.FechaApertura >= period.UtcStart && box.FechaApertura < period.UtcEndExclusive)
            .Select(box => new { box.Estado, box.MontoInicialCentavos, box.MontoCierreCentavos })
            .ToListAsync(cancellationToken);
        var sales = await context.PagosVenta.ExcludeVoidSalePayments(context).AsNoTracking()
            .Where(payment => payment.FechaPago >= period.UtcStart && payment.FechaPago < period.UtcEndExclusive &&
                payment.Origen == OrigenPagoVenta.VENTA && payment.MetodoPago == MetodoPago.EFECTIVO && payment.AfectaCaja)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;
        var corrections = await context.PagosCorreccionVenta.AsNoTracking()
            .Where(payment => payment.FechaPago >= period.UtcStart && payment.FechaPago < period.UtcEndExclusive &&
                payment.MetodoPago == MetodoPago.EFECTIVO)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;
        var customerPayments = await context.AbonosCuentaPorCobrar.AsNoTracking()
            .Where(payment => payment.FechaAbono >= period.UtcStart && payment.FechaAbono < period.UtcEndExclusive &&
                payment.MetodoPago == MetodoPago.EFECTIVO)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;
        var otherInputs = await context.PagosPedidoPersonalizado.AsNoTracking()
            .Where(payment => payment.Fecha >= period.UtcStart && payment.Fecha < period.UtcEndExclusive &&
                payment.MetodoPago == MetodoPago.EFECTIVO && payment.AfectaCaja)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;
        var cashContributions = await context.AportesCaja.AsNoTracking()
            .Where(contribution => contribution.FechaAporte >= period.UtcStart &&
                contribution.FechaAporte < period.UtcEndExclusive)
            .SumAsync(contribution => (long?)contribution.MontoCentavos, cancellationToken) ?? 0;
        var saleRefunds = await context.Reembolsos.AsNoTracking()
            .Where(refund => refund.FechaReembolso >= period.UtcStart && refund.FechaReembolso < period.UtcEndExclusive &&
                refund.MetodoReembolso == MetodoReembolso.EFECTIVO)
            .SumAsync(refund => (long?)refund.MontoCentavos, cancellationToken) ?? 0;
        var orderRefunds = await context.ReembolsosPedidoPersonalizado.AsNoTracking()
            .Where(refund => refund.Fecha >= period.UtcStart && refund.Fecha < period.UtcEndExclusive &&
                refund.MetodoPago == MetodoPago.EFECTIVO && refund.AfectaCaja)
            .SumAsync(refund => (long?)refund.MontoCentavos, cancellationToken) ?? 0;
        var initialSupplierPayments = await context.PagosCompra.AsNoTracking()
            .Where(payment => payment.FechaPago >= period.UtcStart && payment.FechaPago < period.UtcEndExclusive &&
                payment.MetodoPago == MetodoPago.EFECTIVO)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;
        var supplierPayments = await context.AbonosCuentaPorPagar.AsNoTracking()
            .Where(payment => payment.FechaAbono >= period.UtcStart && payment.FechaAbono < period.UtcEndExclusive &&
                payment.MetodoPago == MetodoPago.EFECTIVO)
            .SumAsync(payment => (long?)payment.MontoCentavos, cancellationToken) ?? 0;
        var expenses = await context.Gastos.AsNoTracking()
            .Where(expense => expense.FechaGasto >= period.UtcStart && expense.FechaGasto < period.UtcEndExclusive &&
                expense.CajaId.HasValue &&
                (expense.MetodoPago == null || expense.MetodoPago == MetodoPago.EFECTIVO))
            .SumAsync(expense => (long?)expense.MontoCentavos, cancellationToken) ?? 0;
        var expensesOutsideCash = await context.Gastos.AsNoTracking()
            .Where(expense => expense.FechaGasto >= period.UtcStart && expense.FechaGasto < period.UtcEndExclusive &&
                !expense.CajaId.HasValue)
            .SumAsync(expense => (long?)expense.MontoCentavos, cancellationToken) ?? 0;
        var employeePayments = await context.EmployeePayments.AsNoTracking()
            .Where(payment => payment.CreatedAtUtc >= period.UtcStart && payment.CreatedAtUtc < period.UtcEndExclusive &&
                payment.CajaId.HasValue)
            .SumAsync(payment => (long?)payment.AmountCentavos, cancellationToken) ?? 0;
        var employeePaymentsOutsideCash = await context.EmployeePayments.AsNoTracking()
            .Where(payment => payment.CreatedAtUtc >= period.UtcStart && payment.CreatedAtUtc < period.UtcEndExclusive &&
                !payment.CajaId.HasValue && payment.OrigenFondos.HasValue)
            .SumAsync(payment => (long?)payment.AmountCentavos, cancellationToken) ?? 0;
        var withdrawals = await context.RetirosCaja.AsNoTracking()
            .Where(withdrawal => withdrawal.FechaRetiro >= period.UtcStart && withdrawal.FechaRetiro < period.UtcEndExclusive)
            .SumAsync(withdrawal => (long?)withdrawal.MontoCentavos, cancellationToken) ?? 0;

        var opening = boxes.Sum(box => box.MontoInicialCentavos);
        var refunds = saleRefunds + orderRefunds;
        var supplierOutputs = initialSupplierPayments + supplierPayments;
        var inputs = sales + corrections + customerPayments + otherInputs + cashContributions;
        var outputs = refunds + supplierOutputs + expenses + employeePayments + withdrawals;
        var expected = checked(opening + inputs - outputs);
        var allClosed = boxes.Count > 0 && boxes.All(box => box.Estado == EstadoCaja.CERRADA);
        long? counted = allClosed ? boxes.Sum(box => box.MontoCierreCentavos) : null;
        return new CashAnalyticsSummary(
            opening,
            sales + corrections,
            customerPayments,
            otherInputs,
            refunds,
            supplierOutputs,
            expenses,
            withdrawals,
            expected,
            inputs - outputs,
            counted,
            counted.HasValue ? counted.Value - expected : null,
            boxes.Count(box => box.Estado == EstadoCaja.ABIERTA),
            boxes.Count(box => box.Estado == EstadoCaja.CERRADA),
            expensesOutsideCash,
            employeePayments,
            employeePaymentsOutsideCash,
            cashContributions);
    }

    private static async Task<ObligationAnalytics> LoadReceivablesAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var accounts = await context.CuentasPorCobrar.AsNoTracking()
            .Where(account => account.FechaCreacion < period.UtcEndExclusive)
            .Select(account => new ObligationAccountFact(
                account.CuentaPorCobrarId,
                account.ClienteId,
                account.Cliente.CodigoCliente,
                account.Cliente.NombreCompleto,
                account.TotalOriginalCentavos,
                account.FechaCreacion,
                account.FechaVencimiento,
                account.FechaPago,
                account.FechaAnulacion))
            .ToListAsync(cancellationToken);
        var payments = await context.AbonosCuentaPorCobrar.AsNoTracking()
            .Where(payment => payment.FechaAbono < period.UtcEndExclusive)
            .Select(payment => new ObligationPaymentFact(
                payment.CuentaPorCobrarId,
                payment.FechaAbono,
                payment.MontoCentavos))
            .ToListAsync(cancellationToken);
        return BuildObligations(accounts, payments, period, timeZone);
    }

    private static async Task<ObligationAnalytics> LoadPayablesAsync(
        AppDbContext context,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var accounts = await context.CuentasPorPagar.AsNoTracking()
            .Where(account => account.FechaCreacion < period.UtcEndExclusive)
            .Select(account => new ObligationAccountFact(
                account.CuentaPorPagarId,
                account.ProveedorId,
                account.Proveedor.CodigoProveedor,
                account.Proveedor.NombreRazonSocial,
                account.TotalOriginalCentavos,
                account.FechaCreacion,
                account.FechaVencimiento,
                account.FechaPago,
                null))
            .ToListAsync(cancellationToken);
        var installments = await context.AbonosCuentaPorPagar.AsNoTracking()
            .Where(payment => payment.FechaAbono < period.UtcEndExclusive)
            .Select(payment => new ObligationPaymentFact(
                payment.CuentaPorPagarId,
                payment.FechaAbono,
                payment.MontoCentavos))
            .ToListAsync(cancellationToken);
        var initialPayments = await (
                from payment in context.PagosCompra.AsNoTracking()
                join account in context.CuentasPorPagar.AsNoTracking()
                    on payment.CompraId equals account.CompraId
                where payment.FechaPago < period.UtcEndExclusive
                select new ObligationPaymentFact(
                    account.CuentaPorPagarId,
                    payment.FechaPago,
                    payment.MontoCentavos))
            .ToListAsync(cancellationToken);
        return BuildObligations(accounts, installments.Concat(initialPayments).ToList(), period, timeZone);
    }

    private static ObligationAnalytics BuildObligations(
        IReadOnlyList<ObligationAccountFact> accounts,
        IReadOnlyList<ObligationPaymentFact> payments,
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone)
    {
        var eligible = accounts.Where(account => !account.CancelledUtc.HasValue ||
            account.CancelledUtc.Value >= period.UtcEndExclusive).ToList();
        var paidByAccount = payments.GroupBy(payment => payment.AccountId)
            .ToDictionary(group => group.Key, group => group.Sum(payment => payment.AmountCentavos));
        var balances = eligible.Select(account => new AccountBalance(
                account,
                Math.Max(0, account.TotalCentavos - paidByAccount.GetValueOrDefault(account.AccountId))))
            .ToList();
        var asOfLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(period.UtcEndExclusive.AddTicks(-1), DateTimeKind.Utc),
            timeZone).Date;
        var open = balances.Where(balance => balance.BalanceCentavos > 0).ToList();
        var aging = new[]
        {
            Aging("Vigente", open.Where(item => item.Account.DueDateUtc.Date >= asOfLocal)),
            Aging("1-30 dias", open.Where(item => DaysOverdue(item.Account.DueDateUtc, asOfLocal) is >= 1 and <= 30)),
            Aging("31-60 dias", open.Where(item => DaysOverdue(item.Account.DueDateUtc, asOfLocal) is >= 31 and <= 60)),
            Aging("61-90 dias", open.Where(item => DaysOverdue(item.Account.DueDateUtc, asOfLocal) is >= 61 and <= 90)),
            Aging("Mas de 90 dias", open.Where(item => DaysOverdue(item.Account.DueDateUtc, asOfLocal) > 90))
        };
        var highest = open.GroupBy(item => new
            {
                item.Account.CounterpartyId,
                item.Account.CounterpartyCode,
                item.Account.CounterpartyName
            })
            .Select(group => new CounterpartyBalanceAnalytics(
                group.Key.CounterpartyId,
                group.Key.CounterpartyCode,
                group.Key.CounterpartyName,
                group.Sum(item => item.BalanceCentavos)))
            .OrderByDescending(item => item.BalanceCentavos)
            .ThenBy(item => item.Name)
            .Take(10)
            .ToList();

        var buckets = CreateBuckets(period, timeZone);
        var openingCreated = eligible.Where(account => account.CreatedUtc < period.UtcStart)
            .Sum(account => account.TotalCentavos);
        var openingPaid = payments.Where(payment => payment.DateUtc < period.UtcStart)
            .Sum(payment => payment.AmountCentavos);
        var runningBalance = Math.Max(0, openingCreated - openingPaid);
        var evolution = new List<BalanceEvolutionPoint>(buckets.Count);
        foreach (var bucket in buckets)
        {
            var created = eligible.Where(account => IsInBucket(account.CreatedUtc, bucket, period.Grouping, timeZone))
                .Sum(account => account.TotalCentavos);
            var paid = payments.Where(payment => IsInBucket(payment.DateUtc, bucket, period.Grouping, timeZone))
                .Sum(payment => payment.AmountCentavos);
            runningBalance = Math.Max(0, runningBalance + created - paid);
            evolution.Add(new BalanceEvolutionPoint(
                bucket.BucketStart,
                bucket.Label,
                created,
                paid,
                created - paid,
                runningBalance));
        }

        return new ObligationAnalytics(
            open.Sum(item => item.BalanceCentavos),
            eligible.Where(account => account.CreatedUtc >= period.UtcStart).Sum(account => account.TotalCentavos),
            payments.Where(payment => payment.DateUtc >= period.UtcStart).Sum(payment => payment.AmountCentavos),
            eligible.Count(account => account.PaidUtc >= period.UtcStart && account.PaidUtc < period.UtcEndExclusive),
            open.Count(item => item.Account.DueDateUtc.Date < asOfLocal),
            aging,
            highest,
            evolution);
    }

    private static AgingBucketAnalytics Aging(string name, IEnumerable<AccountBalance> rows)
    {
        var materialized = rows.ToList();
        return new AgingBucketAnalytics(name, materialized.Count, materialized.Sum(row => row.BalanceCentavos));
    }

    private static int DaysOverdue(DateTime dueDateUtc, DateTime asOfLocal) =>
        Math.Max(0, (asOfLocal - dueDateUtc.Date).Days);

    private static IReadOnlyList<BucketDefinition> CreateBuckets(
        AnalyticsPeriodDefinition period,
        TimeZoneInfo timeZone)
    {
        var first = NormalizeBucket(period.UtcStart, period.Grouping, timeZone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(period.UtcEndExclusive, DateTimeKind.Utc),
            timeZone);
        var result = new List<BucketDefinition>();
        for (var current = first; current < end; current = AddBucket(current, period.Grouping))
        {
            var offset = timeZone.GetUtcOffset(current);
            result.Add(new BucketDefinition(
                current,
                new DateTimeOffset(current, offset),
                FormatBucket(current, period.Grouping)));
        }

        return result;
    }

    private static bool IsInBucket(
        DateTime dateUtc,
        BucketDefinition bucket,
        AnalyticsGrouping grouping,
        TimeZoneInfo timeZone) => NormalizeBucket(dateUtc, grouping, timeZone) == bucket.LocalKey;

    private static DateTime NormalizeBucket(DateTime dateUtc, AnalyticsGrouping grouping, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc), timeZone);
        return grouping switch
        {
            AnalyticsGrouping.Hour => new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0),
            AnalyticsGrouping.Day => local.Date,
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

    private static string FormatBucket(DateTime value, AnalyticsGrouping grouping) => grouping switch
    {
        AnalyticsGrouping.Hour => value.ToString("HH:mm"),
        AnalyticsGrouping.Day => value.ToString("dd/MM"),
        AnalyticsGrouping.Month => value.ToString("MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("es-SV")),
        AnalyticsGrouping.Year => value.ToString("yyyy"),
        _ => value.ToString("d")
    };

    private static AnalyticsMetricResult MoneyMetric(long current, long compared) =>
        new(current, MetricComparisonCalculator.Calculate(current, compared));

    private static DecimalAnalyticsMetricResult DecimalMetric(decimal? current, decimal? compared)
    {
        if (!current.HasValue && !compared.HasValue)
        {
            return new(null, null, null, MetricComparisonState.NoChange);
        }

        if (!compared.HasValue || compared.Value == 0)
        {
            return new(
                current,
                compared,
                null,
                current.GetValueOrDefault() > 0
                    ? MetricComparisonState.NewMovement
                    : MetricComparisonState.NoComparisonBase);
        }

        decimal? change = current.HasValue
            ? decimal.Round((current.Value - compared.Value) * 100m / compared.Value, 1)
            : null;
        var state = !current.HasValue || current.Value < compared.Value
            ? MetricComparisonState.Decrease
            : current.Value > compared.Value ? MetricComparisonState.Increase : MetricComparisonState.NoChange;
        return new(current, compared, change, state);
    }

    private static HistoricalCoverageMetric Coverage(
        string name,
        int complete,
        int total,
        DateTime? reliableFromUtc)
    {
        _ = reliableFromUtc;
        var percentage = total == 0 ? 0 : decimal.Round(complete * 100m / total, 2);
        var quality = total == 0 || complete == 0
            ? HistoricalDataQuality.Unavailable
            : complete == total ? HistoricalDataQuality.Exact : HistoricalDataQuality.Partial;
        return new HistoricalCoverageMetric(name, complete, total, percentage, quality);
    }

    private static async Task<DateTime?> LoadReceptionSnapshotStartAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var value = await context.ConfiguracionSistema.AsNoTracking()
            .Where(item => item.Clave == ReceptionSnapshotStartKey)
            .Select(item => item.Valor)
            .SingleOrDefaultAsync(cancellationToken);
        return DateTime.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : null;
    }

    private async Task EnsureActiveAdminAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var user = _currentSession.UsuarioActual;
        if (user is null || !_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede consultar la analitica financiera.");
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

    private sealed record ProfitFact(
        DateTime DateUtc,
        long NetSalesCentavos,
        long CostEffectCentavos,
        bool IsCovered);

    private sealed record ReceptionFact(
        DateTime DateUtc,
        int Quantity,
        long GrossUnitCostCentavos,
        long? NetUnitCostCentavos,
        long? AssignedDiscountCentavos)
    {
        public bool IsCovered => NetUnitCostCentavos.HasValue && AssignedDiscountCentavos.HasValue;
    }

    private sealed record ObligationAccountFact(
        int AccountId,
        int CounterpartyId,
        string CounterpartyCode,
        string CounterpartyName,
        long TotalCentavos,
        DateTime CreatedUtc,
        DateTime DueDateUtc,
        DateTime? PaidUtc,
        DateTime? CancelledUtc);

    private sealed record ObligationPaymentFact(int AccountId, DateTime DateUtc, long AmountCentavos);
    private sealed record AccountBalance(ObligationAccountFact Account, long BalanceCentavos);
    private sealed record BucketDefinition(DateTime LocalKey, DateTimeOffset BucketStart, string Label);
    private sealed record PeriodFacts(
        long CoveredNetSalesCentavos,
        long CostOfGoodsSoldCentavos,
        long GrossProfitCentavos,
        decimal? GrossMarginPercentage,
        int CoveredCostRecords,
        int TotalCostRecords,
        long OperatingExpensesCentavos,
        long CommittedPurchasesCentavos,
        long ReceivedInventoryCentavos,
        long SupplierPaymentsCentavos,
        int CoveredReceptionRecords,
        int TotalReceptionRecords,
        IReadOnlyList<FinancialAnalyticsPoint> Trend,
        IReadOnlyList<ExpenseAnalyticsRow> Expenses,
        CashAnalyticsSummary Cash,
        ObligationAnalytics Receivables,
        ObligationAnalytics Payables);
}
