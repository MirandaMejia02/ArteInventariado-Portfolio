using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Policy stored below the protected machine-wide Station directory.  The
/// scheduled SYSTEM runner reads this file instead of trusting the mutable
/// operational station preferences.
/// </summary>
public sealed record MachineWideBackupPolicy(
    int SchemaVersion,
    bool Enabled,
    string DestinationDirectory,
    int DailyGenerations,
    int WeeklyGenerations,
    int MonthlyGenerations,
    string TaskName,
    string TaskIdentitySid);

public enum MachineWideBackupStatusKind
{
    Unknown = 0,
    SuccessHealthy = 1,
    SuccessDegradedAssets = 2,
    Failure = 3
}

/// <summary>Small, non-sensitive status summary exposed to the UI if needed.</summary>
public sealed record MachineWideBackupStatus(
    int SchemaVersion,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessfulBackupUtc,
    string? LastSuccessfulFileName,
    long? LastSuccessfulSizeBytes,
    string? LastSuccessfulSha256,
    string? LastError,
    int RetainedDaily,
    int RetainedWeekly,
    int RetainedMonthly,
    MachineWideBackupStatusKind LastOutcome = MachineWideBackupStatusKind.Unknown,
    int LastOmittedAssetCount = 0,
    Guid? LastOperationId = null,
    ArteInventariado.Application.Models.BackupKind? LastBackupKind = null);

public sealed class MachineWideBackupPolicyStore
{
    public const int CurrentSchemaVersion = 1;
    public const string LocalSystemSid = "S-1-5-18";
    public const string DefaultTaskName = "\\ArteInventariado\\BackupProtegido";
    public const int DefaultDailyGenerations = 7;
    public const int DefaultWeeklyGenerations = 4;
    public const int DefaultMonthlyGenerations = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public MachineWideBackupPolicy EnsureInitialized(ArteInventariadoPathProvider paths)
    {
        EnsureMachineWide(paths);
        if (File.Exists(paths.ProtectedBackupPolicyPath))
        {
            return Load(paths);
        }

        var policy = CreateDefault(paths);
        WriteAtomically(paths, paths.ProtectedBackupPolicyPath, policy);
        return policy;
    }

    public MachineWideBackupPolicy Load(ArteInventariadoPathProvider paths)
    {
        EnsureMachineWide(paths);
        var path = paths.ProtectedBackupPolicyPath;
        if (!File.Exists(path))
        {
            throw new InvalidDataException("La política protegida de respaldos no existe.");
        }

        try
        {
            var document = JsonSerializer.Deserialize<MachineWideBackupPolicy>(
                File.ReadAllText(path),
                SerializerOptions);
            if (document is null)
            {
                throw new InvalidDataException("La política protegida de respaldos está vacía.");
            }

            Validate(paths, document);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("La política protegida de respaldos no contiene JSON válido.", ex);
        }
    }

