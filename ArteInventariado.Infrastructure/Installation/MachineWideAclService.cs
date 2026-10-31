using System.Security.AccessControl;
using System.Security.Principal;

#pragma warning disable CA1416 // All ACL entry points are guarded by EnsureWindows().

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Describes the positive Windows ACLs used by the explicit machine-wide
/// installer.  The plan uses well-known SIDs rather than localized account
/// names and never emits deny ACEs (administrators are also members of Users).
/// </summary>
public enum MachineWideAclProfile
{
    InstallReadExecute = 1,
    RootReadExecute = 2,
    DataModify = 3,
    AssetsModify = 4,
    StationReadExecute = 5,
    UpdateStateReadExecute = 6,
    LogsAppend = 7,
    MaintenanceModify = 8,
    ProtectedBackupsReadExecute = 9,
    StationOperationalModify = 10,
    CloudBackupReadExecute = 11,
    CloudBackupCredentialProtected = 12
}

public sealed record MachineWideAclRule(
    string IdentitySid,
    FileSystemRights Rights,
    AccessControlType AccessType);

public sealed record MachineWideAclScope(
    string Name,
    string Path,
    MachineWideAclProfile Profile,
    IReadOnlyList<MachineWideAclRule> DirectoryRules,
    IReadOnlyList<MachineWideAclRule> FileRules);

public sealed record MachineWideAclPlan(
    IReadOnlyList<MachineWideAclScope> Scopes,
    string SystemSid,
    string AdministratorsSid,
    string UsersSid);

public sealed record MachineWideAclResult(IReadOnlyList<MachineWideAclScope> AppliedScopes);

public sealed class MachineWideAclService
{
    private readonly bool _allowSyntheticRoots;

    public MachineWideAclService()
        : this(allowSyntheticRoots: false)
    {
    }

    // Test-only mode. Production callers use the parameterless constructor,
    // which always applies the restrictive machine-wide ACL plan. The
    // isolated fixture mode keeps temporary roots writable by the test host
    // so the full promotion/database/task orchestration can be exercised
    // without elevating the test process or touching protected folders.
    internal MachineWideAclService(bool allowSyntheticRoots)
    {
        _allowSyntheticRoots = allowSyntheticRoots;
    }

    public MachineWideAclPlan CreatePlan(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureWindows();
        EnsureMachineWide(paths);
        EnsureRootsDoNotOverlap(paths);

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value;

        var scopes = new List<MachineWideAclScope>
        {
            Scope("ProgramFiles", paths.MachineInstallRoot, MachineWideAclProfile.InstallReadExecute, systemSid, administratorsSid, usersSid),
            Scope("ProgramData", paths.MachineDataRoot, MachineWideAclProfile.RootReadExecute, systemSid, administratorsSid, usersSid),
            Scope("SQLiteData", paths.DataRoot, MachineWideAclProfile.DataModify, systemSid, administratorsSid, usersSid),
            Scope("Assets", paths.AssetsRoot, MachineWideAclProfile.AssetsModify, systemSid, administratorsSid, usersSid),
            Scope("BusinessAssets", paths.BusinessAssetsRoot, MachineWideAclProfile.AssetsModify, systemSid, administratorsSid, usersSid),
            Scope("Station", paths.StationRoot, MachineWideAclProfile.StationReadExecute, systemSid, administratorsSid, usersSid),
            Scope("StationOperational", paths.StationOperationalRoot, MachineWideAclProfile.StationOperationalModify, systemSid, administratorsSid, usersSid),
            Scope("UpdateState", paths.UpdateStateRoot, MachineWideAclProfile.UpdateStateReadExecute, systemSid, administratorsSid, usersSid),
            Scope("UpdateLogs", paths.UpdateLogsRoot, MachineWideAclProfile.LogsAppend, systemSid, administratorsSid, usersSid),
            Scope("StationLogs", paths.StationLogsRoot, MachineWideAclProfile.LogsAppend, systemSid, administratorsSid, usersSid),
            Scope("Logs", paths.LogsRoot, MachineWideAclProfile.LogsAppend, systemSid, administratorsSid, usersSid),
            Scope("Maintenance", paths.MaintenanceRoot, MachineWideAclProfile.MaintenanceModify, systemSid, administratorsSid, usersSid),
            Scope("ProtectedBackups", paths.ProtectedBackupsRoot, MachineWideAclProfile.ProtectedBackupsReadExecute, systemSid, administratorsSid, usersSid),
            Scope("CloudBackup", paths.CloudBackupRoot, MachineWideAclProfile.CloudBackupReadExecute, systemSid, administratorsSid, usersSid),
            // The parent cloud directory is readable so operational metadata
            // can be inspected, but the DPAPI credential subtree is private.
            Scope("CloudBackupCredentials", paths.CloudBackupCredentialsRoot, MachineWideAclProfile.CloudBackupCredentialProtected, systemSid, administratorsSid, usersSid)
        };

        return new MachineWideAclPlan(
            scopes,
            systemSid,
            administratorsSid,
            usersSid);
    }

