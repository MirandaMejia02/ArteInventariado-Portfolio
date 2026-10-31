using System.Security.AccessControl;
using System.Security.Principal;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Updater;
using Microsoft.Data.Sqlite;

#pragma warning disable CA1416 // These tests are intentionally Windows ACL tests.

namespace ArteInventariado.Tests;

public sealed class MachineWideAclTests
{
    [Fact]
    public void Plan_UsesWellKnownSids_AndOnlyAllowRules()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new AclFixture();
        var plan = new MachineWideAclService().CreatePlan(fixture.Paths);

        Assert.Equal("S-1-5-18", plan.SystemSid);
        Assert.Equal("S-1-5-32-544", plan.AdministratorsSid);
        Assert.Equal("S-1-5-32-545", plan.UsersSid);
        Assert.DoesNotContain(plan.Scopes, scope => IsWithin(scope.Path, fixture.Paths.UserDataRoot));
        Assert.All(
            plan.Scopes.SelectMany(scope => scope.DirectoryRules.Concat(scope.FileRules)),
            rule => Assert.Equal(AccessControlType.Allow, rule.AccessType));

        var protectedProfiles = new[]
        {
            MachineWideAclProfile.InstallReadExecute,
            MachineWideAclProfile.UpdateStateReadExecute,
            MachineWideAclProfile.ProtectedBackupsReadExecute
        };
        foreach (var scope in plan.Scopes.Where(scope => protectedProfiles.Contains(scope.Profile)))
        {
            var userRules = scope.DirectoryRules.Where(rule => rule.IdentitySid == plan.UsersSid).ToArray();
            Assert.Single(userRules);
            Assert.True(userRules[0].Rights.HasFlag(FileSystemRights.ReadAndExecute));
            Assert.False(userRules[0].Rights.HasFlag(FileSystemRights.Modify));
            Assert.False(userRules[0].Rights.HasFlag(FileSystemRights.Delete));
        }
    }

    [Fact]
    public void Plan_RejectsOverlappingInstallDataAndUserRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new AclFixture();
        var overlapping = ArteInventariadoPathProvider.CreateMachineWide(
            fixture.Root,
            Path.Combine(fixture.Root, "data"),
            Path.Combine(fixture.Root, "user"));

        Assert.Throws<InvalidDataException>(() => new MachineWideAclService().CreatePlan(overlapping));
    }

    [Fact]
    public async Task Apply_ProtectsInstallAndProtectedState_WhileDataRemainsWritable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new AclFixture();
        await new ArteInventariado.Infrastructure.Installation.MachineWideBootstrapService()
            .PrepareCleanInstallAsync(fixture.Paths);
        Directory.CreateDirectory(fixture.Paths.GetVersionDirectory("1.12.0-rc1"));
        await File.WriteAllTextAsync(fixture.Paths.LauncherPath, "launcher");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Paths.GetVersionDirectory("1.12.0-rc1"), "ArteInventariado.WinForms.exe"),
            "application");
        await using (var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        }.ToString()))
        {
            await database.OpenAsync();
            await using var journal = database.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=WAL;";
            _ = await journal.ExecuteScalarAsync();
            await using var create = database.CreateCommand();
            create.CommandText = "CREATE TABLE c2_acl_probe (id INTEGER PRIMARY KEY, value TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync();
        }
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "backup.aibak"),
            "backup");

        var service = new MachineWideAclService();
        service.EnsureRuntimeDirectories(fixture.Paths);
        foreach (var logRoot in new[]
                 {
                     fixture.Paths.UpdateLogsRoot,
                     fixture.Paths.StationLogsRoot,
                     fixture.Paths.LogsRoot
                 })
        {
            Directory.CreateDirectory(logRoot);
            File.WriteAllText(Path.Combine(logRoot, "existing.log"), "seed");
        }

        var first = service.Apply(fixture.Paths);
        var firstInstallFingerprint = Fingerprint(fixture.Paths.MachineInstallRoot);
        var second = service.Apply(fixture.Paths);
        var secondInstallFingerprint = Fingerprint(fixture.Paths.MachineInstallRoot);

        Assert.Contains(first.AppliedScopes, scope => scope.Name == "ProgramFiles");
        Assert.Contains(first.AppliedScopes, scope => scope.Name == "UpdateState");
        Assert.Contains(first.AppliedScopes, scope => scope.Name == "UpdateLogs");
        Assert.Contains(first.AppliedScopes, scope => scope.Name == "StationLogs");
        Assert.Contains(first.AppliedScopes, scope => scope.Name == "ProtectedBackups");
        Assert.Equal(firstInstallFingerprint, secondInstallFingerprint);
        Assert.Equal(first.AppliedScopes.Select(scope => scope.Name), second.AppliedScopes.Select(scope => scope.Name));

        var plan = service.CreatePlan(fixture.Paths);
        AssertProtectedDirectory(fixture.Paths.MachineInstallRoot, plan);
        AssertProtectedFile(fixture.Paths.LauncherPath, plan);
        AssertProtectedDirectory(fixture.Paths.UpdateStateRoot, plan);
        AssertProtectedDirectory(fixture.Paths.LocalRestoreStagingRoot, plan);
        AssertProtectedDirectory(fixture.Paths.ProtectedBackupsRoot, plan);
        AssertProtectedDirectory(fixture.Paths.StationRoot, plan);
        AssertProtectedFile(fixture.Paths.StationSettingsPath, plan);
        AssertWritableDirectory(fixture.Paths.StationOperationalRoot, plan.UsersSid);
        AssertWritableDirectory(fixture.Paths.DataRoot, plan.UsersSid);
        AssertAppendOnlyDirectory(fixture.Paths.UpdateLogsRoot, plan.UsersSid);
        AssertAppendOnlyDirectory(fixture.Paths.StationLogsRoot, plan.UsersSid);
        AssertAppendOnlyFile(Path.Combine(fixture.Paths.UpdateLogsRoot, "existing.log"), plan.UsersSid);
        AssertAppendOnlyFile(Path.Combine(fixture.Paths.StationLogsRoot, "existing.log"), plan.UsersSid);
        AssertAppendOnlyFile(Path.Combine(fixture.Paths.LogsRoot, "existing.log"), plan.UsersSid);

        await using (var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        }.ToString()))
        {
            await database.OpenAsync();
            await using var insert = database.CreateCommand();
            insert.CommandText = "INSERT INTO c2_acl_probe(value) VALUES ('wal-ok');";
            await insert.ExecuteNonQueryAsync();
        }

        Assert.True(Directory.Exists(fixture.Paths.UserDataRoot));
    }

    [Fact]
    public void LogsAppendAcl_ReproducesMissingWriteExtendedAttributesFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new AclFixture();
        var plan = new MachineWideAclService().CreatePlan(fixture.Paths);
        var scope = Assert.Single(plan.Scopes, candidate => candidate.Name == "UpdateLogs");
        Directory.CreateDirectory(scope.Path);

        var directoryRule = Assert.Single(
            scope.DirectoryRules,
            rule => rule.IdentitySid == plan.UsersSid);
        var fileRule = Assert.Single(
            scope.FileRules,
            rule => rule.IdentitySid == plan.UsersSid);
        var withoutExtendedAttributes = ~FileSystemRights.WriteExtendedAttributes;
        var baseDirectoryRights = directoryRule.Rights & withoutExtendedAttributes;
        var baseFileRights = fileRule.Rights & withoutExtendedAttributes;
        ApplyUsersOnlyDirectoryAcl(scope.Path, baseDirectoryRights, plan.UsersSid);

        var existing = Path.Combine(scope.Path, "missing-wea.log");
        File.AppendAllText(existing, "first" + Environment.NewLine);
        ApplyUsersOnlyFileAcl(existing, baseFileRights, plan.UsersSid);

        // The PC1 ACL shape (RX + WriteData + AppendData + WriteAttributes)
        // is enough to create the file, but not enough for the generic write
        // access requested by File.AppendAllText on a subsequent open.
        Assert.Throws<UnauthorizedAccessException>(
            () => File.AppendAllText(existing, "second" + Environment.NewLine));

        ApplyUsersOnlyDirectoryAcl(scope.Path, directoryRule.Rights, plan.UsersSid);
        ApplyUsersOnlyFileAcl(existing, fileRule.Rights, plan.UsersSid);
        File.AppendAllText(existing, "second" + Environment.NewLine);
        Assert.Equal(2, File.ReadAllLines(existing).Length);
    }

    [Fact]
    public void LogsAppendAcl_SupportsRealFileAppendAllText_ForExistingAndNewFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new AclFixture();
        var service = new MachineWideAclService();
        var plan = service.CreatePlan(fixture.Paths);
        var logScopes = plan.Scopes
            .Where(scope => scope.Profile == MachineWideAclProfile.LogsAppend)
            .ToArray();

        Assert.Equal(
            new[] { "UpdateLogs", "StationLogs", "Logs" },
            logScopes.Select(scope => scope.Name));

        foreach (var scope in logScopes)
        {
            Directory.CreateDirectory(scope.Path);
            var directoryRule = Assert.Single(
                scope.DirectoryRules,
                rule => rule.IdentitySid == plan.UsersSid);
            Assert.True(directoryRule.Rights.HasFlag(FileSystemRights.CreateFiles));
            Assert.True(directoryRule.Rights.HasFlag(FileSystemRights.AppendData));
            Assert.True(directoryRule.Rights.HasFlag(FileSystemRights.WriteAttributes));
            Assert.True(directoryRule.Rights.HasFlag(FileSystemRights.WriteExtendedAttributes));
            Assert.False(directoryRule.Rights.HasFlag(FileSystemRights.Modify));
            Assert.False(directoryRule.Rights.HasFlag(FileSystemRights.Delete));
            Assert.False(directoryRule.Rights.HasFlag(FileSystemRights.DeleteSubdirectoriesAndFiles));
            ApplyUsersOnlyDirectoryAcl(scope.Path, directoryRule.Rights, plan.UsersSid);

            var existing = Path.Combine(scope.Path, "existing.log");
            File.AppendAllText(existing, "first" + Environment.NewLine);
            var fileRule = Assert.Single(
                scope.FileRules,
                rule => rule.IdentitySid == plan.UsersSid);
            Assert.True(fileRule.Rights.HasFlag(FileSystemRights.CreateFiles));
            Assert.True(fileRule.Rights.HasFlag(FileSystemRights.AppendData));
            Assert.True(fileRule.Rights.HasFlag(FileSystemRights.WriteAttributes));
            Assert.True(fileRule.Rights.HasFlag(FileSystemRights.WriteExtendedAttributes));
            Assert.False(fileRule.Rights.HasFlag(FileSystemRights.Modify));
            Assert.False(fileRule.Rights.HasFlag(FileSystemRights.Delete));
            ApplyUsersOnlyFileAcl(existing, fileRule.Rights, plan.UsersSid);

            // This is the exact API used by the production append-only
            // loggers.  It must work both on an existing file and on a file
            // created after the directory inheritance is applied.
            File.AppendAllText(existing, "second" + Environment.NewLine);
            var fresh = Path.Combine(scope.Path, "fresh.log");
            File.AppendAllText(fresh, "first" + Environment.NewLine);
            File.AppendAllText(fresh, "second" + Environment.NewLine);

            Assert.Equal(2, File.ReadAllLines(existing).Length);
            Assert.Equal(2, File.ReadAllLines(fresh).Length);
            Assert.Throws<UnauthorizedAccessException>(() => File.Delete(existing));
            Assert.Throws<UnauthorizedAccessException>(
                () => File.Move(existing, Path.Combine(scope.Path, "renamed.log")));

            if (scope.Name == "UpdateLogs")
            {
                var writer = new MachineWideBootstrapLogWriter(fixture.Paths, Guid.NewGuid());
                writer.Started();
                writer.Phase("acl-test");
                Assert.Equal(2, File.ReadAllLines(writer.Path).Length);
            }
        }
    }

    [Fact]
    public void MachineWideShortcuts_TargetStableLauncher_AndCarryExplicitLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new AclFixture();
        Directory.CreateDirectory(fixture.Paths.MachineInstallRoot);
        File.WriteAllText(fixture.Paths.LauncherPath, "launcher");
        var desktop = Path.Combine(fixture.Root, "CommonDesktop");
        var programs = Path.Combine(fixture.Root, "CommonPrograms");
        var backend = new FakeShortcutBackend();
        var runtime = UpdateRuntimePaths.Create(fixture.Paths);
        var manager = new WindowsShortcutManager(
            new ShortcutLocations(desktop, programs),
            backend,
            runtime.GetMachineWideShortcutArguments());

        manager.EnsureStableShortcuts(fixture.Paths.LauncherPath);

        Assert.Equal(2, backend.Saved.Count);
        Assert.All(backend.Saved.Values, shortcut =>
        {
            Assert.Equal(Path.GetFullPath(fixture.Paths.LauncherPath), shortcut.Target);
            Assert.Equal(runtime.GetMachineWideShortcutArguments(), shortcut.Arguments);
        });
        Assert.DoesNotContain("Versions", backend.Saved.Values.Select(value => value.Target), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("--user-data-root", backend.Saved.Values.Select(value => value.Arguments), StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertProtectedDirectory(string path, MachineWideAclPlan plan)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected, path);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        AssertFullControl(rules, plan.SystemSid);
        AssertFullControl(rules, plan.AdministratorsSid);
        var user = Assert.Single(rules, rule => rule.IdentityReference.Value == plan.UsersSid);
        Assert.Equal(AccessControlType.Allow, user.AccessControlType);
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.ReadAndExecute));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Modify));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Delete));
        Assert.DoesNotContain(rules, rule => rule.AccessControlType == AccessControlType.Deny);
    }

    private static void AssertProtectedFile(string path, MachineWideAclPlan plan)
    {
        var security = new FileInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected, path);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        AssertFullControl(rules, plan.SystemSid);
        AssertFullControl(rules, plan.AdministratorsSid);
        var user = Assert.Single(rules, rule => rule.IdentityReference.Value == plan.UsersSid);
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.ReadAndExecute));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Modify));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Delete));
    }

    private static void AssertFullControl(IEnumerable<FileSystemAccessRule> rules, string sid)
    {
        var rule = Assert.Single(rules, candidate => candidate.IdentityReference.Value == sid);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.True(rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
    }

    private static void AssertWritableDirectory(string path, string usersSid)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        var user = Assert.Single(rules, rule => rule.IdentityReference.Value == usersSid);
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.Modify));
    }

    private static void AssertAppendOnlyDirectory(string path, string usersSid)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        var user = Assert.Single(rules, rule => rule.IdentityReference.Value == usersSid);
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.AppendData));
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.CreateFiles));
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.WriteAttributes));
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.WriteExtendedAttributes));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Modify));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Delete));
    }

    private static void AssertAppendOnlyFile(string path, string usersSid)
    {
        var security = new FileInfo(path).GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        var user = Assert.Single(rules, rule => rule.IdentityReference.Value == usersSid);
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.CreateFiles));
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.AppendData));
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.WriteAttributes));
        Assert.True(user.FileSystemRights.HasFlag(FileSystemRights.WriteExtendedAttributes));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Modify));
        Assert.False(user.FileSystemRights.HasFlag(FileSystemRights.Delete));
    }

    private static void ApplyUsersOnlyDirectoryAcl(
        string path,
        FileSystemRights rights,
        string usersSid)
    {
        var directory = new DirectoryInfo(path);
        var security = directory.GetAccessControl(AccessControlSections.Access);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var existing in security
                     .GetAccessRules(true, true, typeof(SecurityIdentifier))
                     .Cast<FileSystemAccessRule>()
                     .ToArray())
        {
            security.RemoveAccessRuleSpecific(existing);
        }

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(usersSid),
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static void ApplyUsersOnlyFileAcl(
        string path,
        FileSystemRights rights,
        string usersSid)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl(AccessControlSections.Access);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var existing in security
                     .GetAccessRules(true, true, typeof(SecurityIdentifier))
                     .Cast<FileSystemAccessRule>()
                     .ToArray())
        {
            security.RemoveAccessRuleSpecific(existing);
        }

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(usersSid),
            rights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        file.SetAccessControl(security);
    }

    private static string Fingerprint(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        return string.Join(
            "|",
            security.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(rule =>
                    $"{rule.IdentityReference.Value}:{rule.AccessControlType}:{rule.FileSystemRights}:{rule.InheritanceFlags}:{rule.PropagationFlags}")
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static bool IsWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AclFixture : IDisposable, IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"ArteInventariado-c2-acl-{Guid.NewGuid():N}");
        public ArteInventariadoPathProvider Paths { get; }

        public AclFixture()
        {
            Paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(Root, "Program Files", "ArteInventariado"),
                Path.Combine(Root, "ProgramData", "ArteInventariado"),
                Path.Combine(Root, "LocalAppData", "ArteInventariado"));
            Directory.CreateDirectory(Root);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    RestoreOwnerAccess(Root);
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The test must not leave a locked ACL tree behind if a local
                // Windows policy prevents cleanup; the assertion already ran.
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        private static void RestoreOwnerAccess(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            var current = WindowsIdentity.GetCurrent().User!;
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var security = directory.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(
                    current,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                directory.SetAccessControl(security);
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                {
                    RestoreOwnerAccess(child);
                }
            }
            else
            {
                var file = new FileInfo(path);
                var security = file.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(
                    current,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
                file.SetAccessControl(security);
            }
        }
    }

    private sealed class FakeShortcutBackend : IWindowsShortcutBackend
    {
        public Dictionary<string, Shortcut> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Exists(string path) => Saved.ContainsKey(path);

        public string? GetTargetOrNull(string path) => Saved.TryGetValue(path, out var value) ? value.Target : null;

        public string? GetArgumentsOrNull(string path) => Saved.TryGetValue(path, out var value) ? value.Arguments : null;

        public void Save(
            string shortcutPath,
            string targetPath,
            string workingDirectory,
            string iconPath,
            string description,
            string arguments) =>
            Saved[shortcutPath] = new Shortcut(targetPath, arguments);

        public sealed record Shortcut(string Target, string Arguments);
    }
}

#pragma warning restore CA1416