    public MachineWideBackupStatus LoadStatus(ArteInventariadoPathProvider paths)
    {
        EnsureMachineWide(paths);
        var path = paths.ProtectedBackupStatusPath;
        // Status is informational, but it still lives below a protected
        // machine-wide root.  Refuse a redirected/reparse path before reading
        // it so an untrusted filesystem substitution cannot become input to
        // the UI or retention diagnostics.
        EnsureNoReparsePoints(paths, path);
        if (!File.Exists(path))
        {
            return EmptyStatus;
        }

        try
        {
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > 64 * 1024)
            {
                throw new InvalidDataException("El estado protegido de respaldos tiene un tamaño inválido.");
            }

            var document = JsonSerializer.Deserialize<MachineWideBackupStatus>(
                File.ReadAllText(path),
                SerializerOptions);
            if (document is null ||
                document.SchemaVersion != CurrentSchemaVersion ||
                !Enum.IsDefined(document.LastOutcome) ||
                document.LastOmittedAssetCount < 0)
            {
                throw new InvalidDataException("El estado protegido de respaldos no es compatible.");
            }

            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("El estado protegido de respaldos no contiene JSON válido.", ex);
        }
    }

    public void SaveStatus(ArteInventariadoPathProvider paths, MachineWideBackupStatus status)
    {
        EnsureMachineWide(paths);
        ArgumentNullException.ThrowIfNull(status);
        if (status.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("El estado protegido de respaldos usa una versión inválida.");
        }

        WriteAtomically(paths, paths.ProtectedBackupStatusPath, status);
    }

    public static MachineWideBackupPolicy CreateDefault(ArteInventariadoPathProvider paths)
    {
        EnsureMachineWide(paths);
        return new MachineWideBackupPolicy(
            CurrentSchemaVersion,
            Enabled: true,
            Path.GetFullPath(paths.ProtectedBackupsRoot),
            DefaultDailyGenerations,
            DefaultWeeklyGenerations,
            DefaultMonthlyGenerations,
            DefaultTaskName,
            LocalSystemSid);
    }

    public static MachineWideBackupStatus EmptyStatus => new(
        CurrentSchemaVersion,
        null,
        null,
        null,
        null,
        null,
        null,
        0,
        0,
        0,
        MachineWideBackupStatusKind.Unknown,
        0);

    public static void Validate(ArteInventariadoPathProvider paths, MachineWideBackupPolicy policy)
    {
        EnsureMachineWide(paths);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.SchemaVersion != CurrentSchemaVersion ||
            policy.DailyGenerations is < 1 or > 365 ||
            policy.WeeklyGenerations is < 1 or > 365 ||
            policy.MonthlyGenerations is < 1 or > 365)
        {
            throw new InvalidDataException("La política protegida de respaldos contiene valores inválidos.");
        }

        var expectedDestination = Path.GetFullPath(paths.ProtectedBackupsRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var actualDestination = Path.GetFullPath(policy.DestinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!PathEquals(actualDestination, expectedDestination))
        {
            throw new InvalidDataException("El destino protegido de respaldos no puede cambiarse a una ruta arbitraria.");
        }

        if (!string.Equals(policy.TaskName, DefaultTaskName, StringComparison.Ordinal) ||
            !string.Equals(policy.TaskIdentitySid, LocalSystemSid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La identidad de la tarea protegida de respaldos no es válida.");
        }

        EnsureNoReparsePoints(paths, paths.ProtectedBackupPolicyPath);
        EnsureNoReparsePoints(paths, paths.ProtectedBackupsRoot);
    }

    private static void WriteAtomically(
        ArteInventariadoPathProvider paths,
        string targetPath,
        object document)
    {
        EnsureMachineWide(paths);
        EnsureNoReparsePoints(paths, targetPath);
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new InvalidOperationException("La ruta protegida no tiene directorio.");
        Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(paths, directory);

        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, document, document.GetType(), SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullTarget))
            {
                File.Replace(temporary, fullTarget, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, fullTarget);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void EnsureMachineWide(ArteInventariadoPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsMachineWide)
        {
            throw new InvalidOperationException("La política protegida requiere el layout machine-wide.");
        }
    }

    private static void EnsureNoReparsePoints(ArteInventariadoPathProvider paths, string path)
    {
        var full = Path.GetFullPath(path);
        if (!IsWithin(full, paths.StationRoot) && !IsWithin(full, paths.ProtectedBackupsRoot))
        {
            throw new InvalidDataException("La ruta de la política protegida queda fuera de sus raíces autorizadas.");
        }

        var current = full;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException($"La ruta protegida contiene un reparse point: '{current}'.");
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InvalidDataException($"No se pudo inspeccionar la ruta protegida: '{current}'.", ex);
                }
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || PathEquals(parent, current))
            {
                break;
            }

            current = parent;
        }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PathEquals(fullCandidate, fullRoot) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
