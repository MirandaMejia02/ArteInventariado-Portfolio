using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class EmployeeActivityQueryServiceTests
{
    [Fact]
    public async Task ActivityDays_UseBusinessLocalDateAcrossUtcMidnight()
    {
        await using var db = CreateDatabase();
        db.Usuarios.AddRange(Admin(1, "admin"), Employee(2, "ana"));
        db.ConfiguracionSistema.Add(new ConfiguracionSistema { Clave = "negocio.zona_horaria", Valor = "UTC" });
        db.EmployeeLoginEvents.AddRange(
            Event(2, new DateTime(2026, 9, 8, 23, 30, 0)),
            Event(2, new DateTime(2026, 9, 9, 0, 15, 0)));
        await db.SaveChangesAsync();

        var result = await Service(db, 1, "admin").GetEmployeesAsync();
        Assert.Equal(2, result.Single(x => x.UserId == 2).ActivityDays);
    }

    [Fact]
    public async Task ThreeLoginsSameLocalDay_CountAsThreeEventsAndOneDay()
    {
        await using var db = CreateDatabase();
        db.Usuarios.AddRange(Admin(1, "admin"), Employee(2, "ana"));
        db.ConfiguracionSistema.Add(new ConfiguracionSistema { Clave = "negocio.zona_horaria", Valor = "UTC" });
        db.EmployeeLoginEvents.AddRange(Event(2, new DateTime(2026, 9, 8, 8, 0, 0)), Event(2, new DateTime(2026, 9, 8, 12, 0, 0)), Event(2, new DateTime(2026, 9, 8, 16, 0, 0)));
        await db.SaveChangesAsync();
        var service = Service(db, 1, "admin");
        Assert.Equal(3, (await service.GetLoginHistoryAsync(2)).Count);
        Assert.Equal(1, (await service.GetEmployeesAsync()).Single(x => x.UserId == 2).ActivityDays);
    }

    [Fact]
    public async Task MissingTimezone_UsesCentralAmericaDefault()
    {
        await using var db = CreateDatabase();
        db.Usuarios.AddRange(Admin(1, "admin"), Employee(2, "ana"));
        db.EmployeeLoginEvents.Add(Event(2, new DateTime(2026, 9, 8, 5, 30, 0)));
        await db.SaveChangesAsync();
        var rows = await Service(db, 1, "admin").GetEmployeesAsync();
        Assert.Equal(1, rows.Single(x => x.UserId == 2).ActivityDays);
    }

    [Fact]
    public async Task Authorization_AllowsPrincipalAndDelegatedAdmin_RejectsOthers()
    {
        await using var db = CreateDatabase();
        db.Usuarios.AddRange(Admin(1, "admin"), Admin(2, "delegado"), Employee(3, "empleado"));
        db.UsuarioPermisos.Add(new UsuarioPermiso { UsuarioId = 2, Permiso = PermisoEmpleado.EMPLEADOS_VER, Habilitado = true });
        await db.SaveChangesAsync();

        await Service(db, 1, "admin").GetEmployeesAsync();
        await Service(db, 2, "delegado").GetEmployeesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, 3, "empleado").GetEmployeesAsync());
    }

    [Fact]
    public async Task InactiveEmployee_RetainsHistoricalActivity()
    {
        await using var db = CreateDatabase();
        var inactive = Employee(2, "ana"); inactive.Activo = false;
        db.Usuarios.AddRange(Admin(1, "admin"), inactive);
        db.EmployeeLoginEvents.Add(Event(2, new DateTime(2026, 9, 8, 12, 0, 0)));
        await db.SaveChangesAsync();
        var rows = await Service(db, 1, "admin").GetEmployeesAsync();
        Assert.Equal(1, rows.Single(x => x.UserId == 2).ActivityDays);
        var history = await Service(db, 1, "admin").GetLoginHistoryAsync(2);
        Assert.Single(history);
    }

    [Fact]
    public async Task ActivityCalendar_UsesBusinessTimezoneAndCountsEachLogin()
    {
        await using var db = CreateDatabase();
        db.Usuarios.AddRange(Admin(1, "admin"), Employee(2, "ana"));
        db.ConfiguracionSistema.Add(new ConfiguracionSistema
        {
            Clave = "negocio.zona_horaria",
            Valor = "Central America Standard Time"
        });
        db.EmployeeLoginEvents.AddRange(
            Event(2, new DateTime(2026, 9, 1, 5, 30, 0)), // Aug 31, 23:30 local: outside September.
            Event(2, new DateTime(2026, 9, 1, 6, 0, 0)), // Sep 1, 00:00 local.
            Event(2, new DateTime(2026, 9, 1, 18, 0, 0)), // Sep 1, 12:00 local.
            Event(2, new DateTime(2026, 9, 2, 5, 30, 0)), // Sep 1, 23:30 local.
            Event(2, new DateTime(2026, 9, 2, 6, 0, 0))); // Sep 2, 00:00 local.
        await db.SaveChangesAsync();

        var result = await Service(db, 1, "admin").GetActivityCalendarAsync(2, 2026, 9);

        Assert.Equal(
            [
                new EmployeeActivityDay(new DateOnly(2026, 9, 1), 3),
                new EmployeeActivityDay(new DateOnly(2026, 9, 2), 1)
            ],
            result);
    }

    [Fact]
    public async Task ActivityCalendar_WithNoActivity_ReturnsEmpty()
    {
        await using var db = CreateDatabase();
        db.Usuarios.AddRange(Admin(1, "admin"), Employee(2, "ana"));
        await db.SaveChangesAsync();

        var result = await Service(db, 1, "admin").GetActivityCalendarAsync(2, 2026, 9);

        Assert.Empty(result);
    }

    private static Usuario Admin(int id, string name) => new() { UsuarioId = id, NombreUsuario = name, NombreCompleto = name, Rol = RolUsuario.ADMIN, PasswordHash = "x" };
    private static Usuario Employee(int id, string name) => new() { UsuarioId = id, NombreUsuario = name, NombreCompleto = name, Rol = RolUsuario.EMPLEADO, PasswordHash = "x" };
    private static EmployeeLoginEvent Event(int userId, DateTime utc) => new() { UserId = userId, LoggedInAtUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc), InstallationId = "test", StationMode = "PRINCIPAL" };

    private static AppDbContext CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static EmployeeActivityQueryService Service(AppDbContext db, int id, string name)
    {
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(id, name, name, RolUsuario.ADMIN));
        return new EmployeeActivityQueryService(new Factory(db), session, TimeProvider.System);
    }

    private sealed class Factory(AppDbContext db) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(db.Database.GetDbConnection()).Options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
