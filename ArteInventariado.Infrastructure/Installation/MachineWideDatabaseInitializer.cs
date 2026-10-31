using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Installation;

public sealed record MachineWideDatabaseInitializationResult(
    bool DatabaseCreated,
    bool PrincipalExists,
    Guid InstallationId,
    InstallationMode? StationMode,
    StationConfigurationState StationState);

/// <summary>
/// Completes the structural machine-wide installation with the existing
/// DatabaseInitializer.  It intentionally does not create a password or user;
/// the installed application's PrincipalProvisioningForm remains the only
/// first-run credential flow.
/// </summary>
public static class MachineWideDatabaseInitializer
{
    public static async Task<MachineWideDatabaseInitializationResult> InitializeAsync(
        ArteInventariadoPathProvider paths,
        CancellationToken cancellationToken = default)
        => await InitializeCoreAsync(paths, allowSyntheticRoots: false, cancellationToken);

    // Test-only fixture entry point.  Production setup always uses the
    // canonical Program Files/ProgramData/LocalAppData roots; allowing a
    // caller to opt into synthetic roots is intentionally internal so it
    // cannot weaken the machine-wide installer contract.
    internal static async Task<MachineWideDatabaseInitializationResult> InitializeSyntheticAsync(
        ArteInventariadoPathProvider paths,
        CancellationToken cancellationToken = default)
        => await InitializeCoreAsync(paths, allowSyntheticRoots: true, cancellationToken);

    private static async Task<MachineWideDatabaseInitializationResult> InitializeCoreAsync(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        MachineWideInstallCleanup.EnsureSafeRoots(paths, allowSyntheticRoots);
        Directory.CreateDirectory(paths.DataRoot);

        var databaseCreated = !File.Exists(paths.DatabasePath);
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 30
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection.ToString())
            .Options;

        await using (var context = new AppDbContext(options))
        {
            await DatabaseInitializer.InicializarAsync(context);
            var principalExists = await context.Usuarios
                .AsNoTracking()
                .AnyAsync(
                    user => user.Activo &&
                            user.NombreUsuario == Application.Services.ProtectedAdministrator.UserName &&
                            user.Rol == RolUsuario.ADMIN,
                    cancellationToken);

            var station = new LocalStationConfigurationStore(paths).EnsureInitialized().Status;
            return new MachineWideDatabaseInitializationResult(
                databaseCreated,
                principalExists,
                station.InstallationId ?? Guid.Empty,
                station.Mode,
                station.State);
        }
    }

    public static async Task<bool> HasProtectedPrincipalAsync(
        ArteInventariadoPathProvider paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!File.Exists(paths.DatabasePath))
        {
            return false;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                ForeignKeys = true
            }.ToString())
            .Options;
        await using var context = new AppDbContext(options);
        return await context.Usuarios
            .AsNoTracking()
            .AnyAsync(
                user => user.Activo &&
                        user.NombreUsuario == Application.Services.ProtectedAdministrator.UserName &&
                        user.Rol == RolUsuario.ADMIN,
                cancellationToken);
    }
}
