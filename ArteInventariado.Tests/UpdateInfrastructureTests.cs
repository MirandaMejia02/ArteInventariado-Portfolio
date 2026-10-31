using System.IO.Compression;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Tests;

public sealed class UpdateInfrastructureTests
{
    [Fact]
    public void Semver_OrdersReleaseAfterPrerelease()
    {
        Assert.True(UpdateVersion.Parse("1.11.0").CompareTo(UpdateVersion.Parse("1.11.0-rc1")) > 0);
        Assert.True(UpdateVersion.Parse("1.11.0-rc2").CompareTo(UpdateVersion.Parse("1.11.0-rc1")) > 0);
        Assert.Equal("1.11.0-rc2", UpdateVersion.Sanitize("v1.11.0-rc2"));
    }

    [Fact]
    public async Task Validator_AcceptsLocalPackageWithMatchingManifestAndHash()
    {
        using var fixture = new UpdatePackageFixture();
        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.10.0");
        Assert.True(result.IsValid, string.Join(" ", result.Errors));
        Assert.Equal("1.11.0", result.Manifest!.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UpdatePackageTargets.VisualDev)]
    public async Task Validator_RejectsPackageNotMarkedForMachineWideDeployment(string? deploymentTarget)
    {
        using var fixture = new UpdatePackageFixture(deploymentTarget: deploymentTarget);

        var result = await fixture.Validator.ValidateAsync(
            fixture.ManifestPath,
            fixture.PackagePath,
            "1.10.0",
            expectedDeploymentTarget: UpdatePackageTargets.MachineWide);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("DeploymentTarget", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validator_RejectsWrongSha()
    {
        using var fixture = new UpdatePackageFixture(shaOverride: new string('0', 64));
        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.10.0");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("SHA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validator_RejectsMalformedManifest()
    {
        using var fixture = new UpdatePackageFixture();
        await File.WriteAllTextAsync(fixture.ManifestPath, "{ not-json }");

        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.10.0");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("mal formado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validator_RejectsIncompatibleSourceVersion()
    {
        using var fixture = new UpdatePackageFixture();

        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.8.0");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("minima", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validator_RejectsSameVersion()
    {
        using var fixture = new UpdatePackageFixture("1.13.1", "1.13.0");

        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.13.1");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("posterior", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validator_AcceptsSequentialHotfixVersions()
    {
        using var firstHotfix = new UpdatePackageFixture("1.13.1", "1.13.0");
        var first = await firstHotfix.Validator.ValidateAsync(firstHotfix.ManifestPath, firstHotfix.PackagePath, "1.13.0");

        using var secondHotfix = new UpdatePackageFixture("1.13.2", "1.13.1");
        var second = await secondHotfix.Validator.ValidateAsync(secondHotfix.ManifestPath, secondHotfix.PackagePath, "1.13.1");

        Assert.True(first.IsValid, string.Join(" ", first.Errors));
        Assert.True(second.IsValid, string.Join(" ", second.Errors));
        Assert.Equal("1.13.1", first.Manifest!.Version);
        Assert.Equal("1.13.2", second.Manifest!.Version);
    }

    [Fact]
    public async Task Validator_RejectsCorruptZip()
    {
        using var fixture = new UpdatePackageFixture();
        await File.WriteAllTextAsync(fixture.PackagePath, "not a zip");
        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.10.0");
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Validator_RejectsZipSlipEntry()
    {
        using var fixture = new UpdatePackageFixture(extraEntry: "../escape.txt");
        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.10.0");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("ruta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validator_RejectsManifestThatNamesAnotherPackage()
    {
        using var fixture = new UpdatePackageFixture();
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(await File.ReadAllTextAsync(fixture.ManifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await File.WriteAllTextAsync(
            fixture.ManifestPath,
            JsonSerializer.Serialize(manifest with { PackagePath = "otro-paquete.zip" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.10.0");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("PackagePath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validator_RejectsExtremeCompressionRatio()
    {
        using var fixture = new UpdatePackageFixture();
        File.Delete(fixture.PackagePath);
        using (var archive = ZipFile.Open(fixture.PackagePath, ZipArchiveMode.Create))
        {
            var executable = archive.CreateEntry("ArteInventariado.WinForms.exe", CompressionLevel.NoCompression);
            await using (var writer = new StreamWriter(executable.Open()))
            {
                await writer.WriteAsync("test executable");
            }

            var compressed = archive.CreateEntry("highly-compressible.bin", CompressionLevel.SmallestSize);
            await using var stream = compressed.Open();
            await stream.WriteAsync(new byte[2 * 1024 * 1024]);
        }

        var manifest = new UpdateManifest(
            "1.11.0",
            "1.10.0",
            null,
            Path.GetFileName(fixture.PackagePath),
            await UpdatePackageValidator.ComputeSha256Async(fixture.PackagePath),
            new FileInfo(fixture.PackagePath).Length,
            "Actualizacion de prueba");
        await File.WriteAllTextAsync(
            fixture.ManifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = await fixture.Validator.ValidateAsync(fixture.ManifestPath, fixture.PackagePath, "1.10.0");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("compresion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StateStore_PersistsAndReloadsAtomically()
    {
        using var directory = new TemporaryDirectory();
        var store = new UpdateStateStore(Path.Combine(directory.Path, "state.json"));
        var expected = new UpdateStateDocument(UpdateStateStore.CurrentSchemaVersion, UpdateState.Idle, "1.10.0", "1.11.0", "package.zip", "manifest.json", null, null, null, null, DateTimeOffset.UtcNow);
        store.Save(expected);
        Assert.Equal(expected, store.Load());
    }

    [Fact]
    public void PackageValidator_RecognizesUnsafePaths()
    {
        Assert.True(UpdatePackageValidator.IsUnsafeEntry("../outside.txt"));
        Assert.True(UpdatePackageValidator.IsUnsafeEntry("C:/outside.txt"));
        Assert.False(UpdatePackageValidator.IsUnsafeEntry("Assets/app.dll"));
    }

    private sealed class UpdatePackageFixture : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private readonly TemporaryDirectory _directory = new();
        public string ManifestPath { get; }
        public string PackagePath { get; }
        public UpdatePackageValidator Validator { get; } = new();

        public UpdatePackageFixture(
            string manifestVersion = "1.11.0",
            string minimumCompatibleVersion = "1.10.0",
            string? shaOverride = null,
            string? extraEntry = null,
            string? deploymentTarget = null)
        {
            PackagePath = Path.Combine(_directory.Path, "package.zip");
            ManifestPath = Path.Combine(_directory.Path, "manifest.json");
            using (var archive = ZipFile.Open(PackagePath, ZipArchiveMode.Create))
            {
                var executable = archive.CreateEntry("ArteInventariado.WinForms.exe");
                using (var writer = new StreamWriter(executable.Open()))
                    writer.Write("test executable");
                if (extraEntry is not null)
                {
                    var unsafeEntry = archive.CreateEntry(extraEntry);
                    using var unsafeWriter = new StreamWriter(unsafeEntry.Open());
                    unsafeWriter.Write("unsafe");
                }
            }
            var manifest = new UpdateManifest(
                manifestVersion, minimumCompatibleVersion, null, "package.zip",
                shaOverride ?? UpdatePackageValidator.ComputeSha256Async(PackagePath).GetAwaiter().GetResult(),
                new FileInfo(PackagePath).Length, "Actualización de prueba",
                DeploymentTarget: deploymentTarget);
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        }

        public void Dispose() => _directory.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arte-update-tests-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { } }
    }
}
