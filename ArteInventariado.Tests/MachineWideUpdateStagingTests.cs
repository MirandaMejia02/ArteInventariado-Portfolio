using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;

namespace ArteInventariado.Tests;

public sealed class MachineWideUpdateStagingTests
{
    [Fact]
    public async Task StageAsync_UsesPerUserGuidDirectoryWithoutTouchingCanonicalState()
    {
        using var fixture = new MachineWideFixture();
        var package = await fixture.CreatePackageAsync();

        var staged = await fixture.Staging.StageAsync(
            fixture.Paths,
            package.ManifestPath,
            package.PackagePath,
            "1.11.0");

        Assert.StartsWith(
            Path.GetFullPath(fixture.Paths.StagingRoot) + Path.DirectorySeparatorChar,
            staged.StagingDirectory,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Guid.TryParseExact(new DirectoryInfo(staged.StagingDirectory).Name, "N", out _));
        Assert.True(File.Exists(staged.ManifestPath));
        Assert.True(File.Exists(staged.PackagePath));
        Assert.True(staged.Validation.IsValid);
        Assert.False(File.Exists(fixture.Paths.StatePath));
        Assert.False(Directory.Exists(fixture.Paths.RecoveryDirectory));
        Assert.False(Directory.Exists(fixture.Paths.PathProvider!.ProtectedBackupsRoot));

        MachineWideUpdateGuard.ValidateHandoffPath(fixture.Paths, staged.StagingDirectory);
        MachineWideUpdateStagingService.DeleteStagingDirectory(fixture.Paths, staged.StagingDirectory);
        Assert.False(Directory.Exists(staged.StagingDirectory));
    }

    [Fact]
    public async Task StageAsync_RejectsVisualDevPackageBeforeCreatingStaging()
    {
        using var fixture = new MachineWideFixture();
        var package = await fixture.CreatePackageAsync(deploymentTarget: UpdatePackageTargets.VisualDev);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Staging.StageAsync(
                fixture.Paths,
                package.ManifestPath,
                package.PackagePath,
                "1.11.0"));

