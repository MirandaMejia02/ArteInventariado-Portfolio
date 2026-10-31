using System.Text.Json;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Backup;

/// <summary>
/// Atomic journal for the protected restore operation.  The file lives below
/// UpdateState/Recovery and never contains passwords, cloud credentials or
/// arbitrary caller supplied destinations.
/// </summary>
internal sealed class ProtectedRestoreJournalStore
{
    internal const int CurrentSchemaVersion = 1;
    private const long MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new(BackupPackageEngine.JsonOptions)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private readonly string _recoveryRoot;

    public ProtectedRestoreJournalStore(string recoveryRoot)
    {
        _recoveryRoot = Path.GetFullPath(recoveryRoot);
    }

    public string RecoveryRoot => _recoveryRoot;

    public string CreateOperationDirectory(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("La operacion de restore no tiene identificador.", nameof(operationId));
        }

        EnsureSafeRoot(_recoveryRoot);
        var root = Path.Combine(_recoveryRoot, "Restore");
        EnsureSafeRoot(root);
        Directory.CreateDirectory(root);
        EnsureNoReparse(root);
        var operation = Path.Combine(root, operationId.ToString("N"));
        if (File.Exists(operation) || Directory.Exists(operation))
        {
            throw new IOException("Ya existe una operacion de restore con el mismo identificador.");
        }

        Directory.CreateDirectory(operation);
        EnsureNoReparse(operation);
        return operation;
    }

    public string GetJournalPath(string operationDirectory) =>
        ResolveWithin(operationDirectory, "journal.json");

    public void Save(string operationDirectory, ProtectedRestoreJournalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != CurrentSchemaVersion || document.OperationId == Guid.Empty)
        {
            throw new InvalidDataException("El journal de restore no es compatible.");
        }

        var path = GetJournalPath(operationDirectory);
        EnsureNoReparse(operationDirectory);
        WriteAtomically(path, JsonSerializer.Serialize(document, Options));
    }

    public ProtectedRestoreJournalDocument Load(string operationDirectory)
    {
        var path = GetJournalPath(operationDirectory);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No existe el journal de restore.", path);
        }

        var file = new FileInfo(path);
        if (file.Length is <= 0 or > MaximumBytes)
        {
            throw new InvalidDataException("El journal de restore tiene un tamano invalido.");
        }

        var document = JsonSerializer.Deserialize<ProtectedRestoreJournalDocument>(
            File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("El journal de restore esta corrupto.");
        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.OperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.CandidatePath) ||
            string.IsNullOrWhiteSpace(document.CandidateSha256) ||
            document.CandidateBackupId == Guid.Empty ||
            document.CandidateSourceInstallationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.PreRestorePath) ||
            string.IsNullOrWhiteSpace(document.StagedDatabasePath) ||
            string.IsNullOrWhiteSpace(document.StagedAssetsRoot) ||
            document.DestinationInstallationId == Guid.Empty ||
            !Enum.IsDefined(document.DestinationMode) ||
            string.IsNullOrWhiteSpace(document.StationCoreSha256))
        {
            throw new InvalidDataException("El journal de restore no contiene datos suficientes.");
        }

        return document;
    }

    public IEnumerable<string> EnumerateOperationDirectories()
    {
        var root = Path.Combine(_recoveryRoot, "Restore");
        if (!Directory.Exists(root))
        {
            return [];
        }

        EnsureNoReparse(root);
        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _))
            .ToArray();
    }

    public void DeleteOperationDirectory(string operationDirectory)
    {
        EnsureWithinRecovery(operationDirectory);
        if (Directory.Exists(operationDirectory))
        {
            Directory.Delete(operationDirectory, recursive: true);
        }
    }

    private string ResolveWithin(string root, string fileName)
    {
        EnsureWithinRecovery(root);
        if (fileName.Contains(Path.DirectorySeparatorChar) ||
            fileName.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(fileName))
        {
            throw new InvalidDataException("El nombre de journal no es seguro.");
        }

        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!IsWithin(path, root))
        {
            throw new InvalidDataException("La ruta de journal queda fuera de su operacion.");
        }

        return path;
    }

    private void EnsureWithinRecovery(string path)
    {
        if (!IsWithin(path, _recoveryRoot))
        {
            throw new InvalidDataException("La operacion de restore queda fuera de Recovery.");
        }

        EnsureNoReparse(path);
    }

    private static void EnsureSafeRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidDataException("La raiz de Recovery no es local.");
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("El journal no tiene directorio.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
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

    private static void EnsureNoReparse(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Recovery contiene un reparse point no permitido: '{current}'.");
                }
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
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
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
