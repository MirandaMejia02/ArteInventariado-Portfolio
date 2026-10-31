namespace ArteInventariado.Domain.Entities;

public class EmployeeLoginEvent
{
    public long EmployeeLoginEventId { get; set; }
    public int UserId { get; set; }
    public DateTime LoggedInAtUtc { get; set; }
    public string? InstallationId { get; set; }
    public string? StationMode { get; set; }
    public Usuario User { get; set; } = null!;
}
