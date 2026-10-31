using System.Text.Json;
using Amazon;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.CloudBackup;

/// <summary>
/// Reads and writes the protected, non-secret cloud uploader configuration.
/// The store never accepts a bucket or prefix from a command-line argument.
/// </summary>
public sealed class CloudBackupConfigurationStore
{
    public const int CurrentSchemaVersion = 1;
    private const long MaximumConfigurationBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ArteInventariadoPathProvider _paths;

    public CloudBackupConfigurationStore(ArteInventariadoPathProvider paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public CloudBackupConfiguration? LoadOptional()
    {
        EnsureMachineWide();
        EnsureNoReparse(_paths.CloudBackupRoot);
        if (!File.Exists(_paths.CloudBackupConfigurationPath))
        {
            return null;
        }

        var file = new FileInfo(_paths.CloudBackupConfigurationPath);
        if (file.Length is <= 0 or > MaximumConfigurationBytes)
        {
            throw new InvalidDataException("La configuración cloud protegida tiene un tamaño inválido.");
        }

        try
        {
            var configuration = JsonSerializer.Deserialize<CloudBackupConfiguration>(
                File.ReadAllText(_paths.CloudBackupConfigurationPath),
                SerializerOptions);
            Validate(configuration);
            return configuration;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("La configuración cloud protegida no contiene JSON válido.", ex);
        }
    }

    public void Save(CloudBackupConfiguration configuration)
    {
        EnsureMachineWide();
        Validate(configuration);
        EnsureNoReparse(_paths.CloudBackupRoot);
        Directory.CreateDirectory(_paths.CloudBackupRoot);
        EnsureNoReparse(_paths.CloudBackupRoot);

        var temporary = Path.Combine(
            _paths.CloudBackupRoot,
            $".config-{Guid.NewGuid():N}.tmp");
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
                JsonSerializer.Serialize(stream, configuration, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_paths.CloudBackupConfigurationPath))
            {
                File.Replace(temporary, _paths.CloudBackupConfigurationPath, null);
            }
            else
            {
                File.Move(temporary, _paths.CloudBackupConfigurationPath);
            }
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static void Validate(CloudBackupConfiguration? configuration)
    {
        if (configuration is null || configuration.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("La configuración cloud protegida no es compatible.");
        }

        if (!configuration.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.BucketName) ||
            configuration.BucketName.Length is < 3 or > 63 ||
            configuration.BucketName.StartsWith(".", StringComparison.Ordinal) ||
            configuration.BucketName.EndsWith(".", StringComparison.Ordinal) ||
            configuration.BucketName.StartsWith("-", StringComparison.Ordinal) ||
            configuration.BucketName.EndsWith("-", StringComparison.Ordinal) ||
            configuration.BucketName.Contains("..", StringComparison.Ordinal) ||
            configuration.BucketName.Any(character =>
                !(character is >= 'a' and <= 'z') &&
                !(character is >= '0' and <= '9') &&
                character != '.' &&
                character != '-'))
        {
            throw new InvalidDataException("El bucket cloud no tiene un nombre S3 válido.");
        }

        if (System.Net.IPAddress.TryParse(configuration.BucketName, out _))
        {
            throw new InvalidDataException("El bucket cloud no puede tener formato de dirección IP.");
        }

        if (string.IsNullOrWhiteSpace(configuration.RegionSystemName) ||
            configuration.RegionSystemName.Length > 64 ||
            configuration.RegionSystemName.Any(char.IsWhiteSpace) ||
            !RegionEndpoint.EnumerableAllRegions.Any(region =>
                string.Equals(region.SystemName, configuration.RegionSystemName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("La región cloud no es válida.");
        }

        ValidatePrefix(configuration.KeyPrefix);
        if (configuration.MultipartThresholdBytes < 5L * 1024 * 1024 ||
            configuration.MultipartThresholdBytes > 5L * 1024 * 1024 * 1024 ||
            configuration.MultipartPartSizeBytes < 5L * 1024 * 1024 ||
            configuration.MultipartPartSizeBytes > 5L * 1024 * 1024 * 1024 ||
            configuration.MultipartPartSizeBytes > configuration.MultipartThresholdBytes)
        {
            throw new InvalidDataException("Los límites multipart de cloud no son válidos.");
        }
    }

    public static void ValidatePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) ||
            prefix.StartsWith("/", StringComparison.Ordinal) ||
            prefix.EndsWith("/", StringComparison.Ordinal) ||
            prefix.Contains("//", StringComparison.Ordinal) ||
            prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment =>
                segment is "." or ".." ||
                segment.Any(character =>
                    char.IsWhiteSpace(character) ||
                    character is '\\' or ':' or '\0')))
        {
            throw new InvalidDataException("El prefijo cloud no es un segmento seguro.");
        }
    }

    private void EnsureMachineWide()
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException("La configuración cloud requiere el layout machine-wide.");
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
                throw new InvalidDataException($"La ruta cloud protegida contiene un reparse point: '{current}'.");
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
