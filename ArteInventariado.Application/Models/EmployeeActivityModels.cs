using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record EmployeeActivitySummary(int UserId, string UserName, string FullName, RolUsuario Role, bool Active, DateTime CreatedAtUtc, DateTime? LastLoginUtc, int ActivityDays);
public sealed record EmployeeLoginActivity(long Id, int UserId, DateTime LoggedInAtUtc, string? InstallationId, string? StationMode);
public sealed record EmployeeActivityDay(DateOnly LocalDate, int LoginCount);
