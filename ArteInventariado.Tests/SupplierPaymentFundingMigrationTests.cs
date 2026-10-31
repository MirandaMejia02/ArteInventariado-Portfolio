using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ArteInventariado.Tests;

public sealed class SupplierPaymentFundingMigrationTests
{
    private const string PreviousMigration = "20260912184247_AddSubcategorySnapshotsBlock1D2B";
    private const string FundingMigration = "20260923022006_AddSupplierPaymentFundingOriginV115";

    [Fact]
    public async Task PreviousDatabase_GetsNullableFundingColumnsAndCashConcurrencyVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.SupplierFundingMigration.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "legacy.db");
        try
        {
            await using (var previous = new AppDbContext(CreateOptions(databasePath)))
            {
                await previous.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            await using (var migrated = new AppDbContext(CreateOptions(databasePath)))
            {
                await migrated.Database.MigrateAsync();
                Assert.Empty(migrated.Database.GetPendingMigrations());
            }

            Assert.Equal(1, await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM pragma_table_info('pagos_compra') WHERE name = 'operacion_lote_id' AND \"notnull\" = 0;"));
            Assert.Equal(1, await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM pragma_table_info('pagos_compra') WHERE name = 'origen_fondos' AND \"notnull\" = 0;"));
            Assert.Equal(1, await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM pragma_table_info('pagos_compra') WHERE name = 'responsable_origen' AND \"notnull\" = 0;"));
            Assert.Equal(1, await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM pragma_table_info('abonos_cuenta_por_pagar') WHERE name = 'operacion_lote_id' AND \"notnull\" = 0;"));
            Assert.Equal(1, await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM pragma_table_info('abonos_cuenta_por_pagar') WHERE name = 'origen_fondos' AND \"notnull\" = 0;"));
            Assert.Equal(1, await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM pragma_table_info('abonos_cuenta_por_pagar') WHERE name = 'responsable_origen' AND \"notnull\" = 0;"));
            Assert.Equal(1, await ReadScalarAsync(databasePath, $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{FundingMigration}';"));
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
