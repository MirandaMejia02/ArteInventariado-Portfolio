using System.Text.Json;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Station;

public sealed class StationEventLog
{
    private static readonly object Sync = new();
    private readonly string _path;
    private readonly TimeProvider _timeProvider;

    public StationEventLog()
        : this(ArteInventariadoPathProvider.CreateDefault(), TimeProvider.System)
    {
    }

    public StationEventLog(
        ArteInventariadoPathProvider paths,
        TimeProvider? timeProvider = null)
        : this(StationConfigurationPathProvider.GetAuditLogPath(paths), timeProvider)
    {
    }

    internal StationEventLog(string path, TimeProvider? timeProvider = null)
    {
        _path = System.IO.Path.GetFullPath(path);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Path => _path;

    public void Write(string eventName, string? userName, InstallationMode? mode, string description)
    {
        var entry = new
        {
            timestampUtc = _timeProvider.GetUtcNow(),
            eventName,
            userName,
            installationMode = mode?.ToString(),
            description
        };
        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;

        lock (Sync)
        {
            var directory = System.IO.Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("La ruta del log local no tiene directorio.");
            Directory.CreateDirectory(directory);
            File.AppendAllText(_path, line);
        }
    }
}
