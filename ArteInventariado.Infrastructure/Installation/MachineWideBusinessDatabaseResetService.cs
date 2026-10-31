using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Installation;

public sealed record MachineWideBusinessDatabaseResetResult(
    Guid OperationId,
    Guid InstallationId,
    string BackupPath,
    string BackupSha256,
    string DatabasePath,
    bool LauncherStarted);

/// <summary>
/// Replaces only the machine-wide business database. A healthy protected
/// backup and a durable recovery journal are required before the live DB is
/// moved. Station identity/configuration, assets, backups, installation files,
/// and scheduled tasks are deliberately outside the reset scope.
/// </summary>
public sealed class MachineWideBusinessDatabaseResetService
{
    public const string ConfirmationPhrase = "REINICIAR DB";
    public const string ConfirmationText =
        "Se conservarán la estación PRINCIPAL, su identidad, configuración, archivos, assets y respaldos. " +
        "Se reemplazará únicamente la base de datos del negocio. El respaldo protegido se conservará. " +
        "Esta acción no se puede deshacer desde la aplicación.";

    private readonly Func<ArteInventariadoPathProvider, Guid, CancellationToken, MachineWideBackupRunResult> _createBackup;
    private readonly Func<ArteInventariadoPathProvider, Guid, int> _provisionPrincipal;
    private readonly Func<IDisposable> _acquireRuntimeGate;
    private readonly Func<bool> _isAdministrator;
    private readonly bool _allowSyntheticRoots;

    [SupportedOSPlatform("windows")]
    public MachineWideBusinessDatabaseResetService()
        : this(
            (paths, operationId, cancellationToken) =>
                new MachineWideBackupRunner(paths)
                    .RunAsync(BackupKind.Manual, operationId, cancellationToken)
                    .GetAwaiter()
                    .GetResult(),
            ProvisionPrincipalInteractively,
            AcquireRuntimeGate,
            MachineWideElevation.IsAdministrator,
            allowSyntheticRoots: false)
    {
    }

    internal MachineWideBusinessDatabaseResetService(
        Func<ArteInventariadoPathProvider, Guid, CancellationToken, MachineWideBackupRunResult> createBackup,
        Func<ArteInventariadoPathProvider, Guid, int> provisionPrincipal,
        Func<IDisposable> acquireRuntimeGate,
        Func<bool> isAdministrator,
        bool allowSyntheticRoots)
    {
        _createBackup = createBackup ?? throw new ArgumentNullException(nameof(createBackup));
        _provisionPrincipal = provisionPrincipal ?? throw new ArgumentNullException(nameof(provisionPrincipal));
        _acquireRuntimeGate = acquireRuntimeGate ?? throw new ArgumentNullException(nameof(acquireRuntimeGate));
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public static bool IsConfirmationValid(string? value) =>
        string.Equals(value?.Trim(), ConfirmationPhrase, StringComparison.OrdinalIgnoreCase);

    public MachineWideBusinessDatabaseResetResult ResetBusinessDatabase(
        ArteInventariadoPathProvider paths,
        string? confirmation,
        CancellationToken cancellationToken = default) =>
        ResetCore(paths, confirmation, cancellationToken, startLauncherAfterSuccess: true);

    internal MachineWideBusinessDatabaseResetResult ResetSynthetic(
        ArteInventariadoPathProvider paths,
        string? confirmation,
        bool startLauncherAfterSuccess = false,
        CancellationToken cancellationToken = default) =>
        ResetCore(paths, confirmation, cancellationToken, startLauncherAfterSuccess);

    public void RecoverPendingReset(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureAdministrator();
        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths);
        using var maintenanceGate = MachineWideMaintenanceLease.Acquire(paths);
        using var runtimeGate = _acquireRuntimeGate();
        RecoverCore(paths, allowSyntheticRoots: false);
    }

