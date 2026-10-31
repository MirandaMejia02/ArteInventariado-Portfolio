using System.Text;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.CloudBackup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class CloudBackupProvisioningAuthorizationTests
{
    private const string Password = "Principal-Cloud-123!";

    [Fact]
    public async Task ProtectedPrincipalMustReauthenticateAndGrantIsSingleUse()
    {
        await using var fixture = await Fixture.CreateAsync("admin", RolUsuario.ADMIN);

        var grant = await fixture.Authorization.ReauthenticateAndCreateGrantAsync(
            Password,
            confirmationAccepted: true);

        Assert.NotEqual(string.Empty, grant.Proof);
        Assert.True(File.Exists(CloudBackupProvisioningHandoffPath(fixture.Paths)));

        var consumed = await fixture.Authorization.ConsumeHandoffAsync();

        Assert.Equal(grant, consumed);
        Assert.False(File.Exists(CloudBackupProvisioningHandoffPath(fixture.Paths)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Authorization.ConsumeHandoffAsync());
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Descripcion.Contains("aprovisionamiento cloud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DelegatedAdminAndEmployeeCannotOpenOrReauthenticateCloudProvisioning()
    {
        await using var delegated = await Fixture.CreateAsync("delegated-admin", RolUsuario.ADMIN);

        Assert.False(await delegated.Authorization.CanCurrentSessionOpenAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            delegated.Authorization.ReauthenticateAndCreateGrantAsync(Password, true));
        Assert.False(File.Exists(CloudBackupProvisioningHandoffPath(delegated.Paths)));

        await using var employee = await Fixture.CreateAsync("employee", RolUsuario.EMPLEADO);

        Assert.False(await employee.Authorization.CanCurrentSessionOpenAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            employee.Authorization.ReauthenticateAndCreateGrantAsync(Password, true));
        Assert.False(File.Exists(CloudBackupProvisioningHandoffPath(employee.Paths)));
    }

    [Fact]
    public async Task ProtectedPrincipalRejectsIncorrectPassword()
    {
        await using var fixture = await Fixture.CreateAsync("admin", RolUsuario.ADMIN);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Authorization.ReauthenticateAndCreateGrantAsync("wrong-password", true));

        Assert.False(File.Exists(CloudBackupProvisioningHandoffPath(fixture.Paths)));
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Descripcion.Contains("reautenticación", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DirectOrTamperedContinuationIsRejectedAndLoggedWithoutSecrets()
    {
        await using var fixture = await Fixture.CreateAsync("admin", RolUsuario.ADMIN);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Authorization.ConsumeHandoffAsync());

        var issued = new CloudBackupProvisioningAuthorizationGrant(
            fixture.PrincipalId,
            fixture.InstallationId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new string('a', 64));
        CloudBackupProvisioningHandoffStore.Save(fixture.Paths, issued);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Authorization.ConsumeHandoffAsync());

        var eventText = File.ReadAllText(fixture.EventLogPath);
        Assert.Contains("CLOUD_PROVISIONING_REJECTED", eventText, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, eventText, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, string.Join("\n", fixture.Audit.Entries.Select(entry => entry.Descripcion)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisioningServiceRechecksGrantBeforePreflight()
    {
        await using var fixture = await Fixture.CreateAsync("admin", RolUsuario.ADMIN);
        var grant = await fixture.Authorization.ReauthenticateAndCreateGrantAsync(Password, true);
        var consumed = await fixture.Authorization.ConsumeHandoffAsync();
        var service = new CloudBackupProvisioningService(
            fixture.Paths,
            (_, _, _) => Task.FromResult(new CloudBackupProvisioningIdentity("123", "arn:test", "AIDA_TEST")),
            allowSyntheticRoots: true,
            authorization: fixture.Authorization);

        var check = await service.ValidateAsync(
            new CloudBackupConfiguration(1, true, "portfolio-example-backups", "us-east-1", "v1"),
            new CloudBackupCredential("AKIA_TEST", "secret-test"),
            consumed);

        Assert.True(check.TargetValidated);
        Assert.Equal(grant, consumed);
    }

    private static string CloudBackupProvisioningHandoffPath(ArteInventariadoPathProvider paths) =>
        Path.Combine(paths.UserDataRoot, "CloudProvisioning", "authorization.json");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly SqliteConnection _connection;
        private readonly AppDbContext _db;

        private Fixture(
            string root,
            ArteInventariadoPathProvider paths,
            SqliteConnection connection,
            AppDbContext db,
            TestContextFactory factory,
            TestSession session,
            CollectingAuditService audit,
            CloudBackupProvisioningAuthorizationService authorization,
            Guid installationId,
            int principalId,
            string eventLogPath)
        {
            _root = root;
            Paths = paths;
            _connection = connection;
            _db = db;
            Factory = factory;
            Session = session;
            Audit = audit;
            Authorization = authorization;
            InstallationId = installationId;
            PrincipalId = principalId;
            EventLogPath = eventLogPath;
        }

        public ArteInventariadoPathProvider Paths { get; }
        public TestContextFactory Factory { get; }
        public TestSession Session { get; }
        public CollectingAuditService Audit { get; }
        public CloudBackupProvisioningAuthorizationService Authorization { get; }
        public Guid InstallationId { get; }
        public int PrincipalId { get; }
        public string EventLogPath { get; }

        public static async Task<Fixture> CreateAsync(string sessionUserName, RolUsuario sessionRole)
        {
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.CloudProvisioningAuth.{Guid.NewGuid():N}");
            var paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"),
                Path.Combine(root, "user"));
            foreach (var directory in new[] { paths.MachineInstallRoot, paths.MachineDataRoot, paths.DataRoot, paths.StationRoot })
            {
                Directory.CreateDirectory(directory);
            }

            var station = new LocalStationConfigurationStore(paths);
            station.EnsureInitialized();
            var installation = station.SaveConfigured(InstallationMode.Principal);

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var principal = new Usuario
            {
                NombreCompleto = "Administrador principal",
                NombreUsuario = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            db.Usuarios.Add(principal);
            await db.SaveChangesAsync();

            var sessionUser = principal;
            if (!string.Equals(sessionUserName, "admin", StringComparison.OrdinalIgnoreCase))
            {
                sessionUser = new Usuario
                {
                    NombreCompleto = "Usuario de prueba",
                    NombreUsuario = sessionUserName,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                    Rol = sessionRole,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                };
                db.Usuarios.Add(sessionUser);
                await db.SaveChangesAsync();
            }

            var session = new TestSession(new UsuarioSesion(
                sessionUser.UsuarioId,
                sessionUser.NombreCompleto,
                sessionUser.NombreUsuario,
                sessionUser.Rol));
            var audit = new CollectingAuditService();
            var factory = new TestContextFactory(options);
            var eventLogPath = Path.Combine(root, "cloud-provisioning-events.jsonl");
            var authorization = new CloudBackupProvisioningAuthorizationService(
                factory,
                paths,
                session,
                audit,
                new InstallationContext(station),
                new StationEventLog(eventLogPath),
                TimeProvider.System);
            return new Fixture(
                root,
                paths,
                connection,
                db,
                factory,
                session,
                audit,
                authorization,
                installation.InstallationId,
                principal.UsuarioId,
                eventLogPath);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class TestSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class CollectingAuditService : IAuditService
    {
        public List<AuditEntryRequest> Entries { get; } = [];

        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            Entries.Add(request);
            return Task.CompletedTask;
        }
    }
}
