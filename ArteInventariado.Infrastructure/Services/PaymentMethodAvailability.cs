using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

internal static class PaymentMethodAvailability
{
    public static Task EnsureAvailableAsync(
        AppDbContext context,
        MetodoPago? method,
        CancellationToken cancellationToken = default) =>
        method == MetodoPago.BITCOIN
            ? EnsureBitcoinEnabledAsync(context, cancellationToken)
            : Task.CompletedTask;

    public static Task EnsureAvailableAsync(
        AppDbContext context,
        MetodoReembolso? method,
        CancellationToken cancellationToken = default) =>
        method == MetodoReembolso.BITCOIN
            ? EnsureBitcoinEnabledAsync(context, cancellationToken)
            : Task.CompletedTask;

    public static async Task EnsureBitcoinEnabledAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var configured = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => c.Clave == SystemConfigService.BitcoinEnabledKey)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        if (!SystemConfigService.IsBitcoinEnabledValue(configured))
        {
            throw new InvalidOperationException(
                "Bitcoin esta deshabilitado en la configuracion del negocio.");
        }
    }
}
