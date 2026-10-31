using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;

namespace ArteInventariado.Infrastructure.Services;

internal static class SalePaymentEffectQueries
{
    public static IQueryable<PagoVenta> ExcludeVoidSalePayments(
        this IQueryable<PagoVenta> payments,
        AppDbContext context)
    {
        return payments.Where(payment => !context.Devoluciones.Any(correction =>
            correction.VentaId == payment.VentaId &&
            correction.TipoCorreccion == TipoCorreccionVenta.ANULACION &&
            correction.EsErrorRegistroSinOperacionReal));
    }
}
