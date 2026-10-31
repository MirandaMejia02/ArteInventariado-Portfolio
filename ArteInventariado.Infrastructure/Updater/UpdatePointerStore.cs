using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ArteInventariado.Infrastructure.Updater;

public sealed record UpdateVersionPointer(
    int SchemaVersion,
    string Version,
    string ExecutablePath,
    string? PreviousExecutablePath,
    DateTimeOffset UpdatedUtc);

public sealed class UpdatePointerStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;

    public UpdatePointerStore(string path) => _path = Path.GetFullPath(path);

    public UpdateVersionPointer? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var file = new FileInfo(_path);
        if (file.Length is <= 0 or > 32 * 1024)
        {
            throw new InvalidDataException("El puntero de version tiene un tamano invalido.");
        }

        UpdateVersionPointer pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<UpdateVersionPointer>(File.ReadAllText(_path), Options)
                ?? throw new InvalidDataException("El puntero de version esta corrupto.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("El puntero de version esta corrupto.", ex);
        }
        if (pointer.SchemaVersion != CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(pointer.Version) ||
            string.IsNullOrWhiteSpace(pointer.ExecutablePath))
        {
            throw new InvalidDataException("El puntero de version no es compatible.");
        }

        return pointer with
        {
            ExecutablePath = Path.GetFullPath(pointer.ExecutablePath),
            PreviousExecutablePath = string.IsNullOrWhiteSpace(pointer.PreviousExecutablePath)
                ? null
                : Path.GetFullPath(pointer.PreviousExecutablePath)
        };
    }

    public void Save(string version, string executablePath, string? previousExecutablePath, DateTimeOffset updatedUtc)
    {
        var pointer = new UpdateVersionPointer(
            CurrentSchemaVersion,
            version,
            Path.GetFullPath(executablePath),
            string.IsNullOrWhiteSpace(previousExecutablePath) ? null : Path.GetFullPath(previousExecutablePath),
            updatedUtc);
        WriteAtomically(JsonSerializer.Serialize(pointer, Options));
    }

    public void Restore(string? executablePath, DateTimeOffset updatedUtc)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Clear();
            return;
        }

        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("No existe el ejecutable anterior para restaurar el puntero.", fullPath);
        }

        var existing = Load();
        var version = existing is not null &&
                      string.Equals(existing.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase)
            ? existing.Version
            : FileVersionInfo.GetVersionInfo(fullPath).ProductVersion ?? "previous";
        Save(version, fullPath, null, updatedUtc);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private void WriteAtomically(string content)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("La ruta del puntero no tiene directorio.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // current.json is the installation commit point. Flush the
            // complete UTF-8 document to disk before replacing the previous
            // pointer so readers never observe a partially written file.
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                       4096,
                       leaveOpen: true))
            {
                writer.Write(content);
                writer.Flush();
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
}
