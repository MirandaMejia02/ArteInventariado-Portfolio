using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ArteInventariado.Tests;

public sealed class ExpenseFundingMigrationTests
{
    private const string PreviousMigration = "20260923022006_AddSupplierPaymentFundingOriginV115";
    private const string ExpenseMigration = "20260923035939_AddExpenseFundingOriginV115";

    [Fact]
    public async Task Upgrade_PreservesHistoricalExpenseAndLeavesFundingFieldsUnknown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.ExpenseFundingMigration.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "legacy.db");
        try
        {
            int userId;
            int cashId;
            await using (var previous = new AppDbContext(CreateOptions(databasePath)))
            {
                await previous.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                var user = new Usuario
                {
                    NombreCompleto = "Usuario histórico",
                    NombreUsuario = "historico-gastos",
                    PasswordHash = "not-used-in-this-test",
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                };
                previous.Usuarios.Add(user);
                await previous.SaveChangesAsync();
                var cash = new Caja
                {
                    FechaOperacion = new DateOnly(2026, 1, 1),
                    FechaApertura = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    MontoInicialCentavos = 5_000,
                    Estado = EstadoCaja.ABIERTA,
                    UsuarioAperturaId = user.UsuarioId
                };
                previous.Cajas.Add(cash);
                await previous.SaveChangesAsync();
                userId = user.UsuarioId;
                cashId = cash.CajaId;
                await previous.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO gastos (caja_id, usuario_id, categoria_gasto_id, concepto, monto_centavos,
                        fecha_gasto, observaciones, metodo_pago, referencia)
                    VALUES ({cashId}, {userId}, NULL, 'Gasto antes de v1.15', 750,
                        '2026-01-01 01:00:00', NULL, 'EFECTIVO', NULL);
                    """);
            }

            await using (var upgraded = new AppDbContext(CreateOptions(databasePath)))
            {
                await upgraded.Database.MigrateAsync();
                Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
                var historical = await upgraded.Gastos.AsNoTracking().SingleAsync();
                Assert.Equal(cashId, historical.CajaId);
                Assert.Equal(userId, historical.UsuarioId);
                Assert.Equal("Gasto antes de v1.15", historical.Concepto);
                Assert.Null(historical.OperacionId);
                Assert.Null(historical.OrigenFondos);
                Assert.Null(historical.ResponsableOrigen);
            }

            Assert.Equal(1, await ReadScalarAsync(databasePath,
                $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{ExpenseMigration}';"));
            Assert.Equal(1, await ReadScalarAsync(databasePath,
                "SELECT COUNT(*) FROM pragma_table_info('gastos') WHERE name = 'caja_id' AND \"notnull\" = 0;"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                Pooling = false
            }.ToString())
            .Options;

    private static async Task<int> ReadScalarAsync(string databasePath, string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
