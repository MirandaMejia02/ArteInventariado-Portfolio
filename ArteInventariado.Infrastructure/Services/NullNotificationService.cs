using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Services;

public class NullNotificationService : INotificationService
{
    public Task NotifyAsync(CashNotificationEvent notificationEvent, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
