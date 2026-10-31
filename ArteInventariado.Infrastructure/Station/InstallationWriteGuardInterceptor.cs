using ArteInventariado.Application.Interfaces;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArteInventariado.Infrastructure.Station;

internal sealed class InstallationWriteGuardInterceptor(IInstallationContext installationContext)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Guard(DbContext? context)
    {
        if (context is AppDbContext && context.ChangeTracker.HasChanges())
        {
            installationContext.EnsureOperationalWriteAllowed("Persistencia operativa");
        }
    }
}
