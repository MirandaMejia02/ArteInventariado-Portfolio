using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ArteInventariado.Tests;

public sealed class EmployeePaymentFundingMigrationTests
{
    private const string PreviousMigration = "20260923035939_AddExpenseFundingOriginV115";
    private const string FundingMigration = "20260923044029_AddEmployeePaymentFundingOriginV115";

    [Fact]
    public async Task Upgrade_PreservesHistoricalEmployeePaymentAndLeavesFundingUnknown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.EmployeePaymentFunding.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "legacy.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString())
                .Options;

            int userId;
            await using (var previous = new AppDbContext(options))
            {
                await previous.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                var admin = new Usuario
                {
                    NombreCompleto = "Admin histórico",
                    NombreUsuario = "historico-pagos-empleado",
                    PasswordHash = "fixture-hash",
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                };
                previous.Usuarios.Add(admin);
                await previous.SaveChangesAsync();
                userId = admin.UsuarioId;

                await previous.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO employee_payments (
                        usuario_id, amount_centavos, payment_type, period_start, period_end,
                        days_considered, note, registered_by_user_id, created_at_utc, status,
                        cancelled_by_user_id, cancelled_at_utc, cancellation_reason)
                    VALUES (
                        {userId}, 12500, 'Weekly', '2026-01-01', '2026-01-07',
                        5, 'Pago histórico', {userId}, '2026-01-08 12:30:00', 'Active',
                        NULL, NULL, NULL);
                    """);
            }

            await using (var upgraded = new AppDbContext(options))
            {
                await upgraded.Database.MigrateAsync();
                Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());

                var historical = await upgraded.EmployeePayments.AsNoTracking().SingleAsync();
                Assert.Equal(userId, historical.UserId);
                Assert.Equal(12500, historical.AmountCentavos);
                Assert.Equal("Pago histórico", historical.Note);
                Assert.Null(historical.CajaId);
                Assert.Null(historical.MetodoPago);
                Assert.Null(historical.OrigenFondos);
                Assert.Null(historical.ResponsableOrigen);
                Assert.Null(historical.Referencia);
                Assert.Null(historical.OperacionId);
                Assert.Equal("ok", await ScalarAsync(upgraded, "PRAGMA integrity_check;"));
            }

            var applied = await ReadScalarAsync(databasePath,
                $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{FundingMigration}';");
            Assert.Equal(1, applied);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath + "-wal")) File.Delete(databasePath + "-wal");
            if (File.Exists(databasePath + "-shm")) File.Delete(databasePath + "-shm");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string?> ScalarAsync(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static async Task<int> ReadScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