    /// <summary>
    /// Creates directories used by the existing asset/backup maintenance
    /// code before the parent ACL is locked down.  It intentionally does not
    /// create a database, station identity, current pointer or backup.
    /// </summary>
    public void EnsureRuntimeDirectories(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureMachineWide(paths);
        EnsureRootsDoNotOverlap(paths);

        foreach (var directory in new[]
        {
            paths.AssetQuarantineRoot,
            paths.AssetStagingRoot,
            paths.StationOperationalRoot,
            paths.StationLogsRoot,
            Path.Combine(paths.ProtectedBackupsRoot, ".staging"),
            Path.Combine(paths.MaintenanceRoot, "Staging"),
            paths.LocalRestoreStagingRoot,
            paths.CloudBackupRoot,
            paths.CloudBackupValidationRoot
        })
        {
            EnsureDirectory(directory);
        }
    }

    public MachineWideAclResult Apply(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureWindows();
        EnsureMachineWide(paths);
        EnsureRootsDoNotOverlap(paths);
        var plan = CreatePlan(paths);
        var applied = new List<MachineWideAclScope>();

        if (_allowSyntheticRoots)
        {
            applied.AddRange(plan.Scopes.Where(scope => Directory.Exists(scope.Path)));
            return new MachineWideAclResult(applied);
        }

        foreach (var scope in plan.Scopes)
        {
            if (File.Exists(scope.Path))
            {
                throw new IOException($"La ruta ACL esperada como directorio es un archivo: '{scope.Path}'.");
            }

            if (!Directory.Exists(scope.Path))
            {
                continue;
            }

            RejectReparsePoint(scope.Path);
            ApplyDirectoryTree(scope, plan);

            applied.Add(scope);
        }

        return new MachineWideAclResult(applied);
    }

    private static MachineWideAclScope Scope(
        string name,
        string path,
        MachineWideAclProfile profile,
        string systemSid,
        string administratorsSid,
        string usersSid)
    {
        var directoryRules = CreateRules(profile, true, systemSid, administratorsSid, usersSid);
        var fileRules = CreateRules(profile, false, systemSid, administratorsSid, usersSid);
        return new MachineWideAclScope(name, Path.GetFullPath(path), profile, directoryRules, fileRules);
    }

    private static IReadOnlyList<MachineWideAclRule> CreateRules(
        MachineWideAclProfile profile,
        bool isDirectory,
        string systemSid,
        string administratorsSid,
        string usersSid)
    {
        var system = new MachineWideAclRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow);
        var administrators = new MachineWideAclRule(
            administratorsSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow);

        var userRights = profile switch
        {
            MachineWideAclProfile.InstallReadExecute or
            MachineWideAclProfile.RootReadExecute or
            MachineWideAclProfile.UpdateStateReadExecute or
            MachineWideAclProfile.ProtectedBackupsReadExecute or
            MachineWideAclProfile.CloudBackupReadExecute => FileSystemRights.ReadAndExecute,
            MachineWideAclProfile.DataModify or
            MachineWideAclProfile.AssetsModify or
            MachineWideAclProfile.MaintenanceModify or
            MachineWideAclProfile.StationOperationalModify => FileSystemRights.Modify,
            MachineWideAclProfile.StationReadExecute => FileSystemRights.ReadAndExecute,
            MachineWideAclProfile.CloudBackupCredentialProtected => FileSystemRights.FullControl,
            MachineWideAclProfile.LogsAppend => isDirectory
                ? FileSystemRights.ReadAndExecute |
                  FileSystemRights.CreateFiles |
                  FileSystemRights.CreateDirectories |
                  FileSystemRights.AppendData |
                  FileSystemRights.WriteAttributes |
                  FileSystemRights.WriteExtendedAttributes
                : FileSystemRights.ReadAndExecute |
                  // File.AppendAllText opens FileMode.Append/FileAccess.Write.
                  // Windows maps that generic write request to WriteData,
                  // AppendData, WriteAttributes and WriteExtendedAttributes.
                  // Keep the two write bits explicit: an AppendData-only
                  // ACE is insufficient for an existing log file.
                  FileSystemRights.CreateFiles |
                  FileSystemRights.AppendData |
                  FileSystemRights.WriteAttributes |
                  FileSystemRights.WriteExtendedAttributes,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };

