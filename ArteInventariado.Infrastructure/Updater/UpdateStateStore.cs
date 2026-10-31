using System.Text.Json;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdateStateStore
{
    public const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;

    public UpdateStateStore(string? path = null) =>
        _path = System.IO.Path.GetFullPath(path ?? UpdateRuntimePaths.CreateDefault().StatePath);
    public string Path => _path;

    public UpdateStateDocument? Load()
    {
        if (!File.Exists(_path)) return null;
        var file = new FileInfo(_path);
        if (file.Length is <= 0 or > 128 * 1024) throw new InvalidDataException("El estado del actualizador tiene un tamano invalido.");
        var document = JsonSerializer.Deserialize<UpdateStateDocument>(File.ReadAllText(_path), Options)
            ?? throw new InvalidDataException("El estado del actualizador esta corrupto.");
        if (document.SchemaVersion != CurrentSchemaVersion ||
            !Enum.IsDefined(document.State) ||
            !UpdateVersion.TryParse(document.SourceVersion, out _) ||
            !UpdateVersion.TryParse(document.TargetVersion, out _))
        {
            throw new InvalidDataException("El estado del actualizador no es compatible.");
        }

        if (document.State is UpdateState.ReadyToInstall or
            UpdateState.Installing or
            UpdateState.Verifying or
            UpdateState.RollbackRequired or
            UpdateState.RollingBack)
        {
            if (string.IsNullOrWhiteSpace(document.PackagePath) ||
                string.IsNullOrWhiteSpace(document.ManifestPath) ||
                string.IsNullOrWhiteSpace(document.PreUpdateBackupPath) ||
                string.IsNullOrWhiteSpace(document.SourceApplicationPath) ||
                !document.InstallationId.HasValue ||
                !document.StationMode.HasValue ||
                string.IsNullOrWhiteSpace(document.StationSettingsSha256) ||
                string.IsNullOrWhiteSpace(document.StationSettingsBackupPath))
            {
                throw new InvalidDataException("El estado del actualizador no contiene los datos necesarios para rollback.");
            }
        }

        return document;
    }

    public void Save(UpdateStateDocument document)
    {
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options));
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Clear() { if (File.Exists(_path)) File.Delete(_path); }
}
