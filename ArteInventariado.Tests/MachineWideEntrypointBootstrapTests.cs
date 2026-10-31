using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Station;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class MachineWideEntrypointBootstrapTests
{
    private static string CurrentProductVersion =>
        FileVersionInfo.GetVersionInfo(typeof(MachineWideEntrypointBootstrapTests).Assembly.Location)
            .ProductVersion
        ?? throw new InvalidDataException("El assembly de pruebas no declara ProductVersion.");

    [Fact]
    public async Task Bootstrap_ReplacesOnlyStableEntrypointsAndLeavesCanonicalStateUntouched()
    {
        using var fixture = await Fixture.CreateAsync();
        var pointerBefore = await File.ReadAllBytesAsync(fixture.Paths.CurrentPointerPath);
        var launcherBefore = await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath);
        var updaterBefore = await File.ReadAllBytesAsync(fixture.Paths.StableUpdaterPath);

        var result = await fixture.CreateService().BootstrapAsync(
            fixture.Paths,
            fixture.ManifestPath,
            fixture.PackagePath);

        Assert.Equal("1.13.4", result.SourceVersion);
        Assert.Equal(fixture.TargetVersion, result.TargetVersion);
        Assert.Equal(
            fixture.LauncherPayloadHash,
            await UpdatePackageValidator.ComputeSha256Async(fixture.Paths.StableLauncherPath));
        Assert.Equal(
            fixture.UpdaterPayloadHash,
            await UpdatePackageValidator.ComputeSha256Async(fixture.Paths.StableUpdaterPath));
        Assert.NotEqual(launcherBefore, await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath));
        Assert.NotEqual(updaterBefore, await File.ReadAllBytesAsync(fixture.Paths.StableUpdaterPath));
        Assert.Equal(pointerBefore, await File.ReadAllBytesAsync(fixture.Paths.CurrentPointerPath));
        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.False(File.Exists(Path.Combine(fixture.Paths.RecoveryDirectory, "PRE_UPDATE.aibak")));
    }

    [Fact]
    public async Task Bootstrap_WhenSecondEntrypointFails_RollsBackFirstAndLeavesStateUntouched()
    {
        using var fixture = await Fixture.CreateAsync();
        var pointerBefore = await File.ReadAllBytesAsync(fixture.Paths.CurrentPointerPath);
        var launcherBefore = await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath);
        var updaterBefore = await File.ReadAllBytesAsync(fixture.Paths.StableUpdaterPath);
        var failedOnce = false;

        var service = fixture.CreateService(
            replaceFile: async (source, destination, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!failedOnce && string.Equals(
                        destination,
                        fixture.Paths.StableUpdaterPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    failedOnce = true;
                    throw new IOException("simulated updater replacement failure");
                }

                File.Move(source, destination, overwrite: true);
                await Task.CompletedTask;
            });

        await Assert.ThrowsAsync<IOException>(() => service.BootstrapAsync(
            fixture.Paths,
            fixture.ManifestPath,
            fixture.PackagePath));

        Assert.Equal(launcherBefore, await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath));
        Assert.Equal(updaterBefore, await File.ReadAllBytesAsync(fixture.Paths.StableUpdaterPath));
        Assert.Equal(pointerBefore, await File.ReadAllBytesAsync(fixture.Paths.CurrentPointerPath));
        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
    }

    [Fact]
    public async Task Bootstrap_WhenNotElevatedOrCancelledBeforeMutation_LeavesEntrypointsUntouched()
    {
        using var fixture = await Fixture.CreateAsync();
        var launcherBefore = await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath);
        var updaterBefore = await File.ReadAllBytesAsync(fixture.Paths.StableUpdaterPath);

        var denied = fixture.CreateService(isAdministrator: () => false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.BootstrapAsync(
            fixture.Paths,
            fixture.ManifestPath,
            fixture.PackagePath));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var interrupted = fixture.CreateService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted.BootstrapAsync(
            fixture.Paths,
            fixture.ManifestPath,
            fixture.PackagePath,
            cancelled.Token));

        Assert.Equal(launcherBefore, await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath));
        Assert.Equal(updaterBefore, await File.ReadAllBytesAsync(fixture.Paths.StableUpdaterPath));
        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
    }

    [Fact]
    public async Task Bootstrap_WhenManagedEntrypointIsRunning_FailsClosedBeforeMutation()
    {
        using var fixture = await Fixture.CreateAsync();
        var launcherBefore = await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath);
        var service = fixture.CreateService(
            processProbe: _ =>
            [new MachineWideEntrypointProcessInfo(4242, fixture.Paths.StableLauncherPath)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BootstrapAsync(
            fixture.Paths,
            fixture.ManifestPath,
            fixture.PackagePath));

        Assert.Equal(launcherBefore, await File.ReadAllBytesAsync(fixture.Paths.StableLauncherPath));
        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
    }

    [Fact]
    public async Task BootstrapThenOfficialHandoffAndInstaller_PreservesCurrentPreviousAndStation()
    {
        using var fixture = await Fixture.CreateAsync();
        var databaseBefore = await UpdatePackageValidator.ComputeSha256Async(fixture.Paths.DatabasePath);
        var pointerBefore = new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load();
        var stationBefore = new LocalStationConfigurationStore(fixture.Paths.StationSettingsPath).Load().Status;

        await fixture.CreateService().BootstrapAsync(
            fixture.Paths,
            fixture.ManifestPath,
            fixture.PackagePath);
        Assert.Equal(pointerBefore, new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load());

        var staged = await new MachineWideUpdateStagingService().StageAsync(
            fixture.Paths,
            fixture.ManifestPath,
            fixture.PackagePath,
            "1.13.4");
        var handoff = new MachineWideUpdateHandoffService(
            preUpdateCreator: new MachineWidePreUpdateBackupCreator(() => true),
            isAdministrator: () => true,
            resolverFactory: fixture.CreateResolver);
        var ready = await handoff.PrepareAsync(fixture.Paths, staged.StagingDirectory);
        Assert.Equal(UpdateState.ReadyToInstall, ready.State);
        Assert.NotNull(ready.PreUpdateBackupPath);
        Assert.True(File.Exists(ready.PreUpdateBackupPath));

        var installer = new UpdateInstaller(
            new UpdatePackageValidator(),
            new UpdateStateStore(fixture.Paths.StatePath),
            new UpdateBackupService(),
            fixture.Paths,
            verifier: new AlwaysValidApplicationVerifier(),
            pointerStore: new UpdatePointerStore(fixture.Paths.CurrentPointerPath),
            stationGuard: new UpdateStationGuard(),
            eventLog: new UpdateEventLog(fixture.Paths.LogDirectory),
            launchTargetResolver: fixture.CreateResolver(fixture.Paths),
            isAdministrator: () => true);
        var result = await installer.InstallFromStateAsync();

        var state = new UpdateStateStore(fixture.Paths.StatePath).Load()!;
        Assert.True(result == 0, $"Install result={result}; state={state.State}; error={state.LastError}");
        var pointer = new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load()!;
        Assert.Equal(UpdateState.Success, state.State);
        Assert.Equal(fixture.TargetVersion, pointer.Version);
        Assert.Equal("1.13.4", state.SourceVersion);
        Assert.Equal(fixture.TargetVersion, state.TargetVersion);
        Assert.Equal(Path.GetFullPath(state.PreviousVersionPath!), pointer.PreviousExecutablePath);
        Assert.Equal(stationBefore.InstallationId, new LocalStationConfigurationStore(fixture.Paths.StationSettingsPath).Load().Status.InstallationId);
        Assert.Equal(stationBefore.Mode, new LocalStationConfigurationStore(fixture.Paths.StationSettingsPath).Load().Status.Mode);
        Assert.Equal(databaseBefore, await UpdatePackageValidator.ComputeSha256Async(fixture.Paths.DatabasePath));
    }

    [Fact]
    public void EntrypointBootstrapArguments_RejectsNormalUpdateFlagsAndMissingValues()
    {
        var valid = new[]
        {
            "--machine-entrypoint-bootstrap",
            "--machine-install-root", "C:\\Program Files\\ArteInventariado",
            "--machine-data-root", "C:\\ProgramData\\ArteInventariado",
            "--user-data-root", "C:\\PortfolioDemo\\AppData\\Local\\ArteInventariado",
            "--manifest", "C:\\Downloads\\manifest.json",
            "--package", "C:\\Downloads\\package.zip"
        };

        var parsed = MachineWideEntrypointBootstrapArguments.Parse(valid);
        Assert.Equal("C:\\Downloads\\package.zip", parsed.PackagePath);
        Assert.Throws<ArgumentException>(() => MachineWideEntrypointBootstrapArguments.Parse(
            valid.Append("--no-relaunch").ToArray()));
        Assert.Throws<ArgumentException>(() => MachineWideEntrypointBootstrapArguments.Parse(
            valid.Where(value => !string.Equals(value, "--manifest", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(value, "C:\\Downloads\\manifest.json", StringComparison.OrdinalIgnoreCase)).ToArray()));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public ArteInventariadoPathProvider Provider { get; }
        public UpdateRuntimePaths Paths { get; }
        public string ManifestPath { get; private set; } = string.Empty;
        public string PackagePath { get; private set; } = string.Empty;
        public string LauncherPayloadHash { get; private set; } = string.Empty;
        public string UpdaterPayloadHash { get; private set; } = string.Empty;
        public string TargetVersion { get; } = CurrentProductVersion;

        private Fixture()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                $"ArteInventariado-entrypoint-bootstrap-tests-{Guid.NewGuid():N}");
            Provider = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(_root, "install"),
                Path.Combine(_root, "data"),
                Path.Combine(_root, "user"));
            Paths = UpdateRuntimePaths.Create(Provider);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.Paths.StableLauncherPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.Paths.StableUpdaterPath)!);
                await File.WriteAllTextAsync(fixture.Paths.StableLauncherPath, "old launcher");
                await File.WriteAllTextAsync(fixture.Paths.StableUpdaterPath, "old updater");

                var versionDirectory = fixture.Paths.GetVersionDirectory("1.13.4");
                Directory.CreateDirectory(versionDirectory);
                var currentExecutable = Path.Combine(
                    versionDirectory,
                    UpdateLaunchTargetResolver.ApplicationExecutableName);
                await File.WriteAllBytesAsync(
                    currentExecutable,
                    await File.ReadAllBytesAsync(typeof(MachineWideEntrypointBootstrapTests).Assembly.Location));
                new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Save(
                    "1.13.4",
                    currentExecutable,
                    null,
                    DateTimeOffset.UtcNow);
                var station = new LocalStationConfigurationStore(fixture.Paths.StationSettingsPath)
                    .SaveConfigured(InstallationMode.Principal);
                _ = station;
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.Paths.DatabasePath)!);
                var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={fixture.Paths.DatabasePath};Pooling=False")
                    .Options;
                await using (var context = new AppDbContext(dbOptions))
                {
                    await DatabaseInitializer.InicializarAsync(context);
                }

                Directory.CreateDirectory(fixture.Paths.BusinessAssetsDirectory);
                Directory.CreateDirectory(fixture.Paths.ManagedAssetsDirectory);
                var sourceAssembly = typeof(MachineWideEntrypointBootstrapTests).Assembly.Location;
                var sourceDirectory = Path.Combine(fixture._root, "source");
                Directory.CreateDirectory(sourceDirectory);
                fixture.PackagePath = Path.Combine(sourceDirectory, $"ArteInventariado-{fixture.TargetVersion}-win-x64.zip");
                fixture.ManifestPath = Path.Combine(sourceDirectory, "manifest.json");
                using (var archive = ZipFile.Open(fixture.PackagePath, ZipArchiveMode.Create))
                {
                    foreach (var name in new[]
                             {
                                 UpdateLaunchTargetResolver.ApplicationExecutableName,
                                 StableDeploymentBootstrap.LauncherExecutableName,
                                 StableDeploymentBootstrap.UpdaterExecutableName
                             })
                    {
                        var entry = archive.CreateEntry(name);
                        await using var output = entry.Open();
                        await using var input = File.OpenRead(sourceAssembly);
                        await input.CopyToAsync(output);
                    }
                }

                var launcherEntry = Path.Combine(sourceDirectory, "launcher-payload.bin");
                var updaterEntry = Path.Combine(sourceDirectory, "updater-payload.bin");
                await File.WriteAllBytesAsync(launcherEntry, await File.ReadAllBytesAsync(sourceAssembly));
                await File.WriteAllBytesAsync(updaterEntry, await File.ReadAllBytesAsync(sourceAssembly));
                fixture.LauncherPayloadHash = await UpdatePackageValidator.ComputeSha256Async(launcherEntry);
                fixture.UpdaterPayloadHash = await UpdatePackageValidator.ComputeSha256Async(updaterEntry);

                var manifest = new UpdateManifest(
                    fixture.TargetVersion,
                    "1.13.4",
                    null,
                    Path.GetFileName(fixture.PackagePath),
                    await UpdatePackageValidator.ComputeSha256Async(fixture.PackagePath),
                    new FileInfo(fixture.PackagePath).Length,
                    "Bootstrap de entrypoints de prueba",
                    DeploymentTarget: UpdatePackageTargets.MachineWide);
                await File.WriteAllTextAsync(
                    fixture.ManifestPath,
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public MachineWideEntrypointBootstrapService CreateService(
            Func<bool>? isAdministrator = null,
            Func<string, string, CancellationToken, Task>? replaceFile = null,
            Func<UpdateRuntimePaths, IReadOnlyList<MachineWideEntrypointProcessInfo>>? processProbe = null) =>
            new(
                new UpdatePackageValidator(),
                isAdministrator ?? (() => true),
                paths => new UpdateLaunchTargetResolver(
                    paths,
                    new FixedExecutableProbe("1.13.4")),
                processProbe ?? (_ => []),
                replaceFile ?? ((source, destination, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(source, destination, overwrite: true);
                    return Task.CompletedTask;
                }),
                allowSyntheticRoots: true);

        public UpdateLaunchTargetResolver CreateResolver(UpdateRuntimePaths paths) =>
            new(paths, new FixedExecutableProbe("1.13.4"));

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

        private sealed class FixedExecutableProbe(string version) : IUpdateExecutableProbe
        {
            public bool FileExists(string path) => File.Exists(path);
            public bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);
            public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
            public string? GetProductVersion(string path) => version;
        }
    }

    private sealed class AlwaysValidApplicationVerifier : IUpdateApplicationVerifier
    {
        public Task<bool> VerifyAsync(
            string executablePath,
            UpdateRuntimePaths paths,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
