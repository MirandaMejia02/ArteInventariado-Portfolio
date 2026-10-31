namespace ArteInventariado.Infrastructure;

/// <summary>
/// Resolves every application path from one explicit layout.
///
/// The legacy per-user layout remains the runtime default for v1.11
/// compatibility.  An explicit machine-wide command line (or an injected
/// provider from the installer) opts the process into Program Files/ProgramData.
/// </summary>
public sealed class ArteInventariadoPathProvider
{
    public const string VerificationModeEnvironmentVariable = "ARTE_UPDATE_VERIFY";
    public const string VerificationRootEnvironmentVariable = "ARTE_UPDATE_VERIFICATION_ROOT";
    public const string VerificationInstallRootEnvironmentVariable = "ARTE_UPDATE_VERIFICATION_INSTALL_ROOT";
    public const string VerificationDataRootEnvironmentVariable = "ARTE_UPDATE_VERIFICATION_DATA_ROOT";
    public const string VerificationUserRootEnvironmentVariable = "ARTE_UPDATE_VERIFICATION_USER_ROOT";
    public const string VisualDevEnvironmentVariable = "ARTE_DEV_VISUAL";
    public const string VisualDevDataRootEnvironmentVariable = "ARTE_DEV_DATA_ROOT";
    public const string ApplicationDirectoryName = "ArteInventariado";
    public const string DatabaseFileName = "arteinventariado_v1.db";
    public const string LauncherFileName = "ArteInventariado.Launcher.exe";
    public const string UpdaterFileName = "ArteInventariado.Updater.exe";
    public const string StationHelperFileName = "ArteInventariado.StationHelper.exe";
    public const string CurrentStateFileName = "current.json";
    public const string UpdateStateFileName = "update-state.json";
    public const string StationSettingsFileName = "station-settings.json";
    public const string StationOperationalDirectoryName = "Operational";
    public const string BackupPolicyFileName = "backup-policy.json";
    public const string BackupStatusFileName = "backup-status.json";
    public const string BackupRunnerFileName = "ArteInventariado.BackupRunner.exe";
    public const string CloudBackupRunnerFileName = "ArteInventariado.CloudBackupRunner.exe";

    private ArteInventariadoPathProvider(
        ArteInventariadoPathLayout layout,
        string machineInstallRoot,
        string machineDataRoot,
        string userDataRoot)
    {
        Layout = layout;
        MachineInstallRoot = NormalizeRoot(machineInstallRoot, nameof(machineInstallRoot));
        MachineDataRoot = NormalizeRoot(machineDataRoot, nameof(machineDataRoot));
        UserDataRoot = NormalizeRoot(userDataRoot, nameof(userDataRoot));
    }

    public ArteInventariadoPathLayout Layout { get; }

    /// <summary>Root reserved for stable binaries and installed versions.</summary>
    public string MachineInstallRoot { get; }

    /// <summary>Root reserved for mutable machine-wide application data.</summary>
    public string MachineDataRoot { get; }

    /// <summary>Root reserved for per-user cache and preferences.</summary>
    public string UserDataRoot { get; }

    public bool IsMachineWide => Layout == ArteInventariadoPathLayout.MachineWide;

    /// <summary>
    /// True only for the explicit source-tree visual runner.  The runner sets
    /// this variable on the child process rather than changing the user's
    /// environment, so normal machine-wide launches keep their existing path.
    /// </summary>
    public static bool IsVisualDev => IsVisualDevValue(
        Environment.GetEnvironmentVariable(VisualDevEnvironmentVariable));

    internal static bool IsVisualDevValue(string? value) => string.Equals(
        value,
        "1",
        StringComparison.Ordinal);

    public string LauncherPath => Path.Combine(MachineInstallRoot, LauncherFileName);

    public string UpdaterPath => Path.Combine(MachineInstallRoot, UpdaterFileName);

    /// <summary>
    /// Stable, narrowly-scoped elevated helper used only to change the
    /// protected station mode. It is kept beside the other machine-wide
    /// entry points and is never resolved from a user-writable directory.
    /// </summary>
    public string StationHelperPath => Path.Combine(MachineInstallRoot, StationHelperFileName);

