using System.Security.Cryptography;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;

namespace ArteInventariado.Tests;

public sealed class ProtectedRestoreCandidateStagerTests
{
    [Fact]
    public async Task StageAsync_CopiesExactBytesToGuidPathAndPublishesOnlyAibak()
    {
        using var fixture = new StagerFixture();
        var source = fixture.CreateSource("backup-from-usb.aibak", RandomNumberGenerator.GetBytes(257_123));
        var expectedHash = await BackupPackageEngine.ComputeSha256Async(source, CancellationToken.None);
        var requestId = Guid.NewGuid();
        var stager = new ProtectedRestoreCandidateStager(_ => true, () => true, allowSyntheticRoots: true);

        var staged = await stager.StageAsync(fixture.Paths, source, requestId, expectedHash);

        Assert.Equal(Path.Combine(fixture.Paths.LocalRestoreStagingRoot, $"{requestId:N}.aibak"), staged.Path);
        Assert.Equal(expectedHash, staged.Sha256);
        Assert.Equal(new FileInfo(source).Length, staged.SizeBytes);
        Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(staged.Path));
        Assert.Equal(new[] { staged.Path }, Directory.GetFiles(fixture.Paths.LocalRestoreStagingRoot));
    }

    [Fact]
    public async Task StageAsync_HashMismatchDeletesPartialAndPublishesNoCandidate()
    {
        using var fixture = new StagerFixture();
        var source = fixture.CreateSource("backup-from-usb.aibak", RandomNumberGenerator.GetBytes(4096));
        var stager = new ProtectedRestoreCandidateStager(_ => true, () => true, allowSyntheticRoots: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => stager.StageAsync(
            fixture.Paths,
            source,
            Guid.NewGuid(),
            new string('0', 64)));

        Assert.Empty(Directory.GetFiles(fixture.Paths.LocalRestoreStagingRoot));
    }

    [Fact]
    public void ValidateSourcePathSyntax_RejectsNetworkPaths()
    {
        Assert.Throws<InvalidDataException>(() =>
            ProtectedRestoreCandidateStager.ValidateSourcePathSyntax("\\\\server\\share\\backup.aibak"));
    }

    [Fact]
    public void ValidateSourcePathSyntax_RejectsNonAibakExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), "backup.zip");
        Assert.Throws<InvalidDataException>(() => ProtectedRestoreCandidateStager.ValidateSourcePathSyntax(path));
    }

    private sealed class StagerFixture : IDisposable
    {
        public StagerFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.RestoreStager.{Guid.NewGuid():N}");
            var install = Path.Combine(Root, "ProgramFiles", "ArteInventariado");
            var data = Path.Combine(Root, "ProgramData", "ArteInventariado");
            var user = Path.Combine(Root, "UserData", "ArteInventariado");
            Paths = ArteInventariadoPathProvider.CreateMachineWide(install, data, user);
            Directory.CreateDirectory(Paths.MachineInstallRoot);
            Directory.CreateDirectory(Paths.MachineDataRoot);
            Directory.CreateDirectory(Paths.StationRoot);
            Directory.CreateDirectory(Paths.StationOperationalRoot);
            File.WriteAllText(Paths.StationSettingsPath, "{}");
            File.WriteAllText(Paths.StationHelperPath, "helper");
            File.WriteAllText(Paths.StationOperationalSettingsPath, "{}");
            SourceRoot = Path.Combine(Root, "USB");
            Directory.CreateDirectory(SourceRoot);
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public ArteInventariadoPathProvider Paths { get; }

        public string CreateSource(string fileName, byte[] content)
        {
            var path = Path.Combine(SourceRoot, fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
