using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;

    [ActivatorUtilitiesConstructor]
    public AuditService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // Conservado para fixtures unitarios que comparten una conexion SQLite en memoria.
    internal AuditService(AppDbContext context)
    {
        _legacyContext = context;
    }

    public async Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var bitacora = AuditEntryFactory.Create(request);

        try
        {
            lease.Context.Bitacora.Add(bitacora);
            await lease.Context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ex.Data["AuditTracker"] = string.Join(" | ", lease.Context.ChangeTracker
                .Entries<Bitacora>()
                .Select(entry =>
                    $"State={entry.State}; BitacoraId={entry.Entity.BitacoraId}; " +
                    $"Temporary={entry.Property(item => item.BitacoraId).IsTemporary}"));
            throw;
        }
        finally
        {
            if (_legacyContext is not null)
            {
                _legacyContext.Entry(bitacora).State = EntityState.Detached;
            }
        }
    }
}
