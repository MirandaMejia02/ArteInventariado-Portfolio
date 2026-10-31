using System.Reflection;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

public sealed class BackupMaintenanceService : IBackupMaintenanceService
{
    private static readonly SemaphoreSlim OperationGate = new(1, 1);
    private readonly BackupStoragePaths _paths;
    private readonly BackupPackageEngine _engine;
    private readonly LocalStationConfigurationStore _settingsStore;
    private readonly PendingRestoreStore _pendingStore;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IInstallationContext _installation;
    private readonly IEmployeePermissionService _permissions;
    private readonly ICurrentSession _currentSession;
    private readonly StationEventLog _eventLog;
    private readonly TimeProvider _timeProvider;

    internal BackupMaintenanceService(
        BackupStoragePaths paths,
        BackupPackageEngine engine,
        LocalStationConfigurationStore settingsStore,
        PendingRestoreStore pendingStore,
        IDbContextFactory<AppDbContext> contextFactory,
        IInstallationContext installation,
        IEmployeePermissionService permissions,
        ICurrentSession currentSession,
        StationEventLog eventLog,
        TimeProvider timeProvider)
    {
        _paths = paths;
        _engine = engine;
        _settingsStore = settingsStore;
        _pendingStore = pendingStore;
        _contextFactory = contextFactory;
        _installation = installation;
        _permissions = permissions;
        _currentSession = currentSession;
        _eventLog = eventLog;
        _timeProvider = timeProvider;
    }

