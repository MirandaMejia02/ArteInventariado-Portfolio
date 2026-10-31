using Microsoft.Data.Sqlite;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class IncompatibleDatabaseException : InvalidOperationException
{
    public IncompatibleDatabaseException(string message) : base(message) { }
}

public static class SchemaCompatibilityGuard
{
    public static void EnsureCompatible(string databasePath, IReadOnlyList<string> currentMigrations)
    {
        if (!File.Exists(databasePath)) return;
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 15 };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
        if (Convert.ToInt32(exists.ExecuteScalar()) == 0) return;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        var applied = new List<string>();
        while (reader.Read()) applied.Add(reader.GetString(0));
        if (applied.Count > currentMigrations.Count || !applied.SequenceEqual(currentMigrations.Take(applied.Count), StringComparer.Ordinal))
            throw new IncompatibleDatabaseException("La base de datos fue actualizada por una version mas reciente de ArteInventariado.");
    }
}
