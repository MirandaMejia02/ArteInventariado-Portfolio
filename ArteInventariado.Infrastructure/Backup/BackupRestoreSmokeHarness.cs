using System.Diagnostics;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

public static class BackupRestoreSmokeHarness
{
    public static int Run(string? logPath = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.PublishBackupSmoke.{Guid.NewGuid():N}");
        logPath = string.IsNullOrWhiteSpace(logPath)
            ? Path.Combine(Path.GetTempPath(), "arteinventariado-backup-restore-smoke.log")
            : Path.GetFullPath(logPath);
        var lines = new List<string> { $"START {DateTimeOffset.UtcNow:O}", $"root={root}" };
        try
        {
            RunAsync(root, lines).GetAwaiter().GetResult();
            lines.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            lines.Add("RESULT=FAILED");
            lines.Add(ex.ToString());
            return 1;
        }
        finally
        {
            lines.Add($"END {DateTimeOffset.UtcNow:O}");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllLines(logPath, lines);
            SqliteConnection.ClearAllPools();
            try
            {
                var resolvedRoot = Path.GetFullPath(root);
                if (resolvedRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(resolvedRoot))
                {
                    Directory.Delete(resolvedRoot, recursive: true);
                }
            }
            catch (Exception cleanupError)
            {
                File.AppendAllText(logPath, $"{Environment.NewLine}cleanup={cleanupError.GetType().Name}");
            }
        }
    }

    private static async Task RunAsync(string root, List<string> lines)
    {
        var password = $"{Guid.NewGuid():N}-Aa1!";
        var databasePath = Path.Combine(root, "Data", "smoke.db");
        var backupDirectory = Path.Combine(root, "Backups");
        var maintenance = Path.Combine(root, "Maintenance");
        var businessAssets = Path.Combine(root, "BusinessAssets", "Products");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        Directory.CreateDirectory(backupDirectory);

        var factory = new SmokeDbContextFactory(databasePath);
        var runtimePaths = ArteInventariadoPathProvider.CreateLegacy(root);
        var stationStore = new LocalStationConfigurationStore(runtimePaths.StationSettingsPath);
        stationStore.EnsureInitialized();
        var provisioningInstallation = new InstallationContext(stationStore);
        var audit = new NoOpAudit();
        int adminId;
        await using (var context = factory.CreateDbContext())
        {
            var migrationTimer = Stopwatch.StartNew();
            await DatabaseInitializer.InicializarAsync(context);
            migrationTimer.Stop();
            lines.Add($"initializeMs={migrationTimer.ElapsedMilliseconds}");
            var provisioning = new PrincipalProvisioningService(
                factory,
                runtimePaths,
                provisioningInstallation,
                audit);
            await provisioning.ProvisionAsync(new PrincipalProvisioningRequest(password, password));
            var admin = await context.Usuarios.SingleAsync(user => user.NombreUsuario == "admin");
            adminId = admin.UsuarioId;
            var category = new Categoria { Nombre = "Smoke", Activa = true, FechaCreacion = DateTime.UtcNow };
            context.Categorias.Add(category);
            await context.SaveChangesAsync();
            var imagePath = Path.Combine(root, "product-smoke.png");
            await File.WriteAllBytesAsync(imagePath, [1, 3, 5, 7, 9]);
            context.Productos.Add(new Producto
            {
                CodigoInterno = "SMOKE-001",
                Modo = ModoProducto.SIMPLE,
                Nombre = "Producto publish original",
                CategoriaId = category.CategoriaId,
                ImagenLocalPath = imagePath,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 250,
                StockActual = 10,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = "SMOKE-001",
                        EsPrincipal = true,
                        SaldosInventario =
                        {
                            new InventarioSaldo { Ubicacion = UbicacionInventario.EXHIBICION, Version = 1 },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, ExistenciaFisica = 10, Version = 1 }
                        }
                    }
                }
            });
            await context.SaveChangesAsync();
        }

        stationStore.SaveConfigured(InstallationMode.Principal);
        var installation = new InstallationContext(stationStore);
        var originalInstallationId = installation.Status.InstallationId;
        var session = new CurrentSession();
        session.IniciarSesion(new UsuarioSesion(adminId, "Administrador", "admin", RolUsuario.ADMIN));
        var log = new StationEventLog(Path.Combine(root, "Logs", "maintenance.log"));
        var engine = new BackupPackageEngine();
        var paths = new BackupStoragePaths(
            databasePath,
            backupDirectory,
            maintenance,
            Path.Combine(maintenance, "pending-restore.json"),
            businessAssets);
        var pending = new PendingRestoreStore(paths.PendingRestorePath);
        var permissions = new EmployeePermissionService(factory, session, new NoOpAudit());
        var service = new BackupMaintenanceService(
            paths, engine, stationStore, pending, factory, installation, permissions, session, log, TimeProvider.System);

        var backupTimer = Stopwatch.StartNew();
        var backup = await service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        backupTimer.Stop();
        lines.Add($"backupMs={backupTimer.ElapsedMilliseconds}");
        lines.Add($"backupBytes={new FileInfo(backup.FilePath).Length}");
        var validation = await service.ValidateBackupAsync(backup.FilePath);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Publish smoke backup invalido: {string.Join(" ", validation.Errors)}");
        }

        await using (var context = factory.CreateDbContext())
        {
            var product = await context.Productos.SingleAsync(product => product.CodigoInterno == "SMOKE-001");
            product.Nombre = "Producto modificado antes de restore";
            await context.SaveChangesAsync();
        }

        var prepareTimer = Stopwatch.StartNew();
        var prepared = await service.PrepareRestoreAsync(new PrepareRestoreRequest(backup.FilePath, password, true));
        prepareTimer.Stop();
        lines.Add($"prepareRestoreMs={prepareTimer.ElapsedMilliseconds}");
        if (!prepared.RestartRequired || !File.Exists(prepared.PreventiveBackupPath))
        {
            throw new InvalidOperationException("El restore publish no preparo el respaldo preventivo.");
        }

        SqliteConnection.ClearAllPools();
        var restoreTimer = Stopwatch.StartNew();
        var processor = new PendingRestoreProcessor(paths, pending, engine, installation, log);
        var restored = processor.ProcessPendingRestore();
        restoreTimer.Stop();
        lines.Add($"startupRestoreMs={restoreTimer.ElapsedMilliseconds}");
        if (!restored.Succeeded)
        {
            throw new InvalidOperationException(restored.Message);
        }

        await using (var context = factory.CreateDbContext())
        {
            var product = await context.Productos.AsNoTracking().SingleAsync(product => product.CodigoInterno == "SMOKE-001");
            if (product.Nombre != "Producto publish original" ||
                string.IsNullOrWhiteSpace(product.ImagenLocalPath) ||
                !File.Exists(product.ImagenLocalPath))
            {
                throw new InvalidOperationException("El restore publish no recupero datos y assets esperados.");
            }
        }

        if (installation.Status.InstallationId != originalInstallationId ||
            installation.Status.Mode != InstallationMode.Principal)
        {
            throw new InvalidOperationException("El restore publish modifico la identidad local de estacion.");
        }

        lines.Add("dataAndAssets=OK");
        lines.Add("stationIdentity=OK");
    }

    private sealed class SmokeDbContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false,
                    ForeignKeys = true
                }.ToString())
                .Options;
            return new AppDbContext(options);
        }

        public ValueTask<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreateDbContext());
        }
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
