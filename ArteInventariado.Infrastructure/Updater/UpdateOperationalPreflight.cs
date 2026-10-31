using ArteInventariado.Application.Models;
using Microsoft.Data.Sqlite;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateOperationalPreflight
{
    public async Task ValidateAsync(
        UpdateRuntimePaths paths,
        InstallationStatus station,
        CancellationToken cancellationToken = default)
    {
        if (!station.IsConfigured || !station.InstallationId.HasValue || !station.Mode.HasValue)
        {
            throw new InvalidOperationException("La estacion no esta lista para actualizar.");
        }

        if (!File.Exists(paths.StationSettingsPath))
        {
            throw new FileNotFoundException("No existe station-settings.json.", paths.StationSettingsPath);
        }

        if (!File.Exists(paths.DatabasePath))
        {
            throw new FileNotFoundException("No existe la base de datos activa.", paths.DatabasePath);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 15
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var integrity = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La base activa no supera PRAGMA quick_check.");
        }

        EnsureDirectoryAccessible(paths.BusinessAssetsDirectory);
        EnsureDirectoryAccessible(paths.ManagedAssetsDirectory);
    }

    private static void EnsureDirectoryAccessible(string path)
    {
        Directory.CreateDirectory(path);
        _ = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).Take(1).ToList();
    }
}
