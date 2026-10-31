using System.Text.Json;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Small, non-secret H2 journal under the protected Recovery root.  It is
/// intentionally independent from the H1 journal so startup can distinguish
/// an interrupted bootstrap from an interrupted restore.
/// </summary>
public sealed class DisasterRecoveryJournalStore
{
    public const int CurrentSchemaVersion = 1;
    private const string DirectoryName = "DisasterRecovery";
    private const string FileName = "journal.json";
    private const long MaximumBytes = 128 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _recoveryRoot;

    public DisasterRecoveryJournalStore(string recoveryRoot)
    {
        _recoveryRoot = Path.GetFullPath(recoveryRoot);
    }

    public string DirectoryPath => Path.Combine(_recoveryRoot, DirectoryName);

    public string JournalPath => Path.Combine(DirectoryPath, FileName);

    public static bool HasPending(string recoveryRoot)
    {
        var root = Path.GetFullPath(recoveryRoot);
        var path = Path.Combine(root, DirectoryName, FileName);
        try
        {
            EnsureNoReparse(root);
            EnsureNoReparse(path);
        }
        catch (InvalidDataException)
        {
            // A reparse in a recovery root is itself recovery evidence. The
            // caller must not proceed as if the machine were fresh.
            return true;
        }

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > MaximumBytes)
            {
                return true;
            }

            var document = JsonSerializer.Deserialize<DisasterRecoveryJournalDocument>(
                File.ReadAllText(path),
                Options);
            return document is null ||
                   document.SchemaVersion != CurrentSchemaVersion ||
                   document.Phase != DisasterRecoveryPhase.Completed &&
                   document.Phase != DisasterRecoveryPhase.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return true;
        }
    }

    public DisasterRecoveryJournalDocument? Load()
    {
        if (!File.Exists(JournalPath))
        {
            return null;
        }

        EnsureNoReparse(DirectoryPath);
        EnsureNoReparse(JournalPath);
        var file = new FileInfo(JournalPath);
        if (file.Length is <= 0 or > MaximumBytes)
        {
            throw new InvalidDataException("El journal H2 tiene un tamaño inválido.");
        }

        var document = JsonSerializer.Deserialize<DisasterRecoveryJournalDocument>(
            File.ReadAllText(JournalPath),
            Options)
            ?? throw new InvalidDataException("El journal H2 está vacío o corrupto.");
        Validate(document);
        return document;
    }

    public void Save(DisasterRecoveryJournalDocument document)
    {
        Validate(document);
        EnsureSafePath(_recoveryRoot);
        Directory.CreateDirectory(DirectoryPath);
        EnsureNoReparse(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, $".{FileName}.{Guid.NewGuid():N}.tmp");
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
                JsonSerializer.Serialize(stream, document, Options);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(JournalPath))
            {
                File.Replace(temporary, JournalPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, JournalPath);
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

    public void Clear()
    {
        EnsureSafePath(_recoveryRoot);
        EnsureNoReparse(_recoveryRoot);
        EnsureNoReparse(DirectoryPath);
        if (File.Exists(JournalPath))
        {
            File.Delete(JournalPath);
        }

        if (Directory.Exists(DirectoryPath) && !Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
        {
            Directory.Delete(DirectoryPath);
        }
    }

    private static void Validate(DisasterRecoveryJournalDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.OperationId == Guid.Empty ||
            document.SourceInstallationId == Guid.Empty ||
            document.DestinationInstallationId == Guid.Empty ||
            !Enum.IsDefined(document.DestinationMode) ||
            !Enum.IsDefined(document.Phase))
        {
            throw new InvalidDataException("El journal H2 no contiene una operación válida.");
        }

        foreach (var path in new[] { document.CandidatePath, document.OriginalStationBackupPath })
        {
            if (path is not null && !Path.IsPathFullyQualified(path))
            {
                throw new InvalidDataException("El journal H2 contiene una ruta no absoluta.");
            }
        }
    }

    private static void EnsureSafePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("La raíz del journal H2 no es local.");
        }
    }

    private static void EnsureNoReparse(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("El journal H2 contiene un reparse point no permitido.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }
}
