using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class StationConfigurationTests
{
    [Fact]
    public void MissingConfiguration_IsCreatedAsPendingAndFailsClosed()
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();

        Assert.Equal(StationConfigurationState.RequiresSetup, store.Load().Status.State);

        var initialized = store.EnsureInitialized();
        var context = new InstallationContext(store);

        Assert.True(File.Exists(files.ConfigurationPath));
        Assert.Equal(StationConfigurationState.RequiresSetup, initialized.Status.State);
        Assert.NotEqual(Guid.Empty, initialized.Status.InstallationId);
        Assert.False(context.AllowsOperationalWrites);
        Assert.Throws<InstallationWriteDeniedException>(
            () => context.EnsureOperationalWriteAllowed("Prueba"));
        Assert.Empty(Directory.EnumerateFiles(files.DirectoryPath, "*.tmp"));
    }

    [Theory]
    [InlineData(InstallationMode.Principal, true)]
    [InlineData(InstallationMode.Consulta, false)]
    public void ConfiguredMode_PersistsAcrossStoreAndContextRestarts(
        InstallationMode mode,
        bool allowsWrites)
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();
        store.EnsureInitialized();
        var originalId = store.Load().Status.InstallationId;

        store.SaveConfigured(mode);

        var restartedStore = files.CreateStore();
        var restartedContext = new InstallationContext(restartedStore);
        Assert.Equal(StationConfigurationState.Ready, restartedContext.Status.State);
        Assert.Equal(mode, restartedContext.Status.Mode);
        Assert.Equal(originalId, restartedContext.Status.InstallationId);
        Assert.Equal(allowsWrites, restartedContext.AllowsOperationalWrites);
        Assert.Equal(LocalStationConfigurationStore.CurrentSchemaVersion, restartedContext.Status.SchemaVersion);
        Assert.Empty(Directory.EnumerateFiles(files.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void CorruptConfiguration_IsNotTreatedAsPrincipalAndCanBeRecoveredExplicitly()
    {
        using var files = new TemporaryStationFiles();
        Directory.CreateDirectory(files.DirectoryPath);
        File.WriteAllText(files.ConfigurationPath, "{ definitely-not-json");
        var store = files.CreateStore();

        var corrupt = store.Load();
        Assert.Equal(StationConfigurationState.Corrupt, corrupt.Status.State);
        Assert.False(new InstallationContext(store).AllowsOperationalWrites);

        store.SaveConfigured(InstallationMode.Consulta);

        Assert.Equal(InstallationMode.Consulta, files.CreateStore().Load().Status.Mode);
        Assert.Single(Directory.EnumerateFiles(files.DirectoryPath, "*.corrupt-*.bak"));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("99")]
    [InlineData("")]
    public void InvalidMode_FailsClosed(string mode)
    {
        using var files = new TemporaryStationFiles();
        Directory.CreateDirectory(files.DirectoryPath);
        File.WriteAllText(files.ConfigurationPath, JsonSerializer.Serialize(new
        {
            schemaVersion = LocalStationConfigurationStore.CurrentSchemaVersion,
            installationId = Guid.NewGuid(),
            installationMode = mode,
            isFirstRunCompleted = true
        }));

        var context = new InstallationContext(files.CreateStore());

        Assert.Equal(StationConfigurationState.Invalid, context.Status.State);
        Assert.False(context.AllowsOperationalWrites);
    }

    [Fact]
    public void LocalIdentity_DoesNotChangeWhenOperationalDatabaseIsReplaced()
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();
        store.SaveConfigured(InstallationMode.Consulta);
        var before = store.Load().Status;

        var databasePath = Path.Combine(files.DirectoryPath, "arteinventariado.db");
        File.WriteAllText(databasePath, "principal database copy v1");
        File.WriteAllText(databasePath, "restored principal database copy v2");

        var after = files.CreateStore().Load().Status;
        Assert.Equal(before.InstallationId, after.InstallationId);
        Assert.Equal(InstallationMode.Consulta, after.Mode);
        Assert.False(after.AllowsOperationalWrites);
    }

    [Fact]
    public void VersionOneStationSettings_UpgradeInPlaceWithoutChangingIdentityOrMode()
    {
        using var files = new TemporaryStationFiles();
        Directory.CreateDirectory(files.DirectoryPath);
        var installationId = Guid.NewGuid();
        File.WriteAllText(files.ConfigurationPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            installationId,
            installationMode = InstallationMode.Consulta.ToString(),
            isFirstRunCompleted = true
        }));

        var result = files.CreateStore().Load();

        Assert.Equal(StationConfigurationState.Ready, result.Status.State);
        Assert.Equal(LocalStationConfigurationStore.CurrentSchemaVersion, result.Status.SchemaVersion);
        Assert.Equal(installationId, result.Status.InstallationId);
        Assert.Equal(InstallationMode.Consulta, result.Status.Mode);
        using var json = JsonDocument.Parse(File.ReadAllText(files.ConfigurationPath));
        Assert.Equal(LocalStationConfigurationStore.CurrentSchemaVersion,
            json.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void VersionTwoStationSettings_UpgradePreservesBackupAndAddsLocalReportSlot()
    {
        using var files = new TemporaryStationFiles();
        Directory.CreateDirectory(files.DirectoryPath);
        var installationId = Guid.NewGuid();
        var document = new StationConfigurationDocument(
            2,
            installationId,
            InstallationMode.Principal.ToString(),
            true,
            new BackupStationSettingsDocument(
                true,
                Path.Combine(files.DirectoryPath, "backups"),
                "Daily",
                5,
                null,
                null));
        File.WriteAllText(
            files.ConfigurationPath,
            JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

        var result = files.CreateStore().Load();

        Assert.Equal(StationConfigurationState.Ready, result.Status.State);
        Assert.Equal(LocalStationConfigurationStore.CurrentSchemaVersion, result.Status.SchemaVersion);
        Assert.Equal(installationId, result.Status.InstallationId);
        Assert.Equal(InstallationMode.Principal, result.Status.Mode);
        Assert.NotNull(result.Document!.Backup);
        Assert.Null(result.Document.Reports);
    }

    [Fact]
    public async Task LocalReportDirectory_RemainsOnStationAndAllowsAdminInConsultaMode()
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();
        store.EnsureInitialized();
        store.SaveConfigured(InstallationMode.Consulta);
        var reportsDirectory = Path.Combine(files.DirectoryPath, "reports-station");
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(1, "Administrador", "admin", RolUsuario.ADMIN));
        var service = new StationSettingsService(
            store,
            session,
            new InstallationContext(store),
            new StationEventLog(Path.Combine(files.DirectoryPath, "station-audit.log")));

        await service.UpdateReportSettingsAsync(new UpdateLocalReportSettingsRequest(reportsDirectory));
        File.WriteAllText(Path.Combine(files.DirectoryPath, "restored-business.db"), "replacement database");

        var afterReplacement = await service.GetReportSettingsAsync();
        var reportPaths = new ReportPathProvider(service);
        var quotationDirectory = await reportPaths.GetDirectoryAsync("Cotizaciones");
        Assert.Equal(Path.GetFullPath(reportsDirectory), afterReplacement.ReportsDirectory);
        Assert.True(Directory.Exists(reportsDirectory));
        Assert.Equal(Path.Combine(Path.GetFullPath(reportsDirectory), "Cotizaciones"), quotationDirectory);
        Assert.Equal(InstallationMode.Consulta, new InstallationContext(files.CreateStore()).Status.Mode);
    }

    [Fact]
    public async Task LocalReportDirectory_RejectsEmployeeChanges()
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();
        store.EnsureInitialized();
        store.SaveConfigured(InstallationMode.Principal);
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(2, "Empleado", "empleado", RolUsuario.EMPLEADO));
        var service = new StationSettingsService(
            store,
            session,
            new InstallationContext(store),
            new StationEventLog(Path.Combine(files.DirectoryPath, "station-audit.log")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateReportSettingsAsync(new UpdateLocalReportSettingsRequest(
                Path.Combine(files.DirectoryPath, "reports"))));

        Assert.Contains("ADMIN", error.Message);
    }

    [Fact]
    public async Task VersionFourStationSettings_UpgradeAddsDisabledPeripheralDefaults()
    {
        using var files = new TemporaryStationFiles();
        Directory.CreateDirectory(files.DirectoryPath);
        var installationId = Guid.NewGuid();
        File.WriteAllText(files.ConfigurationPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            installationId,
            installationMode = InstallationMode.Principal.ToString(),
            isFirstRunCompleted = true,
            pos = new { quickPosEnabled = true }
        }));

        var store = files.CreateStore();
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(1, "Administrador", "admin", RolUsuario.ADMIN));
        var service = new StationSettingsService(store, session, new InstallationContext(store),
            new StationEventLog(Path.Combine(files.DirectoryPath, "station-audit.log")));

        var peripherals = await service.GetPeripheralSettingsAsync();

        Assert.False(peripherals.TicketsEnabled);
        Assert.False(peripherals.LabelsEnabled);
        Assert.False(peripherals.CashDrawerEnabled);
        Assert.Equal(LocalStationConfigurationStore.CurrentSchemaVersion, store.Load().Status.SchemaVersion);
        Assert.True((await service.GetPosSettingsAsync()).QuickPosEnabled);
    }

    [Fact]
    public async Task PeripheralSettings_AreLocalAndEmployeeCannotModifyThem()
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();
        store.EnsureInitialized();
        store.SaveConfigured(InstallationMode.Principal);
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(1, "Administrador", "admin", RolUsuario.ADMIN));
        var service = new StationSettingsService(store, session, new InstallationContext(store),
            new StationEventLog(Path.Combine(files.DirectoryPath, "station-audit.log")));

        await service.UpdatePeripheralSettingsAsync(new UpdateLocalPeripheralSettingsRequest(
            true, "Caja-1", true, "Etiquetas-1", true, "Gaveta-1"));
        var saved = await service.GetPeripheralSettingsAsync();
        Assert.True(saved.TicketsEnabled);
        Assert.Equal("Caja-1", saved.ReceiptPrinterLogicalName);

        session.IniciarSesion(new UsuarioSesion(2, "Empleado", "empleado", RolUsuario.EMPLEADO));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePeripheralSettingsAsync(
            new UpdateLocalPeripheralSettingsRequest(false, null, false, null, false, null)));
    }

    [Fact]
    public async Task QuickPos_IsDisabledByDefaultAndPersistsPerStation()
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();
        store.EnsureInitialized();
        store.SaveConfigured(InstallationMode.Consulta);
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(1, "Administrador", "admin", RolUsuario.ADMIN));
        var service = new StationSettingsService(
            store,
            session,
            new InstallationContext(store),
            new StationEventLog(Path.Combine(files.DirectoryPath, "station-audit.log")));

        var initial = await service.GetPosSettingsAsync();
        Assert.False(initial.QuickPosEnabled);

        await service.UpdatePosSettingsAsync(new UpdateLocalPosSettingsRequest(true));

        var restartedStore = files.CreateStore();
        var restartedService = new StationSettingsService(
            restartedStore,
            session,
            new InstallationContext(restartedStore),
            new StationEventLog(Path.Combine(files.DirectoryPath, "station-audit.log")));
        Assert.True((await restartedService.GetPosSettingsAsync()).QuickPosEnabled);
        Assert.Equal(InstallationMode.Consulta, restartedStore.Load().Status.Mode);
        Assert.True(restartedStore.Load().Document!.Pos!.QuickPosEnabled);
    }

    [Fact]
    public async Task QuickPos_LocalSetting_RejectsEmployeeChanges()
    {
        using var files = new TemporaryStationFiles();
        var store = files.CreateStore();
        store.EnsureInitialized();
        store.SaveConfigured(InstallationMode.Principal);
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(2, "Empleado", "empleado", RolUsuario.EMPLEADO));
        var service = new StationSettingsService(
            store,
            session,
            new InstallationContext(store),
            new StationEventLog(Path.Combine(files.DirectoryPath, "station-audit.log")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdatePosSettingsAsync(new UpdateLocalPosSettingsRequest(true)));

        Assert.Contains("ADMIN", error.Message);
        Assert.False((await service.GetPosSettingsAsync()).QuickPosEnabled);
    }

    [Fact]
    public void PasswordPolicyAndProtectedAdministrator_AreSharedAndStable()
    {
        var shortPassword = Assert.Throws<ArgumentException>(
            () => PasswordPolicy.EnsureValid("1234567"));

        Assert.Contains(PasswordPolicy.MinimumLength.ToString(), shortPassword.Message);
        PasswordPolicy.EnsureValid("12345678");
        Assert.True(ProtectedAdministrator.IsUserName(" ADMIN "));
        Assert.False(ProtectedAdministrator.IsUserName("admin2"));
        Assert.True(ProtectedAdministrator.IsProtectedAdministrator(new UsuarioSesion(
            1,
            "Administrador principal",
            "admin",
            RolUsuario.ADMIN)));
        Assert.False(ProtectedAdministrator.IsProtectedAdministrator(new UsuarioSesion(
            2,
            "Administrador delegado",
            "delegated-admin",
            RolUsuario.ADMIN)));
        Assert.False(ProtectedAdministrator.IsProtectedAdministrator(new UsuarioSesion(
            3,
            "Empleado",
            "admin",
            RolUsuario.EMPLEADO)));
    }

    [Fact]
    public async Task DatabaseUpgrade_PreservesExistingAdminCredentialAndDoesNotCreateAnotherPrincipalAdmin()
    {
        var existingPassword = TestCredentials.Password("upgrade-admin");
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado.AdminUpgrade.{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using (var initial = new AppDbContext(options))
            {
                await DatabaseInitializer.InicializarAsync(initial);
                var admin = new Usuario
                {
                    NombreCompleto = "Administrador principal",
                    NombreUsuario = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(existingPassword),
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                };
                initial.Usuarios.Add(admin);
                await initial.SaveChangesAsync();
            }

            await using (var upgraded = new AppDbContext(options))
            {
                await DatabaseInitializer.InicializarAsync(upgraded);
                var admins = await upgraded.Usuarios
                    .Where(user => user.NombreUsuario == "admin")
                    .ToListAsync();

                var admin = Assert.Single(admins);
                Assert.True(BCrypt.Net.BCrypt.Verify(existingPassword, admin.PasswordHash));
                Assert.Equal(RolUsuario.ADMIN, admin.Rol);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private sealed class TemporaryStationFiles : IDisposable
    {
        public TemporaryStationFiles()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"ArteInventariado.Tests.{Guid.NewGuid():N}");
            ConfigurationPath = Path.Combine(DirectoryPath, "station-settings.json");
        }

        public string DirectoryPath { get; }
        public string ConfigurationPath { get; }

        public LocalStationConfigurationStore CreateStore() => new(ConfigurationPath);

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}

public sealed class InstallationManagementTests
{
    [Fact]
    public async Task NewAndExistingInstallations_AreDistinguishedByDatabaseData()
    {
        await using var fixture = await ManagementFixture.CreateAsync(existingBusiness: false, "admin");
        Assert.Equal(
            InstallationSetupKind.NewInstallation,
            (await fixture.Service.GetSetupProfileAsync()).Kind);

        await fixture.AddExistingUserAsync();

        Assert.Equal(
            InstallationSetupKind.ExistingInstallationUpgrade,
            (await fixture.Service.GetSetupProfileAsync()).Kind);
    }

    [Theory]
    [InlineData("delegated-admin", RolUsuario.ADMIN)]
    [InlineData("employee", RolUsuario.EMPLEADO)]
    public async Task OnlyProtectedAdminCanCompleteSetup(string userName, RolUsuario role)
    {
        await using var fixture = await ManagementFixture.CreateAsync(existingBusiness: true, userName, role);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CompleteSetupAsync(new(
                InstallationMode.Principal,
                ManagementFixture.Password,
                true)));

        Assert.Contains("administrador principal", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.Context.AllowsOperationalWrites);
    }

    [Fact]
    public async Task ProtectedAdminCanCompleteSetupAfterReauthenticationWithoutPersistingPassword()
    {
        await using var fixture = await ManagementFixture.CreateAsync(existingBusiness: true, "admin");

        var result = await fixture.Service.CompleteSetupAsync(new(
            InstallationMode.Principal,
            ManagementFixture.Password,
            true));

        Assert.True(result.RestartRequired);
        var json = File.ReadAllText(fixture.ConfigurationPath);
        Assert.DoesNotContain(ManagementFixture.Password, json, StringComparison.Ordinal);
        Assert.Equal(InstallationMode.Principal, fixture.ReloadContext().Status.Mode);
        Assert.Single(fixture.Audit.Entries);
        Assert.Contains("INSTALLATION_SETUP_COMPLETED", File.ReadAllText(fixture.EventLogPath));
    }

    [Fact]
    public async Task ModeChangeRequiresPasswordConfirmationAndPersistsOnlyLocally()
    {
        await using var fixture = await ManagementFixture.CreateAsync(existingBusiness: true, "admin", initialMode: InstallationMode.Principal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ChangeModeAsync(new(
            InstallationMode.Consulta,
            "incorrect-password",
            true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ChangeModeAsync(new(
            InstallationMode.Consulta,
            ManagementFixture.Password,
            false)));

        var result = await fixture.Service.ChangeModeAsync(new(
            InstallationMode.Consulta,
            ManagementFixture.Password,
            true));

        Assert.True(result.RestartRequired);
        Assert.Equal(InstallationMode.Consulta, fixture.ReloadContext().Status.Mode);
        Assert.Contains("INSTALLATION_MODE_CHANGED", File.ReadAllText(fixture.EventLogPath));
        Assert.DoesNotContain(ManagementFixture.Password, File.ReadAllText(fixture.EventLogPath));
    }

    [Fact]
    public async Task DelegatedAdminCannotChangeConfiguredStationModeDirectly()
    {
        await using var fixture = await ManagementFixture.CreateAsync(
            existingBusiness: true,
            "delegated-admin",
            RolUsuario.ADMIN,
            InstallationMode.Principal);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ChangeModeAsync(new(
                InstallationMode.Consulta,
                ManagementFixture.Password,
                true)));

        Assert.Contains("administrador principal", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InstallationMode.Principal, fixture.ReloadContext().Status.Mode);
    }

    private sealed class ManagementFixture : IAsyncDisposable
    {
        public static readonly string Password = TestCredentials.Password("station");
        private readonly string _directory;
        private readonly SqliteConnection _connection;
        private readonly LocalStationConfigurationStore _store;

        private ManagementFixture(
            string directory,
            SqliteConnection connection,
            AppDbContext db,
            LocalStationConfigurationStore store,
            InstallationContext context,
            CollectingAuditService audit,
            InstallationManagementService service,
            string eventLogPath)
        {
            _directory = directory;
            _connection = connection;
            Db = db;
            _store = store;
            Context = context;
            Audit = audit;
            Service = service;
            EventLogPath = eventLogPath;
            ConfigurationPath = store.ConfigurationPath;
        }

        public AppDbContext Db { get; }
        public InstallationContext Context { get; }
        public CollectingAuditService Audit { get; }
        public InstallationManagementService Service { get; }
        public string EventLogPath { get; }
        public string ConfigurationPath { get; }

        public static async Task<ManagementFixture> CreateAsync(
            bool existingBusiness,
            string sessionUserName,
            RolUsuario role = RolUsuario.ADMIN,
            InstallationMode? initialMode = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"ArteInventariado.ManagementTests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var store = new LocalStationConfigurationStore(Path.Combine(directory, "station-settings.json"));
            store.EnsureInitialized();
            if (initialMode.HasValue)
            {
                store.SaveConfigured(initialMode.Value);
            }

            var context = new InstallationContext(store);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var protectedAdmin = new Usuario
            {
                NombreCompleto = "Administrador principal",
                NombreUsuario = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            db.Usuarios.Add(protectedAdmin);
            await db.SaveChangesAsync();

            Usuario sessionUser = protectedAdmin;
            if (!ProtectedAdministrator.IsUserName(sessionUserName))
            {
                sessionUser = new Usuario
                {
                    NombreCompleto = "Usuario de prueba",
                    NombreUsuario = sessionUserName,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                    Rol = role,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                };
                db.Usuarios.Add(sessionUser);
                await db.SaveChangesAsync();
            }

            if (existingBusiness && db.Usuarios.Count() == 1)
            {
                db.Usuarios.Add(new Usuario
                {
                    NombreCompleto = "Empleado existente",
                    NombreUsuario = "existing-employee",
                    PasswordHash = "hash",
                    Rol = RolUsuario.EMPLEADO,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var session = new TestSession(new UsuarioSesion(
                sessionUser.UsuarioId,
                sessionUser.NombreCompleto,
                sessionUser.NombreUsuario,
                sessionUser.Rol));
            var audit = new CollectingAuditService();
            var eventLogPath = Path.Combine(directory, "station-audit.log");
            var service = new InstallationManagementService(
                store,
                context,
                new TestContextFactory(options),
                session,
                audit,
                new StationEventLog(eventLogPath));
            return new ManagementFixture(directory, connection, db, store, context, audit, service, eventLogPath);
        }

        public async Task AddExistingUserAsync()
        {
            Db.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Segundo usuario",
                NombreUsuario = $"employee-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.EMPLEADO,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            });
            await Db.SaveChangesAsync();
        }

        public InstallationContext ReloadContext() => new(new LocalStationConfigurationStore(ConfigurationPath));

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
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

    public sealed class CollectingAuditService : IAuditService
    {
        public List<AuditEntryRequest> Entries { get; } = [];

        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            Entries.Add(request);
            return Task.CompletedTask;
        }
    }
}