        return profile == MachineWideAclProfile.CloudBackupCredentialProtected
            ? [system, administrators]
            : [
                system,
                administrators,
                new MachineWideAclRule(usersSid, userRights, AccessControlType.Allow)
            ];
    }

    private static void ApplyDirectoryTree(MachineWideAclScope scope, MachineWideAclPlan plan)
    {
        // Secure children before the directory itself.  This is important for
        // private subtrees such as CloudBackup\Credentials: once the parent
        // ACL removes Users, a non-elevated installer/test process can no
        // longer traverse the directory to finish securing its files.
        var entries = Directory.EnumerateFileSystemEntries(scope.Path).ToArray();
        foreach (var entry in entries)
        {
            RejectReparsePoint(entry);
            if (!IsWithin(entry, scope.Path))
            {
                throw new InvalidDataException($"La ruta ACL queda fuera de su raiz: '{entry}'.");
            }

            if (Directory.Exists(entry))
            {
                ApplyDirectoryTreeAt(entry, scope);
            }
            else if (File.Exists(entry))
            {
                ApplyFileAcl(entry, scope.FileRules);
            }
        }

        ApplyDirectoryAcl(scope.Path, scope.DirectoryRules);
    }

    private static void ApplyDirectoryTreeAt(string path, MachineWideAclScope scope)
    {
        var entries = Directory.EnumerateFileSystemEntries(path).ToArray();
        foreach (var entry in entries)
        {
            RejectReparsePoint(entry);
            if (!IsWithin(entry, scope.Path))
            {
                throw new InvalidDataException($"La ruta ACL queda fuera de su raiz: '{entry}'.");
            }

            if (Directory.Exists(entry))
            {
                ApplyDirectoryTreeAt(entry, scope);
            }
            else if (File.Exists(entry))
            {
                ApplyFileAcl(entry, scope.FileRules);
            }
        }

        ApplyDirectoryAcl(path, scope.DirectoryRules);
    }

    private static void ApplyDirectoryAcl(
        string path,
        IReadOnlyList<MachineWideAclRule> rules)
    {
        var directory = new DirectoryInfo(path);
        var security = directory.GetAccessControl(AccessControlSections.Access);
        ReplaceRules(
            security,
            rules,
            (identity, rule) => new FileSystemAccessRule(
                identity,
                rule.Rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                rule.AccessType));
        directory.SetAccessControl(security);
    }

    private static void ApplyFileAcl(
        string path,
        IReadOnlyList<MachineWideAclRule> rules)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl(AccessControlSections.Access);
        ReplaceRules(
            security,
            rules,
            (identity, rule) => new FileSystemAccessRule(
                identity,
                rule.Rights,
                InheritanceFlags.None,
                PropagationFlags.None,
                rule.AccessType));
        file.SetAccessControl(security);
    }

    private static void ReplaceRules<TSecurity>(
        TSecurity security,
        IReadOnlyList<MachineWideAclRule> rules,
        Func<SecurityIdentifier, MachineWideAclRule, FileSystemAccessRule> factory)
        where TSecurity : FileSystemSecurity
    {
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var existing in security
                     .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                     .Cast<FileSystemAccessRule>()
                     .ToArray())
        {
            security.RemoveAccessRuleSpecific(existing);
        }

        foreach (var rule in rules)
        {
            if (rule.AccessType != AccessControlType.Allow)
            {
                throw new InvalidDataException("El plan ACL machine-wide no permite reglas DENY.");
            }

            security.AddAccessRule(factory(
                new SecurityIdentifier(rule.IdentitySid),
                rule));
        }
    }

    private static void EnsureMachineWide(ArteInventariadoPathProvider paths)
    {
        if (!paths.IsMachineWide)
        {
            throw new ArgumentException("El ACL protegido requiere el layout machine-wide.", nameof(paths));
        }
    }

    private static void EnsureRootsDoNotOverlap(ArteInventariadoPathProvider paths)
    {
        var roots = new[]
        {
            paths.MachineInstallRoot,
            paths.MachineDataRoot,
            paths.UserDataRoot
        };
        for (var i = 0; i < roots.Length; i++)
        {
            for (var j = i + 1; j < roots.Length; j++)
            {
                if (IsWithin(roots[i], roots[j]) || IsWithin(roots[j], roots[i]))
                {
                    throw new InvalidDataException("Las raices machine-wide no pueden superponerse.");
                }
            }
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Los ACL machine-wide requieren Windows.");
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (File.Exists(path))
        {
            throw new IOException($"La ruta esperada como directorio es un archivo: '{path}'.");
        }

        Directory.CreateDirectory(path);
        RejectReparsePoint(path);
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"No se permite un reparse point en la ruta ACL: '{path}'.");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidDataException($"No se pudo inspeccionar la ruta ACL: '{path}'.", ex);
        }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

#pragma warning restore CA1416
