using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class PrincipalProvisioningTests
{
    [Fact]
    public void SourceDoesNotContainBootstrapCredentialOrInitialAdminCreator()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsGeneratedOrArtifactPath(path))
            .ToArray();

        var removedIdentifier = string.Concat("Initial", "AdminPassword");
        foreach (var path in sourceFiles)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain(removedIdentifier, text, StringComparison.Ordinal);
        }

        var initializer = File.ReadAllText(
            Path.Combine(repositoryRoot, "ArteInventariado.Infrastructure", "Data", "DatabaseInitializer.cs"));
        Assert.DoesNotContain("HashPassword", initializer, StringComparison.Ordinal);
        Assert.DoesNotContain("Usuarios.Add", initializer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshDatabaseInitializerCreatesStructureButNoPrincipalOrPassword()
    {
        await using var fixture = await Fixture.CreateAsync();

        await using (var context = fixture.CreateContext())
        {
            await DatabaseInitializer.InicializarAsync(context);
            Assert.Empty(await context.Usuarios.AsNoTracking().ToListAsync());
        }

        Assert.True(await fixture.Service.IsRequiredAsync());
    }

    [Fact]
    public async Task OwnerProvisioningCreatesProtectedPrincipalWithHashedPassword()
    {
        await using var fixture = await Fixture.CreateAsync();
        var password = TestCredentials.Password("principal");

        await fixture.Service.ProvisionAsync(new(password, password));

        await using var context = fixture.CreateContext();
        var principal = await context.Usuarios.AsNoTracking().SingleAsync();
        Assert.Equal(ProtectedAdministrator.UserName, principal.NombreUsuario);
        Assert.Equal(RolUsuario.ADMIN, principal.Rol);
        Assert.True(principal.Activo);
        Assert.NotEqual(password, principal.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, principal.PasswordHash));
        Assert.False(await fixture.Service.IsRequiredAsync());
    }

    [Fact]
    public async Task PasswordNeverAppearsInAuditEntryOrSerializedLogData()
    {
        await using var fixture = await Fixture.CreateAsync();
        var password = TestCredentials.Password("audit");

        await fixture.Service.ProvisionAsync(new(password, password));

        var auditText = JsonSerializer.Serialize(fixture.Audit.Entries);
        Assert.DoesNotContain(password, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(password, string.Join('\n', fixture.Audit.Entries.Select(entry => entry.Descripcion)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingInstallationRetainsPrincipalHashAndRejectsReprovisioning()
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalPassword = TestCredentials.Password("existing");
        var originalHash = await fixture.AddUserAsync(
            ProtectedAdministrator.UserName,
            RolUsuario.ADMIN,
            true,
            originalPassword);

        Assert.False(await fixture.Service.IsRequiredAsync());
        var attemptPassword = TestCredentials.Password("attempt");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ProvisionAsync(new(
                attemptPassword,
                attemptPassword)));
        Assert.Contains("usuarios", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var context = fixture.CreateContext();
        var storedHash = await context.Usuarios
            .Where(user => user.NombreUsuario == ProtectedAdministrator.UserName)
            .Select(user => user.PasswordHash)
            .SingleAsync();
        Assert.Equal(originalHash, storedHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(originalPassword, storedHash));
    }

    [Fact]
    public async Task DatabaseUpgradeAndLegacyMigrationPreserveExistingPrincipal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var password = TestCredentials.Password("legacy");
        var hash = await fixture.AddUserAsync(
            ProtectedAdministrator.UserName,
            RolUsuario.ADMIN,
            true,
            password);

        await using (var context = fixture.CreateContext())
        {
            await DatabaseInitializer.InicializarAsync(context);
        }

        await using var verify = fixture.CreateContext();
        var stored = await verify.Usuarios
            .Where(user => user.NombreUsuario == ProtectedAdministrator.UserName)
            .Select(user => user.PasswordHash)
            .SingleAsync();
        Assert.Equal(hash, stored);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, stored));
    }

    [Fact]
    public async Task RestoredPrincipalIsRecognizedAndNeverReplacedByProvisioning()
    {
        await using var fixture = await Fixture.CreateAsync();
        var restoredPassword = TestCredentials.Password("restored");
        await fixture.AddUserAsync(
            ProtectedAdministrator.UserName,
            RolUsuario.ADMIN,
            true,
            restoredPassword);

        Assert.False(await fixture.Service.IsRequiredAsync());
        var replacementPassword = TestCredentials.Password("replacement");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ProvisionAsync(new(
                replacementPassword,
                replacementPassword)));

        await using var context = fixture.CreateContext();
        var restored = await context.Usuarios.AsNoTracking().SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify(restoredPassword, restored.PasswordHash));
    }

    [Fact]
    public async Task InterruptedFirstRunIsSafeAndCanBeRetried()
    {
        await using var fixture = await Fixture.CreateAsync();
        var validPassword = TestCredentials.Password("retry");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ProvisionAsync(new(validPassword, TestCredentials.Password("mismatch"))));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.ProvisionAsync(new(new string('x', PasswordPolicy.MinimumLength - 1), "")));

        await using (var afterFailure = fixture.CreateContext())
        {
            Assert.Empty(await afterFailure.Usuarios.AsNoTracking().ToListAsync());
        }

        await fixture.Service.ProvisionAsync(new(validPassword, validPassword));
        await using var verify = fixture.CreateContext();
        Assert.Single(await verify.Usuarios.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DelegatedAdminOrInactiveUserCannotBecomeProtectedPrincipal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync(
            "delegated-admin",
            RolUsuario.ADMIN,
            true,
            TestCredentials.Password("delegated"));

        Assert.False(await fixture.Service.IsRequiredAsync());
        var takeoverPassword = TestCredentials.Password("takeover");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ProvisionAsync(new(
                takeoverPassword,
                takeoverPassword)));
        Assert.Empty(await fixture.ReadPrincipalUsersAsync());

        await using var secondFixture = await Fixture.CreateAsync();
        await secondFixture.AddUserAsync(
            "inactive-user",
            RolUsuario.EMPLEADO,
            false,
            TestCredentials.Password("inactive"));
        Assert.False(await secondFixture.Service.IsRequiredAsync());
        var inactiveTakeoverPassword = TestCredentials.Password("inactive-takeover");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            secondFixture.Service.ProvisionAsync(new(
                inactiveTakeoverPassword,
                inactiveTakeoverPassword)));
        Assert.Empty(await secondFixture.ReadPrincipalUsersAsync());
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArteInventariado.sln")) &&
                Directory.Exists(Path.Combine(current.FullName, "ArteInventariado.Infrastructure")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio para el gate de secretos.");
    }

    private static bool IsGeneratedOrArtifactPath(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/artifacts/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        private Fixture(
            string root,
            SqliteConnection connection,
            DbContextOptions<AppDbContext> options,
            ArteInventariadoPathProvider paths,
            TestInstallationContext installation,
            CollectingAuditService audit,
            PrincipalProvisioningService service)
        {
            _root = root;
            _connection = connection;
            _options = options;
            Paths = paths;
            Installation = installation;
            Audit = audit;
            Service = service;
        }

        public ArteInventariadoPathProvider Paths { get; }
        public TestInstallationContext Installation { get; }
        public CollectingAuditService Audit { get; }
        public PrincipalProvisioningService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.PrincipalProvisioning.{Guid.NewGuid():N}");
            var paths = ArteInventariadoPathProvider.CreateLegacy(root);
            Directory.CreateDirectory(paths.DataRoot);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            await using (var context = new AppDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var installation = new TestInstallationContext(paths.StationSettingsPath);
            var audit = new CollectingAuditService();
            var service = new PrincipalProvisioningService(
                new TestContextFactory(options),
                paths,
                installation,
                audit);
            return new Fixture(root, connection, options, paths, installation, audit, service);
        }

        public AppDbContext CreateContext() => new(_options);

        public async Task<string> AddUserAsync(
            string userName,
            RolUsuario role,
            bool active,
            string password)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(password);
            await using var context = CreateContext();
            context.Usuarios.Add(new Usuario
            {
                NombreCompleto = userName,
                NombreUsuario = userName,
                PasswordHash = hash,
                Rol = role,
                Activo = active,
                FechaCreacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            return hash;
        }

        public async Task<List<Usuario>> ReadPrincipalUsersAsync()
        {
            await using var context = CreateContext();
            return await context.Usuarios
                .Where(user => user.NombreUsuario == ProtectedAdministrator.UserName)
                .AsNoTracking()
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup for the temporary test root.
            }
        }
    }

    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AppDbContext(options));
        }
    }

    private sealed class TestInstallationContext(string configurationPath) : IInstallationContext
    {
        public InstallationStatus Status => new(
            StationConfigurationState.RequiresSetup,
            null,
            false,
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            1,
            configurationPath,
            "provisioning test");

        public bool AllowsOperationalWrites => false;

        public void EnsureInitialized()
        {
        }

        public void EnsureOperationalWriteAllowed(string operationName)
        {
        }
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
