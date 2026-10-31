using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Data;

internal static class PersistenceGuard
{
    public static async Task SaveChangesAsync(
        AppDbContext context,
        string operation,
        string userMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw await DbConcurrencyDiagnostics.WrapAsync(
                exception,
                operation,
                userMessage,
                cancellationToken);
        }
    }
}
