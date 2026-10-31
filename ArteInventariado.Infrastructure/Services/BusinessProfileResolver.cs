using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

internal sealed record BusinessProfile(
    string Name,
    string? Address,
    string? Phone,
    string? Email)
{
    public IReadOnlyList<string> ToDocumentHeaderLines()
    {
        var lines = new List<string> { Name };
        AddIfPresent(lines, "Direccion negocio: ", Address);
        AddIfPresent(lines, "Telefono negocio: ", Phone);
        AddIfPresent(lines, "Correo negocio: ", Email);
        return lines;
    }

    private static void AddIfPresent(List<string> lines, string prefix, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(prefix + value);
        }
    }
}

internal static class BusinessProfileResolver
{
    private static readonly string[] Keys =
    [
        SystemConfigService.BusinessNameKey,
        SystemConfigService.BusinessAddressKey,
        SystemConfigService.BusinessPhoneKey,
        SystemConfigService.BusinessEmailKey
    ];

    public static async Task<BusinessProfile> ResolveAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var values = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => Keys.Contains(c.Clave))
            .ToDictionaryAsync(c => c.Clave, c => c.Valor, cancellationToken);

        return new BusinessProfile(
            GetValue(values, SystemConfigService.BusinessNameKey, SystemConfigService.DefaultBusinessName),
            GetOptionalValue(values, SystemConfigService.BusinessAddressKey),
            GetOptionalValue(values, SystemConfigService.BusinessPhoneKey),
            GetOptionalValue(values, SystemConfigService.BusinessEmailKey));
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : defaultValue;
    }

    private static string? GetOptionalValue(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }
}
