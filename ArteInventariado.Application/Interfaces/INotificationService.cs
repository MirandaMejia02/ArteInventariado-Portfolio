using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface INotificationService
{
    Task NotifyAsync(CashNotificationEvent notificationEvent, CancellationToken cancellationToken = default);
}
