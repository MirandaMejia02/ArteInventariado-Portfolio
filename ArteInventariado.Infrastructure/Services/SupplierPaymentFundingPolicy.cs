using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Infrastructure.Services;

/// <summary>
/// Shared validation for supplier payment funding. The funding source is
/// intentionally independent from the payment method; the service derives
/// the cash effect and never accepts a caller-supplied effect flag.
/// </summary>
internal static class SupplierPaymentFundingPolicy
{
    public static string? NormalizeResponsible(OrigenFondos origin, string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if ((origin is OrigenFondos.FONDOS_EXTERNOS_DUENO or OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA) && string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Debe indicar el responsable o fuente del dinero externo.");
        }

        if (normalized?.Length > 160)
        {
            throw new ArgumentException("El responsable o fuente no puede superar 160 caracteres.", nameof(value));
        }

        return normalized;
    }

    public static void Validate(OrigenFondos origin, MetodoPago method, string? reference, string? responsible)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "El origen de fondos no es valido.");
        }

        if (origin == (OrigenFondos)0)
        {
            throw new ArgumentException("El origen historico no puede utilizarse en una nueva escritura.", nameof(origin));
        }

        if (origin == OrigenFondos.CAJA_FISICA && method != MetodoPago.EFECTIVO)
        {
            throw new InvalidOperationException(
                "CAJA_FISICA solo puede usarse con el metodo EFECTIVO. Para transferencia o Bitcoin, seleccione una fuente de fondos fuera de caja.");
        }

        SupplierFinancialRules.ValidateMethod(method, reference);
        _ = NormalizeResponsible(origin, responsible);
    }

    public static bool AffectsPhysicalCash(OrigenFondos origin) =>
        origin == OrigenFondos.CAJA_FISICA;
}
