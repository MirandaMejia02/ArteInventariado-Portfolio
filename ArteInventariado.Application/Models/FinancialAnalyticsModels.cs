namespace ArteInventariado.Application.Models;

public sealed record DecimalAnalyticsMetricResult(
    decimal? Value,
    decimal? ComparedValue,
    decimal? ChangePercentage,
    MetricComparisonState State);

public sealed record FinancialAnalyticsPoint(
    DateTimeOffset BucketStart,
    string Label,
    long CoveredNetSalesCentavos,
    long CostOfGoodsSoldCentavos,
    long GrossProfitCentavos,
    long OperatingExpensesCentavos,
    long ReceivedInventoryCentavos);

public sealed record ExpenseAnalyticsRow(
    int ExpenseId,
    DateTime DateUtc,
    string Category,
    string User,
    string PaymentMethod,
    string? Reference,
    string Description,
    long AmountCentavos,
    string FundingOrigin = "No especificado (histórico)",
    string? FundingResponsible = null,
    bool AffectsCash = false);

public sealed record ExpenseCategoryAnalyticsRow(
    string Category,
    long AmountCentavos,
    decimal ParticipationPercentage);

public sealed record FinancialOverviewAnalytics(
    AnalyticsMetricResult NetSales,
    AnalyticsMetricResult CoveredNetSales,
    AnalyticsMetricResult CostOfGoodsSold,
    AnalyticsMetricResult GrossProfit,
    DecimalAnalyticsMetricResult GrossMargin,
    AnalyticsMetricResult OperatingExpenses,
    AnalyticsMetricResult InventoryInvestment,
    HistoricalCoverageMetric CostCoverage,
    IReadOnlyList<FinancialAnalyticsPoint> Trend,
    IReadOnlyList<ExpenseCategoryAnalyticsRow> ExpensesByCategory,
    IReadOnlyList<ExpenseAnalyticsRow> Expenses);

public sealed record CashAnalyticsSummary(
    long OpeningCashCentavos,
    long CashSalesCentavos,
    long CashCustomerPaymentsCentavos,
    long OtherCashInputsCentavos,
    long CashRefundsCentavos,
    long SupplierCashPaymentsCentavos,
    long ExpensesCentavos,
    long WithdrawalsCentavos,
    long ExpectedCashCentavos,
    long NetMovementCentavos,
    long? CountedCashCentavos,
    long? ClosingDifferenceCentavos,
    int OpenCashRegisters,
    int ClosedCashRegisters,
    long ExpensesOutsideCashCentavos = 0,
    long EmployeePaymentsCentavos = 0,
    long EmployeePaymentsOutsideCashCentavos = 0,
    long CashContributionsCentavos = 0);

public sealed record PurchaseInvestmentAnalytics(
    AnalyticsMetricResult CommittedPurchases,
    AnalyticsMetricResult ReceivedMerchandise,
    AnalyticsMetricResult SupplierPayments,
    long CurrentInventoryValueCentavos,
    HistoricalCoverageMetric ReceptionSnapshotCoverage,
    InventoryValuationTotals? InventoryValuation = null);

public sealed record AgingBucketAnalytics(
    string Name,
    int Accounts,
    long BalanceCentavos);

public sealed record CounterpartyBalanceAnalytics(
    int CounterpartyId,
    string Code,
    string Name,
    long BalanceCentavos);

public sealed record ObligationAnalytics(
    long OutstandingBalanceCentavos,
    long CreatedCentavos,
    long PaymentsCentavos,
    int PaidAccounts,
    int OverdueAccounts,
    IReadOnlyList<AgingBucketAnalytics> Aging,
    IReadOnlyList<CounterpartyBalanceAnalytics> HighestBalances,
    IReadOnlyList<BalanceEvolutionPoint> BalanceEvolution);

public sealed record BalanceEvolutionPoint(
    DateTimeOffset BucketStart,
    string Label,
    long CreatedCentavos,
    long PaymentsCentavos,
    long NetChangeCentavos,
    long BalanceCentavos);

public sealed record FinancialAnalyticsReport(
    AnalyticsPeriodDefinition Period,
    AnalyticsPeriodDefinition ComparedPeriod,
    FinancialOverviewAnalytics Finance,
    CashAnalyticsSummary Cash,
    PurchaseInvestmentAnalytics Purchases,
    ObligationAnalytics Receivables,
    ObligationAnalytics Payables);