        Assert.False(Directory.Exists(fixture.Paths.StagingRoot));
        Assert.False(File.Exists(fixture.Paths.StatePath));
        Assert.False(Directory.Exists(fixture.Paths.RecoveryDirectory));
    }

    [Fact]
    public async Task Handoff_RejectsPackageChangedAfterNonElevatedStaging()
    {
        using var fixture = new MachineWideFixture();
        await fixture.PrepareCurrentAsync();
        var package = await fixture.CreatePackageAsync();
        var staged = await fixture.Staging.StageAsync(
            fixture.Paths,
            package.ManifestPath,
            package.PackagePath,
            "1.11.0");
        await File.AppendAllTextAsync(staged.PackagePath, "tampered");

        var handoff = fixture.CreateHandoff();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            handoff.PrepareAsync(fixture.Paths, staged.StagingDirectory));

        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.False(Directory.Exists(fixture.Paths.RecoveryDirectory));
        Assert.False(Directory.Exists(fixture.Paths.PathProvider!.ProtectedBackupsRoot));
        Assert.True(Directory.Exists(staged.StagingDirectory));
    }

    [Fact]
    public async Task Handoff_RejectsManifestChangedAfterNonElevatedStaging()
    {
        using var fixture = new MachineWideFixture();
        await fixture.PrepareCurrentAsync();
        var package = await fixture.CreatePackageAsync();
        var staged = await fixture.Staging.StageAsync(
            fixture.Paths,
            package.ManifestPath,
            package.PackagePath,
            "1.11.0");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(
            await File.ReadAllTextAsync(staged.ManifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await File.WriteAllTextAsync(
            staged.ManifestPath,
            JsonSerializer.Serialize(
                manifest with { Sha256 = new string('0', 64) },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.CreateHandoff().PrepareAsync(fixture.Paths, staged.StagingDirectory));

        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.True(
            !Directory.Exists(fixture.Paths.RecoveryDirectory) ||
            !Directory.EnumerateFileSystemEntries(fixture.Paths.RecoveryDirectory).Any());
    }

    [Fact]
    public async Task Handoff_RejectsExecutableProductVersionMismatch()
    {
        using var fixture = new MachineWideFixture();
        await fixture.PrepareCurrentAsync();
        var package = await fixture.CreatePackageAsync("1.12.0-test-mismatch");
        var staged = await fixture.Staging.StageAsync(
            fixture.Paths,
            package.ManifestPath,
            package.PackagePath,
            "1.11.0");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.CreateHandoff().PrepareAsync(fixture.Paths, staged.StagingDirectory));

        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.True(
            !Directory.Exists(fixture.Paths.RecoveryDirectory) ||
            !Directory.EnumerateFileSystemEntries(fixture.Paths.RecoveryDirectory).Any());
    }

    [Fact]
    public async Task Handoff_PromotesToRecoveryCreatesPreUpdateAndRemovesUserStaging()
    {
        using var fixture = new MachineWideFixture();
        await fixture.PrepareCurrentAsync("1.10.0");
        var targetVersion = FileVersionInfo.GetVersionInfo(
                typeof(MachineWideUpdateStagingTests).Assembly.Location)
            .ProductVersion
            ?? throw new InvalidOperationException("El ensamblado de pruebas no declara ProductVersion.");
        var package = await fixture.CreatePackageAsync(targetVersion, "1.10.0");
        var staged = await fixture.Staging.StageAsync(
            fixture.Paths,
            package.ManifestPath,
            package.PackagePath,
            "1.10.0");
        var pointerBefore = new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load();

        var backup = new FakePreUpdateBackupCreator(fixture.InstallationId, InstallationMode.Principal);
        var state = await new MachineWideUpdateHandoffService(
            preUpdateCreator: backup,
            isAdministrator: () => true,
            resolverFactory: fixture.CreateResolver)
            .PrepareAsync(fixture.Paths, staged.StagingDirectory);

        Assert.Equal(UpdateState.ReadyToInstall, state.State);
        Assert.Equal("1.10.0", state.SourceVersion);
        Assert.Equal(targetVersion, state.TargetVersion);
        Assert.True(backup.Called);
        Assert.NotNull(state.PreUpdateBackupPath);
        Assert.StartsWith(
            Path.GetFullPath(fixture.Paths.RecoveryDirectory) + Path.DirectorySeparatorChar,
            state.PreUpdateBackupPath!,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            Path.GetFullPath(fixture.Paths.RecoveryDirectory) + Path.DirectorySeparatorChar,
            state.PackagePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(state.PackagePath));
        Assert.True(File.Exists(state.ManifestPath));
        Assert.True(File.Exists(state.PreUpdateBackupPath));
        Assert.False(Directory.Exists(staged.StagingDirectory));
        Assert.False(Directory.Exists(fixture.Paths.PathProvider!.ProtectedBackupsRoot));

        var persisted = new UpdateStateStore(fixture.Paths.StatePath).Load();
        Assert.Equal(state, persisted);
        Assert.Equal(pointerBefore, new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load());
    }

    [Fact]
    public async Task Handoff_WhenUacIsNotElevatedLeavesInstallationUntouched()
    {
        using var fixture = new MachineWideFixture();
        await fixture.PrepareCurrentAsync();
        var package = await fixture.CreatePackageAsync();
        var staged = await fixture.Staging.StageAsync(
            fixture.Paths,
            package.ManifestPath,
            package.PackagePath,
            "1.11.0");
        var pointerBefore = new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new MachineWideUpdateHandoffService(
                preUpdateCreator: new FakePreUpdateBackupCreator(fixture.InstallationId, InstallationMode.Principal),
                    isAdministrator: () => false,
                    resolverFactory: fixture.CreateResolver)
                .PrepareAsync(fixture.Paths, staged.StagingDirectory));

        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.False(Directory.Exists(fixture.Paths.RecoveryDirectory));
        Assert.False(Directory.Exists(fixture.Paths.PathProvider!.ProtectedBackupsRoot));
        Assert.True(Directory.Exists(staged.StagingDirectory));
        Assert.Equal(pointerBefore, new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load());
    }

    [Fact]
    public async Task Handoff_RejectsExtraStagingEntry()
    {
        using var fixture = new MachineWideFixture();
        var package = await fixture.CreatePackageAsync();
        var staged = await fixture.Staging.StageAsync(
            fixture.Paths,
            package.ManifestPath,
            package.PackagePath,
            "1.11.0");
        await File.WriteAllTextAsync(Path.Combine(staged.StagingDirectory, "unexpected.txt"), "not allowed");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.CreateHandoff().PrepareAsync(fixture.Paths, staged.StagingDirectory));
    }

    [Fact]
    public void HandoffPath_RejectsRootTraversalAndNonGuid()
    {
        using var fixture = new MachineWideFixture();
        Directory.CreateDirectory(fixture.Paths.StagingRoot);

        Assert.Throws<InvalidDataException>(() =>
            MachineWideUpdateGuard.ValidateHandoffPath(fixture.Paths, fixture.Paths.StagingRoot));
        Assert.Throws<InvalidDataException>(() =>
            MachineWideUpdateGuard.ValidateHandoffPath(
                fixture.Paths,
                Path.Combine(fixture.Paths.StagingRoot, "not-a-guid")));
        Assert.Throws<InvalidDataException>(() =>
            MachineWideUpdateGuard.ValidateHandoffPath(
                fixture.Paths,
                Path.Combine(fixture.Paths.StagingRoot, "..", "outside")));
    }

    private sealed class MachineWideFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-machine-staging-tests-{Guid.NewGuid():N}");

        public ArteInventariadoPathProvider Provider { get; }
        public UpdateRuntimePaths Paths { get; }
        public MachineWideUpdateStagingService Staging { get; } = new();
        public Guid InstallationId { get; private set; }
        public string CurrentVersion { get; private set; } = "1.11.0";

        public MachineWideFixture()
        {
            Provider = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(_root, "install"),
                Path.Combine(_root, "data"),
                Path.Combine(_root, "user"));
            Paths = UpdateRuntimePaths.Create(Provider);
        }

        public async Task PrepareCurrentAsync(string version = "1.11.0")
        {
            CurrentVersion = version;
            var versionDirectory = Paths.GetVersionDirectory(version);
            Directory.CreateDirectory(versionDirectory);
            var executable = Path.Combine(versionDirectory, UpdateLaunchTargetResolver.ApplicationExecutableName);
            File.Copy(typeof(MachineWideUpdateStagingTests).Assembly.Location, executable);
            new UpdatePointerStore(Paths.CurrentPointerPath).Save(
                version,
                executable,
                null,
                DateTimeOffset.UtcNow);

            var station = new LocalStationConfigurationStore(Paths.StationSettingsPath)
                .SaveConfigured(InstallationMode.Principal);
            InstallationId = station.InstallationId;
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.DatabasePath)!);
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
        }

        public async Task<(string ManifestPath, string PackagePath)> CreatePackageAsync(
            string version = "1.12.0",
            string minimumCompatibleVersion = "1.11.0",
            string deploymentTarget = UpdatePackageTargets.MachineWide)
        {
            var directory = Path.Combine(_root, "source");
            Directory.CreateDirectory(directory);
            var packagePath = Path.Combine(directory, "ArteInventariado-1.12.0-win-x64.zip");
            var manifestPath = Path.Combine(directory, "manifest.json");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                var executable = archive.CreateEntry(UpdateLaunchTargetResolver.ApplicationExecutableName);
                await using var stream = executable.Open();
                await using var source = File.OpenRead(typeof(MachineWideUpdateStagingTests).Assembly.Location);
                await source.CopyToAsync(stream);
            }

            var manifest = new UpdateManifest(
                version,
                minimumCompatibleVersion,
                null,
                Path.GetFileName(packagePath),
                await UpdatePackageValidator.ComputeSha256Async(packagePath),
                new FileInfo(packagePath).Length,
                "Paquete de prueba D2",
                DeploymentTarget: deploymentTarget);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return (manifestPath, packagePath);
        }

        public MachineWideUpdateHandoffService CreateHandoff() =>
            new(
                preUpdateCreator: new FakePreUpdateBackupCreator(InstallationId, InstallationMode.Principal),
                isAdministrator: () => true,
                resolverFactory: CreateResolver);

        public UpdateLaunchTargetResolver CreateResolver(UpdateRuntimePaths paths) =>
            new(paths, new FixedExecutableProbe(CurrentVersion));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FixedExecutableProbe(string productVersion) : IUpdateExecutableProbe
    {
        public bool FileExists(string path) => File.Exists(path);

        public bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

        public string? GetProductVersion(string path) => productVersion;
    }

    private sealed class FakePreUpdateBackupCreator : IMachineWidePreUpdateBackupCreator
    {
        private readonly Guid _installationId;
        private readonly InstallationMode _mode;

        public FakePreUpdateBackupCreator(Guid installationId, InstallationMode mode)
        {
            _installationId = installationId;
            _mode = mode;
        }

        public bool Called { get; private set; }

        public Task<BackupResult> CreateAsync(
            UpdateRuntimePaths paths,
            string sourceVersion,
            string targetVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Called = true;
            Directory.CreateDirectory(paths.RecoveryDirectory);
            var filePath = Path.Combine(paths.RecoveryDirectory, "PRE_UPDATE_fake.aibak");
            File.WriteAllText(filePath, "synthetic PRE_UPDATE");
            var now = DateTimeOffset.UtcNow;
            var manifest = new BackupManifest(
                1,
                Guid.NewGuid(),
                now,
                now,
                sourceVersion,
                "SQLite",
                [],
                null,
                new string('A', 64),
                1,
                _installationId,
                _mode,
                BackupKind.PreUpdate,
                []);
            return Task.FromResult(new BackupResult(manifest.BackupId, filePath, manifest, []));
        }
    }
}
