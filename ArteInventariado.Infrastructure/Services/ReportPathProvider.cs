using ArteInventariado.Application.Interfaces;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class ReportPathProvider : IReportPathProvider
{
    private readonly IStationSettingsService? _stationSettings;
    private readonly AppDbContext? _legacyContext;

    [ActivatorUtilitiesConstructor]
    public ReportPathProvider(IStationSettingsService stationSettings)
    {
        _stationSettings = stationSettings;
    }

    internal ReportPathProvider(AppDbContext context) => _legacyContext = context;

    public async Task<string> GetRootDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (_stationSettings is not null)
        {
            var settings = await _stationSettings.GetReportSettingsAsync(cancellationToken);
            Directory.CreateDirectory(settings.ReportsDirectory);
            return settings.ReportsDirectory;
        }

        await using var lease = await AppDbContextLease.CreateAsync(
            null, _legacyContext, cancellationToken);
        return await GetLegacyRootDirectoryAsync(lease.Context, cancellationToken);
    }

    private static async Task<string> GetLegacyRootDirectoryAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var configured = await context.ConfiguracionSistema
            .AsNoTracking()
            .Where(c => c.Clave == SystemConfigService.ReportsFolderKey)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ArteInventariado",
                "Reportes")
            : configured.Trim();

        Directory.CreateDirectory(directory);
        return directory;
    }

    public async Task<string> GetDirectoryAsync(
        string category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("La categoria de reporte es obligatoria.", nameof(category));

        var root = await GetRootDirectoryAsync(cancellationToken);
        var directory = Path.Combine(root, SanitizeSegment(category));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
