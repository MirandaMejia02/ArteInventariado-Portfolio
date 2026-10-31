using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;

namespace ArteInventariado.Tests;

public sealed class UpdateInstallerTests
{
    [Theory]
    [InlineData(InstallationMode.Principal)]
    [InlineData(InstallationMode.Consulta)]
    public async Task Installer_CompletesSideBySideAndPreservesStation(InstallationMode mode)
    {
        await using var fixture = await InstallerFixture.CreateAsync(mode);
        var installer = fixture.CreateInstaller(new DelegateVerifier((_, _, _) => Task.FromResult(true)));

        var result = await installer.InstallFromStateAsync();

        Assert.Equal(0, result);
        var state = fixture.StateStore.Load()!;
        Assert.Equal(UpdateState.Success, state.State);
        Assert.False(state.DatabaseMayHaveChanged is false && string.IsNullOrWhiteSpace(state.CurrentVersionPath));
        Assert.True(File.Exists(state.CurrentVersionPath));
        Assert.False(File.Exists(state.StationSettingsBackupPath));
        var pointer = fixture.PointerStore.Load()!;
        Assert.Equal(Path.GetFullPath(state.CurrentVersionPath!), pointer.ExecutablePath);
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), pointer.PreviousExecutablePath);
        fixture.AssertStationPreserved(mode);
    }

    [Fact]
    public async Task Installer_VerificationFailureRollsBackDatabaseAssetsStationAndPointer()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var installer = fixture.CreateInstaller(new DelegateVerifier(async (_, _, cancellationToken) =>
        {
            await fixture.MutatePersistentStateAsync(cancellationToken);
            return false;
        }));

        var result = await installer.InstallFromStateAsync();

        Assert.Equal(2, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.Equal("Producto anterior", await fixture.ReadProductNameAsync());
        Assert.Equal("asset-legado-anterior", await File.ReadAllTextAsync(fixture.LegacyAssetPath));
        Assert.Equal("asset-administrado-anterior", await File.ReadAllTextAsync(fixture.ManagedAssetPath));
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(fixture.TargetVersion)));
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), fixture.PointerStore.Load()!.ExecutablePath);
        fixture.AssertStationPreserved(InstallationMode.Principal);
    }

    [Fact]
    public async Task Installer_FinalizesStableEntryPointBeforeSuccess()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var finalizer = new RecordingDeploymentFinalizer();

        var result = await fixture.CreateInstaller(
            new DelegateVerifier((_, _, _) => Task.FromResult(true)),
            stableDeploymentFinalizer: finalizer)
            .InstallFromStateAsync();

        Assert.Equal(0, result);
        Assert.Single(finalizer.VersionDirectories);
        Assert.Equal(
            fixture.Paths.GetVersionDirectory(fixture.TargetVersion),
            finalizer.VersionDirectories.Single());
        Assert.Equal(UpdateState.Success, fixture.StateStore.Load()!.State);
    }

    [Fact]
    public async Task Installer_DeploymentFinalizerFailureRollsBackPointerAndTarget()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var finalizer = new RecordingDeploymentFinalizer(throwOnFinalize: true);

        var result = await fixture.CreateInstaller(
            new DelegateVerifier((_, _, _) => Task.FromResult(true)),
            stableDeploymentFinalizer: finalizer)
            .InstallFromStateAsync();

        Assert.Equal(2, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), fixture.PointerStore.Load()!.ExecutablePath);
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(fixture.TargetVersion)));
    }

    [Theory]
    [InlineData(InstallationMode.Principal)]
    [InlineData(InstallationMode.Consulta)]
    public async Task ManualRollback_FromSuccessfulUpdateRestoresPreviousDataAssetsAndStation(InstallationMode mode)
    {
        await using var fixture = await InstallerFixture.CreateAsync(mode);
        var installer = fixture.CreateInstaller(new DelegateVerifier(async (_, _, cancellationToken) =>
        {
            await fixture.MutateBusinessStateAsync(cancellationToken);
            return true;
        }));

        var installResult = await installer.InstallFromStateAsync();
        var installedState = fixture.StateStore.Load()!;

        Assert.Equal(0, installResult);
        Assert.Equal(UpdateState.Success, installedState.State);
        Assert.Equal("Producto migrado", await fixture.ReadProductNameAsync());
        Assert.Equal("asset-legado-migrado", await File.ReadAllTextAsync(fixture.LegacyAssetPath));
        Assert.Equal("asset-administrado-migrado", await File.ReadAllTextAsync(fixture.ManagedAssetPath));

        var rollbackResult = await installer.RollbackSuccessfulAsync();

        Assert.Equal(0, rollbackResult);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.Equal("Producto anterior", await fixture.ReadProductNameAsync());
        Assert.Equal("asset-legado-anterior", await File.ReadAllTextAsync(fixture.LegacyAssetPath));
        Assert.Equal("asset-administrado-anterior", await File.ReadAllTextAsync(fixture.ManagedAssetPath));
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), fixture.PointerStore.Load()!.ExecutablePath);
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(fixture.TargetVersion)));
        fixture.AssertStationPreserved(mode);
    }

    [Fact]
    public async Task Recovery_FromInterruptedVerificationCompletesRollbackIdempotently()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Consulta);
        var state = fixture.StateStore.Load()!;
        var targetDirectory = fixture.Paths.GetVersionDirectory(fixture.TargetVersion);
        Directory.CreateDirectory(targetDirectory);
        var targetExecutable = Path.Combine(targetDirectory, "ArteInventariado.WinForms.exe");
        File.Copy(fixture.SourceExecutable, targetExecutable);
        fixture.PointerStore.Save(fixture.TargetVersion, targetExecutable, fixture.SourceExecutable, DateTimeOffset.UtcNow);
        await fixture.MutatePersistentStateAsync();
        fixture.StateStore.Save(state with
        {
            State = UpdateState.Verifying,
            PreviousVersionPath = fixture.SourceExecutable,
            CurrentVersionPath = targetExecutable,
            DatabaseMayHaveChanged = true,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        var installer = fixture.CreateInstaller(new DelegateVerifier((_, _, _) => Task.FromResult(true)));
        var first = await installer.RecoverInterruptedAsync();
        var second = await installer.RecoverInterruptedAsync();

        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.Equal("Producto anterior", await fixture.ReadProductNameAsync());
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), fixture.PointerStore.Load()!.ExecutablePath);
        Assert.False(Directory.Exists(targetDirectory));
        fixture.AssertStationPreserved(InstallationMode.Consulta);
    }

    [Fact]
    public async Task BackupService_RejectsCorruptAssetBeforeTouchingActiveDatabase()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var corrupt = await fixture.CreatePreUpdatePackageAsync(corruptManagedAsset: true);
        await fixture.SetProductNameAsync("Estado vigente");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new UpdateBackupService().RestorePreUpdateAsync(
                corrupt,
                fixture.Paths.DatabasePath,
                fixture.Paths.BusinessAssetsDirectory,
                fixture.Paths.ManagedAssetsDirectory));

        Assert.Equal("Estado vigente", await fixture.ReadProductNameAsync());
        Assert.Equal("asset-administrado-anterior", await File.ReadAllTextAsync(fixture.ManagedAssetPath));
    }

    [Fact]
    public async Task BackupService_RejectsZipSlipPreUpdate()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var unsafeBackup = await fixture.CreatePreUpdatePackageAsync(includeUnsafeEntry: true);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new UpdateBackupService().ValidatePreUpdateAsync(unsafeBackup));

        Assert.False(File.Exists(Path.Combine(fixture.Temp.Path, "escape.txt")));
    }

    [Fact]
    public async Task Installer_RejectsPackageWhenExecutableVersionDoesNotMatchManifest()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var state = fixture.StateStore.Load()!;
        const string mismatchedTargetVersion = "1.11.1";
        var mismatchedManifest = await fixture.CreateUpdatePackageAsync(mismatchedTargetVersion);
        fixture.StateStore.Save(state with
        {
            TargetVersion = mismatchedTargetVersion,
            PackagePath = mismatchedManifest.PackagePath,
            ManifestPath = mismatchedManifest.ManifestPath
        });
        var installer = fixture.CreateInstaller(new DelegateVerifier((_, _, _) => Task.FromResult(true)));

        var result = await installer.InstallFromStateAsync();

        Assert.Equal(2, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(mismatchedTargetVersion)));
    }

    [Fact]
    public async Task Installer_RevalidatesPackageHashImmediatelyBeforeExtraction()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var state = fixture.StateStore.Load()!;

        var result = await fixture.CreateInstaller(
            new DelegateVerifier((_, _, _) => Task.FromResult(true)),
            new MutatingDiskSpaceProbe(state.PackagePath))
            .InstallFromStateAsync();

        Assert.Equal(2, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(fixture.TargetVersion)));
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), fixture.PointerStore.Load()!.ExecutablePath);
    }

    [Fact]
    public async Task Installer_InsufficientSpaceStopsBeforeExtractionAndRollsBackCleanly()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var installer = fixture.CreateInstaller(
            new DelegateVerifier((_, _, _) => Task.FromResult(true)),
            new FixedDiskSpaceProbe(0));

        var result = await installer.InstallFromStateAsync();

        Assert.Equal(2, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(fixture.TargetVersion)));
        fixture.AssertStationPreserved(InstallationMode.Principal);
    }

    [Fact]
    public async Task Installer_MissingStationSettingsIsRecoveredFromProtectedSnapshot()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Consulta);
        File.Delete(fixture.Paths.StationSettingsPath);

        var result = await fixture.CreateInstaller(
            new DelegateVerifier((_, _, _) => Task.FromResult(true)))
            .InstallFromStateAsync();

        Assert.Equal(2, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        fixture.AssertStationPreserved(InstallationMode.Consulta);
    }

    [Fact]
    public async Task Recovery_ClosesInterruptedPreparingStateWithoutInstalling()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var state = fixture.StateStore.Load()!;
        fixture.StateStore.Save(state with
        {
            State = UpdateState.Preparing,
            PreUpdateBackupPath = null,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        var result = await fixture.CreateInstaller(new DelegateVerifier((_, _, _) => Task.FromResult(true)))
            .RecoverInterruptedAsync();

        Assert.Equal(0, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), fixture.PointerStore.Load()!.ExecutablePath);
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(fixture.TargetVersion)));
    }

    [Fact]
    public async Task Recovery_CleansInterruptedInstallingStateAndRestoresPreviousPointer()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var targetDirectory = fixture.Paths.GetVersionDirectory(fixture.TargetVersion);
        Directory.CreateDirectory(targetDirectory);
        var targetExecutable = Path.Combine(targetDirectory, "ArteInventariado.WinForms.exe");
        File.Copy(fixture.SourceExecutable, targetExecutable);
        fixture.PointerStore.Save(fixture.TargetVersion, targetExecutable, fixture.SourceExecutable, DateTimeOffset.UtcNow);
        var state = fixture.StateStore.Load()!;
        fixture.StateStore.Save(state with
        {
            State = UpdateState.Installing,
            PreviousVersionPath = fixture.SourceExecutable,
            CurrentVersionPath = targetExecutable,
            DatabaseMayHaveChanged = false,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        var result = await fixture.CreateInstaller(new DelegateVerifier((_, _, _) => Task.FromResult(true)))
            .RecoverInterruptedAsync();

        Assert.Equal(0, result);
        Assert.Equal(UpdateState.Failed, fixture.StateStore.Load()!.State);
        Assert.False(Directory.Exists(targetDirectory));
        Assert.Equal(Path.GetFullPath(fixture.SourceExecutable), fixture.PointerStore.Load()!.ExecutablePath);
    }

    [Fact]
    public async Task Recovery_WhenPreUpdateIsCorruptLeavesExplicitRollbackRequired()
    {
        await using var fixture = await InstallerFixture.CreateAsync(InstallationMode.Principal);
        var state = fixture.StateStore.Load()!;
        await fixture.MutatePersistentStateAsync();
        await File.WriteAllTextAsync(fixture.StateStore.Load()!.PreUpdateBackupPath!, "corrupt backup");
        fixture.StateStore.Save(state with
        {
            State = UpdateState.Verifying,
            PreviousVersionPath = fixture.SourceExecutable,
            CurrentVersionPath = Path.Combine(fixture.Paths.GetVersionDirectory(fixture.TargetVersion), "ArteInventariado.WinForms.exe"),
            DatabaseMayHaveChanged = true,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        var result = await fixture.CreateInstaller(new DelegateVerifier((_, _, _) => Task.FromResult(true)))
            .RecoverInterruptedAsync();

        Assert.Equal(3, result);
        Assert.Equal(UpdateState.RollbackRequired, fixture.StateStore.Load()!.State);
        Assert.Contains("Rollback", fixture.StateStore.Load()!.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchemaGuard_RejectsDatabaseWithUnknownNewerMigration()
    {
        using var directory = new TemporaryDirectory();
        var database = Path.Combine(directory.Path, "newer.db");
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL, ProductVersion TEXT NOT NULL);" +
                                  "INSERT INTO __EFMigrationsHistory VALUES ('001_base', '10.0.0');" +
                                  "INSERT INTO __EFMigrationsHistory VALUES ('999_future', '10.0.0');";
            command.ExecuteNonQuery();
        }

        Assert.Throws<IncompatibleDatabaseException>(() =>
            SchemaCompatibilityGuard.EnsureCompatible(database, ["001_base"]));
    }

    private sealed class DelegateVerifier(
        Func<string, UpdateRuntimePaths, CancellationToken, Task<bool>> action) : IUpdateApplicationVerifier
    {
        public Task<bool> VerifyAsync(
            string executablePath,
            UpdateRuntimePaths paths,
            CancellationToken cancellationToken = default) =>
            action(executablePath, paths, cancellationToken);
    }

    private sealed class FixedDiskSpaceProbe(long availableBytes) : IUpdateDiskSpaceProbe
    {
        public long GetAvailableBytes(string path) => availableBytes;
    }

    private sealed class MutatingDiskSpaceProbe(string packagePath) : IUpdateDiskSpaceProbe
    {
        private int _mutated;

        public long GetAvailableBytes(string path)
        {
            if (Interlocked.Exchange(ref _mutated, 1) == 0)
            {
                File.AppendAllText(packagePath, "\nmutacion posterior a la preparacion");
            }

            return long.MaxValue;
        }
    }

    private sealed class RecordingDeploymentFinalizer(bool throwOnFinalize = false) : IStableDeploymentFinalizer
    {
        public List<string> VersionDirectories { get; } = [];

        public Task FinalizeAsync(string versionDirectory, CancellationToken cancellationToken = default)
        {
            VersionDirectories.Add(Path.GetFullPath(versionDirectory));
            if (throwOnFinalize) throw new IOException("Fallo controlado del deployment finalizer.");
            return Task.CompletedTask;
        }
    }

    private sealed class InstallerFixture : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = BackupPackageEngine.JsonOptions;
        private readonly Guid _installationId;
        public TemporaryDirectory Temp { get; } = new();
        public UpdateRuntimePaths Paths { get; }
        public UpdateStateStore StateStore { get; }
        public UpdatePointerStore PointerStore { get; }
        public string SourceExecutable { get; }
        public string TargetVersion { get; }
        public string LegacyAssetPath { get; }
        public string ManagedAssetPath { get; }

        private InstallerFixture(InstallationMode mode)
        {
            Paths = UpdateRuntimePaths.Create(Path.Combine(Temp.Path, "app-data"));
            StateStore = new UpdateStateStore(Paths.StatePath);
            PointerStore = new UpdatePointerStore(Paths.CurrentPointerPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.DatabasePath)!);
            Directory.CreateDirectory(Paths.BusinessAssetsDirectory);
            Directory.CreateDirectory(Paths.ManagedAssetsDirectory);

            var station = new LocalStationConfigurationStore(Paths.StationSettingsPath);
            station.SaveConfigured(mode);
            _installationId = station.Load().Status.InstallationId!.Value;

            var sourceVersion = "1.10.0";
            SourceExecutable = Path.Combine(Paths.GetVersionDirectory(sourceVersion), "ArteInventariado.WinForms.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(SourceExecutable)!);
            File.Copy(typeof(UpdateInstaller).Assembly.Location, SourceExecutable);
            TargetVersion = UpdateVersion.Parse(
                FileVersionInfo.GetVersionInfo(SourceExecutable).ProductVersion ?? "1.10.0").ToString();
            PointerStore.Save(sourceVersion, SourceExecutable, null, DateTimeOffset.UtcNow);

            var legacyBytes = Encoding.UTF8.GetBytes("asset-legado-anterior");
            var legacyHash = Convert.ToHexString(SHA256.HashData(legacyBytes));
            LegacyAssetPath = Path.Combine(Paths.BusinessAssetsDirectory, $"{legacyHash.ToLowerInvariant()}.png");
            File.WriteAllBytes(LegacyAssetPath, legacyBytes);
            ManagedAssetPath = Path.Combine(Paths.ManagedAssetsDirectory, "Products", "fixture", "original.png");
            Directory.CreateDirectory(Path.GetDirectoryName(ManagedAssetPath)!);
            File.WriteAllText(ManagedAssetPath, "asset-administrado-anterior");
        }

        public static async Task<InstallerFixture> CreateAsync(InstallationMode mode)
        {
            var fixture = new InstallerFixture(mode);
            await fixture.CreateDatabaseAsync();
            var backup = await fixture.CreatePreUpdatePackageAsync();
            var update = await fixture.CreateUpdatePackageAsync(fixture.TargetVersion);
            var stationGuard = new UpdateStationGuard();
            var stationSnapshot = await stationGuard.CaptureAsync(
                fixture.Paths.StationSettingsPath,
                fixture.Paths.Root,
                fixture.TargetVersion);
            fixture.StateStore.Save(new UpdateStateDocument(
                UpdateStateStore.CurrentSchemaVersion,
                UpdateState.ReadyToInstall,
                "1.10.0",
                fixture.TargetVersion,
                update.PackagePath,
                update.ManifestPath,
                backup,
                fixture.SourceExecutable,
                null,
                null,
                DateTimeOffset.UtcNow,
                fixture.SourceExecutable,
                fixture._installationId,
                mode,
                stationSnapshot.Sha256,
                false,
                stationSnapshot.BackupPath));
            return fixture;
        }

        public UpdateInstaller CreateInstaller(
            IUpdateApplicationVerifier verifier,
            IUpdateDiskSpaceProbe? diskSpace = null,
            IStableDeploymentFinalizer? stableDeploymentFinalizer = null) =>
            new(
                new UpdatePackageValidator(),
                StateStore,
                new UpdateBackupService(),
                Paths,
                verifier,
                PointerStore,
                new UpdateStationGuard(),
                new UpdateEventLog(Paths.LogDirectory),
                null,
                diskSpace,
                new UpdateLaunchTargetResolver(Paths, new FixtureExecutableProbe(SourceExecutable)),
                stableDeploymentFinalizer);

        public async Task<(string ManifestPath, string PackagePath)> CreateUpdatePackageAsync(string targetVersion)
        {
            var packagePath = Path.Combine(Temp.Path, $"ArteInventariado-{targetVersion}-win-x64.zip");
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                var executable = archive.CreateEntry("ArteInventariado.WinForms.exe", CompressionLevel.NoCompression);
                await using (var entryStream = executable.Open())
                await using (var source = File.OpenRead(SourceExecutable))
                {
                    await source.CopyToAsync(entryStream);
                }

                var marker = archive.CreateEntry("VERSION.txt");
                await using (var writer = new StreamWriter(marker.Open()))
                {
                    await writer.WriteAsync(targetVersion);
                }
            }

            var manifestPath = Path.Combine(Temp.Path, $"manifest-{targetVersion}.json");
            var manifest = new UpdateManifest(
                targetVersion,
                "1.9.0",
                null,
                Path.GetFileName(packagePath),
                await UpdatePackageValidator.ComputeSha256Async(packagePath),
                new FileInfo(packagePath).Length,
                "Paquete de prueba");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return (manifestPath, packagePath);
        }

        public async Task<string> CreatePreUpdatePackageAsync(
            bool corruptManagedAsset = false,
            bool includeUnsafeEntry = false)
        {
            var suffix = corruptManagedAsset ? "corrupt" : includeUnsafeEntry ? "unsafe" : "valid";
            var package = Path.Combine(Temp.Path, $"PRE_UPDATE-{suffix}-{Guid.NewGuid():N}.aibak");
            var legacyBytes = await File.ReadAllBytesAsync(LegacyAssetPath);
            var managedBytes = Encoding.UTF8.GetBytes("asset-administrado-anterior");
            var legacyHash = Convert.ToHexString(SHA256.HashData(legacyBytes));
            var managedHash = Convert.ToHexString(SHA256.HashData(managedBytes));
            var databaseHash = await UpdatePackageValidator.ComputeSha256Async(Paths.DatabasePath);
            var managedRelative = "Products/fixture/original.png";
            var assets = new List<BackupAssetManifest>
            {
                new(1, $"business-assets/products/{legacyHash.ToLowerInvariant()}.png", "legacy.png", legacyBytes.Length, legacyHash, false),
                new(1, $"assets/{managedRelative}", "original.png", managedBytes.Length, managedHash, false,
                    ProductAssetOwnerKind.Product, 1, 1, managedRelative)
            };
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.Now,
                "1.9.0",
                "Microsoft.EntityFrameworkCore.Sqlite",
                [],
                null,
                databaseHash,
                new FileInfo(Paths.DatabasePath).Length,
                _installationId,
                new LocalStationConfigurationStore(Paths.StationSettingsPath).Load().Status.Mode!.Value,
                BackupKind.PreUpdate,
                assets);

            using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
            var manifestEntry = archive.CreateEntry(BackupPackageEngine.ManifestEntryName);
            await using (var stream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions);
            }

            var databaseEntry = archive.CreateEntry(BackupPackageEngine.DatabaseEntryName, CompressionLevel.NoCompression);
            await using (var destination = databaseEntry.Open())
            await using (var source = File.OpenRead(Paths.DatabasePath))
            {
                await source.CopyToAsync(destination);
            }

            await WriteEntryAsync(archive, assets[0].PackagePath, legacyBytes);
            await WriteEntryAsync(
                archive,
                assets[1].PackagePath,
                corruptManagedAsset ? Encoding.UTF8.GetBytes("asset-administrado-corrupto") : managedBytes);
            if (includeUnsafeEntry)
            {
                await WriteEntryAsync(archive, "../escape.txt", Encoding.UTF8.GetBytes("escape"));
            }

            return package;
        }

        public async Task MutatePersistentStateAsync(CancellationToken cancellationToken = default)
        {
            await MutateBusinessStateAsync(cancellationToken);
            await File.WriteAllTextAsync(Paths.StationSettingsPath, "{\"installationId\":\"00000000-0000-0000-0000-000000000001\"}", cancellationToken);
        }

        public async Task MutateBusinessStateAsync(CancellationToken cancellationToken = default)
        {
            await SetProductNameAsync("Producto migrado", cancellationToken);
            await File.WriteAllTextAsync(LegacyAssetPath, "asset-legado-migrado", cancellationToken);
            await File.WriteAllTextAsync(ManagedAssetPath, "asset-administrado-migrado", cancellationToken);
        }

        public void AssertStationPreserved(InstallationMode mode)
        {
            var status = new LocalStationConfigurationStore(Paths.StationSettingsPath).Load().Status;
            Assert.True(status.IsConfigured);
            Assert.Equal(_installationId, status.InstallationId);
            Assert.Equal(mode, status.Mode);
        }

        public async Task<string?> ReadProductNameAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={Paths.DatabasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nombre FROM productos WHERE producto_id = 1;";
            return Convert.ToString(await command.ExecuteScalarAsync());
        }

        public async Task SetProductNameAsync(string name, CancellationToken cancellationToken = default)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection($"Data Source={Paths.DatabasePath};Pooling=False");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE productos SET nombre = $name WHERE producto_id = 1;";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task CreateDatabaseAsync()
        {
            var managedBytes = await File.ReadAllBytesAsync(ManagedAssetPath);
            var managedHash = Convert.ToHexString(SHA256.HashData(managedBytes));
            await using var connection = new SqliteConnection($"Data Source={Paths.DatabasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL, ProductVersion TEXT NOT NULL);" +
                                  "CREATE TABLE usuarios (usuario_id INTEGER PRIMARY KEY);" +
                                  "CREATE TABLE ventas (venta_id INTEGER PRIMARY KEY);" +
                                  "CREATE TABLE configuracion_sistema (configuracion_id INTEGER PRIMARY KEY);" +
                                  "CREATE TABLE productos (producto_id INTEGER PRIMARY KEY, nombre TEXT NOT NULL, imagen_local_path TEXT NULL);" +
                                  "CREATE TABLE producto_assets (producto_asset_id INTEGER PRIMARY KEY, producto_id INTEGER NOT NULL, ruta_relativa TEXT NOT NULL, size_bytes INTEGER NOT NULL, sha256 TEXT NOT NULL);" +
                                  "INSERT INTO productos (producto_id, nombre, imagen_local_path) VALUES (1, 'Producto anterior', $path);" +
                                  "INSERT INTO producto_assets (producto_asset_id, producto_id, ruta_relativa, size_bytes, sha256) VALUES (1, 1, 'Products/fixture/original.png', $size, $sha);";
            command.Parameters.AddWithValue("$path", LegacyAssetPath);
            command.Parameters.AddWithValue("$size", managedBytes.Length);
            command.Parameters.AddWithValue("$sha", managedHash);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
            await using var stream = entry.Open();
            await stream.WriteAsync(content);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Temp.Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed class FixtureExecutableProbe(string sourceExecutable) : IUpdateExecutableProbe
        {
            private readonly string _sourceExecutable = Path.GetFullPath(sourceExecutable);

            public bool FileExists(string path) => File.Exists(path);

            public bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

            public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

            public string? GetProductVersion(string path) =>
                string.Equals(Path.GetFullPath(path), _sourceExecutable, StringComparison.OrdinalIgnoreCase)
                    ? "1.10.0"
                    : FileVersionInfo.GetVersionInfo(path).ProductVersion;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"arte-update-installer-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => System.IO.Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Path))
                {
                    System.IO.Directory.Delete(Path, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
