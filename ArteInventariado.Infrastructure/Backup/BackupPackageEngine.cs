using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Backup;

internal sealed class BackupPackageEngine
{
    internal sealed record ManagedAssetSource(
        int ProductId,
        ProductAssetOwnerKind OwnerKind,
        int AssetId,
        int OwnerId,
        string RelativePath,
        string OriginalFileName,
        long DeclaredSizeBytes,
        string DeclaredSha256,
        string FullPath);

    internal sealed record UntrustedManagedAssetSource(
        int ProductId,
        ProductAssetOwnerKind OwnerKind,
        int AssetId,
        int OwnerId,
        string RelativePath,
        string OriginalFileName,
        long DeclaredSizeBytes,
        string DeclaredSha256);
    internal const int CurrentFormatVersion = 1;
    internal const string ManifestEntryName = "manifest.json";
    internal const string DatabaseEntryName = "database.db";
    private const long MaximumManifestBytes = 2 * 1024 * 1024;
    private const long MaximumDatabaseBytes = 20L * 1024 * 1024 * 1024;
    private const long MaximumAssetBytes = 2L * 1024 * 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string[] RequiredTables =
    [
        "__EFMigrationsHistory",
        "usuarios",
        "productos",
        "ventas",
        "configuracion_sistema"
    ];

    public async Task CreateSnapshotAsync(
        string activeDatabasePath,
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(activeDatabasePath))
        {
            throw new FileNotFoundException("No existe la base de datos activa.", activeDatabasePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = activeDatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 30
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30
        };

        await using var source = new SqliteConnection(sourceBuilder.ToString());
        await using var destination = new SqliteConnection(destinationBuilder.ToString());
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
        await destination.CloseAsync();
    }

    public async Task<IReadOnlyList<string>> ValidateDatabaseAsync(
        string databasePath,
        IReadOnlyList<string> currentMigrations,
        bool allowOlderSchema,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (!File.Exists(databasePath))
        {
            return ["El snapshot de base de datos no existe."];
        }

        if (new FileInfo(databasePath).Length is <= 0 or > MaximumDatabaseBytes)
        {
            return ["El tamano del snapshot de base de datos no es valido."];
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                ForeignKeys = true,
                DefaultTimeout = 30
            };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken);

