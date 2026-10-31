using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Infrastructure.Services;

internal static class SupplierFinancialRules
{
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static void ValidateMethod(MetodoPago method, string? reference)
    {
        if (method is not MetodoPago.EFECTIVO and not MetodoPago.TRANSFERENCIA and not MetodoPago.BITCOIN)
        {
            throw new InvalidOperationException("El metodo de pago no esta permitido.");
        }

        if (method is MetodoPago.TRANSFERENCIA or MetodoPago.BITCOIN && string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("La referencia es obligatoria para transferencia y Bitcoin.");
        }
    }

    public static EstadoCuentaPorPagar ResolveAccountState(long paid, long balance, DateTime dueDate, DateTime now)
    {
        if (balance == 0)
        {
            return EstadoCuentaPorPagar.PAGADA;
        }

        if (dueDate.Date < now.Date)
        {
            return EstadoCuentaPorPagar.VENCIDA;
        }

        return paid > 0 ? EstadoCuentaPorPagar.PARCIAL : EstadoCuentaPorPagar.ABIERTA;
    }

    public static EstadoFinancieroCompra ResolvePurchaseState(long paid, long balance) =>
        balance == 0
            ? EstadoFinancieroCompra.PAGADA
            : paid > 0
                ? EstadoFinancieroCompra.PARCIAL
                : EstadoFinancieroCompra.PENDIENTE;

    public static bool IsOperationIdUniqueConflict(Exception ex)
    {
        var message = ex.GetBaseException().Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("operacion_id", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatCentavos(long value) => (value / 100m).ToString("$0.00");
}