    public string VersionsRoot => Layout == ArteInventariadoPathLayout.LegacyPerUser
        ? Path.Combine(MachineDataRoot, "Updates", "Versions")
        : Path.Combine(MachineInstallRoot, "Versions");

    public string UpdateStateRoot => Layout == ArteInventariadoPathLayout.LegacyPerUser
        ? Path.Combine(MachineDataRoot, "Updates")
        : Path.Combine(MachineDataRoot, "UpdateState");

    public string CurrentStatePath => Path.Combine(UpdateStateRoot, CurrentStateFileName);

    public string UpdateStatePath => Path.Combine(UpdateStateRoot, UpdateStateFileName);

    public string StationSnapshotsRoot => Path.Combine(UpdateStateRoot, "StationSnapshots");

    /// <summary>
    /// Mutable recovery/pre-update state. It is operational update state,
    /// not a protected business backup and therefore remains under
    /// UpdateState rather than ProtectedBackups.
    /// </summary>
    public string RecoveryRoot => Path.Combine(UpdateStateRoot, "Recovery");

    public string UpdateLogsRoot => Path.Combine(UpdateStateRoot, "Logs");

    public string DataRoot => Path.Combine(MachineDataRoot, "Data");

    public string DatabasePath => Path.Combine(DataRoot, DatabaseFileName);

    public string AssetsRoot => Path.Combine(MachineDataRoot, "Assets");

    public string BusinessAssetsRoot => Path.Combine(MachineDataRoot, "BusinessAssets");

    public string ProductBusinessAssetsRoot => Path.Combine(BusinessAssetsRoot, "Products");

    public string StationRoot => Path.Combine(MachineDataRoot, "Station");

    /// <summary>
    /// In the machine-wide layout this is the protected identity/mode file.
    /// The legacy layout retains the v1.11 combined file unchanged.
    /// </summary>
    public string StationSettingsPath => Path.Combine(StationRoot, StationSettingsFileName);

    /// <summary>
    /// Operational station preferences are isolated from the protected core
    /// in machine-wide installations. Legacy installations intentionally map
    /// both paths to the original combined file.
    /// </summary>
    public string StationOperationalRoot => Layout == ArteInventariadoPathLayout.MachineWide
        ? Path.Combine(StationRoot, StationOperationalDirectoryName)
        : StationRoot;

    public string StationOperationalSettingsPath => Path.Combine(
        StationOperationalRoot,
        StationSettingsFileName);

    /// <summary>Explicit alias for callers that need to name the protected core.</summary>
    public string StationCorePath => StationSettingsPath;

    /// <summary>
    /// Machine-wide backup policy.  It is deliberately outside the
    /// user-writable operational station document and inherits the protected
    /// Station ACL. Legacy installations keep the path available only as a
    /// deterministic compatibility location; the protected runner requires
    /// the machine-wide layout.
    /// </summary>
    public string ProtectedBackupPolicyPath => Path.Combine(StationRoot, BackupPolicyFileName);

    public string StationLogsRoot => Path.Combine(StationRoot, "Logs");

    public string LogsRoot => Path.Combine(MachineDataRoot, "Logs");

    public string MaintenanceRoot => Path.Combine(MachineDataRoot, "Maintenance");

    /// <summary>
    /// Atomic completion marker written by the explicit machine-wide
    /// bootstrap. It is deliberately kept outside the business database and
    /// does not change the normal v1.11 runtime layout.
    /// </summary>
    public string BootstrapStatePath => Path.Combine(MaintenanceRoot, "bootstrap-state.json");

    public string ProtectedBackupsRoot => Layout == ArteInventariadoPathLayout.LegacyPerUser
        ? Path.Combine(MachineDataRoot, "Backups")
        : Path.Combine(MachineDataRoot, "ProtectedBackups");

    /// <summary>Readable status summary written by the protected runner.</summary>
    public string ProtectedBackupStatusPath => Path.Combine(ProtectedBackupsRoot, BackupStatusFileName);