    internal void RecoverPendingResetSynthetic(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureAdministrator();
        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, allowSyntheticRoots: true);
        using var maintenanceGate = MachineWideMaintenanceLease.Acquire(paths, allowSyntheticRoots: true);
        using var runtimeGate = _acquireRuntimeGate();
        RecoverCore(paths, allowSyntheticRoots: true);
    }

    private MachineWideBusinessDatabaseResetResult ResetCore(
        ArteInventariadoPathProvider paths,
        string? confirmation,
        CancellationToken cancellationToken,
        bool startLauncherAfterSuccess)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!IsConfirmationValid(confirmation))
        {
            throw new MachineWideDatabaseResetConfirmationRequiredException();
        }

        EnsureAdministrator();
        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, _allowSyntheticRoots);
        if (!paths.IsMachineWide)
        {
            throw new InvalidDataException("El reinicio de DB solo admite una estación machine-wide.");
        }

        var resetAlreadyPending = _allowSyntheticRoots
            ? MachineWideDatabaseResetJournalStore.HasPendingSynthetic(paths)
            : MachineWideDatabaseResetJournalStore.HasPending(paths);
        if (resetAlreadyPending)
        {
            throw new InvalidOperationException(
                "Ya existe un reinicio de base de datos pendiente. Use primero la recuperación protegida de Setup.");
        }

        if (DisasterRecoveryJournalStore.HasPending(paths.RecoveryRoot) || File.Exists(paths.PendingRestorePath))
        {
            throw new InvalidOperationException(
                "Hay otra recuperación pendiente. Resuélvala antes de reiniciar la base de datos.");
        }

        using var initialRuntimeGate = new IdempotentDisposable(_acquireRuntimeGate());
        using var maintenanceGate = MachineWideMaintenanceLease.Acquire(paths, _allowSyntheticRoots);
        cancellationToken.ThrowIfCancellationRequested();
        var stationStore = new LocalStationConfigurationStore(paths);
        var station = stationStore.Load().Status;
        if (!station.IsConfigured ||
            station.Mode != InstallationMode.Principal ||
            station.InstallationId is not { } installationId || installationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "El reinicio solo se permite en una estación PRINCIPAL ya configurada con identidad válida.");
        }

        if (!File.Exists(paths.DatabasePath))
        {
            throw new FileNotFoundException("No se encontró la base de datos existente que se debe respaldar.", paths.DatabasePath);
        }

        EnsureDatabaseIntegrity(paths.DatabasePath);
        var stationCoreHash = HashFileIfExists(paths.StationCorePath);
        var stationOperationalHash = HashFileIfExists(paths.StationOperationalSettingsPath);
        var currentPointerHash = HashFileIfExists(paths.CurrentStatePath);
        EnsureNoPendingUpdate(paths);

        cancellationToken.ThrowIfCancellationRequested();
        var operationId = Guid.NewGuid();
        var journal = new MachineWideDatabaseResetJournal(
            MachineWideDatabaseResetJournalStore.CurrentSchemaVersion,
            operationId,
            installationId,
            InstallationMode.Principal,
            MachineWideDatabaseResetPhase.BackingUp,
            DateTimeOffset.UtcNow,
            "pending.aibak",
            new string('0', 64));

        MachineWideDatabaseResetJournalStore.EnsureStagingDirectory(
            paths,
            operationId,
            _allowSyntheticRoots);
        var stagedDatabase = MachineWideDatabaseResetJournalStore.StagedDatabasePath(paths, operationId);
        var journalWritten = false;
        var committed = false;
        IDisposable? runtimeGate = null;
        MachineWideBackupRunResult? backup = null;
        try
        {
            MachineWideDatabaseResetJournalStore.Save(paths, journal, _allowSyntheticRoots);
            journalWritten = true;

            // The journal blocks application launches and the updater while
            // the protected snapshot is being produced.
            backup = _createBackup(paths, operationId, cancellationToken);
            ValidateBackup(backup, paths, installationId);
            cancellationToken.ThrowIfCancellationRequested();
            journal = journal with
            {
                Phase = MachineWideDatabaseResetPhase.Prepared,
                BackupFileName = Path.GetFileName(backup.FilePath!),
                BackupSha256 = backup.Sha256!
            };
            MachineWideDatabaseResetJournalStore.Save(paths, journal, _allowSyntheticRoots);

            MoveDatabaseToStaging(paths.DatabasePath, stagedDatabase);
            cancellationToken.ThrowIfCancellationRequested();

            var initializationTask = _allowSyntheticRoots
                ? MachineWideDatabaseInitializer.InitializeSyntheticAsync(paths, cancellationToken)
                : MachineWideDatabaseInitializer.InitializeAsync(paths, cancellationToken);
            var initialization = initializationTask.GetAwaiter().GetResult();
            if (initialization.InstallationId != installationId ||
                initialization.StationMode != InstallationMode.Principal ||
                initialization.PrincipalExists)
            {
                throw new InvalidDataException(
                    "La DB nueva no quedó vacía o cambió la identidad/modo de la estación.");
            }

            EnsureFreshDatabaseBeforeProvision(paths.DatabasePath);
            journal = journal with { Phase = MachineWideDatabaseResetPhase.Provisioning };
            MachineWideDatabaseResetJournalStore.Save(paths, journal, _allowSyntheticRoots);

            // The pending journal blocks every ordinary application launch.
            // Release the instance gate only so this exact child can display
            // the existing protected administrator-provisioning form.
            initialRuntimeGate.Dispose();
            var provisioningExitCode = _provisionPrincipal(paths, operationId);
            runtimeGate = _acquireRuntimeGate();
            if (provisioningExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"La configuración del Administrador Principal terminó con código {provisioningExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureDatabaseIntegrity(paths.DatabasePath);
            if (!MachineWideDatabaseInitializer.HasProtectedPrincipalAsync(paths, cancellationToken)
                    .GetAwaiter()
                    .GetResult())
            {
                throw new InvalidOperationException(
                    "La ventana de configuración terminó sin crear el Administrador Principal protegido.");
            }

            EnsureFreshDatabaseAfterProvision(paths.DatabasePath);

            EnsureFileHashUnchanged(paths.StationCorePath, stationCoreHash, "identidad/configuración protegida de estación");
            EnsureFileHashUnchanged(paths.StationOperationalSettingsPath, stationOperationalHash, "configuración operativa de estación");
            EnsureFileHashUnchanged(paths.CurrentStatePath, currentPointerHash, "puntero de versión");

            journal = journal with { Phase = MachineWideDatabaseResetPhase.Committed };
            MachineWideDatabaseResetJournalStore.Save(paths, journal, _allowSyntheticRoots);
            committed = true;
            // Keep the raw previous DB staged until the protected snapshot
            // has been re-hashed after the credential UI and immediately
            // before the irreversible cleanup.
            MachineWideDatabaseResetJournalStore.EnsureCommittedBackupIsPresent(
                paths,
                journal,
                _allowSyntheticRoots);
            MachineWideDatabaseResetJournalStore.DeleteStagingFiles(paths, operationId, _allowSyntheticRoots);
            MachineWideDatabaseResetJournalStore.DeleteJournal(paths, _allowSyntheticRoots);

            runtimeGate.Dispose();
            runtimeGate = null;
        }
        catch (Exception resetException)
        {
            if (journalWritten && !committed)
            {
                try
                {
                    runtimeGate ??= _acquireRuntimeGate();
                    RecoverCore(paths, _allowSyntheticRoots);
                }
                catch (Exception recoveryException)
                {
                    throw new InvalidOperationException(
                        "Falló el reinicio y no se pudo completar el rollback automático. La aplicación permanece bloqueada; ejecute Setup --recover-database-reset.",
                        new AggregateException(resetException, recoveryException));
                }
            }

            throw;
        }
        finally
        {
            runtimeGate?.Dispose();
        }

        var launcherStarted = false;
        if (startLauncherAfterSuccess)
        {
            try
            {
                StartLauncherUnelevated(paths);
                launcherStarted = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // The DB reset is already committed. Setup reports the
                // launcher state separately instead of pretending it rolled
                // the reset back.
            }
        }

        return new MachineWideBusinessDatabaseResetResult(
            operationId,
            installationId,
            backup!.FilePath!,
            backup.Sha256!,
            paths.DatabasePath,
            launcherStarted);
    }

    private void RecoverCore(ArteInventariadoPathProvider paths, bool allowSyntheticRoots)
    {
        var journal = allowSyntheticRoots
            ? MachineWideDatabaseResetJournalStore.LoadSynthetic(paths)
            : MachineWideDatabaseResetJournalStore.Load(paths);
        if (journal is null)
        {
            throw new InvalidOperationException("No hay un reinicio de base de datos pendiente que recuperar.");
        }

        if (journal.Phase == MachineWideDatabaseResetPhase.Committed)
        {
            MachineWideDatabaseResetJournalStore.EnsureCommittedBackupIsPresent(
                paths,
                journal,
                allowSyntheticRoots);
            MachineWideDatabaseResetJournalStore.DeleteStagingFiles(paths, journal.OperationId, allowSyntheticRoots);
            MachineWideDatabaseResetJournalStore.DeleteJournal(paths, allowSyntheticRoots);
            return;
        }

        journal = journal with { Phase = MachineWideDatabaseResetPhase.RollingBack };
        MachineWideDatabaseResetJournalStore.Save(paths, journal, allowSyntheticRoots);
        MachineWideDatabaseResetJournalStore.RestorePreviousDatabase(paths, journal, allowSyntheticRoots);
        if (!File.Exists(paths.DatabasePath))
        {
            throw new InvalidDataException("El rollback no pudo recuperar la base de datos anterior.");
        }

        EnsureDatabaseIntegrity(paths.DatabasePath);
        MachineWideDatabaseResetJournalStore.DeleteStagingFiles(paths, journal.OperationId, allowSyntheticRoots);
        MachineWideDatabaseResetJournalStore.DeleteJournal(paths, allowSyntheticRoots);
    }

    private static void EnsureNoPendingUpdate(ArteInventariadoPathProvider paths)
    {
        var state = new UpdateStateStore(paths.UpdateStatePath).Load();
        var current = new UpdatePointerStore(paths.CurrentStatePath).Load();
        if (UpdateRecoveryPolicy.RequiresRecovery(state, current) ||
            state?.State == UpdateState.ReadyToInstall)
        {
            throw new InvalidOperationException(
                "Hay una actualización pendiente o en recuperación. Complétela antes de reiniciar la base de datos.");
        }
    }

    private static void ValidateBackup(
        MachineWideBackupRunResult backup,
        ArteInventariadoPathProvider paths,
        Guid installationId)
    {
        if (backup.Outcome != MachineWideBackupRunOutcome.Succeeded ||
            backup.Manifest is null ||
            string.IsNullOrWhiteSpace(backup.FilePath) ||
            string.IsNullOrWhiteSpace(backup.Sha256))
        {
            throw new InvalidOperationException(
                $"No se obtuvo un respaldo protegido completo. {backup.Message}");
        }

        var manifest = backup.Manifest;
        var expectedPath = Path.GetFullPath(backup.FilePath);
        var backupRoot = Path.GetFullPath(paths.ProtectedBackupsRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!expectedPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(expectedPath) ||
            (File.GetAttributes(expectedPath) & FileAttributes.ReparsePoint) != 0 ||
            manifest.Kind != BackupKind.Manual ||
            manifest.SourceInstallationId != installationId ||
            manifest.SourceInstallationMode != InstallationMode.Principal ||
            manifest.ContentStatus != BackupContentStatus.Healthy ||
            (manifest.OmittedAssetCount ?? manifest.Assets.Count(asset => asset.WasMissing)) != 0)
        {
            throw new InvalidDataException(
                "El respaldo no es saludable o no corresponde a la identidad PRINCIPAL que se va a reiniciar.");
        }

        using var stream = new FileStream(expectedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actualHash, backup.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !backup.Sha256.All(Uri.IsHexDigit) || backup.Sha256.Length != 64)
        {
            throw new InvalidDataException("El SHA-256 del respaldo protegido no coincide con el archivo publicado.");
        }
    }

    private static void MoveDatabaseToStaging(string databasePath, string stagedDatabase)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("La base de datos desapareció antes del movimiento protegido.", databasePath);
        }

        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            var source = databasePath + suffix;
            if (File.Exists(source))
            {
                File.Move(source, stagedDatabase + suffix);
            }
        }

        // Move the main file last. If the process stops during sidecar moves,
        // recovery can see the original main DB and put any moved sidecars
        // back without mistaking a partial swap for a new database.
        File.Move(databasePath, stagedDatabase);
    }

    private static void EnsureDatabaseIntegrity(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 10
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = command.ExecuteScalar() as string;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La base de datos no superó PRAGMA quick_check.");
        }
    }

    private static void EnsureFreshDatabaseBeforeProvision(string databasePath)
    {
        using var context = CreateContext(databasePath);
        if (context.Usuarios.Any() ||
            context.Ventas.Any() || context.Compras.Any() || context.Clientes.Any() ||
            context.Proveedores.Any() || context.Productos.Any() || context.ProductoSkus.Any() ||
            context.InventarioSaldos.Any() || context.Cajas.Any() || context.Cotizaciones.Any() ||
            context.PedidosPersonalizados.Any())
        {
            throw new InvalidDataException("La base nueva no está vacía antes de configurar el administrador.");
        }
    }

    private static void EnsureFreshDatabaseAfterProvision(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var users = context.Usuarios.AsNoTracking().ToList();
        if (users.Count != 1 ||
            !users[0].Activo ||
            users[0].NombreUsuario != ProtectedAdministrator.UserName ||
            users[0].Rol != RolUsuario.ADMIN ||
            context.Ventas.Any() || context.Compras.Any() || context.Clientes.Any() ||
            context.Proveedores.Any() || context.Productos.Any() || context.ProductoSkus.Any() ||
            context.InventarioSaldos.Any() || context.Cajas.Any() || context.Cotizaciones.Any() ||
            context.PedidosPersonalizados.Any())
        {
            throw new InvalidDataException(
                "La base reiniciada no cumple el estado inicial: solo debe contener al Administrador Principal y datos estructurales.");
        }
    }

    private static AppDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                ForeignKeys = true
            }.ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static string? HashFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void EnsureFileHashUnchanged(string path, string? expectedHash, string description)
    {
        var actualHash = HashFileIfExists(path);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Cambió {description} durante el reinicio de DB.");
        }
    }

    private void EnsureAdministrator()
    {
        if (!_isAdministrator())
        {
            throw new UnauthorizedAccessException(
                "El reinicio de base de datos requiere consentimiento UAC de un administrador.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IDisposable AcquireRuntimeGate()
    {
        var legacyMutex = new Mutex(
            initiallyOwned: false,
            ApplicationInstanceMutex.LegacyInteractiveMutexName);
        Mutex? interactiveMutex = null;
        var legacyAcquired = false;
        var interactiveAcquired = false;
        try
        {
            legacyAcquired = TryAcquireMutex(legacyMutex);
            if (!legacyAcquired)
            {
                throw new InvalidOperationException(
                    "Cierre ArteInventariado en esta computadora antes de reiniciar la base de datos.");
            }

            interactiveMutex = ApplicationInstanceMutex.CreateInteractive(
                initiallyOwned: false,
                out _);
            interactiveAcquired = TryAcquireMutex(interactiveMutex);
            if (!interactiveAcquired)
            {
                throw new InvalidOperationException(
                    "Cierre ArteInventariado en esta computadora antes de reiniciar la base de datos.");
            }

            return new RuntimeGateLease(legacyMutex, interactiveMutex);
        }
        catch
        {
            try
            {
                if (interactiveAcquired)
                {
                    interactiveMutex!.ReleaseMutex();
                }
            }
            finally
            {
                interactiveMutex?.Dispose();
                try
                {
                    if (legacyAcquired)
                    {
                        legacyMutex.ReleaseMutex();
                    }
                }
                finally
                {
                    legacyMutex.Dispose();
                }
            }

            throw;
        }
    }

    private static bool TryAcquireMutex(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private sealed class IdempotentDisposable(IDisposable inner) : IDisposable
    {
        private IDisposable? _inner = inner;

        public void Dispose() => Interlocked.Exchange(ref _inner, null)?.Dispose();
    }

    private static int ProvisionPrincipalInteractively(ArteInventariadoPathProvider paths, Guid operationId)
    {
        var pointer = new UpdatePointerStore(paths.CurrentStatePath).Load()
            ?? throw new InvalidDataException("No se encontró una versión activa para configurar el Administrador Principal.");
        var executable = Path.Combine(
            paths.GetVersionDirectory(pointer.Version),
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "No se encontró la aplicación instalada para abrir la configuración del Administrador Principal.",
                executable);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("--provision-principal");
        startInfo.ArgumentList.Add("--database-reset-provision");
        startInfo.ArgumentList.Add(operationId.ToString("D"));
        UpdateRuntimePaths.Create(paths).AddLayoutArguments(startInfo);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo abrir la configuración del Administrador Principal.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void StartLauncherUnelevated(ArteInventariadoPathProvider paths)
    {
        if (!File.Exists(paths.LauncherPath))
        {
            throw new FileNotFoundException("No se encontró el launcher estable de ArteInventariado.", paths.LauncherPath);
        }

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(paths.LauncherPath)!,
            Arguments = $"\"{paths.LauncherPath}\""
        };
        _ = Process.Start(startInfo);
    }

    private sealed class RuntimeGateLease(Mutex legacyMutex, Mutex interactiveMutex) : IDisposable
    {
        private Mutex? _legacyMutex = legacyMutex;
        private Mutex? _interactiveMutex = interactiveMutex;

        public void Dispose()
        {
            var interactive = Interlocked.Exchange(ref _interactiveMutex, null);
            var legacy = Interlocked.Exchange(ref _legacyMutex, null);
            if (interactive is null && legacy is null)
            {
                return;
            }

            try
            {
                interactive?.ReleaseMutex();
            }
            finally
            {
                interactive?.Dispose();
                try
                {
                    legacy?.ReleaseMutex();
                }
                finally
                {
                    legacy?.Dispose();
                }
            }
        }
    }
}

public sealed class MachineWideDatabaseResetConfirmationRequiredException : InvalidOperationException
{
    public MachineWideDatabaseResetConfirmationRequiredException()
        : base($"{MachineWideBusinessDatabaseResetService.ConfirmationText} Escriba {MachineWideBusinessDatabaseResetService.ConfirmationPhrase} para continuar.")
    {
    }
}
