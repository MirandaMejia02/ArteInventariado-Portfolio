using System.IO.Compression;
using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class BackupRestoreTests
{
    [Fact]
    public async Task V110CanonicalData_RoundTripsWithSkuInventoryAssetsBarcodesAndSnapshots()
    {
        await using var source = await BackupFixture.CreateAsync(InstallationMode.Principal, "Origen v1.10");
        await source.SeedCanonicalV110DataAsync();
        var expected = await source.ReadCanonicalFingerprintAsync();
        var backup = await source.Service.CreateBackupAsync(
            new CreateBackupRequest(BackupKind.Manual, source.BackupsDirectory));

        await using var destination = await BackupFixture.CreateAsync(InstallationMode.Consulta, "Destino local");
        var destinationIdentity = destination.Installation.Status.InstallationId;
        await destination.Service.PrepareRestoreAsync(new PrepareRestoreRequest(
            backup.FilePath, BackupFixture.Password, true));
        var restored = destination.Processor.ProcessPendingRestore();

        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal(expected, await destination.ReadCanonicalFingerprintAsync());
        Assert.Equal(destinationIdentity, destination.Installation.Status.InstallationId);
        Assert.Equal(InstallationMode.Consulta, destination.Installation.Status.Mode);
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task ManagedProductAssets_ArePackagedAndRestoredWithoutThumbnailCache()
    {
        await using var source = await BackupFixture.CreateAsync(InstallationMode.Principal, "Con galeria");
        var relative = await source.AddManagedProductAssetAsync();
        var backup = await source.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        var validation = await source.Service.ValidateBackupAsync(backup.FilePath);
        var asset = Assert.Single(validation.Manifest!.Assets, item => item.OwnerKind == ProductAssetOwnerKind.Product);
        Assert.Equal(relative, asset.ManagedRelativePath);
        using (var archive = ZipFile.OpenRead(backup.FilePath))
            Assert.Contains(archive.Entries, entry => entry.FullName == $"assets/{relative}");

        await using var destination = await BackupFixture.CreateAsync(InstallationMode.Principal, "Destino");
        await destination.Service.PrepareRestoreAsync(new PrepareRestoreRequest(backup.FilePath, BackupFixture.Password, true));
        var result = destination.Processor.ProcessPendingRestore();
        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(Path.Combine(destination.ManagedAssetsDirectory,
            relative.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(Directory.Exists(Path.Combine(destination.Root, "Cache", "Thumbnails")));
    }

    [Fact]
    public async Task ManualBackup_WithWalOpenConnection_ProducesValidSelfContainedPackageAndAssets()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Producto WAL");
        await using var walConnection = await fixture.OpenWalConnectionAsync();
        await BackupFixture.InsertCommittedCategoryThroughWalAsync(walConnection, "Categoria WAL");

        var result = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        var validation = await fixture.Service.ValidateBackupAsync(result.FilePath);

        Assert.True(validation.IsValid, string.Join(" ", validation.Errors));
        Assert.Equal(BackupKind.Manual, validation.Manifest!.Kind);
        Assert.Equal(InstallationMode.Principal, validation.Manifest.SourceInstallationMode);
        Assert.Single(validation.Manifest.Assets, asset => !asset.WasMissing);
        Assert.True(File.Exists(result.FilePath));
        Assert.False(File.Exists(result.FilePath + "-wal"));
        using var archive = ZipFile.OpenRead(result.FilePath);
        Assert.NotNull(archive.GetEntry(BackupPackageEngine.ManifestEntryName));
        Assert.NotNull(archive.GetEntry(BackupPackageEngine.DatabaseEntryName));
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("business-assets/products/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SharedProductImage_IsPackagedAndValidatedOnceThenRestoredForEveryProduct()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Producto principal");
        await fixture.AddProductAsync("P-SHARED", "Producto compartido", fixture.SourceImagePath);

        var backup = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        var validation = await fixture.Service.ValidateBackupAsync(backup.FilePath);

        Assert.True(validation.IsValid, string.Join(" ", validation.Errors));
        var assets = validation.Manifest!.Assets.Where(asset => !asset.WasMissing).ToList();
        Assert.Equal(2, assets.Count);
        Assert.Single(assets.Select(asset => asset.PackagePath).Distinct(StringComparer.OrdinalIgnoreCase));
        using (var archive = ZipFile.OpenRead(backup.FilePath))
        {
            Assert.Single(archive.Entries, entry =>
                entry.FullName.StartsWith("business-assets/products/", StringComparison.Ordinal));
        }

        File.Delete(fixture.SourceImagePath);
        await fixture.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(backup.FilePath, BackupFixture.Password, true));
        var startup = fixture.Processor.ProcessPendingRestore();
        var restoredPaths = await fixture.ReadProductImagePathsAsync();

        Assert.True(startup.Succeeded, startup.Message);
        Assert.Equal(2, restoredPaths.Count);
        Assert.Single(restoredPaths.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.All(restoredPaths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task SharedAsset_WithContradictoryMetadata_IsRejected()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Producto principal");
        await fixture.AddProductAsync("P-SHARED", "Producto compartido", fixture.SourceImagePath);
        var backup = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        var tampered = Path.Combine(fixture.Root, "shared-contradictory.aibak");
        RewritePackage(backup.FilePath, tampered, manifest => manifest with
        {
            Assets = manifest.Assets
                .Select((asset, index) => index == 1
                    ? asset with { Sha256 = new string('A', 64) }
                    : asset)
                .ToList()
        });

        var validation = await fixture.Service.ValidateBackupAsync(tampered);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("contradictorio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EmptyMigratedDatabase_CreatesAndValidatesSnapshot()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Producto base");
        var emptyPath = Path.Combine(fixture.Root, "empty.db");
        await using (var context = fixture.CreateContext(emptyPath))
        {
            await context.Database.MigrateAsync();
        }

        var snapshot = Path.Combine(fixture.Root, "empty-snapshot.db");
        await fixture.Engine.CreateSnapshotAsync(emptyPath, snapshot, CancellationToken.None);
        var errors = await fixture.Engine.ValidateDatabaseAsync(snapshot, fixture.CurrentMigrations, false, CancellationToken.None);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(BackupFrequency.Daily, 1)]
    [InlineData(BackupFrequency.Weekly, 7)]
    [InlineData(BackupFrequency.EveryFifteenDays, 15)]
    public void Frequencies_CalculateExpectedDueDate(BackupFrequency frequency, int days)
    {
        var start = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(start.AddDays(days), BackupMaintenanceService.CalculateNext(start, frequency));
    }

    [Fact]
    public void MonthlyFrequency_PreservesCalendarSemantics()
    {
        var start = new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 10, 0, 0, TimeSpan.Zero),
            BackupMaintenanceService.CalculateNext(start, BackupFrequency.Monthly));
    }

    [Fact]
    public async Task AutomaticBackup_CatchesMissedCycleAndRetentionOnlyDeletesAutomaticPackages()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Retencion");
        await fixture.Service.UpdateSettingsAsync(new UpdateBackupSettingsRequest(
            true, fixture.BackupsDirectory, BackupFrequency.Daily, 1));
        var manual = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        var restore = await fixture.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(manual.FilePath, BackupFixture.Password, true));

        Assert.NotNull(await fixture.Service.RunAutomaticBackupIfDueAsync());
        fixture.Time.Advance(TimeSpan.FromDays(2));
        Assert.NotNull(await fixture.Service.RunAutomaticBackupIfDueAsync());

        var history = await fixture.Service.GetHistoryAsync();
        Assert.Single(history, item => item.Kind == BackupKind.Automatic);
        Assert.Contains(history, item => item.FilePath == manual.FilePath && item.Kind == BackupKind.Manual);
        Assert.Contains(history, item => item.FilePath == restore.PreventiveBackupPath && item.Kind == BackupKind.PreRestore);
    }

    [Fact]
    public async Task CancellationBeforePublish_LeavesNoPartialOrCompletedPackage()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Cancelar");
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(value =>
        {
            if (value.Stage == "assets")
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CreateBackupAsync(
            new CreateBackupRequest(BackupKind.Manual), progress, cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(fixture.BackupsDirectory, "*.aibak"));
        Assert.Empty(Directory.EnumerateFiles(fixture.BackupsDirectory, "*.uploading-*"));
    }

    [Fact]
    public async Task ConcurrentBackup_IsRejectedWhileFirstOperationIsActive()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Concurrencia");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blocker = new CallbackProgress(value =>
        {
            if (value.Stage == "snapshot")
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        });
        var first = Task.Run(() => fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual), blocker));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual)));
        release.Set();
        await first;

        Assert.Contains("operacion", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidDestinationAndMissingPermission_AreRejectedByServiceBoundary()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Permisos");
        var fileInsteadOfDirectory = Path.Combine(fixture.Root, "not-a-directory");
        await File.WriteAllTextAsync(fileInsteadOfDirectory, "x");
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.UpdateSettingsAsync(
            new UpdateBackupSettingsRequest(true, fileInsteadOfDirectory, BackupFrequency.Daily, 10)));

        await fixture.SwitchToUnprivilegedEmployeeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual)));
    }

    [Fact]
    public async Task CorruptPackageChecksumAndFutureManifest_AreRejectedWithoutTouchingDatabase()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Validacion");
        var backup = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        var beforeHash = await BackupPackageEngine.ComputeSha256Async(fixture.DatabasePath, CancellationToken.None);

        var tampered = Path.Combine(fixture.Root, "tampered.aibak");
        RewritePackage(backup.FilePath, tampered, manifest => manifest with { DatabaseSha256 = new string('0', 64) });
        var checksum = await fixture.Service.ValidateBackupAsync(tampered);
        Assert.False(checksum.IsValid);
        Assert.Contains(checksum.Errors, error => error.Contains("checksum", StringComparison.OrdinalIgnoreCase));

        var future = Path.Combine(fixture.Root, "future.aibak");
        RewritePackage(backup.FilePath, future, manifest => manifest with
        {
            AppliedMigrations = [.. manifest.AppliedMigrations, "99999999999999_FutureMigration"]
        });
        var futureValidation = await fixture.Service.ValidateBackupAsync(future);
        Assert.False(futureValidation.IsValid);
        Assert.Contains(futureValidation.Errors, error => error.Contains("version mas reciente", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeHash, await BackupPackageEngine.ComputeSha256Async(fixture.DatabasePath, CancellationToken.None));
    }

    [Fact]
    public async Task ForeignKeyViolation_IsRejected()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Foreign key");
        var invalid = Path.Combine(fixture.Root, "foreign-invalid.db");
        File.Copy(fixture.DatabasePath, invalid);
        await using (var connection = new SqliteConnection($"Data Source={invalid};Mode=ReadWrite;Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys=OFF;
                CREATE TABLE test_parent(id INTEGER PRIMARY KEY);
                CREATE TABLE test_child(id INTEGER PRIMARY KEY, parent_id INTEGER REFERENCES test_parent(id));
                INSERT INTO test_child(id, parent_id) VALUES(1, 999);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var errors = await fixture.Engine.ValidateDatabaseAsync(invalid, fixture.CurrentMigrations, false, CancellationToken.None);
        Assert.Contains(errors, error => error.Contains("foreign_key_check", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestoreRequiresCorrectReauthenticationAndCreatesPreventiveBackup()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Original");
        var backup = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        await fixture.RenameProductAsync("Modificado");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(backup.FilePath, "incorrecta", true)));
        Assert.Equal("Modificado", await fixture.ReadProductNameAsync());

        var prepared = await fixture.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(backup.FilePath, BackupFixture.Password, true));
        Assert.True(prepared.RestartRequired);
        Assert.True(File.Exists(prepared.PreventiveBackupPath));
        var preventive = await fixture.Service.ValidateBackupAsync(prepared.PreventiveBackupPath);
        Assert.Equal(BackupKind.PreRestore, preventive.Manifest!.Kind);
        Assert.True(fixture.PendingStore.Load() is not null);
    }

    [Fact]
    public async Task PrincipalBackup_RestoredOnConsulta_PreservesDestinationIdentityAndReadOnlyModeAndAssets()
    {
        await using var source = await BackupFixture.CreateAsync(InstallationMode.Principal, "Estado X");
        var sourceId = source.Installation.Status.InstallationId;
        var backup = await source.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));

        await using var destination = await BackupFixture.CreateAsync(InstallationMode.Consulta, "Estado Y");
        var destinationId = destination.Installation.Status.InstallationId;
        var prepared = await destination.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(backup.FilePath, BackupFixture.Password, true));
        var startup = destination.Processor.ProcessPendingRestore();

        Assert.True(startup.Succeeded, startup.Message);
        Assert.Equal("Estado X", await destination.ReadProductNameAsync());
        Assert.NotEqual(sourceId, destinationId);
        Assert.Equal(destinationId, destination.Installation.Status.InstallationId);
        Assert.Equal(InstallationMode.Consulta, destination.Installation.Status.Mode);
        var restoredImage = await destination.ReadProductImagePathAsync();
        Assert.NotNull(restoredImage);
        Assert.True(File.Exists(restoredImage));
        await AssertConsultaWriteDeniedAsync(destination);
        Assert.False(prepared.RequiresConsultaToPrincipalWarning);
    }

    [Fact]
    public async Task ConsultaBackup_RestoredOnPrincipal_WarnsAndPreservesPrincipalMode()
    {
        await using var source = await BackupFixture.CreateAsync(InstallationMode.Consulta, "Copia Consulta");
        var backup = await source.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        Assert.Equal(InstallationMode.Consulta, backup.Manifest.SourceInstallationMode);

        await using var destination = await BackupFixture.CreateAsync(InstallationMode.Principal, "Principal actual");
        var destinationId = destination.Installation.Status.InstallationId;
        var prepared = await destination.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(backup.FilePath, BackupFixture.Password, true));
        var startup = destination.Processor.ProcessPendingRestore();

        Assert.True(prepared.RequiresConsultaToPrincipalWarning);
        Assert.True(startup.Succeeded, startup.Message);
        Assert.Equal("Copia Consulta", await destination.ReadProductNameAsync());
        Assert.Equal(destinationId, destination.Installation.Status.InstallationId);
        Assert.Equal(InstallationMode.Principal, destination.Installation.Status.Mode);
    }

    [Fact]
    public async Task CorruptPendingRestore_FailsBeforeReplacingCurrentDatabase()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Intacto");
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.PendingStore.Path)!);
        await File.WriteAllTextAsync(fixture.PendingStore.Path, "{not-json");

        var result = fixture.Processor.ProcessPendingRestore();

        Assert.True(result.HadPendingRestore);
        Assert.False(result.Succeeded);
        Assert.Equal("Intacto", await fixture.ReadProductNameAsync());
        Assert.False(File.Exists(fixture.PendingStore.Path));
    }

    [Fact]
    public async Task FailureAfterAtomicReplace_RollsBackToLatestValidatedDatabase()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Antes del backup");
        var backup = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        await fixture.RenameProductAsync("Estado actual protegido");
        await fixture.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(backup.FilePath, BackupFixture.Password, true));
        var faultingProcessor = fixture.CreateProcessor(() => throw new IOException("Falla simulada posterior al reemplazo."));

        var result = faultingProcessor.ProcessPendingRestore();

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Equal("Estado actual protegido", await fixture.ReadProductNameAsync());
        Assert.False(File.Exists(fixture.PendingStore.Path));
    }

    [Fact]
    public async Task InvalidSqliteAndForeignDatabase_AreRejectedAsArteInventariadoBackups()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Esquema");
        var invalidSqlite = Path.Combine(fixture.Root, "invalid.db");
        await File.WriteAllTextAsync(invalidSqlite, "esto no es sqlite");
        var invalidErrors = await fixture.Engine.ValidateDatabaseAsync(
            invalidSqlite, fixture.CurrentMigrations, true, CancellationToken.None);
        Assert.NotEmpty(invalidErrors);

        var foreign = Path.Combine(fixture.Root, "foreign.db");
        await using (var connection = new SqliteConnection($"Data Source={foreign};Mode=ReadWriteCreate;Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE otra_aplicacion(id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var foreignErrors = await fixture.Engine.ValidateDatabaseAsync(
            foreign, fixture.CurrentMigrations, true, CancellationToken.None);
        Assert.Contains(foreignErrors, error => error.Contains("tabla requerida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestoreAndConfigurationPermissions_AreEnforcedIndependently()
    {
        await using var fixture = await BackupFixture.CreateAsync(InstallationMode.Principal, "Permisos restore");
        var backup = await fixture.Service.CreateBackupAsync(new CreateBackupRequest(BackupKind.Manual));
        await fixture.SwitchToUnprivilegedEmployeeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateSettingsAsync(
            new UpdateBackupSettingsRequest(true, fixture.BackupsDirectory, BackupFrequency.Weekly, 5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PrepareRestoreAsync(
            new PrepareRestoreRequest(backup.FilePath, BackupFixture.Password, true)));
    }

    private static async Task AssertConsultaWriteDeniedAsync(BackupFixture fixture)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath};Mode=ReadOnly;Pooling=False")
            .AddInterceptors(new InstallationWriteGuardInterceptor(fixture.Installation))
            .Options;
        await using var context = new AppDbContext(options);
        var category = await context.Categorias.FirstAsync();
        category.Nombre = "No permitido";
        await Assert.ThrowsAsync<InstallationWriteDeniedException>(() => context.SaveChangesAsync());
    }

    private static void RewritePackage(
        string source,
        string destination,
        Func<BackupManifest, BackupManifest> transform)
    {
        using var sourceArchive = ZipFile.OpenRead(source);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var targetArchive = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var sourceEntry in sourceArchive.Entries)
        {
            var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
            using var targetStream = targetEntry.Open();
            if (sourceEntry.FullName == BackupPackageEngine.ManifestEntryName)
            {
                using var manifestStream = sourceEntry.Open();
                var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestStream, BackupPackageEngine.JsonOptions)!;
                JsonSerializer.Serialize(targetStream, transform(manifest), BackupPackageEngine.JsonOptions);
            }
            else
            {
                using var sourceStream = sourceEntry.Open();
                sourceStream.CopyTo(targetStream);
            }
        }
    }

    private sealed class CallbackProgress(Action<BackupProgress> callback) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => callback(value);
    }

    private sealed class BackupFixture : IAsyncDisposable
    {
        public static readonly string Password = TestCredentials.Password("backup");
        private readonly TestDbContextFactory _contextFactory;
        private readonly LocalStationConfigurationStore _stationStore;
        private readonly BackupStoragePaths _paths;
        private readonly StationEventLog _eventLog;

        private BackupFixture(
            string root,
            string databasePath,
            string backupsDirectory,
            BackupPackageEngine engine,
            AdjustableTimeProvider time,
            TestDbContextFactory contextFactory,
            LocalStationConfigurationStore stationStore,
            BackupStoragePaths paths,
            StationEventLog eventLog,
            InstallationContext installation,
            CurrentSession session,
            PendingRestoreStore pendingStore,
            BackupMaintenanceService service,
            PendingRestoreProcessor processor,
            IReadOnlyList<string> migrations)
        {
            Root = root;
            DatabasePath = databasePath;
            BackupsDirectory = backupsDirectory;
            Engine = engine;
            Time = time;
            _contextFactory = contextFactory;
            _stationStore = stationStore;
            _paths = paths;
            _eventLog = eventLog;
            Installation = installation;
            Session = session;
            PendingStore = pendingStore;
            Service = service;
            Processor = processor;
            CurrentMigrations = migrations;
            SourceImagePath = Path.Combine(root, "source-image.png");
        }

        public string Root { get; }
        public string DatabasePath { get; }
        public string BackupsDirectory { get; }
        public BackupPackageEngine Engine { get; }
        public AdjustableTimeProvider Time { get; }
        public InstallationContext Installation { get; }
        public CurrentSession Session { get; }
        public PendingRestoreStore PendingStore { get; }
        public BackupMaintenanceService Service { get; }
        public PendingRestoreProcessor Processor { get; }
        public IReadOnlyList<string> CurrentMigrations { get; }
        public string SourceImagePath { get; }
        public string ManagedAssetsDirectory => _paths.ManagedAssetsDirectory!;

        public static async Task<BackupFixture> CreateAsync(InstallationMode mode, string productName)
        {
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.BackupTests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "Data", "arteinventariado.db");
            var backups = Path.Combine(root, "Backups");
            var maintenance = Path.Combine(root, "Maintenance");
            var assets = Path.Combine(root, "BusinessAssets", "Products");
            var managedAssets = Path.Combine(root, "Assets");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            Directory.CreateDirectory(backups);
            Directory.CreateDirectory(assets);

            var contextFactory = new TestDbContextFactory(databasePath);
            int adminId;
            string imagePath = Path.Combine(root, "source-image.png");
            await File.WriteAllBytesAsync(imagePath, [1, 2, 3, 4, 5, 6]);
            await using (var context = contextFactory.CreateDbContext())
            {
                await context.Database.MigrateAsync();
                var admin = new Usuario
                {
                    NombreCompleto = "Administrador",
                    NombreUsuario = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                };
                var category = new Categoria { Nombre = "Categoria", Activa = true, FechaCreacion = DateTime.UtcNow };
                context.AddRange(admin, category);
                await context.SaveChangesAsync();
                context.Productos.Add(new Producto
                {
                    CodigoInterno = $"P-{Guid.NewGuid():N}"[..12],
                    Nombre = productName,
                    CategoriaId = category.CategoriaId,
                    ImagenLocalPath = imagePath,
                    PrecioCompraCentavos = 100,
                    PrecioVentaCentavos = 200,
                    StockActual = 5,
                    StockMinimo = 1,
                    Estado = EstadoProducto.ACTIVO,
                    FechaCreacion = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
                adminId = admin.UsuarioId;
            }

            var stationPath = Path.Combine(root, "Station", "station-settings.json");
            var stationStore = new LocalStationConfigurationStore(stationPath);
            stationStore.EnsureInitialized();
            stationStore.SaveConfigured(mode);
            var installation = new InstallationContext(stationStore);
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(adminId, "Administrador", "admin", RolUsuario.ADMIN));
            var eventLog = new StationEventLog(Path.Combine(root, "Logs", "maintenance.log"));
            var engine = new BackupPackageEngine();
            var paths = new BackupStoragePaths(
                databasePath,
                backups,
                maintenance,
                Path.Combine(maintenance, "pending-restore.json"),
                assets,
                managedAssets);
            var pending = new PendingRestoreStore(paths.PendingRestorePath);
            var permissionService = new EmployeePermissionService(contextFactory, session, new NoOpAuditService());
            var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));
            var service = new BackupMaintenanceService(
                paths, engine, stationStore, pending, contextFactory, installation,
                permissionService, session, eventLog, time);
            var processor = new PendingRestoreProcessor(paths, pending, engine, installation, eventLog);
            await using var migrationContext = contextFactory.CreateDbContext();
            var migrations = migrationContext.Database.GetMigrations().ToList();
            return new BackupFixture(
                root, databasePath, backups, engine, time, contextFactory, stationStore, paths, eventLog,
                installation, session, pending, service, processor, migrations);
        }

        public AppDbContext CreateContext(string? path = null) =>
            path is null ? _contextFactory.CreateDbContext() : new TestDbContextFactory(path).CreateDbContext();

        public async Task<SqliteConnection> OpenWalConnectionAsync()
        {
            var connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadWrite;Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteScalarAsync();
            return connection;
        }

        public static async Task InsertCommittedCategoryThroughWalAsync(SqliteConnection connection, string name)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO categorias(nombre, descripcion, activa, fecha_creacion) VALUES($name, NULL, 1, $date);";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$date", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync();
        }

        public async Task RenameProductAsync(string name)
        {
            await using var context = CreateContext();
            var product = await context.Productos.SingleAsync();
            product.Nombre = name;
            await context.SaveChangesAsync();
        }

        public async Task AddProductAsync(string code, string name, string imagePath)
        {
            await using var context = CreateContext();
            var categoryId = await context.Categorias.Select(category => category.CategoriaId).FirstAsync();
            context.Productos.Add(new Producto
            {
                CodigoInterno = code,
                Nombre = name,
                CategoriaId = categoryId,
                ImagenLocalPath = imagePath,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 200,
                StockActual = 3,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        public async Task<string> AddManagedProductAssetAsync()
        {
            await using var context = CreateContext();
            var productId = await context.Productos.Select(product => product.ProductoId).SingleAsync();
            var key = Guid.NewGuid();
            var relative = $"Products/{key:N}.png";
            var fullPath = Path.Combine(ManagedAssetsDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, TinyPng);
            context.ProductoAssets.Add(new ProductoAsset
            {
                ProductoId = productId, StorageKey = key, RutaRelativa = relative, NombreOriginal = "producto.png",
                MimeType = "image/png", Extension = ".png", SizeBytes = TinyPng.Length, Width = 1, Height = 1,
                Sha256 = await BackupPackageEngine.ComputeSha256Async(fullPath, CancellationToken.None),
                Orden = 0, EsPrincipal = true, FechaCreacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            return relative;
        }

        public async Task SeedCanonicalV110DataAsync()
        {
            await using var context = CreateContext();
            var product = await context.Productos.SingleAsync();
            var adminId = await context.Usuarios.Select(user => user.UsuarioId).SingleAsync();
            var sku = new ProductoSku
            {
                ProductoId = product.ProductoId,
                CodigoSku = $"SKU-{Guid.NewGuid():N}"[..16],
                NombreVariante = "Variante respaldo",
                Activo = true,
                EsPrincipal = true,
                CostoOverrideCentavos = 125
            };
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.EXHIBICION,
                ExistenciaFisica = 2,
                Reservado = 1,
                Version = 1
            });
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.BODEGA,
                ExistenciaFisica = 2,
                Version = 1
            });
            sku.SaldosInventario.Add(new InventarioSaldo
            {
                Ubicacion = UbicacionInventario.SIN_ASIGNAR,
                ExistenciaFisica = 1,
                Version = 1
            });
            sku.Barcodes.Add(new ProductoSkuBarcode
            {
                Codigo = "0012345678905",
                Activo = true,
                EsPrincipal = true
            });
            context.ProductoSkus.Add(sku);
            await context.SaveChangesAsync();

            var key = Guid.NewGuid();
            var relative = $"Skus/{key:N}.png";
            var fullPath = Path.Combine(ManagedAssetsDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, TinyPng);
            context.ProductoSkuAssets.Add(new ProductoSkuAsset
            {
                ProductoSkuId = sku.ProductoSkuId,
                StorageKey = key,
                RutaRelativa = relative,
                NombreOriginal = "sku.png",
                MimeType = "image/png",
                Extension = ".png",
                SizeBytes = TinyPng.Length,
                Width = 1,
                Height = 1,
                Sha256 = await BackupPackageEngine.ComputeSha256Async(fullPath, CancellationToken.None),
                Orden = 0,
                EsPrincipal = true,
                FechaCreacion = DateTime.UtcNow
            });
            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Today),
                FechaApertura = DateTime.UtcNow,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = adminId
            };
            var sale = new Venta
            {
                NumeroVenta = $"VTA-BACKUP-{Guid.NewGuid():N}"[..24],
                Caja = cash,
                UsuarioId = adminId,
                UsuarioNombreSnapshot = "Administrador",
                ClienteNombreSnapshot = "Consumidor final",
                FechaVenta = DateTime.UtcNow,
                SubtotalCentavos = 200,
                TotalCentavos = 200,
                PagadoCentavos = 200,
                Estado = EstadoVenta.COMPLETADA
            };
            sale.Detalles.Add(new DetalleVenta
            {
                ProductoId = product.ProductoId,
                ProductoSkuId = sku.ProductoSkuId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 200,
                CostoUnitarioCentavos = 125,
                OrigenCosto = OrigenCostoHistorico.SKU_OVERRIDE,
                ProductoCodigoSnapshot = product.CodigoInterno,
                ProductoNombreSnapshot = product.Nombre,
                CodigoSkuSnapshot = sku.CodigoSku,
                NombreVarianteSnapshot = sku.NombreVariante,
                SubtotalCentavos = 200,
                TotalCentavos = 200
            });
            context.Ventas.Add(sale);
            await context.SaveChangesAsync();
        }

        public async Task<CanonicalFingerprint> ReadCanonicalFingerprintAsync()
        {
            await using var context = CreateContext();
            return new CanonicalFingerprint(
                await context.ProductoSkus.CountAsync(),
                await context.InventarioSaldos.SumAsync(item => item.ExistenciaFisica),
                await context.InventarioSaldos.SumAsync(item => item.Reservado),
                await context.ProductoSkuAssets.CountAsync(),
                await context.ProductoSkuBarcodes.CountAsync(),
                await context.DetalleVentas.CountAsync(item => item.ProductoSkuId != null && item.CodigoSkuSnapshot != null));
        }

        public async Task<string> ReadProductNameAsync()
        {
            await using var context = CreateContext();
            return await context.Productos.Select(product => product.Nombre).SingleAsync();
        }

        public async Task<string?> ReadProductImagePathAsync()
        {
            await using var context = CreateContext();
            return await context.Productos.Select(product => product.ImagenLocalPath).SingleAsync();
        }

        public async Task<IReadOnlyList<string>> ReadProductImagePathsAsync()
        {
            await using var context = CreateContext();
            return await context.Productos
                .OrderBy(product => product.ProductoId)
                .Select(product => product.ImagenLocalPath!)
                .ToListAsync();
        }

        public async Task SwitchToUnprivilegedEmployeeAsync()
        {
            await using var context = CreateContext();
            var employee = new Usuario
            {
                NombreCompleto = "Empleado",
                NombreUsuario = "empleado",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                Rol = RolUsuario.EMPLEADO,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(employee);
            await context.SaveChangesAsync();
            Session.IniciarSesion(new UsuarioSesion(employee.UsuarioId, employee.NombreCompleto, employee.NombreUsuario, employee.Rol));
        }

        public PendingRestoreProcessor CreateProcessor(Action afterReplace) =>
            new(_paths, PendingStore, Engine, Installation, _eventLog, afterReplace);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record CanonicalFingerprint(
        int Skus,
        int Physical,
        int Reserved,
        int SkuAssets,
        int Barcodes,
        int SkuSnapshots);

    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false,
                    ForeignKeys = true
                }.ToString())
                .Options;
            return new AppDbContext(options);
        }

        public ValueTask<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreateDbContext());
        }
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
