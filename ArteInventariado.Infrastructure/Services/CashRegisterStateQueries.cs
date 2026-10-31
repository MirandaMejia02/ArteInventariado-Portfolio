using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

internal static class CashRegisterStateQueries
{
    public static IOrderedQueryable<Caja> OpenCash(AppDbContext context) =>
        context.Cajas
            .Where(cash => cash.Estado == EstadoCaja.ABIERTA)
            .OrderByDescending(cash => cash.FechaApertura)
            .ThenByDescending(cash => cash.CajaId);

    public static Task<Caja?> GetCurrentOpenCashAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default) =>
        OpenCash(context).FirstOrDefaultAsync(cancellationToken);
}
