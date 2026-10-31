using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class EmployeeStatisticsTests
{
    [Fact]
    public void CombinedSummary_KeepsActivitySalesAndPaymentsSeparate()
    {
        var activity = new EmployeeActivitySummary(
            7, "empleada", "Empleada", RolUsuario.EMPLEADO, true,
            Utc(2026, 1, 1), Utc(2026, 1, 10), 4);
        var sales = new EmployeeSalesSummary(7, 3, 15000, 5000, Utc(2026, 1, 9));
        var payments = new EmployeePaymentSummary(7, 8000, 2, Utc(2026, 1, 8), 1, 3);

        var summary = EmployeeStatisticsSummary.Combine(activity, sales, payments);

        Assert.Equal(4, summary.ActivityDays);
        Assert.Equal(3, summary.SaleCount);
        Assert.Equal(15000, summary.TotalSoldCentavos);
        Assert.Equal(5000, summary.AverageTicketCentavos);
        Assert.Equal(2, summary.PaymentCount);
        Assert.Equal(8000, summary.TotalPaidCentavos);
        Assert.DoesNotContain(
            typeof(EmployeeStatisticsSummary).GetProperties(),
            property => property.Name.Contains("Profit", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Productivity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PaymentTrend_ExcludesCancelledAndGroupsDailyWeeklyBiweeklyAndMonthly()
    {
        await using var fixture = await StatisticsFixture.CreateAsync("UTC");
        fixture.AddPayment(2, 1000, Utc(2026, 1, 5));
        fixture.AddPayment(2, 2000, Utc(2026, 1, 6));
        fixture.AddPayment(2, 3000, Utc(2026, 1, 20));
        fixture.AddPayment(2, 99000, Utc(2026, 1, 6), EmployeePaymentStatus.Cancelled);
        await fixture.Db.SaveChangesAsync();
        var query = fixture.Query();
        var filter = new EmployeePaymentFilter(2, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));

        var daily = await query.GetPaymentTrendAsync(2, filter, EmployeePaymentGrouping.Day);
        var weekly = await query.GetPaymentTrendAsync(2, filter, EmployeePaymentGrouping.Week);
        var biweekly = await query.GetPaymentTrendAsync(2, filter, EmployeePaymentGrouping.Biweekly);
        var monthly = await query.GetPaymentTrendAsync(2, filter, EmployeePaymentGrouping.Month);

        Assert.Equal(3, daily.Count);
        Assert.Equal(6000, daily.Sum(point => point.TotalPaidCentavos));
        Assert.Equal(2, weekly.Count);
        Assert.Equal([3000L, 3000L], weekly.Select(point => point.TotalPaidCentavos).ToArray());
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 16)],
            biweekly.Select(point => point.LocalPeriodStart).ToArray());
        Assert.Equal([3000L, 3000L], biweekly.Select(point => point.TotalPaidCentavos).ToArray());
        var month = Assert.Single(monthly);
        Assert.Equal(new DateOnly(2026, 1, 1), month.LocalPeriodStart);
        Assert.Equal(3, month.PaymentCount);
        Assert.Equal(6000, month.TotalPaidCentavos);
    }

    [Fact]
    public async Task EmptyPeriodAndInactiveEmployeeHistoryRemainQueryable()
    {
        await using var fixture = await StatisticsFixture.CreateAsync("UTC");
        fixture.AddPayment(2, 4500, Utc(2026, 1, 15));
        await fixture.Db.SaveChangesAsync();
        var query = fixture.Query();

        var history = await query.GetEmployeeHistoryAsync(2);
        var empty = await query.GetPaymentTrendAsync(
            2,
            new EmployeePaymentFilter(2, new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 1)),
            EmployeePaymentGrouping.Month);

        Assert.False(fixture.Db.Usuarios.Single(user => user.UsuarioId == 2).Activo);
        Assert.Single(history);
        Assert.Empty(empty);
    }

    [Fact]
    public async Task PaymentTrend_UsesConfiguredBusinessTimezoneAtMonthBoundary()
    {
        await using var fixture = await StatisticsFixture.CreateAsync("Central America Standard Time");
        fixture.AddPayment(2, 2500, Utc(2026, 2, 1, 2)); // 31/01 20:00 in El Salvador.
        await fixture.Db.SaveChangesAsync();

        var trend = await fixture.Query().GetPaymentTrendAsync(
            2,
            new EmployeePaymentFilter(2, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)),
            EmployeePaymentGrouping.Day);

        var point = Assert.Single(trend);
        Assert.Equal(new DateOnly(2026, 1, 31), point.LocalPeriodStart);
        Assert.Equal(2500, point.TotalPaidCentavos);
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0)
        => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private sealed class StatisticsFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly AppDbContext _db;

        private StatisticsFixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            _db = db;
        }

        public AppDbContext Db => _db;

        public static async Task<StatisticsFixture> CreateAsync(string timeZoneId)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Usuarios.AddRange(
                User(1, "admin", "Principal", RolUsuario.ADMIN, true),
                User(2, "inactiva", "Empleada inactiva", RolUsuario.EMPLEADO, false));
            db.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = "negocio.zona_horaria",
                Valor = timeZoneId,
                FechaModificacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return new StatisticsFixture(connection, db);
        }

        public IEmployeePaymentQueryService Query()
        {
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(1, "Principal", "admin", RolUsuario.ADMIN));
            return new EmployeePaymentQueryService(_db, session);
        }

        public void AddPayment(
            int userId,
            long amount,
            DateTime createdAtUtc,
            EmployeePaymentStatus status = EmployeePaymentStatus.Active)
        {
            _db.EmployeePayments.Add(new EmployeePayment
            {
                UserId = userId,
                AmountCentavos = amount,
                PaymentType = EmployeePaymentType.Weekly,
                RegisteredByUserId = 1,
                CreatedAtUtc = createdAtUtc,
                Status = status,
                CancelledByUserId = status == EmployeePaymentStatus.Cancelled ? 1 : null,
                CancelledAtUtc = status == EmployeePaymentStatus.Cancelled ? createdAtUtc.AddHours(1) : null,
                CancellationReason = status == EmployeePaymentStatus.Cancelled ? "Prueba" : null
            });
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Usuario User(
            int id,
            string userName,
            string fullName,
            RolUsuario role,
            bool active) => new()
        {
            UsuarioId = id,
            NombreUsuario = userName,
            NombreCompleto = fullName,
            PasswordHash = "test-hash",
            Rol = role,
            Activo = active,
            FechaCreacion = Utc(2026, 1, 1)
        };
    }
}
