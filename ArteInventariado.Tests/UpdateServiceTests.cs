using System.IO.Compression;
using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;

namespace ArteInventariado.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task Status_IgnoresHistoricalRollbackWhenCurrentAlreadyPointsToTarget()
    {
        using var temporary = new TemporaryDirectory();
        var paths = UpdateRuntimePaths.Create(Path.Combine(temporary.Path, "app-data"));
        var currentExecutable = Path.Combine(temporary.Path, "Versions", "1.14.1", "ArteInventariado.WinForms.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(currentExecutable)!);
        await File.WriteAllTextAsync(currentExecutable, "current");
        new UpdatePointerStore(paths.CurrentPointerPath).Save(
            "1.14.1",
            currentExecutable,
            null,
            DateTimeOffset.UtcNow);

        new UpdateStateStore(paths.StatePath).Save(new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.Failed,
            "1.14.0",
            "1.14.1",
            "old-package.zip",
            "old-manifest.json",
            "old-pre-update.aibak",
            null,
            currentExecutable,
            "Rollback completado; la versión anterior vuelve a ser CURRENT.",
            DateTimeOffset.UtcNow,
            currentExecutable));

        var status = await CreateService(
            paths,
            new UpdateApplicationInfo("1.14.1", currentExecutable, Path.Combine(temporary.Path, "ArteInventariado.Updater.exe")))
            .GetStatusAsync();

        Assert.Equal(UpdateState.Idle, status.State);
        Assert.Equal(UpdatePreparationStage.None, status.PreparationStage);
        Assert.Null(status.AvailableVersion);
        Assert.False(status.CanInstall);
    }

    [Fact]
    public async Task Status_PrefersNewStagingOverPreviousSuccessWithoutChangingCanonicalState()
    {
        using var temporary = new TemporaryDirectory();
        var provider = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(temporary.Path, "install"),
            Path.Combine(temporary.Path, "data"),
            Path.Combine(temporary.Path, "user"));
        var paths = UpdateRuntimePaths.Create(provider);
        var sourceExecutable = Path.Combine(paths.VersionsDirectory, "1.14.1", "ArteInventariado.WinForms.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceExecutable)!);
        await File.WriteAllTextAsync(sourceExecutable, "source");
        new UpdatePointerStore(paths.CurrentPointerPath).Save(
            "1.14.1",
            sourceExecutable,
            null,
            DateTimeOffset.UtcNow);
        var previousSuccess = new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.Success,
            "1.14.0",
            "1.14.1",
            "previous-package.zip",
            "previous-manifest.json",
            "previous-PRE_UPDATE.aibak",
            null,
            sourceExecutable,
            null,
            DateTimeOffset.UtcNow,
            sourceExecutable);
        new UpdateStateStore(paths.StatePath).Save(previousSuccess);

        var package = Path.Combine(temporary.Path, "ArteInventariado-1.14.2-win-x64.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var executable = archive.CreateEntry("ArteInventariado.WinForms.exe");
            await using var source = File.OpenRead(typeof(UpdateServiceTests).Assembly.Location);
            await using var destination = executable.Open();
            await source.CopyToAsync(destination);
        }

        var manifestPath = Path.Combine(temporary.Path, "manifest.json");
        var manifest = new UpdateManifest(
            "1.14.2",
            "1.14.1",
            null,
            Path.GetFileName(package),
            await UpdatePackageValidator.ComputeSha256Async(package),
            new FileInfo(package).Length,
            "Prueba de staging sin PRE_UPDATE",
            DeploymentTarget: UpdatePackageTargets.MachineWide);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        await new MachineWideUpdateStagingService().StageAsync(
            paths,
            manifestPath,
            package,
            "1.14.1");

        var status = await CreateService(
            paths,
            new UpdateApplicationInfo("1.14.1", sourceExecutable, provider.UpdaterPath))
            .GetStatusAsync();

        Assert.Equal(UpdateState.Preparing, status.State);
        Assert.Equal(UpdatePreparationStage.Staged, status.PreparationStage);
        Assert.Equal("1.14.2", status.AvailableVersion);
        Assert.False(status.CanInstall);
        Assert.NotNull(status.StagingPath);
        Assert.Equal(previousSuccess, new UpdateStateStore(paths.StatePath).Load());
        Assert.True(status.TransactionId.HasValue);
        Assert.NotEqual(Guid.Empty, status.TransactionId.Value);
    }

    [Fact]
    public async Task Status_ReadyWithoutPreUpdateCannotBeInstalled()
    {
        using var temporary = new TemporaryDirectory();
        var paths = UpdateRuntimePaths.Create(Path.Combine(temporary.Path, "app-data"));
        var sourceExecutable = Path.Combine(temporary.Path, "published", "ArteInventariado.WinForms.exe");
        var updaterExecutable = Path.Combine(temporary.Path, "published", "ArteInventariado.Updater.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceExecutable)!);
        await File.WriteAllTextAsync(sourceExecutable, "source");
        await File.WriteAllTextAsync(updaterExecutable, "updater");
        var stationStore = new LocalStationConfigurationStore(paths.StationSettingsPath);
        stationStore.SaveConfigured(InstallationMode.Principal);
        var status = stationStore.Load().Status;
        new UpdateStateStore(paths.StatePath).Save(new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.ReadyToInstall,
            "1.14.1",
            "1.14.2",
            Path.Combine(temporary.Path, "package.zip"),
            Path.Combine(temporary.Path, "manifest.json"),
            Path.Combine(temporary.Path, "missing-pre-update.aibak"),
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            sourceExecutable,
            status.InstallationId,
            status.Mode,
            new string('A', 64),
            false,
            Path.Combine(paths.Root, "StationSnapshots", "snapshot.json"),
            Guid.NewGuid(),
            UpdatePreparationStage.ReadyToInstall));

        var updateStatus = await CreateService(
            paths,
            new UpdateApplicationInfo("1.14.1", sourceExecutable, updaterExecutable))
            .GetStatusAsync();

        Assert.Equal(UpdatePreparationStage.ReadyToInstall, updateStatus.PreparationStage);
        Assert.False(updateStatus.CanInstall);
        Assert.Contains("PRE_UPDATE", updateStatus.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Prepare_WhenPreUpdateFails_DoesNotBecomeReadyOrInstallAnything()
    {
        using var temporary = new TemporaryDirectory();
        var paths = UpdateRuntimePaths.Create(Path.Combine(temporary.Path, "app-data"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
        }

        var stationStore = new LocalStationConfigurationStore(paths.StationSettingsPath);
        stationStore.SaveConfigured(InstallationMode.Principal);
        var status = stationStore.Load().Status;
        var sourceExecutable = Path.Combine(temporary.Path, "published", "ArteInventariado.WinForms.exe");
        var updaterExecutable = Path.Combine(temporary.Path, "published", "ArteInventariado.Updater.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceExecutable)!);
        await File.WriteAllTextAsync(sourceExecutable, "source");
        await File.WriteAllTextAsync(updaterExecutable, "updater");
        var package = Path.Combine(temporary.Path, "ArteInventariado-1.11.0-win-x64.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var executable = archive.CreateEntry("ArteInventariado.WinForms.exe");
            await using var writer = new StreamWriter(executable.Open());
            await writer.WriteAsync("package executable");
        }

        var manifestPath = Path.Combine(temporary.Path, "manifest.json");
        var manifest = new UpdateManifest(
            "1.11.0",
            "1.10.0",
            null,
            Path.GetFileName(package),
            await UpdatePackageValidator.ComputeSha256Async(package),
            new FileInfo(package).Length,
            "Prueba de fallo PRE_UPDATE");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var stateStore = new UpdateStateStore(paths.StatePath);
        var service = new UpdateService(
            new FailingBackupService(),
            new TestInstallationContext(status),
            new AdminSession(),
            new NoPendingRestore(),
            new UpdatePackageValidator(),
            stateStore,
            new UpdateBackupService(),
            new UpdateStationGuard(),
            paths,
            TimeProvider.System,
            new UpdateOperationalPreflight(),
            new UpdateApplicationInfo("1.10.0", sourceExecutable, updaterExecutable),
            new UpdateEventLog(paths.LogDirectory));

        await Assert.ThrowsAsync<IOException>(() => service.PrepareAsync(manifestPath, package));

        var state = stateStore.Load()!;
        Assert.Equal(UpdateState.Failed, state.State);
        Assert.Contains("PRE_UPDATE simulado", state.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(state.StationSettingsBackupPath));
        Assert.False(Directory.Exists(paths.GetVersionDirectory("1.11.0")));
    }

    private sealed class FailingBackupService : IBackupMaintenanceService
    {
        public Task<BackupResult> CreatePreUpdateBackupAsync(
            string targetVersion,
            IProgress<BackupProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Fallo PRE_UPDATE simulado.");

        public Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BackupSettings> UpdateSettingsAsync(UpdateBackupSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BackupResult> CreateBackupAsync(CreateBackupRequest request, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BackupResult?> RunAutomaticBackupIfDueAsync(IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BackupValidationResult> ValidateBackupAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BackupHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PrepareRestoreResult> PrepareRestoreAsync(PrepareRestoreRequest request, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static UpdateService CreateService(
        UpdateRuntimePaths paths,
        UpdateApplicationInfo applicationInfo)
    {
        var stationStore = new LocalStationConfigurationStore(paths.StationSettingsPath);
        if (!stationStore.Load().Status.IsConfigured)
        {
            stationStore.SaveConfigured(InstallationMode.Principal);
        }

        return new UpdateService(
            new FailingBackupService(),
            new TestInstallationContext(stationStore.Load().Status),
            new AdminSession(),
            new NoPendingRestore(),
            new UpdatePackageValidator(),
            new UpdateStateStore(paths.StatePath),
            new UpdateBackupService(),
            new UpdateStationGuard(),
            paths,
            TimeProvider.System,
            new UpdateOperationalPreflight(),
            applicationInfo,
            new UpdateEventLog(paths.LogDirectory));
    }

    private sealed class TestInstallationContext(InstallationStatus status) : IInstallationContext
    {
        public InstallationStatus Status { get; } = status;
        public bool AllowsOperationalWrites => Status.AllowsOperationalWrites;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) { }
    }

    private sealed class AdminSession : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; }
        public bool EstaAutenticado => true;
        public bool EsAdmin => true;
        public bool TieneRol(RolUsuario rol) => rol == RolUsuario.ADMIN;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class NoPendingRestore : IPendingRestoreProcessor
    {
        public PendingRestoreStatus GetStatus() => new(false, null, null, null);
        public RestoreStartupResult ProcessPendingRestore() => new(false, true, false, "Sin restore pendiente.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"arte-update-service-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
