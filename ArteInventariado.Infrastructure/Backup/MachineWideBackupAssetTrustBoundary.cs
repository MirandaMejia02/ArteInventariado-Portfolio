using System.Security.Cryptography;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Backup;

internal sealed record MachineWideBackupAssetPreparationResult(
    IReadOnlyList<BackupAssetManifest> Assets,
    IReadOnlyList<string> Warnings,
    int OmittedAssetCount)
{
    public BackupContentStatus ContentStatus =>
        OmittedAssetCount == 0
            ? BackupContentStatus.Healthy
            : BackupContentStatus.DegradedAssets;
}

/// <summary>
/// Copies only assets that have crossed the machine-wide trust boundary.
/// Every database reference is treated as hostile input and is handled per
/// asset so a bad image never prevents the SQLite snapshot from being saved.
/// </summary>
internal sealed class MachineWideBackupAssetTrustBoundary
{
    private const long MaximumAssetBytes = 2L * 1024 * 1024 * 1024;

    private readonly string _productRoot;
    private readonly string _managedRoot;

    public MachineWideBackupAssetTrustBoundary(
        string productRoot,
        string managedRoot)
    {
        _productRoot = Path.GetFullPath(productRoot);
        _managedRoot = Path.GetFullPath(managedRoot);
    }

    public async Task<MachineWideBackupAssetPreparationResult> PrepareAsync(
        IReadOnlyList<(int ProductId, string Path)> productAssets,
        IReadOnlyList<BackupPackageEngine.UntrustedManagedAssetSource> managedAssets,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productAssets);
        ArgumentNullException.ThrowIfNull(managedAssets);
        var manifests = new List<BackupAssetManifest>(productAssets.Count + managedAssets.Count);
        var warnings = new List<string>();

        Directory.CreateDirectory(Path.Combine(stagingDirectory, "business-assets", "products"));
        Directory.CreateDirectory(Path.Combine(stagingDirectory, "assets"));

        foreach (var source in productAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await PrepareProductAssetAsync(source, stagingDirectory, cancellationToken);
            manifests.Add(manifest);
            AddWarningIfOmitted(warnings, manifest);
        }

        foreach (var source in managedAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await PrepareManagedAssetAsync(source, stagingDirectory, cancellationToken);
            manifests.Add(manifest);
            AddWarningIfOmitted(warnings, manifest);
        }

