using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;

    [ActivatorUtilitiesConstructor]
    public AuditLogService(IDbContextFactory<AppDbContext> contextFactory, ICurrentSession currentSession)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
    }

    internal AuditLogService(AppDbContext context, ICurrentSession currentSession)
    {
        _legacyContext = context;
        _currentSession = currentSession;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetEntriesAsync(
        AuditLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();

        var query = lease.Context.Bitacora
            .AsNoTracking()
            .Include(b => b.Usuario)
            .AsQueryable();

        if (filter.FechaDesde.HasValue)
        {
            var desde = ToUtcStart(filter.FechaDesde.Value);
            query = query.Where(b => b.FechaAccion >= desde);
        }

        if (filter.FechaHasta.HasValue)
        {
            var hasta = ToUtcStart(filter.FechaHasta.Value.AddDays(1));
            query = query.Where(b => b.FechaAccion < hasta);
        }

        if (filter.UsuarioId.HasValue)
        {
            query = query.Where(b => b.UsuarioId == filter.UsuarioId.Value);
        }

        if (filter.TipoAccion.HasValue)
        {
            query = query.Where(b => b.TipoAccion == filter.TipoAccion.Value);
        }

        var searchText = filter.TextoBusqueda?.Trim();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = $"%{EscapeLikeTerm(searchText)}%";
            var matchingActions = Enum.GetValues<TipoBitacora>()
                .Where(action => action.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var matchesSystemUser = "Sistema".Contains(searchText, StringComparison.OrdinalIgnoreCase);

            query = query.Where(b =>
                EF.Functions.Like(b.Entidad, pattern, "\\") ||
                EF.Functions.Like(b.Descripcion, pattern, "\\") ||
                (b.Usuario != null && EF.Functions.Like(b.Usuario.NombreUsuario, pattern, "\\")) ||
                matchingActions.Contains(b.TipoAccion) ||
                (matchesSystemUser && b.UsuarioId == null));
        }

        return await query
            .OrderByDescending(b => b.FechaAccion)
            .Take(1000)
            .Select(b => new AuditLogEntry(
                b.BitacoraId,
                b.FechaAccion,
                b.Usuario == null ? "Sistema" : b.Usuario.NombreUsuario,
                b.TipoAccion,
                b.Entidad,
                b.Descripcion))
            .ToListAsync(cancellationToken);
    }

    private static string EscapeLikeTerm(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private void EnsureAdminSession()
    {
        if (!_currentSession.EsAdmin)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede ver la bitacora.");
        }
    }

    private static DateTime ToUtcStart(DateOnly date)
    {
        return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
    }
}
