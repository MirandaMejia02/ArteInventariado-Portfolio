using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Resolves file names that came from the machine-wide database without ever
/// reading a path until it has been proved to be inside the expected local
/// asset root.  This is deliberately independent of ACLs: SYSTEM is not a
/// reason to trust a mutable database value.
/// </summary>
internal static class MachineWideAssetPathGuard
{
    public static bool TryResolveCanonicalPath(
        string? rawPath,
        string allowedRoot,
        out string fullPath,
        out BackupAssetIssueKind issue)
    {
        fullPath = string.Empty;
        issue = BackupAssetIssueKind.InvalidPath;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(rawPath) ||
                IsUncOrExtendedPath(rawPath) ||
                HasAlternateDataStreamSyntax(rawPath))
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (ContainsTraversalSegment(rawPath))
        {
            issue = BackupAssetIssueKind.Traversal;
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!IsWithin(fullPath, allowedRoot))
        {
            issue = BackupAssetIssueKind.OutsideCanonicalRoot;
            fullPath = string.Empty;
            return false;
        }

        return true;
    }

    public static bool TryResolveManagedPath(
        string? relativePath,
        string managedRoot,
        string expectedOwnerDirectory,
        out string fullPath,
        out string normalizedRelativePath,
        out BackupAssetIssueKind issue)
    {
        fullPath = string.Empty;
        normalizedRelativePath = string.Empty;
        issue = BackupAssetIssueKind.InvalidPath;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        normalizedRelativePath = relativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedRelativePath) ||
            normalizedRelativePath.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalizedRelativePath) ||
            IsUncOrExtendedPath(relativePath) ||
            normalizedRelativePath.Contains(':', StringComparison.Ordinal))
        {
            normalizedRelativePath = string.Empty;
            return false;
        }

        if (ContainsTraversalSegment(normalizedRelativePath))
        {
            normalizedRelativePath = string.Empty;
            issue = BackupAssetIssueKind.Traversal;
            return false;
        }

        var prefix = expectedOwnerDirectory.Trim('/').TrimEnd('/') + "/";
        if (!normalizedRelativePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            normalizedRelativePath = string.Empty;
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(Path.Combine(
                managedRoot,
                normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (ArgumentException)
        {
            normalizedRelativePath = string.Empty;
            return false;
        }

        if (!IsWithin(fullPath, managedRoot))
        {
            fullPath = string.Empty;
            normalizedRelativePath = string.Empty;
            issue = BackupAssetIssueKind.OutsideCanonicalRoot;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Inspects only the candidate and its ancestors up to <paramref name="root" />.
    /// Missing candidates are reported as missing; a reparse point is never
    /// followed.  The caller must invoke this only after an in-root check.
    /// </summary>
    public static bool TryGetRegularFile(
        string fullPath,
        string root,
        out FileInfo? file,
        out BackupAssetIssueKind issue)
    {
        file = null;
        issue = BackupAssetIssueKind.InvalidPath;
        if (!IsWithin(fullPath, root))
        {
            issue = BackupAssetIssueKind.OutsideCanonicalRoot;
            return false;
        }

        if (!TryEnsureNoReparsePoints(fullPath, root, out var reparseOrAccessIssue))
        {
            issue = reparseOrAccessIssue;
            return false;
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(fullPath);
        }
        catch (FileNotFoundException)
        {
            issue = BackupAssetIssueKind.Missing;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            issue = BackupAssetIssueKind.Missing;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            issue = BackupAssetIssueKind.InvalidPath;
            return false;
        }
        catch (IOException)
        {
            issue = BackupAssetIssueKind.Missing;
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            issue = BackupAssetIssueKind.ReparsePoint;
            return false;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            issue = BackupAssetIssueKind.InvalidPath;
            return false;
        }

        try
        {
            file = new FileInfo(fullPath);
            if (!file.Exists)
            {
                issue = BackupAssetIssueKind.Missing;
                file = null;
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            issue = BackupAssetIssueKind.InvalidPath;
            return false;
        }
        catch (IOException)
        {
            issue = BackupAssetIssueKind.Missing;
            return false;
        }
    }

    public static bool TryEnsureNoReparsePoints(
        string fullPath,
        string root,
        out BackupAssetIssueKind issue)
    {
        issue = BackupAssetIssueKind.InvalidPath;
        var current = Path.GetFullPath(fullPath);
        var fullRoot = Path.GetFullPath(root);
        if (!IsWithin(current, fullRoot))
        {
            issue = BackupAssetIssueKind.OutsideCanonicalRoot;
            return false;
        }

        while (true)
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issue = BackupAssetIssueKind.ReparsePoint;
                    return false;
                }
            }
            catch (FileNotFoundException)
            {
                // A missing candidate/ancestor is handled by the caller.
            }
            catch (DirectoryNotFoundException)
            {
                // A missing candidate/ancestor is handled by the caller.
            }
            catch (UnauthorizedAccessException)
            {
                issue = BackupAssetIssueKind.InvalidPath;
                return false;
            }
            catch (IOException)
            {
                issue = BackupAssetIssueKind.InvalidPath;
                return false;
            }

            if (PathEquals(current, fullRoot))
            {
                return true;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || PathEquals(parent, current))
            {
                return true;
            }

            current = parent;
        }
    }

    public static bool IsWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PathEquals(fullCandidate, fullRoot) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeRelativePath(string? relativePath, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        normalized = relativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            IsUncOrExtendedPath(relativePath) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            ContainsTraversalSegment(normalized))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    private static bool ContainsTraversalSegment(string value) =>
        value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));

    private static bool IsUncOrExtendedPath(string value) =>
        value.StartsWith("\\\\", StringComparison.Ordinal) ||
        value.StartsWith("//", StringComparison.Ordinal) ||
        value.StartsWith(@"\\?\", StringComparison.Ordinal) ||
        value.StartsWith(@"\\.\", StringComparison.Ordinal);

    private static bool HasAlternateDataStreamSyntax(string value)
    {
        var firstColon = value.IndexOf(':');
        if (firstColon < 0)
        {
            return false;
        }

        // A local Windows drive is the only colon permitted in a canonical
        // absolute path; any later colon denotes an alternate data stream.
        return firstColon != 1 || value.IndexOf(':', firstColon + 1) >= 0;
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