        var omitted = manifests.Count(asset => asset.WasMissing);
        return new MachineWideBackupAssetPreparationResult(manifests, warnings, omitted);
    }

    private async Task<BackupAssetManifest> PrepareProductAssetAsync(
        (int ProductId, string Path) source,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        if (!MachineWideAssetPathGuard.TryResolveCanonicalPath(
                source.Path,
                _productRoot,
                out var fullPath,
                out var issue) ||
            !MachineWideAssetPathGuard.TryGetRegularFile(fullPath, _productRoot, out var file, out issue))
        {
            return MissingProduct(source.ProductId, issue, null);
        }

        try
        {
            // Re-check immediately before opening the source.  The database
            // value and the filesystem are both untrusted at this boundary.
            if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(fullPath, _productRoot, out issue))
            {
                return MissingProduct(source.ProductId, issue, null);
            }

            var hash = await BackupPackageEngine.ComputeSha256Async(fullPath, cancellationToken);
            var extension = NormalizeExtension(Path.GetExtension(fullPath));
            var packagePath = $"business-assets/products/{hash.ToLowerInvariant()}{extension}";
            var target = ResolveStagingPath(stagingDirectory, packagePath);
            var targetResult = await CopyProductIfNeededAsync(
                fullPath,
                target,
                file!.Length,
                hash,
                stagingDirectory,
                cancellationToken);
            if (!targetResult)
            {
                return MissingProduct(source.ProductId, BackupAssetIssueKind.Changed, null);
            }

            return new BackupAssetManifest(
                source.ProductId,
                packagePath,
                Path.GetFileName(fullPath),
                file.Length,
                hash,
                WasMissing: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return MissingProduct(source.ProductId, BackupAssetIssueKind.Changed, null);
        }
        catch (UnauthorizedAccessException)
        {
            return MissingProduct(source.ProductId, BackupAssetIssueKind.InvalidPath, null);
        }
        catch (ArgumentException)
        {
            return MissingProduct(source.ProductId, BackupAssetIssueKind.InvalidPath, null);
        }
        catch (NotSupportedException)
        {
            return MissingProduct(source.ProductId, BackupAssetIssueKind.InvalidPath, null);
        }
    }

    private async Task<BackupAssetManifest> PrepareManagedAssetAsync(
        BackupPackageEngine.UntrustedManagedAssetSource source,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        if (source.OwnerKind is not ProductAssetOwnerKind.Product and not ProductAssetOwnerKind.Sku)
        {
            return MissingManaged(source, BackupAssetIssueKind.InvalidPath, null);
        }

        var expectedDirectory = source.OwnerKind == ProductAssetOwnerKind.Product
            ? "Products"
            : "Skus";
        var safeRelative = MachineWideAssetPathGuard.IsSafeRelativePath(
            source.RelativePath,
            out var normalizedRelative)
            ? normalizedRelative
            : null;

        if (!MachineWideAssetPathGuard.TryResolveManagedPath(
                source.RelativePath,
                _managedRoot,
                expectedDirectory,
                out var fullPath,
                out normalizedRelative,
                out var issue) ||
            !MachineWideAssetPathGuard.TryGetRegularFile(fullPath, _managedRoot, out var file, out issue))
        {
            return MissingManaged(source, issue, safeRelative);
        }

        try
        {
            if (file!.Length is <= 0 or > MaximumAssetBytes ||
                source.DeclaredSizeBytes is <= 0 or > MaximumAssetBytes)
            {
                return MissingManaged(source, BackupAssetIssueKind.Changed, normalizedRelative);
            }

            if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(fullPath, _managedRoot, out issue))
            {
                return MissingManaged(source, issue, normalizedRelative);
            }

            var hash = await BackupPackageEngine.ComputeSha256Async(fullPath, cancellationToken);
            if (file.Length != source.DeclaredSizeBytes ||
                !FixedEquals(hash, source.DeclaredSha256))
            {
                return MissingManaged(source, BackupAssetIssueKind.Changed, normalizedRelative);
            }

            var packagePath = $"assets/{normalizedRelative}";
            var target = ResolveStagingPath(stagingDirectory, packagePath);
            var copied = await CopyManagedIfNeededAsync(
                fullPath,
                target,
                file.Length,
                hash,
                stagingDirectory,
                cancellationToken);
            if (!copied)
            {
                return MissingManaged(source, BackupAssetIssueKind.Changed, normalizedRelative);
            }

            return new BackupAssetManifest(
                source.ProductId,
                packagePath,
                source.OriginalFileName,
                file.Length,
                hash,
                WasMissing: false,
                source.OwnerKind,
                source.AssetId,
                source.OwnerId,
                normalizedRelative);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return MissingManaged(source, BackupAssetIssueKind.Changed, normalizedRelative);
        }
        catch (UnauthorizedAccessException)
        {
            return MissingManaged(source, BackupAssetIssueKind.InvalidPath, normalizedRelative);
        }
        catch (ArgumentException)
        {
            return MissingManaged(source, BackupAssetIssueKind.InvalidPath, normalizedRelative);
        }
        catch (NotSupportedException)
        {
            return MissingManaged(source, BackupAssetIssueKind.InvalidPath, normalizedRelative);
        }
    }

    private static async Task<bool> CopyProductIfNeededAsync(
        string source,
        string target,
        long expectedSize,
        string expectedHash,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        if (MachineWideAssetPathGuard.TryGetRegularFile(
                target,
                stagingDirectory,
                out var existing,
                out var existingIssue))
        {
            return existing!.Length == expectedSize &&
                   FixedEquals(
                       await BackupPackageEngine.ComputeSha256Async(target, cancellationToken),
                       expectedHash);
        }

        if (existingIssue != BackupAssetIssueKind.Missing)
        {
            return false;
        }

        await BackupPackageEngine.CopyFileAsync(source, target, cancellationToken);
        if (!MachineWideAssetPathGuard.TryGetRegularFile(
                target,
                stagingDirectory,
                out var copied,
                out _))
        {
            return false;
        }

        var copiedHash = await BackupPackageEngine.ComputeSha256Async(target, cancellationToken);
        if (copied!.Length != expectedSize || !FixedEquals(copiedHash, expectedHash))
        {
            TryDelete(target);
            return false;
        }

        return true;
    }

    private static async Task<bool> CopyManagedIfNeededAsync(
        string source,
        string target,
        long expectedSize,
        string expectedHash,
        string stagingDirectory,
        CancellationToken cancellationToken) =>
        await CopyProductIfNeededAsync(
            source,
            target,
            expectedSize,
            expectedHash,
            stagingDirectory,
            cancellationToken);

    private static BackupAssetManifest MissingProduct(
        int productId,
        BackupAssetIssueKind issue,
        string? safeOriginalName) =>
        new(
            productId,
            string.Empty,
            safeOriginalName ?? string.Empty,
            0,
            string.Empty,
            WasMissing: true,
            IssueKind: issue);

    private static BackupAssetManifest MissingManaged(
        BackupPackageEngine.UntrustedManagedAssetSource source,
        BackupAssetIssueKind issue,
        string? safeRelativePath) =>
        new(
            source.ProductId,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            WasMissing: true,
            source.OwnerKind,
            source.AssetId,
            source.OwnerId,
            safeRelativePath,
            issue);

    private static void AddWarningIfOmitted(
        ICollection<string> warnings,
        BackupAssetManifest manifest)
    {
        if (!manifest.WasMissing)
        {
            return;
        }

        var kind = manifest.OwnerKind switch
        {
            ProductAssetOwnerKind.Product => "administrado del producto",
            ProductAssetOwnerKind.Sku => "administrado del SKU",
            _ => "de producto"
        };
        var issue = manifest.IssueKind?.ToString() ?? BackupAssetIssueKind.Missing.ToString();
        warnings.Add($"Se omitió un asset {kind} {manifest.ProductId} ({issue}).");
    }

    private static string ResolveStagingPath(string stagingDirectory, string packagePath)
    {
        var normalized = packagePath.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(
            stagingDirectory,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!MachineWideAssetPathGuard.IsWithin(full, stagingDirectory))
        {
            throw new InvalidDataException("La ruta de staging del asset no es segura.");
        }

        var parent = Path.GetDirectoryName(full)!;
        if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(
                parent,
                stagingDirectory,
                out var issue))
        {
            throw new InvalidDataException($"El staging de assets contiene un reparse point ({issue}).");
        }

        Directory.CreateDirectory(parent);
        if (!MachineWideAssetPathGuard.TryEnsureNoReparsePoints(parent, stagingDirectory, out issue))
        {
            throw new InvalidDataException($"El staging de assets contiene un reparse point ({issue}).");
        }

        return full;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Length > 12 ||
            extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
        {
            return ".bin";
        }

        return extension.ToLowerInvariant();
    }

    private static bool FixedEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right) ||
            left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(left.ToUpperInvariant()),
            System.Text.Encoding.ASCII.GetBytes(right.ToUpperInvariant()));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
