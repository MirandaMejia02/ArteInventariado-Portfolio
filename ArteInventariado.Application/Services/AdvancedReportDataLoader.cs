using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Services;

public static class AdvancedReportDataLoader
{
    public const int BatchSize = 1000;

    public static async Task<AdvancedReportResult> LoadAllAsync(
        IAdvancedReportQueryService query,
        AdvancedReportRequest request,
        IProgress<AdvancedReportLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        var first = await query.QueryAsync(
            request with { Page = 1, PageSize = BatchSize },
            cancellationToken);
        var rows = first.Rows.ToList();
        progress?.Report(new AdvancedReportLoadProgress(rows.Count, first.TotalRows));

        for (var page = 2; rows.Count < first.TotalRows; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = await query.QueryAsync(
                request with { Page = page, PageSize = BatchSize },
                cancellationToken);
            if (next.Rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"El reporte indicó {first.TotalRows:N0} filas, pero la página de datos {page:N0} llegó vacía.");
            }

            rows.AddRange(next.Rows);
            progress?.Report(new AdvancedReportLoadProgress(
                Math.Min(rows.Count, first.TotalRows),
                first.TotalRows));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return first with { Rows = rows, TotalRows = rows.Count };
    }
}
