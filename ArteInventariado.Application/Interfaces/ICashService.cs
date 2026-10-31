using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICashService
{
    Task<CashSummary> GetTodaySummaryAsync(CancellationToken cancellationToken = default);

    Task<CashExpectedBalanceSummary> GetTodayExpectedCashBalanceAsync(
        CancellationToken cancellationToken = default);

    Task<CashOpeningReference?> GetPreviousDayCashOpeningReferenceAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashSummary>> GetSessionHistoryAsync(
        DateOnly desde,
        DateOnly hasta,
        CancellationToken cancellationToken = default);

    Task<CashRecoveryStatus?> GetOpenCashRecoveryAsync(CancellationToken cancellationToken = default);

    Task<CashSummary> ContinueCashAsync(ContinueCashRequest request, CancellationToken cancellationToken = default);

    Task<CashSummary> RegularizeCashAsync(RegularizeCashRequest request, CancellationToken cancellationToken = default);

    Task<CashSummary> RegularizeClosedCashDayAsync(
        RegularizeClosedCashDayRequest request,
        CancellationToken cancellationToken = default);

    Task<CashSummary> OpenCashAsync(OpenCashRequest request, CancellationToken cancellationToken = default);

    Task<CashSummary> OpenCashAutomaticallyIfEnabledAsync(CancellationToken cancellationToken = default);

    Task<CashSummary> CloseCashAsync(CloseCashRequest request, CancellationToken cancellationToken = default);

    Task<CashWithdrawalSummary> RegisterWithdrawalAsync(RegisterWithdrawalRequest request, CancellationToken cancellationToken = default);

    Task<CashContributionSummary> RegisterCashContributionAsync(RegisterCashContributionRequest request, CancellationToken cancellationToken = default);

    Task<CashExpenseSummary> RegisterExpenseAsync(RegisterExpenseRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashWithdrawalSummary>> GetTodayWithdrawalsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashExpenseSummary>> GetTodayExpensesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashRefundSummary>> GetTodayRefundsAsync(CancellationToken cancellationToken = default);
}
