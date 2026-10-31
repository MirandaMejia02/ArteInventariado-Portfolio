using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class SystemConfigServiceTests
{
    [Fact]
    public async Task GlobalBusinessProfileAndBitcoinSetting_ArePersistedWithoutStationPaths()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var admin = new Usuario
        {
            NombreCompleto = "Administrador",
            NombreUsuario = "admin",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        context.Usuarios.Add(admin);
        await context.SaveChangesAsync();

        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(
            admin.UsuarioId,
            admin.NombreCompleto,
            admin.NombreUsuario,
            admin.Rol));
        var service = new SystemConfigService(context, session, new AuditService(context));

        Assert.True(await service.IsBitcoinEnabledAsync());

        await service.UpdateBasicSettingsAsync(new UpdateSystemConfigRequest(
        [
            new(SystemConfigService.BusinessNameKey, "Tienda de ejemplo", null),
            new(SystemConfigService.BusinessAddressKey, "Ciudad de ejemplo", null),
            new(SystemConfigService.BusinessPhoneKey, "7000-0000", null),
            new(SystemConfigService.BusinessEmailKey, "ventas@example.test", null),
            new(SystemConfigService.AutomaticCashOpeningTimeKey, "08:30", null),
            new(SystemConfigService.BitcoinEnabledKey, "false", null)
        ]));

        var settings = (await service.GetBasicSettingsAsync())
            .ToDictionary(setting => setting.Clave, setting => setting.Valor);

        Assert.Equal("Tienda de ejemplo", settings[SystemConfigService.BusinessNameKey]);
        Assert.Equal("Ciudad de ejemplo", settings[SystemConfigService.BusinessAddressKey]);
        Assert.Equal("7000-0000", settings[SystemConfigService.BusinessPhoneKey]);
        Assert.Equal("ventas@example.test", settings[SystemConfigService.BusinessEmailKey]);
        Assert.Equal("08:30", settings[SystemConfigService.AutomaticCashOpeningTimeKey]);
        Assert.Equal("false", settings[SystemConfigService.BitcoinEnabledKey]);
        Assert.False(await service.IsBitcoinEnabledAsync());
        Assert.DoesNotContain(
            settings.Keys,
            key => key is SystemConfigService.ReportsFolderKey or SystemConfigService.BackupsFolderKey);
        Assert.Single(await context.Bitacora.Where(entry => entry.TipoAccion == TipoBitacora.CONFIGURACION_MODIFICADA).ToListAsync());
    }

    [Fact]
    public async Task GlobalBusinessProfile_RejectsBlankCommercialName()
    {
        await using var fixture = await ConfigFixture.CreateAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.UpdateBasicSettingsAsync(
            new UpdateSystemConfigRequest(
            [new(SystemConfigService.BusinessNameKey, "   ", null)])));

        Assert.Contains("nombre comercial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ConfigFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ConfigFixture(
            SqliteConnection connection,
            AppDbContext context,
            SystemConfigService service)
        {
            _connection = connection;
            Context = context;
            Service = service;
        }

        public AppDbContext Context { get; }
        public SystemConfigService Service { get; }

        public static async Task<ConfigFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var admin = new Usuario
            {
                NombreCompleto = "Administrador",
                NombreUsuario = "admin",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(admin);
            await context.SaveChangesAsync();
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            return new ConfigFixture(connection, context, new SystemConfigService(context, session, new AuditService(context)));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
