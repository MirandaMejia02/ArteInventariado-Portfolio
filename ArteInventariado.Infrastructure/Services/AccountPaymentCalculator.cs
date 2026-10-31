using ArteInventariado.Domain.Entities;

namespace ArteInventariado.Infrastructure.Services;

internal static class AccountPaymentCalculator
{
    public static AccountPaymentComputation Calculate(CuentaPorCobrar account, long amountCentavos)
    {
        if (amountCentavos <= 0)
        {
            throw new ArgumentException("El monto del abono debe ser mayor que cero.");
        }

        if (amountCentavos > account.SaldoActualCentavos)
        {
            throw new InvalidOperationException("El abono no puede superar el saldo actual de la cuenta.");
        }

        var pending = CalculatePending(account);
        var appliedInterest = Math.Min(amountCentavos, pending.InterestPending);
        var appliedCapital = amountCentavos - appliedInterest;
        var newInterestPending = pending.InterestPending - appliedInterest;
        var newCapitalPending = pending.CapitalPending - appliedCapital;
        var newBalance = account.SaldoActualCentavos - amountCentavos;

        if (appliedInterest < 0 ||
            appliedCapital < 0 ||
            appliedInterest + appliedCapital != amountCentavos ||
            newBalance < 0 ||
            account.SaldoActualCentavos - amountCentavos != newBalance ||
            newInterestPending < 0 ||
            newCapitalPending < 0)
        {
            throw new InvalidOperationException("La distribucion del abono no coincide con el monto registrado.");
        }

        return new AccountPaymentComputation(
            pending.InterestPending,
            pending.CapitalPending,
            appliedInterest,
            appliedCapital,
            newInterestPending,
            newCapitalPending,
            newBalance);
    }

    public static PendingAmounts CalculatePending(CuentaPorCobrar account)
    {
        var interestPaid = Math.Min(account.TotalPagadoCentavos, account.InteresCalculadoCentavos);
        var interestPending = Math.Max(0, account.InteresCalculadoCentavos - interestPaid);
        var capitalPending = account.Detalles.Sum(d => d.CapitalPendienteCentavos);
        var expectedBalance = interestPending + capitalPending;

        if (expectedBalance != account.SaldoActualCentavos)
        {
            throw new InvalidOperationException("La cuenta tiene saldos internos inconsistentes y no puede recibir abonos.");
        }

        return new PendingAmounts(interestPending, capitalPending);
    }
}

internal sealed record PendingAmounts(long InterestPending, long CapitalPending);

internal sealed record AccountPaymentComputation(
    long InterestPending,
    long CapitalPending,
    long AppliedInterest,
    long AppliedCapital,
    long NewInterestPending,
    long NewCapitalPending,
    long NewBalance);
