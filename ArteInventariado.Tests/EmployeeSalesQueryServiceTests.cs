using ArteInventariado.Application.Models;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class EmployeeSalesQueryServiceTests
{
    [Fact]
    public async Task EmployeeSummary_CalculatesCountTotalAverageAndDailyTrend()
    {
        await using var fixture = await SalesFixture.CreateAsync();

        var report = await fixture.Service(1, "admin").GetEmployeeSalesAsync(
            1,
            new EmployeeSalesPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 4)));

        Assert.Equal(3, report.Summary.SaleCount);
        Assert.Equal(6000, report.Summary.TotalSoldCentavos);
        Assert.Equal(2000, report.Summary.AverageTicketCentavos);
        Assert.Equal(new DateTime(2026, 1, 3, 9, 0, 0, DateTimeKind.Utc), report.Summary.LastSaleUtc);
        Assert.Equal(
            [
                new EmployeeSalesTrendPoint(new DateOnly(2026, 1, 1), 1, 1000),
                new EmployeeSalesTrendPoint(new DateOnly(2026, 1, 2), 1, 2000),
                new EmployeeSalesTrendPoint(new DateOnly(2026, 1, 3), 1, 3000)
            ],
            report.DailyTrend);
    }

    [Fact]
    public async Task PeriodBoundaries_UseLocalDatesAndExcludeOutsideSales()
    {
        await using var fixture = await SalesFixture.CreateAsync();

        var report = await fixture.Service(1, "admin").GetEmployeeSalesAsync(
            1,
            new EmployeeSalesPeriod(new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3)));

        Assert.Equal(1, report.Summary.SaleCount);
        Assert.Equal(2000, report.Summary.TotalSoldCentavos);
        Assert.Equal(new DateOnly(2026, 1, 2), Assert.Single(report.DailyTrend).LocalPeriodStart);
    }

    [Fact]
    public async Task Trend_SupportsWeekAndMonthGrouping()
    {
        await using var fixture = await SalesFixture.CreateAsync();
        var service = fixture.Service(1, "admin");
        var period = new EmployeeSalesPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));

        var weekly = await service.GetEmployeeSalesTrendAsync(1, period, EmployeeSalesGrouping.Week);
        var monthly = await service.GetEmployeeSalesTrendAsync(1, period, EmployeeSalesGrouping.Month);

        Assert.Single(weekly);
        Assert.Equal(new DateOnly(2025, 12, 29), weekly[0].LocalPeriodStart);
        Assert.Equal(3, weekly[0].SaleCount);
        Assert.Equal(6000, weekly[0].TotalSoldCentavos);
        Assert.Single(monthly);
        Assert.Equal(new DateOnly(2026, 1, 1), monthly[0].LocalPeriodStart);
        Assert.Equal(3, monthly[0].SaleCount);
    }

    [Fact]
    public async Task EmptyPeriod_ReturnsZeroSummaryAndNoTrend()
    {
        await using var fixture = await SalesFixture.CreateAsync();

        var report = await fixture.Service(1, "admin").GetEmployeeSalesAsync(
            1,
            new EmployeeSalesPeriod(new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 1)));

        Assert.Equal(0, report.Summary.SaleCount);
        Assert.Equal(0, report.Summary.TotalSoldCentavos);
        Assert.Equal(0, report.Summary.AverageTicketCentavos);
        Assert.Null(report.Summary.LastSaleUtc);
        Assert.Empty(report.DailyTrend);
    }

    [Fact]
    public async Task Ranking_IncludesPrincipalAdminsAndInactiveUsers_AndOrdersByTotal()
    {
        await using var fixture = await SalesFixture.CreateAsync();

        var rows = await fixture.Service(1, "admin").GetSalesRankingAsync(
            new EmployeeSalesPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)));

        Assert.Equal([1, 2, 4], rows.Select(row => row.UserId).ToArray());
        Assert.Equal([1, 2, 3], rows.Select(row => row.Position).ToArray());
        Assert.Equal(6000, rows[0].TotalSoldCentavos);
        Assert.Equal(5000, rows[1].TotalSoldCentavos);
        Assert.False(rows[2].Active);
        Assert.Equal(3000, rows[2].TotalSoldCentavos);
    }

    [Fact]
    public async Task CancelledSalesAndUsersWithoutSales_AreNotCounted()
    {
        await using var fixture = await SalesFixture.CreateAsync();

        var empty = await fixture.Service(1, "admin").GetEmployeeSalesAsync(3);
        var ranking = await fixture.Service(1, "admin").GetSalesRankingAsync();

        Assert.Equal(0, empty.Summary.SaleCount);
        Assert.DoesNotContain(ranking, row => row.UserId == 3);
    }

    [Fact]
    public async Task Authorization_UsesEmployeeModuleContract()
    {
        await using var fixture = await SalesFixture.CreateAsync();

        await fixture.Service(1, "admin").GetSalesRankingAsync();
        await fixture.Service(2, "delegado").GetSalesRankingAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service(5, "sin-permiso").GetSalesRankingAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service(3, "empleado").GetSalesRankingAsync());
    }

    [Fact]
    public async Task InvalidPeriodAndGrouping_AreRejected()
    {
        await using var fixture = await SalesFixture.CreateAsync();
        var service = fixture.Service(1, "admin");

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetEmployeeSalesAsync(
            1,
            new EmployeeSalesPeriod(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetEmployeeSalesTrendAsync(
            1,
            null,
            (EmployeeSalesGrouping)999));
    }

    private sealed class SalesFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly AppDbContext _db;

        private SalesFixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            _db = db;
        }

        public static async Task<SalesFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Usuarios.AddRange(
                User(1, "admin", "Principal", RolUsuario.ADMIN),
                User(2, "delegado", "Delegado", RolUsuario.ADMIN),
                User(3, "empleado", "Empleado", RolUsuario.EMPLEADO),
                User(4, "inactivo", "Inactivo", RolUsuario.EMPLEADO, active: false),
                User(5, "sin-permiso", "Sin permiso", RolUsuario.ADMIN));
            db.UsuarioPermisos.Add(new UsuarioPermiso
            {
                UsuarioId = 2,
                Permiso = PermisoEmpleado.EMPLEADOS_VER,
                Habilitado = true
            });
            db.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = "negocio.zona_horaria",
                Valor = "UTC",
                FechaModificacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var cash = new Caja
            {
                FechaOperacion = new DateOnly(2026, 1, 1),
                FechaApertura = Utc(2026, 1, 1),
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.CERRADA,
                UsuarioAperturaId = 1,
                UsuarioCierreId = 1,
                FechaCierre = Utc(2026, 1, 31)
            };
            db.Cajas.Add(cash);
            await db.SaveChangesAsync();

            db.Ventas.AddRange(
                Sale(cash.CajaId, 1, "VTA-1", Utc(2026, 1, 1, 9), 1000),
                Sale(cash.CajaId, 1, "VTA-2", Utc(2026, 1, 2, 9), 2000),
                Sale(cash.CajaId, 1, "VTA-3", Utc(2026, 1, 3, 9), 3000),
                Sale(cash.CajaId, 2, "VTA-4", Utc(2026, 1, 4, 9), 5000),
                Sale(cash.CajaId, 4, "VTA-5", Utc(2026, 1, 5, 9), 3000),
                Sale(cash.CajaId, 3, "VTA-CANCELADA", Utc(2026, 1, 6, 9), 9000, EstadoVenta.ANULADA));
            await db.SaveChangesAsync();
            return new SalesFixture(connection, db);
        }

        public IEmployeeSalesQueryService Service(int userId, string userName)
        {
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                userId,
                userName,
                userName,
                userId == 3 ? RolUsuario.EMPLEADO : RolUsuario.ADMIN));
            return new EmployeeSalesQueryService(new Factory(_connection), session);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Usuario User(int id, string name, string fullName, RolUsuario role, bool active = true) => new()
        {
            UsuarioId = id,
            NombreUsuario = name,
            NombreCompleto = fullName,
            PasswordHash = "hash",
            Rol = role,
            Activo = active,
            FechaCreacion = Utc(2026, 1, 1)
        };

        private static Venta Sale(
            int cashId,
            int userId,
            string number,
            DateTime date,
            long total,
            EstadoVenta state = EstadoVenta.COMPLETADA) => new()
        {
            NumeroVenta = number,
            CajaId = cashId,
            UsuarioId = userId,
            UsuarioNombreSnapshot = userId.ToString(),
            ClienteNombreSnapshot = "Consumidor final",
            FechaVenta = date,
            SubtotalCentavos = total,
            DescuentoCentavos = 0,
            TotalCentavos = total,
            PagadoCentavos = total,
            CambioCentavos = 0,
            Estado = state
        };

        private static DateTime Utc(int year, int month, int day, int hour = 0)
            => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

        private sealed class Factory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
        {
            public AppDbContext CreateDbContext()
                => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(CreateDbContext());
        }
    }
}