    public Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadSettings());
    }

    public async Task<BackupSettings> UpdateSettingsAsync(
        UpdateBackupSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (IsMachineWide)
        {
            throw new InvalidOperationException(
                "La política protegida de respaldos solo puede cambiarse mediante el instalador elevado.");
        }

        await _permissions.EnsurePermissionAsync(PermisoEmpleado.RESPALDO_CONFIGURAR, cancellationToken);
        ValidateSettings(request);
        await VerifyWritableDirectoryAsync(request.DestinationDirectory, cancellationToken);

        var previous = _settingsStore.GetBackupSettings();
        var stored = new BackupStationSettingsDocument(
            request.AutomaticEnabled,
            Path.GetFullPath(request.DestinationDirectory),
            request.Frequency.ToString(),
            request.AutomaticRetentionCount,
            previous?.LastAutomaticBackupUtc,
            previous?.LastAutomaticError);
        _settingsStore.SaveBackupSettings(stored);
        WriteEvent("BACKUP_SETTINGS_UPDATED", "Configuracion local de respaldos actualizada.");
        return ReadSettings();
    }

    public async Task<BackupResult> CreateBackupAsync(
        CreateBackupRequest request,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsMachineWide)
        {
            throw new InvalidOperationException(
                "El escritorio no puede escribir respaldos manuales en ProtectedBackups; use el BackupRunner protegido.");
        }

        if (request.Kind != BackupKind.Manual)
        {
            throw new InvalidOperationException("Los respaldos automaticos y preventivos solo pueden iniciarse por su flujo interno.");
        }

        await _permissions.EnsurePermissionAsync(PermisoEmpleado.RESPALDO_CREAR, cancellationToken);
        return await CreateCoordinatedAsync(request, progress, cancellationToken);
    }

    public async Task<BackupResult> CreatePreUpdateBackupAsync(
        string targetVersion,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsMachineWide)
        {
            throw new InvalidOperationException(
                "PRE_UPDATE machine-wide debe crearlo el handoff elevado en su raíz Recovery.");
        }

        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            throw new ArgumentException("La version destino es obligatoria.", nameof(targetVersion));
        }

        await _permissions.EnsurePermissionAsync(PermisoEmpleado.RESPALDO_CREAR, cancellationToken);
        return await CreateCoordinatedAsync(
            new CreateBackupRequest(BackupKind.PreUpdate, ReadSettings().DestinationDirectory, targetVersion),
            progress,
            cancellationToken);
    }

    public async Task<BackupResult?> RunAutomaticBackupIfDueAsync(
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Machine-wide installations are owned by the SYSTEM Task Scheduler
        // job. Login must never become an alternate writer for protected
        // backups (and a standard employee must not need access to them).
        if (IsMachineWide)
        {
            return null;
        }

        var settings = ReadSettings();
        if (!settings.AutomaticEnabled ||
            settings.NextAutomaticBackupUtc is { } next && next > _timeProvider.GetUtcNow())
        {
            return null;
        }

        try
        {
            var result = await CreateCoordinatedAsync(
                new CreateBackupRequest(BackupKind.Automatic, settings.DestinationDirectory),
                progress,
                cancellationToken);
            SaveAutomaticOutcome(_timeProvider.GetUtcNow(), null);
            await ApplyRetentionAsync(settings.DestinationDirectory, settings.AutomaticRetentionCount, cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SaveAutomaticOutcome(null, ex.Message);
            WriteEvent("AUTOMATIC_BACKUP_FAILED", $"El respaldo automatico fallo: {ex.GetType().Name}.");
            throw;
        }
    }

    public async Task<BackupValidationResult> ValidateBackupAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.RESPALDO_CREAR, cancellationToken);
        var staging = _paths.CreateStagingDirectory("validate");
        try
        {
            return await _engine.ValidatePackageAsync(
                Path.GetFullPath(filePath),
                await GetCurrentMigrationsAsync(cancellationToken),
                staging,
                cancellationToken);
        }
        finally
        {
            DeleteDirectoryBestEffort(staging);
        }
    }

    public async Task<IReadOnlyList<BackupHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.RESPALDO_CREAR, cancellationToken);
        var directory = ReadSettings().DestinationDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var migrations = await GetCurrentMigrationsAsync(cancellationToken);
        var result = new List<BackupHistoryItem>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.aibak", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var staging = _paths.CreateStagingDirectory("history");
            try
            {
                var validation = await _engine.ValidatePackageAsync(file, migrations, staging, cancellationToken);
                result.Add(new BackupHistoryItem(
                    file,
                    validation.Manifest?.CreatedUtc,
                    validation.Manifest?.Kind,
                    new FileInfo(file).Length,
                    validation.Manifest?.SourceInstallationMode.ToString(),
                    validation.IsValid,
                    validation.IsValid ? "Valido" : string.Join(" ", validation.Errors.Take(1))));
            }
            finally
            {
                DeleteDirectoryBestEffort(staging);
            }
        }

        return result;
    }

    public async Task<PrepareRestoreResult> PrepareRestoreAsync(
        PrepareRestoreRequest request,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsMachineWide)
        {
            throw new InvalidOperationException(
                "Las instalaciones machine-wide deben usar la restauracion protegida mediante el helper elevado.");
        }

        await _permissions.EnsurePermissionAsync(PermisoEmpleado.RESPALDO_RESTAURAR, cancellationToken);
        if (!ProtectedAdministrator.IsProtectedAdministrator(_currentSession.UsuarioActual))
        {
            throw new InvalidOperationException(
                "La restauracion esta reservada al Administrador Principal.");
        }

        if (!request.ConfirmationAccepted)
        {
            throw new InvalidOperationException("Debe confirmar que la base de datos actual sera reemplazada.");
        }

        if (!await OperationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Ya existe una operacion de respaldo o restauracion en curso.");
        }

        var restoreStaging = _paths.CreateStagingDirectory("restore");
        IReadOnlyList<string> installedAssets = [];
        try
        {
            progress?.Report(new BackupProgress("validate", 10, "Validando el respaldo seleccionado..."));
            var migrations = await GetCurrentMigrationsAsync(cancellationToken);
            var validation = await _engine.ValidatePackageAsync(
                Path.GetFullPath(request.BackupFilePath), migrations, restoreStaging, cancellationToken);
            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidDataException($"El respaldo no es valido. {string.Join(" ", validation.Errors)}");
            }

            await ReauthenticateCurrentUserAsync(request.Password, cancellationToken);

            progress?.Report(new BackupProgress("pre-restore", 35, "Creando respaldo preventivo..."));
            var preventive = await CreateCoreAsync(
                new CreateBackupRequest(BackupKind.PreRestore, ReadSettings().DestinationDirectory),
                progress,
                cancellationToken);

            var stagedDatabase = Path.Combine(restoreStaging, BackupPackageEngine.DatabaseEntryName);
            progress?.Report(new BackupProgress("migrate", 60, "Preparando el esquema restaurado..."));
            await _engine.MigrateStagedDatabaseAsync(stagedDatabase, cancellationToken);
            installedAssets = await _engine.InstallAssetsIntoStagedDatabaseAsync(
                stagedDatabase,
                restoreStaging,
                validation.Manifest,
                _paths.BusinessAssetsDirectory,
                _paths.ManagedAssetsDirectory ?? Station.StationConfigurationPathProvider.GetManagedAssetsDirectory(),
                cancellationToken);

            var finalErrors = await _engine.ValidateDatabaseAsync(stagedDatabase, migrations, allowOlderSchema: false, cancellationToken);
            if (finalErrors.Count > 0)
            {
                throw new InvalidDataException($"La base preparada no supero la validacion final. {string.Join(" ", finalErrors)}");
            }

            var restoreId = Guid.NewGuid();
            var stagedHash = await BackupPackageEngine.ComputeSha256Async(stagedDatabase, cancellationToken);
            _pendingStore.Save(new PendingRestoreDocument(
                PendingRestoreStore.CurrentSchemaVersion,
                restoreId,
                _timeProvider.GetUtcNow(),
                stagedDatabase,
                stagedHash,
                preventive.FilePath,
                validation.Manifest.BackupId,
                installedAssets));
            WriteEvent("RESTORE_REQUESTED", $"Restauracion {restoreId} preparada para el siguiente inicio.");
            progress?.Report(new BackupProgress("pending", 100, "Restauracion preparada. Reinicie la aplicacion."));

            var warning = validation.Manifest.SourceInstallationMode == InstallationMode.Consulta &&
                _installation.Status.Mode == InstallationMode.Principal;
            return new PrepareRestoreResult(
                restoreId,
                validation.Manifest,
                preventive.FilePath,
                true,
                warning,
                warning
                    ? "Restauracion preparada desde una instalacion Consulta. Puede contener datos anteriores; reinicie para aplicar."
                    : "Restauracion validada y preparada. Reinicie ArteInventariado para aplicarla.");
        }
        catch
        {
            foreach (var path in installedAssets)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            WriteEvent("RESTORE_PREPARATION_FAILED", "La preparacion de restauracion fallo antes de reemplazar la base activa.");
            DeleteDirectoryBestEffort(restoreStaging);
            throw;
        }
        finally
        {
            OperationGate.Release();
        }
    }

    private async Task<BackupResult> CreateCoordinatedAsync(
        CreateBackupRequest request,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!await OperationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Ya existe una operacion de respaldo o restauracion en curso.");
        }

        try
        {
            return await CreateCoreAsync(request, progress, cancellationToken);
        }
        finally
        {
            OperationGate.Release();
        }
    }

    private async Task<BackupResult> CreateCoreAsync(
        CreateBackupRequest request,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!_installation.Status.IsConfigured || !_installation.Status.InstallationId.HasValue || !_installation.Status.Mode.HasValue)
        {
            throw new InvalidOperationException("La estacion debe estar configurada antes de crear respaldos.");
        }

        var destination = Path.GetFullPath(request.DestinationDirectory ?? ReadSettings().DestinationDirectory);
        if (IsMachineWide && !PathEquals(destination, _paths.PathProvider!.ProtectedBackupsRoot))
        {
            throw new InvalidDataException(
                "Los respaldos de una instalación machine-wide deben permanecer en ProtectedBackups.");
        }
        await VerifyWritableDirectoryAsync(destination, cancellationToken);
        var staging = _paths.CreateStagingDirectory("backup");
        var backupId = Guid.NewGuid();
        try
        {
            progress?.Report(new BackupProgress("snapshot", 10, "Creando snapshot SQLite consistente..."));
            var snapshot = Path.Combine(staging, BackupPackageEngine.DatabaseEntryName);
            await _engine.CreateSnapshotAsync(_paths.DatabasePath, snapshot, cancellationToken);
            var migrations = await GetCurrentMigrationsAsync(cancellationToken);
            var errors = await _engine.ValidateDatabaseAsync(snapshot, migrations, allowOlderSchema: false, cancellationToken);
            if (errors.Count > 0)
            {
                throw new InvalidDataException($"El snapshot no supero las validaciones. {string.Join(" ", errors)}");
            }

            progress?.Report(new BackupProgress("assets", 35, "Incorporando activos comerciales..."));
            var productAssets = await _engine.ReadProductAssetsAsync(snapshot, cancellationToken);
            var legacyAssets = await _engine.CopyAssetsToStagingAsync(productAssets, staging, cancellationToken);
            var managedSources = await _engine.ReadManagedAssetsAsync(
                snapshot,
                _paths.ManagedAssetsDirectory ?? Station.StationConfigurationPathProvider.GetManagedAssetsDirectory(),
                cancellationToken);
            var managedAssets = await _engine.CopyManagedAssetsToStagingAsync(managedSources, staging, cancellationToken);
            var assets = legacyAssets.Concat(managedAssets).ToList();
            var warnings = assets
                .Where(asset => asset.WasMissing)
                .Select(asset => $"La imagen del producto {asset.ProductId} ya no existia en su ruta original.")
                .ToList();
            var databaseHash = await BackupPackageEngine.ComputeSha256Async(snapshot, cancellationToken);
            var appliedMigrations = await _engine.ReadAppliedMigrationsAsync(snapshot, cancellationToken);
            var now = _timeProvider.GetUtcNow();
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                backupId,
                now,
                now.ToLocalTime(),
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                "SQLite",
                appliedMigrations,
                appliedMigrations.LastOrDefault(),
                databaseHash,
                new FileInfo(snapshot).Length,
                _installation.Status.InstallationId.Value,
                _installation.Status.Mode.Value,
                request.Kind,
                assets);

            progress?.Report(new BackupProgress("package", 60, "Construyendo paquete autocontenido..."));
            var name = request.Kind == BackupKind.PreUpdate
                ? $"PRE_UPDATE_{SanitizeFilePart(manifest.ApplicationVersion)}_TO_{SanitizeFilePart(request.TargetVersion ?? "unknown")}_{backupId:N}.aibak"
                : $"ArteInventariado_{now:yyyy-MM-dd_HHmmss}_{request.Kind}_{backupId:N}.aibak";
            var localPackage = Path.Combine(staging, name);
            await _engine.CreatePackageAsync(staging, snapshot, manifest, localPackage, cancellationToken);

            progress?.Report(new BackupProgress("validate", 75, "Validando paquete terminado..."));
            var validationDirectory = Path.Combine(staging, "validation");
            var validation = await _engine.ValidatePackageAsync(localPackage, migrations, validationDirectory, cancellationToken);
            if (!validation.IsValid)
            {
                throw new InvalidDataException($"El paquete final no supero la validacion. {string.Join(" ", validation.Errors)}");
            }

            progress?.Report(new BackupProgress("publish", 90, "Publicando archivo terminado..."));
            var finalPath = Path.Combine(destination, name);
            var uploadingPath = Path.Combine(destination, $".{name}.uploading-{backupId:N}");
            try
            {
                await BackupPackageEngine.CopyFileAsync(localPackage, uploadingPath, cancellationToken);
                File.Move(uploadingPath, finalPath, overwrite: false);
            }
            finally
            {
                if (File.Exists(uploadingPath))
                {
                    File.Delete(uploadingPath);
                }
            }
            progress?.Report(new BackupProgress("complete", 100, "Respaldo creado y validado."));
            WriteEvent(
                request.Kind switch
                {
                    BackupKind.Manual => "MANUAL_BACKUP_SUCCEEDED",
                    BackupKind.Automatic => "AUTOMATIC_BACKUP_SUCCEEDED",
                    _ => "PRE_RESTORE_BACKUP_SUCCEEDED"
                },
                $"Respaldo {backupId} creado: {Path.GetFileName(finalPath)}.");
            return new BackupResult(backupId, finalPath, manifest, warnings);
        }
        catch (OperationCanceledException)
        {
            WriteEvent("BACKUP_CANCELLED", $"Respaldo {request.Kind} cancelado antes de publicarse.");
            throw;
        }
        catch (Exception ex)
        {
            WriteEvent("BACKUP_FAILED", $"Respaldo {request.Kind} fallo: {ex.GetType().Name}.");
            throw;
        }
        finally
        {
            DeleteDirectoryBestEffort(staging);
        }
    }

    private async Task ApplyRetentionAsync(string directory, int retentionCount, CancellationToken cancellationToken)
    {
        var migrations = await GetCurrentMigrationsAsync(cancellationToken);
        var automatic = new List<(string Path, DateTimeOffset Created)>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.aibak", SearchOption.TopDirectoryOnly))
        {
            var staging = _paths.CreateStagingDirectory("retention");
            try
            {
                var validation = await _engine.ValidatePackageAsync(file, migrations, staging, cancellationToken);
                if (validation.IsValid && validation.Manifest?.Kind == BackupKind.Automatic)
                {
                    automatic.Add((file, validation.Manifest.CreatedUtc));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                WriteEvent("BACKUP_RETENTION_SKIPPED_FILE", $"Retencion omitio {Path.GetFileName(file)}: {ex.GetType().Name}.");
            }
            finally
            {
                DeleteDirectoryBestEffort(staging);
            }
        }

        foreach (var old in automatic.OrderByDescending(item => item.Created).Skip(retentionCount))
        {
            File.Delete(old.Path);
            WriteEvent("AUTOMATIC_BACKUP_RETAINED", $"Retencion elimino el respaldo automatico {Path.GetFileName(old.Path)}.");
        }
    }

    private async Task<IReadOnlyList<string>> GetCurrentMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return context.Database.GetMigrations().ToList();
    }

    private async Task ReauthenticateCurrentUserAsync(string password, CancellationToken cancellationToken)
    {
        var current = _currentSession.UsuarioActual
            ?? throw new InvalidOperationException("Debe existir una sesion activa para restaurar.");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("La contrasena actual es obligatoria.", nameof(password));
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var stored = await context.Usuarios
            .AsNoTracking()
            .Where(user => user.UsuarioId == current.UsuarioId && user.Activo)
            .Select(user => user.PasswordHash)
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null || !BCrypt.Net.BCrypt.Verify(password, stored))
        {
            WriteEvent("RESTORE_REAUTHENTICATION_FAILED", "Reautenticacion rechazada para restaurar un respaldo.");
            throw new InvalidOperationException("La contrasena actual es incorrecta.");
        }
    }

    private BackupSettings ReadSettings()
    {
        if (IsMachineWide)
        {
            var provider = _paths.PathProvider
                ?? throw new InvalidOperationException(
                    "El almacenamiento machine-wide no tiene proveedor de rutas protegido.");
            var policyStore = new MachineWideBackupPolicyStore();
            var policy = policyStore.Load(provider);
            var status = policyStore.LoadStatus(provider);
            var successful = status.LastSuccessfulBackupUtc;
            return new BackupSettings(
                policy.Enabled,
                policy.DestinationDirectory,
                BackupFrequency.Daily,
                policy.DailyGenerations,
                successful,
                status.LastError,
                successful?.AddDays(1));
        }

        var stored = _settingsStore.GetBackupSettings();
        var destination = string.IsNullOrWhiteSpace(stored?.DestinationDirectory)
            ? _paths.DefaultBackupDirectory
            : stored.DestinationDirectory;
        var frequency = Enum.TryParse<BackupFrequency>(stored?.Frequency, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : BackupFrequency.Daily;
        var retention = stored?.AutomaticRetentionCount is >= 1 and <= 365
            ? stored.AutomaticRetentionCount
            : 15;
        var next = stored?.LastAutomaticBackupUtc is { } last
            ? CalculateNext(last, frequency)
            : _timeProvider.GetUtcNow();
        return new BackupSettings(
            stored?.AutomaticEnabled ?? false,
            Path.GetFullPath(destination),
            frequency,
            retention,
            stored?.LastAutomaticBackupUtc,
            stored?.LastAutomaticError,
            next);
    }

    private void SaveAutomaticOutcome(DateTimeOffset? successUtc, string? error)
    {
        if (IsMachineWide)
        {
            return;
        }

        var settings = ReadSettings();
        _settingsStore.SaveBackupSettings(new BackupStationSettingsDocument(
            settings.AutomaticEnabled,
            settings.DestinationDirectory,
            settings.Frequency.ToString(),
            settings.AutomaticRetentionCount,
            successUtc ?? settings.LastAutomaticBackupUtc,
            error));
    }

    internal static DateTimeOffset CalculateNext(DateTimeOffset last, BackupFrequency frequency) => frequency switch
    {
        BackupFrequency.Daily => last.AddDays(1),
        BackupFrequency.Weekly => last.AddDays(7),
        BackupFrequency.EveryFifteenDays => last.AddDays(15),
        BackupFrequency.Monthly => last.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };

    private static void ValidateSettings(UpdateBackupSettingsRequest request)
    {
        if (!Enum.IsDefined(request.Frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Frequency));
        }

        if (string.IsNullOrWhiteSpace(request.DestinationDirectory))
        {
            throw new ArgumentException("La carpeta de respaldos es obligatoria.", nameof(request.DestinationDirectory));
        }

        if (request.AutomaticRetentionCount is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(request.AutomaticRetentionCount), "La retencion debe estar entre 1 y 365 respaldos.");
        }
    }

    private static async Task VerifyWritableDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, $".arteinventariado-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(probe, [0x41, 0x49], cancellationToken);
            File.Delete(probe);
        }
        catch
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }

            throw;
        }
    }

    private void WriteEvent(string eventName, string description) =>
        _eventLog.Write(eventName, _currentSession.UsuarioActual?.NombreUsuario ?? "system", _installation.Status.Mode, description);

    private bool IsMachineWide => _paths.PathProvider?.IsMachineWide == true;

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Staging abandonado se conserva para diagnostico y se limpia en un inicio posterior.
        }
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
