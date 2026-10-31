using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAuditLogService
{
    Task<IReadOnlyList<AuditLogEntry>> GetEntriesAsync(
        AuditLogFilter filter,
        CancellationToken cancellationToken = default);
}
