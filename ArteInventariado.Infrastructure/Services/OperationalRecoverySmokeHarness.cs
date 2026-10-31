using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

public static class OperationalRecoverySmokeHarness
{
    public static int Run(string? logPath)
    {
        logPath = string.IsNullOrWhiteSpace(logPath)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.OperationalRecoverySmoke.log")
            : Path.GetFullPath(logPath);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var log = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };
        try
        {
            RunAsync(log).GetAwaiter().GetResult();
            log.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("RESULT=FAILED");
            log.Add(ex.ToString());
            return 1;
        }
        finally
        {
            log.Add($"END={DateTimeOffset.UtcNow:O}");
            File.WriteAllLines(logPath, log);
        }
    }

    private static async Task RunAsync(ICollection<string> log)
    {
        var password = $"{Guid.NewGuid():N}-Aa1!";
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new SmokeContextFactory(connection);
        var now = new DateTimeOffset(2026, 8, 15, 18, 0, 0, TimeSpan.Zero);
        int userId;
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new Usuario
            {
                NombreCompleto = "Administrador smoke",
                NombreUsuario = "admin-smoke-recovery",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = now.UtcDateTime
            };
            setup.Usuarios.Add(user);
            await setup.SaveChangesAsync();
            userId = user.UsuarioId;
            setup.Cajas.Add(new Caja
            {
                FechaOperacion = new DateOnly(2026, 8, 14),
                FechaApertura = now.AddDays(-1).UtcDateTime,
                MontoInicialCentavos = 10_000,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = userId
            });
            await setup.SaveChangesAsync();
        }
        log.Add("setup=OK");

        var session = new SmokeSession(new UsuarioSesion(
            userId,
            "Administrador smoke",
            "admin-smoke-recovery",
            RolUsuario.ADMIN));
        var audit = new AuditService(factory);
        await using var permissionContext = factory.CreateDbContext();
        var permissions = new EmployeePermissionService(permissionContext, session, audit);
        var clock = new SmokeTimeProvider(now);
        var principal = CreateService(factory, session, audit, permissions, new SmokeInstallation(true), clock);

        var recovery = await principal.GetOpenCashRecoveryAsync()
            ?? throw new InvalidOperationException("No se detectó la caja stale del smoke.");
        Require(recovery.IsFromPreviousDay, "La caja stale no fue marcada como fecha anterior.");
        log.Add($"detectedCash={recovery.Cash.CajaId}");
        await principal.ContinueCashAsync(new(recovery.Cash.CajaId!.Value, Guid.NewGuid()));
        var movement = await principal.RegisterWithdrawalAsync(new(500, "Movimiento smoke post-crash"));
        Require(movement.CajaId == recovery.Cash.CajaId, "La continuación creó o utilizó otra caja.");
        log.Add("continueAndMovement=OK");
        var closed = await principal.RegularizeCashAsync(new(
            recovery.Cash.CajaId.Value,
            9_400,
            "Regularización smoke tras cierre inesperado",
            password,
            Guid.NewGuid()));
        Require(closed.Estado == EstadoCaja.CERRADA && closed.DiferenciaCentavos == -100,
            "La regularización no conservó el cálculo esperado.");
        log.Add("regularization=OK");

        await using (var setupConsulta = factory.CreateDbContext())
        {
            setupConsulta.Cajas.Add(new Caja
            {
                FechaOperacion = new DateOnly(2026, 8, 15),
                FechaApertura = now.UtcDateTime,
                MontoInicialCentavos = 2_000,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = userId
            });
            await setupConsulta.SaveChangesAsync();
        }
        var consulta = CreateService(factory, session, audit, permissions, new SmokeInstallation(false), clock);
        var consultaRecovery = await consulta.GetOpenCashRecoveryAsync()
            ?? throw new InvalidOperationException("Consulta no pudo visualizar la caja abierta.");
        var writeRejected = false;
        try
        {
            await consulta.ContinueCashAsync(new(consultaRecovery.Cash.CajaId!.Value, Guid.NewGuid()));
        }
        catch (InvalidOperationException)
        {
            writeRejected = true;
        }
        Require(writeRejected, "Consulta permitió continuar una caja.");
        log.Add("consultaReadOnly=OK");

        var cartUnits = 5;
        var cancelled = PosCartExitPolicy.TryExit(new(3, 5), false, false, () => false, () => cartUnits = 0);
        Require(!cancelled && cartUnits == 5, "Cancelar salida alteró el carrito.");
        var discarded = PosCartExitPolicy.TryExit(new(3, 5), false, false, () => true, () => cartUnits = 0);
        Require(discarded && cartUnits == 0, "Salir y descartar no limpió el carrito.");
        log.Add("cartExitPolicy=OK");
    }

    private static CashService CreateService(
        IDbContextFactory<AppDbContext> factory,
        ICurrentSession session,
        IAuditService audit,
        IEmployeePermissionService permissions,
        IInstallationContext installation,
        TimeProvider clock) =>
        new(factory, session, audit, new NullNotificationService(), permissions, installation, clock);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SmokeContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        public AppDbContext CreateDbContext() => new(_options);
    }

    private sealed class SmokeSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class SmokeInstallation(bool allowsWrites) : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            allowsWrites ? InstallationMode.Principal : InstallationMode.Consulta,
            true,
            Guid.NewGuid(),
            1,
            "smoke",
            null);
        public bool AllowsOperationalWrites => allowsWrites;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!allowsWrites)
            {
                throw new InvalidOperationException("Modo Consulta: escritura rechazada por smoke.");
            }
        }
    }

    private sealed class SmokeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
