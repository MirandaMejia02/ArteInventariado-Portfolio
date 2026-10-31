using System.IO.Compression;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class MachineWideBackupF2Tests
{
    [Fact]
    public async Task TrustBoundaryCopiesCanonicalProductAndManagedAssets()
    {
        using var fixture = new Fixture();
        var productPath = Path.Combine(fixture.Paths.ProductBusinessAssetsRoot, "product.txt");
        var managedRelative = "Products/managed.txt";
        var managedPath = Path.Combine(fixture.Paths.AssetsRoot, "Products", "managed.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(productPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
        await File.WriteAllTextAsync(productPath, "product");
        await File.WriteAllTextAsync(managedPath, "managed");
        var managedHash = await BackupPackageEngine.ComputeSha256Async(managedPath, CancellationToken.None);

        var boundary = new MachineWideBackupAssetTrustBoundary(
            fixture.Paths.ProductBusinessAssetsRoot,
            fixture.Paths.AssetsRoot);
        var result = await boundary.PrepareAsync(
            new[] { (1, productPath) },
            new[]
            {
                new BackupPackageEngine.UntrustedManagedAssetSource(
                    1,
                    ProductAssetOwnerKind.Product,
                    7,
                    1,
                    managedRelative,
                    "managed.txt",
                    new FileInfo(managedPath).Length,
                    managedHash)
            },
            fixture.Staging,
            CancellationToken.None);

        Assert.Equal(BackupContentStatus.Healthy, result.ContentStatus);
        Assert.Equal(0, result.OmittedAssetCount);
        Assert.Equal(2, result.Assets.Count);
        Assert.All(result.Assets, asset => Assert.False(asset.WasMissing));
        Assert.True(File.Exists(Path.Combine(
            fixture.Staging,
            "assets",
            "Products",
            "managed.txt")));
        Assert.Contains(result.Assets, asset => asset.PackagePath.StartsWith(
            "business-assets/products/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrustBoundaryOmitsExternalTraversalMissingAndReparseAssetsWithoutReadingThem()
    {
        using var fixture = new Fixture();
        var canonical = Path.Combine(fixture.Paths.ProductBusinessAssetsRoot, "canonical.txt");
        var external = Path.Combine(fixture.Root, "external-secret.txt");
        Directory.CreateDirectory(fixture.Paths.ProductBusinessAssetsRoot);
        await File.WriteAllTextAsync(canonical, "canonical");
        await File.WriteAllTextAsync(external, "secret content must stay outside the backup");

        var sources = new List<(int ProductId, string Path)>
        {
            (1, canonical),
            (2, external),
            (3, Path.Combine(fixture.Paths.ProductBusinessAssetsRoot, "missing.txt")),
            (4, Path.Combine(fixture.Paths.ProductBusinessAssetsRoot, "..", "outside.txt"))
        };

        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.CreateSymbolicLink(
                    Path.Combine(fixture.Paths.ProductBusinessAssetsRoot, "reparse.txt"),
                    canonical);
                sources.Add((5, Path.Combine(fixture.Paths.ProductBusinessAssetsRoot, "reparse.txt")));
            }
            catch (IOException)
            {
                // Creating links may be disabled on a developer workstation;
                // the dedicated path guard remains covered by traversal and
                // external-path assertions below.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var boundary = new MachineWideBackupAssetTrustBoundary(
            fixture.Paths.ProductBusinessAssetsRoot,
            fixture.Paths.AssetsRoot);
        var result = await boundary.PrepareAsync(
            sources,
            Array.Empty<BackupPackageEngine.UntrustedManagedAssetSource>(),
            fixture.Staging,
            CancellationToken.None);

        Assert.Equal(BackupContentStatus.DegradedAssets, result.ContentStatus);
        Assert.Equal(sources.Count - 1, result.OmittedAssetCount);
        Assert.Contains(result.Assets, asset =>
            asset.ProductId == 2 && asset.IssueKind == BackupAssetIssueKind.OutsideCanonicalRoot);
        Assert.Contains(result.Assets, asset =>
            asset.ProductId == 3 && asset.IssueKind == BackupAssetIssueKind.Missing);
        Assert.Contains(result.Assets, asset =>
            asset.ProductId == 4 && asset.IssueKind == BackupAssetIssueKind.Traversal);
        if (sources.Count == 5)
        {
            Assert.Contains(result.Assets, asset =>
                asset.ProductId == 5 && asset.IssueKind == BackupAssetIssueKind.ReparsePoint);
        }

        Assert.DoesNotContain(
            Directory.EnumerateFiles(fixture.Staging, "*", SearchOption.AllDirectories),
            path => File.ReadAllText(path).Contains("secret content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DegradedManifestRemainsStructurallyValidAndMarksOmittedAssets()
    {
        using var fixture = new Fixture();
        await fixture.InitializeDatabaseAsync();
        var engine = new BackupPackageEngine();
        var snapshot = Path.Combine(fixture.Root, "snapshot.db");
        await engine.CreateSnapshotAsync(fixture.Paths.DatabasePath, snapshot, CancellationToken.None);
        var migrations = await engine.ReadAppliedMigrationsAsync(snapshot, CancellationToken.None);
        var hash = await BackupPackageEngine.ComputeSha256Async(snapshot, CancellationToken.None);
        var manifest = new BackupManifest(
            BackupPackageEngine.CurrentFormatVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.12.0",
            "SQLite",
            migrations,
            migrations.LastOrDefault(),
            hash,
            new FileInfo(snapshot).Length,
            Guid.NewGuid(),
            InstallationMode.Principal,
            BackupKind.Automatic,
            new[]
            {
                new BackupAssetManifest(
                    77,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    true,
                    IssueKind: BackupAssetIssueKind.OutsideCanonicalRoot)
            },
            BackupContentStatus.DegradedAssets,
            1);
        var staging = Path.Combine(fixture.Root, "package-staging");
        Directory.CreateDirectory(staging);
        var package = Path.Combine(fixture.Root, "degraded.aibak");
        await engine.CreatePackageAsync(staging, snapshot, manifest, package, CancellationToken.None);

        var validation = await engine.ValidatePackageAsync(
            package,
            migrations,
            Path.Combine(fixture.Root, "validation"),
            CancellationToken.None);

        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));
        Assert.Equal(BackupContentStatus.DegradedAssets, validation.Manifest!.ContentStatus);
        Assert.Equal(1, validation.Manifest.OmittedAssetCount);
        Assert.Contains(validation.Warnings, warning => warning.Contains("OutsideCanonicalRoot", StringComparison.Ordinal));
    }

    [Fact]
    public void RetentionNeverDeletesTheLastHealthyWhenADegradedRunIsNewer()
    {
        using var fixture = new Fixture();
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths) with
        {
            DailyGenerations = 1,
            WeeklyGenerations = 1,
            MonthlyGenerations = 1
        };
        var healthy = new AutomaticBackupCandidate(
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "healthy.aibak"),
            Manifest(DateTimeOffset.UtcNow.AddDays(-40), BackupContentStatus.Healthy));
        var degraded = new AutomaticBackupCandidate(
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "degraded.aibak"),
            Manifest(DateTimeOffset.UtcNow, BackupContentStatus.DegradedAssets, 1));

        var plan = MachineWideBackupRetention.BuildPlan(new[] { healthy, degraded }, policy);

        Assert.Contains(healthy.FilePath, plan.KeptFiles);
        Assert.DoesNotContain(healthy.FilePath, plan.DeletableFiles);
        Assert.Contains(degraded.FilePath, plan.KeptFiles);
    }

    [Fact]
    public void RetentionCanReturnToNormalOnceANewerHealthyBackupExists()
    {
        using var fixture = new Fixture();
        var policy = MachineWideBackupPolicyStore.CreateDefault(fixture.Paths) with
        {
            DailyGenerations = 1,
            WeeklyGenerations = 1,
            MonthlyGenerations = 1
        };
        var oldHealthy = new AutomaticBackupCandidate(
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "old-healthy.aibak"),
            Manifest(DateTimeOffset.UtcNow.AddDays(-500), BackupContentStatus.Healthy));
        var newestHealthy = new AutomaticBackupCandidate(
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "new-healthy.aibak"),
            Manifest(DateTimeOffset.UtcNow, BackupContentStatus.Healthy));
        var degraded = new AutomaticBackupCandidate(
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "degraded.aibak"),
            Manifest(DateTimeOffset.UtcNow.AddMinutes(-1), BackupContentStatus.DegradedAssets, 1));

        var plan = MachineWideBackupRetention.BuildPlan(new[] { oldHealthy, newestHealthy, degraded }, policy);

        Assert.DoesNotContain(oldHealthy.FilePath, plan.KeptFiles);
        Assert.Contains(oldHealthy.FilePath, plan.DeletableFiles);
        Assert.Contains(newestHealthy.FilePath, plan.KeptFiles);
    }

    [Fact]
    public void BackupStatusPersistsHealthyDegradedAndFailureOutcomes()
    {
        using var fixture = new Fixture();
        var store = new MachineWideBackupPolicyStore();
        store.EnsureInitialized(fixture.Paths);

        store.SaveStatus(
            fixture.Paths,
            MachineWideBackupPolicyStore.EmptyStatus with
            {
                LastOutcome = MachineWideBackupStatusKind.SuccessDegradedAssets,
                LastOmittedAssetCount = 2
            });
        var degraded = store.LoadStatus(fixture.Paths);
        Assert.Equal(MachineWideBackupStatusKind.SuccessDegradedAssets, degraded.LastOutcome);
        Assert.Equal(2, degraded.LastOmittedAssetCount);
        Assert.Contains(
            "SuccessDegradedAssets",
            File.ReadAllText(fixture.Paths.ProtectedBackupStatusPath),
            StringComparison.Ordinal);

        store.SaveStatus(
            fixture.Paths,
            degraded with { LastOutcome = MachineWideBackupStatusKind.SuccessHealthy, LastOmittedAssetCount = 0 });
        Assert.Equal(MachineWideBackupStatusKind.SuccessHealthy, store.LoadStatus(fixture.Paths).LastOutcome);

        store.SaveStatus(
            fixture.Paths,
            degraded with { LastOutcome = MachineWideBackupStatusKind.Failure, LastOmittedAssetCount = 0 });
        Assert.Equal(MachineWideBackupStatusKind.Failure, store.LoadStatus(fixture.Paths).LastOutcome);
    }

    [Fact]
    public async Task ManagedTraversalAndMissingAssetsAreDegradedWithoutPackageCopy()
    {
        using var fixture = new Fixture();
        var managedSources = new[]
        {
            new BackupPackageEngine.UntrustedManagedAssetSource(
                1,
                ProductAssetOwnerKind.Product,
                1,
                1,
                "Products/../outside.txt",
                "outside.txt",
                10,
                "AA"),
            new BackupPackageEngine.UntrustedManagedAssetSource(
                2,
                ProductAssetOwnerKind.Sku,
                2,
                2,
                "Skus/missing.txt",
                "missing.txt",
                10,
                "AA")
        };

        var boundary = new MachineWideBackupAssetTrustBoundary(
            fixture.Paths.ProductBusinessAssetsRoot,
            fixture.Paths.AssetsRoot);
        var result = await boundary.PrepareAsync(
            Array.Empty<(int ProductId, string Path)>(),
            managedSources,
            fixture.Staging,
            CancellationToken.None);

        Assert.Equal(BackupContentStatus.DegradedAssets, result.ContentStatus);
        Assert.Equal(2, result.OmittedAssetCount);
        Assert.Contains(result.Assets, asset =>
            asset.ProductId == 1 && asset.IssueKind == BackupAssetIssueKind.Traversal);
        Assert.Contains(result.Assets, asset =>
            asset.ProductId == 2 && asset.IssueKind == BackupAssetIssueKind.Missing);
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(fixture.Staging, "assets"),
            "*",
            SearchOption.AllDirectories));
    }

    [Fact]
    public void LegacyManifestDefaultsToHealthyWithoutChangingItsConstructorContract()
    {
        var manifest = new BackupManifest(
            1,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.11.0",
            "SQLite",
            [],
            null,
            "AA",
            1,
            Guid.NewGuid(),
            InstallationMode.Principal,
            BackupKind.Automatic,
            []);

        Assert.Equal(BackupContentStatus.Healthy, manifest.ContentStatus);
        Assert.Null(manifest.OmittedAssetCount);
    }

    private static BackupManifest Manifest(
        DateTimeOffset createdUtc,
        BackupContentStatus status,
        int omitted = 0) => new(
        1,
        Guid.NewGuid(),
        createdUtc,
        createdUtc.ToLocalTime(),
        "1.12.0",
        "SQLite",
        [],
        null,
        "AA",
        1,
        Guid.NewGuid(),
        InstallationMode.Principal,
        BackupKind.Automatic,
        [],
        status,
        omitted == 0 ? null : omitted);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-backup-f2-{Guid.NewGuid():N}");

        public ArteInventariadoPathProvider Paths { get; }
        public string Staging { get; }

        public Fixture()
        {
            Paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(Root, "install"),
                Path.Combine(Root, "data"),
                Path.Combine(Root, "user"));
            Staging = Path.Combine(Root, "staging");
            Directory.CreateDirectory(Paths.MachineDataRoot);
            Directory.CreateDirectory(Paths.DataRoot);
            Directory.CreateDirectory(Paths.AssetsRoot);
            Directory.CreateDirectory(Paths.BusinessAssetsRoot);
            Directory.CreateDirectory(Paths.ProductBusinessAssetsRoot);
            Directory.CreateDirectory(Paths.ProtectedBackupsRoot);
            Directory.CreateDirectory(Staging);
        }

        public async Task InitializeDatabaseAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false,
                    ForeignKeys = true
                }.ToString())
                .Options;
            await using var context = new AppDbContext(options);
            await DatabaseInitializer.InicializarAsync(context);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
