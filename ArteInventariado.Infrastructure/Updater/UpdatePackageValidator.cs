using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Updater;

public sealed class UpdatePackageValidator
{
    private const long MaximumManifestBytes = 128 * 1024;
    private const long MaximumPackageBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumExpandedPackageBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumEntryCount = 20_000;
    private const int MaximumCompressionRatio = 250;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UpdatePackageValidationResult> ValidateAsync(
        string manifestPath,
        string packagePath,
        string installedVersion,
        CancellationToken cancellationToken = default,
        string? expectedDeploymentTarget = null)
    {
        var errors = new List<string>();
        UpdateManifest? manifest = null;
        long? expandedSize = null;
        manifestPath = Path.GetFullPath(manifestPath);
        packagePath = Path.GetFullPath(packagePath);
        try
        {
            if (!File.Exists(manifestPath)) errors.Add("El manifest de actualizacion no existe.");
            else if (new FileInfo(manifestPath).Length is <= 0 or > MaximumManifestBytes) errors.Add("El manifest tiene un tamano invalido.");
            if (!File.Exists(packagePath)) errors.Add("El paquete de actualizacion no existe.");
            else if (new FileInfo(packagePath).Length is <= 0 or > MaximumPackageBytes) errors.Add("El paquete tiene un tamano invalido.");
            if (errors.Count > 0) return new(false, null, packagePath, errors, expandedSize);

            await using var manifestStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(manifestStream, JsonOptions, cancellationToken);
            if (manifest is null) errors.Add("El manifest esta vacio o mal formado.");
        }
        catch (JsonException) { errors.Add("El manifest esta mal formado."); }
        catch (IOException ex) { errors.Add($"No se pudo leer el manifest: {ex.Message}"); }

        if (manifest is null) return new(false, null, packagePath, errors, expandedSize);
        if (!string.IsNullOrWhiteSpace(expectedDeploymentTarget) &&
            !string.Equals(manifest.DeploymentTarget, expectedDeploymentTarget, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"El paquete no está destinado a '{expectedDeploymentTarget}'. El manifest debe declarar DeploymentTarget correctamente.");
        }
        if (!UpdateVersion.TryParse(manifest.Version, out var target)) errors.Add("La version destino no es valida.");
        if (!UpdateVersion.TryParse(manifest.MinimumCompatibleVersion, out var minimum)) errors.Add("La version minima compatible no es valida.");
        UpdateVersion? maximum = null;
        if (!string.IsNullOrWhiteSpace(manifest.MaximumCompatibleVersion))
        {
            if (!UpdateVersion.TryParse(manifest.MaximumCompatibleVersion, out var parsedMaximum)) errors.Add("La version maxima compatible no es valida.");
            else maximum = parsedMaximum;
        }
        if (string.IsNullOrWhiteSpace(manifest.PackagePath))
        {
            errors.Add("El manifest no declara el paquete.");
        }
        else if (Path.IsPathRooted(manifest.PackagePath) ||
                 !string.Equals(Path.GetFileName(manifest.PackagePath), manifest.PackagePath, StringComparison.Ordinal) ||
                 !string.Equals(Path.GetFileName(packagePath), manifest.PackagePath, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("El paquete seleccionado no coincide con PackagePath del manifest.");
        }
        if (manifest.SizeBytes <= 0) errors.Add("El tamano declarado no es valido.");
        if (string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) errors.Add("El SHA-256 declarado no es valido.");

        if (!UpdateVersion.TryParse(installedVersion, out var installed)) errors.Add("La version instalada no es valida.");
        else
        {
            if (target.CompareTo(installed) <= 0) errors.Add("La version destino no es posterior a la instalada.");
            if (installed.CompareTo(minimum) < 0) errors.Add("La instalacion actual no cumple la version minima compatible.");
            if (maximum.HasValue && installed.CompareTo(maximum.Value) > 0) errors.Add("La instalacion actual supera la version maxima compatible.");
        }

        var actualLength = new FileInfo(packagePath).Length;
        if (manifest.SizeBytes != actualLength) errors.Add("El tamano del paquete no coincide con el manifest.");
        if (errors.Count == 0)
        {
            var hash = await ComputeSha256Async(packagePath, cancellationToken);
            if (!FixedEquals(hash, manifest.Sha256)) errors.Add("El SHA-256 del paquete no coincide con el manifest.");
            try
            {
                using var archive = ZipFile.OpenRead(packagePath);
                if (archive.Entries.Count > MaximumEntryCount)
                {
                    errors.Add("El paquete contiene demasiados archivos.");
                }

                var duplicate = archive.Entries.GroupBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
                if (duplicate is not null) errors.Add($"El paquete contiene una entrada duplicada: {duplicate.Key}.");
                if (!archive.Entries.Any(entry => entry.FullName.Equals("ArteInventariado.WinForms.exe", StringComparison.OrdinalIgnoreCase))) errors.Add("El paquete no contiene ArteInventariado.WinForms.exe.");
                long totalExpanded = 0;
                foreach (var entry in archive.Entries)
                {
                    if (IsUnsafeEntry(entry.FullName))
                    {
                        errors.Add("El paquete contiene una ruta de extraccion insegura.");
                        break;
                    }

                    totalExpanded = checked(totalExpanded + entry.Length);
                    if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaximumCompressionRatio)
                    {
                        errors.Add($"El archivo '{entry.FullName}' supera la relacion de compresion permitida.");
                        break;
                    }
                }

                expandedSize = totalExpanded;
                if (totalExpanded > MaximumExpandedPackageBytes)
                {
                    errors.Add("El contenido expandido del paquete supera el limite permitido.");
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException)
            {
                errors.Add("El ZIP del paquete esta corrupto o declara tamanos no permitidos.");
            }
        }

        return new(errors.Count == 0, manifest, packagePath, errors, expandedSize);
    }

    public static bool IsUnsafeEntry(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName)) return true;
        var normalized = entryName.Replace('\\', '/');
        return normalized.Split('/').Any(part => part is ".." or ".") || normalized.StartsWith("/", StringComparison.Ordinal);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool FixedEquals(string actual, string expected)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
