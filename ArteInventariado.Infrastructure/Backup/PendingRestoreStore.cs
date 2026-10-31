using System.Text.Json;

namespace ArteInventariado.Infrastructure.Backup;

internal sealed record PendingRestoreDocument(
    int SchemaVersion,
    Guid RestoreId,
    DateTimeOffset RequestedUtc,
    string StagedDatabasePath,
    string StagedDatabaseSha256,
    string PreventiveBackupPath,
    Guid SourceBackupId,
    IReadOnlyList<string>? InstalledAssetPaths = null);

internal sealed class PendingRestoreStore
{
    internal const int CurrentSchemaVersion = 1;
    private const long MaximumBytes = 64 * 1024;
    private readonly string _path;

    public PendingRestoreStore(string path)
    {
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public PendingRestoreDocument? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var file = new FileInfo(_path);
        if (file.Length is <= 0 or > MaximumBytes)
        {
            throw new InvalidDataException("La instruccion de restauracion pendiente tiene un tamano invalido.");
        }

        var document = JsonSerializer.Deserialize<PendingRestoreDocument>(File.ReadAllText(_path), BackupPackageEngine.JsonOptions)
            ?? throw new InvalidDataException("La instruccion de restauracion pendiente esta corrupta.");
        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.RestoreId == Guid.Empty ||
            document.SourceBackupId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.StagedDatabasePath) ||
            string.IsNullOrWhiteSpace(document.StagedDatabaseSha256) ||
            string.IsNullOrWhiteSpace(document.PreventiveBackupPath))
        {
            throw new InvalidDataException("La instruccion de restauracion pendiente no es valida.");
        }

        return document;
    }

    public void Save(PendingRestoreDocument document)
    {
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, BackupPackageEngine.JsonOptions);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                File.Replace(temporary, _path, null);
            }
            else
            {
                File.Move(temporary, _path);
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

    public void Complete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    public void ArchiveFailed()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        var failed = $"{_path}.failed-{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}";
        File.Move(_path, failed);
    }
}
