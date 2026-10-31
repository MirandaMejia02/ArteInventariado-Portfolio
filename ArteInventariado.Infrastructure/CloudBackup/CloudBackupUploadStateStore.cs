using System.Text.Json;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>Atomic metadata-only state for resumable cloud uploads.</summary>
public sealed class CloudBackupUploadStateStore
{
    public const int CurrentSchemaVersion = 1;
    private const long MaximumStateBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ArteInventariadoPathProvider _paths;

    public CloudBackupUploadStateStore(ArteInventariadoPathProvider paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public CloudBackupUploadStateDocument Load()
    {
        EnsureMachineWide();
        EnsureNoReparse(_paths.CloudBackupRoot);
        if (!File.Exists(_paths.CloudBackupUploadStatePath))
        {
            return new CloudBackupUploadStateDocument(CurrentSchemaVersion, []);
        }

        var file = new FileInfo(_paths.CloudBackupUploadStatePath);
        if (file.Length is <= 0 or > MaximumStateBytes)
        {
            throw new InvalidDataException("El estado de uploads cloud tiene un tamaño inválido.");
        }

        try
        {
            var document = JsonSerializer.Deserialize<CloudBackupUploadStateDocument>(
                File.ReadAllText(_paths.CloudBackupUploadStatePath),
                SerializerOptions)
                ?? throw new InvalidDataException("El estado de uploads cloud está vacío.");
            Validate(document);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("El estado de uploads cloud no contiene JSON válido.", ex);
        }
    }

    public void Save(CloudBackupUploadStateDocument document)
    {
        EnsureMachineWide();
        Validate(document);
        EnsureNoReparse(_paths.CloudBackupRoot);
        Directory.CreateDirectory(_paths.CloudBackupRoot);
        EnsureNoReparse(_paths.CloudBackupRoot);

        var temporary = Path.Combine(
            _paths.CloudBackupRoot,
            $".upload-state-{Guid.NewGuid():N}.tmp");
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
                JsonSerializer.Serialize(stream, document, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_paths.CloudBackupUploadStatePath))
            {
                File.Replace(temporary, _paths.CloudBackupUploadStatePath, null);
            }
            else
            {
                File.Move(temporary, _paths.CloudBackupUploadStatePath);
            }
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static void Validate(CloudBackupUploadStateDocument? document)
    {
        if (document is null || document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("El estado de uploads cloud no es compatible.");
        }

        var duplicate = document.Entries
            .GroupBy(entry => entry.BackupId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException("El estado de uploads cloud contiene BackupId duplicados.");
        }

        foreach (var entry in document.Entries)
        {
            if (entry.BackupId == Guid.Empty ||
                string.IsNullOrWhiteSpace(entry.Key) ||
                entry.PackageSizeBytes <= 0 ||
                !IsSha256(entry.PackageSha256) ||
                entry.Parts is null ||
                entry.Parts.GroupBy(part => part.PartNumber).Any(group => group.Count() > 1) ||
                entry.Parts.Any(part => part.PartNumber <= 0 || part.SizeBytes <= 0 || string.IsNullOrWhiteSpace(part.ETag) ||
                    (part.ChecksumSha256 is not null && !IsSha256(part.ChecksumSha256))) ||
                !Enum.IsDefined(entry.Status) ||
                !Enum.IsDefined(entry.ContentStatus))
            {
                throw new InvalidDataException("El estado de un upload cloud no es válido.");
            }
        }
    }

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private void EnsureMachineWide()
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException("El estado cloud requiere el layout machine-wide.");
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
                throw new InvalidDataException($"La ruta cloud contiene un reparse point: '{current}'.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
