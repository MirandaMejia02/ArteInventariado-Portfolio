using System.Text.Json;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateEventLog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly object _sync = new();

    public UpdateEventLog(string logDirectory)
    {
        _path = Path.Combine(Path.GetFullPath(logDirectory), "update-events.jsonl");
    }

    public void Write(string eventName, string description, string? version = null)
    {
        var entry = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            eventName,
            version,
            description
        };

        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
        }
    }
}
