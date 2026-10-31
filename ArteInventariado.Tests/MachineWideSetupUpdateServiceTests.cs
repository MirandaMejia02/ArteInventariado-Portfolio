using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class MachineWideSetupUpdateServiceTests
{
    [Fact]
    public async Task ApplySyntheticAsync_RejectsVisualDevPackageBeforeCreatingStaging()
    {
        using var fixture = new Fixture();
        var target = NextVersion(fixture.CurrentVersion);
        var package = await fixture.CreatePackageAsync(target, UpdatePackageTargets.VisualDev);
        var pointerBefore = new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ApplySyntheticAsync(
            fixture.Provider,
            package.ManifestPath,
            package.PackagePath,
            target));

        Assert.False(Directory.Exists(fixture.Paths.StagingRoot));
        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.Equal(pointerBefore, new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load());
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(target)));
    }

    [Fact]
    public async Task ApplySyntheticAsync_RejectsManifestVersionNotMatchingSetupBeforeCreatingStaging()
    {
        using var fixture = new Fixture();
        var target = NextVersion(fixture.CurrentVersion);
        var wrongSetupVersion = NextVersion(target);
        var package = await fixture.CreatePackageAsync(target, UpdatePackageTargets.MachineWide);
        var pointerBefore = new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ApplySyntheticAsync(
            fixture.Provider,
            package.ManifestPath,
            package.PackagePath,
            wrongSetupVersion));

        Assert.False(Directory.Exists(fixture.Paths.StagingRoot));
        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.Equal(pointerBefore, new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load());
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(target)));
    }

    [Fact]
    public async Task ApplySyntheticAsync_RejectsProductVersionMismatchBeforeCreatingStaging()
    {
        using var fixture = new Fixture();
        var target = NextVersion(fixture.CurrentVersion);
        var package = await fixture.CreatePackageAsync(target, UpdatePackageTargets.MachineWide);
        var pointerBefore = new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ApplySyntheticAsync(
            fixture.Provider,
            package.ManifestPath,
            package.PackagePath,
            target));

        Assert.False(Directory.Exists(fixture.Paths.StagingRoot));
        Assert.Null(new UpdateStateStore(fixture.Paths.StatePath).Load());
        Assert.Equal(pointerBefore, new UpdatePointerStore(fixture.Paths.CurrentPointerPath).Load());
        Assert.False(Directory.Exists(fixture.Paths.GetVersionDirectory(target)));
    }

    private static string NextVersion(string version)
    {
        var parsed = UpdateVersion.Parse(version);
        return new UpdateVersion(parsed.Major, parsed.Minor, parsed.Patch + 1).ToString();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-machine-setup-update-tests-{Guid.NewGuid():N}");

        public ArteInventariadoPathProvider Provider { get; }
        public UpdateRuntimePaths Paths { get; }
        public string CurrentVersion { get; }
        public MachineWideSetupUpdateService Service { get; } = new(() => true);

        public Fixture()
        {
            CurrentVersion = FileVersionInfo.GetVersionInfo(typeof(MachineWideSetupUpdateServiceTests).Assembly.Location)
                .ProductVersion
                ?? throw new InvalidOperationException("El ensamblado de pruebas no declara ProductVersion.");
            Provider = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(_root, "install"),
                Path.Combine(_root, "data"),
                Path.Combine(_root, "user"));
            Paths = UpdateRuntimePaths.Create(Provider);
            var versionDirectory = Paths.GetVersionDirectory(CurrentVersion);
            Directory.CreateDirectory(versionDirectory);
            var executablePath = Path.Combine(versionDirectory, UpdateLaunchTargetResolver.ApplicationExecutableName);
            File.Copy(typeof(MachineWideSetupUpdateServiceTests).Assembly.Location, executablePath);
            new UpdatePointerStore(Paths.CurrentPointerPath).Save(
                CurrentVersion,
                executablePath,
                null,
                DateTimeOffset.UtcNow);
        }

        public async Task<(string ManifestPath, string PackagePath)> CreatePackageAsync(
            string targetVersion,
            string deploymentTarget)
        {
            var sourceDirectory = Path.Combine(_root, "source");
            Directory.CreateDirectory(sourceDirectory);
            var packagePath = Path.Combine(sourceDirectory, $"ArteInventariado-{targetVersion}-test.zip");
            var manifestPath = Path.Combine(sourceDirectory, "manifest.json");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                var executable = archive.CreateEntry(UpdateLaunchTargetResolver.ApplicationExecutableName);
                await using var output = executable.Open();
                await using var input = File.OpenRead(typeof(MachineWideSetupUpdateServiceTests).Assembly.Location);
                await input.CopyToAsync(output);
            }

            var manifest = new UpdateManifest(
                targetVersion,
                CurrentVersion,
                null,
                Path.GetFileName(packagePath),
                await UpdatePackageValidator.ComputeSha256Async(packagePath),
                new FileInfo(packagePath).Length,
                "Paquete sintético de prueba",
                DeploymentTarget: deploymentTarget);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return (manifestPath, packagePath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
