using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class AuditLogServiceTests
{
    [Fact]
    public async Task TextSearch_MatchesUsernameActionEntityDescriptionAndSystemLabel()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = new Usuario
        {
            NombreCompleto = "Jacqueline Empleada",
            NombreUsuario = "jacqueline",
            PasswordHash = "hash",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Usuarios.Add(user);
        await fixture.Context.SaveChangesAsync();

        var cash = new Bitacora
        {
            UsuarioId = user.UsuarioId,
            TipoAccion = TipoBitacora.CAJA_ABIERTA,
            Entidad = "cajas",
            Descripcion = "Apertura con diferencia de efectivo.",
            FechaAccion = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc)
        };
        var inventory = new Bitacora
        {
            UsuarioId = user.UsuarioId,
            TipoAccion = TipoBitacora.INVENTARIO_TRANSFERIDO,
            Entidad = "movimientos_inventario_ubicacion",
            Descripcion = "Transferencia de SIN_ASIGNAR a EXHIBICION.",
            FechaAccion = new DateTime(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc)
        };
        var system = new Bitacora
        {
            UsuarioId = null,
            TipoAccion = TipoBitacora.LOGIN,
            Entidad = "usuarios",
            Descripcion = "Inicio de sesión del proceso programado.",
            FechaAccion = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)
        };
        fixture.Context.Bitacora.AddRange(cash, inventory, system);
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(2, (await fixture.Service.GetEntriesAsync(
            new AuditLogFilter(TextoBusqueda: "JACQUELINE"))).Count);
        Assert.Equal(cash.BitacoraId, Assert.Single(await fixture.Service.GetEntriesAsync(
            new AuditLogFilter(TextoBusqueda: "caja_abierta"))).BitacoraId);
        Assert.Equal(inventory.BitacoraId, Assert.Single(await fixture.Service.GetEntriesAsync(
            new AuditLogFilter(TextoBusqueda: "INVENTARIO_UBICACION"))).BitacoraId);
        Assert.Equal(cash.BitacoraId, Assert.Single(await fixture.Service.GetEntriesAsync(
            new AuditLogFilter(TextoBusqueda: "diferencia de EFECTIVO"))).BitacoraId);
        Assert.Equal(system.BitacoraId, Assert.Single(await fixture.Service.GetEntriesAsync(
            new AuditLogFilter(TextoBusqueda: "sist"))).BitacoraId);
    }

    [Fact]
    public async Task TextSearch_ComposesWithStructuredFiltersAndTreatsWildcardsLiterally()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entry = new Bitacora
        {
            UsuarioId = fixture.UserId,
            TipoAccion = TipoBitacora.CAJA_ABIERTA,
            Entidad = "cajas",
            Descripcion = "Aporte del 100% confirmado.",
            FechaAccion = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc)
        };
        fixture.Context.Bitacora.Add(entry);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.GetEntriesAsync(new AuditLogFilter(
            FechaDesde: new DateOnly(2026, 10, 12),
            FechaHasta: new DateOnly(2026, 10, 12),
            UsuarioId: fixture.UserId,
            TipoAccion: TipoBitacora.CAJA_ABIERTA,
            TextoBusqueda: "100%"));

        Assert.Equal(entry.BitacoraId, Assert.Single(result).BitacoraId);
    }

    [Fact]
    public async Task TextSearch_IsAppliedBeforeTheLatestThousandEntryLimit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entries = Enumerable.Range(0, 1000)
            .Select(index => new Bitacora
            {
                UsuarioId = fixture.UserId,
                TipoAccion = TipoBitacora.LOGIN,
                Entidad = "usuarios",
                Descripcion = "Registro reciente sin coincidencia.",
                FechaAccion = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index)
            })
            .Append(new Bitacora
            {
                UsuarioId = fixture.UserId,
                TipoAccion = TipoBitacora.LOGIN,
                Entidad = "usuarios",
                Descripcion = "Coincidencia objetivo anterior.",
                FechaAccion = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            })
            .ToArray();
        fixture.Context.Bitacora.AddRange(entries);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.GetEntriesAsync(new AuditLogFilter(TextoBusqueda: "objetivo"));

        Assert.Equal(entries[^1].BitacoraId, Assert.Single(result).BitacoraId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, AppDbContext context, AuditLogService service, int userId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            UserId = userId;
        }

        public AppDbContext Context { get; }
        public AuditLogService Service { get; }
        public int UserId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = new Usuario
            {
                NombreCompleto = "Administrador de prueba",
                NombreUsuario = "admin-bitacora",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(admin);
            await context.SaveChangesAsync();

            var session = new TestSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            var service = new AuditLogService(context, session);
            return new Fixture(connection, context, service, admin.UsuarioId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
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
}