    /// <summary>
    /// Protected machine-wide state for the optional off-site uploader.  It
    /// is deliberately separate from both the local backup packages and the
    /// update state tree.  The directory contains only non-secret upload
    /// metadata plus a DPAPI-protected credential blob.
    /// </summary>
    public string CloudBackupRoot => Path.Combine(MachineDataRoot, "CloudBackup");

    public string CloudBackupConfigurationPath => Path.Combine(CloudBackupRoot, "config.json");

    public string CloudBackupUploadStatePath => Path.Combine(CloudBackupRoot, "upload-state.json");

    public string CloudBackupValidationRoot => Path.Combine(CloudBackupRoot, "Validation");

    public string CloudBackupCredentialsRoot => Path.Combine(CloudBackupRoot, "Credentials");

    public string CloudBackupCredentialPath => Path.Combine(CloudBackupCredentialsRoot, "s3-upload.dpapi");

    /// <summary>
    /// Protected temporary root for cloud recovery downloads.  It remains
    /// below UpdateState/Recovery so the existing machine-wide ACL protects
    /// it from standard users; successful packages are handed to Bloque H
    /// without ever touching the live database.
    /// </summary>
    public string CloudRecoveryStagingRoot => Path.Combine(RecoveryRoot, "Cloud");

    /// <summary>
    /// Protected copy-only staging root for local restore imports such as a
    /// backup selected from USB. It is a sibling of cloud recovery staging;
    /// neither path is a live database or a canonical backup destination.
    /// </summary>
    public string LocalRestoreStagingRoot => Path.Combine(RecoveryRoot, "Local");

    /// <summary>Stable machine-wide executable used by the scheduled task.</summary>
    public string BackupRunnerPath => Path.Combine(MachineInstallRoot, BackupRunnerFileName);

    /// <summary>Stable machine-wide executable used by the cloud task.</summary>
    public string CloudBackupRunnerPath => Path.Combine(MachineInstallRoot, CloudBackupRunnerFileName);

    /// <summary>Compatibility alias for existing backup consumers.</summary>
    public string BackupsRoot => ProtectedBackupsRoot;

    public string PendingRestorePath => Path.Combine(MaintenanceRoot, "pending-restore.json");

    public string AssetQuarantineRoot => Path.Combine(MaintenanceRoot, "AssetQuarantine");

    public string AssetStagingRoot => Path.Combine(MaintenanceRoot, "AssetStaging");

    public string CacheRoot => Path.Combine(UserDataRoot, "Cache");

    /// <summary>
    /// Per-user, non-canonical staging area used before the elevated
    /// machine-wide updater takes ownership of an update. Nothing in this
    /// directory is installation state or a trusted backup.
    /// </summary>
    public string UpdateStagingRoot => Path.Combine(UserDataRoot, "UpdateStaging");

    public string ThumbnailRoot => Path.Combine(CacheRoot, "Thumbnails");

