using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IAuditService
{
    Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default);
}