            var integrity = await ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"SQLite integrity_check fallo: {integrity ?? "sin resultado"}.");
            }

            await using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_key_check;";
                await using var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    errors.Add("SQLite foreign_key_check encontro referencias invalidas.");
                }
            }

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    tables.Add(reader.GetString(0));
                }
            }

            foreach (var required in RequiredTables.Where(required => !tables.Contains(required)))
            {
                errors.Add($"La tabla requerida '{required}' no existe; no es una base valida de ArteInventariado.");
            }

            if (tables.Contains("__EFMigrationsHistory"))
            {
                var applied = await ReadAppliedMigrationsAsync(connection, cancellationToken);
                if (applied.Count > currentMigrations.Count ||
                    !applied.SequenceEqual(currentMigrations.Take(applied.Count), StringComparer.Ordinal))
                {
                    errors.Add("El respaldo fue creado por una version mas reciente o incompatible de ArteInventariado.");
                }
                else if (!allowOlderSchema && applied.Count != currentMigrations.Count)
                {
                    errors.Add("El esquema de la base no coincide con la version actual.");
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            errors.Add($"No se pudo abrir o validar SQLite: {ex.Message}");
        }

        return errors;
    }

    public async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return await ReadAppliedMigrationsAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<(int ProductId, string Path)>> ReadProductAssetsAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, string)>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT producto_id, imagen_local_path FROM productos WHERE imagen_local_path IS NOT NULL AND trim(imagen_local_path) <> '';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return result;
    }

    public async Task<IReadOnlyList<ManagedAssetSource>> ReadManagedAssetsAsync(
        string databasePath, string managedAssetsDirectory, CancellationToken cancellationToken)
    {
        var rawSources = await ReadManagedAssetSourcesAsync(databasePath, cancellationToken);
        return rawSources
            .Select(source => new ManagedAssetSource(
                source.ProductId,
                source.OwnerKind,
                source.AssetId,
                source.OwnerId,
                source.RelativePath,
                source.OriginalFileName,
                source.DeclaredSizeBytes,
                source.DeclaredSha256,
                ResolveSafeManagedPath(managedAssetsDirectory, source.RelativePath)))
            .ToList();
    }

    /// <summary>
    /// Reads only the metadata needed by the machine-wide trust boundary.
    /// Relative paths are intentionally returned unresolved; callers that run
    /// with elevated rights must validate them before touching the filesystem.
    /// </summary>
    public async Task<IReadOnlyList<UntrustedManagedAssetSource>> ReadManagedAssetSourcesAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var result = new List<UntrustedManagedAssetSource>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT pa.producto_id, 1, pa.producto_asset_id, pa.producto_id,
                   pa.ruta_relativa, pa.nombre_original, pa.size_bytes, pa.sha256
            FROM producto_assets pa
            UNION ALL
            SELECT ps.producto_id, 2, psa.producto_sku_asset_id, psa.producto_sku_id,
                   psa.ruta_relativa, psa.nombre_original, psa.size_bytes, psa.sha256
            FROM producto_sku_assets psa
            JOIN producto_skus ps ON ps.producto_sku_id = psa.producto_sku_id;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var relative = reader.GetString(4).Replace('\\', '/');
            result.Add(new UntrustedManagedAssetSource(
                reader.GetInt32(0), (ProductAssetOwnerKind)reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                relative, reader.GetString(5), reader.GetInt64(6), reader.GetString(7)));
        }
        return result;
    }

    /// <summary>
    /// Rewrites only recognized legacy product image paths in a staged
    /// migration database.  A path is canonicalized after the copied target
    /// has been independently checked for size/hash equality; arbitrary
    /// external paths remain untouched and are never read.
    /// </summary>
    public async Task<int> CanonicalizeLegacyProductAssetPathsAsync(
        string stagedDatabasePath,
        string recognizedLegacyRoot,
        string stagedCopiedRoot,
        string canonicalTargetRoot,
        CancellationToken cancellationToken)
    {
        var updates = new List<(int ProductId, string Path)>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = stagedDatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT producto_id, imagen_local_path FROM productos WHERE imagen_local_path IS NOT NULL AND trim(imagen_local_path) <> '';";
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var productId = reader.GetInt32(0);
                var rawPath = reader.GetString(1);
                if (!MachineWideAssetPathGuard.TryResolveCanonicalPath(
                        rawPath,
                        recognizedLegacyRoot,
                        out var legacyPath,
                        out _))
                {
                    continue;
                }

                if (!MachineWideAssetPathGuard.TryGetRegularFile(
                        legacyPath,
                        recognizedLegacyRoot,
                        out var legacyFile,
                        out _))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(recognizedLegacyRoot, legacyPath).Replace('\\', '/');
                if (!MachineWideAssetPathGuard.IsSafeRelativePath(relative, out var normalizedRelative))
                {
                    continue;
                }

                var stagedPath = Path.GetFullPath(Path.Combine(
                    stagedCopiedRoot,
                    normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
                if (!MachineWideAssetPathGuard.IsWithin(stagedPath, stagedCopiedRoot) ||
                    !MachineWideAssetPathGuard.TryGetRegularFile(
                        stagedPath,
                        stagedCopiedRoot,
                        out var stagedFile,
                        out _))
                {
                    continue;
                }

                if (legacyFile!.Length != stagedFile!.Length)
                {
                    continue;
                }

                var legacyHash = await ComputeSha256Async(legacyPath, cancellationToken);
                var stagedHash = await ComputeSha256Async(stagedPath, cancellationToken);
                if (!FixedEquals(legacyHash, stagedHash))
                {
                    continue;
                }

                var canonicalPath = Path.GetFullPath(Path.Combine(
                    canonicalTargetRoot,
                    normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
                if (!MachineWideAssetPathGuard.IsWithin(canonicalPath, canonicalTargetRoot) ||
                    string.Equals(
                        Path.GetFullPath(rawPath),
                        canonicalPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                updates.Add((productId, canonicalPath));
            }
        }

        if (updates.Count == 0)
        {
            return 0;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var updateData in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE productos SET imagen_local_path = $path WHERE producto_id = $id;";
            update.Parameters.AddWithValue("$path", updateData.Path);
            update.Parameters.AddWithValue("$id", updateData.ProductId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updates.Count;
    }

    public async Task<IReadOnlyList<BackupAssetManifest>> CopyAssetsToStagingAsync(
        IReadOnlyList<(int ProductId, string Path)> assets,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var manifests = new List<BackupAssetManifest>();
        var targetRoot = Path.Combine(stagingDirectory, "business-assets", "products");
        Directory.CreateDirectory(targetRoot);

        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalName = Path.GetFileName(asset.Path);
            if (!File.Exists(asset.Path))
            {
                manifests.Add(new BackupAssetManifest(asset.ProductId, string.Empty, originalName, 0, string.Empty, true));
                continue;
            }

            var hash = await ComputeSha256Async(asset.Path, cancellationToken);
            var extension = NormalizeExtension(Path.GetExtension(asset.Path));
            var packagePath = $"business-assets/products/{hash.ToLowerInvariant()}{extension}";
            var target = Path.Combine(targetRoot, $"{hash.ToLowerInvariant()}{extension}");
            if (!File.Exists(target))
            {
                await CopyFileAsync(asset.Path, target, cancellationToken);
            }

            manifests.Add(new BackupAssetManifest(
                asset.ProductId,
                packagePath,
                originalName,
                new FileInfo(asset.Path).Length,
                hash,
                false));
        }

        return manifests;
    }

    public async Task<IReadOnlyList<BackupAssetManifest>> CopyManagedAssetsToStagingAsync(
        IReadOnlyList<ManagedAssetSource> assets, string stagingDirectory, CancellationToken cancellationToken)
    {
        var manifests = new List<BackupAssetManifest>();
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(asset.FullPath))
                throw new InvalidDataException($"Falta el asset canonico '{asset.RelativePath}' del producto {asset.ProductId}.");
            var file = new FileInfo(asset.FullPath);
            if (file.Length != asset.DeclaredSizeBytes)
                throw new InvalidDataException($"El tamano del asset canonico '{asset.RelativePath}' no coincide con la base.");
            var hash = await ComputeSha256Async(asset.FullPath, cancellationToken);
            if (!FixedEquals(hash, asset.DeclaredSha256))
                throw new InvalidDataException($"El checksum del asset canonico '{asset.RelativePath}' no coincide con la base.");
            var packagePath = $"assets/{asset.RelativePath}";
            var target = ResolveSafeStagingPath(stagingDirectory, packagePath);
            if (File.Exists(target))
                throw new InvalidDataException($"Existe una ruta de asset administrado duplicada: '{asset.RelativePath}'.");
            await CopyFileAsync(asset.FullPath, target, cancellationToken);
            manifests.Add(new BackupAssetManifest(
                asset.ProductId, packagePath, asset.OriginalFileName, file.Length, hash, false,
                asset.OwnerKind, asset.AssetId, asset.OwnerId, asset.RelativePath));
        }
        return manifests;
    }

    public async Task CreatePackageAsync(
        string stagingDirectory,
        string snapshotPath,
        BackupManifest manifest,
        string packagePath,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(stagingDirectory, ManifestEntryName);
        await File.WriteAllBytesAsync(
            manifestPath,
            JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions),
            cancellationToken);

        if (File.Exists(packagePath))
        {
            throw new IOException("Ya existe un archivo temporal con el mismo nombre.");
        }

        await using var package = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true);
        await AddFileAsync(archive, manifestPath, ManifestEntryName, cancellationToken);
        await AddFileAsync(archive, snapshotPath, DatabaseEntryName, cancellationToken);

        foreach (var asset in manifest.Assets.Where(asset => !asset.WasMissing).DistinctBy(asset => asset.PackagePath))
        {
            var source = ResolveSafeStagingPath(stagingDirectory, asset.PackagePath);
            await AddFileAsync(archive, source, asset.PackagePath, cancellationToken);
        }

        archive.Dispose();
        await package.FlushAsync(cancellationToken);
        package.Flush(flushToDisk: true);
    }

    public async Task<BackupValidationResult> ValidatePackageAsync(
        string packagePath,
        IReadOnlyList<string> currentMigrations,
        string extractionDirectory,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        BackupManifest? manifest = null;

        try
        {
            if (!File.Exists(packagePath))
            {
                return new BackupValidationResult(false, packagePath, null, ["El archivo de respaldo no existe."], []);
            }

            Directory.CreateDirectory(extractionDirectory);
            await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var duplicate = archive.Entries
                .GroupBy(entry => NormalizeEntryName(entry.FullName), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
            {
                errors.Add($"El paquete contiene una entrada duplicada: {duplicate.Key}.");
                return new BackupValidationResult(false, packagePath, null, errors, warnings);
            }

            foreach (var entry in archive.Entries)
            {
                try
                {
                    _ = ResolveSafeStagingPath(extractionDirectory, entry.FullName);
                }
                catch (InvalidDataException)
                {
                    errors.Add("El paquete contiene una ruta de extraccion insegura.");
                    return new BackupValidationResult(false, packagePath, null, errors, warnings);
                }
            }

            var manifestEntry = archive.GetEntry(ManifestEntryName);
            var databaseEntry = archive.GetEntry(DatabaseEntryName);
            if (manifestEntry is null || databaseEntry is null)
            {
                errors.Add("El paquete no contiene manifest.json y database.db.");
                return new BackupValidationResult(false, packagePath, null, errors, warnings);
            }

            if (manifestEntry.Length is <= 0 or > MaximumManifestBytes ||
                databaseEntry.Length is <= 0 or > MaximumDatabaseBytes)
            {
                errors.Add("El paquete declara tamanos de archivo no validos.");
                return new BackupValidationResult(false, packagePath, null, errors, warnings);
            }

            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken);
            }

            if (manifest is null ||
                manifest.BackupFormatVersion != CurrentFormatVersion ||
                manifest.BackupId == Guid.Empty ||
                string.IsNullOrWhiteSpace(manifest.DatabaseSha256) ||
                manifest.AppliedMigrations is null ||
                manifest.Assets is null)
            {
                errors.Add("El manifest no es valido o usa un formato no compatible.");
                return new BackupValidationResult(false, packagePath, manifest, errors, warnings);
            }

            if (!Enum.IsDefined(manifest.Kind) ||
                !Enum.IsDefined(manifest.SourceInstallationMode) ||
                !Enum.IsDefined(manifest.ContentStatus))
            {
                errors.Add("El manifest contiene valores de origen o tipo no validos.");
            }

            var omittedAssetCount = manifest.Assets.Count(asset => asset.WasMissing);
            if (manifest.OmittedAssetCount is < 0)
            {
                errors.Add("El manifest declara una cantidad de assets omitidos invalida.");
            }
            else if (manifest.OmittedAssetCount.HasValue &&
                     manifest.OmittedAssetCount.Value != omittedAssetCount)
            {
                errors.Add("La cantidad de assets omitidos no coincide con el contenido del manifest.");
            }
            else if (manifest.OmittedAssetCount.HasValue &&
                     ((omittedAssetCount == 0 && manifest.ContentStatus != BackupContentStatus.Healthy) ||
                      (omittedAssetCount > 0 && manifest.ContentStatus != BackupContentStatus.DegradedAssets)))
            {
                errors.Add("El estado de contenido del manifest no coincide con sus assets omitidos.");
            }
            else if (manifest.ContentStatus == BackupContentStatus.DegradedAssets)
            {
                warnings.Add($"El respaldo contiene {omittedAssetCount} asset(s) omitido(s); la base de datos sigue disponible para recuperación.");
            }

            var extractedDatabase = Path.Combine(extractionDirectory, DatabaseEntryName);
            await ExtractEntryAsync(databaseEntry, extractedDatabase, cancellationToken);
            if (new FileInfo(extractedDatabase).Length != manifest.DatabaseSizeBytes)
            {
                errors.Add("El tamano del snapshot no coincide con el manifest.");
            }

            var databaseHash = await ComputeSha256Async(extractedDatabase, cancellationToken);
            if (!FixedEquals(databaseHash, manifest.DatabaseSha256))
            {
                errors.Add("El checksum SHA-256 del snapshot no coincide.");
            }

            var actualMigrations = await ReadAppliedMigrationsAsync(extractedDatabase, cancellationToken);
            if (!actualMigrations.SequenceEqual(manifest.AppliedMigrations, StringComparer.Ordinal) ||
                !string.Equals(actualMigrations.LastOrDefault(), manifest.LastMigration, StringComparison.Ordinal))
            {
                errors.Add("Las migraciones declaradas por el manifest no coinciden con el snapshot.");
            }

            if (manifest.AppliedMigrations.Count > currentMigrations.Count ||
                !manifest.AppliedMigrations.SequenceEqual(currentMigrations.Take(manifest.AppliedMigrations.Count), StringComparer.Ordinal))
            {
                errors.Add("Este respaldo fue creado por una version mas reciente de ArteInventariado.");
            }

            var duplicatedProduct = manifest.Assets.Where(asset => asset.OwnerKind is null)
                .GroupBy(asset => asset.ProductId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicatedProduct is not null)
            {
                errors.Add($"El manifest contiene mas de una referencia de asset para el producto {duplicatedProduct.Key}.");
            }

            foreach (var asset in manifest.Assets.Where(asset => asset.WasMissing))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var issue = asset.IssueKind?.ToString() ?? BackupAssetIssueKind.Missing.ToString();
                warnings.Add($"Se omitió el asset del producto {asset.ProductId} ({issue}).");
                if (!string.IsNullOrWhiteSpace(asset.PackagePath) ||
                    !string.IsNullOrWhiteSpace(asset.Sha256) ||
                    asset.SizeBytes != 0)
                {
                    errors.Add($"El asset faltante del producto {asset.ProductId} contiene metadata contradictoria.");
                }
                if (asset.IssueKind is not null && !Enum.IsDefined(asset.IssueKind.Value))
                {
                    errors.Add($"El asset faltante del producto {asset.ProductId} declara una causa no valida.");
                }
                if (asset.OwnerKind is not null &&
                    !string.IsNullOrWhiteSpace(asset.ManagedRelativePath) &&
                    !MachineWideAssetPathGuard.IsSafeRelativePath(asset.ManagedRelativePath, out _))
                {
                    errors.Add($"El asset administrado faltante del producto {asset.ProductId} declara una ruta relativa insegura.");
                }
                // Legacy manifests predate degraded managed-asset metadata.
                // Keep accepting their old missing-product convention, while
                // requiring an explicit issue marker for newly written
                // managed omissions.
                if (asset.OwnerKind is not null && asset.IssueKind is null)
                {
                    errors.Add($"El asset canonico administrado no declara la causa de omision ({asset.ProductId}).");
                }
            }

            var duplicatedManagedAsset = manifest.Assets.Where(asset => asset.OwnerKind is not null)
                .GroupBy(asset => new { asset.OwnerKind, asset.AssetId })
                .FirstOrDefault(group => group.Count() > 1 || group.Key.AssetId is null);
            if (duplicatedManagedAsset is not null)
                errors.Add("El manifest contiene una referencia de asset administrado duplicada o sin identificador.");

            var validAssetReferences = new List<BackupAssetManifest>();
            foreach (var asset in manifest.Assets.Where(asset => !asset.WasMissing))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (asset.SizeBytes is <= 0 or > MaximumAssetBytes ||
                    string.IsNullOrWhiteSpace(asset.PackagePath) ||
                    string.IsNullOrWhiteSpace(asset.Sha256))
                {
                    errors.Add($"El asset del producto {asset.ProductId} tiene metadata no valida.");
                    continue;
                }

                validAssetReferences.Add(asset);
            }

            foreach (var assetGroup in validAssetReferences.GroupBy(
                         asset => NormalizeEntryName(asset.PackagePath),
                         StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var representative = assetGroup.First();
                var entryName = assetGroup.Key;
                if (assetGroup.Any(asset =>
                        asset.SizeBytes != representative.SizeBytes ||
                        !string.Equals(asset.Sha256, representative.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Las referencias compartidas del asset '{entryName}' tienen hash o tamano contradictorio.");
                    continue;
                }

                var expectedPrefix = representative.OwnerKind is null
                    ? "business-assets/products/"
                    : representative.OwnerKind == ProductAssetOwnerKind.Product ? "assets/Products/" : "assets/Skus/";
                if (!entryName.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
                    archive.GetEntry(entryName) is not { } entry)
                {
                    errors.Add($"No se encontro el asset compartido por los productos {string.Join(", ", assetGroup.Select(asset => asset.ProductId))}.");
                    continue;
                }

                if (entry.Length != representative.SizeBytes)
                {
                    errors.Add($"El tamano del asset '{entryName}' no coincide.");
                    continue;
                }

                var target = ResolveSafeStagingPath(extractionDirectory, entryName);
                await ExtractEntryAsync(entry, target, cancellationToken);
                var hash = await ComputeSha256Async(target, cancellationToken);
                if (!FixedEquals(hash, representative.Sha256))
                {
                    errors.Add($"El checksum del asset '{entryName}' no coincide.");
                }
            }

            errors.AddRange(await ValidateManagedAssetReferencesAsync(
                extractedDatabase,
                manifest.Assets.Where(asset => asset.OwnerKind is not null && !asset.WasMissing).ToList(),
                cancellationToken));

            errors.AddRange(await ValidateDatabaseAsync(extractedDatabase, currentMigrations, allowOlderSchema: true, cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            errors.Add($"El paquete no pudo validarse: {ex.Message}");
        }

        return new BackupValidationResult(errors.Count == 0, packagePath, manifest, errors, warnings);
    }

    public async Task<IReadOnlyList<string>> InstallAssetsIntoStagedDatabaseAsync(
        string stagedDatabasePath,
        string extractedDirectory,
        BackupManifest manifest,
        string businessAssetsDirectory,
        string managedAssetsDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(businessAssetsDirectory);
        Directory.CreateDirectory(managedAssetsDirectory);
        var createdPaths = new List<string>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = stagedDatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {

        foreach (var asset in manifest.Assets.Where(asset => !asset.WasMissing && asset.OwnerKind is null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolveSafeStagingPath(extractedDirectory, asset.PackagePath);
            var extension = NormalizeExtension(Path.GetExtension(asset.PackagePath));
            var target = Path.Combine(businessAssetsDirectory, $"{asset.Sha256.ToLowerInvariant()}{extension}");
            if (!File.Exists(target))
            {
                await CopyFileAsync(source, target, cancellationToken);
                createdPaths.Add(target);
            }

            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE productos SET imagen_local_path = $path WHERE producto_id = $id;";
            update.Parameters.AddWithValue("$path", target);
            update.Parameters.AddWithValue("$id", asset.ProductId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var asset in manifest.Assets.Where(asset => asset.WasMissing && asset.OwnerKind is null))
        {
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE productos SET imagen_local_path = NULL WHERE producto_id = $id;";
            update.Parameters.AddWithValue("$id", asset.ProductId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }


        foreach (var asset in manifest.Assets.Where(asset => !asset.WasMissing && asset.OwnerKind is not null))
        {
            if (string.IsNullOrWhiteSpace(asset.ManagedRelativePath))
                throw new InvalidDataException("El asset administrado no declara su ruta relativa.");
            var source = ResolveSafeStagingPath(extractedDirectory, asset.PackagePath);
            var target = ResolveSafeManagedPath(managedAssetsDirectory, asset.ManagedRelativePath);
            if (File.Exists(target))
            {
                var existingHash = await ComputeSha256Async(target, cancellationToken);
                if (!FixedEquals(existingHash, asset.Sha256))
                    throw new InvalidDataException($"Existe un asset local distinto en '{asset.ManagedRelativePath}'.");
                continue;
            }
            await CopyFileAsync(source, target, cancellationToken);
            createdPaths.Add(target);
        }

            await transaction.CommitAsync(cancellationToken);
            return createdPaths;
        }
        catch
        {
            foreach (var path in createdPaths)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    public async Task MigrateStagedDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(builder.ToString())
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            try { if (File.Exists(destination)) File.Delete(destination); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(NormalizeEntryName(entryName), CompressionLevel.Optimal);
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = entry.Open();
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await using var input = entry.Open();
        await using var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static string ResolveSafeStagingPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(root, NormalizeEntryName(relativePath).Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El paquete contiene una ruta no segura.");
        }

        return combined;
    }

    private static string ResolveSafeManagedPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El asset administrado contiene una ruta no segura.");
        return combined;
    }

    private static async Task<IReadOnlyList<string>> ValidateManagedAssetReferencesAsync(
        string databasePath,
        IReadOnlyList<BackupAssetManifest> assets,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (assets.Count == 0) return errors;
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        foreach (var asset in assets)
        {
            if (!asset.AssetId.HasValue || !asset.OwnerId.HasValue || string.IsNullOrWhiteSpace(asset.ManagedRelativePath))
            {
                errors.Add("Un asset administrado no declara propietario, identificador y ruta relativa.");
                continue;
            }
            var expectedPackagePath = $"assets/{asset.ManagedRelativePath.Replace('\\', '/')}";
            if (!string.Equals(NormalizeEntryName(asset.PackagePath), expectedPackagePath, StringComparison.Ordinal))
            {
                errors.Add($"La ruta empaquetada del asset {asset.AssetId} no coincide con su ruta administrada.");
                continue;
            }
            await using var command = connection.CreateCommand();
            if (asset.OwnerKind == ProductAssetOwnerKind.Product)
            {
                if (asset.OwnerId.Value != asset.ProductId)
                {
                    errors.Add($"El propietario del asset de producto {asset.AssetId} no coincide.");
                    continue;
                }
                command.CommandText = "SELECT producto_id, ruta_relativa, size_bytes, sha256 FROM producto_assets WHERE producto_asset_id = $id;";
            }
            else if (asset.OwnerKind == ProductAssetOwnerKind.Sku)
            {
                command.CommandText = """
                    SELECT ps.producto_id, psa.ruta_relativa, psa.size_bytes, psa.sha256
                    FROM producto_sku_assets psa
                    JOIN producto_skus ps ON ps.producto_sku_id = psa.producto_sku_id
                    WHERE psa.producto_sku_asset_id = $id AND psa.producto_sku_id = $ownerId;
                    """;
                command.Parameters.AddWithValue("$ownerId", asset.OwnerId.Value);
            }
            else
            {
                errors.Add($"El tipo de propietario del asset {asset.AssetId} no es valido.");
                continue;
            }
            command.Parameters.AddWithValue("$id", asset.AssetId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) != asset.ProductId ||
                !string.Equals(reader.GetString(1).Replace('\\', '/'), asset.ManagedRelativePath.Replace('\\', '/'), StringComparison.Ordinal) ||
                reader.GetInt64(2) != asset.SizeBytes || !FixedEquals(reader.GetString(3), asset.Sha256))
            {
                errors.Add($"La metadata del asset administrado {asset.AssetId} no coincide con el snapshot.");
            }
        }
        return errors;
    }

    private static string NormalizeEntryName(string name) => name.Replace('\\', '/').TrimStart('/');

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
        {
            return ".bin";
        }

        return extension.ToLowerInvariant();
    }

    private static bool FixedEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(left.ToUpperInvariant()),
            System.Text.Encoding.ASCII.GetBytes(right.ToUpperInvariant()));
    }

    private static async Task<string?> ExecuteScalarStringAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var migrations = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }
}
