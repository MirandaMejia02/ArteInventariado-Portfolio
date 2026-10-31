using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class MachineWideBootstrapTests
{
    [Fact]
    public async Task CleanInstall_CreatesMachineWideRootsWithoutBusinessData()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();

        var result = await new MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Target);

        Assert.Equal(MachineWideBootstrapOutcome.CleanInstallPrepared, result.Outcome);
        Assert.All(
            new[]
            {
                fixture.Target.MachineInstallRoot,
                fixture.Target.VersionsRoot,
                fixture.Target.MachineDataRoot,
                fixture.Target.DataRoot,
                fixture.Target.AssetsRoot,
                fixture.Target.BusinessAssetsRoot,
                fixture.Target.StationRoot,
                fixture.Target.UpdateStateRoot,
                fixture.Target.LogsRoot,
                fixture.Target.MaintenanceRoot,
                fixture.Target.ProtectedBackupsRoot,
                fixture.Target.CacheRoot,
                fixture.Target.ThumbnailRoot
            },
            path => Assert.True(Directory.Exists(path), path));
        Assert.False(File.Exists(fixture.Target.DatabasePath));
        Assert.False(File.Exists(fixture.Target.CurrentStatePath));
    }

    [Fact]
    public async Task CleanInstall_PublishedPayloadInstallsCurrentAndStableEntries()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var payload = fixture.CreatePublishedPayload();

        var result = await new MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Target, payload);

        Assert.Equal(MachineWideBootstrapOutcome.CleanInstallPrepared, result.Outcome);
        Assert.True(File.Exists(fixture.Target.LauncherPath));
        Assert.True(File.Exists(fixture.Target.UpdaterPath));
        var pointer = new UpdatePointerStore(fixture.Target.CurrentStatePath).Load();
        Assert.NotNull(pointer);
        Assert.Equal(fixture.Version, pointer!.Version);
        Assert.Equal(
            fixture.Target.GetVersionDirectory(fixture.Version),
            Path.GetDirectoryName(pointer.ExecutablePath));
        Assert.True(File.Exists(pointer.ExecutablePath));
        Assert.True(File.Exists(fixture.Target.StationHelperPath));
    }

    [Fact]
    public async Task CleanInstall_SameVersionAndSamePayload_IsAlreadyReady()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var payload = fixture.CreatePublishedPayload();

        var first = await new MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Target, payload);
        var second = await new MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Target, payload);

        Assert.Equal(MachineWideBootstrapOutcome.CleanInstallPrepared, first.Outcome);
        Assert.Equal(MachineWideBootstrapOutcome.AlreadyReady, second.Outcome);
        Assert.Equal(
            fixture.Version,
            new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()!.Version);
    }

    [Fact]
    public async Task CleanInstall_SameVersionDifferentPayload_RejectsConflictWithoutReplacingVersion()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var payload = fixture.CreatePublishedPayload();

        await new MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Target, payload);
        var installedVersionDirectory = fixture.Target.GetVersionDirectory(fixture.Version);
        var installedMarker = Path.Combine(installedVersionDirectory, "payload-only.marker");
        File.WriteAllText(installedMarker, "installed");

        File.WriteAllText(Path.Combine(payload, "payload-only.marker"), "different");

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            new MachineWideBootstrapService().PrepareCleanInstallAsync(fixture.Target, payload));

        Assert.Contains("no coincide", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("installed", await File.ReadAllTextAsync(installedMarker));
        Assert.Equal(
            fixture.Version,
            new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()!.Version);
    }

    [Fact]
    public async Task CleanInstall_PromotesCompleteVersionBeforePublishingCurrent()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        const string previousVersion = "1.11.0";
        var previousDirectory = fixture.Target.GetVersionDirectory(previousVersion);
        Directory.CreateDirectory(previousDirectory);
        var previousExecutable = Path.Combine(
            previousDirectory,
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        File.Copy(
            typeof(MachineWideBootstrapService).Assembly.Location,
            previousExecutable);
        new UpdatePointerStore(fixture.Target.CurrentStatePath).Save(
            previousVersion,
            previousExecutable,
            null,
            DateTimeOffset.UtcNow);

        var payload = fixture.CreatePublishedPayload();
        var promotedVersionDirectory = fixture.Target.GetVersionDirectory(fixture.Version);
        string? pointerObservedDuringPromotion = null;
        string? pointerObservedBeforePromotion = null;
        var versionWasAbsentBeforePromotion = false;
        var versionWasCompleteDuringPromotion = false;
        var phases = new List<string>();
        var service = new MachineWideBootstrapService(phase =>
        {
            phases.Add(phase);
            if (phase == "pointer-staged")
            {
                pointerObservedBeforePromotion =
                    new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()?.Version;
                versionWasAbsentBeforePromotion = !Directory.Exists(promotedVersionDirectory);
            }

            if (phase == "versions-promoted")
            {
                pointerObservedDuringPromotion =
                    new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()?.Version;
                var expectedFiles = Directory.GetFiles(payload, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(payload, path))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var actualFiles = Directory.GetFiles(promotedVersionDirectory, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(promotedVersionDirectory, path))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                versionWasCompleteDuringPromotion =
                    expectedFiles.SequenceEqual(actualFiles, StringComparer.OrdinalIgnoreCase);
            }
        });

        await service.PrepareCleanInstallAsync(fixture.Target, payload);

        Assert.Contains("versions-promoted", phases);
        Assert.Contains("pointer-staged", phases);
        Assert.True(versionWasAbsentBeforePromotion);
        Assert.Equal(previousVersion, pointerObservedBeforePromotion);
        Assert.True(versionWasCompleteDuringPromotion);
        Assert.Equal(previousVersion, pointerObservedDuringPromotion);
        Assert.Equal(
            fixture.Version,
            new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()!.Version);
        Assert.True(Directory.Exists(previousDirectory));
        Assert.True(Directory.Exists(promotedVersionDirectory));
    }

    [Fact]
    public async Task CleanInstall_FailureAfterPromotionRestoresPreviousPointerAndRemovesNewVersion()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        const string previousVersion = "1.11.0";
        var previousDirectory = fixture.Target.GetVersionDirectory(previousVersion);
        Directory.CreateDirectory(previousDirectory);
        var previousExecutable = Path.Combine(
            previousDirectory,
            UpdateLaunchTargetResolver.ApplicationExecutableName);
        File.Copy(
            typeof(MachineWideBootstrapService).Assembly.Location,
            previousExecutable);
        new UpdatePointerStore(fixture.Target.CurrentStatePath).Save(
            previousVersion,
            previousExecutable,
            null,
            DateTimeOffset.UtcNow);

        var payload = fixture.CreatePublishedPayload();
        var promotedVersionDirectory = fixture.Target.GetVersionDirectory(fixture.Version);
        var service = new MachineWideBootstrapService(phase =>
        {
            if (phase == "state-promoted")
            {
                throw new InvalidOperationException("synthetic post-promotion failure");
            }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrepareCleanInstallAsync(fixture.Target, payload));

        var restored = new UpdatePointerStore(fixture.Target.CurrentStatePath).Load();
        Assert.NotNull(restored);
        Assert.Equal(previousVersion, restored!.Version);
        Assert.True(Directory.Exists(previousDirectory));
        Assert.False(Directory.Exists(promotedVersionDirectory));
        Assert.False(File.Exists(fixture.Target.BootstrapStatePath));
    }

    [Fact]
    public async Task CleanInstall_DoesNotReturnAlreadyReadyForStaleMarkerOrMissingTask()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var payload = fixture.CreatePublishedPayload();
        // Include the protected runner so the readiness predicate must query
        // Task Scheduler rather than accepting the marker alone.
        File.Copy(
            typeof(MachineWideBootstrapService).Assembly.Location,
            Path.Combine(payload, ArteInventariadoPathProvider.BackupRunnerFileName));

        var first = await new MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Target, payload);
        Assert.Equal(MachineWideBootstrapOutcome.CleanInstallPrepared, first.Outcome);

        // Reproduce the PC1 symptom: the marker names an older payload while
        // the requested payload is the newer prerelease.
        var markerPath = fixture.Target.BootstrapStatePath;
        var markerJson = await File.ReadAllTextAsync(markerPath);
        await File.WriteAllTextAsync(
            markerPath,
            markerJson.Replace(fixture.Version, "1.12.0-ib2", StringComparison.Ordinal));

        var second = await new MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Target, payload);

        Assert.NotEqual(MachineWideBootstrapOutcome.AlreadyReady, second.Outcome);
        Assert.Equal(fixture.Version, new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()!.Version);
        Assert.Contains(
            "currentVersion",
            await File.ReadAllTextAsync(fixture.Target.BootstrapStatePath),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_PreservesDatabaseStationAssetsCurrentAndSkipsCache()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await fixture.AddSourceFilesAsync();
        var legacyStationStore = new LocalStationConfigurationStore(fixture.Legacy);
        legacyStationStore.SavePosSettings(new PosStationSettingsDocument(true));
        legacyStationStore.SaveBackupSettings(new BackupStationSettingsDocument(
            true,
            fixture.Legacy.ProtectedBackupsRoot,
            "Daily",
            3,
            null,
            null));
        var stationId = fixture.StationId;

        var result = await new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, fixture.Target);

        Assert.Equal(MachineWideBootstrapOutcome.Migrated, result.Outcome);
        Assert.Equal(stationId, result.InstallationId);
        Assert.Equal(InstallationMode.Principal, result.StationMode);
        await using (var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Target.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString()))
        {
            await database.OpenAsync();
            await using var query = database.CreateCommand();
            query.CommandText = "SELECT nombre FROM productos WHERE codigo_interno = 'C1-001';";
            Assert.Equal("Producto C1", Convert.ToString(await query.ExecuteScalarAsync()));
        }
        var station = new LocalStationConfigurationStore(fixture.Target.StationSettingsPath).Load().Status;
        Assert.Equal(stationId, station.InstallationId);
        Assert.Equal(InstallationMode.Principal, station.Mode);
        using (var core = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Target.StationSettingsPath)))
        {
            Assert.False(core.RootElement.TryGetProperty("pos", out _));
            Assert.Equal(stationId, core.RootElement.GetProperty("installationId").GetGuid());
        }
        using (var operational = JsonDocument.Parse(
                   await File.ReadAllTextAsync(fixture.Target.StationOperationalSettingsPath)))
        {
            Assert.True(operational.RootElement.GetProperty("pos").GetProperty("quickPosEnabled").GetBoolean());
            Assert.False(operational.RootElement.TryGetProperty("installationId", out _));
            Assert.Equal(
                Path.GetFullPath(fixture.Target.ProtectedBackupsRoot),
                operational.RootElement.GetProperty("backup").GetProperty("destinationDirectory").GetString());
        }
        Assert.Equal(fixture.Version, new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()!.Version);
        Assert.Equal("business-asset", await File.ReadAllTextAsync(
            Path.Combine(fixture.Target.ProductBusinessAssetsRoot, "business.txt")));
        Assert.Equal("managed-asset", await File.ReadAllTextAsync(
            Path.Combine(fixture.Target.AssetsRoot, "Products", "managed.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.Target.MachineDataRoot, "Cache", "Thumbnails", "thumb.tmp")));
        Assert.False(File.Exists(Path.Combine(fixture.Target.MachineDataRoot, "Logs", "legacy.log")));
        Assert.False(File.Exists(Path.Combine(fixture.Target.UserDataRoot, "Cache", "Thumbnails", "thumb.tmp")));
        Assert.True(File.Exists(fixture.Legacy.DatabasePath));
        Assert.True(File.Exists(fixture.Legacy.StationSettingsPath));
    }

    [Fact]
    public async Task Migration_UpgradesValidOlderDatabaseBeforePromotion()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await fixture.DowngradeToPreEmployeeActivityAsync();

        var result = await new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, fixture.Target);

        Assert.Equal(MachineWideBootstrapOutcome.Migrated, result.Outcome);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Target.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var tableCheck = connection.CreateCommand();
        tableCheck.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('employee_login_events', 'employee_payments');";
        Assert.Equal(2L, Convert.ToInt64(await tableCheck.ExecuteScalarAsync()));

        await using var barcodeCheck = connection.CreateCommand();
        barcodeCheck.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('producto_sku_barcodes') WHERE name = 'simbologia';";
        Assert.Equal(1L, Convert.ToInt64(await barcodeCheck.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migration_WithPublishedPayloadPromotesPayloadContentsInsteadOfLegacyVersionDirectory()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var payload = fixture.CreatePublishedPayload();
        await File.WriteAllTextAsync(Path.Combine(payload, "payload-only.marker"), "published");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Legacy.GetVersionDirectory(fixture.Version), "legacy-only.marker"),
            "legacy");

        var result = await new MachineWideBootstrapService()
            .MigrateLegacyAsync(
                fixture.Legacy,
                fixture.Target,
                publishedPayloadDirectory: payload);

        Assert.Equal(MachineWideBootstrapOutcome.Migrated, result.Outcome);
        var promoted = fixture.Target.GetVersionDirectory(fixture.Version);
        Assert.Equal("published", await File.ReadAllTextAsync(Path.Combine(promoted, "payload-only.marker")));
        Assert.False(File.Exists(Path.Combine(promoted, "legacy-only.marker")));
        Assert.Equal(
            fixture.Version,
            new UpdatePointerStore(fixture.Target.CurrentStatePath).Load()!.Version);
    }

    [Fact]
    public async Task Migration_CanonicalizesRecognizedLegacyProductAssetPathIdempotently()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await fixture.AddSourceFilesAsync();
        var legacyAsset = Path.Combine(fixture.Legacy.ProductBusinessAssetsRoot, "business.txt");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Legacy.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE productos SET imagen_local_path = $path WHERE codigo_interno = 'C1-001';";
            update.Parameters.AddWithValue("$path", legacyAsset);
            await update.ExecuteNonQueryAsync();
        }

        var first = await new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, fixture.Target);

        Assert.Equal(MachineWideBootstrapOutcome.Migrated, first.Outcome);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Target.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT imagen_local_path FROM productos WHERE codigo_interno = 'C1-001';";
            Assert.Equal(
                Path.Combine(fixture.Target.ProductBusinessAssetsRoot, "business.txt"),
                Convert.ToString(await query.ExecuteScalarAsync()));
        }

        Assert.Equal(legacyAsset, await ReadProductImagePathAsync(fixture.Legacy.DatabasePath));
        var second = await new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, fixture.Target);
        Assert.Equal(MachineWideBootstrapOutcome.AlreadyReady, second.Outcome);
    }

    private static async Task<string?> ReadProductImagePathAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT imagen_local_path FROM productos WHERE codigo_interno = 'C1-001';";
        return Convert.ToString(await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration_CopiesValidBackupsAndReportsInvalidOnes()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var valid = await fixture.CreateValidBackupAsync("valid.aibak");
        var invalid = Path.Combine(fixture.Legacy.ProtectedBackupsRoot, "invalid.aibak");
        await File.WriteAllTextAsync(invalid, "not an ArteInventariado backup");

        var result = await new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, fixture.Target);

        Assert.Contains(Path.GetFileName(valid), result.CopiedFiles.Select(Path.GetFileName));
        Assert.Contains("invalid.aibak", result.IgnoredBackups);
        Assert.True(File.Exists(Path.Combine(fixture.Target.ProtectedBackupsRoot, "valid.aibak")));
        Assert.False(File.Exists(Path.Combine(fixture.Target.ProtectedBackupsRoot, "invalid.aibak")));
    }

    [Fact]
    public async Task Migration_IsIdempotentAndKeepsLegacySourceIntact()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await fixture.AddSourceFilesAsync();
        var sourceHash = await BackupPackageEngine.ComputeSha256Async(fixture.Legacy.DatabasePath, CancellationToken.None);

        var first = await new MachineWideBootstrapService().MigrateLegacyAsync(fixture.Legacy, fixture.Target);
        var second = await new MachineWideBootstrapService().MigrateLegacyAsync(fixture.Legacy, fixture.Target);

        Assert.Equal(MachineWideBootstrapOutcome.Migrated, first.Outcome);
        Assert.Equal(MachineWideBootstrapOutcome.AlreadyReady, second.Outcome);
        Assert.Equal(sourceHash, await BackupPackageEngine.ComputeSha256Async(fixture.Legacy.DatabasePath, CancellationToken.None));
        Assert.True(File.Exists(fixture.Legacy.CurrentStatePath));
        Assert.True(File.Exists(fixture.Legacy.LauncherPath));
        Assert.Equal(fixture.StationId, second.InstallationId);
    }

    [Fact]
    public async Task Migration_CancellationAfterStagingLeavesNoPointerAndCanRetry()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await fixture.AddSourceFilesAsync();
        var service = new MachineWideBootstrapService(
            new BackupPackageEngine(),
            TimeProvider.System,
            phase =>
            {
                if (phase == "staged")
                {
                    fixture.Cancellation.Cancel();
                }
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MigrateLegacyAsync(
            fixture.Legacy,
            fixture.Target,
            cancellationToken: fixture.Cancellation.Token));
        Assert.False(File.Exists(fixture.Target.CurrentStatePath));
        Assert.False(File.Exists(fixture.Target.BootstrapStatePath));
        Assert.True(File.Exists(fixture.Legacy.DatabasePath));

        var retry = await new MachineWideBootstrapService().MigrateLegacyAsync(fixture.Legacy, fixture.Target);
        Assert.Equal(MachineWideBootstrapOutcome.Migrated, retry.Outcome);
    }

    [Fact]
    public async Task Migration_RejectsConflictingStationIdentityWithoutTouchingSource()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var other = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.Target.MachineInstallRoot,
            fixture.Target.MachineDataRoot,
            fixture.Target.UserDataRoot);
        Directory.CreateDirectory(other.StationRoot);
        var conflicting = new LocalStationConfigurationStore(other.StationSettingsPath);
        conflicting.SaveConfigured(InstallationMode.Consulta);

        await Assert.ThrowsAsync<InvalidDataException>(() => new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, fixture.Target));
        Assert.True(File.Exists(fixture.Legacy.DatabasePath));
        Assert.False(File.Exists(fixture.Target.CurrentStatePath));
    }

    [Fact]
    public async Task Migration_UsesSqliteBackupSnapshotForWalDatabase()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Legacy.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var enableWal = connection.CreateCommand();
            enableWal.CommandText = "PRAGMA journal_mode=WAL;";
            await enableWal.ExecuteScalarAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE IF NOT EXISTS c1_wal_probe (id INTEGER PRIMARY KEY, value TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO c1_wal_probe(value) VALUES ('wal-ok');";
            await insert.ExecuteNonQueryAsync();

            var result = await new MachineWideBootstrapService().MigrateLegacyAsync(fixture.Legacy, fixture.Target);
            Assert.Equal(MachineWideBootstrapOutcome.Migrated, result.Outcome);
        }

        await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Target.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await verify.OpenAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = "SELECT value FROM c1_wal_probe;";
        Assert.Equal("wal-ok", Convert.ToString(await query.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migration_RefusesIncompleteUpdateAndPreservesLegacy()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        new UpdateStateStore(fixture.Legacy.UpdateStatePath).Save(new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.Verifying,
            fixture.Version,
            fixture.Version,
            "package.zip",
            "manifest.json",
            null,
            null,
            null,
            "interrupted",
            DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidDataException>(() => new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, fixture.Target));
        Assert.True(File.Exists(fixture.Legacy.DatabasePath));
        Assert.True(File.Exists(fixture.Legacy.UpdateStatePath));
        Assert.False(File.Exists(fixture.Target.CurrentStatePath));
    }

    [Fact]
    public async Task Migration_RewritesManagedUpdateStatePathsToTheNewLayout()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var package = Path.Combine(fixture.Legacy.UpdateStateRoot, "Packages", "old.zip");
        var manifest = Path.Combine(fixture.Legacy.UpdateStateRoot, "Packages", "old.json");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        await File.WriteAllTextAsync(package, "package");
        await File.WriteAllTextAsync(manifest, "manifest");
        var current = new UpdatePointerStore(fixture.Legacy.CurrentStatePath).Load()!;
        new UpdateStateStore(fixture.Legacy.UpdateStatePath).Save(new UpdateStateDocument(
            UpdateStateStore.CurrentSchemaVersion,
            UpdateState.Success,
            fixture.Version,
            fixture.Version,
            package,
            manifest,
            null,
            current.ExecutablePath,
            current.ExecutablePath,
            null,
            DateTimeOffset.UtcNow,
            current.ExecutablePath,
            fixture.StationId,
            InstallationMode.Principal,
            "station-hash"));

        await new MachineWideBootstrapService().MigrateLegacyAsync(fixture.Legacy, fixture.Target);

        var migrated = new UpdateStateStore(fixture.Target.UpdateStatePath).Load();
        Assert.NotNull(migrated);
        var paths = new[]
        {
            migrated!.PackagePath,
            migrated.ManifestPath,
            migrated.PreviousVersionPath,
            migrated.CurrentVersionPath,
            migrated.SourceApplicationPath
        };
        Assert.All(paths.Where(path => !string.IsNullOrWhiteSpace(path)), path =>
            Assert.DoesNotContain(fixture.Legacy.MachineDataRoot, path!, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(fixture.Target.UpdateStateRoot, "Packages", "old.zip")));
        Assert.True(File.Exists(Path.Combine(fixture.Target.UpdateStateRoot, "Packages", "old.json")));
    }

    [Fact]
    public async Task Migration_RejectsOverlappingRootsBeforeCopying()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        var overlapping = ArteInventariadoPathProvider.CreateMachineWide(
            Path.Combine(fixture.Legacy.MachineDataRoot, "machine-install"),
            Path.Combine(fixture.Temp.Path, "machine-data"),
            Path.Combine(fixture.Temp.Path, "user-data"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new MachineWideBootstrapService()
            .MigrateLegacyAsync(fixture.Legacy, overlapping));
        Assert.True(File.Exists(fixture.Legacy.DatabasePath));
    }

    private sealed class BootstrapFixture : IAsyncDisposable
    {
        private readonly string _sourceAssembly = typeof(MachineWideBootstrapService).Assembly.Location;
        public TemporaryDirectory Temp { get; } = new();
        public ArteInventariadoPathProvider Legacy { get; }
        public ArteInventariadoPathProvider Target { get; }
        public string Version { get; }
        public Guid StationId { get; private set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public IReadOnlyList<string> Migrations { get; private set; } = [];

        private BootstrapFixture()
        {
            Legacy = ArteInventariadoPathProvider.CreateLegacy(Path.Combine(Temp.Path, "legacy"));
            Target = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(Temp.Path, "machine-install"),
                Path.Combine(Temp.Path, "machine-data"),
                Path.Combine(Temp.Path, "user-data"));
            Version = UpdateVersion.Sanitize(
                FileVersionInfo.GetVersionInfo(_sourceAssembly).ProductVersion ?? "1.11.0");
        }

        public static async Task<BootstrapFixture> CreateAsync()
        {
            var fixture = new BootstrapFixture();
            Directory.CreateDirectory(fixture.Legacy.DataRoot);
            var station = new LocalStationConfigurationStore(fixture.Legacy.StationSettingsPath);
            station.SaveConfigured(InstallationMode.Principal);
            fixture.StationId = station.Load().Status.InstallationId!.Value;
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = fixture.Legacy.DatabasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false,
                    ForeignKeys = true
                }.ToString())
                .Options;
            await using (var context = new AppDbContext(options))
            {
                await context.Database.MigrateAsync();
                var category = new Categoria { Nombre = "C1", Activa = true, FechaCreacion = DateTime.UtcNow };
                context.Categorias.Add(category);
                context.Productos.Add(new Producto
                {
                    CodigoInterno = "C1-001",
                    Nombre = "Producto C1",
                    Categoria = category,
                    PrecioCompraCentavos = 100,
                    PrecioVentaCentavos = 250,
                    StockActual = 1,
                    StockMinimo = 0,
                    Estado = EstadoProducto.ACTIVO,
                    FechaCreacion = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
                fixture.Migrations = context.Database.GetMigrations().ToArray();
            }

            fixture.CreateLegacyDeployment();
            return fixture;
        }

        public string CreatePublishedPayload()
        {
            var payload = Path.Combine(Temp.Path, "published-payload");
            Directory.CreateDirectory(payload);
            File.Copy(_sourceAssembly, Path.Combine(payload, UpdateLaunchTargetResolver.ApplicationExecutableName));
            File.Copy(_sourceAssembly, Path.Combine(payload, StableDeploymentBootstrap.LauncherExecutableName));
            File.Copy(_sourceAssembly, Path.Combine(payload, StableDeploymentBootstrap.UpdaterExecutableName));
            File.Copy(_sourceAssembly, Path.Combine(payload, ArteInventariadoPathProvider.StationHelperFileName));
            File.WriteAllText(Path.Combine(payload, "ArteInventariado.Infrastructure.dll"), "payload");
            return payload;
        }

        public async Task AddSourceFilesAsync()
        {
            Directory.CreateDirectory(Path.Combine(Legacy.AssetsRoot, "Products"));
            Directory.CreateDirectory(Path.Combine(Legacy.ProductBusinessAssetsRoot));
            await File.WriteAllTextAsync(Path.Combine(Legacy.AssetsRoot, "Products", "managed.txt"), "managed-asset");
            await File.WriteAllTextAsync(Path.Combine(Legacy.ProductBusinessAssetsRoot, "business.txt"), "business-asset");
            Directory.CreateDirectory(Legacy.ThumbnailRoot);
            await File.WriteAllTextAsync(Path.Combine(Legacy.ThumbnailRoot, "thumb.tmp"), "cache");
            Directory.CreateDirectory(Legacy.LogsRoot);
            await File.WriteAllTextAsync(Path.Combine(Legacy.LogsRoot, "legacy.log"), "log");
        }

        public async Task DowngradeToPreEmployeeActivityAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Legacy.DatabasePath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                    ForeignKeys = true
                }.ToString())
                .Options;
            await using var context = new AppDbContext(options);
            await context.Database.MigrateAsync("20260825055759_V110OperationalSkuIntegrations");
        }

        public async Task<string> CreateValidBackupAsync(string name)
        {
            Directory.CreateDirectory(Legacy.ProtectedBackupsRoot);
            var engine = new BackupPackageEngine();
            var snapshot = Path.Combine(Temp.Path, $"{name}.db");
            await engine.CreateSnapshotAsync(Legacy.DatabasePath, snapshot, CancellationToken.None);
            var applied = await engine.ReadAppliedMigrationsAsync(snapshot, CancellationToken.None);
            var hash = await BackupPackageEngine.ComputeSha256Async(snapshot, CancellationToken.None);
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                Version,
                "sqlite",
                applied,
                applied.LastOrDefault(),
                hash,
                new FileInfo(snapshot).Length,
                StationId,
                InstallationMode.Principal,
                BackupKind.Manual,
                []);
            var staging = Path.Combine(Temp.Path, $"staging-{name}");
            Directory.CreateDirectory(staging);
            var package = Path.Combine(Legacy.ProtectedBackupsRoot, name);
            await engine.CreatePackageAsync(staging, snapshot, manifest, package, CancellationToken.None);
            return package;
        }

        private void CreateLegacyDeployment()
        {
            var versionDirectory = Legacy.GetVersionDirectory(Version);
            Directory.CreateDirectory(versionDirectory);
            File.Copy(_sourceAssembly, Path.Combine(versionDirectory, UpdateLaunchTargetResolver.ApplicationExecutableName));
            File.Copy(_sourceAssembly, Legacy.LauncherPath);
            File.Copy(_sourceAssembly, Legacy.UpdaterPath);
            new UpdatePointerStore(Legacy.CurrentStatePath).Save(
                Version,
                Path.Combine(versionDirectory, UpdateLaunchTargetResolver.ApplicationExecutableName),
                null,
                DateTimeOffset.UtcNow);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Cancellation.Dispose();
            Temp.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ArteInventariado-c1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