    public static ArteInventariadoPathProvider CreateDefault()
    {
        // Visual DEV is an explicit, isolated source-tree mode.  It must be
        // evaluated before verification and machine-wide inference so a DEV
        // child can never fall through to the stable installation.
        if (IsVisualDev)
        {
            var visualDevRoot = Environment.GetEnvironmentVariable(VisualDevDataRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(visualDevRoot))
            {
                throw new InvalidOperationException(
                    $"{VisualDevEnvironmentVariable}=1 requiere {VisualDevDataRootEnvironmentVariable}.");
            }

            return CreateLegacy(visualDevRoot);
        }

        // Preserve the existing updater smoke/verification override without
        // introducing a general-purpose production root override.
        if (string.Equals(
                Environment.GetEnvironmentVariable(VerificationModeEnvironmentVariable),
                "1",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VerificationInstallRootEnvironmentVariable)) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VerificationDataRootEnvironmentVariable)) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VerificationUserRootEnvironmentVariable)))
        {
            return CreateMachineWide(
                Environment.GetEnvironmentVariable(VerificationInstallRootEnvironmentVariable)!,
                Environment.GetEnvironmentVariable(VerificationDataRootEnvironmentVariable)!,
                Environment.GetEnvironmentVariable(VerificationUserRootEnvironmentVariable)!);
        }

        if (string.Equals(
                Environment.GetEnvironmentVariable(VerificationModeEnvironmentVariable),
                "1",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VerificationRootEnvironmentVariable)))
        {
            return CreateLegacy(Environment.GetEnvironmentVariable(VerificationRootEnvironmentVariable)!);
        }

        // Published machine-wide entry points carry the three roots on their
        // command line.  Keep static helpers (logs, station, assets, etc.) on
        // the same layout as the DI provider instead of silently falling back
        // to the legacy per-user tree.  Normal launches have no such
        // arguments and therefore retain the v1.11 default unchanged.
        var arguments = Environment.GetCommandLineArgs();
        var machineInstallRoot = GetCommandLineArgument(arguments, "--machine-install-root");
        var machineDataRoot = GetCommandLineArgument(arguments, "--machine-data-root");
        var userDataRoot = GetCommandLineArgument(arguments, "--user-data-root");
        var hasMachineArgument = arguments.Any(argument =>
            string.Equals(argument, "--machine-install-root", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "--machine-data-root", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "--user-data-root", StringComparison.OrdinalIgnoreCase));
        if (hasMachineArgument)
        {
            if (string.IsNullOrWhiteSpace(machineInstallRoot) ||
                string.IsNullOrWhiteSpace(machineDataRoot))
            {
                throw new ArgumentException("Las raices machine-wide de instalacion y datos deben especificarse juntas.");
            }

            return CreateMachineWide(
                machineInstallRoot,
                machineDataRoot,
                userDataRoot ?? GetDefaultUserDataRoot());
        }

        return CreateLegacyDefault();
    }

    /// <summary>
    /// Resolves the v1.11 per-user layout used by the current application.
    /// </summary>
    public static ArteInventariadoPathProvider CreateLegacyDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName);
        return CreateLegacy(root);
    }

    public static ArteInventariadoPathProvider CreateLegacy(string applicationRoot)
    {
        var root = NormalizeRoot(applicationRoot, nameof(applicationRoot));
        return new ArteInventariadoPathProvider(
            ArteInventariadoPathLayout.LegacyPerUser,
            root,
            root,
            root);
    }

    /// <summary>
    /// Resolves the future machine-wide layout without creating directories
    /// or changing the current installation.
    /// </summary>
    public static ArteInventariadoPathProvider CreateMachineWideDefault()
    {
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ApplicationDirectoryName);
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ApplicationDirectoryName);
        var userRoot = GetDefaultUserDataRoot();
        return CreateMachineWide(installRoot, dataRoot, userRoot);
    }

    public static ArteInventariadoPathProvider CreateMachineWide(
        string machineInstallRoot,
        string machineDataRoot,
        string userDataRoot)
    {
        return new ArteInventariadoPathProvider(
            ArteInventariadoPathLayout.MachineWide,
            machineInstallRoot,
            machineDataRoot,
            userDataRoot);
    }

    public string GetVersionDirectory(string version)
    {
        var safeVersion = Updater.UpdateVersion.Sanitize(version);
        return CombineSafeSegment(VersionsRoot, safeVersion, "version");
    }

    public string GetRecoveryPath(string fileName)
    {
        return CombineSafeSegment(RecoveryRoot, fileName, "recovery file");
    }

    private static string CombineSafeSegment(string root, string segment, string description)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException($"El {description} no puede estar vacio.", nameof(segment));
        }

        if (segment is "." or ".." ||
            segment.Contains(Path.DirectorySeparatorChar) ||
            segment.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(segment))
        {
            throw new InvalidDataException($"El {description} no es un segmento seguro.");
        }

        var path = Path.GetFullPath(Path.Combine(root, segment));
        var normalizedRoot = NormalizeRoot(root, nameof(root));
        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"El {description} queda fuera de su raiz.");
        }

        return path;
    }

    private static string NormalizeRoot(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("La raiz de rutas no puede estar vacia.", parameterName);
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()));
        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetDefaultUserDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationDirectoryName);

    private static string? GetCommandLineArgument(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = arguments[index + 1];
            return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
        }

        return null;
    }
}

public enum ArteInventariadoPathLayout
{
    LegacyPerUser = 0,
    MachineWide = 1
}
